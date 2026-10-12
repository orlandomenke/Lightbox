using Lightbox.App.Services;

namespace Lightbox.App.Views;

/// <summary>Part of MainWindow: the screen's refresh, handed to playback's clock.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Pace playback by this window's refresh rather than a timer
    /// (<see cref="PlaybackClock.Screen"/>): the frame that is due is published
    /// inside the refresh that shows it. Taken back when the window closes, so a
    /// clock never asks a window that is gone.
    /// </summary>
    private void HandPlaybackTheScreen()
    {
        _vm.PaceBy(callback => RequestAnimationFrame(callback));
        Closed += (_, _) => _vm.PaceBy(null);
    }
}
