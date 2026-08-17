using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The input trace (B126/B254, Q115): what it records and, more importantly,
/// what it concludes.
/// </summary>
/// <remarks>
/// <para>
/// <b>These prove the instrument, not the fix</b> — the split the render
/// report's <c>StrokeToScreenTests</c> made, and the reason neither set is an
/// evidence anchor on the bug it serves. Nothing here can close B126: no pen
/// and no Windows exist in this repository. What they can hold is that a trace
/// taken on the reporter's machine says the right thing about what it saw, so
/// the one round-trip that diagnosis costs is not spent on a report that
/// mis-attributes.
/// </para>
/// <para>
/// Every verdict is asserted by its <em>claim</em> rather than its whole
/// sentence, so the wording can be improved without a test rewrite, while the
/// attribution — which mechanism this trace blames — cannot drift silently.
/// </para>
/// </remarks>
public class InputTraceTests : IDisposable
{
    public InputTraceTests() => InputTrace.ResetForTests();

    public void Dispose() => InputTrace.ResetForTests();

    /// <summary>Events at chosen times, so the rates are arithmetic rather than timing.</summary>
    private static void Hover(double seconds, PointerType device, int id, InputTrace.Kind kind) =>
        InputTrace.NoteForTests(seconds, kind, device, id);

    // ---- the disarmed cost ----------------------------------------------------------

    [Fact]
    public void NothingIsRecordedUntilItIsArmed()
    {
        // The whole disarmed cost is a volatile read: this sits on the pointer
        // path, which invariant 6 does not let us spend on a feature nobody
        // switched on.
        Assert.False(InputTrace.Armed);
        Hover(0.1, PointerType.Pen, 1, InputTrace.Kind.Move);

        Assert.Equal(0, InputTrace.Summarize().Events);
    }

    [Fact]
    public void ArmingTwiceStartsAFreshTraceRatherThanAppending()
    {
        InputTrace.Arm();
        Hover(0.1, PointerType.Pen, 1, InputTrace.Kind.Move);
        InputTrace.Arm();

        // A second run of the ritual must not carry the first run's counters, or
        // a "before and after the fix" comparison silently sums the two.
        Assert.Equal(0, InputTrace.Summarize().Events);
    }

    // ---- the three mechanisms it exists to tell apart --------------------------------

    [Fact]
    public void TwoInterleavedPointerIdsAreCalledASecondDevice()
    {
        InputTrace.Arm();
        // The driver-emulated-mouse hypothesis: a pen and a mouse stream
        // alternating, which is what would flip the cursor and steal a flyout's
        // pointer at the same time.
        for (var i = 0; i < 10; i++)
        {
            Hover(i * 1.0, PointerType.Pen, 1, InputTrace.Kind.Move);
            Hover(i * 1.0 + 0.5, PointerType.Mouse, 2, InputTrace.Kind.Move);
        }

        var summary = InputTrace.Summarize();
        Assert.Equal(2, summary.Devices.Count);
        Assert.True(summary.Alternations >= 19, $"alternations {summary.Alternations}");
        Assert.Contains(summary.Verdicts, v => v.Contains("Two pointer streams interleave"));
        // Named specifically enough to act on: which device, and what to try.
        Assert.Contains(summary.Verdicts, v => v.Contains("Pen id 1") && v.Contains("Mouse id 2"));
    }

    [Fact]
    public void OneSteadyStreamIsNotCalledASecondDevice()
    {
        InputTrace.Arm();
        for (var i = 0; i < 20; i++) Hover(i * 1.0, PointerType.Pen, 1, InputTrace.Kind.Move);

        var summary = InputTrace.Summarize();
        Assert.Equal(0, summary.Alternations);
        // The trap this guards: a verdict that fires on every trace is a verdict
        // that says nothing, and it would send the fix after the wrong mechanism.
        Assert.DoesNotContain(summary.Verdicts, v => v.Contains("Two pointer streams interleave"));
    }

    [Fact]
    public void RepeatedEnterAndExitIsCalledChurn()
    {
        InputTrace.Arm();
        // A pen held still, flapping in and out of proximity: ten crossings in
        // ten seconds is sixty a minute, well over the threshold.
        for (var i = 0; i < 5; i++)
        {
            Hover(i * 2.0, PointerType.Pen, 1, InputTrace.Kind.Exit);
            Hover(i * 2.0 + 1.0, PointerType.Pen, 1, InputTrace.Kind.Enter);
        }

        var summary = InputTrace.Summarize();
        Assert.Equal(5, summary.Enters);
        Assert.Equal(5, summary.Exits);
        Assert.Contains(summary.Verdicts, v => v.Contains("left and re-entered"));
    }

    [Fact]
    public void OneCrossingInAMinuteIsNotCalledChurn()
    {
        InputTrace.Arm();
        Hover(1.0, PointerType.Pen, 1, InputTrace.Kind.Enter);
        Hover(59.0, PointerType.Pen, 1, InputTrace.Kind.Move);

        // Moving the pointer onto the canvas once is what using the application
        // looks like. A trace that calls that a fault has cried wolf.
        Assert.DoesNotContain(InputTrace.Summarize().Verdicts, v => v.Contains("left and re-entered"));
    }

    [Fact]
    public void ACursorLightboxNeverChangedIsNotBlamedOnItsOwnCursorLogic()
    {
        InputTrace.Arm();
        // The decisive case for B126, and the owner's first real trace is
        // exactly this: 30 s of hovering, one decision, zero reassignments.
        InputTrace.CursorDecided("Paint");
        for (var i = 0; i < 50; i++) Hover(i, PointerType.Pen, 1, InputTrace.Kind.Move);

        var summary = InputTrace.Summarize();
        Assert.Equal(1, summary.CursorDecisionChanges);
        Assert.Contains(summary.Verdicts, v => v.Contains("nothing here is flipping it deliberately"));
        // **This wording was wrong and the correction is the point of the
        // test.** It used to conclude that the fix therefore belonged upstream.
        // The cause is upstream — Windows Ink's echo stream — but the exits it
        // produces are what hand the arrow back, and Lightbox decides how to
        // react to those. Sending it upstream would have parked a fixable bug.
        Assert.Contains(summary.Verdicts, v => v.Contains("the cure need not be"));
    }

    [Fact]
    public void ARepeatedCursorDecisionIsRecordedOnceRatherThanPerEvent()
    {
        InputTrace.Arm();
        // A 240 Hz hover asks this thousands of times a minute. Recording each
        // one would bury the changes — which are the entire signal — in the
        // repeats, and would wrap the ring inside a few seconds.
        for (var i = 0; i < 100; i++) InputTrace.CursorDecided("Paint");
        InputTrace.CursorDecided("Move");

        Assert.Equal(2, InputTrace.Summarize().CursorDecisionChanges);
    }

    [Fact]
    public void APopupThatDiesImmediatelyIsCalledCollapsed()
    {
        InputTrace.Arm();
        var flyout = new object();
        InputTrace.Popup(flyout, "Flyout", open: true);
        InputTrace.Popup(flyout, "Flyout", open: false);
        // Long enough to be allowed a verdict at all — the popup's own life is
        // the thing being measured, not the trace's.
        Hover(30, PointerType.Pen, 1, InputTrace.Kind.Move);

        var summary = InputTrace.Summarize();
        Assert.Equal(1, summary.PopupsOpened);
        Assert.Equal(1, summary.PopupsCollapsed);
        Assert.NotNull(summary.ShortestPopupMs);
        Assert.Contains(summary.Verdicts, v => v.Contains("collapsed rather than dismissed"));
    }

    [Fact]
    public void NothingConclusiveSaysSoRatherThanInventingAVerdict()
    {
        InputTrace.Arm();
        // One device, no churn, one popup that lived: the trace that did not
        // catch the bug. Saying "nothing here distinguishes them" is the honest
        // answer, and it tells the reporter to hover differently rather than
        // leaving them to read a wall of events.
        InputTrace.CursorDecided("Paint");
        InputTrace.CursorAssigned("Paint");
        InputTrace.CursorAssigned("Move");
        for (var i = 0; i < 20; i++) Hover(i * 0.5, PointerType.Pen, 1, InputTrace.Kind.Move);

        var summary = InputTrace.Summarize();
        Assert.Single(summary.Verdicts);
        Assert.Contains("Nothing in this trace distinguishes", summary.Verdicts[0]);
    }

    [Fact]
    public void ATraceTooShortToMeanAnythingSaysSoInsteadOfExtrapolating()
    {
        InputTrace.Arm();
        // Twelve alternations in 0.6 s is what an accidental key press records,
        // and it extrapolated to "1241/min" with a confident mechanism attached
        // until this guard existed — found by reading a sample report, which is
        // why the sample is worth generating rather than trusting the asserts.
        for (var i = 0; i < 6; i++)
        {
            Hover(i * 0.1, PointerType.Pen, 1, InputTrace.Kind.Move);
            Hover(i * 0.1 + 0.05, PointerType.Mouse, 2, InputTrace.Kind.Move);
        }

        var summary = InputTrace.Summarize();
        // The counters are still honest — they are counts, not rates.
        Assert.Equal(12, summary.Moves);
        Assert.True(summary.Alternations > 0);
        // The conclusions are not.
        Assert.Single(summary.Verdicts);
        Assert.Contains("too short to conclude", summary.Verdicts[0]);
        Assert.DoesNotContain(summary.Verdicts, v => v.Contains("/min"));
    }

    // ---- telling a freeze from a pointer that went elsewhere -------------------------

    [Fact]
    public void ALongSilenceWithNoStallIsThePointerBeingElsewhere()
    {
        InputTrace.Arm();
        // The shape of the owner's first real trace: 16 seconds in which the
        // canvas saw nothing, because the pointer was over a menu. Reported as
        // a freeze, and this is the reading that says it was not one.
        Hover(1, PointerType.Pen, 1, InputTrace.Kind.Exit);
        Hover(17.5, PointerType.Pen, 1, InputTrace.Kind.Enter);
        for (var i = 0; i < 10; i++) Hover(18 + i, PointerType.Pen, 1, InputTrace.Kind.Move);

        var summary = InputTrace.Summarize();
        Assert.Equal(0, summary.Stalls);
        Assert.True(summary.LongestSilenceMs > 16000, $"silence {summary.LongestSilenceMs}");
        Assert.Contains(summary.Verdicts, v => v.Contains("nothing was frozen"));
        Assert.DoesNotContain(summary.Verdicts, v => v.Contains("really did stop answering"));
    }

    [Fact]
    public void TheSameSilenceWithAStallInItIsAFreeze()
    {
        InputTrace.Arm();
        Hover(1, PointerType.Pen, 1, InputTrace.Kind.Exit);
        InputTrace.NoteStallForTests(9, blockedMs: 8000);
        Hover(17.5, PointerType.Pen, 1, InputTrace.Kind.Enter);
        for (var i = 0; i < 10; i++) Hover(18 + i, PointerType.Pen, 1, InputTrace.Kind.Move);

        var summary = InputTrace.Summarize();
        // Same silence, opposite conclusion — which is the entire point of
        // measuring the dispatcher rather than inferring from the gap.
        Assert.Equal(1, summary.Stalls);
        Assert.Contains(summary.Verdicts, v => v.Contains("really did stop answering"));
        Assert.DoesNotContain(summary.Verdicts, v => v.Contains("nothing was frozen"));
    }

    // ---- what the report has to carry for a comparison to be possible ----------------

    [Fact]
    public void TheSummaryCarriesTheCountersAFixWouldBeMeasuredAgainst()
    {
        InputTrace.Arm();
        Hover(0.5, PointerType.Pen, 1, InputTrace.Kind.Enter);
        Hover(1.0, PointerType.Pen, 1, InputTrace.Kind.Move);
        Hover(1.5, PointerType.Mouse, 2, InputTrace.Kind.Move);
        InputTrace.NoteForTests(2.0, InputTrace.Kind.Move, PointerType.Pen, 1, pressure: 0.62f, tiltX: 12);

        var summary = InputTrace.Summarize();
        // Every number the bug entry's before/after table quotes has to be here,
        // or "did the fix work" goes back to being an opinion.
        Assert.Equal(4, summary.Events);
        Assert.True(summary.Seconds >= 2.0);
        Assert.Equal(1, summary.Enters);
        Assert.Equal(2, summary.Devices.Count);

        var pen = summary.Devices.First(d => d.Type == PointerType.Pen);
        Assert.Equal(3, pen.Events);
        Assert.Equal(0.62f, pen.MaxPressure, 3);
        // Whether the pen reports tilt at all is the other question the same
        // hover answers, and it is what the tilt work needs to know first.
        Assert.True(pen.TiltSeen);
    }

    // ---- the wiring, driven through the real control ---------------------------------

    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 640, 360, 12, 72, "#ffffff", false));
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// The hooks really are on the pointer path.
    /// </summary>
    /// <remarks>
    /// The class tests above drive <see cref="InputTrace.NoteForTests"/>, which
    /// would keep passing if every hook in <c>CanvasControl</c> were deleted.
    /// This is the one that would notice — a silent instrument is worse than
    /// none, because the empty report reads as "nothing happened" rather than
    /// as "nothing was recorded".
    /// </remarks>
    [AvaloniaFact]
    public void TheCanvasFeedsTheTraceWhileItIsArmed()
    {
        var (window, _) = Open();
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        var centre = canvas.TranslatePoint(
            new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;

        InputTrace.Arm();
        window.MouseMove(centre);
        window.MouseMove(centre + new Vector(10, 10));
        Pump();

        var summary = InputTrace.Summarize();
        // Moves specifically, not "events > 0": entering the canvas alone
        // satisfies that, so the looser assertion passed with the move hook
        // deleted — checked by deleting it. A guard that survives the thing it
        // guards being removed is not a guard.
        Assert.True(summary.Moves > 0, "no pointer moves reached the trace");
        Assert.True(summary.Enters > 0, "no enter reached the trace");
        // The cursor decision rides along, which is the half B126 turns on.
        Assert.True(summary.CursorDecisionChanges > 0, "no cursor decision was recorded");
    }

    /// <summary>
    /// Leaving the canvas is recorded, and it is the event both bugs turn on.
    /// </summary>
    /// <remarks>
    /// <b>Written because deleting the Exit hook left every other test green.</b>
    /// An adversarial pass removed each hook in turn and ran the suite; this one
    /// went unnoticed, which made it the most dangerous hook in the file —
    /// enter/exit churn is one of the three mechanisms the report claims to
    /// discriminate, so an Exit that silently never fired would have produced a
    /// confident "not churn" verdict from an instrument that could not have
    /// seen churn.
    /// </remarks>
    [AvaloniaFact]
    public void LeavingTheCanvasIsRecorded()
    {
        var (window, _) = Open();
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        var centre = canvas.TranslatePoint(
            new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;

        InputTrace.Arm();
        window.MouseMove(centre);
        Pump();
        // Out of the canvas and into the chrome at the top of the window, which
        // is a real boundary crossing rather than a synthetic Exit.
        window.MouseMove(new Point(centre.X, 2));
        Pump();

        var summary = InputTrace.Summarize();
        Assert.True(summary.Enters > 0, "no enter reached the trace");
        Assert.True(summary.Exits > 0, "no exit reached the trace");
    }

    /// <summary>
    /// A press and a release are recorded, so a trace can show where capture
    /// was in force.
    /// </summary>
    /// <remarks>
    /// B126's sharpest observation is that the flicker never happens *while
    /// drawing* — which is exactly where <c>Pointer.Capture</c> is held. A
    /// trace that could not show the press and the release could not show that
    /// boundary, which is the one piece of timing the whole hypothesis rests on.
    /// </remarks>
    [AvaloniaFact]
    public void APressAndAReleaseAreRecorded()
    {
        var (window, _) = Open();
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        var centre = canvas.TranslatePoint(
            new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;

        InputTrace.Arm();
        window.MouseMove(centre);
        window.MouseDown(centre, MouseButton.Left);
        Pump();
        window.MouseUp(centre, MouseButton.Left);
        Pump();

        var kinds = InputTrace.KindsForTests();
        Assert.Contains(InputTrace.Kind.Press, kinds);
        Assert.Contains(InputTrace.Kind.Release, kinds);
    }

    /// <summary>
    /// The popup wiring observes real flyouts, rather than only the calls the
    /// tests make themselves.
    /// </summary>
    /// <remarks>
    /// <b>The gap this closes is the one that would have been invisible.</b> The
    /// popup tests above call <see cref="InputTrace.Popup"/> directly, so they
    /// pass whether or not <c>WirePopups</c> ever registers a handler — and B254
    /// *is* the popup half of this pair. Opening a real
    /// <see cref="Avalonia.Controls.Flyout"/> is what proves the class handler
    /// is attached to the property the application's own flyouts use.
    /// </remarks>
    [AvaloniaFact]
    public void ARealFlyoutOpeningIsSeenByTheTrace()
    {
        var (window, _) = Open();
        var flyout = new Avalonia.Controls.Flyout
        {
            Content = new Avalonia.Controls.TextBlock { Text = "hover" },
        };

        InputTrace.Arm();
        flyout.ShowAt(window);
        Pump();

        var opened = InputTrace.Summarize().PopupsOpened;
        Assert.True(opened > 0, "the trace did not see a real flyout open");

        flyout.Hide();
        Pump();
        // And closing it gives the life the collapse test is about.
        Assert.NotNull(InputTrace.Summarize().ShortestPopupMs);
    }

    /// <summary>
    /// A right-click context menu is seen.
    /// </summary>
    /// <remarks>
    /// <b>The first real trace reported "popups opened 0" for a session that
    /// opened two.</b> The owner right-clicked the Layers docker and opened a
    /// fly-out from a menu item; the instrument watched
    /// <c>FlyoutBase.IsOpen</c> and <c>ToolTip.IsOpen</c>, and a
    /// <see cref="Avalonia.Controls.ContextMenu"/> has neither. A counter that
    /// reads zero when the thing happened twice is worse than no counter, and
    /// B254 is the bug it was zero about.
    /// </remarks>
    [AvaloniaFact]
    public void ARightClickContextMenuIsSeenByTheTrace()
    {
        var (window, _) = Open();
        var target = new Avalonia.Controls.Border { Width = 40, Height = 40 };
        var menu = new Avalonia.Controls.ContextMenu
        {
            ItemsSource = new[] { new Avalonia.Controls.MenuItem { Header = "Something" } },
        };
        target.ContextMenu = menu;
        ((Avalonia.Controls.Panel)window.Content!).Children.Add(target);
        Pump();

        InputTrace.Arm();
        menu.Open(target);
        Pump();

        Assert.True(InputTrace.Summarize().PopupsOpened > 0,
            "the trace did not see a context menu open");
    }

    /// <summary>
    /// A menu item's fly-out is seen — the other half of the same miss.
    /// </summary>
    /// <remarks>
    /// A submenu is <see cref="Avalonia.Controls.MenuItem.IsSubMenuOpenProperty"/>,
    /// which is neither a flyout nor a tooltip, and is exactly what the owner
    /// described opening when the trace recorded nothing.
    /// </remarks>
    [AvaloniaFact]
    public void AMenuItemSubmenuIsSeenByTheTrace()
    {
        var (window, _) = Open();
        var item = new Avalonia.Controls.MenuItem
        {
            Header = "Layers",
            ItemsSource = new[] { new Avalonia.Controls.MenuItem { Header = "Child" } },
        };
        ((Avalonia.Controls.Panel)window.Content!).Children.Add(
            new Avalonia.Controls.Menu { ItemsSource = new[] { item } });
        Pump();

        InputTrace.Arm();
        item.IsSubMenuOpen = true;
        Pump();

        Assert.True(InputTrace.Summarize().PopupsOpened > 0,
            "the trace did not see a submenu open");
    }

    /// <summary>The same, for a tooltip — the other thing that opens on hover.</summary>
    [AvaloniaFact]
    public void ARealToolTipOpeningIsSeenByTheTrace()
    {
        var (window, _) = Open();
        var owner = new Avalonia.Controls.Border();
        Avalonia.Controls.ToolTip.SetTip(owner, "a tip");

        InputTrace.Arm();
        Avalonia.Controls.ToolTip.SetIsOpen(owner, true);
        Pump();

        Assert.True(InputTrace.Summarize().PopupsOpened > 0,
            "the trace did not see a real tooltip open");
    }

    /// <summary>The same path, disarmed: the hooks must record nothing at all.</summary>
    [AvaloniaFact]
    public void ADisarmedTraceRecordsNothingFromTheRealCanvas()
    {
        var (window, _) = Open();
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        var centre = canvas.TranslatePoint(
            new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;

        window.MouseMove(centre);
        window.MouseMove(centre + new Vector(10, 10));
        Pump();

        Assert.Equal(0, InputTrace.Summarize().Events);
    }

    /// <summary>
    /// The key arms it, and the key writes it — the whole ritual, without a
    /// menu.
    /// </summary>
    /// <remarks>
    /// Asserted through <see cref="ShortcutMap"/> rather than by pressing F9
    /// literally, so an artist who has rebound the command still has this
    /// guarantee — and so the test does not quietly become a test of one key.
    /// </remarks>
    [AvaloniaFact]
    public void TheShortcutArmsTheTraceAndThenWritesIt()
    {
        var (window, vm) = Open();
        var map = new ShortcutMap();
        var binding = map.Find("diagnostics.inputTrace");
        Assert.NotNull(binding);
        var key = binding!.Current?.Key ?? Key.F9;

        var previous = DiagnosticLog.DirectoryOverride;
        DiagnosticLog.DirectoryOverride = Path.Combine(
            Path.GetTempPath(), "lightbox-input-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Pump();
            Assert.True(InputTrace.Armed, "the shortcut did not arm the trace");
            Assert.Contains("Recording", vm.AiStatus ?? "");
            // The title, because the status line it used to rely on lives in a
            // bar that is hidden whenever AI assistance is off — which is how
            // this feature came to look completely dead to the person it was
            // built for. Nothing else writes the title, so it cannot be hidden.
            Assert.Contains("recording an input trace", window.Title ?? "");
            Assert.Contains("Recording", vm.PenDiagnostic ?? "");

            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Pump();
            Assert.False(InputTrace.Armed, "the shortcut did not stop the trace");
            Assert.Contains("Input trace written to", vm.AiStatus ?? "");
            Assert.Contains("Input trace written to", vm.PenDiagnostic ?? "");
            Assert.DoesNotContain("recording", window.Title ?? "");
        }
        finally
        {
            DiagnosticLog.DirectoryOverride = previous;
        }
    }

    /// <summary>
    /// The command is in the registry, so it can be found, searched and rebound.
    /// </summary>
    /// <remarks>
    /// <c>CLAUDE.md</c>'s first row: a command wired straight to a gesture is
    /// invisible to the whole configuration system. A tablet driver claiming
    /// F9 is exactly the case that needs the rebind to exist.
    /// </remarks>
    [Fact]
    public void TheTraceCommandIsRebindable()
    {
        var found = new ShortcutMap().Find("diagnostics.inputTrace");
        Assert.NotNull(found);
        Assert.Equal("Help", found!.Category);
        Assert.NotNull(found.Default);
    }

    [Fact]
    public void TheReportNamesTheBugsItServes()
    {
        var previous = DiagnosticLog.DirectoryOverride;
        DiagnosticLog.DirectoryOverride = Path.Combine(
            Path.GetTempPath(), "lightbox-input-trace-" + Guid.NewGuid().ToString("N"));
        try
        {
            InputTrace.Arm();
            Hover(0.5, PointerType.Pen, 1, InputTrace.Kind.Move);

            var path = InputTrace.WriteReport();
            Assert.NotNull(path);
            var text = File.ReadAllText(path!);

            // A report that lands in a bug thread has to say which bug it is
            // about and what build produced it, or it is a wall of numbers
            // nobody can file.
            Assert.Contains("B126", text);
            Assert.Contains("B254", text);
            Assert.Contains("verdicts", text);
            Assert.Contains("counters", text);
            // Writing the report stops the trace: a trace left armed keeps
            // recording the reporter's ordinary work into the next one.
            Assert.False(InputTrace.Armed);
        }
        finally
        {
            DiagnosticLog.DirectoryOverride = previous;
        }
    }
}
