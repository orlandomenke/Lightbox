using System.Diagnostics;

namespace Lightbox.App.Rendering;

/// <summary>
/// How long a change to the path overlay waits to be drawn, for the perf lab.
/// </summary>
/// <remarks>
/// <para>
/// The pen's rubber band, its nodes and the white arrow's retrace are chrome:
/// they never go through a publish, so <c>pen.screen</c> — which clocks ink —
/// cannot see them. They reach the screen by <c>InvalidateVisual</c> and a
/// whole canvas redraw, and whether that keeps up with the pointer is the one
/// thing an artist judges a pen by.
/// </para>
/// <para>
/// <b>The stamp travels with the draw operation</b> rather than being read back
/// when a frame lands: the frame that lands next may have been built before the
/// overlay changed, and crediting it would under-report by a refresh. The
/// oldest unshown change is what is clocked, so a burst of pointer events
/// coalesced into one redraw reports the wait of the first, which is the one
/// the eye was left behind on.
/// </para>
/// <para>
/// Measured from the overlay being handed over, not from the pointer event:
/// the handler's own cost is the lab's <c>pen.hover</c> / <c>pen.drag</c> span,
/// and the time the event spent queued behind other input is in neither.
/// Free when the log is off.
/// </para>
/// </remarks>
public partial class CanvasControl
{
    private long _overlayChangedAt;
    private long _overlayLogged;

    private void NoteOverlayChanged()
    {
        if (!Services.PerfLog.On || _overlayChangedAt != 0) return;
        _overlayChangedAt = Stopwatch.GetTimestamp();
    }

    /// <summary>The frame-time callback for the draw being built, carrying the stamp if one is waiting.</summary>
    private Action<double> FrameTimeReporter()
    {
        var stamp = _overlayChangedAt;
        if (stamp == 0) return ReportFrameTime;
        _overlayChangedAt = 0;
        return milliseconds =>
        {
            ReportFrameTime(milliseconds);
            // A draw operation can be rendered more than once; only its first
            // frame is the one the change was waiting for.
            if (Interlocked.Exchange(ref _overlayLogged, stamp) == stamp) return;
            Services.PerfLog.Took("overlay.draw", milliseconds);
            Services.PerfLog.Took("overlay.shown", Stopwatch.GetElapsedTime(stamp).TotalMilliseconds);
        };
    }
}
