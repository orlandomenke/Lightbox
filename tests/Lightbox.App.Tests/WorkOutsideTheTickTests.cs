using Lightbox.App.Services;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The render report accounts for drawing the frame to the screen, not just
/// compositing it.
/// </summary>
/// <remarks>
/// <para>
/// <b>B161, and the reason four rounds of captures could not close the
/// arithmetic.</b> The tick breakdown measures compositing. Drawing the
/// composited frame to the screen happens <em>outside</em> the tick, was
/// measured all along by <c>CanvasControl</c>'s draw op, and was printed only
/// inside a section guarded on <c>Presents &gt; 0</c> — a counter only the
/// durable-frame path increments, and B130 turned that off by default. So on
/// every real machine the number was collected and discarded.
/// </para>
/// <para>
/// <b><c>PerformanceMonitor.FrameMs</c> already warned about exactly this
/// mistake</b> in its own summary: <i>"on a large document it is usually the
/// larger of the two … reporting headroom from compositing alone said smooth
/// while the canvas ran at 34 fps."</i> The report was doing that, and the
/// captures showed a tick inside its budget beside a clock 200 ms late with no
/// way to reconcile them.
/// </para>
/// </remarks>
public class WorkOutsideTheTickTests(ITestOutputHelper output)
{
    private static IReadOnlyList<TickProfile.PhaseStats> Tick(double msPerTick) =>
    [
        new(TickProfile.Phase.Compose, 100, msPerTick * 100, msPerTick * 1.2),
    ];

    private static string Section(double renderMs, double fps = 12, double tickMs = 34)
    {
        RenderReport.ResetForTests();
        var path = RenderReport.WriteStartup(RenderReportTests.Facts(
            tickPhases: Tick(tickMs), tickCount: 100,
            scene: (5, 2, 11, fps), renderMedianMs: renderMs))!;
        var text = File.ReadAllText(path);
        var start = text.IndexOf("where the tick's time went", StringComparison.Ordinal);
        Assert.True(start >= 0, text);
        return text[start..];
    }

    [Fact]
    public void TheDrawIsReportedBesideTheTickAndAddedToIt()
    {
        var text = Section(renderMs: 38);
        output.WriteLine(text);

        Assert.Contains("drawing it to the screen", text);
        Assert.Contains("OUTSIDE the tick", text);
        // 34 + 38 = 72, and the sum is the point: neither number alone is the
        // budget, which is the mistake this section exists to stop.
        Assert.Contains("72", text);
    }

    /// <summary>
    /// The case every capture so far has actually been in: a tick that looks
    /// affordable, a draw that is not, and no headroom once they are added.
    /// </summary>
    [Fact]
    public void NoHeadroomIsSaidPlainlyRatherThanLeftAsArithmetic()
    {
        var text = Section(renderMs: 45);   // 34 + 45 = 79 of an 83.3 ms budget
        output.WriteLine(text);

        Assert.Contains("no headroom", text);
        Assert.Contains("dropped frames above are the consequence", text);
    }

    /// <summary>
    /// And the opposite conclusion has to be reachable, or the first is a rubber
    /// stamp — this is the shape that would send the next session looking
    /// somewhere neither number covers.
    /// </summary>
    [Fact]
    public void PlentyOfHeadroomSendsTheReaderElsewhere()
    {
        var text = Section(renderMs: 4, tickMs: 12);   // 16 of 83.3
        output.WriteLine(text);

        Assert.Contains("Comfortable", text);
        Assert.Contains("neither of these two numbers covers", text);
    }

    /// <summary>
    /// A report from a session that never drew has nothing to say here, and says
    /// nothing rather than printing a confident zero.
    /// </summary>
    [Fact]
    public void NothingIsClaimedWhenTheDrawWasNeverMeasured()
    {
        var text = Section(renderMs: 0);
        output.WriteLine(text);

        Assert.DoesNotContain("drawing it to the screen", text);
    }
}
