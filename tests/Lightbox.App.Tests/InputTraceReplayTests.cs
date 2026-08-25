using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// A recorded input trace, played back through the real canvas (B126, B254).
/// </summary>
/// <remarks>
/// <para>
/// <b>These prove the harness and the app's reaction to a capture — never the
/// pen.</b> The same split <c>InputTraceTests</c> makes for the instrument, and
/// for the same reason: no pen and no Windows exist here, so nothing in this
/// file can close B126. What it can hold is that a capture taken on the
/// reporter's machine still comes out of the leave grace the way the fix said
/// it would, on every build from now on.
/// </para>
/// <para>
/// <b>The counter is proved sensitive before it is trusted.</b> A test that
/// asserts "the ring was never torn down" passes just as well on a build where
/// nothing can tear it down — the shape of mistake
/// <c>.claude/skills/brush-measurement</c> exists for. So the churn assertion
/// is paired with <see cref="ADepartureThatLastsIsStillHonoured"/>, which
/// drives the same rig to a non-zero count.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class InputTraceReplayTests : BrushStateIsolated
{
    public InputTraceReplayTests() => InputTrace.ResetForTests();

    public override void Dispose()
    {
        InputTrace.ResetForTests();
        base.Dispose();
    }

    private static (Window Window, CanvasControl Canvas, MainViewModel Vm) NewRig() =>
        NewRig(800, 600);

    /// <summary>
    /// The same rig at a capture's own recorded size.
    /// </summary>
    /// <remarks>
    /// Every position in a trace is canvas-relative, so replaying a capture
    /// against a canvas of another size is a different run — most so for the
    /// enters and exits near an edge, which is what <c>OutsideCanvas</c> exists
    /// to make loud. A capture that records its rig gets to be replayed on it.
    /// </remarks>
    private static (Window Window, CanvasControl Canvas, MainViewModel Vm) NewRig(
        double width, double height)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        var canvas = new CanvasControl();
        var window = new Window
        {
            Width = width > 0 ? width : 800,
            Height = height > 0 ? height : 600,
            Content = canvas,
        };
        vm.SnapshotChanged += s => canvas.UpdateSnapshot(s);
        canvas.PaintStarted += vm.BeginStroke;
        canvas.PaintMoved += vm.MoveStrokeBatch;
        canvas.PaintEnded += vm.EndStroke;
        window.Show();
        vm.PublishSnapshot();
        // Without a laid-out canvas every recorded position is "outside" it and
        // the geometry counter would be noise rather than a signal.
        Assert.True(canvas.Bounds.Width > 0, "the canvas has no bounds to replay against");
        return (window, canvas, vm);
    }

    // ---- the stream the reporter's machine actually produces -------------------------

    /// <summary>
    /// The measured shape of Windows Ink's echo, as three of the reporter's
    /// traces recorded it.
    /// </summary>
    /// <remarks>
    /// Every exit is followed by a <em>different device</em> entering — 663,
    /// then 2,763, then 1,448 of them, not one followed by the same device — at
    /// a median gap of 0.5 ms and a rate of up to 39 a second. The pen reports
    /// fractional coordinates, real pressure and tilt; the phantom mouse reports
    /// whole pixels and Avalonia's default 0.50 pressure, which is how the two
    /// were told apart in the first place.
    /// </remarks>
    private static List<InputTrace.Entry> EchoChurn(double seconds, double perSecond = 39)
    {
        var entries = new List<InputTrace.Entry>
        {
            Pen(0, InputTrace.Kind.Enter, 400.37, 300.91),
        };
        var period = 1.0 / perSecond;
        var cycles = (int)(seconds * perSecond);
        for (var i = 0; i < cycles; i++)
        {
            var t = 0.001 + i * period;
            // Drifting by a fraction of a pixel, because the hand is not still
            // either — and because an unmoving ring would let a teardown hide.
            var x = 400.37 + i * 0.11;
            var y = 300.91 + i * 0.07;
            entries.Add(Pen(t, InputTrace.Kind.Exit, x, y));
            entries.Add(Phantom(t + 0.0005, InputTrace.Kind.Enter, Math.Round(x), Math.Round(y)));
            entries.Add(Phantom(t + 0.0009, InputTrace.Kind.Move, Math.Round(x), Math.Round(y)));
            entries.Add(Phantom(t + 0.0013, InputTrace.Kind.Exit, Math.Round(x), Math.Round(y)));
            entries.Add(Pen(t + 0.0018, InputTrace.Kind.Enter, x, y));
            entries.Add(Pen(t + 0.0022, InputTrace.Kind.Move, x, y));
        }
        return entries;
    }

    private static InputTrace.Entry Pen(double t, InputTrace.Kind kind, double x, double y) =>
        new(t, kind, PointerType.Pen, 1, (float)x, (float)y, 0.63f, -8, 3, KeyModifiers.None, null);

    private static InputTrace.Entry Phantom(double t, InputTrace.Kind kind, double x, double y) =>
        new(t, kind, PointerType.Mouse, 2, (float)x, (float)y, 0.5f, 0, 0, KeyModifiers.None, null);

    [AvaloniaFact]
    public void TheMeasuredEchoChurnNeverTearsTheRingDown()
    {
        var (window, canvas, _) = NewRig();
        var churn = EchoChurn(seconds: 3);

        var result = InputTraceReplay.Replay(churn, window, canvas);

        // Three seconds of the thing the artist called a flicker: 117 departures,
        // none of which the hand made. Before the leave grace every one of them
        // dropped the ring; the complaint was that it strobed.
        Assert.True(result.Exits >= 117, $"exits {result.Exits}");
        Assert.Equal(2, result.Devices);
        Assert.Equal(0, result.HoverTeardowns);
        Assert.Equal(0, result.OutsideCanvas);
        window.Close();
    }

    [AvaloniaFact]
    public void ADepartureThatLastsIsStillHonoured()
    {
        var (window, canvas, _) = NewRig();
        var entries = EchoChurn(seconds: 1);
        var last = entries[^1].Seconds;
        // The hand really goes away. The longest genuine departure in the
        // reporter's traces was 16 seconds; this is one, which is already twenty
        // times the grace.
        entries.Add(Pen(last + 0.001, InputTrace.Kind.Exit, 420.5, 310.25));
        entries.Add(Pen(last + 1.001, InputTrace.Kind.Enter, 120.5, 90.25));

        var result = InputTraceReplay.Replay(entries, window, canvas);

        // Exactly one: the grace swallowed every false departure in the second of
        // churn before it and none of the real one. Without this the assertion
        // above would pass on a build that could not drop the ring at all.
        Assert.Equal(1, result.HoverTeardowns);
        window.Close();
    }

    [AvaloniaFact]
    public void ThePenAndItsPhantomStayTwoDevicesWithTheirOwnAxes()
    {
        var (window, canvas, _) = NewRig();

        var result = InputTraceReplay.Replay(EchoChurn(seconds: 0.5), window, canvas);

        // The discrimination B126 turned on: if a replay collapsed the two
        // streams into one pointer, every capture would look like a clean pen
        // and the fixture would agree with any build.
        Assert.Equal(2, result.Devices);
        Assert.True(result.Moves > 0, "no moves replayed");
        window.Close();
    }

    [AvaloniaFact]
    public void NothingButInputIsRaisedAtTheCanvas()
    {
        var (window, canvas, _) = NewRig();
        var entries = EchoChurn(seconds: 0.2);
        var noise = entries.Count;
        entries.Add(new InputTrace.Entry(
            0.3, InputTrace.Kind.PopupOpened, PointerType.Mouse, -1, 0, 0, 0, 0, 0,
            KeyModifiers.None, "Submenu of “Follows the rig”"));
        entries.Add(new InputTrace.Entry(
            0.31, InputTrace.Kind.Stall, PointerType.Mouse, -1, 0, 0, 0, 0, 0,
            KeyModifiers.None, "the UI thread was blocked for 6103 ms"));

        var result = InputTraceReplay.Replay(entries, window, canvas);

        // Counted rather than dropped in silence: a capture that is mostly
        // popups and stalls would otherwise replay almost nothing and still
        // report a quiet minute.
        Assert.Equal(noise, result.Replayed);
        Assert.Equal(2, result.Skipped);
        window.Close();
    }

    [AvaloniaFact]
    public void ACaptureTakenAgainstAnotherCanvasSaysSoRatherThanReplayingQuietly()
    {
        var (window, canvas, _) = NewRig();
        // A 4K canvas's coordinates, replayed on an 800×600 rig. Every position
        // means something else, and the enters and exits most of all.
        var entries = new List<InputTrace.Entry>
        {
            Pen(0.0, InputTrace.Kind.Enter, 2100.5, 1300.25),
            Pen(0.1, InputTrace.Kind.Move, 2101.5, 1301.25),
        };

        var result = InputTraceReplay.Replay(entries, window, canvas);

        Assert.Equal(2, result.OutsideCanvas);
        window.Close();
    }

    [AvaloniaFact]
    public void ALostCaptureIsReplayedRatherThanSkipped()
    {
        var (window, canvas, _) = NewRig();
        var entries = new List<InputTrace.Entry>
        {
            Pen(0.0, InputTrace.Kind.Enter, 400.5, 300.5),
            new(0.1, InputTrace.Kind.CaptureLost, PointerType.Pen, 1, 0, 0, 0, 0, 0,
                KeyModifiers.None, null),
        };

        var result = InputTraceReplay.Replay(entries, window, canvas);

        // Capture loss mid-stroke is how a stroke ends when the OS takes the
        // pointer away, so a replay that quietly skipped it would end no stroke.
        Assert.Equal(2, result.Replayed);
        Assert.Equal(0, result.Skipped);
        window.Close();
    }

    // ---- the coalesced batch the paint path actually reads ----------------------------

    [AvaloniaFact]
    public void TheCoalescedBatchReachesTheStroke()
    {
        var (window, canvas, vm) = NewRig();
        var capture = InputTraceLog.ReadFile(Fixture("synthetic-coalesced-stroke.txt"));

        var result = InputTraceReplay.Replay(capture, window, canvas);

        Assert.Equal(2, capture.Version);
        // Thirteen recorded samples: twelve in contact and one from before the
        // press. A capture that recorded only delivered events would have had
        // three, and the stroke would have been a quarter of the drawing.
        Assert.Equal(13, result.Samples);
        Assert.Equal(800, capture.CanvasWidth);
        Assert.Equal(0, result.OutsideCanvas);
        Assert.Single(vm.PaintedCel().Strokes);
        window.Close();
    }

    [AvaloniaFact]
    public void AHoverSampleInTheBatchDoesNotJoinTheStroke()
    {
        var (window, canvas, vm) = NewRig();
        var capture = InputTraceLog.ReadFile(Fixture("synthetic-coalesced-stroke.txt"));

        InputTraceReplay.Replay(capture, window, canvas);

        // B185: a coalesced batch reaches back past the press into hover
        // positions, and after a refocus those are wherever the pen last was.
        // The fixture puts one at (100, 100) while the stroke is out at (400,
        // 300); if contact were ignored — or inferred from the surrounding press
        // rather than recorded — the mark would start with a dash across the
        // canvas.
        var stroke = Assert.Single(vm.PaintedCel().Strokes);
        var minX = stroke.Points.Min(p => p.X);
        var minY = stroke.Points.Min(p => p.Y);
        var first = stroke.Points[0];
        Assert.All(stroke.Points, p =>
        {
            Assert.True(p.X > first.X - 50, $"a point ran back to x {p.X:F1} from {first.X:F1}");
            Assert.True(p.Y > first.Y - 50, $"a point ran back to y {p.Y:F1} from {first.Y:F1}");
        });
        Assert.True(minX > 0 && minY > 0, $"stroke starts at {minX:F1},{minY:F1}");
        window.Close();
    }

    [AvaloniaFact]
    public void InferringContactInsteadOfReadingItIsWhatB185LooksLike()
    {
        var (window, canvas, vm) = NewRig();
        var capture = InputTraceLog.ReadFile(Fixture("synthetic-coalesced-stroke.txt"));

        // The same capture, replayed the way a v1 one has to be: contact deduced
        // from the surrounding press rather than read per sample. The hover
        // sample in the first batch is then delivered as though the pen were
        // down, and the mark starts with a dash across the canvas.
        InputTraceReplay.Replay(capture.Entries, window, canvas, trustRecordedContact: false);

        var stroke = Assert.Single(vm.PaintedCel().Strokes);
        var first = stroke.Points[0];
        // This test is why the contact column exists. If it ever goes green in
        // the other direction — no point running back — then the recorded flag
        // is buying nothing and AHoverSampleInTheBatchDoesNotJoinTheStroke is
        // passing on a build that could not fail it.
        Assert.Contains(stroke.Points, p => p.X < first.X - 50);
        window.Close();
    }

    // ---- the format the capture arrives in -------------------------------------------

    [Fact]
    public void EveryFieldSurvivesTheRoundTrip()
    {
        var entry = new InputTrace.Entry(
            12.3456789, InputTrace.Kind.Sample, PointerType.Pen, 1,
            413.62731f, 289.44189f, 0.6274510f, -8.5f, 3.25f,
            KeyModifiers.Shift | KeyModifiers.Control, "something\twith a tab",
            InContact: true, DeviceTime: 18446744073709551615);

        Assert.True(InputTraceLog.TryParse(InputTraceLog.Format(entry), out var back));

        // Position to the last bit, because position is the evidence: the pen
        // reports 89% fractional coordinates and the phantom mouse 0%, and that
        // is the whole of how the two were told apart. It is also invariant 2 —
        // every dab dynamic is seeded from the IEEE-754 bits of a position, so a
        // replay one bit off is a different mark.
        Assert.Equal(entry.X, back.X);
        Assert.Equal(entry.Y, back.Y);
        Assert.Equal(entry.Pressure, back.Pressure);
        Assert.Equal(entry.TiltX, back.TiltX);
        Assert.Equal(entry.TiltY, back.TiltY);
        Assert.Equal(entry.Seconds, back.Seconds);
        Assert.Equal(entry.Kind, back.Kind);
        Assert.Equal(entry.Device, back.Device);
        Assert.Equal(entry.DeviceId, back.DeviceId);
        Assert.Equal(entry.Modifiers, back.Modifiers);
        Assert.True(back.InContact);
        // The device clock is a raw platform stamp with no documented origin, so
        // it is carried rather than interpreted — including a value at the top of
        // its range, which a narrower type would have silently truncated.
        Assert.Equal(entry.DeviceTime, back.DeviceTime);
        // The tab is flattened rather than kept: it would have moved every
        // column after it, and a fixture that silently loses its last field is
        // worse than one that fails to load.
        Assert.Equal("something with a tab", back.Detail);
    }

    [Fact]
    public void AReportCarriesEnoughOfItselfToBeReplayed()
    {
        var previous = DiagnosticLog.DirectoryOverride;
        DiagnosticLog.DirectoryOverride = Path.Combine(
            Path.GetTempPath(), "lightbox-input-replay-" + Guid.NewGuid().ToString("N"));
        try
        {
            InputTrace.Arm();
            InputTrace.NoteForTests(
                0.25, InputTrace.Kind.Enter, PointerType.Pen, 1, 0.62f, -8, 3, deviceTime: 4096);
            InputTrace.NoteForTests(
                0.50, InputTrace.Kind.Sample, PointerType.Pen, 1, 0.71f, -8, 3, inContact: true);
            InputTrace.NoteForTests(0.75, InputTrace.Kind.Exit, PointerType.Mouse, 2, 0.5f);

            var path = InputTrace.WriteReport();
            Assert.NotNull(path);

            // The point of the whole exercise: what the reporter sends back is
            // the file the harness reads. If these two ever part company, a
            // capture becomes a wall of numbers again.
            var capture = InputTraceLog.ReadFile(path!);
            Assert.Equal(2, capture.Version);
            Assert.Equal(3, capture.Entries.Count);
            Assert.Equal(InputTrace.Kind.Enter, capture.Entries[0].Kind);
            Assert.Equal(4096ul, capture.Entries[0].DeviceTime);
            Assert.True(capture.Entries[1].InContact);
            Assert.Equal(PointerType.Mouse, capture.Entries[2].Device);
            Assert.Equal(0.75, capture.Entries[2].Seconds);
            Assert.False(capture.Wrapped);

            // The counters have to name the samples too, or a coalesced stream
            // and a throttled one still read the same (B189).
            Assert.Contains("coalesced samples", File.ReadAllText(path!));
        }
        finally
        {
            DiagnosticLog.DirectoryOverride = previous;
        }
    }

    [Fact]
    public void AFileWithNoReplaySectionSaysSoRatherThanReadingEmpty()
    {
        var reader = new StringReader("Lightbox input trace (B126/B254)\ncounters\n  moves 4\n");

        var ex = Assert.Throws<FormatException>(() => InputTraceLog.Read(reader));

        // An older build's report is the realistic case, and "0 events, all
        // assertions pass" is the outcome worth refusing.
        Assert.Contains(InputTraceLog.Marker, ex.Message);
    }

    [Fact]
    public void AMalformedEventFailsRatherThanLoadingHalfACapture()
    {
        var reader = new StringReader(
            InputTraceLog.Marker + "\n"
            + InputTraceLog.Columns + "\n"
            + InputTraceLog.Format(Pen(0.1, InputTrace.Kind.Enter, 400.5, 300.5)) + "\n"
            + "0.2\tMove\tPen\t1\tnot-a-number\t300.5\t0.6\t0\t0\tNone\t\t0\t0\n");

        var ex = Assert.Throws<FormatException>(() => InputTraceLog.Read(reader));

        // Named by line, because the first thing anybody does with a capture
        // that will not load is open it and look.
        Assert.Contains("line 4", ex.Message);
    }

    [AvaloniaFact]
    public void AV1CaptureStillLoadsAndStillReplays()
    {
        var (window, canvas, _) = NewRig();
        var capture = InputTraceLog.ReadFile(Fixture("synthetic-huion-echo.txt"));

        var result = InputTraceReplay.Replay(capture, window, canvas);

        // Captures already taken are still evidence. A v1 file has no contact
        // column and no device clock, so the replay infers the first and
        // synthesises the second — and says so rather than pretending the
        // capture recorded them.
        Assert.Equal(1, capture.Version);
        Assert.Equal(0, result.Samples);
        Assert.Equal(0, capture.CanvasWidth);
        Assert.True(result.Replayed > 0);
        window.Close();
    }

    /// <summary>
    /// The promoted mouse press does not keep the stroke the pen came to make
    /// (B256).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The reporter's capture, and the whole of the bug in two strokes.</b>
    /// The pen came back into proximity; Windows Ink's phantom mouse pressed at
    /// (921, 193) and the pen pressed at (921.6, 193.0) <b>63 ms later</b>. The
    /// mouse owned the stroke, so the move handler's ownership guard dropped
    /// every one of the pen's 238 in-contact samples — 210 delivered moves and
    /// the coalesced points riding with them — and the mark the artist drew for
    /// 1.1 seconds reached the record as <b>one point</b>. The second stroke
    /// in the same minute — pen already in proximity, no promoted press — was
    /// never affected, which is the reporter's <i>"on release and drawing again
    /// solves it"</i> measured rather than described.
    /// </para>
    /// <para>
    /// <b>Asserted on the record, not on the events.</b> Invariant 1: the stroke
    /// record is the document, so what was lost is points in a document, and
    /// counting delivered batches would pass on a build that delivered them and
    /// then dropped them.
    /// </para>
    /// <para>
    /// <b>This one IS an evidence anchor on B256</b>, unlike
    /// <c>StrokeAxisLockTests</c>, and the difference is what each proves. Those
    /// demonstrate a mechanism that could produce the reported shape; this
    /// replays the minute in which it actually happened and holds the outcome.
    /// The residual manual step is the reporter confirming the symptom is gone,
    /// which no capture can do — but the mark being lost is no longer a
    /// hypothesis about a machine this repository has not got.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ThePromotedMousePressDoesNotKeepTheStrokeThePenCameToMake()
    {
        var capture = InputTraceLog.ReadFile(
            Fixture("huion-pen-echo-press-steals-the-stroke.txt"));
        var (window, canvas, vm) = NewRig(capture.CanvasWidth, capture.CanvasHeight);

        InputTraceReplay.Replay(capture, window, canvas);

        var strokes = vm.PaintStrokes();
        Assert.Equal(2, strokes.Count);
        // One point before the fix, measured. The threshold is far above that
        // and far below the ~200 the pen's moves survive the sample-distance
        // filter as, so it fails on the bug and does not chase the filter.
        Assert.True(
            strokes[0].Points.Count > 50,
            $"the pen's mark reached the record as {strokes[0].Points.Count} points");
        // The sensitivity half: the stroke that never lost the race is the
        // control, so a build where no mark lands at all cannot pass.
        Assert.True(
            strokes[1].Points.Count > 50,
            $"the uncontested mark reached the record as {strokes[1].Points.Count} points");
        window.Close();
    }

    // ---- the captures kept in the tree ------------------------------------------------

    private static string FixtureDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "input-traces");

    private static string Fixture(string name) => Path.Combine(FixtureDirectory, name);

    [AvaloniaFact]
    public void EveryCheckedInCaptureReplaysCleanly()
    {
        var files = Directory.Exists(FixtureDirectory)
            ? Directory.GetFiles(FixtureDirectory, "*.txt")
            : [];
        // Not vacuous: an empty folder would let every assertion below pass
        // while the drop-in point quietly stopped existing.
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var capture = InputTraceLog.ReadFile(file);
            Assert.NotEmpty(capture.Entries);

            var (window, canvas, _) = NewRig();
            var result = InputTraceReplay.Replay(capture, window, canvas);
            // Every capture is at least readable, replayable and made of input —
            // a file that parsed but turned out to be all popups would replay
            // nothing and prove nothing.
            Assert.True(result.Replayed > 0, $"{Path.GetFileName(file)} replayed nothing");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheSyntheticCaptureKeepsItsRingThroughTheEchoAndDropsItAtTheEnd()
    {
        var (window, canvas, _) = NewRig();
        var capture = InputTraceLog.ReadFile(Fixture("synthetic-huion-echo.txt"));

        var result = InputTraceReplay.Replay(capture, window, canvas);

        Assert.Equal(2, result.Devices);
        // Four echo cycles the grace swallows, then a departure at 0.43 s that
        // nothing returns from — the ring goes once, at the far end of the gap.
        Assert.Equal(1, result.HoverTeardowns);
        // The cursor decision, the popup pair and the closing note.
        Assert.Equal(4, result.Skipped);
        window.Close();
    }
}
