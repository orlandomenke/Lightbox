using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Paint that meets a surface: pressure decides how far it goes into the sheet,
/// a brush running dry skims it, and a brush with hairs opens into streaks
/// along the stroke. The owner's brief for the media once the simulation was
/// out of painting (Q240, Q246): no physics, but *"visually I would like to
/// come really close with how pigment and water/oil interact with the
/// paper/surface."*
/// </summary>
/// <remarks>
/// Everything here measures <b>coverage</b> — the share of pixels in a patch of
/// the mark that carry real paint — rather than alpha down the middle of the
/// stroke, because a dozen dabs overlap every pixel and their sum saturates
/// (the brush-measurement skill). Coverage does not accumulate: a valley the
/// ceiling keeps empty stays empty however many dabs cross it. Both numbers of
/// every comparison are printed.
/// </remarks>
public class MediaMeetTheSurfaceTests(ITestOutputHelper output)
{
    private const int Width = 900;
    private const int Height = 200;

    private static BrushSettings Paint(
        double? fill = 0.35, double bristles = 0, double load = 1, PaperKind sheet = PaperKind.Linen) => new()
    {
        Size = 48, Hardness = 0.8, Opacity = 1, Flow = 0.9, Spacing = 0.08,
        AngleFollowsDirection = true,
        Bristles = bristles <= 0 ? null : bristles,
        TextureSurface = sheet, TextureScale = 6, TextureDepth = 0.9,
        TexturePressure = 1, TextureFill = fill,
        Medium = new MediumSettings { PaintLoad = load },
    };

    private static SKBitmap Draw(BrushSettings brush, double pressure, int length = 800)
    {
        var points = new List<StrokePoint>();
        for (var x = 50; x <= 50 + length; x += 10) points.Add(new StrokePoint(x, 100, pressure));
        var stroke = new Stroke { Tool = ToolKind.Brush, Color = "#102040", Brush = brush, Points = points };
        return FrameRasterizer.Rasterize([stroke], Width, Height, 1.0);
    }

    /// <summary>The share of a patch of the mark's middle that carries paint.</summary>
    private static double Coverage(SKBitmap bitmap, int fromX, int toX, int halfHeight = 12, int solid = 128)
    {
        var px = bitmap.GetPixelSpan();
        int covered = 0, counted = 0;
        for (var y = 100 - halfHeight; y <= 100 + halfHeight; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                if (px[(((y * Width) + x) * 4) + 3] >= solid) covered++;
                counted++;
            }
        }
        return covered / (double)counted;
    }

    /// <summary>
    /// The complaint this answers: an oil held to the weave at every pressure
    /// *"looks like a stamp, or that it only skims the paper surface instead of
    /// flowing into the crevices with pressure."*
    /// </summary>
    [Fact]
    public void ALightTouchSkimsTheSheetAndAFirmOneFillsIt()
    {
        using var light = Draw(Paint(), 0.08);
        using var firm = Draw(Paint(), 0.6);
        var skim = Coverage(light, 300, 600, halfHeight: 4);
        var fill = Coverage(firm, 300, 600);
        output.WriteLine($"light touch covers {skim:P0} of the sheet under it, a firm press {fill:P0}");

        Assert.True(skim > 0.05, $"a light touch left nothing at all ({skim:P0})");
        Assert.True(skim < 0.75, $"a light touch already covers {skim:P0}: the sheet does not show");
        Assert.True(fill > 0.98, $"a firm press covers only {fill:P0}: the paint did not go into the valleys");
    }

    /// <summary>
    /// What "fills" adds. Without it the tooth is a pencil's: a full press
    /// still leaves the deepest valleys of the sheet lighter.
    /// </summary>
    [Fact]
    public void WithoutAFillPressureAFullPressStillLeavesTheValleys()
    {
        // "Solid" here is nearly opaque: a valley a pencil leaves lighter is
        // still half covered, which a looser bar would count as filled.
        using var paint = Draw(Paint(fill: 0.35), 1.0);
        using var graphite = Draw(Paint(fill: null), 1.0);
        var solid = Coverage(paint, 300, 600, solid: 225);
        var toothed = Coverage(graphite, 300, 600, solid: 225);
        output.WriteLine($"full press: with a fill pressure {solid:P1} covered, without one {toothed:P1}");
        Assert.True(solid > 0.99, $"paint with a fill pressure covers {solid:P1} at a full press");
        Assert.True(toothed < solid - 0.02, $"without a fill pressure the sheet is covered just the same ({toothed:P1})");
    }

    /// <summary>
    /// A brush running dry skims however hard it is pressed: the ragged,
    /// canvas-showing end of a real stroke.
    /// </summary>
    [Fact]
    public void ABrushRunningDryShowsTheSheet()
    {
        using var loaded = Draw(Paint(load: 1), 0.6);
        using var drying = Draw(Paint(load: 0.5), 0.6);
        var start = Coverage(drying, 80, 200);
        var end = Coverage(drying, 700, 820);
        var endLoaded = Coverage(loaded, 700, 820);
        output.WriteLine($"running dry: {start:P0} covered at the start, {end:P0} at the end; a full brush {endLoaded:P0} at the end");

        Assert.True(start > 0.95, $"the stroke starts thin ({start:P0}) - the brush was loaded");
        Assert.True(end < start - 0.2, $"the end ({end:P0}) is as covered as the start ({start:P0}): the brush never ran dry on the sheet");
        Assert.True(endLoaded > 0.98, $"a brush that does not run out thinned anyway ({endLoaded:P0})");

        // And it is the sheet showing, not only the paint getting thinner: the
        // same brush with no sheet under it, part of the way along, where its
        // dabs still cover. A tooth that ignored how much paint is left would
        // be as covered there as this control is (adversary: the two asserts
        // above pass on plain thinning alone).
        var noSheet = Paint(load: 0.5);
        noSheet.TextureSurface = null;
        using var smooth = Draw(noSheet, 0.6);
        // Measured in 50 px steps when this was written: on the sheet 76, 64,
        // 53% from x 450; with no sheet 96, 96, 96%.
        var midway = Coverage(drying, 480, 580);
        var midwaySmooth = Coverage(smooth, 480, 580);
        output.WriteLine($"part of the way along: {midway:P0} covered on the sheet, {midwaySmooth:P0} with no sheet");
        Assert.True(midwaySmooth > 0.9, $"the control has thinned too by here ({midwaySmooth:P0}): move the window");
        Assert.True(midway < midwaySmooth - 0.2, $"the sheet does not show as the brush empties: {midway:P0} against {midwaySmooth:P0} with no sheet");
    }

    /// <summary>
    /// Hairs are streaks along the stroke: where the brush is thin, the mark
    /// varies far more from row to row across the brush than it does along
    /// any one row. And without hairs it does not.
    /// </summary>
    [Fact]
    public void HairsOpenIntoStreaksAlongTheStroke()
    {
        static double RowSpread(SKBitmap bitmap)
        {
            // Mean alpha of each row across the brush, over a thin stretch of
            // the stroke, and then how jagged that profile is: each row
            // against the average of the rows a hair's width either side. The brush's own soft
            // edge makes the profile a smooth hump with or without hairs (the
            // plain spread of the rows measured that and nothing else - 48
            // without hairs against 38 with); hairs make it a comb.
            var px = bitmap.GetPixelSpan();
            var rows = new List<double>();
            for (var y = 82; y <= 118; y++)
            {
                long sum = 0;
                for (var x = 620; x < 800; x++) sum += px[(((y * Width) + x) * 4) + 3];
                rows.Add(sum / 180.0);
            }
            // Three rows apart: a hair is three pixels wide, so a row and its
            // immediate neighbour are usually the same hair.
            var jag = 0.0;
            for (var i = 3; i < rows.Count - 3; i++) jag += Math.Abs(rows[i] - ((rows[i - 3] + rows[i + 3]) / 2));
            return jag / (rows.Count - 6);
        }

        // On Fibre, which has no rows of its own: a weave's threads run along
        // a horizontal stroke and would be counted as streaks with no hairs at
        // all (they were - 43 against 38, the first time this was measured).
        using var plain = Draw(Paint(load: 0.5, sheet: PaperKind.Fibre), 0.6);
        using var bristled = Draw(Paint(load: 0.5, bristles: 0.8, sheet: PaperKind.Fibre), 0.6);
        var without = RowSpread(plain);
        var with = RowSpread(bristled);
        output.WriteLine($"jaggedness across the brush where it is thin: {without:0.0} without hairs, {with:0.0} with");
        // Measured 24.0 with hairs against 14.7 without (a ratio of 1.6) when
        // this was written; without hairs is the broken version, and is 1.0.
        Assert.True(with > without * 1.4, $"hairs made no streaks: {with:0.0} against {without:0.0} without");

        // And they are the brush's own: the same hairs on a second render.
        using var again = Draw(Paint(load: 0.5, bristles: 0.8, sheet: PaperKind.Fibre), 0.6);
        Assert.True(bristled.GetPixelSpan().SequenceEqual(again.GetPixelSpan()), "the hairs moved between two renders of one stroke");
    }

    /// <summary>
    /// The hairs run with the stroke whichever way it goes, on a plain round
    /// tip that is never turned: down a vertical stroke the streaks are
    /// vertical. And they are streaks - the mark is far more jagged across the
    /// stroke than along it - not speckle, which would be the same both ways.
    /// </summary>
    /// <remarks>
    /// Review found the first version laid a round tip's hairs level whichever
    /// way the stroke ran: the round dab is never rotated, and the hairs were
    /// in the dab's frame. A horizontal stroke cannot see that, which is all
    /// the test above draws.
    /// </remarks>
    [Fact]
    public void HairsRunWithTheStrokeOnARoundTip()
    {
        const int W = 300, H = 900;
        var brush = Paint(load: 0.5, bristles: 0.8, sheet: PaperKind.Fibre);
        brush.AngleFollowsDirection = false;
        var points = new List<StrokePoint>();
        for (var y = 50; y <= 850; y += 10) points.Add(new StrokePoint(150, y, 0.6));
        using var bitmap = FrameRasterizer.Rasterize(
            [new Stroke { Tool = ToolKind.Brush, Color = "#102040", Brush = brush, Points = points }], W, H, 1.0);
        var px = bitmap.GetPixelSpan();

        // A patch where the brush is thin: its mean alpha by column (across
        // the stroke) and by row (along it), and how jagged each profile is.
        const int x0 = 132, x1 = 168, y0 = 620, y1 = 800;
        var columns = new double[x1 - x0];
        var rows = new double[y1 - y0];
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var a = px[(((y * W) + x) * 4) + 3];
                columns[x - x0] += a / (double)(y1 - y0);
                rows[y - y0] += a / (double)(x1 - x0);
            }
        }
        static double Jag(double[] v)
        {
            var sum = 0.0;
            for (var i = 3; i < v.Length - 3; i++) sum += Math.Abs(v[i] - ((v[i - 3] + v[i + 3]) / 2));
            return sum / (v.Length - 6);
        }
        var across = Jag(columns);
        var along = Jag(rows);
        output.WriteLine($"vertical stroke, round tip: jaggedness {across:0.0} across the stroke, {along:0.0} along it");
        Assert.True(across > along * 2, $"no streaks down a vertical stroke: {across:0.0} across against {along:0.0} along");
    }

    /// <summary>A loaded bristle brush pressed home is still solid: the hairs are for when it lifts or empties.</summary>
    [Fact]
    public void ALoadedBristleBrushPressedHomeIsSolid()
    {
        using var mark = Draw(Paint(load: 1, bristles: 0.8), 1.0);
        var covered = Coverage(mark, 300, 600);
        output.WriteLine($"loaded, full press, hairs 0.8: {covered:P1} covered");
        Assert.True(covered > 0.97, $"a loaded brush at full press is scratched ({covered:P1})");
    }

    /// <summary>Both settings are absent from a record that does not use them (the optional-settings rule).</summary>
    [Fact]
    public void ABrushWithNoFillPressureAndNoHairsWritesNeitherKey()
    {
        var plain = JsonSerializer.Serialize(new BrushSettings { TexturePressure = 0.7 }, DocJson.Options);
        var used = JsonSerializer.Serialize(new BrushSettings { TextureFill = 0.35, Bristles = 0.8 }, DocJson.Options);
        Assert.DoesNotContain("\"textureFill\"", plain);
        Assert.DoesNotContain("\"bristles\"", plain);
        Assert.Contains("\"textureFill\"", used);
        Assert.Contains("\"bristles\"", used);
        var back = JsonSerializer.Deserialize<BrushSettings>(used, DocJson.Options)!;
        Assert.Equal(0.35, back.TextureFill);
        Assert.Equal(0.8, back.Bristles);
    }
}
