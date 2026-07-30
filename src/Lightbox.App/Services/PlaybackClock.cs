using Avalonia.Threading;

namespace Lightbox.App.Services;

/// <summary>Timeline playback ticker: fires once per frame at the scene fps.</summary>
public sealed class PlaybackClock
{
    private readonly DispatcherTimer _timer = new();

    public event Action? Tick;

    public bool IsRunning => _timer.IsEnabled;

    public PlaybackClock()
    {
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public void Start(int fps)
    {
        _timer.Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, fps));
        _timer.Start();
    }

    public void Stop() => _timer.Stop();
}
