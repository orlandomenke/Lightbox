using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// The project window — Q29's second surface, in its own window by Q41.
/// </summary>
/// <remarks>
/// <para>
/// Almost nothing here. The window owns the multi-selection, because that is
/// the one thing a <c>ListBox</c> holds and a view model cannot be handed, and
/// everything else is <see cref="ProjectWindowViewModel"/> — which is what lets
/// the bulk edits, the filters and the counts be tested with no window at all.
/// </para>
/// <para>
/// Opened as a dialog on the main window, the way Configure and Export are.
/// Modal because it edits the same project the docker is showing, and two
/// surfaces writing one manifest with neither knowing about the other is the
/// class of bug B61 was.
/// </para>
/// </remarks>
public partial class ProjectWindow : Window
{
    private readonly ProjectWindowViewModel _vm;

    public ProjectWindow(Project project, Action? changed = null)
    {
        _vm = new ProjectWindowViewModel(project, changed);
        DataContext = _vm;
        InitializeComponent();

        if (this.FindControl<ListBox>("StructureRows") is { } rows)
        {
            rows.SelectionChanged += OnStructureSelectionChanged;
        }
    }

    /// <summary>Parameterless for the designer and the XAML compiler only.</summary>
    public ProjectWindow()
    {
        _vm = new ProjectWindowViewModel(new Project(new ProjectManifest(), ""));
        DataContext = _vm;
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Mirror the list's selection onto the view model.
    /// </summary>
    /// <remarks>
    /// Rather than binding <c>SelectedItems</c>, which Avalonia exposes as a
    /// non-generic <c>IList</c> that a view model would then have to guard on
    /// every read. Copying it here keeps the typed collection typed, and keeps
    /// the bulk commands drivable by a test that never made a ListBox.
    /// </remarks>
    private void OnStructureSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list) return;
        _vm.SetSelection(list.SelectedItems?.OfType<BoardRow>() ?? []);
    }
}
