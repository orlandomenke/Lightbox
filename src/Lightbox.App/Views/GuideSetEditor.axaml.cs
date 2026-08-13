using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// The guide-set editor: name a set, refresh it from the open document,
/// delete it. The roadmap's "guide sets exist to be made".
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything else already existed when this was built</b> — the record
/// (<see cref="GuideSet"/>), the resolver (<see cref="GuideScopes"/>), the
/// sharing menu in the project window — and no UI could create a set, so the
/// whole chain was reachable only from tests (owner's report, 2026-08-13:
/// "guides we are not even able to create or assign"). This window and the two
/// View ▸ Guides menu items are the missing verbs; the logic stays on
/// <see cref="MainViewModel"/> where <c>GuideSetTests</c> can drive it without
/// a window.
/// </para>
/// <para>
/// Sets come <em>from documents</em> by design — the set is a named copy of
/// guides you already placed, the way a template is work you already did — so
/// this editor has no "add a guide" button: the canvas is the guide editor.
/// </para>
/// </remarks>
public partial class GuideSetEditor : Window
{
    private readonly MainViewModel _vm;

    private sealed record Row(GuideSet Set)
    {
        public override string ToString() =>
            $"{Set.Name}  —  {Set.Guides.Count} guide{(Set.Guides.Count == 1 ? "" : "s")}";
    }

    public GuideSetEditor(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Refresh(keepSelection: null);
    }

    private GuideSet? Selected => (Sets.SelectedItem as Row)?.Set;

    private void Refresh(GuideSet? keepSelection)
    {
        var rows = _vm.ProjectGuideSets.Select(s => new Row(s)).ToList();
        Sets.ItemsSource = rows;
        Sets.SelectedItem = rows.FirstOrDefault(r => r.Set.Id == keepSelection?.Id);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        SaveNewButton.IsEnabled = _vm.CanSaveGuideSet;
        OverwriteButton.IsEnabled = _vm.CanSaveGuideSet && Selected is not null;
        RenameButton.IsEnabled = Selected is not null;
        DeleteButton.IsEnabled = Selected is not null;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Selected is { } set) NameBox.Text = set.Name;
        UpdateButtons();
    }

    private void OnSaveNew(object? sender, RoutedEventArgs e) =>
        Refresh(_vm.SaveGuidesAsSet(NameBox.Text ?? ""));

    private void OnOverwrite(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } set) return;
        Refresh(_vm.SaveGuidesAsSet(NameBox.Text ?? set.Name, overwriteId: set.Id));
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } set) return;
        _vm.RenameGuideSet(set, NameBox.Text ?? "");
        Refresh(set);
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } set) return;
        _vm.DeleteGuideSet(set);
        Refresh(keepSelection: null);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
