using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The artist's line matches through the timeline commands (Q271): ＋ Inbetween
/// draws a gap from the matches on its opening drawing, and Redraw inbetweens
/// replaces a run's inbetweens with what the keys and matches give now — in
/// one undo step, never touching a key or a breakdown.
/// </summary>
[Collection("BrushState")]
public sealed class MatchedInbetweenVmTests : BrushStateIsolated
{
    private static void Draw(MainViewModel vm, params (double X, double Y)[] points)
    {
        vm.BeginStroke(points[0].X, points[0].Y, 0.5);
        foreach (var p in points.Skip(1)) vm.MoveStroke(p.X, p.Y, 0.5);
        vm.EndStroke();
    }

    /// <summary>
    /// Key A at frame 1: an L as one stroke. Key B at frame 2: the same L
    /// moved right by 40, drawn as two strokes, the foot first and backwards —
    /// what the matcher alone pairs wrongly.
    /// </summary>
    private static MainViewModel TwoKeys()
    {
        var vm = new MainViewModel { SmoothStrokes = false };
        Draw(vm, (100, 100), (100, 200), (100, 300), (150, 300), (200, 300));
        vm.AddFrameCommand.Execute(null);
        Draw(vm, (240, 300), (190, 300), (146, 300));
        Draw(vm, (140, 294), (140, 200), (140, 100));
        vm.CurrentFrameIndex = 0;
        vm.TweenCount = 1;
        vm.TweenEasing = Easing.Linear;
        return vm;
    }

    private static List<LineMatch> MatchTheL(MainViewModel vm)
    {
        var layer = vm.PaintLayer();
        var a = layer.Cels[0].Frame!.Strokes;
        var b = layer.Cels[^1].Frame!.Strokes;
        return
        [
            new LineMatch
            {
                A = [new PieceRef(a[0].Id, 0), new PieceRef(a[0].Id, 1)],
                B = [new PieceRef(b[0].Id, 0), new PieceRef(b[1].Id, 0)],
            },
        ];
    }

    /// <summary>How far the drawing's furthest point lies from the L halfway between the keys.</summary>
    private static double OffTheHalfwayL(Frame frame)
    {
        StrokePoint[] l = [new(120, 100, 0), new(120, 300, 0), new(220, 300, 0)];
        return frame.Strokes.SelectMany(s => s.Points).Max(p =>
            Math.Min(GeometryOps.DistToSegment(p, l[0], l[1]), GeometryOps.DistToSegment(p, l[1], l[2])));
    }

    [AvaloniaFact]
    public void WithoutMatchesTheInbetweenIsScrambled()
    {
        // The case the matches are for, measured so the next test means something.
        var vm = TwoKeys();

        vm.InsertInbetweensCommand.Execute(null);

        Assert.True(OffTheHalfwayL(vm.PaintLayer().Cels[1].Frame!) > 20);
    }

    [AvaloniaFact]
    public void InsertInbetweensDrawsAGapFromItsMatches()
    {
        var vm = TwoKeys();
        vm.PaintLayer().Cels[0].Frame!.Matches = MatchTheL(vm);

        vm.InsertInbetweensCommand.Execute(null);

        var off = OffTheHalfwayL(vm.PaintLayer().Cels[1].Frame!);
        Assert.True(off < 4, $"{off:0.0}px off the halfway L");
    }

    [AvaloniaFact]
    public void RedrawInbetweensReplacesARunsInbetweensInOneStep()
    {
        var vm = TwoKeys();
        vm.InsertInbetweensCommand.Execute(null);
        var layer = vm.PaintLayer();
        var keyA = layer.Cels[0].Frame!;
        var keyB = layer.Cels[2].Frame!;
        Assert.Equal(FrameRole.Inbetween, layer.Cels[1].Frame!.Role);
        keyA.Matches = MatchTheL(vm);
        vm.CurrentFrameIndex = 1;

        vm.RedrawInbetweensCommand.Execute(null);

        layer = vm.PaintLayer();
        Assert.Equal(3, layer.Cels.Count);                         // replaced, not inserted
        Assert.True(OffTheHalfwayL(layer.Cels[1].Frame!) < 4);
        Assert.Equal(FrameRole.Inbetween, layer.Cels[1].Frame!.Role);
        Assert.Same(keyA.Strokes[0].Points, layer.Cels[0].Frame!.Strokes[0].Points);
        Assert.Equal(keyB.Strokes.Count, layer.Cels[2].Frame!.Strokes.Count);

        vm.UndoCommand.Execute(null);
        Assert.True(OffTheHalfwayL(vm.PaintLayer().Cels[1].Frame!) > 20);
    }

    [AvaloniaFact]
    public void RedrawingARunWithNoInbetweensChangesNothing()
    {
        var vm = TwoKeys();
        var before = vm.PaintLayer().Cels.Select(c => c.Frame).ToList();

        vm.RedrawInbetweensCommand.Execute(null);

        Assert.Equal(before, vm.PaintLayer().Cels.Select(c => c.Frame).ToList());
    }
}
