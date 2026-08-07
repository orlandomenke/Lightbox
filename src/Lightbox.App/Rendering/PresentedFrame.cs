using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The frame the canvas actually draws, held across repaints and patched with
/// only the region a publish changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The problem this solves (B122).</b> Every publish used to hand the render
/// thread a brand-new <see cref="SKImage"/> from <c>Surface.Snapshot()</c>. A
/// GPU-backed canvas cannot recognise a new image as a partially-changed
/// texture, so it uploaded the whole thing — <b>8.3 MB at 1080p, 33.2 MB at
/// 4K</b>, on every pointer event, however small the dab. The compositor was
/// already careful to repaint only the dirty region (that is what
/// <see cref="ComposeRing"/> is for); all of that care was thrown away at the
/// hand-off.
/// </para>
/// <para>
/// So the frame becomes durable. This class owns a surface that lives across
/// repaints, copies in only what changed, and hands back a snapshot of it. When
/// the surface is GPU-backed the win is the whole point: the patch is the only
/// thing that crosses the bus, and the snapshot is already a texture, so drawing
/// it to the screen is a GPU-to-GPU blit rather than an upload.
/// </para>
/// <para>
/// <b>The patch is small by construction, not by hope.</b> It would be tempting
/// to draw the big source image with a source rectangle and trust Skia to upload
/// only that part. Whether it does is an implementation detail of the backend,
/// and this file is not the place to bet on one — so the changed region is
/// copied into an image whose <em>dimensions are the region</em>, and the upload
/// therefore cannot exceed it whatever the backend decides.
/// </para>
/// <para>
/// <b>Correctness rests on never missing a change.</b> A durable frame is only
/// right if every published change reaches it, so two things are not optional:
/// <see cref="Skipped"/> must be told about any frame that is dropped before it
/// is presented (the canvas drops frames under back-pressure), and a change of
/// size or a null region forces a full repaint. Getting this wrong shows stale
/// pixels, which is wrong quietly — the failure this codebase is least able to
/// notice.
/// </para>
/// </remarks>
public sealed class PresentedFrame : IDisposable
{
    /// <summary>
    /// <see cref="Present"/> runs on the render thread; <see cref="Skipped"/> and
    /// <see cref="ForceFull"/> are called from the UI thread when a frame is
    /// dropped. Both touch the owed region, so both are guarded. The critical
    /// sections are short and only ever contended by two threads, and the
    /// alternative — a torn owed rectangle — is a stale-pixel bug.
    /// </summary>
    private readonly object _gate = new();

    private SKSurface? _surface;
    private SKImage? _image;
    private SKImageInfo _info;
    private bool _gpuBacked;

    /// <summary>True when the next present must repaint everything.</summary>
    private bool _needsFull = true;

    /// <summary>Regions from published frames that never reached this surface.</summary>
    private SKRectI? _owed;

    /// <summary>Pixels copied into the surface by the last present. Tests and telemetry.</summary>
    public long LastPatchedPixels { get; private set; }

    /// <summary>Whether the last present had to repaint the whole frame.</summary>
    public bool LastWasFull { get; private set; }

    /// <summary>Whether the durable surface is on the GPU, where the saving actually lands.</summary>
    public bool IsGpuBacked => _gpuBacked;

    /// <summary>
    /// A published frame that will not be presented — its change must still be
    /// owed, or the next patch leaves those pixels stale.
    /// </summary>
    public void Skipped(SKRectI? changed)
    {
        lock (_gate)
        {
            if (changed is null) _needsFull = true;
            else _owed = Union(_owed, changed.Value);
        }
    }

    /// <summary>Repaint everything on the next present.</summary>
    public void ForceFull()
    {
        lock (_gate) _needsFull = true;
    }

    /// <summary>
    /// Patch the durable frame from <paramref name="source"/> and return the
    /// image to draw. <paramref name="changed"/> is in <paramref name="source"/>
    /// pixel space; null means everything changed.
    /// </summary>
    /// <param name="gpu">
    /// The lease's context, or null when the canvas is on software. Null still
    /// works and is still correct — it simply has nothing to save, because there
    /// is no bus to avoid crossing.
    /// </param>
    /// <summary>
    /// The publish this frame is currently showing, so a repaint that carries no
    /// new publish costs nothing.
    /// </summary>
    /// <remarks>
    /// This matters more than it looks. The canvas repaints on <em>pointer move</em>
    /// to redraw the brush ring, which is far more often than the compositor
    /// publishes — so without this guard every hover would re-copy the last
    /// publish's region and snapshot the surface again, doing patch work for a
    /// frame that had not changed. A repaint that only moves the cursor must not
    /// touch the artwork at all.
    /// </remarks>
    private long _presentedSeq = long.MinValue;

    /// <inheritdoc cref="Present(GRContext?, SKImage, SKRectI?, long)"/>
    public SKImage Present(GRContext? gpu, SKImage source, SKRectI? changed) =>
        Present(gpu, source, changed, long.MinValue);

    public SKImage Present(GRContext? gpu, SKImage source, SKRectI? changed, long seq)
    {
        lock (_gate)
        {
            // Same publish as last time and nothing owed: the frame on the surface
            // is already right, so hand it back untouched.
            if (seq != long.MinValue
                && seq == _presentedSeq
                && !_needsFull
                && _owed is null
                && _image is not null
                && _surface is not null)
            {
                LastPatchedPixels = 0;
                LastWasFull = false;
                return _image;
            }
            var image = PresentCore(gpu, source, changed);
            _presentedSeq = seq;
            return image;
        }
    }

    private SKImage PresentCore(GRContext? gpu, SKImage source, SKRectI? changed)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (_surface is null || !_info.Equals(info) || _gpuBacked != (gpu is not null))
        {
            Reset(gpu, info);
        }

        var bounds = new SKRectI(0, 0, info.Width, info.Height);
        SKRectI? region = _needsFull || changed is null
            ? null
            : SKRectI.Intersect(Union(_owed, changed.Value) ?? bounds, bounds);

        // An intersection can come back empty when the change lies entirely
        // outside the frame — nothing to copy, but the frame is still current.
        if (region is { } empty && (empty.Width <= 0 || empty.Height <= 0))
        {
            _owed = null;
            LastPatchedPixels = 0;
            LastWasFull = false;
            return _image ??= _surface!.Snapshot();
        }

        var canvas = _surface!.Canvas;
        if (region is null)
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(source, 0, 0);
            LastPatchedPixels = (long)info.Width * info.Height;
            LastWasFull = true;
        }
        else
        {
            var r = region.Value;
            // The patch's dimensions ARE the region, so the upload is bounded by
            // it no matter what the backend does with source rectangles.
            using var patch = source.Subset(r) ?? source;
            canvas.Save();
            canvas.ClipRect(new SKRect(r.Left, r.Top, r.Right, r.Bottom));
            canvas.Clear(SKColors.Transparent);
            canvas.DrawImage(patch, r.Left, r.Top);
            canvas.Restore();
            LastPatchedPixels = (long)r.Width * r.Height;
            LastWasFull = false;
        }

        canvas.Flush();
        _owed = null;
        _needsFull = false;

        // The old snapshot described the frame before this patch, so it cannot be
        // handed out again. Renders are sequential, so by the time a present runs
        // the previous image is no longer being drawn.
        _image?.Dispose();
        _image = _surface.Snapshot();
        return _image;
    }

    private void Reset(GRContext? gpu, SKImageInfo info)
    {
        _image?.Dispose();
        _image = null;
        _surface?.Dispose();
        _surface = gpu is not null
            ? SKSurface.Create(gpu, false, info)
            : SKSurface.Create(info);
        // A GPU surface can fail to allocate — fall back rather than crash the
        // canvas, because a slow frame beats no frame.
        if (_surface is null)
        {
            _surface = SKSurface.Create(info);
            _gpuBacked = false;
        }
        else
        {
            _gpuBacked = gpu is not null;
        }
        if (_surface is null) throw new InvalidOperationException("Could not create a presentation surface.");
        _info = info;
        _needsFull = true;
        _owed = null;
    }

    private static SKRectI? Union(SKRectI? a, SKRectI b)
    {
        if (a is not { } existing) return b;
        existing.Union(b);
        return existing;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _image?.Dispose();
            _image = null;
            _surface?.Dispose();
            _surface = null;
        }
    }
}
