using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// A gradient is a pure function of its stops, which is what lets it live in
/// the record and re-render identically. The one judgement call in it is
/// interpolating in linear light rather than sRGB.
/// </summary>
public class GradientTests
{
    private static Gradient BlackToWhite() => new()
    {
        Stops =
        [
            new GradientStop { Position = 0, Color = "#000000" },
            new GradientStop { Position = 1, Color = "#ffffff" },
        ],
    };

    [Fact]
    public void TheEndsAreTheStopsThemselves()
    {
        var g = BlackToWhite();
        Assert.Equal((byte)0, GradientOps.Sample(g, 0).R);
        Assert.Equal((byte)255, GradientOps.Sample(g, 1).R);
    }

    [Fact]
    public void InterpolationIsInLinearLightNotSrgb()
    {
        // Halfway between black and white is mid-GREY in light, which is
        // about 188 in sRGB — not 128. Averaging the sRGB numbers instead is
        // why naive gradients go muddy through the middle, and this app
        // treats colour as light everywhere else.
        var mid = GradientOps.Sample(BlackToWhite(), 0.5);
        Assert.InRange(mid.R, 185, 191);
    }

    [Fact]
    public void AMultiStopRampHitsEachStopExactly()
    {
        var g = new Gradient
        {
            Stops =
            [
                new GradientStop { Position = 0, Color = "#ff0000" },
                new GradientStop { Position = 0.5, Color = "#00ff00" },
                new GradientStop { Position = 1, Color = "#0000ff" },
            ],
        };
        Assert.Equal((byte)255, GradientOps.Sample(g, 0).R);
        Assert.Equal((byte)255, GradientOps.Sample(g, 0.5).G);
        Assert.Equal((byte)255, GradientOps.Sample(g, 1).B);
    }

    [Fact]
    public void CoincidentStopsAreAHardEdge()
    {
        // Two stops at the same position is how a stripe or a cel-shading
        // terminator is authored; it must not divide by zero.
        var g = new Gradient
        {
            Stops =
            [
                new GradientStop { Position = 0, Color = "#000000" },
                new GradientStop { Position = 0.5, Color = "#000000" },
                new GradientStop { Position = 0.5, Color = "#ffffff" },
                new GradientStop { Position = 1, Color = "#ffffff" },
            ],
        };
        Assert.Equal((byte)0, GradientOps.Sample(g, 0.49).R);
        Assert.Equal((byte)255, GradientOps.Sample(g, 0.51).R);
    }

    [Fact]
    public void StopsOutOfOrderStillRampInPositionOrder()
    {
        var g = new Gradient
        {
            Stops =
            [
                new GradientStop { Position = 1, Color = "#ffffff" },
                new GradientStop { Position = 0, Color = "#000000" },
            ],
        };
        Assert.Equal((byte)0, GradientOps.Sample(g, 0).R);
        Assert.Equal((byte)255, GradientOps.Sample(g, 1).R);
    }

    [Theory]
    [InlineData(GradientSpread.Pad, -0.5, 0)]
    [InlineData(GradientSpread.Pad, 1.5, 255)]
    // Repeat wraps 1.25 to 0.25; Mirror reflects it to 0.75. Both are then
    // sampled in linear light, so the sRGB results are 140 and 225 rather
    // than the 64 and 191 a naive sRGB ramp would give.
    [InlineData(GradientSpread.Repeat, 1.25, 140)]
    [InlineData(GradientSpread.Mirror, 1.25, 225)]
    public void SpreadDecidesWhatHappensOffTheEnds(GradientSpread spread, double t, int expected)
    {
        var g = BlackToWhite();
        g.Spread = spread;
        Assert.InRange(GradientOps.Sample(g, t).R, Math.Max(0, expected - 4), Math.Min(255, expected + 4));
    }

    [Fact]
    public void AlphaInterpolatesAsCoverageNotAsLight()
    {
        var g = new Gradient
        {
            Stops =
            [
                new GradientStop { Position = 0, Color = "#ffffff", Alpha = 0 },
                new GradientStop { Position = 1, Color = "#ffffff", Alpha = 1 },
            ],
        };
        Assert.Equal((byte)0, GradientOps.Sample(g, 0).A);
        Assert.Equal((byte)255, GradientOps.Sample(g, 1).A);
        // Coverage is linear in itself — halfway is halfway.
        Assert.InRange(GradientOps.Sample(g, 0.5).A, 126, 130);
    }

    [Fact]
    public void AnEmptyGradientIsTransparentRatherThanACrash()
    {
        var g = new Gradient { Stops = [] };
        Assert.Equal((byte)0, GradientOps.Sample(g, 0.5).A);
    }

    [Fact]
    public void GradientsRoundTripThroughTheDocument()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        var g = BlackToWhite();
        g.Kind = GradientKind.Radial;
        g.Spread = GradientSpread.Mirror;
        g.Name = "Sky";
        doc.Gradients[g.Id] = g;

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        var back = Assert.Single(reloaded.Gradients).Value;

        Assert.Equal("Sky", back.Name);
        Assert.Equal(GradientKind.Radial, back.Kind);
        Assert.Equal(GradientSpread.Mirror, back.Spread);
        Assert.Equal(2, back.Stops.Count);
    }

    [Fact]
    public void ANewDocumentHasNoGradients()
    {
        Assert.Empty(DocumentFactory.CreateDoc(100, 100, 12).Gradients);
    }
}
