using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// The rig and armature overlays' wiring: pushed snapshots, window-mediated
/// hits, one editor step per gesture. A partial because
/// <c>MainWindow.axaml.cs</c> is on the monolith ratchet.
/// </summary>
public partial class MainWindow
{
    /// <summary>How near a line a Match lines click has to land, in screen pixels.</summary>
    private const double LineMatchReachPixels = 8;

    /// <summary>A joint of the pose guide is a point: the ten pixels that decide a grip on a point (DESIGN).</summary>
    private const double PoseReachPixels = 10;

    private void WireOverlayGestures()
    {
        WireReferenceSheetGestures();
        WireNavigator();

        // B58. The whole reason the rig was invisible: `RigMarks` existed and
        // nothing ever asked for it. Pushed rather than bound, following `Guides`
        // — the list is a flattened snapshot for the render thread, not a value
        // an artist edits.
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.RigMarks)
                or nameof(MainViewModel.RigEditMode)
                or nameof(MainViewModel.SelectedRigMarkId)
                or nameof(MainViewModel.CurrentFrameIndex))
            {
                RefreshRigOverlay();
            }
        };
        Canvas.RigPressed += (x, y, scale) =>
        {
            var hit = _vm.PressRig(x, y, scale);
            if (hit is { Id: { } id }) Canvas.BeginRigDrag(id, hit.Corner);
            RefreshRigOverlay();
        };
        Canvas.RigDragged += (id, corner, dx, dy) =>
        {
            _vm.DragRig(id, corner, dx, dy);
            RefreshRigOverlay();
        };

        // The motion trail is the simplest push of all: the view model owns
        // every hook that can move a tick, so the window only ferries the
        // list. Refreshed once here so a trail left on last session shows
        // without waiting for the playhead to move.
        _vm.MotionTrailChanged += overlay => Canvas.TrailPoints = overlay;
        _vm.MotionArcChanged += arc => Canvas.MotionArc = arc;

        // Q271: Match lines is pushed the same way, and its presses go straight
        // to the view model, with a reach of a few screen pixels in document units.
        _vm.LineMatchChanged += overlay => Canvas.LineMatch = overlay;
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.LineMatchMode)) Canvas.LineMatchMode = _vm.LineMatchMode;
        };
        Canvas.LineMatchPressed += (x, y, shift, scale) => _vm.LineMatchPress(x, y, shift, LineMatchReachPixels / scale);
        Canvas.LineMatchRightPressed += (x, y, scale) => _vm.LineMatchToggleSolid(x, y, LineMatchReachPixels / scale);
        _vm.RefreshMotionTrail();

        // Q274: the pose guide, pushed the same way. A press takes a joint only
        // while the guide is being edited, so drawing near one never drags it.
        _vm.PoseGuideChanged += guide => Canvas.PoseGuide = guide;
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.PoseEditMode)) Canvas.PoseEditMode = _vm.PoseEditMode;
        };
        Canvas.PoseHit = (x, y, scale) => _vm.PosePress(x, y, PoseReachPixels / scale);
        Canvas.PoseDragged += _vm.PoseDrag;
        Canvas.PoseReleased += _vm.PoseRelease;

        // The armature overlay follows the rig's pattern exactly: pushed
        // snapshots, window-mediated hits, one editor step per gesture.
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.BoneChromes)
                or nameof(MainViewModel.HeatPoints)
                or nameof(MainViewModel.ArmatureEditMode)
                or nameof(MainViewModel.PosingMode)
                or nameof(MainViewModel.SelectedBoneId))
            {
                RefreshArmatureOverlay();
            }
        };
        Canvas.BonePressed += (x, y, scale) =>
        {
            var hit = _vm.PressArmature(x, y, scale);
            if (hit is { Id: { } id }) Canvas.BeginBoneDrag(id, hit.Grab);
            RefreshArmatureOverlay();
        };
        // Both halves of the gesture go through the view model's one
        // dispatch (MainViewModel.BoneGesture.cs): the drag previews, the
        // release lands the editor step. The window only relays.
        Canvas.BoneDragged += (id, grab, x0, y0, x, y, extruding) =>
        {
            _vm.PreviewBoneGesture(id, grab, x0, y0, x, y, extruding);
            RefreshArmatureOverlay();
        };
        Canvas.BoneGestureEnded += (id, grab, x0, y0, x1, y1, extruding) =>
        {
            _vm.EndBoneGesture(id, grab, x0, y0, x1, y1, extruding);
            RefreshArmatureOverlay();
        };
        Canvas.BoneGestureCancelled += () =>
        {
            _vm.ClearBoneGesturePreview();
            RefreshArmatureOverlay();
        };
        // The bucket's and the wand's hover preview: traced in the view
        // model by the same functions the click runs, drawn by the canvas
        // as chrome.
        _vm.FillPreviewChanged += (contours, wand, hex) => Canvas.SetFillPreview(contours, wand, hex);
        Canvas.WeightStrokeStarted += (x, y, p) => _vm.BeginWeightStroke(x, y, p);
        Canvas.WeightDabbed += (x, y, p) =>
        {
            _vm.WeightDab(x, y, p);
            RefreshArmatureOverlay();
        };
        Canvas.WeightStrokeEnded += () =>
        {
            _vm.EndWeightStroke();
            RefreshArmatureOverlay();
        };
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.WeightPainting))
                Canvas.WeightPainting = _vm.WeightPainting;
        };
    }
}
