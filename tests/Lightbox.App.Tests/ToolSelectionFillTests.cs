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
        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Empty(frame.Strokes);
    }
}

public class FillToolTests
{
    private static PaintedFrame FrameOf(MainViewModel vm) =>
        (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;

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

        var strokes = ((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes;
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

    [AvaloniaFact]
    public void PaintOutsideTheSelection_LeavesNoVisiblePixels()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ApplySelectionShape(Box(400, 400, 500, 500), add: false, subtract: false);

        // stroke fully outside the selected box
        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(150, 50, 1);
        vm.EndStroke();

        var strokes = ((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes;
        using var bmp = Lightbox.Raster.FrameRasterizer.Rasterize(
            strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        Assert.Equal(0, bmp.GetPixel(100, 50).Alpha); // clipped away entirely
    }
}
