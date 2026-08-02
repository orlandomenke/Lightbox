using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Controls;

namespace Lightbox.App.Tests;

/// <summary>
/// The loop bounds, dragged on the scrub bar.
/// </summary>
/// <remarks>
/// Toon Boom's gesture. Setting a range used to be two menu commands that each
/// asked you to park the playhead first; these are the same decision made in
/// one motion, where you can see it.
/// </remarks>
public sealed class LoopRangeHandleTests
{
    private const double Inset = 100;
    private const double Cell = 40;

    /// <remarks>
    /// Hosted in a window on purpose. A synthetic <c>PointerEventArgs</c>
    /// carries its position in the visual root's frame, and a control with no
    /// root reports every point as the origin — which makes every press look
    /// like a click on frame zero and every assertion fail for the wrong
    /// reason.
    /// </remarks>
    private static (Window Host, TimelineRuler Ruler) Ruler(int start = -1, int end = -1)
    {
        var ruler = new TimelineRuler
        {
            LeadingInset = Inset,
            CellWidth = Cell,
            Extent = 24,
            MaxFrame = 11,
            RangeStart = start,
            RangeEnd = end,
            Width = 1200,
            Height = 22,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
        var host = new Window { Width = 1400, Height = 200, Content = ruler };
        host.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (host, ruler);
    }

    private static Point Root(Window host, TimelineRuler r, double x) =>
        r.TranslatePoint(new Point(x, 10), host) ?? new Point(x, 10);

    private static readonly Pointer TestPointer = new(1, PointerType.Mouse, true);

    private static void Press(Window host, TimelineRuler r, double x,
        KeyModifiers mods = KeyModifiers.None, bool right = false) =>
        r.RaiseEvent(new PointerPressedEventArgs(
            r, TestPointer, host, Root(host, r, x), 0,
            new PointerPointProperties(
                right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton,
                right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed),
            mods));

    private static void Move(Window host, TimelineRuler r, double x) =>
        r.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent, r, TestPointer, host, Root(host, r, x), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));

    private static void Release(Window host, TimelineRuler r, double x) =>
        r.RaiseEvent(new PointerReleasedEventArgs(
            r, TestPointer, host, Root(host, r, x), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));

    /// <summary>The left edge of a frame's cell.</summary>
    private static double LeftOf(int frame) => Inset + frame * Cell;

    /// <summary>The right edge — where the end handle lives.</summary>
    private static double RightOf(int frame) => Inset + (frame + 1) * Cell;

    // ---- dragging the bounds --------------------------------------------------------

    [AvaloniaFact]
    public void DraggingTheStartHandleSetsTheStartFrame()
    {
        var (host, r) = Ruler(start: 2, end: 8);

        Press(host, r, LeftOf(2));
        Move(host, r, LeftOf(5) + 4);
        Release(host, r, LeftOf(5) + 4);

        Assert.Equal(5, r.RangeStart);
        Assert.Equal(8, r.RangeEnd);
    }

    [AvaloniaFact]
    public void DraggingTheEndHandleSetsTheEndFrame()
    {
        var (host, r) = Ruler(start: 2, end: 8);

        Press(host, r, RightOf(8));
        Move(host, r, RightOf(5));
        Release(host, r, RightOf(5));

        Assert.Equal(2, r.RangeStart);
        Assert.Equal(5, r.RangeEnd);
    }

    [AvaloniaFact]
    public void TheBoundsCannotCross()
    {
        // An inverted range is something nothing downstream can read, so the
        // pushed handle pins rather than passes.
        var (host, r) = Ruler(start: 2, end: 8);

        Press(host, r, LeftOf(2));
        Move(host, r, LeftOf(11) + 4);
        Release(host, r, LeftOf(11) + 4);

        Assert.Equal(8, r.RangeStart);
        Assert.Equal(8, r.RangeEnd);
    }

    [AvaloniaFact]
    public void DraggingAHandleDoesNotScrub()
    {
        // Grabbing the loop bound and moving the playhead are the same gesture
        // in nearly the same place; the handle has to win where it is.
        var (host, r) = Ruler(start: 2, end: 8);
        r.CurrentFrame = 0;

        Press(host, r, LeftOf(2));
        Move(host, r, LeftOf(5) + 4);
        Release(host, r, LeftOf(5) + 4);

        Assert.Equal(0, r.CurrentFrame);
    }

    [AvaloniaFact]
    public void ClickingAwayFromTheHandlesStillScrubs()
    {
        var (host, r) = Ruler(start: 2, end: 8);

        Press(host, r, LeftOf(6) + Cell / 2);

        Assert.Equal(6, r.CurrentFrame);
    }

    // ---- setting one from nothing ----------------------------------------------------

    [AvaloniaFact]
    public void WithNoRangeYetTheHandlesSitOnTheWholeTimeline()
    {
        // So the first drag narrows the timeline rather than starting from a
        // range of one frame somewhere arbitrary.
        var (host, r) = Ruler();

        Press(host, r, LeftOf(0));
        Move(host, r, LeftOf(3) + 4);
        Release(host, r, LeftOf(3) + 4);

        Assert.Equal(3, r.RangeStart);
        Assert.Equal(11, r.RangeEnd);   // the far end filled itself in
    }

    // ---- giving it back ---------------------------------------------------------------

    [AvaloniaFact]
    public void AltClickingAHandleResetsTheRange()
    {
        var (host, r) = Ruler(start: 2, end: 8);

        Press(host, r, LeftOf(2), KeyModifiers.Alt);

        Assert.Equal(-1, r.RangeStart);
        Assert.Equal(-1, r.RangeEnd);
    }

    [AvaloniaFact]
    public void AltClickingBetweenThemResetsItToo()
    {
        // The region the range is actually about, which is a bigger and more
        // findable target than either grip.
        var (host, r) = Ruler(start: 2, end: 8);

        Press(host, r, LeftOf(5), KeyModifiers.Alt);

        Assert.Equal(-1, r.RangeStart);
    }

    [AvaloniaFact]
    public void AltClickingOutsideTheRangeLeavesItAlone()
    {
        var (host, r) = Ruler(start: 2, end: 8);

        Press(host, r, LeftOf(10), KeyModifiers.Alt);

        Assert.Equal(2, r.RangeStart);
        Assert.Equal(8, r.RangeEnd);
    }

    [AvaloniaFact]
    public void AltClickingDoesNotScrubEither()
    {
        var (host, r) = Ruler(start: 2, end: 8);
        r.CurrentFrame = 0;

        Press(host, r, LeftOf(5), KeyModifiers.Alt);

        Assert.Equal(0, r.CurrentFrame);
    }

    [AvaloniaFact]
    public void RightClickingAsksForTheMenuRatherThanScrubbing()
    {
        var (host, r) = Ruler(start: 2, end: 8);
        r.CurrentFrame = 0;
        var asked = 0;
        r.RangeMenuRequested += (_, _) => asked++;

        Press(host, r, LeftOf(6), right: true);

        Assert.Equal(1, asked);
        Assert.Equal(0, r.CurrentFrame);
    }

    [AvaloniaFact]
    public void ResetRangeClearsBothBounds()
    {
        var (host, r) = Ruler(start: 2, end: 8);

        r.ResetRange();

        Assert.Equal(-1, r.RangeStart);
        Assert.Equal(-1, r.RangeEnd);
    }
}
