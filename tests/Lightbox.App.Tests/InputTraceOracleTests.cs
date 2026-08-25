using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The capture as an oracle: what this build decides, against what the
/// recording says was decided (B126).
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim is about churn and about regression, never about the reporter's
/// document.</b> A cursor decision depends on the tool, the gizmo, the guides
/// and the artwork's own geometry, none of which a trace records — so these
/// assert the two things that survive that: the answer did not go away and come
/// back while the hand was still, and a sequence this rig once produced it still
/// produces. <c>InputTraceOracle</c>'s own remarks carry the reasoning.
/// </para>
/// <para>
/// <b>Every metric here is shown able to move before it is trusted.</b>
/// <see cref="TheEchoStormNeverChangesTheCanvasMind"/> asserts a zero, and would
/// pass on a build where nothing could ever be counted; it is paired with
/// <see cref="AnAnswerThatGoesAwayAndComesBackIsAFlipFlop"/>, which drives the
/// same rig to a non-zero count through the same instrument.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class InputTraceOracleTests : BrushStateIsolated
{
    public InputTraceOracleTests() => InputTrace.ResetForTests();

    public override void Dispose()
    {
        InputTrace.ResetForTests();
        base.Dispose();
    }

    private static (Window Window, CanvasControl Canvas) NewRig()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var canvas = new CanvasControl();
        var window = new Window { Width = 800, Height = 600, Content = canvas };
        vm.SnapshotChanged += s => canvas.UpdateSnapshot(s);
        canvas.PaintStarted += vm.BeginStroke;
        canvas.PaintMoved += vm.MoveStrokeBatch;
        canvas.PaintEnded += vm.EndStroke;
        window.Show();
        vm.PublishSnapshot();
        return (window, canvas);
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "input-traces", name);

    private static InputTrace.Entry Pen(double t, InputTrace.Kind kind, double x, double y) =>
        new(t, kind, PointerType.Pen, 1, (float)x, (float)y, 0.63f, -8, 3, KeyModifiers.None, null);

    /// <summary>A capture standing in for a file, so a test can choose what was recorded.</summary>
    private static InputTraceLog.Capture Capture(params InputTrace.Entry[] entries) =>
        new(2, entries, 800, 600, 100, false);

    private static InputTrace.Entry Decided(double t, string from, string to) =>
        new(t, InputTrace.Kind.CursorDecided, PointerType.Mouse, -1, 0, 0, 0, 0, 0,
            KeyModifiers.None, $"{from}→{to}");

    // ---- churn: the figure that survives not having the reporter's document ----------

    [AvaloniaFact]
    public void TheEchoStormNeverChangesTheCanvasMind()
    {
        var (window, canvas) = NewRig();
        var capture = InputTraceLog.ReadFile(Fixture("synthetic-huion-echo.txt"));

        var verdict = InputTraceOracle.Run(capture, window, canvas);

        // The finding B126's report exists to produce: through the whole echo
        // storm — the pen and its phantom mouse trading the canvas, every exit
        // followed by a different device entering — the canvas made its mind up
        // once and never changed it. If Lightbox is not moving the cursor, the
        // flicker the artist sees is below the application, which is what sends
        // that bug upstream instead of round another guess.
        Assert.Single(verdict.Replayed);
        Assert.Equal(0, verdict.ReplayedFlipFlops);
        Assert.True(verdict.Replay.Replayed > 20, verdict.Report());
        window.Close();
    }

    [AvaloniaFact]
    public void AnAnswerThatGoesAwayAndComesBackIsAFlipFlop()
    {
        var (window, canvas) = NewRig();
        var hover = new[]
        {
            Pen(0.0, InputTrace.Kind.Enter, 400.37, 300.91),
            Pen(0.1, InputTrace.Kind.Move, 401.44, 301.62),
            Pen(0.2, InputTrace.Kind.Move, 402.51, 302.33),
        };

        // The same hover three times over, with the tool's intent changed
        // between and then put back. Nothing about the pointer differs; the
        // application's answer does.
        InputTrace.Arm();
        try
        {
            canvas.PointerIntent = CanvasCursorKind.Paint;
            InputTraceReplay.Replay(hover, window, canvas);
            canvas.PointerIntent = CanvasCursorKind.Pick;
            InputTraceReplay.Replay(hover, window, canvas);
            canvas.PointerIntent = CanvasCursorKind.Paint;
            InputTraceReplay.Replay(hover, window, canvas);
        }
        finally
        {
            InputTrace.Disarm();
        }

        var decisions = InputTraceOracle.DecisionsIn(InputTrace.EntriesInOrder());
        // Paint → Pick → Paint. Without this the zero asserted above would pass
        // on a build where no decision could ever be recorded at all.
        Assert.Equal(3, decisions.Count);
        Assert.Equal(1, InputTraceOracle.FlipFlops(decisions));
        Assert.Equal("Paint", decisions[0].To);
        Assert.Equal("Pick", decisions[1].To);
        Assert.Equal("Paint", decisions[2].To);
        window.Close();
    }

    [Fact]
    public void AFlipFlopIsAReturn_NotMerelyAChange()
    {
        InputTraceOracle.Decision[] climbing =
            [D("Default"), D("Paint"), D("Pick"), D("Move")];
        InputTraceOracle.Decision[] returning =
            [D("Default"), D("Paint"), D("Default"), D("Paint")];

        // Four changes either way. The first is a pointer crossing things; the
        // second is the application arguing with itself over a hand that has not
        // moved, and only the second is what the artist calls a flicker.
        Assert.Equal(0, InputTraceOracle.FlipFlops(climbing));
        Assert.Equal(2, InputTraceOracle.FlipFlops(returning));

        static InputTraceOracle.Decision D(string to) =>
            new(0, "whatever", to, Assigned: true);
    }

    // ---- the comparison, both ways ---------------------------------------------------

    [AvaloniaFact]
    public void ASequenceThisBuildProducesIsASequenceItStillProduces()
    {
        var hover = new[]
        {
            Pen(0.0, InputTrace.Kind.Enter, 400.37, 300.91),
            Pen(0.1, InputTrace.Kind.Move, 401.44, 301.62),
        };

        // Learn what this build decides, then hand that back to it as the
        // recording. This is the golden mode with the golden taken live, so the
        // test says what it means — the comparator agrees with itself — without
        // pinning a cursor kind that is allowed to change for good reasons.
        var (first, firstCanvas) = NewRig();
        var learned = InputTraceOracle.Run(Capture(hover), first, firstCanvas);
        Assert.NotEmpty(learned.Replayed);
        first.Close();

        var golden = Capture([.. hover,
            .. learned.Replayed.Select(d => Decided(d.Seconds, d.From, d.To))]);

        var (second, secondCanvas) = NewRig();
        var verdict = InputTraceOracle.Run(golden, second, secondCanvas);

        Assert.True(verdict.Matches, verdict.Report());
        Assert.Null(verdict.FirstDivergence);
        Assert.Equal(verdict.Recorded.Count, verdict.MatchedPrefix);
        second.Close();
    }

    [AvaloniaFact]
    public void ADivergenceSaysWhichDecisionPartedAndWhatEachSideSaid()
    {
        var (window, canvas) = NewRig();
        var capture = Capture(
            Pen(0.0, InputTrace.Kind.Enter, 400.37, 300.91),
            Pen(0.1, InputTrace.Kind.Move, 401.44, 301.62),
            // The recording says the canvas chose the eyedropper here. This rig
            // has no such tool in hand, so it will not.
            Decided(0.2, "start", "Pick"));

        var verdict = InputTraceOracle.Run(capture, window, canvas);

        Assert.False(verdict.Matches);
        Assert.NotNull(verdict.FirstDivergence);
        Assert.Contains("decision 0", verdict.FirstDivergence);
        Assert.Contains("Pick", verdict.FirstDivergence);
        Assert.Equal(0, verdict.MatchedPrefix);
        window.Close();
    }

    [AvaloniaFact]
    public void ARecordingThatRanLongerThanThisBuildSaysSoRatherThanMatching()
    {
        var (window, canvas) = NewRig();
        var (_, probeCanvas) = NewRig();
        var hover = new[]
        {
            Pen(0.0, InputTrace.Kind.Enter, 400.37, 300.91),
            Pen(0.1, InputTrace.Kind.Move, 401.44, 301.62),
        };
        var learned = InputTraceOracle.Run(Capture(hover), window, probeCanvas);

        // The same opening decision, and then one more the recording made and
        // this build does not. A prefix comparison alone would call that a
        // match, which is the failure mode worth refusing: the interesting
        // divergence is usually the app doing *less* than it used to.
        var capture = Capture([.. hover,
            .. learned.Replayed.Select(d => Decided(d.Seconds, d.From, d.To)),
            Decided(9.0, learned.Replayed[^1].To, "Move")]);

        var verdict = InputTraceOracle.Run(capture, window, canvas);

        Assert.False(verdict.Matches);
        Assert.Contains("in all", verdict.FirstDivergence!);
        window.Close();
    }

    [AvaloniaFact]
    public void TheReportPrintsBothSidesLineForLine()
    {
        var (window, canvas) = NewRig();
        var capture = Capture(
            Pen(0.0, InputTrace.Kind.Enter, 400.37, 300.91),
            Pen(0.1, InputTrace.Kind.Move, 401.44, 301.62),
            Decided(0.2, "start", "Pick"));

        var report = InputTraceOracle.Run(capture, window, canvas).Report();

        // The question after "these differ" is always "differ how". A message
        // that answers only the first sends the reader back to the capture file
        // to do by hand what the harness already has in memory.
        Assert.Contains("recorded 1 decisions", report);
        Assert.Contains("[0] recorded", report);
        Assert.Contains("replayed", report);
        window.Close();
    }

    // ---- the sweep every capture gets for free ----------------------------------------

    [AvaloniaFact]
    public void NoCheckedInCaptureMakesThisBuildChangeItsMindAndChangeItBack()
    {
        var files = Directory.GetFiles(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "input-traces"), "*.txt");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var capture = InputTraceLog.ReadFile(file);
            var (window, canvas) = NewRig();

            var verdict = InputTraceOracle.Run(capture, window, canvas);

            // The claim B126's fix makes, checked against every minute anybody
            // has ever sent in, on every build from now on. A capture that
            // *should* flip — a pointer genuinely crossing a guide and back — is
            // a reason to move it out of this sweep and give it its own
            // assertion, never a reason to soften this one: the whole value is
            // that a new capture is checked without anybody remembering to.
            Assert.Equal(0, verdict.ReplayedFlipFlops);
            window.Close();
        }
    }

    // ---- the instrument is not left running -------------------------------------------

    [AvaloniaFact]
    public void TheOracleLeavesTheTraceDisarmed()
    {
        var (window, canvas) = NewRig();

        InputTraceOracle.Run(
            Capture(Pen(0.0, InputTrace.Kind.Enter, 400.37, 300.91)), window, canvas);

        // A trace left armed records the next test's work into this one's, and
        // the failure surfaces somewhere else entirely — which is why Arm's own
        // tests exist and why this one is here rather than trusted.
        Assert.False(InputTrace.Armed);
        window.Close();
    }
}
