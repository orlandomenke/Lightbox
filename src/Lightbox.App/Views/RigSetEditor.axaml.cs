using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// The skeleton-set editor: name a set, refresh it from the open drawing,
/// delete it. Q181's library, and <see cref="GuideSetEditor"/>'s shape on
/// purpose.
/// </summary>
/// <remarks>
/// <para>
/// <b>The lesson the guide sets taught, applied before it could be repeated.</b>
/// Guide sets shipped as a record, a resolver and a sharing menu with nothing
/// that could create one, so the whole chain was reachable only from tests.
/// This window and the Skeleton menu are the verbs, added in the same commit
/// as the record rather than a release later.
/// </para>
/// <para>
/// Sets come <em>from drawings</em>, like guide sets and templates: the set is
/// a named copy of a skeleton you already built, so there is no build-a-bone
/// button here — the canvas and the bone tool are the skeleton editor.
/// </para>
/// </remarks>
public partial class RigSetEditor : Window
{
    private readonly MainViewModel _vm;

    private sealed record Row(RigSet Set)
    {
        public override string ToString()
        {
            var bones = Set.Armature.Bones.Count;
            var size = Set.Heads is > 0 ? $"{Set.Heads:0.##} heads" : "no head count";
            return $"{Set.Name}  —  {bones} bone{(bones == 1 ? "" : "s")}, {size}";
        }
    }

    public RigSetEditor(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Refresh(keepSelection: null);
    }

    private RigSet? Selected => (Sets.SelectedItem as Row)?.Set;

    private void Refresh(RigSet? keepSelection)
    {
        var rows = _vm.ProjectRigSets.Select(s => new Row(s)).ToList();
        Sets.ItemsSource = rows;
        Sets.SelectedItem = rows.FirstOrDefault(r => r.Set.Id == keepSelection?.Id);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        SaveNewButton.IsEnabled = _vm.CanSaveRigSet;
        OverwriteButton.IsEnabled = _vm.CanSaveRigSet && Selected is not null;
        RenameButton.IsEnabled = Selected is not null;
        DeleteButton.IsEnabled = Selected is not null;
        // Said before the save rather than after it: a set with no head count
        // is not broken, but it cannot do the one thing the library is for,
        // and finding that out later means saving it twice.
        NoHeightScaleNote.IsVisible = _vm.CanSaveRigSet && !_vm.DocumentHasHeightScale;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Selected is { } set) NameBox.Text = set.Name;
        UpdateButtons();
    }

    private void OnSaveNew(object? sender, RoutedEventArgs e) =>
        Refresh(_vm.SaveArmatureAsSet(NameBox.Text ?? ""));

    private void OnOverwrite(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } set) return;
        Refresh(_vm.SaveArmatureAsSet(NameBox.Text ?? set.Name, overwriteId: set.Id));
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } set) return;
        _vm.RenameRigSet(set, NameBox.Text ?? "");
        Refresh(set);
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } set) return;
        _vm.DeleteRigSet(set);
        Refresh(keepSelection: null);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
