using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Lightbox.App.Input;

/// <summary>
/// How fast a list scrolls under a drag held near its top or bottom edge.
/// </summary>
/// <remarks>
/// The arithmetic on its own, for the reason <see cref="CelDragGesture"/> is:
/// the cases worth getting right — a pointer dragged clean past the list, a
/// docker too short to have two bands and a middle — are numbers, and a window
/// is a slow way to ask about numbers.
/// </remarks>
public static class EdgeScroll
{
    /// <summary>How close to an edge the pointer has to be, in pixels.</summary>
    /// <remarks>About one row: near enough that reaching for the last row showing does not start it.</remarks>
    public const double Band = 28;

    /// <summary>Pixels per tick on entering the band — slow enough to stop on a row.</summary>
    public const double Slowest = 2;

    /// <summary>Pixels per tick at the edge and beyond — about a thousand a second.</summary>
    public const double Fastest = 18;

    /// <summary>
    /// How many ticks a drag has to stay in the band before anything moves —
    /// about a quarter of a second.
    /// </summary>
    /// <remarks>
    /// Found in review. A drag is often only crossing an edge on its way
    /// somewhere else — a swatch going to the canvas passes the bottom of
    /// whatever lies between — and a row picked up near the edge of its own
    /// list is in the band from the first move. Without a pause both scroll a
    /// list the artist was not asking to scroll. The pause is per visit: leave
    /// the band and come back, and it waits again.
    /// </remarks>
    public const int DwellTicks = 15;

    /// <param name="y">The pointer, measured down from the top of what shows.</param>
    /// <param name="height">How much of the list shows.</param>
    /// <returns>Pixels to scroll per tick: negative is up, zero is leave it alone.</returns>
    public static double Speed(double y, double height)
    {
        if (!(height > 0)) return 0;
        // A short docker keeps a middle: a third at each end and no more, or
        // the pointer is always in one band or the other and can never rest.
        var band = Math.Min(Band, height / 3);

        if (y < band) return -Ramp((band - y) / band);
        if (y > height - band) return Ramp((y - (height - band)) / band);
        return 0;
    }

    /// <summary>Faster the deeper into the band; past the edge is as deep as it goes.</summary>
    private static double Ramp(double depth) =>
        Slowest + (Fastest - Slowest) * Math.Clamp(depth, 0, 1);
}

/// <summary>
/// Scrolls a list under a drag that is held near its top or bottom.
/// </summary>
/// <remarks>
/// <para>
/// <b>On a clock, not on pointer moves.</b> An artist who has dragged to the
/// bottom of a list and stopped is waiting for the list to come to them; a
/// scroll that only happens when the pointer moves makes them wiggle it. The
/// drag says where the pointer is (<see cref="Track"/> or
/// <see cref="TrackUnder"/>) and when it is over (<see cref="Stop"/>); between
/// those the timer does the scrolling.
/// </para>
/// <para>
/// <b>It finds the scroller that actually scrolls.</b> A docker's content sits
/// in the docker's own scroller, and a list inside it often has one of its own
/// that is handed all the height it asks for and therefore never moves. The
/// layer docker's first auto-scroll nudged that inner one, and did nothing.
/// </para>
/// <para>
/// <see cref="Tick"/> is public to the assembly so a test can turn the clock by
/// hand: a headless test that sleeps to let a timer fire fails on a busy
/// machine.
/// </para>
/// </remarks>
public sealed class DragEdgeScroller
{
    private readonly DispatcherTimer _timer;
    private double _speed;
    private int _waited;

    public DragEdgeScroller()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick());
        _timer.Stop();
    }

    /// <summary>
    /// For tests: never start the timer, so that <see cref="Tick"/> called by
    /// hand is the only clock there is.
    /// </summary>
    internal bool Manual { get; set; }

    internal bool ClockIsRunning => _timer.IsEnabled;

    /// <summary>The list being scrolled, or null when nothing is.</summary>
    public ScrollViewer? Target { get; private set; }

    public bool IsScrolling => Target is not null && _speed != 0;

    /// <summary>
    /// Raised after each scroll, so a drag can re-aim: the pointer has not
    /// moved, and what is under it has.
    /// </summary>
    public event Action? Scrolled;

    /// <summary>The nearest list above <paramref name="from"/> with more content than room, or null.</summary>
    public static ScrollViewer? ScrollingAbove(Visual? from) =>
        from?.GetSelfAndVisualAncestors().OfType<ScrollViewer>().FirstOrDefault(
            s => s.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                && s.Extent.Height > s.Viewport.Height + 1);

    /// <summary>The drag is at <paramref name="inScroller"/>, in that list's own coordinates.</summary>
    public void Track(ScrollViewer scroller, Point inScroller)
    {
        // The room that shows, not the control's own box: a horizontal bar
        // along the bottom is not part of what a row can be dropped on.
        var height = scroller.Viewport.Height > 0 ? scroller.Viewport.Height : scroller.Bounds.Height;
        _speed = EdgeScroll.Speed(inScroller.Y, height);
        if (_speed == 0)
        {
            Stop();
            return;
        }
        // A different list is a new visit, and waits from the start.
        if (!ReferenceEquals(Target, scroller)) _waited = 0;
        Target = scroller;
        if (!Manual && !_timer.IsEnabled) _timer.Start();
    }

    /// <summary>
    /// The drag is at <paramref name="inRoot"/>; whichever list is under it is
    /// the one to scroll. For a drag the operating system runs, which reports
    /// where it is and nothing about what it is over.
    /// </summary>
    public void TrackUnder(TopLevel root, Point inRoot)
    {
        if (ScrollingAbove(root.InputHitTest(inRoot) as Visual) is { } scroller
            && root.TranslatePoint(inRoot, scroller) is { } inScroller)
        {
            Track(scroller, inScroller);
        }
        else
        {
            Stop();
        }
    }

    /// <summary>The drag ended, or left.</summary>
    public void Stop()
    {
        _speed = 0;
        _waited = 0;
        Target = null;
        _timer.Stop();
    }

    /// <summary>One step of the clock.</summary>
    /// <returns>
    /// Whether there is anything still to do: true while waiting out the dwell
    /// and while the list moves, false at the end of the list and when nothing
    /// is being scrolled.
    /// </returns>
    internal bool Tick()
    {
        if (Target is not { } scroller || _speed == 0) return false;
        if (_waited < EdgeScroll.DwellTicks)
        {
            _waited++;
            return true;
        }

        var offset = scroller.Offset.Y;
        var furthest = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        var next = Math.Clamp(offset + _speed, 0, furthest);
        if (Math.Abs(next - offset) < 0.01) return false;

        scroller.Offset = scroller.Offset.WithY(next);
        Scrolled?.Invoke();
        return true;
    }
}
