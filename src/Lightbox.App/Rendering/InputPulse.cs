using System.Threading;

namespace Lightbox.App.Rendering;

/// <summary>
/// A count of pointer events, split by whether they landed on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists to settle one question that three rounds of fixes could not:
/// why playback is smooth while the pointer moves over the canvas, stuttery
/// when it is still, and — the fact that breaks every theory so far — no better
/// when the pointer moves over a docker instead.</b>
/// </para>
/// <para>
/// The last part is what makes this worth instrumenting rather than reasoning
/// about. "Any input wakes the loop" predicts that a docker would help, and it
/// does not. The only thing the canvas does that a docker cannot is call
/// <c>InvalidateVisual</c> on the canvas itself, from
/// <c>CanvasControl.OnPointerMoved</c> — so the question is whether a frame's
/// wait to be drawn depends on that call arriving, and no number in the report
/// could see it.
/// </para>
/// <para>
/// Two counters rather than one, because "input happened" and "input happened
/// <em>to the canvas</em>" are the two hypotheses and they differ only here.
/// Bumped from the pointer handlers and read when a frame is published and
/// again when it is drawn; the difference says what happened in between.
/// </para>
/// <para>
/// Interlocked rather than locked: these are bumped on the UI thread at pointer
/// rate and read on the render thread, and a torn read would cost a
/// misattributed frame rather than anything worse. The alternative is a lock on
/// the pointer path, which is the one path in the application that must not
/// acquire one.
/// </para>
/// </remarks>
internal static class InputPulse
{
    private static long _canvas;
    private static long _elsewhere;

    /// <summary>A pointer event reached the canvas.</summary>
    public static void OnCanvas() => Interlocked.Increment(ref _canvas);

    /// <summary>A pointer event reached the window, but not the canvas.</summary>
    public static void Elsewhere() => Interlocked.Increment(ref _elsewhere);

    /// <summary>The two counts, read together.</summary>
    public static (long Canvas, long Elsewhere) Read() =>
        (Interlocked.Read(ref _canvas), Interlocked.Read(ref _elsewhere));

    public static void Reset()
    {
        Interlocked.Exchange(ref _canvas, 0);
        Interlocked.Exchange(ref _elsewhere, 0);
    }
}
