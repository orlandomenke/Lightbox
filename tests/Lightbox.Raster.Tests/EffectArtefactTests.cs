using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B39: effect brushes leave hard-edged opaque black artefacts mid-stroke.
/// </summary>
/// <remarks>
/// <para>
/// The measurement that discriminates, and it took three wrong probes on B33 to
/// learn why the obvious ones do not: <b>a smudge moves colour, so it cannot
/// produce alpha higher than the highest alpha it could have sampled.</b> Drag
/// over a wash whose maximum is 60 and every output pixel must still be at or
/// below 60. Opaque black is 255 and fails that by a factor of four.
/// </para>
/// <para>
/// Three things about the setup are deliberate, each because its absence hid this
/// bug from the tests that already existed:
/// </para>
/// <list type="bullet">
/// <item><b>A soft-edged blob, not a bar.</b> Every pre-existing smudge test uses
/// an opaque rectangle, where the failure cannot appear — an opaque source makes
/// opaque output correct.</item>
/// <item><b>A diagonal drag.</b> `LerpDab` writes an axis-aligned patch per dab,
/// so a diagonal stroke tiles them in a staircase — which is the shape the report
/// shows, and a horizontal drag would stack them in one row and hide it.</item>
/// <item><b>Across the edge, not along the middle.</b> Along the middle of a flat
/// region a smudge is close to a no-op by construction, which is exactly how B33's
/// first two probes passed against broken code.</item>
/// </list>
/// </remarks>
public class EffectArtefactTests(ITestOutputHelper output)
{
    private const int W = 220, H = 160;

    /// <summary>The highest alpha anywhere in the source. Nothing may exceed it.</summary>
    private const byte PeakAlpha = 60;

    /// <summary>
    /// A soft radial blob of black pigment on bare canvas — a pale wash with a
    /// gradient edge, which is what the report was drawn on.
    /// </summary>
    private static SKBitmap Blob()
    {
        var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(
                new SKPoint(90, 80), 60,
                [new SKColor(0, 0, 0, PeakAlpha), new SKColor(0, 0, 0, 0)],
                [0f, 1f],
                SKShaderTileMode.Clamp),
        };
        canvas.DrawCircle(90, 80, 60, paint);
        canvas.Flush();
        return bmp;
    }

    private static Stroke DiagonalDrag(BrushKind kind, double flow) => new()
    {
        Color = "#000000",
        Brush = new BrushSettings
        {
            Kind = kind, Size = 26, Hardness = 0.5, Flow = flow, Spacing = 0.1,
            SmudgeLength = 0.6, SmudgeRadius = 0.7,
        },
        // Diagonally across the blob's edge and out into bare canvas.
        Points = Enumerable.Range(0, 60)
            .Select(i => new StrokePoint(40 + i * 2.0, 40 + i * 1.6, 0.8))
            .ToList(),
    };

    private static (byte Peak, int OverPeak, int Opaque) Survey(SKBitmap b)
    {
        byte peak = 0;
        var overPeak = 0;
        var opaque = 0;
        for (var y = 0; y < b.Height; y++)
        {
            for (var x = 0; x < b.Width; x++)
            {
                var a = b.GetPixel(x, y).Alpha;
                if (a > peak) peak = a;
                // A few units of slack for rounding in the lerp; the artefact is
                // four times the peak, not a rounding error.
                if (a > PeakAlpha + 4) overPeak++;
                if (a > 250) opaque++;
            }
        }
        return (peak, overPeak, opaque);
    }

    private static SKBitmap Painted(Stroke stroke)
    {
        var layer = Blob();
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, stroke, new SKImageInfo(W, H), layer);
        canvas.Flush();
        return layer;
    }

    [Theory]
    [InlineData(BrushKind.Smudge, 0.08)]
    [InlineData(BrushKind.Smudge, 0.5)]
    [InlineData(BrushKind.Blur, 0.1)]
    public void AnEffectBrushCannotRaiseAlphaAboveWhatItCouldHaveSampled(BrushKind kind, double flow)
    {
        using var painted = Painted(DiagonalDrag(kind, flow));
        var (peak, overPeak, opaque) = Survey(painted);

        output.WriteLine($"{kind} flow {flow}: peak {peak} (source {PeakAlpha}), {overPeak} px over, {opaque} px opaque");

        Assert.True(
            opaque == 0,
            $"{kind} at flow {flow} left {opaque} opaque pixels on a wash whose peak is {PeakAlpha}");
        Assert.True(
            peak <= PeakAlpha + 4,
            $"{kind} at flow {flow} reached alpha {peak} from a source that never exceeded {PeakAlpha}");
    }

    /// <summary>
    /// The same invariant on the <b>draft</b> path, which is what an artist is
    /// looking at while the pointer is down.
    /// </summary>
    /// <remarks>
    /// Split from the committed path deliberately: the committed path measured
    /// clean (peak 49 from a source of 60), and the report is "while painting …
    /// sometimes persisting after release". A preview that can exceed what the
    /// commit produces is the whole shape of that complaint, and it is the same
    /// class of defect as B33 — which also lived only in the draft path.
    /// </remarks>
    [Theory]
    [InlineData(BrushKind.Smudge, 0.08)]
    [InlineData(BrushKind.Smudge, 0.5)]
    [InlineData(BrushKind.Blur, 0.1)]
    public void ADraftEffectCannotRaiseAlphaEither(BrushKind kind, double flow)
    {
        var stroke = DiagonalDrag(kind, flow);
        var layer = Blob();
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, stroke, new SKImageInfo(W, H), layer, draft: true);
        canvas.Flush();

        var (peak, overPeak, opaque) = Survey(layer);
        output.WriteLine($"draft {kind} flow {flow}: peak {peak} (source {PeakAlpha}), {overPeak} px over, {opaque} px opaque");
        layer.Dispose();

        Assert.True(opaque == 0, $"the draft {kind} at flow {flow} left {opaque} opaque pixels");
        Assert.True(peak <= PeakAlpha + 4, $"the draft {kind} at flow {flow} reached alpha {peak}");
    }

    /// <summary>
    /// The live preview as the view model actually drives it: one draft pass per
    /// pointer event over a growing tail, into a composite that is kept.
    /// </summary>
    /// <remarks>
    /// This is the arrangement B33 was found in and the one a single call cannot
    /// reproduce: `FlushLivePreview` stamps the tail of the stroke on every event,
    /// so a pixel under many dabs is visited many times. A per-call test passes
    /// against a per-event accumulation bug.
    /// </remarks>
    [Fact]
    public void TheLivePreviewAccumulationCannotRaiseAlphaEither()
    {
        var full = DiagonalDrag(BrushKind.Smudge, 0.5);
        var composite = Blob();
        using var canvas = new SKCanvas(composite);

        // 20 events, each stamping the stroke so far — what the pointer does.
        for (var n = 4; n <= full.Points.Count; n += 3)
        {
            var tail = new Stroke
            {
                Color = full.Color,
                Brush = full.Brush,
                Points = full.Points.Take(n).ToList(),
            };
            BrushEngine.StampStroke(canvas, tail, new SKImageInfo(W, H), composite, draft: true);
            canvas.Flush();
        }

        var (peak, overPeak, opaque) = Survey(composite);
        output.WriteLine($"live accumulation: peak {peak} (source {PeakAlpha}), {overPeak} px over, {opaque} px opaque");
        composite.Dispose();

        Assert.True(opaque == 0, $"the live preview left {opaque} opaque pixels");
        Assert.True(peak <= PeakAlpha + 4, $"the live preview reached alpha {peak} from a source of {PeakAlpha}");
    }

    /// <summary>
    /// The reported configuration, not a scaled-down stand-in: a 147 px Smudge on
    /// a 1920x1080 canvas, with the preset's own defaults.
    /// </summary>
    /// <remarks>
    /// The small-brush versions above all measure clean, so the size is a
    /// suspect rather than an incidental. Two things scale with radius and only
    /// one of them is the mark: `SampleAverage` offsets its taps by
    /// <c>radius * SmudgeRadius</c>, which at 147 px is 37 px from the dab
    /// centre — far enough to be reading something entirely unlike what the dab
    /// covers — and `LerpDab`'s patch rect grows with it.
    /// </remarks>
    [Fact]
    public void TheReportedConfigurationCannotRaiseAlphaEither()
    {
        const int w = 1920, h = 1080;
        const byte peakAlpha = 60;

        var layer = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(layer))
        {
            c.Clear(SKColors.Transparent);
            using var paint = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(700, 500), 320,
                    [new SKColor(0, 0, 0, peakAlpha), new SKColor(0, 0, 0, 0)],
                    [0f, 1f], SKShaderTileMode.Clamp),
            };
            c.DrawCircle(700, 500, 320, paint);
            c.Flush();
        }

        // The preset as it ships: only Size, Hardness, Flow, Spacing and Kind are
        // set, so SmudgeMode, SmudgeLength, SmudgeRadius and ColorRate take the
        // BrushSettings defaults — which is the brush the report was drawn with.
        var stroke = new Stroke
        {
            Color = "#000000",
            Brush = new BrushSettings
            {
                Kind = BrushKind.Smudge, Size = 147, Hardness = 0.5, Flow = 0.08, Spacing = 0.1,
            },
            Points = Enumerable.Range(0, 80)
                .Select(i => new StrokePoint(420 + i * 8.0, 360 + i * 5.0, 1.0))
                .ToList(),
        };

        using (var c = new SKCanvas(layer))
        {
            BrushEngine.StampStroke(c, stroke, new SKImageInfo(w, h), layer);
            c.Flush();
        }

        byte peak = 0;
        var opaque = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var a = layer.GetPixel(x, y).Alpha;
                if (a > peak) peak = a;
                if (a > 250) opaque++;
            }
        }
        layer.Dispose();

        output.WriteLine($"147px smudge: peak {peak} (source {peakAlpha}), {opaque} px opaque");
        Assert.True(opaque == 0, $"a 147 px smudge left {opaque} opaque pixels on a wash whose peak is {peakAlpha}");
        Assert.True(peak <= peakAlpha + 4, $"a 147 px smudge reached alpha {peak} from a source of {peakAlpha}");
    }

    [Fact]
    public void TheStrokeStillDoesSomething()
    {
        // The other half, or the assertion above also passes on a brush that
        // paints nothing at all — which is how a "fix" that disables the tool
        // gets shipped green.
        var before = Blob();
        using var after = Painted(DiagonalDrag(BrushKind.Smudge, 0.5));

        var moved = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (before.GetPixel(x, y) != after.GetPixel(x, y)) moved++;
            }
        }
        before.Dispose();

        output.WriteLine($"smudge changed {moved} px");
        Assert.True(moved > 500, $"the smudge only changed {moved} px — it is doing nothing");
    }
}
