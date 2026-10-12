using Avalonia.Headless.XUnit;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Playback paced by the screen's refresh: each frame is shown on the refresh
/// nearest the moment it is due, and none is dropped for lateness.
/// </summary>
/// <remarks>
/// <para>
/// The clock was a timer queued at <c>Input</c>, deliberately behind the
/// screen's commit (B150) — so, as its own remarks say, no tick was ever seen
/// arriving on time. The lab measured it (play-steady with play.pacing,
/// 2026-10-12): every tick late by 16-26 ms on average, about one refresh, and
/// 14-52 frames dropped in a play even with every frame prepared. A clock that
/// starts each 41.7 ms frame a refresh behind turns any hiccup into a drop.
/// </para>
/// <para>
/// Driven by the window's per-refresh callback instead, the clock asks, on each
/// refresh, which frame the wall clock says is due — the same pacing arithmetic,
/// with the frame counted due when the refresh is within half a refresh of it.
/// At 60 Hz, 24 fps shows as 3:2 pulldown, as video does; nothing is lost.
/// </para>
/// <para>
/// The refresh and the wall clock are both simulated here: the headless
/// dispatcher does not deliver either in real time, which is why the timer's
/// magnitude was never testable. The pacing is arithmetic, and is tested as such.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ScreenPacedClockTests : BrushStateIsolated
{
    private const double Refresh = 1000.0 / 60;

    /// <summary>A screen: callbacks asked for are run at the next simulated refresh.</summary>
    private sealed class Screen
    {
        private List<Action<TimeSpan>> _pending = [];
        public double NowMs;

        public void Request(Action<TimeSpan> callback) => _pending.Add(callback);

        public int Waiting => _pending.Count;

        /// <summary>Advance to the next refresh and run what was asked for.</summary>
        public void Refresh(double afterMs = ScreenPacedClockTests.Refresh)
        {
            NowMs += afterMs;
            var run = _pending;
            _pending = [];
            foreach (var callback in run) callback(TimeSpan.FromMilliseconds(NowMs));
        }
    }

    private static (PlaybackClock Clock, Screen Screen, List<(double At, int Frames)> Ticks) Clock()
    {
        var screen = new Screen();
        var clock = new PlaybackClock(screen.Request, () => TimeSpan.FromMilliseconds(screen.NowMs));
        var ticks = new List<(double, int)>();
        clock.Tick += frames => ticks.Add((screen.NowMs, frames));
        return (clock, screen, ticks);
    }

    [AvaloniaFact]
    public void ASecondAt24FpsOn60HzShowsEveryFrameOnce()
    {
        var (clock, screen, ticks) = Clock();
        clock.Start(24);
        for (var i = 0; i < 60; i++) screen.Refresh();
        clock.Stop();

        Assert.Equal(24, ticks.Sum(t => t.Frames));
        Assert.All(ticks, t => Assert.Equal(1, t.Frames));
        Assert.Equal(0, clock.Stats.DroppedFrames);
    }

    [AvaloniaFact]
    public void EachFrameLandsOnTheRefreshNearestItsMoment()
    {
        var (clock, screen, ticks) = Clock();
        clock.Start(24);
        for (var i = 0; i < 120; i++) screen.Refresh();
        clock.Stop();

        var period = 1000.0 / 24;
        for (var k = 0; k < ticks.Count; k++)
        {
            var due = (k + 1) * period;
            Assert.True(Math.Abs(ticks[k].At - due) <= Refresh / 2 + 0.01,
                $"frame {k + 1}, due at {due:0.0} ms, was shown at {ticks[k].At:0.0}");
        }
    }

    [AvaloniaFact]
    public void AStallStillCatchesUpAndCountsWhatItSkipped()
    {
        var (clock, screen, ticks) = Clock();
        clock.Start(24);
        for (var i = 0; i < 10; i++) screen.Refresh();
        screen.Refresh(150); // a stall of nine refreshes
        for (var i = 0; i < 10; i++) screen.Refresh();
        clock.Stop();

        Assert.Contains(ticks, t => t.Frames > 1);
        Assert.True(clock.Stats.DroppedFrames > 0, "a stall's skipped frames were not counted");
    }

    [AvaloniaFact]
    public void StoppingAsksTheScreenForNothingMore()
    {
        var (clock, screen, ticks) = Clock();
        clock.Start(24);
        screen.Refresh();
        clock.Stop();
        var shown = ticks.Count;

        screen.Refresh();
        screen.Refresh();

        Assert.Equal(shown, ticks.Count);
        Assert.Equal(0, screen.Waiting);
        Assert.False(clock.IsRunning);
    }

    [AvaloniaFact]
    public void ARestartIsOneLoopNotTwo()
    {
        var (clock, screen, ticks) = Clock();
        clock.Start(24);
        screen.Refresh();
        clock.Start(24); // a change of fps mid-play

        for (var i = 0; i < 60; i++) screen.Refresh();
        clock.Stop();

        Assert.True(ticks.Sum(t => t.Frames) <= 25, $"{ticks.Sum(t => t.Frames)} frames in a second: two loops ran");
    }
}
