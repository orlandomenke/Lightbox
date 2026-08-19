using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Which pen axes a brush needs — the question that decides what a stroke
/// bothers to record.
/// </summary>
/// <remarks>
/// Response to tilt and speed was always the brush's business, the same way
/// pressure response is, so whether the numbers are worth storing is that same
/// question asked one step earlier. The cost of getting it wrong is measured
/// rather than asserted: writing all three axes is 113 bytes a point and 1.70×
/// the saved document.
/// </remarks>
public class PenAxisUseTests
{
    private static ResponseCurve ACurve() => ResponseCurve.Linear();

    // ---- what counts as needing an axis -------------------------------------------

    [Fact]
    public void APlainBrushNeedsNeitherAxis()
    {
        var brush = new BrushSettings();

        Assert.False(PenAxisUse.NeedsTilt(brush));
        Assert.False(PenAxisUse.NeedsSpeed(brush));
    }

    [Fact]
    public void ATiltCurveMakesABrushNeedTiltAndNothingElse()
    {
        var brush = new BrushSettings
        {
            TiltCurves = new Dictionary<BrushDynamic, ResponseCurve> { [BrushDynamic.Size] = ACurve() },
        };

        Assert.True(PenAxisUse.NeedsTilt(brush));
        // Per axis, deliberately: a brush that leans on tilt has no use for
        // speed, and recording it anyway is the waste this whole gate exists
        // to avoid.
        Assert.False(PenAxisUse.NeedsSpeed(brush));
    }

    [Fact]
    public void ASpeedCurveMakesABrushNeedSpeedAndNothingElse()
    {
        var brush = new BrushSettings
        {
            SpeedCurves = new Dictionary<BrushDynamic, ResponseCurve> { [BrushDynamic.Flow] = ACurve() },
        };

        Assert.True(PenAxisUse.NeedsSpeed(brush));
        Assert.False(PenAxisUse.NeedsTilt(brush));
    }

    [Fact]
    public void SteeringTheDabByLeanCountsAsNeedingTiltEvenWithNoCurve()
    {
        // Azimuth is a direction rather than a multiplier, so it drives no
        // curve — and it still cannot work without the stored tilt.
        var brush = new BrushSettings { AngleFollowsTilt = true };

        Assert.True(PenAxisUse.NeedsTilt(brush));
    }

    [Fact]
    public void AnEmptyCurveDictionaryIsNotADemand()
    {
        // An editor that created the dictionary and then had every curve
        // removed must not keep a document paying for the axis.
        var brush = new BrushSettings
        {
            TiltCurves = [],
            SpeedCurves = [],
            AngleFollowsTilt = false,
        };

        Assert.False(PenAxisUse.NeedsTilt(brush));
        Assert.False(PenAxisUse.NeedsSpeed(brush));
    }

    // ---- absent unless used --------------------------------------------------------

    [Fact]
    public void ABrushUsingNoneOfThisWritesNoneOfItsKeys()
    {
        // The reason AngleFollowsTilt is bool? rather than bool: the serializer
        // omits nulls and nothing else, so a plain flag would write
        // "angleFollowsTilt": false into the brush block of every stroke of
        // every document. AngleFollowsDirection predates the rule and is the
        // reason for it, not a licence to repeat it.
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke { Points = [new StrokePoint(1, 2, 0.5)] });

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("angleFollowsTilt", json);
        Assert.DoesNotContain("tiltCurves", json);
        Assert.DoesNotContain("speedCurves", json);
    }

    // ---- carried, like every other brush setting -----------------------------------

    [Fact]
    public void CloningABrushCarriesTheNewSettingsAndDeepCopiesTheCurves()
    {
        var brush = new BrushSettings
        {
            AngleFollowsTilt = true,
            TiltCurves = new Dictionary<BrushDynamic, ResponseCurve> { [BrushDynamic.Size] = ACurve() },
            SpeedCurves = new Dictionary<BrushDynamic, ResponseCurve> { [BrushDynamic.Flow] = ACurve() },
        };

        var copy = brush.Clone();

        Assert.True(copy.AngleFollowsTilt);
        Assert.True(PenAxisUse.NeedsTilt(copy));
        Assert.True(PenAxisUse.NeedsSpeed(copy));
        // Deep, like Curves: a stroke sharing its brush's curve object would
        // have its mark change when the brush was next edited, which is
        // invariant 4 with an extra step.
        Assert.NotSame(brush.TiltCurves![BrushDynamic.Size], copy.TiltCurves![BrushDynamic.Size]);
        Assert.NotSame(brush.SpeedCurves![BrushDynamic.Flow], copy.SpeedCurves![BrushDynamic.Flow]);
    }

    [Fact]
    public void TheNewSettingsSurviveARoundTrip()
    {
        var doc = DocumentFactory.CreateDoc(120, 80, 12);
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Points = [new StrokePoint(1, 2, 0.5)],
            Brush = new BrushSettings
            {
                AngleFollowsTilt = true,
                TiltCurves = new Dictionary<BrushDynamic, ResponseCurve> { [BrushDynamic.Size] = ACurve() },
            },
        });

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var brush = ((Frame)back.Scene.Layers[0].Cels[0].Frame!).Strokes[0].Brush;

        Assert.True(brush.AngleFollowsTilt);
        Assert.True(PenAxisUse.NeedsTilt(brush));
    }
}
