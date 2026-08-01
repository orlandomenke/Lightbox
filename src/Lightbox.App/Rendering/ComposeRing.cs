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
    /// </summary>
    public SKImage Publish(SKImageInfo info, SKRectI? dirty, Action<SKSurface, SKRectI?> paint)
    {
        if (!_info.Equals(info)) Reset(info);

        var buffer = PickBuffer();

        buffer.Surface ??= SKSurface.Create(_info)
            ?? throw new InvalidOperationException("Could not create compose surface.");

        // This buffer repaints its own stale region plus whatever changed in
        // this publish; the others just accumulate this publish's change.
        // A null dirty means "everything changed" and forces a full repaint;
        // a null Stale means "nothing outstanding", which is not the same.
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
        return image;
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
