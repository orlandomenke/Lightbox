using Lightbox.App.Input;
using Lightbox.App.Services;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Smoothing as the pen moves filters the end of the stroke, and the stroke in
/// hand is still exactly the filter run over every point.
/// </summary>
/// <remarks>
/// <para>
/// The stroke builder ran the stabiliser's filter over the whole stroke on
/// every sample: with the default mode, two new lists of every point per pen
/// event, 430 KB of large-object garbage an event at 3000 points. Found in
/// review of B449, the long line that stopped following the pen; this is the
/// same shape one step earlier in the same event.
/// </para>
/// <para>
/// Each filter is a window over neighbouring points, so a new sample changes
/// only the last few smoothed points. The guard has two halves, and the first
/// is the one that matters: <b>the points are the full filter's, to the bit</b>,
/// at every sample - a smoothed point that drifted would move the dabs and the
/// committed stroke with them. Then the work, counted in points filtered.
/// </para>
/// </remarks>
public class SmoothingALongStrokeTests(ITestOutputHelper output)
{
    public static TheoryData<SmoothingMode, int> Shapes => new()
    {
        { SmoothingMode.Laplacian, 5 },
        { SmoothingMode.MovingAverage, 1 },
        { SmoothingMode.MovingAverage, 3 },
        { SmoothingMode.MovingAverage, 9 },
        { SmoothingMode.MovingAverage, 25 },
        { SmoothingMode.SavitzkyGolay, 1 },
        { SmoothingMode.SavitzkyGolay, 5 },
        { SmoothingMode.SavitzkyGolay, 7 },
        { SmoothingMode.SavitzkyGolay, 11 },
        { SmoothingMode.SavitzkyGolay, 25 },
        { SmoothingMode.Off, 5 },
        { SmoothingMode.Ema, 5 },
    };

    private static (double X, double Y, double Pressure) Sample(int i) => (
        600 + (Math.Cos(i * 0.021) * 500 * Math.Cos(i * 0.0031)) + (Math.Sin(i * 1.7) * 3),
        400 + (Math.Sin(i * 0.017) * 300) + (Math.Cos(i * 2.3) * 3),
        0.5 + (0.4 * Math.Sin(i * 0.01)));

    private static (StrokeBuilder Builder, StrokeStabilizer Stabilizer) Wired(SmoothingMode mode, int window)
    {
        var stabilizer = new StrokeStabilizer { Settings = new BrushStabilisation { Mode = mode, Window = window } };
        var builder = new StrokeBuilder
        {
            Smoothing = stabilizer.PostProcess,
            SmoothingShape = () => stabilizer.Shape,
        };
        return (builder, stabilizer);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TheStrokeInHandIsTheFilterOverEveryPoint(SmoothingMode mode, int window)
    {
        var (builder, stabilizer) = Wired(mode, window);
        var raw = new List<StrokePoint>();
        var (x0, y0, p0) = Sample(0);
        builder.Begin(ToolKind.Brush, "#101010", new BrushSettings(), x0, y0, p0);
        raw.Add(new StrokePoint(x0, y0, p0));
        for (var i = 1; i <= 400; i++)
        {
            var (x, y, p) = Sample(i);
            var had = builder.Current!.Points.Count;
            builder.Add(x, y, p);
            // The builder drops a sample that has not moved far enough to be one.
            if (builder.Current!.Points.Count == had) continue;
            raw.Add(new StrokePoint(x, y, p));
            var whole = stabilizer.PostProcess([.. raw]);
            var inHand = builder.Current!.Points;
            Assert.True(
                whole.SequenceEqual(inHand),
                $"{mode}, window {window}, {raw.Count} points: the stroke in hand is not the filter over every point "
                + $"(first difference at {FirstDifference(whole, inHand)} of {whole.Count} and {inHand.Count})");
        }
        Assert.True(raw.Count > 380, $"only {raw.Count} samples were kept");
    }

    private static int FirstDifference(IReadOnlyList<StrokePoint> a, IReadOnlyList<StrokePoint> b)
    {
        var shared = Math.Min(a.Count, b.Count);
        for (var i = 0; i < shared; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return shared;
    }

    [Theory]
    [InlineData(SmoothingMode.Laplacian, 5)]
    [InlineData(SmoothingMode.MovingAverage, 9)]
    [InlineData(SmoothingMode.SavitzkyGolay, 11)]
    public void ASampleFiltersTheEndOfTheStrokeNotTheStroke(SmoothingMode mode, int window)
    {
        var (builder, _) = Wired(mode, window);
        var (x0, y0, p0) = Sample(0);
        builder.Begin(ToolKind.Brush, "#101010", new BrushSettings(), x0, y0, p0);
        var late = 0;
        for (var i = 1; i <= 3000; i++)
        {
            var (x, y, p) = Sample(i);
            builder.Add(x, y, p);
            if (i > 2700) late = Math.Max(late, builder.LastSmoothedPoints);
        }
        output.WriteLine($"{mode}, window {window}: a sample at the end of a 3000-point stroke filtered {late} points");
        Assert.True(late > 0, "nothing was counted");
        Assert.True(late <= 100, $"{mode}: a sample at the end of a 3000-point stroke filtered {late} points");
    }

    /// <summary>A setting changed while the pen is down is honoured from the next sample, over the whole stroke.</summary>
    [Fact]
    public void AChangeOfSmoothingMidStrokeRefiltersTheWholeStroke()
    {
        var (builder, stabilizer) = Wired(SmoothingMode.MovingAverage, 9);
        var raw = new List<StrokePoint>();
        var (x0, y0, p0) = Sample(0);
        builder.Begin(ToolKind.Brush, "#101010", new BrushSettings(), x0, y0, p0);
        raw.Add(new StrokePoint(x0, y0, p0));
        for (var i = 1; i <= 200; i++)
        {
            if (i == 80) stabilizer.Settings = new BrushStabilisation { Mode = SmoothingMode.SavitzkyGolay, Window = 9 };
            if (i == 120) stabilizer.Settings = new BrushStabilisation { Mode = SmoothingMode.SavitzkyGolay, Window = 5 };
            if (i == 160) stabilizer.Settings = new BrushStabilisation { Mode = SmoothingMode.Off };
            var (x, y, p) = Sample(i);
            var had = builder.Current!.Points.Count;
            builder.Add(x, y, p);
            // The builder drops a sample that has not moved far enough to be one.
            if (builder.Current!.Points.Count == had) continue;
            raw.Add(new StrokePoint(x, y, p));
            Assert.True(
                stabilizer.PostProcess([.. raw]).SequenceEqual(builder.Current!.Points),
                $"at {raw.Count} points, after a change of smoothing");
        }
        Assert.True(raw.Count > 190, $"only {raw.Count} samples were kept");
    }
}
