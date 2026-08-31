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
    // ---- symbols ------------------------------------------------------------------

    /// <summary>
    /// Turn the current drawing into a symbol.
    /// </summary>
    /// <remarks>
    /// Asks for a name first, because a browser full of "Symbol", "Symbol 2"
    /// and "Symbol 3" is a browser nobody searches — and naming a thing is
    /// cheapest at the moment you decide it is a thing.
    /// </remarks>
    private async void OnMakeSymbol(object? sender, RoutedEventArgs e)
    {
        if (await PromptForText("Make symbol", "Name", "Symbol") is not { } name) return;
        _vm.MakeSymbolFromWhatIsChosen(name);
    }

    /// <summary>
    /// Break the selected placement's link, making it ordinary strokes.
    /// </summary>
    /// <remarks>
    /// A one-way door with no confirmation, deliberately: it is a single undo
    /// step, and a prompt in front of something undo already covers is a prompt
    /// an artist learns to dismiss. The status line says what happened.
    /// </remarks>
    private void OnBreakSymbolLink(object? sender, RoutedEventArgs e) => _vm.BreakSelectedLink();

    /// <summary>
    /// Make one symbol of the layers the docker has picked, or of the active
    /// link's members.
    /// </summary>
    /// <remarks>
    /// <b>The prompt is conditional on purpose.</b> A head drawn once across
    /// four layers has the same answer whichever depth is chosen, so it is
    /// captured without a word; only layers actually holding drawings on other
    /// frames raise the question, because only then is there a question.
    /// </remarks>
    private async void OnMakeSymbolOfLayers(object? sender, RoutedEventArgs e)
    {
        var layers = _vm.SelectedLayerCount > 1
            ? _vm.SelectedLayers
            : _vm.LayersInActiveLink();
        if (layers.Count < 2)
        {
            _vm.AiStatus = "Pick more than one layer, or link them, to make one symbol of them.";
            return;
        }

        var depth = LayerCaptureDepth.ThisDrawing;
        if (_vm.LayersHoldMoreThanTheDrawingOnShow(layers))
        {
            if (await AskLayerCaptureDepthAsync() is not { } chosen) return;
            depth = chosen;
        }

        if (await PromptForText("Make symbol", "Name", "Symbol") is not { } name) return;
        _vm.MakeSymbolFromLayers(name, layers, depth);
    }

    /// <summary>
    /// How much of the picked layers to take, or null if the artist backed out.
    /// </summary>
    /// <remarks>
    /// <b>Three outcomes rather than a yes/no</b>, because both answers are
    /// things an artist means: a twelve-frame character captured whole is one
    /// animated symbol, and captured at the playhead is one pose of it. Neither
    /// is the "safe" one, so neither is Cancel — which is why this is not
    /// <c>ConfirmAsync</c>, whose whole shape is built around a destructive verb
    /// and a safe default.
    /// </remarks>
    private async Task<LayerCaptureDepth?> AskLayerCaptureDepthAsync()
    {
        LayerCaptureDepth? chosen = null;
        var whole = new Button { Content = "Every drawing" };
        var one = new Button { Content = "Only this one", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        var dialog = new Window
        {
            Title = "Make symbol",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "These layers hold drawings on other frames too.\n\n"
                            + "Take every drawing on them, or only the one showing now?",
                        MaxWidth = 380,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, whole, one },
                    },
                },
            },
        };
        whole.Click += (_, _) => { chosen = LayerCaptureDepth.WholeLayers; dialog.Close(); };
        one.Click += (_, _) => { chosen = LayerCaptureDepth.ThisDrawing; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return chosen;
    }

    private void OnDeleteSymbol(object? sender, RoutedEventArgs e)
    {
        if (_vm.SymbolBrowser.Selected is { } row) _vm.DeleteSymbol(row.Model);
    }

    private void OnPromoteSymbol(object? sender, RoutedEventArgs e) => _vm.PromoteSelectedSymbol();

    private void OnUpdateSymbolsFromLibrary(object? sender, RoutedEventArgs e) =>
        _vm.UpdateSymbolsFromLibrary();

    /// <summary>
    /// Report where the selected symbol is placed.
    /// </summary>
    /// <remarks>
    /// A button rather than something the panel shows automatically: the count
    /// comes from reading every document in the project, which is exactly what
    /// the folder layout exists to avoid doing on its own.
    /// </remarks>
    private void OnSymbolUsage(object? sender, RoutedEventArgs e)
    {
        if (_vm.SymbolBrowser.Selected is { } row) _vm.AiStatus = _vm.DescribeUsage(row.Model);
    }

    private void OnAcknowledgeStale(object? sender, RoutedEventArgs e)
    {
        var count = _vm.AcknowledgeOutdatedPlacements();
        if (count > 0)
        {
            _vm.AiStatus = $"Marked {count} placement(s) as seen. Nothing about the drawing changed.";
        }
    }

    /// <summary>Double-click a tile to open the symbol, like an animation row.</summary>
    private void OnSymbolTileOpened(object? sender, RoutedEventArgs e)
    {
        if (_vm.SymbolBrowser.Selected is { } row) _vm.OpenSymbol(row.Model);
    }

    /// <summary>
    /// Double-tap a history row: stand the document at that state. The same
    /// activation gesture the symbol tiles use — a single click only selects,
    /// so scrolling through a long history cannot rewrite the drawing.
    /// </summary>
    private void OnHistoryRowJump(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is UndoHistoryRow row) _vm.UndoHistory.Jump(row);
    }
    /// <remarks>
    /// Titled with the menu item's own words: this dialog also opened as
    /// plain "Save workspace", which reads as save-current — and since an
    /// unchanged name now deliberately forks "Name (edited)" (B230), a
    /// dialog that looks like save-current would manufacture exactly the
    /// surprise B230 removed.
    /// </remarks>
    private async void OnSaveWorkspaceAs(object? sender, RoutedEventArgs e)
    {
        if (await PromptForText("Save as new workspace", "Name", _vm.Workspace.SelectedName) is not { } name) return;
        _vm.Workspace.SaveAs(name);
        _vm.AiStatus = $"Saved workspace “{_vm.Workspace.SelectedName}”.";
    }

    private void OnResetWorkspace(object? sender, RoutedEventArgs e)
    {
        _vm.Workspace.Reset();
        _vm.AiStatus = $"Reset to “{_vm.Workspace.SelectedName}”.";
    }

    private void OnWorkspacePicked(object? sender, SelectionChangedEventArgs e)
    {
        // SelectingItemsControl rather than ListBox: the picker became a
        // ComboBox when its tab strip was found to be eating 872px of the quick
        // bar, and a handler typed to the old control silently stops firing.
        if (sender is not SelectingItemsControl picker || picker.SelectedItem is not WorkspaceRow row) return;
        // The picker SHOWS the current workspace, so selection is state rather
        // than a verb — and the guard is what stops the loop: applying raises
        // SelectedName, which re-selects the row, which fires this again.
        if (row.Name == _vm.Workspace.SelectedName) return;
        _vm.Workspace.Apply(row.Name);
    }

    private void OnDeleteWorkspace(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string name) return;
        e.Handled = true;
        _vm.Workspace.Delete(name);
    }

    // ---- drag a symbol onto the canvas to place it -----------------------------

    private static readonly DataFormat<string> SymbolDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-symbol");

    /// <summary>
    /// A tile does two things, told apart by whether the pointer moves: a click
    /// selects it, a drag carries the symbol to the spot you want it. Same
    /// shape as the colour swatch next door, and for the same reason — the drag
    /// API cannot be asked afterwards whether the gesture moved, so the press
    /// is held and the decision made on the first move.
    /// </summary>
    private Point? _tilePress;

    private PointerPressedEventArgs? _tilePressArgs;

    private string? _tileSymbolId;

    private void OnSymbolTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SymbolTiles).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not ViewModels.SymbolRow row) return;
        _tilePress = e.GetPosition(this);
        _tilePressArgs = e;
        _tileSymbolId = row.Model.Id;
        SymbolTiles.PointerMoved += OnSymbolTileMoved;
        SymbolTiles.PointerReleased += OnSymbolTileReleased;
    }

    private async void OnSymbolTileMoved(object? sender, PointerEventArgs e)
    {
        if (_tilePress is not { } start) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
        var press = _tilePressArgs;
        var id = _tileSymbolId;
        EndTileGesture();
        if (press is null || id is null) return;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(SymbolDragFormat, id));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("symbol-drag", ex);
        }
    }

    private void OnSymbolTileReleased(object? sender, PointerReleasedEventArgs e) => EndTileGesture();

    private void EndTileGesture()
    {
        SymbolTiles.PointerMoved -= OnSymbolTileMoved;
        SymbolTiles.PointerReleased -= OnSymbolTileReleased;
        _tilePress = null;
        _tilePressArgs = null;
        _tileSymbolId = null;
    }

    private static string? DraggedSymbolOf(DragEventArgs e) =>
        e.DataTransfer is { } transfer ? transfer.TryGetValue(SymbolDragFormat) : null;

    private void OnCanvasSymbolDragOver(object? sender, DragEventArgs e)
    {
        if (DraggedSymbolOf(e) is null) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnCanvasSymbolDrop(object? sender, DragEventArgs e)
    {
        if (DraggedSymbolOf(e) is not { } id) return;
        var (x, y) = Canvas.ViewToDoc(e.GetPosition(Canvas));
        // Where the pointer is, not the middle of the canvas: the whole point
        // of dragging rather than pressing Place is choosing the spot.
        _vm.PlaceSymbol(id, x, y);
        e.Handled = true;
    }
}
