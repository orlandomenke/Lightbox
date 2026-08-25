using Lightbox.App.Input;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// What a stroke keeps of the pen's axes: the brush decides, the artist can
/// override.
/// </summary>
/// <remarks>
/// <para>
/// <b>The control always measures tilt and speed; this is where they are kept
/// or dropped.</b> Reading two properties off a pointer point is free — writing
/// them into every point of every document is 113 bytes a point and 1.70× the
/// saved file, on an application whose unit of work is two hundred drawings.
/// </para>
/// <para>
/// Every test here feeds the builder the <em>same</em> full set of axes, so
/// what differs between them is only the brush. A gate that quietly stopped
/// filtering would pass a test that also changed its input.
/// </para>
/// </remarks>
public class StrokeBuilderPenAxisTests
{
    /// <summary>Far enough apart to clear the builder's minimum-distance filter.</summary>
    private const double Step = 10;

    private static ResponseCurve ACurve() => ResponseCurve.Linear();

    private static BrushSettings WithTiltCurve() => new()
    {
        TiltCurves = new Dictionary<BrushDynamic, ResponseCurve> { [BrushDynamic.Size] = ACurve() },
    };

    private static BrushSettings WithSpeedCurve() => new()
    {
        SpeedCurves = new Dictionary<BrushDynamic, ResponseCurve> { [BrushDynamic.Flow] = ACurve() },
    };

    /// <summary>Draw two samples through a brush and return the second point.</summary>
    private static StrokePoint SecondPointOf(BrushSettings brush, bool alwaysRecordAxes = false)
    {
        var builder = new StrokeBuilder();
        builder.Begin(ToolKind.Brush, "#000000", brush, 0, 0, 0.5, null, alwaysRecordAxes);
        // The pen reported everything it had; the brush decides what survives.
        builder.Add(Step, 0, 0.5, 34.2, -12.7, 0.418);
        var stroke = builder.End()!;
        Assert.Equal(2, stroke.Points.Count);
        return stroke.Points[1];
    }

    [Fact]
    public void ABrushThatReadsNeitherAxisRecordsNeither()
    {
        var point = SecondPointOf(new BrushSettings());

        Assert.Null(point.TiltX);
        Assert.Null(point.TiltY);
        Assert.Null(point.Speed);
        Assert.False(point.HasPenAxes);
    }

    [Fact]
    public void ABrushWithATiltCurveKeepsTiltAndDropsSpeed()
    {
        var point = SecondPointOf(WithTiltCurve());

        Assert.Equal(34.2, point.TiltX);
        Assert.Equal(-12.7, point.TiltY);
        // Per axis rather than all-or-nothing: a tilt brush has no use for
        // speed, and storing it would be half the waste the gate exists to
        // avoid.
        Assert.Null(point.Speed);
    }

    [Fact]
    public void ABrushWithASpeedCurveKeepsSpeedAndDropsTilt()
    {
        var point = SecondPointOf(WithSpeedCurve());

        Assert.Equal(0.418, point.Speed);
        Assert.Null(point.TiltX);
        Assert.Null(point.TiltY);
    }

    [Fact]
    public void SteeringTheDabByLeanIsEnoughToKeepTilt()
    {
        var point = SecondPointOf(new BrushSettings { AngleFollowsTilt = true });

        Assert.Equal(34.2, point.TiltX);
    }

    [Fact]
    public void TheArtistsOverrideKeepsEverythingThroughABrushThatWantsNone()
    {
        // The whole of what the preference buys: the numbers are there so a
        // stroke can be given a curve months later and actually respond. Without
        // it, that curve reads the neutral default and the mark never changes.
        var point = SecondPointOf(new BrushSettings(), alwaysRecordAxes: true);

        Assert.Equal(34.2, point.TiltX);
        Assert.Equal(-12.7, point.TiltY);
        Assert.Equal(0.418, point.Speed);
    }

    [Fact]
    public void TheDecisionIsFixedWhenTheStrokeStarts()
    {
        // Switching brushes mid-drag is a real gesture, and half a stroke
        // carrying tilt is worse than none of it: a curve would read the
        // recorded half and the neutral default for the rest, and the mark
        // would step.
        var brush = WithTiltCurve();
        var builder = new StrokeBuilder();
        builder.Begin(ToolKind.Brush, "#000000", brush, 0, 0, 0.5);

        // Mutating the caller's object after Begin must change nothing: the
        // stroke took its own snapshot, and so did the gate.
        brush.TiltCurves = null;
        builder.Add(Step, 0, 0.5, 34.2, -12.7, 0.418);

        Assert.Equal(34.2, builder.End()!.Points[1].TiltX);
    }

    [Fact]
    public void TheFirstPointOfAStrokeNeverCarriesAxes()
    {
        // Begin takes no axes and should invent none: a stroke starts from
        // rest, and the pen has reported nothing to compare against yet.
        var builder = new StrokeBuilder();
        builder.Begin(ToolKind.Brush, "#000000", WithTiltCurve(), 0, 0, 0.5, null, true);

        Assert.False(builder.Current!.Points[0].HasPenAxes);
    }
}
