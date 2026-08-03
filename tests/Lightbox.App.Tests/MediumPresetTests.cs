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
}
