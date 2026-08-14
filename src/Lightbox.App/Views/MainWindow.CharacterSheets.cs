using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using static Lightbox.App.Views.PlacementChoiceDialog;

namespace Lightbox.App.Views;

/// <summary>Part of the MainWindow code-behind — see MainWindow.axaml.cs.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c> under Q76, which was 5,706 lines across 37
/// sections with 79% of its fields touched by exactly one of them. Every field this
/// file uses is either declared here or in the shared block at the top of
/// <c>MainWindow.axaml.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainWindow
{
    // ---- character sheets -----------------------------------------------------

    /// <summary>
    /// Create a character sheet: name it first, then make sure the document it
    /// lives in has somewhere on disk to live.
    /// </summary>
    /// <remarks>
    /// <b>B66.</b> A sheet is part of its document (Q25 answered (a)), so an
    /// untitled document meant the work existed nowhere. The order is the fix:
    /// the name is asked for before anything is written — B65's rule on this
    /// surface — and the save is offered only once the sheet exists, so
    /// cancelling the save keeps the work rather than discarding it.
    /// </remarks>
    private async void OnAddReferenceSheet(object? sender, RoutedEventArgs e)
    {
        var suggested = $"Character {_vm.ReferenceSheetsView.Count + 1}";
        var name = await PromptForText("New character sheet", "Name", suggested);
        if (name is null) return;   // cancelled: nothing is created

        var needsAFile = _vm.AReferenceSheetWouldBeUnsaved;
        // Null only with nothing open, where the docker holding this button is
        // not on screen to be pressed.
        if (_vm.AddReferenceSheet(name) is not { } sheet) return;
        // B78: the picker opens already named, rather than asking a second time.
        if (needsAFile) await SaveDocumentAsAsync(sheet.Name);
    }

    private void OnAddReferenceView(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Lightbox.Core.Documents.ReferenceSheet sheet)
            _vm.AddReferenceView(sheet);
    }

    private void OnOpenReferenceView(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Lightbox.Core.Documents.ReferenceView view)
            _vm.OpenReferenceView(view);
    }

    /// <summary>
    /// The reference board, if it is open. One per window rather than one per
    /// view (Q87): the board is a wall, and a second wall showing the same
    /// references is the thing it exists to replace.
    /// </summary>
    private ReferenceBoardWindow? _referenceBoard;

    private void OnOpenReferenceBoard(object? sender, RoutedEventArgs e)
    {
        if (_referenceBoard is { } open)
        {
            open.Activate();
            return;
        }

        var window = new ReferenceBoardWindow(_vm, _shortcuts);
        _referenceBoard = window;
        window.Closed += (_, _) => _referenceBoard = null;
        // A child of the main window: it floats beside the art, and closing
        // the application does not leave a reference orphaned on the desktop.
        window.Show(this);
    }

    private void OnToggleViewOnCanvas(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Lightbox.Core.Documents.ReferenceView view)
            _vm.ToggleViewOnCanvas(view);
    }

    /// <summary>
    /// The text in a sheet or view name box, captured when it took focus, so
    /// losing focus can tell a rename from a click-through.
    /// </summary>
    private string? _referenceNameOnFocus;

    private void OnReferenceNameFocused(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox box) _referenceNameOnFocus = box.Text ?? "";
    }

    /// <remarks>
    /// <b>B95.</b> Bound to <c>LostFocus</c>, which fires whether or not the
    /// artist typed anything — so this used to mark the document unsaved for
    /// clicking into a name box and out again. Compare against what was there
    /// on the way in, exactly as the project docker's rename does.
    /// </remarks>
    private void OnReferenceRenamed(object? sender, RoutedEventArgs e)
    {
        var before = _referenceNameOnFocus;
        _referenceNameOnFocus = null;
        if (sender is not TextBox box) return;
        // The two-way binding has already written the new name by now, so the
        // only record of the old one is what was captured on the way in.
        if (before is null || string.Equals(before, box.Text ?? "", StringComparison.Ordinal))
        {
            _vm.RefreshReferenceList();
            return;
        }
        // The DataContext says what was renamed — a sheet or a view — which the
        // view model needs now that a sheet can belong to the project rather
        // than to the document.
        _vm.MarkReferenceRenamed(box.DataContext);
    }
}
