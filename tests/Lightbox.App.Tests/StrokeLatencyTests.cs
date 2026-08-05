using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Does the canvas keep up with the pen — measured in <b>events and frames</b> rather than in
/// milliseconds.
/// </summary>
/// <remarks>
/// <para>
/// B73 reported fast strokes trailing behind the pen, and its entry said no test could reach it:
/// "a test that reproduced it would have to measure <em>latency from input to pixel</em>, which
/// nothing here does and which Xvfb cannot supply honestly". That is true of latency in
/// milliseconds — there is no compositor, no vsync and no display, so a wall-clock number here
/// measures the machine and not the application.
/// </para>
/// <para>
/// <b>It is not true of latency counted in pointer events</b>, and that is the reframe that makes
/// this testable. "The ink trails the pen" is a statement about <em>order</em>: pen events are
/// waiting in the queue and the frame on screen does not contain them yet. Whether the frame the
/// application publishes includes the events already queued when it was published is a fact about
/// dispatcher priority, it is exactly reproducible, and it needs no clock at all.
/// </para>
/// <para>
/// Two clock-free quantities, both of which were wrong:
/// </para>
/// <list type="number">
/// <item><b>Staleness.</b> Avalonia's priorities run <c>Render &gt; Loaded &gt; Default &gt; Input
/// &gt; Background</c> — <c>Default</c> is <em>above</em> <c>Input</c>, unlike WPF, where it is
/// below. <c>RequestSnapshot</c> posted at <c>Default</c>, so a publish jumped the queue and drew a
/// frame that was already behind by however many pen events were pending.</item>
/// <item><b>Work amplification.</b> Because the publish ran between events instead of after them,
/// a burst of <em>n</em> events produced about <em>n</em> publishes rather than one. A publish is
/// the expensive half — tens of milliseconds with an effect brush on a large canvas — so at a high
/// pen rate the queue grows faster than it drains, and the lag compounds along the stroke. That is
/// the mechanism an artist feels as the mark chasing the cursor.</item>
/// </list>
/// <para>
/// Posting at <c>Input</c> fixes both, and it is the priority rather than a queue of our own
/// because <c>Input</c> is FIFO: the publish lands behind the events already waiting, so one frame
/// covers the burst and is current — and ahead of events that arrive later, so a continuous drag
/// still renders and cannot starve. <c>Background</c> drains the burst too and can starve.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class StrokeLatencyTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int Size = 40;

    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,   // stabilisation lags on purpose; that is not what this measures
            ColorHex = "#101010",
            BrushSize = Size,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
        };
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>A fast drag: the points a pen would report, spaced as a quick flick.</summary>
    private static IReadOnlyList<(double X, double Y)> Flick(int events = 12) =>
        [.. Enumerable.Range(1, events).Select(i => (120 + i * 55.0, 260 + Math.Sin(i * 0.4) * 40))];

    /// <summary>Is there ink within <paramref name="radius"/> of this point?</summary>
    private static bool Inked(SKBitmap frame, double x, double y, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var px = (int)Math.Round(x) + dx;
                var py = (int)Math.Round(y) + dy;
                if (px < 0 || py < 0 || px >= frame.Width || py >= frame.Height) continue;
                if (frame.GetPixel(px, py).Red < 140) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Drive a burst the way input arrives — queued, then drained once — and record every frame.
    /// </summary>
    /// <remarks>
    /// Posting the <c>MoveStroke</c> calls at <c>Input</c> priority is the faithful part. Calling
    /// them in a plain loop and draining afterwards coalesces trivially and would pass on the broken
    /// code: the defect only exists when a publish and a pending pointer event compete, which is
    /// what a real burst is and what a synchronous loop never produces.
    /// </remarks>
    private static (int Publishes, List<SKBitmap> Frames) Burst(
        MainViewModel vm, IReadOnlyList<(double X, double Y)> points)
    {
        var frames = new List<SKBitmap>();
        var publishes = 0;
        void OnSnapshot(RenderSnapshot s)
        {
            publishes++;
            if (SKBitmap.FromImage(s.Image) is { } bmp) frames.Add(bmp);
        }

        vm.BeginStroke(points[0].X, points[0].Y, 1);
        Dispatcher.UIThread.RunJobs();

        vm.SnapshotChanged += OnSnapshot;
        foreach (var (x, y) in points.Skip(1))
        {
            Dispatcher.UIThread.Post(() => vm.MoveStroke(x, y, 1), DispatcherPriority.Input);
        }
        Dispatcher.UIThread.RunJobs();
        vm.SnapshotChanged -= OnSnapshot;
        return (publishes, frames);
    }

    /// <summary>
    /// The frame that lands while pen events are already queued must contain them.
    /// </summary>
    /// <remarks>
    /// This is B73 stated without a clock. Before the fix the first frame of a burst was published
    /// before a single queued <c>MoveStroke</c> had run, so it showed the stroke as it was at
    /// pen-down while eleven events waited — the ink visibly behind the cursor, which is precisely
    /// what was reported.
    /// </remarks>
    [AvaloniaFact]
    public void TheFirstFrameOfABurstIsNotStale()
    {
        var vm = Ready();
        var points = Flick();
        var (publishes, frames) = Burst(vm, points);

        try
        {
            Assert.NotEmpty(frames);
            var last = points[^1];
            var first = frames[0];

            // Where the pen has been by the time anything is drawn. A frame is stale if it does not
            // reach the last queued event; the radius is the brush's own, since a dab is that wide.
            var reaches = Inked(first, last.X, last.Y, Size / 2 + 4);
            output.WriteLine(
                $"{points.Count - 1} queued events → {publishes} publishes; "
                + $"first frame {(reaches ? "reaches" : "does NOT reach")} the last event at "
                + $"({last.X:0}, {last.Y:0})");

            // Not vacuous: the frame has to contain the stroke's beginning, or "does not reach the
            // end" would also be true of a frame with nothing in it at all.
            Assert.True(
                Inked(first, points[0].X, points[0].Y, Size),
                "the frame has no ink at the start of the stroke, so it is not a picture of the stroke");

            Assert.True(
                reaches,
                $"the first published frame of a {points.Count - 1}-event burst stops short of the "
                + "last event — the canvas is showing a stroke that is behind the pen (B73)");
        }
        finally
        {
            foreach (var f in frames) f.Dispose();
        }
    }

    /// <summary>
    /// A burst of pen events costs about one frame, not one frame each.
    /// </summary>
    /// <remarks>
    /// The compounding half. A publish is the expensive part of an event, so publishing per event
    /// rather than per batch multiplies the work at exactly the moment there is least time for it —
    /// and unlike staleness this one gets worse the faster the artist draws, which is why the report
    /// was about <em>fast</em> strokes.
    /// </remarks>
    [AvaloniaFact]
    public void APenBurstIsOneFrameNotOnePerEvent()
    {
        var vm = Ready();
        var points = Flick(16);
        var (publishes, frames) = Burst(vm, points);
        foreach (var f in frames) f.Dispose();

        var events = points.Count - 1;
        output.WriteLine($"{events} queued events → {publishes} publishes ({publishes / (double)events:P0})");

        // Generous: the point is the order of magnitude. One publish per event is the defect; a
        // couple for a burst is coalescing working.
        Assert.True(
            publishes <= 3,
            $"a burst of {events} pointer events produced {publishes} publishes — the frame is being "
            + "rendered between events instead of after them, so the work multiplies exactly when "
            + "there is least time for it (B73)");
    }

    /// <summary>
    /// The published frame reaches the pen at every brush size, including the expensive ones.
    /// </summary>
    /// <remarks>
    /// A large brush costs more per event, so if anything is going to fall behind it is this. Run
    /// after the burst has fully drained, so what is asserted is that the mark is <em>complete</em>
    /// — a spatial check that would also catch a tail of dabs never being stamped, which is a
    /// different mechanism with the same symptom.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(12)]
    [InlineData(40)]
    [InlineData(147)]
    public void WhenTheBurstHasDrainedTheMarkReachesThePen(double size)
    {
        var vm = Ready();
        vm.BrushSize = size;
        Dispatcher.UIThread.RunJobs();

        var points = Flick();
        var (_, frames) = Burst(vm, points);
        try
        {
            Assert.NotEmpty(frames);
            var last = points[^1];
            var reaches = Inked(frames[^1], last.X, last.Y, (int)(size / 2) + 4);
            output.WriteLine($"{size:0} px brush: final frame {(reaches ? "reaches" : "stops short of")} the pen");
            Assert.True(reaches, $"after the burst drained, a {size:0} px stroke stops short of the last event");
        }
        finally
        {
            foreach (var f in frames) f.Dispose();
        }
    }

    /// <summary>
    /// Smoothing lags on purpose, and this says how much so nobody mistakes it for B73.
    /// </summary>
    /// <remarks>
    /// The report explicitly covered strokes with the stabiliser off, so stabilisation is not the
    /// cause — but it <em>is</em> a real distance between the pen and the ink, and a future
    /// investigation that measures the two together will find a number that does not match either
    /// story. Measured separately here, and deliberately not asserted tightly: what is asserted is
    /// only that the smoothed stroke still arrives, so this cannot become the excuse for a
    /// regression in the unsmoothed one.
    /// </remarks>
    [AvaloniaFact]
    public void SmoothingsOwnLagIsMeasuredSeparately()
    {
        foreach (var smooth in new[] { false, true })
        {
            var vm = Ready();
            vm.SmoothStrokes = smooth;
            Dispatcher.UIThread.RunJobs();

            var points = Flick();
            var (_, frames) = Burst(vm, points);
            try
            {
                var last = points[^1];
                var shortfall = 0;
                while (shortfall < 200 && !Inked(frames[^1], last.X, last.Y, Size / 2 + 4 + shortfall))
                {
                    shortfall += 4;
                }
                output.WriteLine(
                    $"smoothing {(smooth ? "on " : "off")}: ink reaches within "
                    + $"{Size / 2 + 4 + shortfall} px of the pen"
                    + (shortfall == 0 ? " (no extra allowance needed)" : $" — {shortfall} px beyond the dab"));
                Assert.True(shortfall < 200, $"with smoothing {(smooth ? "on" : "off")} the stroke never arrives");
            }
            finally
            {
                foreach (var f in frames) f.Dispose();
            }
        }
    }
}
