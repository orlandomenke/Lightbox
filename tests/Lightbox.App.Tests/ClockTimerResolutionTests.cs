using Avalonia.Headless.XUnit;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// While playback runs, the clock asks Windows for 1 ms timer resolution, and
/// gives it back when playback stops.
/// </summary>
/// <remarks>
/// <para>
/// The clock is a <c>DispatcherTimer</c>, which on Windows wakes in steps of the
/// system timer, about 15.6 ms by default. A 24 fps frame is 41.7 ms, which no
/// whole number of steps makes: the tick wakes at 31 or 47 ms, the lateness
/// builds, and the catch-up pacing drops a frame. The lab (play-steady,
/// 2026-10-11) showed 19-23 fps of 24 with everything prepared, on the
/// 10-layer document as on the 30-layer one — a clock problem, not a drawing
/// one. Media players ask for 1 ms while they play for exactly this reason.
/// </para>
/// <para>
/// <b>Counted, and released on every way out.</b> The request is a process
/// state Windows keeps until it is given back; two clocks, or a start without
/// its stop, must neither leave it on nor turn it off under the other.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ClockTimerResolutionTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void PlayingHoldsOneMillisecondTimingAndStoppingGivesItBack()
    {
        var before = TimerResolution.Holders;
        var clock = new PlaybackClock();

        clock.Start(24);
        Assert.Equal(before + 1, TimerResolution.Holders);

        clock.Stop();
        Assert.Equal(before, TimerResolution.Holders);
    }

    [AvaloniaFact]
    public void StartingAgainWithoutStoppingHoldsItOnce()
    {
        var before = TimerResolution.Holders;
        var clock = new PlaybackClock();

        clock.Start(24);
        clock.Start(12); // a change of fps mid-play restarts the clock
        Assert.Equal(before + 1, TimerResolution.Holders);

        clock.Stop();
        clock.Stop();
        Assert.Equal(before, TimerResolution.Holders);
    }

    [AvaloniaFact]
    public void TwoClocksDoNotTurnItOffUnderEachOther()
    {
        var before = TimerResolution.Holders;
        var a = new PlaybackClock();
        var b = new PlaybackClock();

        a.Start(24);
        b.Start(24);
        a.Stop();

        Assert.Equal(before + 1, TimerResolution.Holders);
        b.Stop();
        Assert.Equal(before, TimerResolution.Holders);
    }
}
