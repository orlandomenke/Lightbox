using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Lightbox.App.Tests;
using Lightbox.App.Views;
using Xunit.v3;

[assembly: ClosesItsWindows]

namespace Lightbox.App.Tests;

/// <summary>
/// Every window a test shows, closed when the test ends.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists: the release build hung.</b> The suite shows a few
/// thousand windows and closed almost none of them. A shown window stays alive
/// in the headless platform until it is closed, so each one went on being laid
/// out and rendered for the rest of the run, and every later window test was
/// slower than the one before. Measured 2026-10-16 on 45 window-heavy test
/// classes in one process: 16.2 GB at the end and still climbing with the
/// windows left open, 2.0 GB with them closed, and 8 m 21 s against 4 m 51 s.
/// Split six ways (<c>build.yml</c>) no process held enough to notice; run
/// whole (<c>release.yml</c>) the App suite starved the CI runner until it lost
/// contact (B482). B281 had found the same leak's static-subscription half and
/// made it weak; a shown window needs closing, not a weaker reference.
/// </para>
/// <para>
/// <b>Recorded as they open rather than listed at the end</b>, because the
/// headless platform has no list of open windows to ask. The record is a class
/// handler on <see cref="Window.WindowOpenedEvent"/>, so it sees every window
/// whoever made it — a test's own, or a dialog the app opened.
/// </para>
/// <para>
/// <b>Closed in one place rather than in each test</b>, so a test written
/// tomorrow cannot leak by forgetting: 79 files open a main window, and
/// almost none of them closed it.
/// </para>
/// </remarks>
internal static class TestWindows
{
    private static readonly List<Window> Shown = [];
    private static int _tracking;

    /// <summary>Start recording windows as they open. Idempotent.</summary>
    internal static void Track()
    {
        if (Interlocked.Exchange(ref _tracking, 1) == 1) return;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            lock (Shown) Shown.Add(window);
        });
    }

    /// <summary>How many windows are recorded open right now.</summary>
    internal static int OpenCount
    {
        get { lock (Shown) return Shown.Count(w => w.IsVisible); }
    }

    /// <summary>
    /// Close every window still open, newest first, so a dialog goes before
    /// the window that owns it. Returns how many were closed.
    /// </summary>
    internal static int CloseAll()
    {
        Window[] open;
        lock (Shown)
        {
            open = [.. Shown];
            Shown.Clear();
        }
        var closed = 0;
        foreach (var window in open.Reverse())
        {
            if (!window.IsVisible) continue;
            if (window is MainWindow main) main.CloseWithoutAsking();
            else window.Close();
            closed++;
        }
        return closed;
    }
}

/// <summary>
/// After each test, close what it opened — on the UI thread, which is where
/// an <c>AvaloniaFact</c> runs and the only thread that may touch a window.
/// </summary>
/// <remarks>
/// A plain <c>Fact</c> runs elsewhere and cannot show a window, so off the UI
/// thread there is nothing to do, and marshalling there could wait on a
/// dispatcher nobody is pumping between tests.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ClosesItsWindowsAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (Avalonia.Application.Current is null || !Dispatcher.UIThread.CheckAccess()) return;
        TestWindows.CloseAll();
        Dispatcher.UIThread.RunJobs();
    }
}
