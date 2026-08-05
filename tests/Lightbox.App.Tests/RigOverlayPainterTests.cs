using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Whether the rig can be reached at all — the overlay draws, the canvas is given
/// the marks, and the mode has a way to be switched on.
/// </summary>
/// <remarks>
/// <para>
/// <b>B58.</b> Everything behind this worked: <c>RigOverlay</c> decided what a press
/// hit and what a drag produced, <c>MainViewModel.Rig.cs</c> landed the edit on the
/// drawing rather than the frame index, and about thirty tests covered hit order,
/// screen-sized targets, drag maths, holds and re-times. <b>Nothing drew it and
/// nothing switched it on.</b> `RigMarks`, `RigEditMode`, `SelectedRigMarkId`,
/// `AddAnchorAt`, `AddShapeAt` and `HasRig` had no consumer outside that one file
/// and its own tests — no binding, no menu item, no <c>ShortcutMap</c> entry — so an
/// artist could not place, select, drag or delete a socket or a hitbox.
/// </para>
/// <para>
/// <b>How it stayed hidden is the part worth copying from.</b> Every evidence
/// anchor named a decision and none named a pixel or a binding, so
/// <c>roadmap.py</c> resolved all sixteen and ticked the box — and
/// <c>RigEditingTests</c> even carried the comment "the canvas is handed an empty
/// list" about an integration that did not exist. That is charter <b>O7</b> one
/// level up: test what a feature <em>does</em>, not that its parts changed.
/// </para>
/// <para>
/// So the tests here deliberately assert the three things nobody had: that pixels
/// appear, that the canvas is handed the list, and that the mode is reachable and
/// rebindable.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class RigOverlayPainterTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 200, H = 160;

    /// <summary>Paint marks onto a transparent bitmap and count what landed.</summary>
    /// <remarks>
    /// Reachable precisely because <c>RigOverlayPainter</c> is not inside
    /// <c>CanvasControl.DrawOp</c>: the draw op only runs inside a Skia lease from
    /// the compositor, which a headless run never grants, so anything written there
    /// cannot be tested at all. That is the mistake this class exists because of.
    /// </remarks>
    private static (int Painted, SKBitmap Image) PaintOf(IReadOnlyList<RigMark>? marks, float scale = 1)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            RigOverlayPainter.Paint(canvas, marks, scale);
            canvas.Flush();
        }
        var painted = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (bmp.GetPixel(x, y).Alpha > 8) painted++;
            }
        }
        return (painted, bmp);
    }

    private static RigMark Anchor(double x, double y, bool selected = false) =>
        new("a1", RigMarkKind.Anchor, "hand", x, y, 0, 0, selected);

    private static RigMark Shape(double x, double y, double w, double h, bool selected = false) =>
        new("s1", RigMarkKind.Shape, "body", x, y, w, h, selected);

    /// <summary>
    /// Marks reach pixels, and no marks reach none.
    /// </summary>
    /// <remarks>
    /// The plainest statement of the bug: for the whole life of B58 this would have
    /// painted nothing, because there was nothing to call.
    /// </remarks>
    [Fact]
    public void TheRigOverlayReachesTheCanvas()
    {
        var (empty, blank) = PaintOf(null);
        blank.Dispose();
        Assert.Equal(0, empty);

        var (none, blank2) = PaintOf([]);
        blank2.Dispose();
        Assert.Equal(0, none);

        var (anchor, a) = PaintOf([Anchor(100, 80)]);
        a.Dispose();
        var (shape, s) = PaintOf([Shape(40, 40, 80, 60)]);
        s.Dispose();
        output.WriteLine($"nothing → {empty} px; one anchor → {anchor} px; one shape → {shape} px");

        Assert.True(anchor > 10, $"an anchor painted {anchor} px — the overlay is drawing nothing");
        Assert.True(shape > 100, $"a shape painted {shape} px — the rectangle is not being drawn");
    }

    /// <summary>
    /// A selected shape grows handles; an unselected one does not.
    /// </summary>
    /// <remarks>
    /// The overlay has to agree with the hit-test, which already refuses corners on
    /// an unselected shape (<c>RigOverlayTests.AnUnselectedShapeHasNoCornersToGrab</c>).
    /// Drawing handles on everything would offer a grab that is not there — and a
    /// rig of a dozen hitboxes would be confetti.
    /// </remarks>
    [Fact]
    public void OnlyTheSelectedShapeShowsHandles()
    {
        var (plain, a) = PaintOf([Shape(40, 40, 80, 60)]);
        var (selected, b) = PaintOf([Shape(40, 40, 80, 60, selected: true)]);
        a.Dispose();
        b.Dispose();
        output.WriteLine($"unselected {plain} px, selected {selected} px");

        Assert.True(
            selected > plain,
            $"the selected shape painted {selected} px against the unselected {plain} — the handles the "
            + "hit-test offers are not drawn, so the grab is invisible");
    }

    /// <summary>
    /// The furniture is screen-sized, so it survives being zoomed.
    /// </summary>
    /// <remarks>
    /// <c>RigOverlay.HandleScreenRadius</c> is in screen pixels for the reason its
    /// own comment gives — a document-space handle is unclickable at 25% and covers
    /// the drawing at 800% — and the overlay has to divide the scale out the same
    /// way, or the thing you can see stops being the thing you can grab. Measured as
    /// an anchor's cross, whose length is the whole mark.
    /// </remarks>
    [Fact]
    public void AnAnchorsCrossIsScreenSizedRatherThanDocumentSized()
    {
        var (atOne, a) = PaintOf([Anchor(100, 80)], scale: 1);
        var (atFour, b) = PaintOf([Anchor(100, 80)], scale: 4);
        a.Dispose();
        b.Dispose();
        output.WriteLine($"scale 1 → {atOne} px, scale 4 → {atFour} px");

        // At 4× the cross must be about a quarter as long in document units, so it
        // is the same size on screen. Loose: this catches "the scale is ignored",
        // not a rounding difference.
        Assert.True(
            atFour < atOne,
            $"the cross painted {atFour} px at 4x against {atOne} px at 1x — the zoom is not divided out, "
            + "so a handle is unclickable when zoomed out and covers the drawing when zoomed in");
    }

    /// <summary>
    /// Anchors go on over shapes, matching the order the hit-test prefers.
    /// </summary>
    [Fact]
    public void AnAnchorInsideAShapeIsStillVisible()
    {
        // The anchor sits inside the shape, which is the ordinary arrangement — a
        // socket on a body.
        var (both, image) = PaintOf([Shape(40, 40, 120, 100), Anchor(100, 90, selected: true)]);
        try
        {
            // The anchor is drawn selected, so it has a ring the shape's outline
            // cannot account for at that position.
            var atCentre = 0;
            for (var y = 84; y <= 96; y++)
            {
                for (var x = 94; x <= 106; x++)
                {
                    if (image.GetPixel(x, y).Alpha > 8) atCentre++;
                }
            }
            output.WriteLine($"{both} px total, {atCentre} px around the anchor inside the shape");
            Assert.True(
                atCentre > 4,
                "the anchor inside the shape painted nothing — it is being drawn under the shape, so the "
                + "one mark the hit-test prefers is the one you cannot see");
        }
        finally
        {
            image.Dispose();
        }
    }

    /// <summary>
    /// The mode can be switched on, which is the half of B58 no painter fixes.
    /// </summary>
    /// <remarks>
    /// A shortcut in the registry rather than a gesture wired into the view: a
    /// gesture works perfectly and is invisible to the shortcut editor, which is how
    /// `Ctrl+Shift+S` came to have no binding at all. The menu item is asserted
    /// against the XAML because `ShortcutRegistrationTests` requires any gesture
    /// declared there to be registered here too — so this pins both ends.
    /// </remarks>
    [Fact]
    public void RigEditModeIsBindable()
    {
        var map = new ShortcutMap();
        var entry = map.Definitions.FirstOrDefault(d => d.Id == "canvas.rigEditMode");
        Assert.NotNull(entry);
        Assert.NotNull(entry!.Default);
        output.WriteLine($"{entry.Id} → {entry.GestureText}, category {entry.Category}");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(
            Path.Combine(dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));

        // The mode, and the three things it exists to let you do. An artist who can
        // switch it on and then cannot add anything is no better off.
        Assert.Contains("IsChecked=\"{Binding RigEditMode, Mode=TwoWay}\"", xaml);
        Assert.Contains("OnAddRigAnchor", xaml);
        Assert.Contains("OnAddRigShape", xaml);
        Assert.Contains("OnDeleteRigMark", xaml);
    }

    /// <summary>
    /// The canvas is actually handed the marks, and given nothing when the mode is
    /// off.
    /// </summary>
    /// <remarks>
    /// <b>The exact gap.</b> <c>RigEditingTests</c> carried the comment "the canvas
    /// is handed an empty list" about an integration that did not exist; this is
    /// that sentence made true. Through the real window, because what was missing
    /// was a line in the window and nothing below it.
    /// </remarks>
    [AvaloniaFact]
    public void TheCanvasIsHandedTheMarks()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();

        Assert.Null(canvas.RigMarks);
        Assert.False(canvas.RigEditMode);

        vm.RigEditMode = true;
        vm.AddAnchorAt("hand", 120, 90);
        Dispatcher.UIThread.RunJobs();

        Assert.True(canvas.RigEditMode, "the canvas was never told the mode is on, so a press still draws");
        Assert.NotNull(canvas.RigMarks);
        var mark = Assert.Single(canvas.RigMarks!);
        Assert.Equal(RigMarkKind.Anchor, mark.Kind);
        output.WriteLine($"canvas holds {canvas.RigMarks!.Count} mark(s): {mark.Label} at ({mark.X}, {mark.Y})");

        // And it goes away with the mode rather than lingering as furniture over the
        // drawing: absent, not merely invisible.
        vm.RigEditMode = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Null(canvas.RigMarks);
        Assert.False(canvas.RigEditMode);
    }

    /// <summary>
    /// A press in rig edit mode selects a mark and lays down no paint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rest of B58 was "nothing is drawn"; this is "nothing happens when you
    /// press". <c>PressRig</c> and <c>DragRig</c> were public, tested and unreachable
    /// — <c>CanvasControl</c>'s nineteen matches for "rig" were all <c>Right</c>,
    /// <c>origin</c> and <c>trigger</c>.
    /// </para>
    /// <para>
    /// <b>Both halves, because either alone passes on a plausible half-fix.</b> A
    /// press that selects but still paints leaves a mark to find and undo — the same
    /// reason reference alignment claims the press before every tool.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void APressInRigModeSelectsAMarkInsteadOfPainting()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();

        vm.RigEditMode = true;
        var id = vm.AddAnchorAt("hand", 200, 150);
        vm.SelectedRigMarkId = null;
        Dispatcher.UIThread.RunJobs();

        var strokesBefore = ((Lightbox.Core.Documents.PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;

        // Press where the anchor is, in view space.
        var at = canvas.DocToView(200, 150);
        var screen = ((Visual)canvas).TranslatePoint(new Point(at.X, at.Y), window)!.Value;
        window.MouseDown(screen, Avalonia.Input.MouseButton.Left);
        window.MouseUp(screen, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        var strokesAfter = ((Lightbox.Core.Documents.PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;
        output.WriteLine($"rig mode: selected {vm.SelectedRigMarkId ?? "nothing"}, strokes {strokesBefore} → {strokesAfter}");

        // Asserted before the control below, because the control draws — and drawing
        // clears the rig selection, which is correct (a mark selected while you paint
        // is stale) and would make a later assertion read as a failure to select.
        Assert.Equal(id, vm.SelectedRigMarkId);
        Assert.Equal(strokesBefore, strokesAfter);

        // The control, and it is not optional: "no new strokes" is also true of a
        // harness where a click never paints at all, which would make the assertion
        // above meaningless. With the mode off, the same click has to leave a mark.
        vm.RigEditMode = false;
        Dispatcher.UIThread.RunJobs();
        window.MouseDown(screen, Avalonia.Input.MouseButton.Left);
        window.MouseUp(screen, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var strokesDrawing = ((Lightbox.Core.Documents.PaintedFrame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;
        output.WriteLine($"mode off: the same click left {strokesDrawing} stroke(s)");

        Assert.True(
            strokesDrawing > strokesAfter,
            "the same click painted nothing with rig mode off either, so this test cannot tell a press "
            + "that was intercepted from a press that never reached the canvas");
    }
}
