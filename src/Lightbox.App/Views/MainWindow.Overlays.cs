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
        Canvas.BoneGestureEnded += (id, grab, x0, y0, x1, y1, extruding) =>
        {
            if (id is null)
            {
                // An empty-canvas drag creates a bone — in bind mode only;
                // posing an empty spot means nothing and writes nothing.
                if (!_vm.PosingMode) _vm.CreateBoneFromDrag(x0, y0, x1, y1);
            }
            else if (_vm.PosingMode)
            {
                _vm.PoseBoneTo(id, x1, y1);
            }
            else if (extruding && grab is Rendering.BoneGrab.Tip)
            {
                // Blender's idiom: take hold of the tip and pull a child out
                // of it, already joined to the parent.
                _vm.ExtrudeChildFrom(id, x1, y1);
            }
            else if (grab is Rendering.BoneGrab.Origin or Rendering.BoneGrab.Tip)
            {
                _vm.DragBoneBind(id, grab, x1, y1);
            }
            else
            {
                // The shaft moves the whole bone, children and all. It used to
                // select and do nothing, which is what "no option to move bones
                // around" meant: the only thing that moved one was the joint
                // handle, five screen pixels wide.
                _vm.MoveBoneBy(id, x1 - x0, y1 - y0);
            }
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
