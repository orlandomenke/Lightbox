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
    // The owner's document.
    private const int Width = 3840;
    private const int Height = 2160;

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
        for (var i = 0; i < 3; i++)
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
        output.WriteLine($"one frame-cache miss at {Width}x{Height}, 5 heavy strokes: {best:0.#} ms (best of 3)");
        output.WriteLine($"  a pen event arrives every ~5 ms, so that is {best / 5:0} events' worth");
        output.WriteLine($"  the captures' worst build was 3170-6354 ms");

        // Deliberately not a budget. The assertion is only that the measurement
        // happened and is a real duration — what it MEANS is B332's business and
        // belongs in the ledger beside the capture, not in a threshold here that
        // would need moving on every machine.
        Assert.True(best > 0, "the render did not take measurable time, so nothing was measured");
    }

    /// <summary>
    /// <b>The comparison that turns the number above into a verdict.</b> A miss
    /// is only a stall if it is enormous beside the frame build it happens
    /// inside — the captures put that at a 2 ms median.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void AMissCostsVastlyMoreThanTheFrameBuildItHappensInside()
    {
        var frame = FiveStrokes();
        using (var warm = FrameBitmapCache.RenderDetached(new Frame(), 64, 64)) { }

        var missMs = double.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            using var bmp = FrameBitmapCache.RenderDetached(frame, Width, Height);
            missMs = Math.Min(
                missMs,
                (System.Diagnostics.Stopwatch.GetTimestamp() - t0)
                * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }

        // The median build in all four of the owner's captures, 2026-08-27.
        const double TypicalBuildMs = 2.0;
        output.WriteLine($"a miss costs {missMs:0.#} ms against a {TypicalBuildMs} ms typical build");
        output.WriteLine($"  ratio: {missMs / TypicalBuildMs:0}x");

        Assert.True(
            missMs > TypicalBuildMs * 20,
            $"a frame-cache miss ({missMs:0.#} ms) is not dramatically dearer than the typical "
            + $"build ({TypicalBuildMs} ms), so it cannot be the multi-second stall B332 blames "
            + "it for and that entry needs a different mechanism");
    }
}
