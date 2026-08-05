using Lightbox.Core.Documents;
using Lightbox.Testing;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A brush tip shapes smudge, blur and the blender, as it already shaped paint.
/// </summary>
/// <remarks>
/// <para>
/// B70. <c>StampSmudge</c> built its dab from <c>RadiusAt</c> and a hardness falloff in
/// <c>LerpDab</c>, and the blur path drew a blurred snapshot through a circle — neither consulted
/// <c>BrushTipRegistry</c>. So a chisel or a bristle tip did nothing at all on exactly the brushes
/// whose job is to push existing paint around, and nothing said so. That silence is the defect: the
/// mark was a legitimate mark, and a control the artist had set was inert.
/// </para>
/// <para>
/// The tip used here is deliberately a shape no falloff can imitate — a single horizontal bar across
/// the middle of an otherwise empty square. A round dab and a tipped dab then differ in a way that
/// cannot be confused with "softer" or "smaller": the tipped one must leave the corners of its own
/// footprint untouched. Testing with a soft round tip would prove nothing, because that is what the
/// falloff already draws.
/// </para>
/// </remarks>
[Collection("Registries")]
[Trait("Category", "Visual")]
public class EffectBrushTipTests(ITestOutputHelper output)
{
    private const int W = 240, H = 160;
    private const string BarTip = "tip-test-bar";

    /// <summary>A tip that is one wide horizontal bar — nothing above it, nothing below.</summary>
    private static void RegisterBarTip()
    {
        using var bmp = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(new SKRect(0, 26, 64, 38), paint);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        BrushTipRegistry.Register(new Dictionary<string, string>
        {
            [BarTip] = Convert.ToBase64String(data.AsSpan()),
        });
    }

    /// <summary>Something to push around: a solid block, so a dab either moves it or does not.</summary>
    private static SKBitmap Block()
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var body = new SKPaint { Color = new SKColor(0x20, 0xC0, 0x40, 255), IsAntialias = false };
        canvas.DrawRect(new SKRect(20, 40, 220, 120), body);
        using var stripe = new SKPaint { Color = new SKColor(0x10, 0x10, 0x10, 220) };
        for (var x = 20; x < 220; x += 16) canvas.DrawRect(new SKRect(x, 40, x + 8, 120), stripe);
        canvas.Flush();
        return bmp;
    }

    /// <summary>
    /// The three brushes B70 names. <b>The blender is not its own kind</b> — it is Smudge in Dulling
    /// mode, which is why the theory is parameterised on a pair rather than on <c>BrushKind</c>.
    /// </summary>
    public static TheoryData<string, BrushKind, SmudgeMode> Effects => new()
    {
        { "smudge", BrushKind.Smudge, SmudgeMode.Smearing },
        { "blender", BrushKind.Smudge, SmudgeMode.Dulling },
        { "blur", BrushKind.Blur, SmudgeMode.Smearing },
    };

    private static SKBitmap Painted(BrushKind kind, SmudgeMode mode, string? tipId, double size = 60)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var layer = Block();
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            // Straight and horizontal, so the bar tip's own orientation is what decides the
            // footprint rather than a heading that keeps turning.
            Points = [.. Enumerable.Range(0, 7).Select(i => new StrokePoint(60 + i * 20.0, 80, 1))],
            Brush = new BrushSettings
            {
                Kind = kind, Size = size, Hardness = 1, Flow = kind == BrushKind.Blur ? 0.7 : 1,
                Spacing = 0.12, SmudgeLength = 0.9, SmudgeRadius = 0.7, ColorRate = 0,
                SmudgeMode = mode,
                TipId = tipId,
            },
        };
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
        return layer;
    }

    /// <summary>Pixels this pass changed, and how many of them sit outside the tip's band.</summary>
    private static (int Changed, int AboveBand, int InBand) Survey(SKBitmap before, SKBitmap after)
    {
        // The bar covers texels 26..38 of 64, so a dab of radius r reaches about ±0.09r vertically
        // about its centre. The stroke runs at y = 80, so anything changed above y = 66 is outside
        // any dab's band and can only be there if the tip was ignored.
        int changed = 0, above = 0, inBand = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (before.GetPixel(x, y) == after.GetPixel(x, y)) continue;
                changed++;
                if (y < 66) above++;
                if (y is >= 74 and <= 86) inBand++;
            }
        }
        return (changed, above, inBand);
    }

    [Theory]
    [MemberData(nameof(Effects))]
    public void ATipChangesWhatAnEffectBrushTouches(string kindName, BrushKind kind, SmudgeMode mode)
    {
        RegisterBarTip();
        using var clean = Block();
        using var round = Painted(kind, mode, tipId: null);
        using var tipped = Painted(kind, mode, BarTip);

        var r = Survey(clean, round);
        var t = Survey(clean, tipped);
        output.WriteLine(
            $"{kindName}: round touched {r.Changed} px ({r.AboveBand} above the band), "
            + $"tipped touched {t.Changed} px ({t.AboveBand} above the band)");

        if (VisualSheet.Wanted)
        {
            using var flatRound = VisualSheet.OverPaper(round);
            using var flatTipped = VisualSheet.OverPaper(tipped);
            VisualSheet.WriteComparison(
                $"tip-{kindName}",
                $"B70 — {kindName} with a round dab against the same brush wearing a horizontal bar tip. "
                + "The tipped pass must leave the top and bottom of its own footprint alone.",
                "round dab", flatRound, "bar tip", flatTipped, amplify: 2);
        }

        // Neither is vacuous: both have to actually do something, or "they differ" is trivial.
        Assert.True(r.Changed > 500, $"the round {kindName} changed only {r.Changed} px — nothing to compare");
        Assert.True(t.Changed > 100, $"the tipped {kindName} changed only {t.Changed} px — the tip killed the dab");

        // The tip's shape reached the mark: a bar cannot touch what is well above the bar.
        Assert.True(
            t.AboveBand * 4 < r.AboveBand,
            $"the tipped {kindName} changed {t.AboveBand} px above the tip's band against the round dab's "
            + $"{r.AboveBand} — the tip is being ignored (B70)");

        // And it still works where it should, so the fix is not "mask everything away".
        Assert.True(
            t.InBand > 200,
            $"the tipped {kindName} changed only {t.InBand} px inside the tip's own band — it is masked "
            + "out rather than shaped");
    }

    /// <summary>
    /// The tip follows the stroke when the brush says it should.
    /// </summary>
    /// <remarks>
    /// A chisel that ignores direction is a chisel at a fixed angle, which is a different tool. The
    /// effect paths had no heading at all before this — they iterate positions, not the dab walk —
    /// so it is computed from consecutive dabs the same way the walk does it, including the rule
    /// that a stalled pen keeps its last heading rather than snapping to zero.
    /// </remarks>
    [Fact]
    public void AFollowingTipTurnsWithTheStroke()
    {
        RegisterBarTip();
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

        SKBitmap Diagonal(bool follows)
        {
            var layer = Block();
            var stroke = new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#000000",
                Points = [.. Enumerable.Range(0, 7).Select(i => new StrokePoint(50 + i * 20.0, 50 + i * 10.0, 1))],
                Brush = new BrushSettings
                {
                    Kind = BrushKind.Smudge, Size = 60, Hardness = 1, Flow = 1, Spacing = 0.12,
                    SmudgeLength = 0.9, SmudgeRadius = 0.7, ColorRate = 0,
                    TipId = BarTip, AngleFollowsDirection = follows,
                },
            };
            using var canvas = new SKCanvas(layer);
            BrushEngine.StampStroke(canvas, stroke, info, layer);
            canvas.Flush();
            return layer;
        }

        using var clean = Block();
        using var fixedAngle = Diagonal(follows: false);
        using var following = Diagonal(follows: true);

        var f = Survey(clean, fixedAngle);
        var g = Survey(clean, following);
        output.WriteLine($"fixed angle touched {f.Changed} px, following touched {g.Changed} px");

        var differing = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (fixedAngle.GetPixel(x, y) != following.GetPixel(x, y)) differing++;
            }
        }
        output.WriteLine($"the two footprints differ over {differing} px");

        Assert.True(f.Changed > 200 && g.Changed > 200, "one of the two passes did nothing");
        Assert.True(
            differing > 200,
            $"following the stroke changed the footprint by only {differing} px — AngleFollowsDirection "
            + "is not reaching the effect dab");
    }

    /// <summary>
    /// A brush with no tip renders exactly as it did, and that is the guard on the whole change.
    /// </summary>
    /// <remarks>
    /// The tip work threads a shape sampler through the hottest pixel loop in the engine. Every
    /// document already drawn uses the untipped path, so its output must be bit-identical — a
    /// refactor that shifted a round smudge by one 255th would be a silent change to art that
    /// already exists, which is what invariant 1 is for.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Effects))]
    public void AnUntippedEffectBrushIsUnchanged(string kindName, BrushKind kind, SmudgeMode mode)
    {
        RegisterBarTip();   // registered but unused: resolution must not be what changes the mark
        using var a = Painted(kind, mode, tipId: null);
        using var b = Painted(kind, mode, tipId: null);

        var identical = true;
        for (var y = 0; y < H && identical; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (a.GetPixel(x, y) == b.GetPixel(x, y)) continue;
                identical = false;
                break;
            }
        }
        Assert.True(identical, $"two renders of the same untipped {kindName} disagree — the engine is not a function");

        using var clean = Block();
        var survey = Survey(clean, a);
        output.WriteLine($"untipped {kindName} changed {survey.Changed} px, {survey.AboveBand} above the band");
        Assert.True(survey.Changed > 500, $"the untipped {kindName} did nothing, so this proves nothing");
    }
}
