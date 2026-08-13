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

/// <summary>The window half of a transform session.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c>, which was 5,544 lines. See
/// <c>docs/DESIGN-mainviewmodel-decomposition.md</c> — the view is 79% single-section
/// state over one <c>_vm</c> field, so it needed splitting rather than decomposing.
/// </remarks>
public partial class MainWindow
{
    // ---- transform session (window side) --------------------------------------

    /// <summary>Read the gizmo and commit through the matching VM path.</summary>
    private void CommitTransformFromGizmo()
    {
        if (Canvas.TransformIsIdentity)
        {
            _vm.CancelTransform(); // nothing changed — don't record an undo step
            return;
        }
        if (Canvas.TransformIsPerspectiveResult)
        {
            var (src, dst) = Canvas.TransformQuadResult;
            _vm.CommitTransformPerspective(src, dst);
        }
        else
        {
            var (px, py, sx, sy, angle, dx, dy) = Canvas.TransformAffineResult;
            _vm.CommitTransformAffine(px, py, sx, sy, angle, dx, dy);
        }
    }

    private void OnTransformPerspectiveToggled(object? sender, RoutedEventArgs e) =>
        Canvas.TransformPerspective = TransformPerspectiveToggle.IsChecked == true;

    private void OnTransformMirrorH(object? sender, RoutedEventArgs e) =>
        Canvas.MirrorTransformGizmo(horizontal: true);

    private void OnTransformMirrorV(object? sender, RoutedEventArgs e) =>
        Canvas.MirrorTransformGizmo(horizontal: false);

    private void OnTransformReset(object? sender, RoutedEventArgs e) => Canvas.ResetTransformGizmo();

    private void OnTransformApply(object? sender, RoutedEventArgs e) => CommitTransformFromGizmo();

    private void OnTransformCancel(object? sender, RoutedEventArgs e) => _vm.CancelTransform();

    /// <summary>Right-click on the canvas during a transform: the options menu.</summary>
    private void ShowTransformMenu(Avalonia.Point viewPos)
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var menu = new ContextMenu
        {
            ItemsSource = new Control[]
            {
                Item("Apply transform (Enter)", CommitTransformFromGizmo),
                Item("Cancel (Esc)", _vm.CancelTransform),
                new Separator(),
                Item("Mirror horizontally", () => Canvas.MirrorTransformGizmo(horizontal: true)),
                Item("Mirror vertically", () => Canvas.MirrorTransformGizmo(horizontal: false)),
                new Separator(),
                Item(Canvas.TransformPerspective ? "Box mode (affine)" : "Perspective mode (free corners)",
                    () =>
                    {
                        Canvas.TransformPerspective = !Canvas.TransformPerspective;
                        TransformPerspectiveToggle.IsChecked = Canvas.TransformPerspective;
                    }),
                Item("Reset transform", Canvas.ResetTransformGizmo),
            },
            Placement = PlacementMode.Pointer,
        };
        menu.Open(Canvas);
    }

    private static readonly FilePickerFileType LightboxFileType = new("Lightbox document")
    {
        Patterns = ["*.lightbox.json"],
    };

    /// <summary>
    /// A dot on the track timeline was dragged to a new frame. Track 0 is the
    /// camera when one exists (the projection puts it on top); everything
    /// after maps onto LayerRows in the same order.
    /// </summary>
    private void OnTrackKeyDragged(int trackIndex, int fromFrame, int toFrame)
    {
        var hasCamera = _vm.HasCamera;
        if (hasCamera && trackIndex == 0)
        {
            _vm.MoveCameraKey(fromFrame, toFrame);
            return;
        }
        var rowIndex = trackIndex - (hasCamera ? 1 : 0);
        if (rowIndex < 0 || rowIndex >= _vm.LayerRows.Count) return;
        var row = _vm.LayerRows[rowIndex];
        var from = row.Cells.FirstOrDefault(c => c.Index == fromFrame);
        var to = row.Cells.FirstOrDefault(c => c.Index == toFrame);
        if (from is null || to is null) return;
        _vm.MoveCel(from, to, copy: false);
    }

    /// <summary>
    /// The graph's key menu: how this key eases into the next, and removal.
    /// Built in code because the flyout needs the frame it was asked about.
    /// </summary>
    private void OnGraphKeyMenu(string series, int frame, Avalonia.Point at)
    {
        var current = _vm.CameraKeyEaseAt(frame);
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };
        foreach (var ease in (Lightbox.Core.Inbetween.Easing[])
                 [Lightbox.Core.Inbetween.Easing.Linear, Lightbox.Core.Inbetween.Easing.EaseIn,
                  Lightbox.Core.Inbetween.Easing.EaseOut, Lightbox.Core.Inbetween.Easing.EaseInOut])
        {
            var item = new MenuItem
            {
                Header = ease == current ? $"✓ {ease}" : ease.ToString(),
            };
            var chosen = ease;
            item.Click += (_, _) => _vm.SetCameraKeyEase(frame, chosen);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        var remove = new MenuItem { Header = $"Remove key at {frame + 1}" };
        remove.Click += (_, _) => _vm.RemoveCameraKeyAt(frame);
        flyout.Items.Add(remove);
        flyout.ShowAt(GraphEditorView, showAtPointer: true);
    }

    /// <summary>
    /// Right-clicking a clip bar (Q57): what can be done to a section, at the
    /// playhead. A cut is offered only where one is possible — inside a
    /// section rather than at its edge — and says so when it is not, because a
    /// menu item that silently does nothing teaches an artist to distrust the
    /// menu.
    /// </summary>
    private void OnClipMenu(Controls.ClipBar bar, bool isAudio, Avalonia.Point at)
    {
        var frame = _vm.CurrentFrameIndex;
        var insideSection = frame > bar.Start && frame <= bar.End;
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };

        var split = new MenuItem
        {
            Header = $"Split at frame {frame + 1}",
            IsEnabled = insideSection,
        };
        split.Click += (_, _) =>
        {
            if (isAudio) _vm.SplitAudioAtPlayhead();
            else _vm.SplitVideoAtPlayhead(bar.StripIndex);
        };
        flyout.Items.Add(split);

        if (!insideSection)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Move the playhead inside the clip to cut it",
                IsEnabled = false,
            });
        }
        flyout.ShowAt(TimelineTrackView, showAtPointer: true);
    }
}
