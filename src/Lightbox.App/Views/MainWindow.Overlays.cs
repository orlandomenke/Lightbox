using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// The rig and armature overlays' wiring: pushed snapshots, window-mediated
/// hits, one editor step per gesture. A partial because
/// <c>MainWindow.axaml.cs</c> is on the monolith ratchet.
/// </summary>
public partial class MainWindow
{
    private void WireOverlayGestures()
    {
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
