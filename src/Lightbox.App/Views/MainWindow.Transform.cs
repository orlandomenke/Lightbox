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
    // ---- transform session (window side) --------------------------------------

    /// <summary>Transform session: the VM owns the frames, the canvas owns the gizmo.</summary>
    private void WireTransformSession()
    {
        // The timeline's menu and fold events, wired beside their handlers —
        // the shared wiring block in MainWindow.axaml.cs is at its ratchet.
        TimelineTrackView.TrackAreaMenuRequested += OnTrackAreaMenu;
        TimelineTrackView.FoldToggleRequested += _vm.ToggleTrackFold;

        _vm.TransformBegun += (minX, minY, maxX, maxY) =>
        {
            Canvas.BeginTransformGizmo(minX, minY, maxX, maxY);
            Canvas.ToolMode = Rendering.CanvasControl.CanvasToolMode.Transform;
        };
        _vm.TransformEnded += () =>
        {
            Canvas.EndTransformGizmo();
            TransformPerspectiveToggle.IsChecked = false; // gizmo resets per session
            SyncCanvasToolMode();
        };
        // The gizmo is the authority on the shape of the drag; the view model
        // owns the pixels. Feeding the matrix across on every gizmo change is
        // what makes the drawing move with the box instead of after it.
        Canvas.TransformGizmoChanged += () => _vm.PreviewTransform(Canvas.TransformMatrix);
        // The ants ride the same matrix the moving pixels composite through,
        // so the outline follows the drag instead of catching up on release.
        _vm.TransformPreviewChanged += Canvas.SetSelectionPreviewTransform;
        Canvas.TransformMenuRequested += ShowTransformMenu;
    }

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

    /// <summary>Edit ▸ Transform: the same session Ctrl+T begins.</summary>
    private void OnMenuBeginTransform(object? sender, RoutedEventArgs e)
    {
        if (!_vm.TransformActive) _vm.BeginTransform();
    }

    /// <summary>
    /// Edit ▸ Transform ▸ Perspective: the gizmo context menu's toggle, kept in
    /// step with the tool-options ToggleButton the same way that menu is.
    /// </summary>
    private void OnMenuTransformPerspective(object? sender, RoutedEventArgs e)
    {
        Canvas.TransformPerspective = !Canvas.TransformPerspective;
        TransformPerspectiveToggle.IsChecked = Canvas.TransformPerspective;
    }

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
    /// camera when one exists (the projection puts it on top), the armature
    /// and its bones come next, and everything after maps onto LayerRows in
    /// the same order.
    /// </summary>
    /// <remarks>
    /// The row arithmetic lives in the view model (<c>TracksAboveLayers</c>,
    /// <c>IsPoseTrack</c>, <c>BoneOfTrack</c>) rather than here, because the
    /// same three questions are asked again by the key menu below and a second
    /// count that disagreed would retime the wrong track.
    /// </remarks>
    /// <summary>
    /// A key was dragged along its row. Retimes the whole selection when the
    /// grabbed key is in it, and that key alone when it is not.
    /// </summary>
    /// <remarks>
    /// The three kinds no longer route to three methods here. Retiming is the
    /// one verb camera keys, pose keys and cels share, so it is one call — and
    /// it has to be, because a mixed selection cannot be moved by a branch that
    /// picks a single kind from the row it started on. See
    /// <c>MainViewModel.RetimeSelection</c>.
    /// </remarks>
    private void OnTrackKeyDragged(int trackIndex, int fromFrame, int toFrame)
    {
        if (_vm.TrackKeyAt(trackIndex, fromFrame) is not { } grabbed) return;
        _vm.RetimeSelection(grabbed, toFrame - fromFrame);
    }

    /// <summary>A modified click on a key: Ctrl adds or drops, Shift ranges.</summary>
    private void OnTrackKeySelect(int trackIndex, int frame, bool toggle, bool range)
    {
        if (_vm.TrackKeyAt(trackIndex, frame) is not { } key) return;
        _vm.SelectTrackKey(key, toggle, range);
    }

    /// <summary>
    /// A right-click on a dot: the key menu, on every kind of row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copy and cut cross kinds because they are the clipboard's face of
    /// retiming — "this beat, elsewhere" — and cover the selection when the
    /// clicked key is in it. Delete stays per kind: removing a camera key,
    /// unkeying a bone and clearing a drawing leave different things behind,
    /// so each row offers its own and leaves the others' keys alone.
    /// </para>
    /// <para>
    /// This used to answer only the armature's rows; the owner asked for a
    /// timeline right-click menu outright (2026-08-21), and a menu that
    /// worked on one row in three read as broken rather than as restrained.
    /// </para>
    /// </remarks>
    private void OnTrackKeyMenu(int trackIndex, int frame, Avalonia.Point at)
    {
        if (_vm.TrackKeyAt(trackIndex, frame) is not { } clicked) return;
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };

        var count = _vm.IsTrackKeySelected(clicked) ? _vm.KeySelection.Count : 1;
        var what = count > 1 ? $"{count} keys" : "key";
        var copy = new MenuItem { Header = $"Copy {what}" };
        copy.Click += (_, _) => _vm.CopyTimelineKeys(clicked);
        flyout.Items.Add(copy);
        var cut = new MenuItem { Header = $"Cut {what}" };
        cut.Click += (_, _) => _vm.CutTimelineKeys(clicked);
        flyout.Items.Add(cut);
        flyout.Items.Add(new Separator());

        switch (clicked.Kind)
        {
            case TimelineKeyKind.Camera:
            {
                // The graph editor's easing menu, where the key actually is.
                var ease = new MenuItem { Header = "Ease into next" };
                var current = _vm.CameraKeyEaseAt(frame);
                foreach (var choice in (Lightbox.Core.Inbetween.Easing[])
                         [Lightbox.Core.Inbetween.Easing.Linear, Lightbox.Core.Inbetween.Easing.EaseIn,
                          Lightbox.Core.Inbetween.Easing.EaseOut, Lightbox.Core.Inbetween.Easing.EaseInOut])
                {
                    var item = new MenuItem { Header = choice == current ? $"✓ {choice}" : choice.ToString() };
                    var chosen = choice;
                    item.Click += (_, _) => _vm.SetCameraKeyEase(frame, chosen);
                    ease.Items.Add(item);
                }
                flyout.Items.Add(ease);
                var remove = new MenuItem { Header = "Remove camera key" };
                remove.Click += (_, _) => _vm.RemoveCameraKeyAt(frame);
                flyout.Items.Add(remove);
                break;
            }
            case TimelineKeyKind.Pose:
            {
                var bone = clicked.BoneId;
                var pose = _vm.SelectedPoseKeys(clicked);
                var delete = new MenuItem
                {
                    Header = pose.Count > 1
                        ? $"Delete {pose.Count} pose keys"
                        : bone is null ? "Delete this pose key" : "Unkey this bone here",
                };
                delete.Click += (_, _) =>
                {
                    foreach (var k in pose.OrderByDescending(k => k.Frame)) _vm.DeletePoseKey(k.BoneId, k.Frame);
                };
                flyout.Items.Add(delete);
                break;
            }
            default:
            {
                var clear = new MenuItem { Header = "Clear drawing (keep the timing)" };
                clear.Click += (_, _) =>
                {
                    if (TrackCellAt(trackIndex, frame) is { } cell) _vm.ClearCelAt(cell);
                };
                flyout.Items.Add(clear);
                break;
            }
        }

        flyout.Items.Add(new Separator());
        var goThere = new MenuItem { Header = $"Go to frame {frame + 1}" };
        goThere.Click += (_, _) => _vm.CurrentFrameIndex = frame;
        flyout.Items.Add(goThere);
        flyout.ShowAt(TimelineTrackView, showAtPointer: true);
    }

    /// <summary>
    /// A right-click on a track's empty run: the verbs that need a frame
    /// rather than a key — paste, keying here, the playback range.
    /// </summary>
    private void OnTrackAreaMenu(int trackIndex, int frame, Avalonia.Point at)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };

        var paste = new MenuItem
        {
            Header = $"Paste keys at frame {frame + 1}",
            IsEnabled = _vm.HasTimelineKeyClipboard,
        };
        paste.Click += (_, _) => _vm.PasteTimelineKeysAt(frame);
        flyout.Items.Add(paste);

        // Keying belongs to the row the pointer is on — a camera key from the
        // camera's row, a pose key from the armature's. Both key what is
        // already interpolated there, so nothing jumps.
        if (_vm.TrackKeyAt(trackIndex, frame) is { Kind: TimelineKeyKind.Camera })
        {
            var keyHere = new MenuItem { Header = $"Key the camera at frame {frame + 1}" };
            keyHere.Click += (_, _) => _vm.AddCameraKeyAt(frame);
            flyout.Items.Add(keyHere);
        }
        else if (_vm.IsPoseTrack(trackIndex))
        {
            var keyHere = new MenuItem { Header = $"Key the pose at frame {frame + 1}" };
            keyHere.Click += (_, _) => _vm.AddPoseKeyAt(frame);
            flyout.Items.Add(keyHere);
        }

        flyout.Items.Add(new Separator());
        var start = new MenuItem { Header = $"Playback starts at frame {frame + 1}" };
        start.Click += (_, _) => _vm.SetPlaybackStartAt(frame);
        flyout.Items.Add(start);
        var end = new MenuItem { Header = $"Playback ends at frame {frame + 1}" };
        end.Click += (_, _) => _vm.SetPlaybackEndAt(frame);
        flyout.Items.Add(end);

        flyout.Items.Add(new Separator());
        var goThere = new MenuItem { Header = $"Go to frame {frame + 1}" };
        goThere.Click += (_, _) => _vm.CurrentFrameIndex = frame;
        flyout.Items.Add(goThere);
        flyout.ShowAt(TimelineTrackView, showAtPointer: true);
    }

    /// <summary>The X-sheet cell a layer track's (row, frame) stands for, or null.</summary>
    private FrameCell? TrackCellAt(int trackIndex, int frame)
    {
        var rowIndex = trackIndex - _vm.TracksAboveLayers;
        if (rowIndex < 0 || rowIndex >= _vm.LayerRows.Count) return null;
        return _vm.LayerRows[rowIndex].Cells.FirstOrDefault(c => c.Index == frame);
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
