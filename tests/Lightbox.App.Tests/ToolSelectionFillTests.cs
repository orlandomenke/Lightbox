using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

public class ToolModelTests
{
    [AvaloniaFact]
    public void SelectShortcut_ActivatesThenCyclesVariants()
    {
        var vm = new MainViewModel(null);
        Assert.Equal(ToolId.Brush, vm.ActiveTool);

        vm.SelectToolCommand.Execute(ToolId.Select);
        Assert.Equal(ToolId.Select, vm.ActiveTool);
        Assert.Equal(SelectVariant.Freehand, vm.ActiveSelectVariant);

        // pressing S again cycles: freehand → polygon → box → ellipse → wand → freehand
        vm.SelectToolCommand.Execute(ToolId.Select);
        Assert.Equal(SelectVariant.Polygon, vm.ActiveSelectVariant);
        vm.SelectToolCommand.Execute(ToolId.Select);
        Assert.Equal(SelectVariant.Box, vm.ActiveSelectVariant);
        vm.SelectToolCommand.Execute(ToolId.Select);
        Assert.Equal(SelectVariant.Ellipse, vm.ActiveSelectVariant);
        vm.SelectToolCommand.Execute(ToolId.Select);
        Assert.Equal(SelectVariant.Wand, vm.ActiveSelectVariant);
        vm.SelectToolCommand.Execute(ToolId.Select);
        Assert.Equal(SelectVariant.Freehand, vm.ActiveSelectVariant);

        // picking a variant directly also activates the tool
        vm.ActiveTool = ToolId.Brush;
        vm.SelectVariantOfCommand.Execute(SelectVariant.Box);
        Assert.Equal(ToolId.Select, vm.ActiveTool);
        Assert.Equal(SelectVariant.Box, vm.ActiveSelectVariant);
    }

    [AvaloniaFact]
    public void IsEraserCompat_TracksTheActiveTool()
    {
        var vm = new MainViewModel(null);
        vm.IsEraser = true;
        Assert.Equal(ToolId.Eraser, vm.ActiveTool);
        vm.IsEraser = false;
        Assert.Equal(ToolId.Brush, vm.ActiveTool);

        vm.ActiveTool = ToolId.Fill;
        Assert.False(vm.IsEraser);
        Assert.True(vm.IsFillTool);
        Assert.False(vm.IsPaintTool);
    }

    [AvaloniaFact]
    public void NonPaintTools_DoNotProduceBrushStrokes()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, ActiveTool = ToolId.Select };
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(50, 50, 1);
        vm.EndStroke();
        var frame = vm.PaintedCel();
        Assert.Empty(frame.Strokes);
    }
}

public class FillToolTests
{
    private static Frame FrameOf(MainViewModel vm) =>
        vm.PaintedCel();

    private static void DrawLine(MainViewModel vm)
    {
        vm.ActiveTool = ToolId.Brush;
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 100, 1);
        vm.EndStroke();
    }

    [AvaloniaFact]
    public void FillAt_RecordsAFillStroke_BelowTheLineWork()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        DrawLine(vm);

        vm.ActiveTool = ToolId.Fill;
        vm.FillBelowLines = true;
        vm.FillAt(50, 300); // empty area away from the line

        var strokes = FrameOf(vm).Strokes;
        Assert.Equal(2, strokes.Count);
        Assert.Equal(ToolKind.Fill, strokes[0].Tool);      // inserted at the bottom
        Assert.Equal(ToolKind.Brush, strokes[1].Tool);     // line work stays on top
        Assert.True(strokes[0].Points.Count >= 3);
    }

    [AvaloniaFact]
    public void FillAt_AboveLineWork_AppendsOnTop()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        DrawLine(vm);

        vm.ActiveTool = ToolId.Fill;
        vm.FillBelowLines = false;
        vm.FillAt(50, 300);

        var strokes = FrameOf(vm).Strokes;
        Assert.Equal(ToolKind.Brush, strokes[0].Tool);
        Assert.Equal(ToolKind.Fill, strokes[1].Tool);      // appended above
    }

    [AvaloniaFact]
    public void FillAt_UnderASelection_StaysInsideIt_AndRecordsTheClip()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, ActiveTool = ToolId.Fill };
        vm.ApplySelectionShape(
            [new(100, 100, 1), new(220, 100, 1), new(220, 220, 1), new(100, 220, 1)],
            add: false, subtract: false);
        Assert.True(vm.HasSelection);

        vm.FillAt(150, 150);

        var stroke = Assert.Single(FrameOf(vm).Strokes);
        Assert.Equal(ToolKind.Fill, stroke.Tool);
        Assert.All(stroke.Points, p =>
        {
            Assert.InRange(p.X, 98, 222);
            Assert.InRange(p.Y, 98, 222);
        });
        Assert.NotNull(stroke.ClipId);
        Assert.True(vm.Doc.ClipRegions.ContainsKey(stroke.ClipId!));
    }

    [AvaloniaFact]
    public void FillAt_IsUndoable()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, ActiveTool = ToolId.Fill };
        vm.FillAt(50, 50);
        Assert.Single(FrameOf(vm).Strokes);

        vm.UndoCommand.Execute(null);
        Assert.Empty(FrameOf(vm).Strokes);
    }
}

public class SelectionTests
{
    private static List<StrokePoint> Box(double l, double t, double r, double b) =>
        [new(l, t, 1), new(r, t, 1), new(r, b, 1), new(l, b, 1)];

    [AvaloniaFact]
    public void PaintingUnderASelection_TagsTheStroke_AndDedupesTheRegion()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ApplySelectionShape(Box(50, 50, 200, 200), add: false, subtract: false);

        vm.BeginStroke(60, 60, 1);
        vm.MoveStroke(120, 120, 1);
        vm.EndStroke();
        vm.BeginStroke(80, 150, 1);
        vm.MoveStroke(150, 150, 1);
        vm.EndStroke();

        var strokes = (vm.PaintedCel()).Strokes;
        Assert.Equal(2, strokes.Count);
        Assert.NotNull(strokes[0].ClipId);
        Assert.Equal(strokes[0].ClipId, strokes[1].ClipId); // same selection → same region
        Assert.Single(vm.Doc.ClipRegions);                  // stored once (content-hash dedup)
    }

    [AvaloniaFact]
    public void SelectionCombineOps_AddSubtractReplace()
    {
        var vm = new MainViewModel(null);
        vm.ApplySelectionShape(Box(0, 0, 100, 100), add: false, subtract: false);
        Assert.Single(vm.SelectionContours);

        // add a disjoint box → two outlines
        vm.ApplySelectionShape(Box(200, 200, 300, 300), add: true, subtract: false);
        Assert.Equal(2, vm.SelectionContours.Count);

        // subtract one of them again
        vm.ApplySelectionShape(Box(195, 195, 305, 305), add: false, subtract: true);
        Assert.Single(vm.SelectionContours);

        // plain shape replaces everything
        vm.ApplySelectionShape(Box(400, 300, 500, 400), add: false, subtract: false);
        Assert.Single(vm.SelectionContours);
        Assert.All(vm.SelectionContours[0], p => Assert.InRange(p.X, 398, 502));
    }

    [AvaloniaFact]
    public void SelectAll_Invert_Deselect()
    {
        var vm = new MainViewModel(null);
        vm.SelectAllCommand.Execute(null);
        Assert.True(vm.HasSelection);

        // inverting a full-canvas selection leaves nothing selected
        vm.InvertSelectionCommand.Execute(null);
        Assert.False(vm.HasSelection);

        // inverting an empty selection selects the whole canvas
        vm.InvertSelectionCommand.Execute(null);
        Assert.True(vm.HasSelection);

        vm.DeselectCommand.Execute(null);
        Assert.False(vm.HasSelection);
    }

    [AvaloniaFact]
    public void GrowAndShrink_MoveTheOutline()
    {
        var vm = new MainViewModel(null) { SelectionAdjustPx = 4 };
        vm.ApplySelectionShape(Box(100, 100, 200, 200), add: false, subtract: false);

        double MinX() => vm.SelectionContours[0].Min(p => p.X);
        var baseline = MinX();

        vm.GrowSelectionCommand.Execute(null);
        Assert.True(MinX() < baseline - 2, "grow must push the outline outward");

        vm.ShrinkSelectionCommand.Execute(null);
        vm.ShrinkSelectionCommand.Execute(null);
        Assert.True(MinX() > baseline + 2, "shrink must pull the outline inward");
    }

    [AvaloniaFact]
    public void PolygonSelection_BuildsFromVertices_AndEscCancels()
    {
        var vm = new MainViewModel(null);
        vm.AddPolygonVertex(10, 10);
        vm.AddPolygonVertex(90, 10);
        Assert.Equal(2, vm.PolygonInProgress.Count);

        vm.CancelPolygon();
        Assert.Empty(vm.PolygonInProgress);
        Assert.False(vm.HasSelection);

        vm.AddPolygonVertex(10, 10);
        vm.AddPolygonVertex(90, 10);
        vm.AddPolygonVertex(50, 90);
        vm.CompletePolygon(add: false, subtract: false);
        Assert.Empty(vm.PolygonInProgress);
        Assert.True(vm.HasSelection);
    }

    /// <summary>
    /// Picking up another tool abandons the half-drawn lasso, the same way it
    /// finishes a pen path and drops a line selection — a shape you can no
    /// longer close is drawn state pointing at a capability you no longer have.
    /// </summary>
    /// <remarks>
    /// Pinned because the line that does it sits beside the guard that makes a
    /// <em>borrowed</em> tool skip all of this (see
    /// <c>MainViewModel.Momentary.cs</c>), and which side of that guard it falls
    /// on is not visible from here.
    /// </remarks>
    [AvaloniaFact]
    public void PolygonSelection_IsAbandonedWhenTheToolChanges()
    {
        var vm = new MainViewModel(null) { ActiveTool = ToolId.Select };
        vm.AddPolygonVertex(10, 10);
        vm.AddPolygonVertex(90, 10);
        Assert.Equal(2, vm.PolygonInProgress.Count);

        vm.ActiveTool = ToolId.Brush;

        Assert.Empty(vm.PolygonInProgress);
        Assert.False(vm.HasSelection);
    }

    [AvaloniaFact]
    public void PaintOutsideTheSelection_LeavesNoVisiblePixels()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ApplySelectionShape(Box(400, 400, 500, 500), add: false, subtract: false);

        // stroke fully outside the selected box
        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(150, 50, 1);
        vm.EndStroke();

        var strokes = (vm.PaintedCel()).Strokes;
        using var bmp = Lightbox.Raster.FrameRasterizer.Rasterize(
            strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        Assert.Equal(0, bmp.GetPixel(100, 50).Alpha); // clipped away entirely
    }
}

/// <summary>
/// What Select All, Deselect, Delete and Backspace mean — and which document
/// they mean it about.
/// </summary>
/// <remarks>
/// B168, B171 and B173, together because they are the same surface and because
/// two of them only make sense beside each other: B173's Delete asks whether a
/// marquee is live, and B171 is why the answer can be trusted.
/// </remarks>
[Collection("BrushState")]
public class SelectionScopeTests : BrushStateIsolated
{
    private static List<StrokePoint> Box(double l, double t, double r, double b) =>
        [new(l, t, 1), new(r, t, 1), new(r, b, 1), new(l, b, 1)];

    private static MainViewModel Marqueed(ToolId tool)
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ApplySelectionShape(Box(50, 50, 200, 200), add: false, subtract: false);
        vm.ActiveTool = tool;
        Assert.True(vm.HasSelection);
        return vm;
    }

    // ---- B168: the commands asked which tool was held, not what was selected --

    [AvaloniaFact]
    public void DeselectClearsAMarqueeWhileTheSelectToolIsActive()
    {
        // The tool that *makes* a marquee was the one tool whose deselect
        // could not clear it — the old branch read ToolId.Select and meant
        // the arrows.
        var vm = Marqueed(ToolId.Select);
        vm.DeselectCommand.Execute(null);
        Assert.False(vm.HasSelection);
    }

    [AvaloniaFact]
    public void DeselectClearsAMarqueeWhicheverToolIsHeld()
    {
        // "None" means none, from anywhere: an artist must never be left
        // holding a selection they cannot see how to remove, because a
        // selection clips painting and the symptom is "it stopped drawing".
        foreach (var tool in new[] { ToolId.Brush, ToolId.Select, ToolId.Arrow, ToolId.DirectSelect })
        {
            var vm = Marqueed(tool);
            vm.DeselectCommand.Execute(null);
            Assert.False(vm.HasSelection);
        }
    }

    [AvaloniaFact]
    public void SelectAllTakesTheWholeCanvasWhileTheSelectToolIsActive()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ActiveTool = ToolId.Select;
        vm.SelectAllCommand.Execute(null);

        Assert.True(vm.HasSelection);
        var contour = vm.SelectionContours[0];
        Assert.Equal(vm.Doc.Scene.Width, contour.Max(p => p.X));
        Assert.Equal(vm.Doc.Scene.Height, contour.Max(p => p.Y));
    }

    [AvaloniaFact]
    public void SelectAllStillMeansTheObjectsWithAnArrowInHand()
    {
        // The other half of the same branch: "all" has to know what all means,
        // and with an arrow in hand it is the objects rather than the canvas.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        (vm.Doc.Scene.Guides ??= []).Add(new Guide { Kind = GuideKind.Line, X = 100, Y = 0 });
        vm.ActiveTool = ToolId.Arrow;

        vm.SelectAllCommand.Execute(null);
        Assert.False(vm.HasSelection); // not the canvas
    }

    // ---- B171: a selection belongs to its document ---------------------------

    [AvaloniaFact]
    public void ANewDocumentStartsWithNothingSelected()
    {
        var vm = Marqueed(ToolId.Select);
        vm.NewDocument(new NewDocumentSettings("Second", 640, 480, 12, 72, "#ffffff", false));

        Assert.False(vm.HasSelection);
    }

    [AvaloniaFact]
    public void SwitchingTabsDoesNotCarryASelectionAcross()
    {
        // Two real tabs, because the document the app opens on is not one —
        // `Tabs` is empty until something adds to it, which is part of why the
        // leak existed: with no tab there was nowhere for a selection to be put
        // down, so it stayed on the view model and outlived its document.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("First", 800, 600, 12, 72, "#ffffff", false));
        var first = vm.ActiveTab!;
        vm.ApplySelectionShape(Box(50, 50, 200, 200), add: false, subtract: false);
        Assert.True(vm.HasSelection);

        vm.NewDocument(new NewDocumentSettings("Second", 640, 480, 12, 72, "#ffffff", false));
        Assert.False(vm.HasSelection);

        // …and coming back finds it where it was left. The leak and the memory
        // are one fix: state that belongs to a document cannot travel to
        // another one once it has somewhere of its own to live.
        vm.ActiveTab = first;
        Assert.True(vm.HasSelection);

        vm.ActiveTab = vm.Tabs[^1];
        Assert.False(vm.HasSelection);
    }

    // ---- B173: Delete and Backspace act on the region ------------------------

    [AvaloniaFact]
    public void DeleteClearsWhatIsInsideTheMarquee()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, BrushSize = 40 };
        vm.ColorHex = "#101010";
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(160, 100, 1);
        vm.EndStroke();

        vm.ApplySelectionShape(Box(50, 50, 200, 200), add: false, subtract: false);
        vm.DeleteSelectionContentsCommand.Execute(null);

        var strokes = vm.PaintedCel().Strokes;
        Assert.Equal(ToolKind.ClearRegion, strokes[^1].Tool);

        using var bmp = Lightbox.Raster.FrameRasterizer.Rasterize(
            strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        Assert.Equal(0, bmp.GetPixel(130, 100).Alpha);

        // The outline stays: the next thing done with an emptied region is
        // usually putting something else in it.
        Assert.True(vm.HasSelection);
    }

    [AvaloniaFact]
    public void BackspaceFillsTheMarqueeWithTheBackgroundColour()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ColorHex = "#101010";
        vm.BackgroundColorHex = "#20c040";

        vm.ApplySelectionShape(Box(50, 50, 200, 200), add: false, subtract: false);
        vm.FillSelectionWithBackgroundCommand.Execute(null);

        var strokes = vm.PaintedCel().Strokes;
        Assert.Equal(ToolKind.Fill, strokes[^1].Tool);
        // The background, not the foreground — the two keys differ in what
        // they leave behind, which is the whole reason both exist.
        Assert.Equal("#20c040", strokes[^1].Color);

        using var bmp = Lightbox.Raster.FrameRasterizer.Rasterize(
            strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        var px = bmp.GetPixel(120, 120);
        Assert.Equal(0x20, px.Red);
        Assert.Equal(0xc0, px.Green);
        Assert.Equal(0x40, px.Blue);
    }

    [AvaloniaFact]
    public void DeleteStillRemovesTheSelectedLinesWhenNoRegionIsUp()
    {
        // The fallback is what lets `lines.delete` keep its meaning in the case
        // it used to have: marquee first, lines second.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(160, 100, 1);
        vm.EndStroke();

        var id = vm.PaintedCel().Strokes[^1].Id;
        vm.ActiveTool = ToolId.Arrow;
        vm.Selection.SelectStroke(id);
        Assert.False(vm.HasSelection);

        vm.DeleteSelectionContentsCommand.Execute(null);
        Assert.DoesNotContain(vm.PaintedCel().Strokes, s => s.Id == id);
    }

    [AvaloniaFact]
    public void BackspaceDoesNothingWithNoRegionUp()
    {
        // No fallback invented for it: Backspace has never meant anything on
        // the canvas, so there is no established behaviour to preserve.
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(160, 100, 1);
        vm.EndStroke();
        var before = vm.PaintedCel().Strokes.Count;

        vm.FillSelectionWithBackgroundCommand.Execute(null);
        Assert.Equal(before, vm.PaintedCel().Strokes.Count);
    }
}
