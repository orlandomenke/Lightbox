using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Tests;

public class StrokeFilterTests
{
    private static List<StrokePoint> ZigZag(int n = 30)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i < n; i++)
        {
            pts.Add(new StrokePoint(i * 4, 100 + (i % 2 == 0 ? 3 : -3), 0.8)); // hand tremor
        }
        return pts;
    }

    private static double Roughness(IReadOnlyList<StrokePoint> pts)
    {
        double sum = 0;
        for (var i = 1; i < pts.Count - 1; i++)
        {
            sum += Math.Abs(pts[i - 1].Y - 2 * pts[i].Y + pts[i + 1].Y); // second difference
        }
        return sum / (pts.Count - 2);
    }

    [Fact]
    public void MovingAverage_FiltersTremor_AndAnchorsEndpoints()
    {
        var raw = ZigZag();
        var smooth = StrokeFilters.MovingAverage(raw, 7);
        Assert.Equal(raw.Count, smooth.Count);
        Assert.True(Roughness(smooth) < Roughness(raw) / 4, "tremor must shrink substantially");
        Assert.Equal(raw[0].X, smooth[0].X);       // endpoints stay put
        Assert.Equal(raw[^1].X, smooth[^1].X);
    }

    [Fact]
    public void SavitzkyGolay_Smooths_ButKeepsAPeakBetterThanAveraging()
    {
        // a deliberate corner spike in the middle of a flat stroke
        var pts = Enumerable.Range(0, 31).Select(i => new StrokePoint(i * 4, 100, 0.8)).ToList();
        pts[15] = new StrokePoint(60, 130, 0.8);

        var savgol = StrokeFilters.SavitzkyGolay(ZigZag(), 7);
        Assert.True(Roughness(savgol) < Roughness(ZigZag()), "tremor shrinks");

        var savgolPeak = StrokeFilters.SavitzkyGolay(pts, 7)[15].Y;
        var averagedPeak = StrokeFilters.MovingAverage(pts, 7)[15].Y;
        Assert.True(savgolPeak > averagedPeak, $"SavGol keeps the peak ({savgolPeak:0.0}) better than MA ({averagedPeak:0.0})");
    }

    [Fact]
    public void PulledString_HasADeadZone_ThenTrailsTheCursor()
    {
        var s = new StrokeStabilizer { Mode = SmoothingMode.PulledString, LazyRadius = 20 };
        s.Begin(100, 100);

        var inside = s.FilterLive(110, 100);        // within the radius: string is slack
        Assert.Equal((100, 100), inside);

        var outside = s.FilterLive(140, 100);       // 40 px away: brush is pulled to radius distance
        Assert.Equal(120, outside.X, 3);
        Assert.Equal(100, outside.Y, 3);
    }

    [Fact]
    public void Ema_LagsBehind_AndConverges()
    {
        var s = new StrokeStabilizer { Mode = SmoothingMode.Ema, Strength = 0.5 };
        s.Begin(0, 0);
        var first = s.FilterLive(100, 0);
        Assert.True(first.X is > 0 and < 100, "EMA moves toward the cursor but lags");
        for (var i = 0; i < 60; i++) s.FilterLive(100, 0);
        Assert.Equal(100, s.FilterLive(100, 0).X, 1); // converged
    }
}

public class SmoothingVmTests
{
    [AvaloniaFact]
    public void SmoothStrokesCompat_MapsToTheMode()
    {
        // The brush store is shared across tests, so pin a known state first.
        var vm = new MainViewModel(null) { SmoothingMode = SmoothingMode.Laplacian };
        Assert.True(vm.SmoothStrokes);

        vm.SmoothStrokes = false;
        Assert.Equal(SmoothingMode.Off, vm.SmoothingMode);
        vm.SmoothStrokes = true;
        Assert.Equal(SmoothingMode.Laplacian, vm.SmoothingMode);
    }

    [AvaloniaFact]
    public void PulledString_RecordsTheSmoothedPath_NotTheCursor()
    {
        var vm = new MainViewModel(null)
        {
            SmoothingMode = SmoothingMode.PulledString,
            LazyRadius = 30,
        };
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(115, 100, 1); // inside the dead zone — no recorded movement
        vm.MoveStroke(200, 100, 1); // pulls the string
        vm.EndStroke();

        var stroke = ((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes[0];
        Assert.True(stroke.Points.Max(p => p.X) <= 171, "the brush trails the cursor by the radius");
    }

    [AvaloniaFact]
    public void LazyGizmoRadius_OnlyShowsForPulledStringPaintTools()
    {
        var vm = new MainViewModel(null) { SmoothingMode = SmoothingMode.PulledString, LazyRadius = 25 };
        Assert.Equal(25, vm.LazyRadiusForCursor);

        vm.ActiveTool = ToolId.Fill;
        Assert.Equal(0, vm.LazyRadiusForCursor);

        vm.ActiveTool = ToolId.Brush;
        vm.SmoothingMode = SmoothingMode.Ema;
        Assert.Equal(0, vm.LazyRadiusForCursor);
    }
}

public class DeltaCommitTests
{
    [AvaloniaFact]
    public void StrokeCommit_UndoRedo_WorksWithoutSnapshots()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(10, 10, 1);
        vm.MoveStroke(60, 10, 1);
        vm.EndStroke();
        var frame = (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Single(frame.Strokes);

        vm.UndoCommand.Execute(null);
        Assert.Empty(frame.Strokes);
        vm.RedoCommand.Execute(null);
        Assert.Single(frame.Strokes);
    }

    [AvaloniaFact]
    public void DeltaAndSnapshotSteps_Interleave_Cleanly()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.BeginStroke(10, 10, 1);
        vm.EndStroke();                       // delta step
        vm.AddFrameCommand.Execute(null);     // snapshot step
        vm.CurrentFrameIndex = 0;
        vm.BeginStroke(30, 30, 1);
        vm.EndStroke();                       // delta step

        PaintedFrame Frame0() => (PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!;
        Assert.Equal(2, Frame0().Strokes.Count);
        Assert.Equal(2, vm.Doc.Scene.FrameCount);

        vm.UndoCommand.Execute(null);         // undo second stroke
        Assert.Single(Frame0().Strokes);
        vm.UndoCommand.Execute(null);         // undo add-frame (snapshot swap)
        Assert.Equal(1, vm.Doc.Scene.FrameCount);
        vm.UndoCommand.Execute(null);         // undo first stroke
        Assert.Empty(((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes);

        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        vm.RedoCommand.Execute(null);
        Assert.Equal(2, ((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Count);
        Assert.Equal(2, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void StrokeUnderSelection_UndoRemovesTheDedupedClipToo()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        vm.ApplySelectionShape(
            [new(0, 0, 1), new(400, 0, 1), new(400, 400, 1), new(0, 400, 1)],
            add: false, subtract: false);
        vm.BeginStroke(50, 50, 1);
        vm.EndStroke();
        Assert.Single(vm.Doc.ClipRegions);

        vm.UndoCommand.Execute(null);
        Assert.Empty(vm.Doc.ClipRegions);
        Assert.Empty(((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes);
    }

    [AvaloniaFact]
    public void GlobalAntiAliasing_StampsIntoStrokesAndFills()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, AntiAliasing = false };
        vm.BeginStroke(20, 20, 1);
        vm.EndStroke();
        vm.ActiveTool = ToolId.Fill;
        vm.FillAt(400, 400);

        var strokes = ((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes;
        Assert.All(strokes, s => Assert.False(s.Brush.AntiAlias));

        vm.AntiAliasing = true;
        vm.ActiveTool = ToolId.Brush;
        vm.BeginStroke(60, 60, 1);
        vm.EndStroke();
        Assert.True(strokes[^1].Brush.AntiAlias);
    }
}
