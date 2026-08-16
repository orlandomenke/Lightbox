using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// The Bone tool's options: the mode, the rig's bones, the weight brush and
/// the binding actions. Every decision it shows belongs to
/// <c>MainViewModel.Armature</c>; the one piece of code here is the bone
/// list's selection wiring.
/// </summary>
/// <remarks>
/// <b>The list is wired by hand because a two-way selection binding deletes
/// the selection it displays.</b> <c>BoneRows</c> is rebuilt on every rig
/// change, and a bound list answers the rebuild by clearing its selection —
/// which a two-way binding then writes back as <c>SelectedBoneId = null</c>.
/// The visible symptom was the reported one: a bone never <em>stayed</em>
/// selected, so nothing that needs a selected bone (rename, delete, IK)
/// ever appeared. The rule the wiring implements: <b>a cleared list is the
/// panel refreshing; only a picked row is the artist speaking.</b>
/// </remarks>
public partial class BoneOptionsBar : UserControl
{
    private MainViewModel? _vm;
    private bool _syncing;

    public BoneOptionsBar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookViewModel();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void HookViewModel()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            SyncBoneList();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.BoneRows) or nameof(MainViewModel.SelectedBoneId))
            SyncBoneList();
    }

    /// <summary>
    /// Rebuild the list from the view model, then re-point its selection at
    /// the selected bone — one direction, one writer, no echo.
    /// </summary>
    private void SyncBoneList()
    {
        if (_vm is null || this.FindControl<ListBox>("BoneList") is not { } list) return;
        var rows = _vm.BoneRows;
        _syncing = true;
        try
        {
            list.ItemsSource = rows;
            list.SelectedItem = _vm.SelectedBoneId is { } id
                ? rows.FirstOrDefault(r => r.Id == id)
                : null;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Take the picked fix off the drawing.</summary>
    /// <remarks>
    /// A click handler rather than a command with a parameter, for the reason
    /// the bone list is wired by hand: the list is rebuilt whenever the drawing
    /// changes, and a two-way selection binding answers each rebuild by writing
    /// its own selection away.
    /// </remarks>
    private void OnRemoveCorrective(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (this.FindControl<ListBox>("CorrectiveList")?.SelectedItem is BoneRow row)
            _vm.RemoveCorrective(row.Id);
    }

    private void OnBoneListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _vm is null) return;
        // Only a row is a pick. A cleared selection is the control reacting
        // to its items changing, and echoing it into the view model is the
        // bug this handler exists to prevent.
        if ((sender as ListBox)?.SelectedItem is BoneRow row && _vm.SelectedBoneId != row.Id)
            _vm.SelectedBoneId = row.Id;
    }
}
