using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// A published frame must stay alive until the compositor has finished
/// drawing it.
///
/// The canvas frees old snapshots once a newer one has been rendered, which
/// is sound — renders are sequential. It also had a hard cap that freed the
/// oldest unconditionally, to stop the queue growing forever when nothing
/// ever renders. That cap was safe only while publishes never outran renders.
/// Live wet-media previews made them outrun renders, the cap freed an image
/// the compositor was mid-draw on, and the process died inside
/// <c>sk_canvas_draw_image_rect</c> with an access violation.
///
/// Bounded memory is not worth a crash: if everything queued is still in
/// flight, the queue grows.
/// </summary>
[Collection("BrushState")]
public class SnapshotRetirementTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Retire", 200, 150, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.BrushSize = 16;
        return vm;
    }

    [AvaloniaFact]
    public void PublishesThatOutrunTheRenderer_NeverFreeAnImageStillInFlight()
    {
        // The canvas here never reports a render back, which is exactly the
        // state a hidden or lagging window is in. Every snapshot it was handed
        // must still be usable afterwards.
        var vm = Vm();
        var canvas = new CanvasControl();
        var published = 0;
        var accepted = new List<RenderSnapshot>();
        vm.SnapshotChanged += s =>
        {
            published++;
            // Dropping an incoming frame is fine — the renderer never saw it.
            // Freeing one the canvas already took is the access violation.
            if (canvas.UpdateSnapshot(s)) accepted.Add(s);
        };

        vm.BeginStroke(20, 75, 1);
        for (var x = 30; x <= 180; x += 5)
        {
            vm.MoveStroke(x, 75, 1);
            Dispatcher.UIThread.RunJobs();
        }
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        Assert.True(published > 8, $"only {published} snapshots — the cap was never reached");
        // Nothing ever reported a completed render, so not one accepted frame
        // may have been freed. A disposed SKImage has a zero handle, and
        // drawing one is the crash.
        var dead = accepted.Count(s => s.Image.Handle == IntPtr.Zero);
        Assert.True(dead == 0,
            $"{dead} of {accepted.Count} accepted frames were freed while still unrendered");
        // And the queue is bounded rather than growing without limit: the
        // canvas drops incoming frames instead of freeing held ones.
        Assert.True(canvas.RetiredCount <= 8,
            $"{canvas.RetiredCount} frames queued — back-pressure is not holding");
    }

    [AvaloniaFact]
    public void OnceRenderedTheOldFramesAreStillReleased()
    {
        // The other half: the cap exists for a reason, and a canvas that does
        // report back must not accumulate full-canvas images forever.
        var vm = Vm();
        var canvas = new CanvasControl();
        var handed = new List<RenderSnapshot>();
        vm.SnapshotChanged += s =>
        {
            handed.Add(s);
            canvas.UpdateSnapshot(s);
            // Pretend the compositor drew it immediately.
            canvas.NoteRenderedForTest(s.Seq);
        };

        vm.BeginStroke(20, 75, 1);
        for (var x = 30; x <= 180; x += 5)
        {
            vm.MoveStroke(x, 75, 1);
            Dispatcher.UIThread.RunJobs();
        }
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        Assert.True(handed.Count > 8, $"only {handed.Count} snapshots");
        Assert.True(canvas.HeldImagesAlive, "a held image was freed even though it was rendered in order");
        // Everything but the most recent few should be gone.
        var alive = handed.Count(s => s.Image.Handle != IntPtr.Zero);
        Assert.True(alive <= 6, $"{alive} of {handed.Count} images are still held after being rendered");
    }
}
