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
    // ---- canvas view tools (view-only: never touch the document) -------------

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Canvas.ZoomIn();

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Canvas.ZoomOut();

    private void OnRotateCw(object? sender, RoutedEventArgs e) => Canvas.RotateBy(15);

    private void OnRotateCcw(object? sender, RoutedEventArgs e) => Canvas.RotateBy(-15);

    private void OnToggleMirror(object? sender, RoutedEventArgs e) => Canvas.ToggleMirror();

    private void OnResetView(object? sender, RoutedEventArgs e) => Canvas.ResetView();


    /// <summary>
    /// The release edge, so a borrowed tool comes back.
    /// </summary>
    /// <remarks>
    /// Tunnelling where it is attached, because a focused control that swallows
    /// the key-up would otherwise leave the modifier stuck down and the artist
    /// holding an eyedropper they let go of — the failure mode that makes a
    /// momentary tool worse than none. A named method rather than a lambda
    /// because every floating panel window attaches it too: a hold that begins
    /// in the main window and ends in a torn-off panel has to release, and the
    /// two copies this would otherwise need are two chances to fix one of them.
    /// </remarks>
    private void OnKeyUpEdge(object? sender, KeyEventArgs e)
    {
        _vm.ApplyHeldModifiers(e.KeyModifiers);
        // The other half of a momentary tool key (B176): matched on the
        // physical key rather than the gesture, so a modifier pressed
        // mid-hold cannot orphan the release.
        if (_momentaryToolKey is { } held && e.Key == held.Key)
        {
            _momentaryToolKey = null;
            _vm.EndMomentaryTool(held.Tool);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Don't hijack keys while the user is typing (layer rename, color hex, AI prompt).
        if (e.Source is TextBox) return;

        // Grid editing owns Escape: it is a mode, and a mode you cannot leave
        // with the key everybody tries first is a mode you are stuck in.
        if (_vm.ReferenceGridEditMode && e.Key == Key.Escape)
        {
            _vm.ReferenceGridEditMode = false;
            e.Handled = true;
            return;
        }

        // An active transform session owns Enter/Escape outright.
        if (_vm.TransformActive)
        {
            if (e.Key == Key.Enter)
            {
                CommitTransformFromGizmo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                _vm.CancelTransform();
                e.Handled = true;
                return;
            }
        }

        // Isolation owns Escape and Enter for the same reason, and above the
        // shortcut switch for the same reason: a mode you cannot leave with the
        // key everybody tries first is a mode you are stuck in. Both keys mean
        // "done" here rather than "keep" and "discard" — every node drag was
        // already its own undo step, so there is nothing left to discard.
        if (_vm.PathEditActive && e.Key is Key.Escape or Key.Enter)
        {
            _vm.EndPathEdit();
            e.Handled = true;
            return;
        }

        // A pen path in progress owns the same two keys, and Backspace as well.
        // Above the shortcut switch for isolation's reason and one more: Delete
        // is `lines.delete` down there, and a pen halfway through a path is the
        // one moment where that key plainly means "take the last point off".
        // Only while the pen is in hand: a parked path (the session survives a
        // tool switch now) must not swallow Delete from the arrow's selection
        // or Escape from whatever mode the current tool is in.
        if (_vm.PenActive && _vm.IsPenTool)
        {
            if (e.Key is Key.Escape or Key.Enter)
            {
                // Both mean "done", and neither discards — see FinishPen. What
                // was drawn is one undo step, so Ctrl+Z is the way to lose it.
                _vm.FinishPen();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Back or Key.Delete)
            {
                _vm.RemoveLastPenNode();
                e.Handled = true;
                return;
            }
        }

        // Both edges go through one call: a press and a release are the same
        // question — what should the tool be now — and answering it in two
        // handlers is how a modifier gets stuck down when a key-up goes missing.
        _vm.ApplyHeldModifiers(e.KeyModifiers);

        var shortcutId = _shortcuts.IdFor(e, CurrentShortcutScope(sender));

        // A momentary tool key (B176): the press borrows, and the release
        // decides — a tap latches, a hold restores. The physical key is
        // remembered here because the release may arrive with different
        // modifiers than the press and would no longer resolve to the same
        // gesture; the key itself is the identity that survives that.
        if (shortcutId is not null
            && _shortcuts.Find(shortcutId)?.MomentaryTool is { } momentary)
        {
            _momentaryToolKey = (e.Key, momentary);
            _vm.BeginMomentaryTool(momentary);
            e.Handled = true;
            return;
        }

        switch (shortcutId)
        {
            case "file.save":
                // Deliberately the same path as the menu item rather than _vm.Save(): a
                // document with nowhere to go has to reach the picker, and duplicating
                // that decision here is how the two drift apart.
                OnSaveInPlaceClicked(this, e);
                e.Handled = true;
                break;
            case "file.saveAs":
                _ = SaveDocumentAsAsync();
                e.Handled = true;
                break;
            case "file.saveVersion":
                // Same path as the menu item, for the reason file.save gives:
                // the "is there anything to version" answer lives in one place.
                OnSaveVersionClicked(this, e);
                e.Handled = true;
                break;
            case "file.versionHistory":
                OnVersionHistoryClicked(this, e);
                e.Handled = true;
                break;
            case "canvas.transform":
                if (!_vm.TransformActive) _vm.BeginTransform();
                break;
            case "project.refresh":
                // Harmless with no project — the command guards on it — so this
                // does not need a HasProject check that could drift from the one
                // in the view model.
                _vm.ProjectDocker.RefreshFromDiskCommand.Execute(null);
                e.Handled = true;
                break;
            case "project.window":
                // Harmless with no project, for the same reason as above: the
                // method guards on it rather than the key handler holding a
                // second copy of the condition.
                _ = OpenProjectWindowAsync();
                e.Handled = true;
                break;
            case "image.resizeCanvas":
                _ = ResizeAsync(ViewModels.ResizeMode.Canvas);
                e.Handled = true;
                break;
            case "image.resizeImage":
                _ = ResizeAsync(ViewModels.ResizeMode.Image);
                e.Handled = true;
                break;
            case "timeline.insertKey":
                _vm.InsertKeyframeAtPlayhead();
                break;
            case "timeline.deleteColumn":
                // The playhead, because a shortcut has no cel under a pointer —
                // the menu route passes the cel that was clicked.
                _vm.DeleteColumnAt(_vm.CurrentFrameIndex);
                e.Handled = true;
                break;
            case "timeline.playPause":
                _vm.TogglePlaybackCommand.Execute(null);
                break;
            case "canvas.undo":
                _vm.UndoCommand.Execute(null);
                break;
            case "canvas.redo":
                _vm.RedoCommand.Execute(null);
                break;
            case "timeline.prevFrame":
                _vm.CurrentFrameIndex = Math.Max(0, _vm.CurrentFrameIndex - 1);
                break;
            case "timeline.nextFrame":
                _vm.CurrentFrameIndex = Math.Min(_vm.Doc.Scene.FrameCount - 1, _vm.CurrentFrameIndex + 1);
                break;
            case "color.swap":
                _vm.SwapColorsCommand.Execute(null);
                break;
            case "color.reset":
                _vm.ResetColorsCommand.Execute(null);
                break;
            case "tool.brush":
                _vm.ActiveTool = ToolId.Brush; // back to the last-configured brush
                break;
            // tool.eraser and canvas.pickColor never reach this switch: they
            // carry MomentaryTool, so the branch above owns both their tap
            // (latch) and their hold (borrow and restore).
            case "tool.fill":
                _vm.ActiveTool = ToolId.Fill;
                break;
            case "tool.gradient":
                _vm.ActiveTool = ToolId.Gradient;
                break;
            case "tool.select":
                _vm.SelectToolCommand.Execute(ToolId.Select); // again = next variant
                break;
            case "select.all":
                _vm.SelectAllCommand.Execute(null);
                break;
            case "timeline.copyCel":
                _vm.CopyCurrentCel();
                break;
            case "timeline.cutCel":
                _vm.CutCurrentCel();
                break;
            case "timeline.pasteCel":
                _vm.PasteCurrentCel();
                break;
            case "select.none":
                _vm.DeselectCommand.Execute(null);
                break;
            case "select.invert":
                _vm.InvertSelectionCommand.Execute(null);
                break;
            case "select.cancel":
                _vm.CancelPolygon();
                _vm.CancelGradient();
                return; // leave Escape unhandled so open flyouts still close
            case "docker.deleteLayer":
                _vm.DeleteActiveLayerCommand.Execute(null);
                break;
            case "docker.clearLayer":
                _vm.ClearActiveLayerCommand.Execute(null);
                break;
            case "docker.mergeDown":
                RequestMergeDown(null); // null = the active layer
                break;
            // Flipping: hop between key drawings without leaving the pen.
            case "timeline.prevKey":
                _vm.PreviousKeyframeCommand.Execute(null);
                break;
            case "timeline.nextKey":
                _vm.NextKeyframeCommand.Execute(null);
                break;
            case "canvas.nudgeLeft":
                _vm.NudgeSelection(-1, 0);
                break;
            case "canvas.nudgeRight":
                _vm.NudgeSelection(1, 0);
                break;
            case "canvas.nudgeUp":
                _vm.NudgeSelection(0, -1);
                break;
            case "canvas.nudgeDown":
                _vm.NudgeSelection(0, 1);
                break;
            // Layer walking: rows show topmost first, so "above" is a higher scene index.
            case "docker.layerAbove":
                _vm.ActiveLayerIndex = Math.Min(_vm.Doc.Scene.Layers.Count - 1, _vm.ActiveLayerIndex + 1);
                break;
            case "docker.layerBelow":
                _vm.ActiveLayerIndex = Math.Max(0, _vm.ActiveLayerIndex - 1);
                break;
            case "canvas.mirror":
                Canvas.ToggleMirror();
                break;
            case "canvas.resetView":
                Canvas.ResetView();
                break;
            case "canvas.rulers":
                _vm.Workspace.RulersVisible = !_vm.Workspace.RulersVisible;
                break;
            case "canvas.rigEditMode":
                _vm.RigEditMode = !_vm.RigEditMode;
                e.Handled = true;
                break;
            case "canvas.motionTrail":
                _vm.MotionTrail = !_vm.MotionTrail;
                break;
            case "canvas.showGuides":
                _vm.Workspace.GuidesVisible = !_vm.Workspace.GuidesVisible;
                break;
            case "canvas.lockGuides":
                _vm.Workspace.GuidesLocked = !_vm.Workspace.GuidesLocked;
                break;
            case "tool.move":
                _vm.SelectToolCommand.Execute(ToolId.Move);
                break;
            case "tool.arrow":
                _vm.SelectToolCommand.Execute(ToolId.Arrow);
                break;
            case "tool.directselect":
                _vm.SelectToolCommand.Execute(ToolId.DirectSelect);
                break;
            case "tool.pen":
                _vm.SelectToolCommand.Execute(ToolId.Pen);
                break;
            case "tool.shape":
                _vm.SelectToolCommand.Execute(ToolId.Shape);
                break;
            case "tool.width":
                _vm.SelectToolCommand.Execute(ToolId.Width);
                break;
            case "tool.bone":
                _vm.SelectToolCommand.Execute(ToolId.Bone);
                break;
            case "armature.posingMode":
                _vm.PosingMode = !_vm.PosingMode;
                break;
            case "armature.weightPaint":
                _vm.WeightPainting = !_vm.WeightPainting;
                break;
            case "armature.deleteBone":
                _vm.DeleteBoneCommand.Execute(null);
                break;
            case "lines.simplify":
                _vm.SimplifyLineCommand.Execute(null);
                break;
            case "lines.delete":
                _vm.DeleteSelectedLinesCommand.Execute(null);
                break;
            // B173. Delete asks the marquee first and falls back to the lines,
            // so the decision lives in the command rather than being split
            // between here and there — this is the case the shortcut registry
            // exists to keep honest, and a branch in the key handler is exactly
            // what the Configure window cannot see.
            case "select.clear":
                _vm.DeleteSelectionContentsCommand.Execute(null);
                break;
            case "select.fillBackground":
                _vm.FillSelectionWithBackgroundCommand.Execute(null);
                break;
            case "lines.recolour":
                _vm.RecolourSelectedLinesCommand.Execute(null);
                break;
            // Sizing by eye used to be Shift+drag on the canvas. Shift is the
            // constraint key now, everywhere, so the brush keeps the two keys
            // every other application binds this to.
            case "brush.smaller":
                _vm.BrushSize = Math.Max(1, _vm.BrushSize - BrushSizeStep(_vm.BrushSize));
                break;
            case "brush.larger":
                _vm.BrushSize = Math.Min(500, _vm.BrushSize + BrushSizeStep(_vm.BrushSize));
                break;
            default:
                return; // unbound or context-gated: not ours
        }
        e.Handled = true;
    }

    private async void OnConfigureClicked(object? sender, RoutedEventArgs e)
    {
        await new ConfigureWindow(_shortcuts, _vm).ShowDialog(this);
        // A rebind has to reach the menu labels, or they advertise the old key.
        ShowSaveGestures();
    }

    /// <summary>
    /// Open the project window — Q29's second surface.
    /// </summary>
    /// <remarks>
    /// Modal on the main window, like Configure and Export. It edits the same
    /// manifest the docker is showing, and two surfaces writing one project with
    /// neither knowing about the other is the class of bug B61 was; the docker
    /// re-reads on close through the same <c>changed</c> callback every other
    /// edit uses.
    /// </remarks>
    private async Task OpenProjectWindowAsync()
    {
        if (_vm.ProjectDocker.Project is not { } project) return;
        var window = new Views.ProjectWindow(project, () => _vm.ProjectDocker.MarkManifestChanged());
        // What creation needs, supplied rather than reached for: the same
        // blank document every other creator makes, the docker's dirty set so
        // the save writes what the window made, and the save itself so
        // "created" means "on disk" — the window is used between drawings,
        // where a pending badge would only defer the question.
        window.ViewModel.NewDocument = _vm.NewProjectDocument;
        window.ViewModel.DocumentCreated = _vm.ProjectDocker.MarkDirty;
        window.ViewModel.RequestSave = () => _vm.SaveProject();
        // The export plan runs from the window's Export tab through the
        // docker's own resolution and bookkeeping — one export, two surfaces.
        window.ViewModel.ResolveExport = _vm.ProjectDocker.ResolveExport;
        window.ViewModel.RecordExport = _vm.ProjectDocker.RecordExport;
        // Opening a row goes through the same two callbacks the docker's
        // double-click uses, so a document opened from the window focuses the
        // tab it is already in rather than arriving in a second one. The window
        // closes itself afterwards — it is modal, and a tab behind it is
        // invisible — and the Refresh below then runs as it does on any close.
        window.ViewModel.OpenDocument = _vm.OpenProjectDocument;
        window.ViewModel.OpenSheet = _vm.OpenProjectSheet;
        await window.ShowDialog(this);
        _vm.ProjectDocker.Refresh();
    }

    private async void OnProjectWindowClicked(object? sender, RoutedEventArgs e) =>
        await OpenProjectWindowAsync();

    private async void OnResizeCanvasClicked(object? sender, RoutedEventArgs e) =>
        await ResizeAsync(ViewModels.ResizeMode.Canvas);

    private async void OnResizeImageClicked(object? sender, RoutedEventArgs e) =>
        await ResizeAsync(ViewModels.ResizeMode.Image);

    /// <summary>
    /// Ask for a size, then hand it to the view model and refit the view.
    /// </summary>
    /// <remarks>
    /// The view is refitted because the paper is a different size than the one
    /// the current zoom and pan were chosen for — leaving a grown canvas half
    /// off-screen reads as the resize having gone wrong.
    /// </remarks>
    private async Task ResizeAsync(ViewModels.ResizeMode mode)
    {
        var dialog = new Views.ResizeDialog(_vm.Doc.Scene, mode);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;
        if (_vm.ApplyResize(dialog.Choice)) Canvas.ResetView();
    }

    /// <summary>
    /// The tip workshop. A window rather than a docker because making a tip is
    /// not something you do mid-stroke, and a panel for it would cost layout
    /// space in every session that never opens one.
    /// </summary>
    private async void OnBrushTipsClicked(object? sender, RoutedEventArgs e) =>
        await new BrushTipsWindow(_vm).ShowDialog(this);

    /// <summary>
    /// The brush library — import, rename, remove.
    /// </summary>
    /// <remarks>
    /// Reached from three places on purpose: the Edit menu, the brush options page, and the
    /// bottom of the picker flyout. The picker is where an artist is standing when they notice
    /// they have forty brushes they did not choose, and making them close it and go to a menu
    /// is the friction that left the collection sitting there.
    /// <para>
    /// The picker's flyout is hidden first. Opening a modal from inside a flyout leaves the
    /// flyout floating over it, which looks like two windows arguing.
    /// </para>
    /// </remarks>
    private async void OnBrushLibraryClicked(object? sender, RoutedEventArgs e)
    {
        if (BrushPickerButton?.Flyout is { } picker) picker.Hide();
        await new BrushLibraryWindow(_vm).ShowDialog(this);
        RefreshBrushPickerButton();
        RefreshPresetPage();
    }
}
