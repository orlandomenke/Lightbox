using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The three wet-medium presets, rendered — not their field values.
/// </summary>
/// <remarks>
/// Against the presets an artist actually gets, because that is where B35 and
/// B36 lived: nothing was wrong with the fluid lattice or the tip generator,
/// only with the numbers the three presets asked them for.
/// </remarks>
public class MediumPresetTests
{
    // App.Tests has no Xunit.Abstractions, so failures carry the numbers in
    // their message instead of a separate output line.
    private static BrushSettings Preset(string name) =>
        BuiltInPresets.Create().First(p => p.Name == name).Settings;

    private static Stroke Stroke(BrushSettings brush) => new()
    {
        Color = "#000000",
        Brush = brush,
        Points = Enumerable.Range(0, 60).Select(i => new StrokePoint(40 + i * 4, 80, 0.8)).ToList(),
    };

    /// <summary>Mean alpha along the stroke's centre line, and 14 px out toward its edge.</summary>
    private static (double Centre, double Flank) Profile(SKBitmap b)
    {
        double Row(int y)
        {
            double s = 0;
            var n = 0;
            for (var x = 80; x < 240; x++) { s += b.GetPixel(x, y).Alpha; n++; }
            return s / n;
        }
        return (Row(80), (Row(66) + Row(94)) / 2);
    }

    /// <summary>
    /// A wet stroke must keep pigment down its middle.
    /// </summary>
    /// <remarks>
    /// B35. `EdgePull` moves pigment toward the wet boundary, and
    /// `MediumSimulator.Apply` runs once over the whole stroke — so the
    /// boundary is the stroke's own outline and the centre, being furthest from
    /// dry paper, empties out. At the shipped 0.70 the watercolour centre held
    /// **3 of 255** alpha against a flank of 55: a white line down every mark.
    ///
    /// The rim is not wrong, it was just turned up until it was the only thing
    /// left. Measured centre/flank for watercolour: 0.05 at EdgePull 0.70, 0.42
    /// at 0.20, 1.11 at 0.06, 1.74 with no pull at all. The threshold below
    /// fails loudly at anything near the old value.
    /// </remarks>
    [Theory]
    [InlineData("Watercolor")]
    [InlineData("Gouache")]
    [InlineData("Oil")]
    public void AWetStrokeKeepsPigmentDownItsMiddle(string name)
    {
        using var bmp = FrameRasterizer.Rasterize([Stroke(Preset(name))], 320, 160);
        var (centre, flank) = Profile(bmp);
        var ratio = centre / Math.Max(1, flank);

        Assert.True(
            ratio >= 0.8,
            $"{name} leaves its centre at {centre:F1} alpha against a flank of {flank:F1} "
            + $"(ratio {ratio:F2}) — the stroke is hollow down the middle");
    }

    /// <summary>
    /// Every medium preset declares a tip, so the mark is not the stamping
    /// interval of a bare circle.
    /// </summary>
    /// <remarks>
    /// B36. None of the three set `TipId`, `AngleFollowsDirection` or any
    /// jitter, so every dab was the same soft circle at the same angle and what
    /// showed on the flanks was the spacing. Guarded rather than merely set: a
    /// preset quietly losing its tip would look like nothing in any other test.
    /// </remarks>
    [Theory]
    [InlineData("Watercolor")]
    [InlineData("Gouache")]
    [InlineData("Oil")]
    public void EveryMediumPresetHasATipAndSomeVariation(string name)
    {
        var b = Preset(name);
        Assert.False(string.IsNullOrEmpty(b.TipId), $"{name} still stamps the default round tip");
        Assert.True(
            b.AngleFollowsDirection || b.SizeJitter > 0 || b.RoundnessJitter > 0,
            $"{name} has no dab variation at all, so its flanks show the dab interval");
    }

    /// <summary>
    /// An opaque preset's faint edge stays paint-coloured instead of running up
    /// to the paper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The regression B273's fix nearly shipped. `Pigment.Over` has to pick a
    /// backing where the layer beneath is transparent, and a low pigment mass
    /// means one of two opposite things: a thin transparent film, lit from
    /// behind by the sheet, or a partly covered pixel at the edge of an opaque
    /// stroke, where the compositor's own alpha already lets the paper through.
    /// Backing the second with paper white counts the paper twice, and a dark
    /// gouache band's edge pixels came out `(249,247,243)` — paper — where they
    /// had been `(70,73,100)`. The mark reads narrower, which is how it was
    /// spotted.
    /// </para>
    /// <para>
    /// <b>It has to be measured here, against the shipped preset.</b> Two
    /// cheaper versions of this test did not work. Averaging over the whole
    /// stroke misses it — the interior does not move at all. Measuring a lone
    /// stroke's soft edge in `Lightbox.Raster` misses it too, because a medium
    /// stroke's antialiased edge comes from the bilinear upscale off the
    /// lattice, which carries the interior's colour at a lower alpha; the effect
    /// needs a real preset's coverage profile — its tip, spacing and flow — to
    /// put genuinely thin deposits at the rim.
    /// </para>
    /// <para>
    /// Measured mean luminance by alpha band, backed white → weighted:
    /// 12..60 <b>83.9 → 61.3</b>, 60..120 59.7 → 58.3, 120..200 58.1 → 58.1,
    /// 200+ 59.1 → 59.1. So the faintest band is the whole signal, and the
    /// threshold below sits between 24.8 and 2.2 above the interior.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOpaquePresetsFaintEdgeIsStillPaintNotPaper()
    {
        var brush = Preset("Gouache");
        var pts = new List<StrokePoint>();
        for (var i = 0; i < 48; i++)
        {
            var t = i / 47.0;
            pts.Add(new StrokePoint(50 + t * 320, 80 + Math.Sin(t * Math.PI) * 6, 0.9));
        }

        using var art = FrameRasterizer.Rasterize(
            [new Stroke { Tool = ToolKind.Brush, Color = "#2c3050", Points = pts, Brush = brush }],
            420, 160);

        // A dark paint, so "washed toward the paper" is a rise in luminance.
        (double Mean, int Count) Band(int lo, int hi)
        {
            double sum = 0;
            var n = 0;
            for (var y = 0; y < 160; y++)
            for (var x = 0; x < 420; x++)
            {
                var c = art.GetPixel(x, y);
                if (c.Alpha < lo || c.Alpha >= hi) continue;
                sum += (c.Red + c.Green + c.Blue) / 3.0;
                n++;
            }
            return (n == 0 ? 0 : sum / n, n);
        }

        var (faint, faintCount) = Band(12, 60);
        var (solid, solidCount) = Band(200, 256);

        Assert.True(faintCount > 100 && solidCount > 100,
            $"nothing to measure: {faintCount} faint px, {solidCount} solid px");
        Assert.True(
            faint - solid < 12,
            $"the faint edge of a dark gouache stroke averages {faint:F1} luminance against {solid:F1} "
            + $"in its interior ({faintCount} and {solidCount} px) — it is washing out toward the paper, "
            + "so the mark has lost its edge");
    }
}
