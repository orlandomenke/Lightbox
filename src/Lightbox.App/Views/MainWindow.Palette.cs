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
    // ---- palette ---------------------------------------------------------------

    private static readonly FilePickerFileType GplFileType = new("GIMP palette")
    {
        Patterns = ["*.gpl"],
    };

    private async void OnImportReferenceClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import reference",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images and video")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp",
                                "*.mp4", "*.mov", "*.avi", "*.mkv", "*.webm"],
                },
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] },
                new FilePickerFileType("Video") { Patterns = ["*.mp4", "*.mov", "*.avi", "*.mkv", "*.webm"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await ImportReferenceFile(path);
    }

    /// <summary>Extensions <see cref="ImportReferenceFile"/> accepts as still images.</summary>
    private static readonly string[] ReferenceImageExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>Extensions <see cref="ImportReferenceFile"/> accepts as footage.</summary>
    private static readonly string[] ReferenceVideoExtensions =
        [".mp4", ".mov", ".avi", ".mkv", ".webm"];

    /// <summary>
    /// Import a file as reference — one path for the picker and for a file
    /// dropped onto the window, so the two can never drift apart.
    /// </summary>
    private async Task ImportReferenceFile(string path)
    {
        // Footage goes its own way (Q56/Q57): frames extracted at the
        // scene's fps, and the artist chooses what the document keeps.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ReferenceVideoExtensions.Contains(ext))
        {
            var clipMb = new FileInfo(path).Length / (1024.0 * 1024.0);
            var storage = await AskImportChoice(
                "What is this clip for?",
                $"“{Path.GetFileName(path)}” — {clipMb:0.#} MB.",
                ("Reference — keep by path",
                 "To draw against. The document stays light; keep the clip beside it when sharing. Never exports.",
                 Services.ClipStorage.ReferenceByPath),
                ("Reference — embed the frames",
                 "To draw against, self-contained: the extracted frames travel in the document at reference quality. Never exports.",
                 Services.ClipStorage.ReferenceEmbedded),
                ("Production — embed the clip",
                 $"Part of the shot: the footage travels in the document at full fidelity ({clipMb:0.#} MB) and composites into video and PNG exports.",
                 Services.ClipStorage.Production));
            if (storage is not { } mode) return;

            _vm.AiStatus = "Reading the clip…";
            var error = await _vm.ImportVideoReference(path, mode);
            _vm.AiStatus = error ?? $"Drawing against “{Path.GetFileName(path)}”.";
            if (error is null) _vm.ReferenceDockerVisible = true;
            return;
        }

        if (_vm.ImportReferenceImageFile(path)) _vm.ReferenceDockerVisible = true;
    }

    /// <summary>
    /// Does a drag carry files this window would import as reference? The
    /// in-process drags (cels, colours, symbols) carry no file format, so
    /// they never reach the answer.
    /// </summary>
    private static List<string> DroppedReferenceFiles(DragEventArgs e)
    {
        var paths = new List<string>();
        if (e.DataTransfer?.TryGetFiles() is not { } items) return paths;
        foreach (var item in items)
        {
            if (item.TryGetLocalPath() is not { } path) continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ReferenceImageExtensions.Contains(ext) || ReferenceVideoExtensions.Contains(ext))
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (DroppedReferenceFiles(e).Count == 0 && DroppedWebImages(e).Count == 0) return;
        if (DroppedReferenceFiles(e).Count == 0) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>
    /// Any image dropped anywhere on the window becomes a reference — the
    /// shortest path from "found the perfect pose on disk" to drawing against
    /// it, with no menu in between. Footage goes through the same import as
    /// the picker, question dialog included. A picture dragged straight from a
    /// browser takes the same door: no file, so the URL is fetched and the
    /// bytes go through the same import as a file's would.
    /// the picker, question dialog included.
    /// </summary>
    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        var files = DroppedReferenceFiles(e);
        if (files.Count > 0)
        {
            e.Handled = true;
            foreach (var path in files)
            {
                await ImportReferenceFile(path);
            }
            return;
        }

        var uris = DroppedWebImages(e);
        if (uris.Count == 0) return;
        e.Handled = true;
        await ImportWebImage(uris);
    }

    /// <summary>
    /// Import the first of a browser drag's candidate URIs that fetches and
    /// decodes. One reference per drop, not one per candidate: the candidates
    /// are the same picture described three ways (uri-list, text, HTML), so
    /// importing them all would tape up duplicates.
    /// </summary>
    private async Task ImportWebImage(IReadOnlyList<Uri> uris)
    {
        _vm.AiStatus = "Fetching the image…";
        foreach (var uri in uris)
        {
            var bytes = await Services.WebImageDrop.FetchAsync(uri);
            if (bytes is null || !_vm.ImportReferenceImageBytes(Services.WebImageDrop.NameFor(uri), bytes))
            {
                continue;
            }
            _vm.AiStatus = $"Drawing against \u201c{Services.WebImageDrop.NameFor(uri)}\u201d.";
            _vm.ReferenceDockerVisible = true;
            return;
        }
        // Every candidate failed — refused by the site, or bytes that are not
        // an image (a page URL dragged instead of the picture).
        _vm.AiStatus = "That drop did not contain an image Lightbox could read.";
    }

    /// <summary>Formats a picture dragged out of a browser can arrive in.</summary>
    private static readonly DataFormat<string> UriListFormat =
        DataFormat.CreateStringPlatformFormat("text/uri-list");

    private static readonly DataFormat<string> HtmlFormat =
        DataFormat.CreateStringPlatformFormat("text/html");

    /// <summary>
    /// The web-image candidates in a drag, or none — the browser counterpart
    /// of <see cref="DroppedReferenceFiles"/>. Anything carrying actual files
    /// is not a web drag, whatever else rides along.
    /// </summary>
    private static IReadOnlyList<Uri> DroppedWebImages(DragEventArgs e)
    {
        if (e.DataTransfer is not { } data || data.Contains(DataFormat.File)) return [];
        return Services.WebImageDrop.ImageUris(
            data.TryGetValue(UriListFormat),
            data.TryGetText(),
            data.TryGetValue(HtmlFormat));
    }

    private async void OnImportPaletteClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import palette",
            AllowMultiple = false,
            FileTypeFilter = [GplFileType],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _vm.PaletteDocker.ImportGpl(path);
    }

    private async void OnExportPaletteClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.PaletteDocker.SelectedPalette is not { } palette) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export palette",
            SuggestedFileName = $"{palette.Name}.gpl",
            FileTypeChoices = [GplFileType],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        _vm.PaletteDocker.ExportGpl(path);
    }

    // ---- drag a colour onto the canvas to fill --------------------------------

    private static readonly DataFormat<string> ColorDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-color");

    /// <summary>
    /// A colour swatch does two things, told apart by whether the pointer
    /// moves: a click opens its picker, a drag carries the colour to the canvas
    /// to fill with — the shortest path from "I chose this colour" to "that
    /// shape is that colour", without visiting the tool bar on the way.
    /// </summary>
    /// <remarks>
    /// <c>DoDragDropAsync</c> does not return until the gesture is over and
    /// reports no distance, so "was that a drag" cannot be asked afterwards.
    /// The press is therefore held, and the decision made on the first move —
    /// which is the same shape as the panel-header grip, for the same reason.
    /// </remarks>
    private Point? _swatchPress;

    /// <summary>
    /// The press that started the gesture. Held because the drag API wants the
    /// event that began it, and by the time we know this is a drag rather than
    /// a click that event has been and gone.
    /// </summary>
    private PointerPressedEventArgs? _swatchPressArgs;

    /// <summary>The swatch a gesture started on — there are three of them now.</summary>
    private Control? _swatchControl;

    /// <summary>
    /// Press on any colour swatch: a click opens its picker, a drag carries
    /// the colour off to be dropped as a fill.
    /// </summary>
    /// <remarks>
    /// One handler for all of them. It used to be two, and both were dead: the
    /// foreground swatch in the tool bar wired its gesture onto the Color
    /// panel's swatch rather than the one you pressed, and the background one
    /// asked for an attached flyout that had never been attached. Neither did
    /// anything at all.
    /// </remarks>
    private void OnColorSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control swatch) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _swatchPress = e.GetPosition(this);
        _swatchPressArgs = e;
        _swatchControl = swatch;
        swatch.PointerMoved += OnColorSwatchMoved;
        swatch.PointerReleased += OnColorSwatchReleased;
    }

    /// <summary>Which colour a swatch carries — its Tag names the half of the pair.</summary>
    private string ColorOf(Control? swatch) =>
        swatch?.Tag as string == "background" ? _vm.BackgroundColorHex : _vm.ColorHex;

    private async void OnColorSwatchMoved(object? sender, PointerEventArgs e)
    {
        if (_swatchPress is not { } start) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
        var press = _swatchPressArgs;
        var hex = ColorOf(_swatchControl);
        EndSwatchGesture();
        if (press is null) return;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ColorDragFormat, hex));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("color-drag", ex);
        }
    }

    private void OnColorSwatchReleased(object? sender, PointerReleasedEventArgs e)
    {
        var clicked = _swatchPress is not null ? _swatchControl : null;
        EndSwatchGesture();
        if (clicked is not null) FlyoutBase.ShowAttachedFlyout(clicked);
    }

    private void EndSwatchGesture()
    {
        if (_swatchControl is { } swatch)
        {
            swatch.PointerMoved -= OnColorSwatchMoved;
            swatch.PointerReleased -= OnColorSwatchReleased;
        }
        _swatchPress = null;
        _swatchPressArgs = null;
        _swatchControl = null;
    }

    private static string? DraggedColorOf(DragEventArgs e) =>
        e.DataTransfer is { } transfer ? transfer.TryGetValue(ColorDragFormat) : null;

    private void OnCanvasColorDragOver(object? sender, DragEventArgs e)
    {
        if (DraggedColorOf(e) is null) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnCanvasColorDrop(object? sender, DragEventArgs e)
    {
        if (DraggedColorOf(e) is not { } hex) return;

        var (x, y) = Canvas.ViewToDoc(e.GetPosition(Canvas));
        _vm.DropColorAt(hex, x, y);
        e.Handled = true;
    }

    /// <summary>
    /// Hand the canvas the boxes to draw, or null when the mode is off.
    /// </summary>
    /// <remarks>
    /// A snapshot in document coordinates rather than the cells themselves.
    /// The renderer runs on another thread and must never read a document
    /// object the UI thread may be halfway through editing.
    /// </remarks>
    /// <summary>
    /// Picking a projected reference up with the Arrow (Q108).
    /// </summary>
    /// <remarks>
    /// The canvas owns the gesture — which box, which corner, how far — and the
    /// view model owns what it means to the document, exactly as the grid
    /// gizmos beneath already split it. Wired from here rather than from the
    /// constructor for the reason the decomposition note gives: the section that
    /// owns the state owns its wiring.
    /// </remarks>
    private void WireReferenceSheetGestures()
    {
        Canvas.ReferenceSheetPicked += (x, y) => _vm.SelectReferenceOnCanvasAt(x, y);
        Canvas.ReferenceSheetGestureStarted += () => _vm.BeginReferenceGesture();
        Canvas.ReferenceSheetMoved += _vm.DragReferenceBy;
        Canvas.ReferenceSheetScaled += _vm.ScaleReferenceAbout;
        Canvas.ReferenceSheetGestureEnded += _vm.EndReferenceGesture;
        // The sweep lock is a workspace setting, so it moves without the
        // document changing and nothing else would repaint the grips.
        _vm.Workspace.ReferenceLockChanged += RefreshReferenceSheetBox;
    }

    /// <summary>
    /// The box round the reference the Arrow has hold of, or none (Q108).
    /// </summary>
    /// <remarks>
    /// Its own channel rather than an entry in <see cref="Rendering.CanvasControl.ReferenceBoxes"/>,
    /// because that list is also the switch that says grid editing is on. A
    /// locked reference still publishes its box — that is what makes the lock
    /// legible — and the canvas draws it without grips.
    /// </remarks>
    private void RefreshReferenceSheetBox()
    {
        Canvas.ReferenceSheetLocked = _vm.CanvasReferenceLocked;
        Canvas.ReferenceSheetBox = _vm.CanvasReferenceBounds is { } r
            ? new Rendering.CanvasControl.ReferenceBox(
                (float)r.X, (float)r.Y, (float)r.W, (float)r.H,
                // No pivot: a sheet has no registration mark of its own, and the
                // cross belongs to the cell that does.
                float.NaN, float.NaN, Selected: !_vm.CanvasReferenceLocked)
            : null;
    }

    private void RefreshReferenceBoxes()
    {
        RefreshReferenceSheetBox();
        if (!_vm.ReferenceGridEditMode || _vm.ActiveReference is not { } strip)
        {
            Canvas.ReferenceBoxes = null;
            return;
        }
        var boxes = new List<Rendering.CanvasControl.ReferenceBox>(strip.Cells.Count);
        for (var i = 0; i < strip.Cells.Count; i++)
        {
            var cell = strip.Cells[i];
            var (x, y, w, h) = _vm.CellRect(strip, cell);
            var (px, py) = cell.Pivot;
            var scale = Math.Max(0.01, strip.Scale);
            boxes.Add(new Rendering.CanvasControl.ReferenceBox(
                (float)x, (float)y, (float)w, (float)h,
                (float)(strip.OffsetX + cell.Dx + px * scale),
                (float)(strip.OffsetY + cell.Dy + py * scale),
                i == _vm.SelectedReferenceCell));
        }
        Canvas.ReferenceBoxes = boxes;
    }

    // ---- the palette hierarchy ------------------------------------------------

    private static readonly DataFormat<string> PaletteNodeDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-palette-node");

    /// <summary>
    /// The row a drag started on, and where. Held for the same reason the
    /// colour swatch holds its press: <c>DoDragDropAsync</c> wants the event
    /// that began the gesture, and by the time we know this is a drag rather
    /// than a click that event has been and gone.
    /// </summary>
    private (PaletteNode Node, Point At, PointerPressedEventArgs Args)? _paletteDrag;

    private static PaletteNode? NodeOf(object? sender) =>
        (sender as Control)?.DataContext as PaletteNode;

    private void OnPaletteNodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (NodeOf(sender) is not { } node) return;
        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsRightButtonPressed)
        {
            _vm.PaletteDocker.SelectedNode = node;
            ShowPaletteNodeMenu((Control)sender!, node);
            e.Handled = true;
            return;
        }
        if (!point.IsLeftButtonPressed || !node.IsDraggable) return;
        _paletteDrag = (node, e.GetPosition(this), e);
    }

    private async void OnPaletteNodeMoved(object? sender, PointerEventArgs e)
    {
        if (_paletteDrag is not { } drag) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - drag.At.X) < 4 && Math.Abs(now.Y - drag.At.Y) < 4) return;
        _paletteDrag = null;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(
                PaletteNodeDragFormat, drag.Node.Palette?.Id ?? drag.Node.Folder!.Id));
            await DragDrop.DoDragDropAsync(drag.Args, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("palette-drag", ex);
        }
    }

    private void OnPaletteNodeReleased(object? sender, PointerReleasedEventArgs e) =>
        _paletteDrag = null;

    /// <summary>The row a drag is carrying, resolved back from its id.</summary>
    private PaletteNode? DraggedNode(DragEventArgs e) =>
        e.DataTransfer?.TryGetValue(PaletteNodeDragFormat) is { } id
            ? FindPaletteNode(_vm.PaletteDocker.Tree, id)
            : null;

    private static PaletteNode? FindPaletteNode(IEnumerable<PaletteNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Palette?.Id == id || node.Folder?.Id == id) return node;
            if (FindPaletteNode(node.Children, id) is { } hit) return hit;
        }
        return null;
    }

    private void OnPaletteNodeDragOver(object? sender, DragEventArgs e)
    {
        // The cursor says no before the drop does. A move that silently does
        // nothing on release reads as a bug in the drag, not as a refusal.
        var onto = NodeOf(sender);
        if (DraggedSwatch(e) is not null)
        {
            e.DragEffects = onto is { IsPalette: true } ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var source = DraggedNode(e);
        var allowed = source is not null && onto is not null
            && !ReferenceEquals(source, onto) && source.Scope == onto.Scope;
        e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPaletteNodeDrop(object? sender, DragEventArgs e)
    {
        if (DraggedSwatch(e) is { } swatch)
        {
            _vm.PaletteDocker.MoveSwatch(swatch, NodeOf(sender)?.Palette);
            e.Handled = true;
            return;
        }
        if (DraggedNode(e) is not { } source) return;
        _vm.PaletteDocker.Drop(source, NodeOf(sender));
        e.Handled = true;
    }

    // ---- dragging a swatch into another palette --------------------------------

    private static readonly DataFormat<string> SwatchDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-swatch");

    private (SwatchRow Row, Point At, PointerPressedEventArgs Args)? _swatchDrag;

    /// <summary>
    /// The swatch a drag is carrying, resolved back from its id.
    /// </summary>
    /// <remarks>
    /// By id rather than by object, so a drop that lands after the grid has
    /// been rebuilt still finds the row it means. Only the palette on screen
    /// is searched — a swatch can only be dragged out of the one you can see.
    /// </remarks>
    private SwatchRow? DraggedSwatch(DragEventArgs e) =>
        e.DataTransfer?.TryGetValue(SwatchDragFormat) is { } id
            ? _vm.PaletteDocker.Swatches.FirstOrDefault(s => s.Id == id)
            : null;

    private void OnSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SwatchRow row) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _swatchDrag = (row, e.GetPosition(this), e);
    }

    private async void OnSwatchMoved(object? sender, PointerEventArgs e)
    {
        if (_swatchDrag is not { } drag) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - drag.At.X) < 4 && Math.Abs(now.Y - drag.At.Y) < 4) return;
        _swatchDrag = null;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(SwatchDragFormat, drag.Row.Id));
            await DragDrop.DoDragDropAsync(drag.Args, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("swatch-drag", ex);
        }
    }

    private void OnSwatchReleased(object? sender, PointerReleasedEventArgs e) => _swatchDrag = null;

    /// <summary>
    /// The right-click menu, built here rather than declared in the template.
    /// </summary>
    /// <remarks>
    /// A menu declared inside a template lives in a popup, outside the tree it
    /// came from, and the bindings that would reach the docker resolve to
    /// nothing there — the items look right and do nothing at all. Handlers
    /// close over the view model instead, which cannot go quiet.
    /// </remarks>
    private void ShowPaletteNodeMenu(Control anchor, PaletteNode node)
    {
        var docker = _vm.PaletteDocker;
        var assign = new MenuItem { Header = "Assign to" };
        foreach (var target in docker.AssignTargets)
        {
            if (!docker.CanAssign(node, target)) continue;
            var item = new MenuItem { Header = target.Label };
            var to = target;
            item.Click += (_, _) => docker.Assign(node, to);
            assign.Items.Add(item);
        }

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) => node.IsRenaming = true;

        var remove = new MenuItem { Header = node.IsFolder ? "Delete folder" : "Delete palette" };
        remove.Click += (_, _) =>
        {
            docker.SelectedNode = node;
            docker.RemovePaletteCommand.Execute(null);
        };

        var menu = new MenuFlyout();
        if (assign.Items.Count > 0) menu.Items.Add(assign);
        menu.Items.Add(rename);
        menu.Items.Add(remove);
        menu.ShowAt(anchor, showAtPointer: true);
    }

    private void OnPaletteNameCommitted(object? sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) node.IsRenaming = false;
    }

    private void OnPaletteNameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        if (NodeOf(sender) is { } node) node.IsRenaming = false;
        e.Handled = true;
    }
}
