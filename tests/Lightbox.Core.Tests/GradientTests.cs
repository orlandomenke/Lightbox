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

    // ---- the separate opacity track ------------------------------------------

    [Fact]
    public void WithNoAlphaTrackTheColourStopsCarryTheirOwnAlpha()
    {
        // Which is every gradient authored before the track existed. It must
        // render exactly as it did, and write no new key.
        var gradient = new Gradient
        {
            Stops =
            [
                new() { Position = 0, Color = "#000000", Alpha = 0 },
                new() { Position = 1, Color = "#ffffff", Alpha = 1 },
            ],
        };

        Assert.False(gradient.HasAlphaTrack);
        Assert.Equal(0, GradientOps.Sample(gradient, 0).A);
        Assert.Equal(255, GradientOps.Sample(gradient, 1).A);
        Assert.InRange(GradientOps.Sample(gradient, 0.5).A, 120, 136);
    }

    [Fact]
    public void AnAlphaTrackOverridesTheColourStopsAlpha()
    {
        var gradient = new Gradient
        {
            Stops =
            [
                new() { Position = 0, Color = "#000000", Alpha = 0 },
                new() { Position = 1, Color = "#ffffff", Alpha = 0 },
            ],
            AlphaStops = [new() { Position = 0, Alpha = 1 }, new() { Position = 1, Alpha = 1 }],
        };

        Assert.Equal(255, GradientOps.Sample(gradient, 0).A);
        Assert.Equal(255, GradientOps.Sample(gradient, 0.5).A);
    }

    [Fact]
    public void OpacityAndColourChangeAtTheirOwnPositions()
    {
        // The whole reason for two tracks: a ramp that fades out at the top
        // while going orange in the middle should not need a colour stop
        // placed to hold an opacity.
        var gradient = new Gradient
        {
            Stops =
            [
                new() { Position = 0, Color = "#ff0000" },
                new() { Position = 1, Color = "#0000ff" },
            ],
            AlphaStops =
            [
                new() { Position = 0, Alpha = 1 },
                new() { Position = 0.25, Alpha = 0 },
                new() { Position = 1, Alpha = 1 },
            ],
        };

        // Transparent a quarter of the way along, where no colour stop is.
        Assert.Equal(0, GradientOps.Sample(gradient, 0.25).A);
        // And the colour there is still the ramp's, untouched by the hole.
        var mid = GradientOps.Sample(gradient, 0.25);
        Assert.True(mid.R > mid.B);
    }

    [Fact]
    public void OneAlphaStopHoldsItsValueEverywhere()
    {
        var gradient = new Gradient { AlphaStops = [new() { Position = 0.5, Alpha = 0.5 }] };

        Assert.Equal(128, GradientOps.Sample(gradient, 0).A);
        Assert.Equal(128, GradientOps.Sample(gradient, 1).A);
    }

    [Fact]
    public void AnAlphaTrackRoundTripsThroughTheDocument()
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var gradient = new Gradient
        {
            AlphaStops = [new() { Position = 0, Alpha = 0 }, new() { Position = 1, Alpha = 1 }],
        };
        doc.Gradients[gradient.Id] = gradient;

        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Gradients[gradient.Id];

        Assert.Equal(2, back.AlphaStops!.Count);
        Assert.Equal(0, back.AlphaStops![0].Alpha);
    }

    [Fact]
    public void AGradientWithNoAlphaTrackWritesNoAlphaKey()
    {
        // Absent unless asked for, the rule the camera and the project follow.
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        doc.Gradients["g"] = new Gradient { Id = "g" };

        Assert.DoesNotContain("alphaStops", DocJson.Serialize(doc));
    }
}
