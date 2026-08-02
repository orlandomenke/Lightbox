using Avalonia.Threading;

namespace Lightbox.App.Services;

/// <summary>
/// Timeline playback ticker: fires once per frame at the scene fps scaled by
/// a playback-speed percentage (100 = real time).
/// </summary>
public sealed class PlaybackClock
{
    private readonly DispatcherTimer _timer = new();

    public event Action? Tick;

    public bool IsRunning => _timer.IsEnabled;

    /// <summary>The interval the timer is (or would be) running at.</summary>
    public TimeSpan Interval => _timer.Interval;

    public PlaybackClock()
    {
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public void Start(int fps, int speedPercent = 100)
    {
        _timer.Interval = IntervalFor(fps, speedPercent);
        _timer.Start();
    }

    public static TimeSpan IntervalFor(int fps, int speedPercent) =>
        TimeSpan.FromSeconds(1.0 / (Math.Max(1, fps) * Math.Max(1, speedPercent) / 100.0));

    public void Stop() => _timer.Stop();
}
