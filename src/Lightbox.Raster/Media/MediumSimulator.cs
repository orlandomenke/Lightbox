using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Media;

/// <summary>
/// Turns a stamped stroke into simulated paint.
///
/// The dabs arrive already laid down in a scratch surface, so this does not
/// care how the stroke was drawn — only what coverage it produced. That
/// coverage becomes water and pigment on a lattice over the paper, the
/// lattice is run for a fixed number of steps, and what settles is what gets
/// composited.
///
/// Two things follow from working off the scratch rather than the dab list.
/// Pressure is already baked into the coverage, so "a light touch carries
/// more water" is expressible without threading per-dab state through. And
/// the whole pass is a pure function of (coverage, existing pixels, paper,
/// settings) — no clock, no RNG, fixed iteration count — so a reload and an
/// AI inbetween re-render the same image (invariant 2).
/// </summary>
public static class MediumSimulator
{
    /// <summary>
    /// Read-only RGBA8888 view over a bitmap. SKBitmap.GetPixel bounds-checks
    /// and builds an SKColor per call, which is fine occasionally and ruinous
    /// in a per-pixel loop — this pass used to make millions of them.
    /// </summary>
    private readonly ref struct Pixels
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private readonly int _rowBytes;
        public readonly int Width;
        public readonly int Height;

        private Pixels(ReadOnlySpan<byte> bytes, int rowBytes, int width, int height)
        {
            _bytes = bytes;
            _rowBytes = rowBytes;
            Width = width;
            Height = height;
        }

        public bool IsEmpty => _bytes.IsEmpty;

        public static Pixels Of(SKPixmap? pixmap) =>
            pixmap is null || pixmap.ColorType != SKColorType.Rgba8888
                ? default
                : new Pixels(pixmap.GetPixelSpan(), pixmap.RowBytes, pixmap.Width, pixmap.Height);

        public byte AlphaAt(int x, int y) => _bytes[y * _rowBytes + x * 4 + 3];

        public SKColor At(int x, int y)
        {
            var i = y * _rowBytes + x * 4;
            return new SKColor(_bytes[i], _bytes[i + 1], _bytes[i + 2], _bytes[i + 3]);
        }
    }

    /// <summary>
    /// Below this the lattice is not worth building — a stroke that covers a
    /// handful of pixels has nowhere to flow.
    /// </summary>
    private const int MinSide = 4;

    /// <summary>
    /// Simulating at full document resolution is wasteful: the fluid is
    /// low-frequency and the lattice is the expensive part. Above this many
    /// cells on a side the region is simulated coarser and the result scaled
    /// back, which keeps a big brush affordable.
    /// </summary>
    private const int MaxSide = 320;

    /// <summary>
    /// Run the medium over a stamped stroke.
    /// </summary>
    /// <param name="scratch">
    /// The stroke's dabs, in a surface covering <paramref name="rect"/> of the
    /// document. Replaced in place by the simulated result.
    /// </param>
    /// <param name="existing">
    /// The layer as it stands before this stroke, for re-wetting and mixing.
    /// Null disables both — there is nothing underneath to interact with.
    /// </param>
    /// <param name="rect">The document region the scratch covers.</param>
    public static void Apply(
        SKSurface scratch,
        SKBitmap? existing,
        SKColor strokeColor,
        MediumSettings medium,
        SKRectI rect)
    {
        if (medium.Kind == MediumKind.None) return;
        if (rect.Width < MinSide || rect.Height < MinSide) return;

        // Work at a coarseness the fluid actually needs.
        var step = Math.Max(1, (int)Math.Ceiling(Math.Max(rect.Width, rect.Height) / (double)MaxSide));
        var w = Math.Max(MinSide, rect.Width / step);
        var h = Math.Max(MinSide, rect.Height / step);

        var coverage = SampleCoverage(scratch, rect, w, h, step);
        if (coverage is null) return;

        var lattice = new FluidLattice(w, h);
        var paper = new float[w * h];
        PaperField.Fill(paper, w, h, rect.Left / step, rect.Top / step, medium.Paper, medium.PaperScale / step);
        lattice.SetPaper(paper, medium.PaperInfluence);

        SeedFromCoverage(lattice, coverage, strokeColor, medium, w, h);
        if (medium.Rewetting > 0 && existing is not null)
        {
            SeedRewetted(lattice, existing, coverage, medium, rect, w, h, step);
        }

        lattice.Run(Math.Clamp(medium.FlowSteps, 0, 32), new FluidParams(
            (float)Math.Clamp(medium.Viscosity, 0, 1),
            (float)Math.Clamp(medium.Drag, 0, 1),
            (float)Math.Clamp(medium.Absorbency, 0, 1),
            (float)Math.Clamp(medium.EdgePull, 0, 1),
            (float)Math.Clamp(medium.Granularity, 0, 1)));

        var deposit = new float[w * h * 4];
        lattice.ReadDeposit(deposit);
        WriteBack(scratch, deposit, existing, strokeColor, medium, rect, w, h, step);
    }

    /// <summary>
    /// The stroke's alpha, downsampled. This is where pressure enters: dab
    /// alpha already carries it, so low coverage means a light touch.
    /// </summary>
    private static float[]? SampleCoverage(SKSurface scratch, SKRectI rect, int w, int h, int step)
    {
        using var image = scratch.Snapshot();
        if (image is null) return null;
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var small = new SKBitmap(info);
        // Box-filtered downsample, so a thin stroke does not fall between samples.
        if (!image.ScalePixels(small.PeekPixels(), new SKSamplingOptions(SKFilterMode.Linear)))
        {
            return null;
        }

        var coverage = new float[w * h];
        using var pixmap = small.PeekPixels();
        var view = Pixels.Of(pixmap);
        if (view.IsEmpty) return null;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                coverage[y * w + x] = view.AlphaAt(x, y) / 255f;
            }
        }
        return coverage;
    }

    /// <summary>
    /// Coverage becomes water and pigment. A light touch (low coverage) puts
    /// down proportionally more water than pigment when PressureWater is
    /// raised, which is what makes a light watercolour stroke pale and prone
    /// to blooming rather than merely thin.
    /// </summary>
    private static void SeedFromCoverage(
        FluidLattice lattice, float[] coverage, SKColor color, MediumSettings medium, int w, int h)
    {
        var (r, g, b) = Linear(color);
        var wetness = (float)Math.Clamp(medium.Wetness, 0, 1);
        var density = (float)Math.Clamp(medium.PigmentDensity, 0, 1);
        var pressureWater = (float)Math.Clamp(medium.PressureWater, 0, 1);
        var load = (float)Math.Clamp(medium.PaintLoad, 0, 1);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = coverage[y * w + x];
                if (c <= 0.002f) continue;

                // Water rises as the touch lightens; pigment tracks coverage.
                var water = wetness * c * (1f + pressureWater * (1f - c));
                var pigment = density * c * load;
                lattice.Seed(x, y, water, pigment, r, g, b);
            }
        }
    }

    /// <summary>
    /// Lift paint that is already on the layer back into suspension where the
    /// new stroke passes, so it travels with it. This is wet into wet, and it
    /// is what makes a second stroke disturb the first instead of simply
    /// covering it.
    /// </summary>
    private static void SeedRewetted(
        FluidLattice lattice, SKBitmap existing, float[] coverage,
        MediumSettings medium, SKRectI rect, int w, int h, int step)
    {
        var rewet = (float)Math.Clamp(medium.Rewetting, 0, 1);
        var pressureMix = (float)Math.Clamp(medium.PressureMix, 0, 1);
        using var underPixmap = existing.PeekPixels();
        var under = Pixels.Of(underPixmap);
        if (under.IsEmpty) return;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = coverage[y * w + x];
                if (c <= 0.01f) continue;

                var sx = Math.Clamp(rect.Left + x * step, 0, under.Width - 1);
                var sy = Math.Clamp(rect.Top + y * step, 0, under.Height - 1);
                var alpha = under.AlphaAt(sx, sy);
                if (alpha == 0) continue;

                // Pressure decides how much the brush engages what is there:
                // at PressureMix 1 a light touch barely disturbs it.
                var engage = 1f - pressureMix * (1f - c);
                var lifted = rewet * engage * (alpha / 255f);
                if (lifted <= 0.002f) continue;

                var (r, g, b) = Linear(under.At(sx, sy));
                lattice.Seed(x, y, lifted * 0.5f, lifted, r, g, b);
            }
        }
    }

    /// <summary>
    /// Replace the scratch with what settled. The scratch is what the caller
    /// composites, so writing the deposit here keeps the medium entirely
    /// inside the existing bounded-region pipeline.
    /// </summary>
    private static void WriteBack(
        SKSurface scratch, float[] deposit, SKBitmap? existing, SKColor strokeColor,
        MediumSettings medium, SKRectI rect, int w, int h, int step)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var small = new SKBitmap(info);
        var hiding = Math.Clamp(medium.Hiding, 0, 1);

        // Build the result in a plain byte buffer and install it in one go.
        // SetPixel per pixel bounds-checks and repacks an SKColor each time;
        // at a 320-cell lattice that was ~100k calls per stroke.
        var rgba = new byte[w * h * 4];
        using (var underPixmap = existing?.PeekPixels())
        {
            var under = Pixels.Of(underPixmap);
            var canMix = medium.PhysicalMixing && !under.IsEmpty;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var i = (y * w + x) * 4;
                    var a = Math.Clamp(deposit[i + 3], 0f, 1f);
                    if (a <= 0.002f) continue; // buffer starts transparent

                    // Deposit is premultiplied linear; recover the pigment colour.
                    var settled = new SKColor(
                        Srgb(deposit[i] / a), Srgb(deposit[i + 1] / a), Srgb(deposit[i + 2] / a),
                        (byte)Math.Round(a * 255));

                    if (canMix)
                    {
                        // Layer the settled pigment over what is beneath it the
                        // way pigment layers, not the way film layers.
                        var sx = Math.Clamp(rect.Left + x * step, 0, under.Width - 1);
                        var sy = Math.Clamp(rect.Top + y * step, 0, under.Height - 1);
                        settled = Pigment.FromColor(settled, hiding).Over(under.At(sx, sy), a);
                    }

                    rgba[i] = settled.Red;
                    rgba[i + 1] = settled.Green;
                    rgba[i + 2] = settled.Blue;
                    rgba[i + 3] = (byte)Math.Round(a * 255);
                }
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, small.GetPixels(), rgba.Length);

        var canvas = scratch.Canvas;
        canvas.Save();
        canvas.ResetMatrix();
        canvas.Clear(SKColors.Transparent);
        using var image = SKImage.FromBitmap(small);
        canvas.DrawImage(
            image,
            new SKRect(0, 0, rect.Width, rect.Height),
            new SKSamplingOptions(SKFilterMode.Linear));
        canvas.Restore();
    }

    private static (float R, float G, float B) Linear(SKColor c) => (
        (float)Pigment.ToLinear(c.Red),
        (float)Pigment.ToLinear(c.Green),
        (float)Pigment.ToLinear(c.Blue));

    private static byte Srgb(float linear) => Pigment.FromLinear(Math.Clamp(linear, 0f, 1f));
}
