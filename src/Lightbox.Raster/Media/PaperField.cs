using System.Collections.Concurrent;
using Lightbox.Core.Documents;

namespace Lightbox.Raster.Media;

/// <summary>
/// The sheet's surface height in 0..1 as a function of <em>document</em>
/// position. Every stroke on a frame asks the same function for the same
/// pixel and gets the same answer, which is the whole difference between
/// pigment settling into a sheet and noise multiplied onto a stroke: grain
/// that moves with the stroke reads as film dirt, grain anchored to the
/// document reads as paper.
///
/// The field is a periodic tile evaluated once per (kind, scale) and then
/// repeated — the same trick as <c>BrushEngine.GrainTile</c>, for the same
/// reason: several octaves of noise per pixel is affordable once and
/// ruinous per stroke. Every construction here is tileable by design (the
/// lattice period divides the tile, the thread counts are whole numbers), so
/// the repeat has no seam to find.
///
/// Determinism (invariant 2): integer hashes and polynomials only. No RNG,
/// no clock, no <see cref="Math"/> transcendentals — the thread profile is a
/// smoothstepped triangle rather than a cosine precisely so the result does
/// not depend on a platform libm.
/// </summary>
public static class PaperField
{
    /// <summary>Surface height in 0..1 at document coordinates.</summary>
    public static float HeightAt(int docX, int docY, PaperKind kind, double scale)
    {
        var tile = GetTile(kind, scale);
        var t = tile.Size;
        return tile.Data[Mod(docY, t) * t + Mod(docX, t)];
    }

    /// <summary>
    /// Fill a document-space rect, row-major, <c>dest.Length == width*height</c>.
    /// Rows are copied out of the tile in runs, so the cost is a memory copy
    /// proportional to the rect (invariant 6) and never to the canvas.
    /// </summary>
    public static void Fill(Span<float> dest, int width, int height,
                            int originX, int originY, PaperKind kind, double scale)
    {
        if (width < 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (width == 0 || height == 0) return;
        if (dest.Length < checked(width * height))
        {
            throw new ArgumentException("dest is smaller than width*height.", nameof(dest));
        }

        var tile = GetTile(kind, scale);
        var t = tile.Size;
        var startX = Mod(originX, t);
        for (var row = 0; row < height; row++)
        {
            var src = tile.Data.AsSpan(Mod(originY + row, t) * t, t);
            var dst = dest.Slice(row * width, width);
            var sx = startX;
            var written = 0;
            while (written < width)
            {
                var run = Math.Min(t - sx, width - written);
                src.Slice(sx, run).CopyTo(dst.Slice(written, run));
                written += run;
                sx = 0;
            }
        }
    }

    /// <summary>
    /// Period of the field in document pixels along both axes: the field
    /// satisfies <c>HeightAt(x + p, y) == HeightAt(x, y)</c>. Exposed so a
    /// caller that wants to align something to the sheet — or a test that
    /// wants to look at the wrap — does not have to guess.
    /// </summary>
    public static int TilePeriod(PaperKind kind, double scale) => GetTile(kind, scale).Size;

    // ---- tile cache -----------------------------------------------------------

    private sealed class Tile
    {
        public required int Size { get; init; }
        public required float[] Data { get; init; }
    }

    /// <summary>
    /// Scale is quantised to 1/16 px before it reaches the cache, so a slider
    /// dragged through a hundred intermediate values does not build a hundred
    /// tiles. Both entry points quantise identically, which is what makes
    /// <see cref="HeightAt"/> and <see cref="Fill"/> agree exactly.
    /// </summary>
    private const double ScaleQuantum = 1.0 / 16;

    private const double MinScale = 1.5;
    private const double MaxScale = 256;

    /// <summary>
    /// Beyond a handful of live (kind, scale) pairs the cache is holding
    /// history, not working set, so it is dropped wholesale rather than
    /// grown. Content is a pure function of the key, so eviction can never
    /// change a rendered pixel.
    /// </summary>
    private const int MaxCachedTiles = 16;

    private static readonly ConcurrentDictionary<(PaperKind Kind, int ScaleKey), Tile> Cache = new();

    private static Tile GetTile(PaperKind kind, double scale)
    {
        var clamped = Math.Clamp(double.IsFinite(scale) ? scale : MinScale, MinScale, MaxScale);
        var key = (kind, (int)Math.Round(clamped / ScaleQuantum, MidpointRounding.AwayFromZero));
        if (Cache.TryGetValue(key, out var hit)) return hit;
        if (Cache.Count >= MaxCachedTiles) Cache.Clear();
        return Cache.GetOrAdd(key, static k => Build(k.Kind, k.ScaleKey * ScaleQuantum));
    }

    // ---- surfaces -------------------------------------------------------------

    /// <summary>
    /// What separates the four sheets. <c>Wavelength</c> is a multiple of the
    /// caller's grain size; <c>Contrast</c> scales the deviation from mid
    /// grey, so it is the visible tooth depth. Cold-press is the reference —
    /// smooth is the same construction an order of magnitude fainter and
    /// finer, rough is coarser and twice as deep.
    /// </summary>
    private readonly record struct Surface(
        double Wavelength, int Octaves, float Gain, float Contrast);

    private static Surface SurfaceOf(PaperKind kind) => kind switch
    {
        // Hot-pressed: a faint fine tooth, near enough to flat that
        // granulation on it reads as an even wash.
        PaperKind.Smooth => new Surface(0.55, 2, 0.5f, 0.16f),
        // Rough: fewer, larger hollows and a much deeper tooth.
        PaperKind.Rough => new Surface(2.2, 4, 0.62f, 1.45f),
        // Canvas is built from threads rather than octaves; only Wavelength
        // (thread pitch = the caller's grain size) and Contrast apply.
        PaperKind.Canvas => new Surface(1.0, 0, 0f, 1.0f),
        _ => new Surface(1.0, 4, 0.55f, 1.0f),
    };

    private static Tile Build(PaperKind kind, double scale)
    {
        var surface = SurfaceOf(kind);
        // Grain finer than two document pixels is below Nyquist — it cannot
        // be represented, only aliased. Clamping here rather than letting the
        // cell count saturate is what keeps scale monotonic: without it,
        // several distinct scales collapsed onto a bit-identical field and
        // the slider silently stopped doing anything.
        var wavelength = Math.Max(2.0, scale * surface.Wavelength);
        var size = TileSizeFor(wavelength);
        // Whole cells across the tile is what makes the wrap seamless; the
        // rounding shifts the realised wavelength by well under a percent at
        // any usable grain size.
        var cells = Math.Clamp((int)Math.Round(size / wavelength), 2, size / 2);

        // Each octave doubles the lattice period, so the finest one decides
        // whether the sum aliases. Drop octaves that would put fewer than two
        // pixels in a cell instead of summing noise the tile cannot carry.
        var octaves = surface.Octaves;
        while (octaves > 1 && (long)cells * (1L << (octaves - 1)) > size / 2) octaves--;

        var data = kind == PaperKind.Canvas
            ? BuildWeave(size, cells)
            : BuildFbm(size, cells, surface, octaves);

        for (var i = 0; i < data.Length; i++)
        {
            data[i] = Math.Clamp(0.5f + surface.Contrast * (data[i] - 0.5f), 0f, 1f);
        }
        return new Tile { Size = size, Data = data };
    }

    /// <summary>
    /// Summed octaves of tileable value noise, normalised back into 0..1.
    /// Each octave doubles the lattice period, so every octave wraps on the
    /// tile and their sum does too.
    ///
    /// Hashing four corners per octave per pixel is what makes a build slow —
    /// a 1024² tile is 16M hashes — and there are only a few thousand
    /// distinct lattice points, so each octave's lattice is hashed once up
    /// front and the pixel loop is array reads.
    /// </summary>
    private static float[] BuildFbm(int size, int cells, Surface surface, int octaves)
    {
        var data = new float[size * size];
        var inv = 1.0 / size;

        // One octave at a time over the whole tile. The lattice column a
        // pixel falls in is the same on every row, so the x-side indices and
        // fade weights are computed once per octave rather than once per
        // pixel; the inner loop is then four reads and three lerps.
        var i0 = new int[size];
        var i1 = new int[size];
        var tu = new float[size];

        float amp = 1, norm = 0;
        var period = cells;
        for (var o = 0; o < octaves; o++)
        {
            var grid = LatticeGrid(period, (uint)(o + 1));
            for (var x = 0; x < size; x++)
            {
                var u = x * inv * period;
                var fu = Math.Floor(u);
                i0[x] = Mod((int)fu, period);
                i1[x] = i0[x] + 1 == period ? 0 : i0[x] + 1;
                tu[x] = Fade((float)(u - fu));
            }

            for (var y = 0; y < size; y++)
            {
                var v = y * inv * period;
                var fv = Math.Floor(v);
                var j0 = Mod((int)fv, period) * period;
                var j1 = Mod((int)fv + 1, period) * period;
                var tv = Fade((float)(v - fv));
                var row = y * size;
                for (var x = 0; x < size; x++)
                {
                    int a = i0[x], b = i1[x];
                    var t = tu[x];
                    var top = Lerp(grid[j0 + a], grid[j0 + b], t);
                    var bottom = Lerp(grid[j1 + a], grid[j1 + b], t);
                    data[row + x] += amp * Lerp(top, bottom, tv);
                }
            }

            norm += amp;
            amp *= surface.Gain;
            period *= 2;
        }

        var invNorm = 1f / norm;
        for (var i = 0; i < data.Length; i++) data[i] *= invNorm;
        return data;
    }

    /// <summary>
    /// Warp and weft: two thread systems at right angles, at different counts
    /// and different weights. That asymmetry is the point of having Canvas at
    /// all — an isotropic field would be cold-press with extra steps — and it
    /// is what a directional-energy test can see.
    ///
    /// Each system's amplitude is modulated by the other's phase, which is
    /// what interlacing physically is: a thread stands proud where it crosses
    /// over and sinks where it passes under.
    /// </summary>
    private static float[] BuildWeave(int size, int cells)
    {
        var warpCount = cells;
        // Weft is the sparser system, as in most primed linen. Whole threads
        // across the tile keep the weave seamless at the wrap.
        var weftCount = Math.Max(2, (int)Math.Round(cells / 1.7));
        // Threads wander slowly along their length rather than running dead
        // straight; the wander period is a few threads wide.
        var wander = Math.Max(2, cells / 4);
        var fibrePeriod = cells * 3;

        var warpWander = LatticeGrid(wander, 11);
        var weftWander = LatticeGrid(wander, 12);
        var fibreLattice = LatticeGrid(fibrePeriod, 14);

        var inv = 1.0 / size;

        // Each thread system's phase varies only along the thread, so it is
        // one value per row (or column), not one per pixel.
        var warpPhase = new float[size];
        var weftPhase = new float[size];
        var warpLift = new float[size];
        var weftLift = new float[size];
        for (var i = 0; i < size; i++)
        {
            var t = i * inv;
            warpPhase[i] = (Noise1(warpWander, wander, t * wander) - 0.5f) * 0.45f;
            weftPhase[i] = (Noise1(weftWander, wander, t * wander) - 0.5f) * 0.45f;
            // Half a thread out of phase: a thread stands highest between the
            // threads it crosses, where nothing lies over it.
            warpLift[i] = 0.55f + 0.45f * ThreadProfile(t * weftCount + 0.5);
            weftLift[i] = 0.55f + 0.45f * ThreadProfile(t * warpCount + 0.5);
        }

        var data = new float[size * size];
        for (var y = 0; y < size; y++)
        {
            var v = y * inv;
            var row = y * size;
            var lift = 0.62f * warpLift[y];
            for (var x = 0; x < size; x++)
            {
                var u = x * inv;
                var warp = ThreadProfile(u * warpCount + warpPhase[y]) - 0.5f;
                var weftHere = ThreadProfile(v * weftCount + weftPhase[x]) - 0.5f;
                var fibre = Noise2(fibreLattice, fibrePeriod, u * fibrePeriod, v * fibrePeriod);
                data[row + x] = 0.5f
                              + lift * warp
                              + 0.30f * weftLift[x] * weftHere
                              + 0.10f * (fibre - 0.5f);
            }
        }
        return data;
    }

    /// <summary>
    /// Enough tile that the repeat is not legible — a couple of dozen grain
    /// cells across — but capped, because the tile is a resident allocation
    /// (1024² floats is 4 MB) and the cost of building it is quadratic in the
    /// side.
    /// </summary>
    private static int TileSizeFor(double wavelength)
    {
        var wanted = wavelength * 24;
        var size = 256;
        while (size < wanted && size < 1024) size <<= 1;
        return size;
    }

    // ---- noise ----------------------------------------------------------------

    /// <summary>
    /// Cross-section of one thread: a triangle wave rounded by smoothstep.
    /// A cosine would look the same and read better, but its bits are the
    /// platform's, and this field has to be reproducible everywhere.
    /// </summary>
    private static float ThreadProfile(double t)
    {
        var f = (float)(t - Math.Floor(t));
        var tri = 1f - Math.Abs(2f * f - 1f);
        return tri * tri * (3f - 2f * tri);
    }

    /// <summary>
    /// One period × period block of hashed lattice values. Wrapping the
    /// lattice — not the sample coordinate — is what makes the noise tile:
    /// the cell to the right of the last one is the first one, so the field
    /// meets itself at the wrap with matching values and matching slopes.
    /// </summary>
    private static float[] LatticeGrid(int period, uint salt)
    {
        var grid = new float[period * period];
        for (var j = 0; j < period; j++)
        {
            for (var i = 0; i < period; i++)
            {
                grid[j * period + i] = Hash01(i, j, salt);
            }
        }
        return grid;
    }

    /// <summary>Tileable value noise: bilinear over a wrapped lattice, with a
    /// quintic fade so cell edges leave no crease.</summary>
    private static float Noise2(float[] grid, int period, double u, double v)
    {
        var fu = Math.Floor(u);
        var fv = Math.Floor(v);
        var i0 = Mod((int)fu, period);
        var j0 = Mod((int)fv, period);
        var i1 = i0 + 1 == period ? 0 : i0 + 1;
        var j1 = j0 + 1 == period ? 0 : j0 + 1;
        var tu = Fade((float)(u - fu));
        var tv = Fade((float)(v - fv));

        var r0 = j0 * period;
        var r1 = j1 * period;
        var top = Lerp(grid[r0 + i0], grid[r0 + i1], tu);
        var bottom = Lerp(grid[r1 + i0], grid[r1 + i1], tu);
        return Lerp(top, bottom, tv);
    }

    /// <summary>The same, along one axis — the first row of the grid.</summary>
    private static float Noise1(float[] grid, int period, double u)
    {
        var fu = Math.Floor(u);
        var i0 = Mod((int)fu, period);
        var i1 = i0 + 1 == period ? 0 : i0 + 1;
        return Lerp(grid[i0], grid[i1], Fade((float)(u - fu)));
    }

    /// <summary>Quintic fade: zero first and second derivative at the lattice
    /// points, so no cell edge shows up as a crease.</summary>
    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Deterministic 0..1 value at a lattice point (never an RNG),
    /// the same FNV-then-avalanche shape as <c>BrushEngine.Hash01</c>.</summary>
    private static float Hash01(int ix, int iy, uint salt)
    {
        var h = 2166136261u ^ (salt * 0x9E3779B9u);
        h = (h ^ (uint)ix) * 16777619u;
        h = (h ^ (uint)iy) * 16777619u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) * (1f / 0x1000000);
    }

    /// <summary>Euclidean modulus: the field extends into negative document
    /// coordinates, where C#'s <c>%</c> would fold the tile back on itself.</summary>
    private static int Mod(int value, int period)
    {
        var m = value % period;
        return m < 0 ? m + period : m;
    }
}
