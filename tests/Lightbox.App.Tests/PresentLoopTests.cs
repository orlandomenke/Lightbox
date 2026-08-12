using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// While the transport runs, the canvas keeps a compositor frame on request.
/// </summary>
/// <remarks>
/// <para>
/// <b>B164.</b> A frame was asked for once per publish, so during playback the
/// compositor ran at the scene's frame rate and in lock-step with it — twelve
/// ticks a second, each one triggered by the publish it was meant to show.
/// Nothing ran between them, so any wobble in when a publish landed went
/// straight to the screen.
/// </para>
/// <para>
/// <b>The owner's experiment is what identified it, and it is the fact every
/// earlier hypothesis got wrong.</b> Mashing Ctrl, mashing Space and switching
/// tools quickly all smooth playback; <em>holding a key down does not</em>, even
/// though a held key repeats about thirty times a second. Events in quantity do
/// not help. Repainting something does. Everything in the working list
/// invalidates a visual; nothing in the failing list does — which is also why
/// hovering a docker never helped.
/// </para>
/// <para>
/// <b>What these tests can and cannot do.</b> They pin the loop's contract: it
/// re-arms while playing, keeps exactly one request in flight, and stops dead
/// when playback does. They cannot show that the screen looks smoother — that
/// needs a compositor running against real hardware, which is the whole reason
/// this defect survived six rounds of headless measurement.
/// </para>
/// </remarks>
public class PresentLoopTests(ITestOutputHelper output)
{
    private static CanvasControl Shown()
    {
        var canvas = new CanvasControl();
        var window = new Window { Content = canvas, Width = 400, Height = 300 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return canvas;
    }

    private static long Requests(CanvasControl c) => c.AnimationFrames.Requested;

    /// <summary>
    /// The loop runs itself. Without this the compositor only ticks when a
    /// publish drags it awake, which is the defect.
    /// </summary>
    [AvaloniaFact]
    public void TurningItOnAsksForFramesWithoutAnythingElseHappening()
    {
        var canvas = Shown();
        canvas.ResetAnimationFrameCounters();

        canvas.KeepPresenting = true;
        for (var i = 0; i < 5; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var asked = Requests(canvas);
        output.WriteLine($"requests while playing, with no publishes and no input: {asked}");

        Assert.True(asked > 0,
            "the present loop asked for nothing — playback would be back to one "
            + "compositor tick per published frame, which is the bug");
    }

    /// <summary>
    /// It must stop. A loop that outlives playback would hold the compositor at
    /// display rate for the rest of the session, which is a battery and a
    /// scrolling-jank problem rather than a smoothness one.
    /// </summary>
    [AvaloniaFact]
    public void TurningItOffStopsTheLoop()
    {
        var canvas = Shown();
        canvas.KeepPresenting = true;
        for (var i = 0; i < 5; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        canvas.KeepPresenting = false;
        // Let any in-flight callback land and decline to re-arm.
        for (var i = 0; i < 3; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        canvas.ResetAnimationFrameCounters();
        for (var i = 0; i < 5; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var afterStopping = Requests(canvas);
        output.WriteLine($"requests after stopping: {afterStopping}");

        Assert.Equal(0, afterStopping);
    }

    /// <summary>
    /// Switching it on twice must not start a second loop — two loops would
    /// double the request rate and each would re-arm the other for ever.
    /// </summary>
    [AvaloniaFact]
    public void TurningItOnTwiceDoesNotStartTwoLoops()
    {
        var canvas = Shown();
        canvas.KeepPresenting = true;
        canvas.ResetAnimationFrameCounters();

        canvas.KeepPresenting = true;   // no-op: already on
        canvas.KeepPresenting = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var asked = Requests(canvas);
        output.WriteLine($"requests after three 'on' writes and one pass: {asked}");

        Assert.True(asked <= 1,
            $"{asked} requests in one pass — a second loop is running, and each will "
            + "re-arm the other for the rest of the session");
    }

    /// <summary>
    /// Off is the default, so nothing spins while the artist is drawing.
    /// </summary>
    [AvaloniaFact]
    public void ACanvasThatIsNotPlayingAsksForNothing()
    {
        var canvas = Shown();
        canvas.ResetAnimationFrameCounters();

        for (var i = 0; i < 5; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(canvas.KeepPresenting);
        Assert.Equal(0, Requests(canvas));
    }
}
