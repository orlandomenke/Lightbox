using System.Diagnostics;
using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Cleanup;

/// <summary>
/// The cleaner runs on the thread the artist draws on — a preview, and each
/// generated inbetween when the opt-in is on — so a dense drawing must not
/// freeze it. The perf review measured the first version at 1.5 s (plain) and
/// 6 s (guided) on 300 lines of 100 points, growing with the square of the
/// drawing: every end was tested against every segment. These budgets are
/// loose, as the suite's are, and catch that coming back.
/// </summary>
public class LineCleanerPerformanceTests(ITestOutputHelper output)
{
    /// <summary>A dense key: wobbly lines in rows, crossing a few others.</summary>
    private static List<Stroke> Dense(int lines, int points, int seed)
    {
        var drawing = new List<Stroke>(lines);
        for (var i = 0; i < lines; i++)
        {
            var x0 = 20.0 + (i * 37 % 1700);
            var y0 = 20.0 + (i * 53 % 1000);
            drawing.Add(new Stroke
            {
                Brush = new BrushSettings { Size = 3 },
                Points = [.. Enumerable.Range(0, points).Select(k =>
                    new StrokePoint(x0 + k * 1.5, y0 + 2 * Math.Sin((k + seed + i) / 3.0), 0.7))],
            });
        }
        return drawing;
    }

    private static double Fastest(Action run, int tries = 3)
    {
        run();
        var best = double.MaxValue;
        for (var i = 0; i < tries; i++)
        {
            var start = Stopwatch.GetTimestamp();
            run();
            best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        return best;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CleaningADenseDrawingStaysWithinItsBudget()
    {
        var drawing = Dense(300, 100, 0);
        var keys = new IReadOnlyList<Stroke>[] { Dense(300, 100, 7), Dense(300, 100, 13) };

        var plain = Fastest(() => LineCleaner.Clean(drawing, CleanupStrength.Medium));
        var guided = Fastest(() => LineCleaner.CleanGuided(drawing, keys));
        var measure = Fastest(() => LineCleaner.Measure(keys));
        output.WriteLine($"300 lines x 100 points: plain {plain:F0} ms, guided {guided:F0} ms, measuring the keys {measure:F0} ms");

        // Loose, as the suite's budgets are, and in a Debug build: the quadratic
        // version this guards against took 1.5 s plain and 6 s guided in Release.
        // Release budgets — CI runs Release — under the 1.5 s and 6 s the
        // quadratic version took there; a Debug run gets four times as long.
        // The doubling tests below catch the shape on any machine.
        Assert.True(plain < 1400 * BuildFactor, $"plain clean took {plain:F0} ms");
        Assert.True(guided < 5000 * BuildFactor, $"guided clean took {guided:F0} ms");
    }

#if DEBUG
    private const double BuildFactor = 4;
#else
    private const double BuildFactor = 1;
#endif

    [Fact]
    [Trait("Category", "Performance")]
    public void TwiceTheLinesCleansTowardKeysInAboutTwiceTheTime()
    {
        var a = Fastest(() => LineCleaner.CleanGuided(Dense(300, 100, 0), [Dense(300, 100, 7), Dense(300, 100, 13)]));
        var b = Fastest(() => LineCleaner.CleanGuided(Dense(600, 100, 0), [Dense(600, 100, 7), Dense(600, 100, 13)]));
        output.WriteLine($"guided: 300 lines {a:F0} ms, 600 lines {b:F0} ms, ratio {b / a:F2}");

        Assert.True(b < 3 * a + 50, $"600 lines cost {b / a:F2}x what 300 did");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TwiceTheLinesCostsAboutTwiceNotFourTimes()
    {
        var small = Dense(300, 100, 0);
        var large = Dense(600, 100, 0);

        var a = Fastest(() => LineCleaner.Clean(small));
        var b = Fastest(() => LineCleaner.Clean(large));
        output.WriteLine($"300 lines {a:F0} ms, 600 lines {b:F0} ms, ratio {b / a:F2}");

        Assert.True(b < 3 * a + 20, $"600 lines cost {b / a:F2}x what 300 did");
    }
}
