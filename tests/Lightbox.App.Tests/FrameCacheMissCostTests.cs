using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What a frame-cache miss actually costs, measured rather than inferred from a
/// stall in a capture (B332).
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim under test.</b> `FrameBitmapCache.Get` counts a miss and then
/// calls `Render` <em>synchronously on the calling thread</em>. Mid-stroke that
/// thread is the UI thread, inside the phase the render report calls
/// <em>describing it (pass list, stack fold, cel fetches)</em>. Four captures on
/// the owner's machine carry a `building each frame` worst of <b>3.2 to 6.4
/// seconds</b> against a median of 2 ms, and the owner's verdict on B322's fix —
/// on both arms — was *"the preview did still jump"*.
/// </para>
/// <para>
/// <b>Why this is measured here and not read off a capture.</b> The report
/// splits a build's phases by MEAN, over every build. A mean across a thousand
/// two-millisecond builds and one six-second one describes the thousand. No
/// number in any capture says which phase the six seconds was in, so the
/// hypothesis had to be tested against the operation itself.
/// </para>
/// <para>
/// <b>This does not prove the stall IS a miss.</b> It establishes what a miss
/// costs on this hardware at this document size. The capture-side half is the
/// worst-build attribution added alongside it, which names the phase and says
/// whether a miss happened inside that particular frame.
/// </para>
/// </remarks>
public class FrameCacheMissCostTests(ITestOutputHelper output)
{
    // **Half the owner's document on each side, a quarter of the pixels.**
    // The 4K figures are recorded in B332 and were taken once, deliberately; a
    // guard that runs on every CI job does not need to re-measure them. At
    // 3840x2160 this rendered eight full frames of 33 MB each and the CI runner
    // died on "MSBUILD error MSB4166: Child node exited prematurely" twice --
    // an out-of-memory dressed as an infrastructure failure, which is the worst
    // kind to leave in a suite because it reads as somebody else's flake.
    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>The brush the owner reported the jump on: large, with a live effect.</summary>
    private static BrushSettings Heavy() => new()
    {
        Size = 70, Hardness = 0.6, Flow = 0.7, Opacity = 1,
        WetEdge = 0.6, Granulation = 0.4,
    };

    private static Stroke LongStroke(int seed)
    {
        var pts = new List<StrokePoint>(600);
        double x = 200 + seed * 90, y = 300 + seed * 240, heading = -0.15 + seed * 0.2;
        for (var i = 0; i < 600; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.0012;
            x += 4.2 * Math.Cos(heading);
            y += 4.2 * Math.Sin(heading);
        }

        return new Stroke
        {
            Tool = ToolKind.Brush, Color = "#203040", Brush = Heavy(), Points = pts,
        };
    }

    /// <summary>
    /// The scene from the captures: five strokes of a heavy brush on one frame.
    /// </summary>
    private static Frame FiveStrokes()
    {
        var frame = new Frame();
        for (var i = 0; i < 5; i++) frame.Strokes.Add(LongStroke(i));
        return frame;
    }

    /// <summary>
    /// <b>What one miss costs, at the size the owner draws at.</b> Reported
    /// rather than bounded: a budget here would be a second guess at a number
    /// nobody has, and the point of this test is to supply the number.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void AFrameCacheMissRendersTheWholeFrameAndThisIsWhatItCosts()
    {
        var frame = FiveStrokes();

        // Warm the engine so the first render does not also pay for jitting the
        // dab walk and building the grain tile.
        using (var warm = FrameBitmapCache.RenderDetached(new Frame(), 64, 64)) { }

        var runs = new List<double>();
        for (var i = 0; i < 2; i++)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            using var bmp = FrameBitmapCache.RenderDetached(frame, Width, Height);
            var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0)
                     * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            runs.Add(ms);
            output.WriteLine($"  render {i + 1}: {ms:0.#} ms");
        }

        var best = runs.Min();
        output.WriteLine("");
        output.WriteLine($"one frame-cache miss at {Width}x{Height}, 5 heavy strokes: {best:0.#} ms (best of 2)");
        output.WriteLine($"  a pen event arrives every ~5 ms, so that is {best / 5:0} events' worth");
        output.WriteLine($"  the captures' worst build was 3170-6354 ms");

        // **The comparison that turns the number into a verdict**, folded into the
        // one measurement rather than paying for a second set of renders. The
        // median build in all four of the owner's captures was about 2 ms.
        const double TypicalBuildMs = 2.0;
        output.WriteLine($"  against a {TypicalBuildMs} ms typical build that is {best / TypicalBuildMs:0}x");

        Assert.True(
            best > TypicalBuildMs * 20,
            $"a frame-cache miss ({best:0.#} ms) is not dramatically dearer than the typical "
            + $"build ({TypicalBuildMs} ms), so it cannot be the multi-second stall B332 blames "
            + "it for and that entry needs a different mechanism");
    }

}
