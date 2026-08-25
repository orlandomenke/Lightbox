using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Shift during a brush drag holds the stroke to whichever axis it has gone
/// furthest along, measured from where the pen came down.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sibling of <see cref="DragAxisLockTests"/>, which had none.</b> That
/// class covers the lock on <em>object</em> drags — guides, reference boxes,
/// anchors, shapes. The lock on a painted stroke is a different assignment in a
/// different handler and was untested, which is worth fixing on its own terms:
/// it is the one of the two that can alter the record, and the record is the
/// document.
/// </para>
/// <para>
/// <b>It is also B256 made executable, and that is the reason it exists now.</b>
/// B256 is <i>"after the pen returns from proximity a stroke draws only a
/// horizontal line"</i>, and its entry says the mechanism is certain while the
/// trigger is not — certain because <c>AxisLocked</c> returns
/// <c>(x, anchor.Y)</c> when horizontal travel dominates, which is exactly a
/// perfectly horizontal line pinned to where the stroke began. That was read off
/// the code rather than demonstrated. These tests demonstrate it: a move event
/// carrying Shift is sufficient to produce the reported shape, from nothing else.
/// </para>
/// <para>
/// <b>Deliberately NOT evidence anchors on B256</b>, following B126's split.
/// They prove the mechanism, not the cure — whether the pen is what reports
/// Shift is a fact about Windows Ink on a machine with a tablet, and this
/// repository has neither. Naming them on the entry would tick a box on a bug
/// nobody has watched stop. B256 stays <c>evidence: manual</c> until a trace
/// reads <c>events claiming Shift</c> on the reporter's machine.
/// </para>
/// <para>
/// <b>Driven with a mouse rather than a pen, on purpose.</b> The fabrication B256
/// suspects is the pen's; the mechanism under test is not device-specific — it is
/// <c>e.KeyModifiers</c> on a move event, whatever produced it. A mouse keeps the
/// test about the one thing, and sidesteps synthesising pen pressure, which would
/// decide whether a mark lands at all and has nothing to do with the axis lock.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class StrokeAxisLockTests : BrushStateIsolated
{
    private static readonly Pointer Mouse = new(1, PointerType.Mouse, true);

    /// <summary>A window with a document open and the brush ready to paint.</summary>
    private static (MainWindow Window, CanvasControl Canvas, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1200, Height = 900 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        vm.OpenDocumentTab(
            DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor), null);
        // Stabilization relocates points by design, and this test is about where
        // the points went. Off, so a failure means the lock and not the smoother.
        vm.SmoothStrokes = false;
        vm.ActiveTool = ToolId.Brush;
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, canvas, vm);
    }

    private static Point Root(Window window, Visual target, Point local) =>
        target.TranslatePoint(local, window) ?? local;

    private static PointerPressedEventArgs Press(Window w, Control t, Point at, KeyModifiers held) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            held);

    private static PointerEventArgs Move(Window w, Control t, Point at, KeyModifiers held) =>
        new(InputElement.PointerMovedEvent, t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            held);

    private static PointerReleasedEventArgs Release(Window w, Control t, Point at) =>
        new(t, Mouse, w, Root(w, t, at), 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left);

    /// <summary>
    /// Paint from one document point to another, holding <paramref name="held"/>
    /// on the move events only — the press is always clean.
    /// </summary>
    /// <remarks>
    /// The press carries no modifiers because that is the case B256 describes:
    /// the artist touched down without Shift. The lock is assigned per move
    /// event, so a clean press does not protect the stroke.
    /// </remarks>
    private static void Paint(
        Window window, CanvasControl canvas,
        (double X, double Y) from, (double X, double Y) to, KeyModifiers held)
    {
        var (sx, sy) = canvas.DocToView(from.X, from.Y);
        var (ex, ey) = canvas.DocToView(to.X, to.Y);
        var start = new Point(sx, sy);
        var end = new Point(ex, ey);
        canvas.RaiseEvent(Press(window, canvas, start, KeyModifiers.None));
        canvas.RaiseEvent(Move(window, canvas, (start + end) / 2, held));
        canvas.RaiseEvent(Move(window, canvas, end, held));
        canvas.RaiseEvent(Release(window, canvas, end));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static List<StrokePoint> LastStrokePoints(MainViewModel vm)
    {
        var strokes = vm.PaintStrokes();
        Assert.NotEmpty(strokes);
        return strokes[^1].Points;
    }

    [AvaloniaFact]
    public void WithoutShiftAStrokeKeepsBothAxes()
    {
        // The sanity half. Without it, a test asserting "Shift flattens the
        // stroke" would pass just as happily on a build where no mark lands at
        // all, or where every stroke is flat for some other reason.
        var (window, canvas, vm) = Open();

        Paint(window, canvas, (200, 200), (320, 260), KeyModifiers.None);

        var pts = LastStrokePoints(vm);
        var spreadX = pts.Max(p => p.X) - pts.Min(p => p.X);
        var spreadY = pts.Max(p => p.Y) - pts.Min(p => p.Y);
        Assert.True(spreadX > 50, $"expected real x travel, got {spreadX:F1}");
        Assert.True(spreadY > 25, $"expected real y travel, got {spreadY:F1}");

        window.Close();
    }

    [AvaloniaFact]
    public void ShiftOnTheMovesPinsTheStrokeToTheYItCameDownAt()
    {
        // B256's reported shape, produced from nothing but a modifier on the
        // move events: "it only draws straight horizontal lines as long as my
        // pen is touching".
        var (window, canvas, vm) = Open();

        // The same travel as above — mostly rightward, so AxisLocked keeps x
        // and replaces y with the anchor's.
        Paint(window, canvas, (200, 200), (320, 260), KeyModifiers.Shift);

        var pts = LastStrokePoints(vm);
        var spreadX = pts.Max(p => p.X) - pts.Min(p => p.X);
        var spreadY = pts.Max(p => p.Y) - pts.Min(p => p.Y);

        Assert.True(spreadX > 50, $"expected the x travel to survive, got {spreadX:F1}");
        Assert.True(spreadY < 1, $"expected a flat stroke, got {spreadY:F1} of y travel");
        // Flat *at the anchor*, not merely flat: the mark sits where the stroke
        // began, which is what makes it look like a line the hand never drew.
        Assert.All(pts, p => Assert.True(
            Math.Abs(p.Y - pts[0].Y) < 1, $"expected y {pts[0].Y:F1}, got {p.Y:F1}"));

        window.Close();
    }

    [AvaloniaFact]
    public void AVerticalDragUnderShiftPinsTheXInstead()
    {
        // The other branch of AxisLocked, so the flattening above is shown to be
        // "whichever axis dominates" rather than "always horizontal" — a test
        // that only ever saw the horizontal case would not notice the lock being
        // replaced by something that just zeroes y.
        var (window, canvas, vm) = Open();

        Paint(window, canvas, (200, 200), (240, 340), KeyModifiers.Shift);

        var pts = LastStrokePoints(vm);
        var spreadX = pts.Max(p => p.X) - pts.Min(p => p.X);
        var spreadY = pts.Max(p => p.Y) - pts.Min(p => p.Y);
        Assert.True(spreadY > 50, $"expected the y travel to survive, got {spreadY:F1}");
        Assert.True(spreadX < 1, $"expected a plumb stroke, got {spreadX:F1} of x travel");

        window.Close();
    }
}
