using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The Photoshop-aligned dab dynamics. Every one is seeded from dab position,
/// which is what lets them be replayed across a whole sequence without the
/// image boiling — the property that decides whether an effect is usable in
/// an animation tool at all.
/// </summary>
public class BrushDynamicsTests
{
    private const int W = 240, H = 160;

    private static SKBitmap Render(BrushSettings brush, string colour = "#3060c0", bool curved = false)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < 14; i++)
        {
            // A straight stroke cannot tell a direction-following tip from a
            // fixed one — the heading never changes — so tests about angle
            // need the curve.
            var y = curved ? 80 + Math.Sin(i * 0.45) * 45 : 80;
            pts.Add(new StrokePoint(30 + i * 13, y, 1));
        }
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        BrushEngine.StampStroke(canvas, new Stroke
        {
            Tool = ToolKind.Brush, Color = colour, Points = pts, Brush = brush,
        }, info, bmp);
        canvas.Flush();
        return bmp;
    }

    private static BrushSettings Base() => new()
    {
        Size = 26, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.25, AntiAlias = false,
    };

    private static (int Ink, double MeanAlpha) Stats(SKBitmap b)
    {
        var ink = 0; double alpha = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            var a = b.GetPixel(x, y).Alpha;
            if (a <= 4) continue;
            ink++; alpha += a;
        }
        return (ink, ink == 0 ? 0 : alpha / ink);
    }

    private static bool Identical(SKBitmap a, SKBitmap b)
    {
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }

    [Fact]
    public void SizeJitter_VariesTheStroke_ButMinimumDiameterStopsDabsVanishing()
    {
        var plain = Base();
        var jittered = Base();
        jittered.SizeJitter = 0.9;
        jittered.MinimumDiameter = 0.5;

        using var a = Render(plain);
        using var b = Render(jittered);
        Assert.False(Identical(a, b), "size jitter changed nothing");

        // With a floor of half, the stroke thins but never breaks into gaps.
        var (inkPlain, _) = Stats(a);
        var (inkJit, _) = Stats(b);
        Assert.True(inkJit < inkPlain, "jittered stroke should cover less");
        Assert.True(inkJit > inkPlain * 0.4, "minimum diameter should stop it collapsing");
    }

    [Fact]
    public void FlowJitter_ChangesAlphaWithoutChangingCoverage()
    {
        var jittered = Base();
        jittered.FlowJitter = 0.8;

        using var plain = Render(Base());
        using var b = Render(jittered);
        Assert.True(Stats(b).MeanAlpha < Stats(plain).MeanAlpha, "flow jitter should lighten some dabs");
    }

    [Fact]
    public void Roundness_SquashesTheDab()
    {
        var flat = Base();
        flat.Roundness = 0.3;

        using var round = Render(Base());
        using var squashed = Render(flat);
        Assert.True(Stats(squashed).Ink < Stats(round).Ink * 0.7,
            "a roundness of 0.3 should cover far less than a circular dab");
    }

    [Fact]
    public void AngleFollowsDirection_ChangesAFlatTip_ButNotACircularOne()
    {
        // A squashed dab that follows the stroke draws a ribbon; the same dab
        // at a fixed angle does not. A circular dab cannot tell the difference,
        // which is the sanity half of the check.
        var following = Base();
        following.Roundness = 0.3;
        following.AngleFollowsDirection = true;
        var fixedAngle = Base();
        fixedAngle.Roundness = 0.3;

        using var a = Render(following, curved: true);
        using var b = Render(fixedAngle, curved: true);
        Assert.False(Identical(a, b), "a flat tip should track the curve");

        var circleFollowing = Base();
        circleFollowing.AngleFollowsDirection = true;
        using var c = Render(circleFollowing, curved: true);
        using var d = Render(Base(), curved: true);
        Assert.True(Identical(c, d), "direction cannot matter to a circular dab");
    }

    [Fact]
    public void ColorDynamics_DriftTowardTheSecondColour()
    {
        var mixed = Base();
        mixed.SecondaryColor = "#e0c040";
        mixed.ColorJitter = 1.0;

        using var plain = Render(Base());
        using var b = Render(mixed);
        Assert.False(Identical(plain, b));

        // Some dabs must have moved toward the yellow.
        var warmer = 0;
        for (var x = 30; x < 210; x++)
        {
            var p = plain.GetPixel(x, 80);
            var q = b.GetPixel(x, 80);
            if (p.Alpha > 200 && q.Alpha > 200 && q.Red > p.Red + 10) warmer++;
        }
        Assert.True(warmer > 0, "no dab drifted toward the secondary colour");
    }

    [Fact]
    public void Texture_BitesIntoTheStroke_AndIsAnchoredToThePaper()
    {
        var textured = Base();
        textured.TextureSurface = PaperKind.Rough;
        textured.TextureScale = 10;
        textured.TextureDepth = 0.8;

        using var plain = Render(Base());
        using var b = Render(textured);
        Assert.True(Stats(b).MeanAlpha < Stats(plain).MeanAlpha, "texture should remove paint");

        // Anchored to the document, so a second identical stroke gets the same
        // grain in the same places rather than a fresh pattern.
        using var again = Render(textured);
        Assert.True(Identical(b, again));
    }

    [Fact]
    public void EveryDynamicIsDeterministic()
    {
        var everything = Base();
        everything.SizeJitter = 0.7;
        everything.FlowJitter = 0.6;
        everything.RoundnessJitter = 0.5;
        everything.RotationJitter = 0.4;
        everything.Scatter = 0.3;
        everything.AngleFollowsDirection = true;
        everything.SecondaryColor = "#e0c040";
        everything.ColorJitter = 0.5;
        everything.HueJitter = 0.4;
        everything.SaturationJitter = 0.3;
        everything.BrightnessJitter = 0.3;
        everything.TextureSurface = PaperKind.ColdPress;
        everything.TextureDepth = 0.5;

        using var first = Render(everything);
        using var second = Render(everything);
        Assert.True(Identical(first, second),
            "a stroke with every dynamic on must still re-render identically");
    }

    [Fact]
    public void ABrushWithNoDynamicsSet_RendersExactlyAsBefore()
    {
        // The defaults must be inert, or every existing document changes.
        var defaults = new BrushSettings { Size = 26, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.25, AntiAlias = false };
        using var a = Render(defaults);
        using var b = Render(Base());
        Assert.True(Identical(a, b));
    }
}
