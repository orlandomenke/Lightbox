using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>
/// Part of <see cref="MainWindow"/> — the crop tool's wiring: the canvas's
/// pointer events into the session, and the session's rectangle back to the
/// canvas.
/// </summary>
/// <remarks>
/// The same shape as <c>WireTransformSession</c> and <c>WireGradientRamp</c>,
/// and here for the same reason: the control has the geometry, the view model
/// has the record, and the window is the only thing that knows about both.
/// Nothing decides anything here — <c>CropSession</c> owns the arithmetic and
/// <c>MainViewModel.ApplyCropFrame</c> owns the edit.
/// </remarks>
public partial class MainWindow
{
    private void WireCropTool()
    {
        // Into the session. Each one notifies afterwards rather than the
        // session raising its own event: the session is deliberately a plain
        // object with no notification in it, which is what lets the whole
        // gesture be tested without a window.
        Canvas.CropPressed += (x, y, tolerance) =>
        {
            _vm.CropFrame?.Press(x, y, tolerance);
            _vm.NotifyCropFrame();
        };
        Canvas.CropDragged += (x, y, constrain) =>
        {
            _vm.CropFrame?.Move(x, y, constrain);
            _vm.NotifyCropFrame();
        };
        Canvas.CropReleased += () =>
        {
            _vm.CropFrame?.Release();
            _vm.NotifyCropFrame();
        };

        // And back out to the canvas. One subscription rather than a push at
        // the end of each handler above, so a frame opened by picking the tool
        // or re-opened after an Apply reaches the screen by the same route a
        // dragged one does.
        _vm.CropFrameChanged += SyncCropFrameToCanvas;
    }

    private void SyncCropFrameToCanvas() =>
        Canvas.CropFrameRect = _vm.CropFrame is { } frame
            ? new SKRect((float)frame.Left, (float)frame.Top, (float)frame.Right, (float)frame.Bottom)
            : null;

    /// <summary>
    /// Apply the frame, then refit the view.
    /// </summary>
    /// <remarks>
    /// The refit is <c>ResizeAsync</c>'s, for its reason: the paper is a
    /// different size than the zoom and pan were chosen for. Only on a crop
    /// that changed something — resetting the view to report that nothing
    /// happened would throw away the artist's zoom.
    /// </remarks>
    private void ApplyCropFrame()
    {
        if (_vm.ApplyCropFrame()) Canvas.ResetView();
    }

    /// <summary>Apply, from the options bar's button. Enter takes the same route.</summary>
    internal void ApplyCropFrameFromBar() => ApplyCropFrame();

    /// <summary>Reset the frame to the whole page, from the bar. Escape does the same.</summary>
    internal void CancelCropFrameFromBar() => _vm.CancelCropFrame();
}
