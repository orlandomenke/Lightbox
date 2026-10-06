namespace Lightbox.App.Views;

/// <summary>
/// The window side of symmetry painting: the axis crosses to the canvas as
/// view-only chrome, and the gizmo's drags come back as view-model edits.
/// </summary>
/// <remarks>
/// Its own file for the monolith ratchet's reason — <c>MainWindow.axaml.cs</c>
/// may not grow — and because the pattern is the camera's exactly: a clone
/// crosses, never the view model's object, and the canvas asks for changes
/// through events rather than reaching into the document.
/// </remarks>
public partial class MainWindow
{
    private void WireSymmetry()
    {
        _vm.SymmetryChanged += () => Canvas.Symmetry = _vm.ActiveSymmetry?.Clone();
        Canvas.Symmetry = _vm.ActiveSymmetry?.Clone();
        Canvas.SymmetryDragStarted += () => _vm.BeginSymmetryDrag();
        Canvas.SymmetryCentreDragged += (dx, dy) => _vm.DragSymmetryCentreBy(dx, dy);
        Canvas.SymmetryAngleDragged += (x, y, snap) => _vm.DragSymmetryAngleTowards(x, y, snap);
        Canvas.SymmetryDragEnded += () => _vm.EndSymmetryDrag();

        // Seamless tiles: the neighbours around the page are chrome, so the
        // flag crosses the same way the axis does.
        _vm.TileWrapChanged += () => Canvas.TiledPreview = _vm.TileWrapEnabled;
        Canvas.TiledPreview = _vm.TileWrapEnabled;
    }
}
