using Lightbox.Core.Documents;

namespace Lightbox.Core.Tests;

/// <summary>
/// Which brushes are the expensive expressive ones, and why the answer is
/// derived rather than written down.
/// </summary>
/// <remarks>
/// The point of the badge is that an artist choosing a brush knows what it
/// costs before they are three hundred strokes into a 4K canvas. That only
/// works if it cannot lie, which is why nothing stores it: turn the medium on
/// and the brush becomes expressive without anybody remembering to say so.
/// </remarks>
public class BrushCostTests
{
    [Fact]
    public void AnOrdinaryBrushIsFast()
    {
        // The default, and what almost every brush is. If this ever flips, the
        // badge stops meaning anything because it is on everything.
        Assert.Equal(BrushCost.Fast, BrushCostOf.Settings(new BrushSettings()));
    }

    [Fact]
    public void ASimulatedMediumIsExpressive()
    {
        var oil = new BrushSettings { Medium = new MediumSettings { Kind = MediumKind.Oil } };

        Assert.Equal(BrushCost.Expressive, BrushCostOf.Settings(oil));
        Assert.Contains("oil", BrushCostOf.Why(oil));
    }

    [Fact]
    public void SmudgeAndBlurAreExpressive()
    {
        // They read the canvas back per dab, which is the shape of cost the
        // badge is about — work that is not a function of the dab alone.
        Assert.Equal(BrushCost.Expressive, BrushCostOf.Settings(new BrushSettings { Kind = BrushKind.Smudge }));
        Assert.Equal(BrushCost.Expressive, BrushCostOf.Settings(new BrushSettings { Kind = BrushKind.Blur }));
    }

    [Fact]
    public void SamplingOtherLayersIsExpressive()
    {
        var live = new BrushSettings { SampleSource = SampleSource.AllLayersLive };

        Assert.Equal(BrushCost.Expressive, BrushCostOf.Settings(live));
        Assert.Contains("layers underneath", BrushCostOf.Why(live));
    }

    [Fact]
    public void JitterAndScatterAndTextureAreNotExpensive()
    {
        // The guard against badge inflation, and it is the assertion that
        // keeps the feature useful. These change how a mark LOOKS using values
        // the engine already holds; they do not send it to fetch anything. If
        // they counted, every interesting brush would be badged and the badge
        // would carry no information at all.
        var busy = new BrushSettings
        {
            Scatter = 0.8,
            SizeJitter = 0.6,
            FlowJitter = 0.5,
            RotationJitter = 1,
            HueJitter = 0.4,
            SaturationJitter = 0.4,
            BrightnessJitter = 0.4,
            RoundnessJitter = 0.5,
            TextureSurface = PaperKind.Rough,
            AngleFollowsDirection = true,
            TipId = "some-imported-tip",
        };

        Assert.Equal(BrushCost.Fast, BrushCostOf.Settings(busy));
        Assert.Null(BrushCostOf.Why(busy));
    }

    [Fact]
    public void TurningTheMediumOffMakesItFastAgain()
    {
        // Derived, not asserted. A stored flag would be wrong from here on.
        var brush = new BrushSettings { Medium = new MediumSettings { Kind = MediumKind.Watercolour } };
        Assert.Equal(BrushCost.Expressive, BrushCostOf.Settings(brush));

        brush.Medium.Kind = MediumKind.None;

        Assert.Equal(BrushCost.Fast, BrushCostOf.Settings(brush));
    }

    /// <summary>
    /// <b>B177: the badge said Fast and the stopwatch said otherwise.</b>
    /// Granulation and wet edge are full-stroke passes at pen-lift — measured
    /// at 57–74% of the commit cost of the presets an artist reported as
    /// laggy — so they cost the middle tier, and the tooltip names what to
    /// turn off to get the speed back.
    /// </summary>
    [Fact]
    public void GranulationCostsWhatTheBadgeSaysItCosts()
    {
        var pencilish = new BrushSettings { Granulation = 0.15 };
        Assert.Equal(BrushCost.Textured, BrushCostOf.Settings(pencilish));
        Assert.Contains("grain", BrushCostOf.Why(pencilish));

        var wet = new BrushSettings { WetEdge = 0.6 };
        Assert.Equal(BrushCost.Textured, BrushCostOf.Settings(wet));
        Assert.Contains("wet edge", BrushCostOf.Why(wet));

        // A medium outranks the texture pass: the brush already carries the
        // heavier badge, and two badges would be noise.
        var both = new BrushSettings
        {
            Granulation = 0.3,
            Medium = new MediumSettings { Kind = MediumKind.Watercolour },
        };
        Assert.Equal(BrushCost.Expressive, BrushCostOf.Settings(both));
    }

    [Fact]
    public void TheReasonNamesEveryCauseSoItCanBeActedOn()
    {
        // "Three expensive options" tells an artist nothing they can do. The
        // list of what to turn off does.
        var everything = new BrushSettings
        {
            Kind = BrushKind.Smudge,
            SampleSource = SampleSource.AllLayersBaked,
            Medium = new MediumSettings { Kind = MediumKind.Gouache },
        };

        var why = BrushCostOf.Why(everything);

        Assert.NotNull(why);
        Assert.Contains("gouache", why);
        Assert.Contains("canvas back", why);
        Assert.Contains("layers underneath", why);
    }
}
