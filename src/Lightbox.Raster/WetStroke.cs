using System.Buffers;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// A rewetting stroke under the pen, kept from one pointer event to the next
/// so an event redoes only what its moving tip changed (<see cref="WetBleed"/>,
/// Q255).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the plain way was too slow.</b> Rendering the rectangle each event
/// reached, from every dab that touches it, rebuilt wetness it already had:
/// at 300 px the perf lab measured 10 to 12 ms an event and the mark a second
/// behind the pen, where a pen sends an event every 4 to 5 ms.
/// </para>
/// <para>
/// <b>What is kept.</b> The wetness of the dabs that are <em>settled</em> -
/// which no point still to come can move, the same structural rule the paint
/// path stamps by - and the wetness every pixel had at the last event. An
/// event adds the newly settled dabs once, works out the moving tail's wetness
/// over the rectangle the tail covers now and covered last time, and
/// recombines only the pixels whose wetness changed. Wetness is a maximum, so
/// splitting the dabs into settled and tail changes no value; the pixel
/// expression is <see cref="WetBleed.Pixel"/>, the one a whole render calls;
/// so the result after every event is <see cref="WetBleed.Render"/> of all the
/// dabs, to the byte (<c>WaterRewetsTests.AStrokeKeptBetweenEventsIsTheWholeRender</c>).
/// </para>
/// </remarks>
public sealed class WetStroke
{
    private const int Tile = 64;
    private readonly int _width;
    private readonly int _height;
    private readonly BrushSettings _brush;
    private readonly WetBleed.Look _look;
    private readonly WetBleed.Spread _spread;
    private readonly Dictionary<(int X, int Y), float[]> _settledMask = [];
    private readonly Dictionary<(int X, int Y), float[]> _settledCover = [];
    private readonly Dictionary<(int X, int Y), float[]> _mask = [];
    private readonly Dictionary<(int X, int Y), float[]> _cover = [];
    private readonly List<BrushEngine.Dab> _tail = [];
    private int _settled;
    private SKRectI _touched = SKRectI.Empty;

    public WetStroke(int width, int height, BrushSettings brush)
    {
        _width = width;
        _height = height;
        _brush = brush;
        _look = new WetBleed.Look(brush);
        _spread = new WetBleed.Spread(width, height, WetBleed.SpreadRadius(brush));
    }

    /// <summary>Whether this was made for a layer of this size and this brush.</summary>
    public bool Fits(int width, int height, BrushSettings brush) =>
        width == _width && height == _height && ReferenceEquals(brush, _brush);

    /// <summary>How many pixels the last update recombined: the work it did, for a test to bound.</summary>
    public int PixelsRedone { get; private set; }

    /// <summary>
    /// Bring <paramref name="dest"/> (the working copy of the layer, as wide and
    /// tall as it) up to date with these dabs, of which the first
    /// <paramref name="settled"/> will never change. Returns the rectangle of
    /// pixels written, empty if none.
    /// </summary>
    public SKRectI Update(
        ReadOnlySpan<byte> pre, int preRowBytes, IReadOnlyList<BrushEngine.Dab> dabs, int settled,
        Span<byte> dest, int destRowBytes)
    {
        settled = Math.Clamp(settled, 0, dabs.Count);
        var layer = new SKRectI(0, 0, _width, _height);
        SKRectI redo;
        if (settled < _settled)
        {
            // A dab called final has moved. Never expected; if it happens the
            // kept wetness is wrong, so it is thrown away and everything the
            // stroke has ever touched is worked out again.
            _settledMask.Clear();
            _settledCover.Clear();
            _settled = 0;
            redo = _touched;
        }
        else
        {
            redo = SKRectI.Empty;
        }

        // 1. The dabs that have just become final, added once.
        if (settled > _settled)
        {
            var box = WetBleed.Bounds(dabs, _settled, settled, _brush, _width, _height);
            if (box.Width > 0 && box.Height > 0) Merge(dabs, _settled, settled, box, _settledMask, _settledCover);
            _settled = settled;
        }

        // 2. Where the tail is now, and where it was.
        redo = Union(redo, WetBleed.Bounds(_tail, 0, _tail.Count, _brush, _width, _height));
        redo = Union(redo, WetBleed.Bounds(dabs, settled, dabs.Count, _brush, _width, _height));
        redo = SKRectI.Intersect(redo, layer);
        _tail.Clear();
        for (var i = settled; i < dabs.Count; i++) _tail.Add(dabs[i]);
        PixelsRedone = 0;
        if (redo.Width <= 0 || redo.Height <= 0) return SKRectI.Empty;
        _touched = Union(_touched, redo);

        // 3. The wetness over it: the settled dabs', and the tail's on top.
        int rw = redo.Width, rh = redo.Height, n = rw * rh;
        var mask = ArrayPool<float>.Shared.Rent(n);
        var cover = ArrayPool<float>.Shared.Rent(n);
        Read(_settledMask, redo, mask);
        Read(_settledCover, redo, cover);
        WetBleed.Wetness(dabs, settled, dabs.Count, _brush, redo, mask.AsSpan(0, n), cover.AsSpan(0, n));

        // 4. Which of those pixels changed since the last event.
        var dirty = ArrayPool<bool>.Shared.Rent(n);
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (var ty = FloorDiv(redo.Top, Tile); ty <= FloorDiv(redo.Bottom - 1, Tile); ty++)
        {
            for (var tx = FloorDiv(redo.Left, Tile); tx <= FloorDiv(redo.Right - 1, Tile); tx++)
            {
                var hadMask = _mask.TryGetValue((tx, ty), out var tileMask);
                _cover.TryGetValue((tx, ty), out var tileCover);
                int x0 = Math.Max(redo.Left, tx * Tile), x1 = Math.Min(redo.Right, (tx + 1) * Tile);
                int y0 = Math.Max(redo.Top, ty * Tile), y1 = Math.Min(redo.Bottom, (ty + 1) * Tile);
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var i = ((y - redo.Top) * rw) + (x - redo.Left);
                        var t = ((y - (ty * Tile)) * Tile) + (x - (tx * Tile));
                        float wasM = hadMask ? tileMask![t] : 0f, wasC = tileCover is null ? 0f : tileCover[t];
                        var changed = wasM != mask[i] || wasC != cover[i];
                        dirty[i] = changed;
                        if (!changed) continue;
                        if (tileMask is null)
                        {
                            _mask[(tx, ty)] = tileMask = new float[Tile * Tile];
                            _cover[(tx, ty)] = tileCover = new float[Tile * Tile];
                            hadMask = true;
                        }
                        tileMask[t] = mask[i];
                        tileCover![t] = cover[i];
                        if (x < left) left = x;
                        if (x > right) right = x;
                        if (y < top) top = y;
                        if (y > bottom) bottom = y;
                    }
                }
            }
        }

        if (left > right)
        {
            ArrayPool<float>.Shared.Return(mask);
            ArrayPool<float>.Shared.Return(cover);
            ArrayPool<bool>.Shared.Return(dirty);
            return SKRectI.Empty;
        }

        // 5. Those pixels, recombined - the same expression a whole render uses.
        var box2 = new SKRectI(left, top, right + 1, bottom + 1);
        int bw = box2.Width, bh = box2.Height;
        var blurred = ArrayPool<byte>.Shared.Rent(bw * bh * 4);
        _spread.Read(pre, preRowBytes, box2, blurred, dirty.AsSpan(0, n), rw, new SKPointI(redo.Left, redo.Top));
        float[]? paper = null;
        if (_look.Settle > 0)
        {
            paper = ArrayPool<float>.Shared.Rent(bw * bh);
            Media.PaperField.Fill(paper.AsSpan(0, bw * bh), bw, bh, box2.Left, box2.Top,
                _brush.TextureSurface!.Value, Math.Max(1.5, _brush.TextureScale));
        }
        var coarse = _look.Coarse;
        var fine = _look.Fine;
        var redone = 0;
        for (var y = box2.Top; y < box2.Bottom; y++)
        {
            var docY = y + 0.5;
            coarse.Row(docY);
            fine.Row(docY);
            var src = pre.Slice(y * preRowBytes, _width * 4);
            var into = dest.Slice(y * destRowBytes, _width * 4);
            for (var x = box2.Left; x < box2.Right; x++)
            {
                var i = ((y - redo.Top) * rw) + (x - redo.Left);
                if (!dirty[i]) continue;
                redone++;
                var m = mask[i];
                if (m <= 0)
                {
                    src.Slice(x * 4, 4).CopyTo(into.Slice(x * 4, 4));
                    continue;
                }
                var docX = x + 0.5;
                var c = _look.Clouds > 0 ? Math.Clamp((coarse.At(docX) * 0.68f) + (fine.At(docX) * 0.32f), 0f, 1f) : 0.5f;
                var b = ((y - box2.Top) * bw) + (x - box2.Left);
                WetBleed.Pixel(src.Slice(x * 4, 4), blurred.AsSpan(b * 4, 4), m, cover[i], c, paper is null ? -1f : paper[b], _look, into.Slice(x * 4, 4));
            }
        }
        PixelsRedone = redone;

        if (paper is not null) ArrayPool<float>.Shared.Return(paper);
        ArrayPool<byte>.Shared.Return(blurred);
        ArrayPool<float>.Shared.Return(mask);
        ArrayPool<float>.Shared.Return(cover);
        ArrayPool<bool>.Shared.Return(dirty);
        return box2;
    }

    /// <summary>Merge a range of dabs into tiles, by keeping the larger value.</summary>
    private void Merge(
        IReadOnlyList<BrushEngine.Dab> dabs, int from, int to, SKRectI box,
        Dictionary<(int X, int Y), float[]> maskTiles, Dictionary<(int X, int Y), float[]> coverTiles)
    {
        int rw = box.Width, n = rw * box.Height;
        var mask = ArrayPool<float>.Shared.Rent(n);
        var cover = ArrayPool<float>.Shared.Rent(n);
        Array.Clear(mask, 0, n);
        Array.Clear(cover, 0, n);
        WetBleed.Wetness(dabs, from, to, _brush, box, mask.AsSpan(0, n), cover.AsSpan(0, n));
        for (var ty = FloorDiv(box.Top, Tile); ty <= FloorDiv(box.Bottom - 1, Tile); ty++)
        {
            for (var tx = FloorDiv(box.Left, Tile); tx <= FloorDiv(box.Right - 1, Tile); tx++)
            {
                if (!maskTiles.TryGetValue((tx, ty), out var tm))
                {
                    maskTiles[(tx, ty)] = tm = new float[Tile * Tile];
                    coverTiles[(tx, ty)] = new float[Tile * Tile];
                }
                var tc = coverTiles[(tx, ty)];
                int x0 = Math.Max(box.Left, tx * Tile), x1 = Math.Min(box.Right, (tx + 1) * Tile);
                int y0 = Math.Max(box.Top, ty * Tile), y1 = Math.Min(box.Bottom, (ty + 1) * Tile);
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        var i = ((y - box.Top) * rw) + (x - box.Left);
                        var t = ((y - (ty * Tile)) * Tile) + (x - (tx * Tile));
                        if (mask[i] > tm[t]) tm[t] = mask[i];
                        if (cover[i] > tc[t]) tc[t] = cover[i];
                    }
                }
            }
        }
        ArrayPool<float>.Shared.Return(mask);
        ArrayPool<float>.Shared.Return(cover);
    }

    /// <summary>Copy tiles over a rectangle into a packed array; zero where there is no tile.</summary>
    private static void Read(Dictionary<(int X, int Y), float[]> tiles, SKRectI box, float[] into)
    {
        var rw = box.Width;
        Array.Clear(into, 0, rw * box.Height);
        for (var ty = FloorDiv(box.Top, Tile); ty <= FloorDiv(box.Bottom - 1, Tile); ty++)
        {
            for (var tx = FloorDiv(box.Left, Tile); tx <= FloorDiv(box.Right - 1, Tile); tx++)
            {
                if (!tiles.TryGetValue((tx, ty), out var tile)) continue;
                int x0 = Math.Max(box.Left, tx * Tile), x1 = Math.Min(box.Right, (tx + 1) * Tile);
                int y0 = Math.Max(box.Top, ty * Tile), y1 = Math.Min(box.Bottom, (ty + 1) * Tile);
                for (var y = y0; y < y1; y++)
                {
                    tile.AsSpan(((y - (ty * Tile)) * Tile) + (x0 - (tx * Tile)), x1 - x0)
                        .CopyTo(into.AsSpan(((y - box.Top) * rw) + (x0 - box.Left), x1 - x0));
                }
            }
        }
    }

    private static SKRectI Union(SKRectI a, SKRectI b) =>
        a.Width <= 0 || a.Height <= 0 ? b : b.Width <= 0 || b.Height <= 0 ? a : SKRectI.Union(a, b);

    private static int FloorDiv(int value, int by) => (int)Math.Floor(value / (double)by);
}
