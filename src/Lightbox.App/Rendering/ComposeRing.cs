using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The compositing back-buffers, rotated so a surface is never redrawn while
/// the render thread still holds a snapshot of it.
///
/// Two costs dominate large-document painting and both are avoided here:
/// <list type="bullet">
/// <item>Skia's raster surfaces are copy-on-write: drawing into a surface
/// whose snapshot is still referenced duplicates the WHOLE pixel buffer
/// (~375 ms at 4K). Rotating surfaces means we only ever draw into one whose
/// snapshot has already been retired.</item>
/// <item>A full recomposite costs one full-canvas blit per layer. Most
/// publishes during a stroke change only a small region, so each surface
/// tracks the region that went stale since IT was last drawn, and only that
/// gets repainted.</item>
/// </list>
///
/// The second point has a sting in its tail that cost the first three events
/// of every stroke ~80 ms at 4K: an idle buffer accumulates the UNION of every
/// region that changed while it sat out, so the first publish to land on it
/// after a stroke crossing the canvas had to recomposite that whole bounding
/// box. The buffers are therefore brought back into line by <em>copying</em>
/// the freshly composited pixels forward (see <see cref="CopyForward"/>)
/// instead of recompositing them: one region blit rather than one blit per
/// layer plus the background clear, and it happens while the region is still
/// only as big as a dab.
/// </summary>
public sealed class ComposeRing : IDisposable
{
    private sealed class Buffer
    {
        public SKSurface? Surface;

        /// <summary>Region that changed since this buffer was last painted; null = everything.</summary>
        public SKRectI? Stale;

        /// <summary>True when this buffer has never been painted (or was invalidated).</summary>
        public bool NeedsFull = true;

        /// <summary>The snapshot handed out last time. Drawing while it lives forces a copy.</summary>
        public SKImage? LastImage;

        public bool IsFree => LastImage is null || LastImage.Handle == IntPtr.Zero;
    }

    // Three buffers: the render thread realistically holds the current image
    // and at most one in flight, so the third is always free to draw into.
    private readonly Buffer[] _buffers = [new(), new(), new()];
    private int _next;
    private SKImageInfo _info;

    /// <summary>The buffer painted most recently — a correct composite, and so the source every catch-up copies from.</summary>
    private Buffer? _current;

    /// <summary>Bytes of back-buffer memory currently allocated.</summary>
    public long AllocatedBytes
    {
        get
        {
            long count = _buffers.Count(b => b.Surface is not null);
            return count * _info.Width * _info.Height * 4L;
        }
    }

    /// <summary>
    /// Paint and hand out an immutable image. <paramref name="dirty"/> is the
    /// document region that changed since the previous publish (null = the
    /// whole canvas); <paramref name="paint"/> receives the clip to honour.
    /// <paramref name="regionScale"/> maps those document coordinates onto the
    /// surface, which is smaller than the document when the canvas cannot show
    /// full detail — it must match the scale <paramref name="paint"/> composes
    /// at, or the copy-forward would move the wrong pixels.
    /// </summary>
    public SKImage Publish(
        SKImageInfo info,
        SKRectI? dirty,
        Action<SKSurface, SKRectI?> paint,
        double regionScale = 1.0)
    {
        if (!_info.Equals(info)) Reset(info);

        // A full-canvas publish repaints its own buffer and invalidates the
        // rest, so catching anything up first would only be thrown away.
        if (dirty is not null) CatchUpIdleBuffers(regionScale);

        var buffer = PickBuffer();

        buffer.Surface ??= SKSurface.Create(_info)
            ?? throw new InvalidOperationException("Could not create compose surface.");

        // This buffer repaints its own stale region plus whatever changed in
        // this publish; the others just accumulate this publish's change.
        // A null dirty means "everything changed" and forces a full repaint;
        // a null Stale means "nothing outstanding", which is not the same.
        // The catch-up above means Stale is normally already null here.
        SKRectI? region = dirty is null || buffer.NeedsFull
            ? null
            : Union(buffer.Stale, dirty.Value);
        paint(buffer.Surface, region);
        buffer.Stale = null;
        buffer.NeedsFull = false;

        foreach (var other in _buffers)
        {
            if (ReferenceEquals(other, buffer)) continue;
            if (dirty is null) other.NeedsFull = true;
            else other.Stale = Union(other.Stale, dirty.Value);
        }

        var image = buffer.Surface.Snapshot();
        buffer.LastImage = image;
        _current = buffer;
        return image;
    }

    /// <summary>
    /// Bring the buffers that sat out back into line by <em>copying</em> the
    /// last composite into them, rather than leaving them to recomposite
    /// everything that changed while they were idle.
    ///
    /// Only a buffer whose own snapshot has been retired may be written to —
    /// drawing into one that is still referenced is the copy-on-write
    /// duplication this class exists to avoid — so a buffer still on screen
    /// keeps accumulating, and is caught up on the first publish after it
    /// comes free. Because this runs before the buffer for this publish is
    /// chosen, that catch-up lands here rather than in the paint below.
    /// </summary>
    private void CatchUpIdleBuffers(double scale)
    {
        if (_current?.Surface is not { } source) return;

        var behind = false;
        foreach (var other in _buffers)
        {
            if (Behind(other)) { behind = true; break; }
        }
        if (!behind) return;

        // Cheap while nothing draws into the source: Skia hands back the
        // existing pixels and only duplicates them if the surface is painted
        // while a snapshot lives, which is why this is disposed before
        // PickBuffer can choose the source.
        using var fresh = source.Snapshot();
        if (fresh is null) return;

        foreach (var other in _buffers)
        {
            if (!Behind(other)) continue;
            CopyForward(other.Surface!, fresh, other.NeedsFull ? null : other.Stale, scale);
            other.Stale = null;
            other.NeedsFull = false;
        }

        bool Behind(Buffer b) =>
            !ReferenceEquals(b, _current)
            && b.Surface is not null
            && b.IsFree
            && (b.NeedsFull || b.Stale is not null);
    }

    /// <summary>
    /// Replace <paramref name="region"/> of an idle buffer with the last
    /// composited pixels. Src rather than SrcOver: the composite may be
    /// transparent where the document is, and blending would leave the old
    /// contents showing through.
    /// </summary>
    private static void CopyForward(SKSurface target, SKImage fresh, SKRectI? region, double scale)
    {
        var canvas = target.Canvas;
        canvas.Save();
        canvas.ResetMatrix();
        if (region is { } r)
        {
            // The same mapping ComposeInto applies to its clip, so a rect that
            // was repainted there is the rect that gets copied here.
            canvas.ClipRect(SKRect.Create(
                (float)Math.Floor(r.Left * scale),
                (float)Math.Floor(r.Top * scale),
                (float)Math.Ceiling(r.Width * scale) + 1,
                (float)Math.Ceiling(r.Height * scale) + 1));
        }
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawImage(fresh, 0, 0, paint);
        canvas.Restore();
    }

    /// <summary>
    /// Prefer a buffer whose previous snapshot has been released — drawing
    /// into one that is still referenced makes Skia duplicate the entire
    /// pixel buffer. If every buffer is still in use (a consumer holding
    /// several frames), fall back to round-robin: correct, just slower.
    /// </summary>
    private Buffer PickBuffer()
    {
        for (var i = 0; i < _buffers.Length; i++)
        {
            var index = (_next + i) % _buffers.Length;
            if (!_buffers[index].IsFree) continue;
            _next = (index + 1) % _buffers.Length;
            return _buffers[index];
        }
        var fallback = _buffers[_next];
        _next = (_next + 1) % _buffers.Length;
        return fallback;
    }

    /// <summary>Force the next publish of every buffer to repaint in full.</summary>
    public void InvalidateAll()
    {
        foreach (var buffer in _buffers)
        {
            buffer.NeedsFull = true;
            buffer.Stale = null;
        }
        // Every buffer is wrong now, including the one a catch-up would copy
        // from — dropping it is what stops the copy from re-certifying stale
        // pixels as current.
        _current = null;
    }

    private void Reset(SKImageInfo info)
    {
        foreach (var buffer in _buffers)
        {
            buffer.Surface?.Dispose();
            buffer.Surface = null;
            buffer.NeedsFull = true;
            buffer.Stale = null;
        }
        _info = info;
        _next = 0;
        _current = null;
    }

    /// <summary>Grow an outstanding stale region by another one (null = none yet).</summary>
    private static SKRectI Union(SKRectI? a, SKRectI b)
    {
        if (a is not { } r) return b;
        r.Union(b);
        return r;
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers) buffer.Surface?.Dispose();
    }
}
