using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B179's fix: a GPU-backed frame image is freed on the render thread, where
/// the graphics context lives, not on the UI thread where the retirement queue
/// runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>What these tests can and cannot prove, stated up front.</b> The leak was
/// a GPU render target parked by an off-thread release, and this repository has
/// no graphics context — so no test here can watch driver memory fall. What
/// they pin instead is the routing: a GPU-backed snapshot's image goes to the
/// reaper and survives until a drain; a CPU one disposes exactly where it
/// always did; the drain frees everything and counts it. The measurement that
/// closes the loop is the owner's next capture, whose report now prints the
/// reaper's two numbers.
/// </para>
/// <para>
/// The images used are raster — the queue and the counters cannot tell, and a
/// raster image's <c>Handle</c> going to zero on dispose is the same observable
/// either way.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class GpuImageReaperTests : BrushStateIsolated, IDisposable
{
    public GpuImageReaperTests() => GpuImageReaper.ResetForTests();

    public new void Dispose()
    {
        GpuImageReaper.ResetForTests();
        base.Dispose();
    }

    private static SKImage Frame()
    {
        using var surface = SKSurface.Create(new SKImageInfo(8, 8));
        surface.Canvas.Clear(SKColors.Coral);
        return surface.Snapshot();
    }

    /// <summary>
    /// The core of the fix: enqueue does not free, drain does. An image freed
    /// at enqueue time would be the old behaviour with extra steps.
    /// </summary>
    [AvaloniaFact]
    public void AnEnqueuedImageLivesUntilTheDrain()
    {
        var image = Frame();

        GpuImageReaper.Enqueue(image);

        Assert.Equal(1, GpuImageReaper.PendingCount);
        Assert.NotEqual(IntPtr.Zero, image.Handle);

        GpuImageReaper.Drain(gpu: null);

        Assert.Equal(0, GpuImageReaper.PendingCount);
        Assert.Equal(1, GpuImageReaper.Reaped);
        Assert.Equal(IntPtr.Zero, image.Handle);
    }

    /// <summary>
    /// A GPU-backed snapshot routes its image to the reaper on dispose; the
    /// image is gone from the snapshot either way — <c>IsDisposed</c> holds and
    /// the B130 guard still reads this snapshot as freed.
    /// </summary>
    [AvaloniaFact]
    public void AGpuBackedSnapshotHandsItsImageToTheReaper()
    {
        var image = Frame();
        var snapshot = new RenderSnapshot(image, 8, 8);
        snapshot.MarkGpuBackedForTests();

        snapshot.Dispose();

        Assert.True(snapshot.IsDisposed);
        Assert.Null(snapshot.Image);
        Assert.Equal(1, GpuImageReaper.PendingCount);
        Assert.NotEqual(IntPtr.Zero, image.Handle);

        GpuImageReaper.Drain(gpu: null);
        Assert.Equal(IntPtr.Zero, image.Handle);
    }

    /// <summary>
    /// A CPU snapshot never takes the detour. The reaper exists because GPU
    /// releases need the context's thread; a raster image freed a frame late
    /// would be pure added latency for the ordinary path.
    /// </summary>
    [AvaloniaFact]
    public void ACpuSnapshotDisposesWhereItAlwaysDid()
    {
        var image = Frame();
        var snapshot = new RenderSnapshot(image, 8, 8);

        snapshot.Dispose();

        Assert.Equal(0, GpuImageReaper.PendingCount);
        Assert.Equal(IntPtr.Zero, image.Handle);
    }

    /// <summary>
    /// Disposing twice reaps once. The retirement queue frees a snapshot from
    /// three sites, and an image enqueued twice would be a double dispose on
    /// the render thread — the exact class of crash this machinery exists to
    /// avoid, moved rather than fixed.
    /// </summary>
    [AvaloniaFact]
    public void DisposingTwiceEnqueuesOnce()
    {
        var snapshot = new RenderSnapshot(Frame(), 8, 8);
        snapshot.MarkGpuBackedForTests();

        snapshot.Dispose();
        snapshot.Dispose();

        Assert.Equal(1, GpuImageReaper.PendingCount);
    }

    /// <summary>
    /// The cap is the fallback for a session that stops drawing: past it,
    /// images dispose inline rather than queue without bound. Freed on the
    /// wrong thread beats never freed — that trade is the whole bug.
    /// </summary>
    [AvaloniaFact]
    public void PastTheCapItDisposesInlineRatherThanQueueing()
    {
        for (var i = 0; i < 512; i++)
        {
            GpuImageReaper.Enqueue(Frame());
        }
        Assert.Equal(512, GpuImageReaper.PendingCount);

        var overflow = Frame();
        GpuImageReaper.Enqueue(overflow);

        Assert.Equal(512, GpuImageReaper.PendingCount);
        Assert.Equal(IntPtr.Zero, overflow.Handle);
    }
}
