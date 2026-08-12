using System.Collections.Concurrent;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Frees GPU-backed frame images where the graphics context lives, instead of
/// where the retirement queue happens to run.
/// </summary>
/// <remarks>
/// <para>
/// <b>B179's fix, and the mechanism deserves spelling out because every number
/// in the investigation pointed away from the caches and toward this.</b> A
/// composed frame on the GPU route is an <see cref="SKImage"/> snapshotted from
/// an unbudgeted <see cref="SKSurface"/>; the surface is disposed immediately,
/// so the image owns the render target — 7.9 MB at a 1080p compose, 31.6 MB at
/// 4K. The retirement queue disposes that image on the <em>UI</em> thread. But
/// the <c>GRContext</c> lives on the render thread, inside the lease, and a GPU
/// resource released off the context's thread is not freed — it is parked on
/// the context's deferred-free queue, waiting for someone to drain it. Nothing
/// did. So every publish leaked one render target: invisible to Skia's cache
/// (unbudgeted, by design — see <see cref="GpuComposite.Budgeted"/>), invisible
/// to ours (already "disposed"), and growing at exactly the rate the captures
/// measured — 12 GB over a long 4K session, flat the moment the toggle went off.
/// </para>
/// <para>
/// <b>The discriminating experiment confirmed the surfaces and refused the easy
/// fix in the same run.</b> With <c>LIGHTBOX_GPU_BUDGETED=1</c> memory climbed,
/// then <em>dropped</em> — Skia purging the surfaces it was finally allowed to
/// account for, which is what proves they were the growth — and then the app
/// froze and crashed: a purged surface recycled underneath a snapshot still
/// being read, which is B130's use-after-free arriving exactly as the
/// <c>budgeted: false</c> comment predicted. So the surfaces must stay
/// unbudgeted, and the freeing must be ours.
/// </para>
/// <para>
/// <b>Hence this class: dispose is deferred to the draw op.</b>
/// <see cref="RenderSnapshot.Dispose"/> hands a GPU-backed image here instead
/// of disposing it; the draw op calls <see cref="Drain"/> once per frame, on
/// the render thread, with the lease's context current, where the release
/// actually reaches the driver. The B130 guarantee is untouched — a snapshot
/// is only retired after a newer frame has rendered, and the reaper only adds
/// delay after that.
/// </para>
/// <para>
/// <b><see cref="Drain"/> also asks the context to sweep its deferred-free
/// queue</b> (<see cref="GRContext.PurgeUnusedResources(long)"/>, Skia's
/// <c>performDeferredCleanup</c>), because this class only reroutes the frame
/// images — anything else released off-thread, a resident texture evicted on
/// the UI thread for instance, parks on the same queue. The two-second grace
/// keeps recently-used scratch alive so the sweep cannot cost the next frame
/// its allocations.
/// </para>
/// </remarks>
internal static class GpuImageReaper
{
    private static readonly ConcurrentQueue<SKImage> Queue = new();
    private static int _pending;
    private static long _reaped;

    /// <summary>
    /// If the queue ever holds this many images, something stopped draining —
    /// a session that lost its GPU lease and never drew again. Disposing inline
    /// past the cap trades "freed on the wrong thread, maybe parked" for
    /// "growing without bound", which is the right trade at frame 512.
    /// </summary>
    private const int Cap = 512;

    /// <summary>Images freed on the render thread so far (for the report).</summary>
    internal static long Reaped => Interlocked.Read(ref _reaped);

    /// <summary>Images waiting for the next drain (for the report).</summary>
    internal static int PendingCount => Volatile.Read(ref _pending);

    /// <summary>
    /// Hand over an image to be freed on the render thread. Called by
    /// <see cref="RenderSnapshot.Dispose"/> for GPU-backed frames only — a CPU
    /// image gains nothing from the detour and disposes where it always did.
    /// </summary>
    internal static void Enqueue(SKImage image)
    {
        if (Volatile.Read(ref _pending) >= Cap)
        {
            image.Dispose();
            return;
        }
        Queue.Enqueue(image);
        Interlocked.Increment(ref _pending);
    }

    /// <summary>
    /// Free everything queued. Call from the draw op, on the render thread,
    /// with the lease's context — that is the whole point; see the class
    /// remarks. A null context still disposes (software fallback, shutdown):
    /// worse than freeing with the context current, better than keeping them.
    /// </summary>
    internal static void Drain(GRContext? gpu)
    {
        var any = false;
        while (Queue.TryDequeue(out var image))
        {
            Interlocked.Decrement(ref _pending);
            image.Dispose();
            Interlocked.Increment(ref _reaped);
            any = true;
        }

        // Sweep the context's own deferred-free queue too — the release above
        // may only have parked the resource there, and so does anything else
        // disposed off this thread. Cheap against the composite this frame just
        // paid for, and the grace period keeps warm scratch out of the sweep.
        if (any && gpu is not null)
        {
            gpu.PurgeUnusedResources(2000);
        }
    }

    /// <summary>Restore the pristine state between tests.</summary>
    internal static void ResetForTests()
    {
        while (Queue.TryDequeue(out var image))
        {
            Interlocked.Decrement(ref _pending);
            image.Dispose();
        }
        Interlocked.Exchange(ref _reaped, 0);
    }
}
