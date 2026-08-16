using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests.Timeline;

/// <summary>
/// The substrate motion path and spacing visualization share (Q98): where the
/// subject is on each drawing around the playhead. The interesting mistakes
/// are the exposure sheet's — a hold is one drawing, not two ticks — and the
/// point resolution's: an authored pivot beats the derived centre, a socket
/// is not the subject, and erasing says nothing about where the subject is.
/// </summary>
public class MotionTrailTests
{
    /// <summary>A layer whose cels are keyed where the string has a letter.</summary>
    /// <example><c>"A--B--C"</c> is three drawings on 3s.</example>
    private static Layer Row(string pattern, Func<char, Frame> make)
    {
        var layer = new Layer { Name = "L", Kind = LayerKind.Painted };
        var byLetter = new Dictionary<char, Frame>();
        foreach (var c in pattern)
        {
            if (c == '-')
            {
                layer.Cels.Add(new Cel { Frame = null });
                continue;
            }
            if (!byLetter.TryGetValue(c, out var frame))
            {
                byLetter[c] = frame = make(c);
                frame.Id = c.ToString();
            }
            layer.Cels.Add(new Cel { Frame = frame });
        }
        return layer;
    }

    /// <summary>A drawing whose only stroke spans (x±10, 40..60), centring at (x, 50).</summary>
    private static Frame DrawingAt(double x) => new()
    {
        Strokes =
        [
            new Stroke { Points = [new StrokePoint(x - 10, 40, 1), new StrokePoint(x + 10, 60, 1)] },
        ],
    };

    [Fact]
    public void TheTrailRunsInTimelineOrderWithTheCurrentDrawingMarked()
    {
        // A, B, C at x = 10, 30, 70 — uneven on purpose; the tick distances
        // ARE the spacing chart, so order and positions must both hold.
        var layer = Row("ABC", c => DrawingAt(10 + "ABC".IndexOf(c) switch { 0 => 0, 1 => 20, _ => 60 }));

        var points = MotionTrail.PointsAround(new Scene(), layer, index: 1, before: 2, after: 2);

        Assert.Equal(["A", "B", "C"], points.Select(p => p.FrameId));
        Assert.Equal([10.0, 30.0, 70.0], points.Select(p => p.X));
        Assert.All(points, p => Assert.Equal(50, p.Y));
        Assert.Equal("B", Assert.Single(points, p => p.Current).FrameId);
    }

    [Fact]
    public void AHoldIsOneTickNotTwo()
    {
        // On 2s, four cels are two drawings. A tick per cel would put two
        // coincident dots on every drawing — invisible, and a lie about how
        // many drawings there are.
        var layer = Row("A-B-", c => DrawingAt(c == 'A' ? 10 : 50));

        var points = MotionTrail.PointsAround(new Scene(), layer, index: 0, before: 0, after: 4);

        Assert.Equal(["A", "B"], points.Select(p => p.FrameId));
    }

    [Fact]
    public void AnAuthoredPivotBeatsTheDerivedCentre()
    {
        var scene = new Scene();
        var pivot = Anchors.Declare(scene, "ground", AnchorKind.Pivot);
        var frame = DrawingAt(30);
        frame.Anchors = new() { [pivot.Id] = new AnchorPoint(33, 77) };
        var layer = new Layer { Cels = [new Cel { Frame = frame }] };

        var point = Assert.Single(MotionTrail.PointsAround(scene, layer, 0, 0, 0));

        Assert.True(point.Anchored);
        Assert.Equal(33, point.X);
        Assert.Equal(77, point.Y);
    }

    [Fact]
    public void ASocketIsNotTheSubject()
    {
        // A socket is where a weapon attaches, not where the character is —
        // tracking one would draw the sword's arc when the artist asked for
        // the character's.
        var scene = new Scene();
        var socket = Anchors.Declare(scene, "hand", AnchorKind.Socket);
        var frame = DrawingAt(30);
        frame.Anchors = new() { [socket.Id] = new AnchorPoint(999, 999) };
        var layer = new Layer { Cels = [new Cel { Frame = frame }] };

        var point = Assert.Single(MotionTrail.PointsAround(scene, layer, 0, 0, 0));

        Assert.False(point.Anchored);
        Assert.Equal(30, point.X);
    }

    [Fact]
    public void ErasingSaysNothingAboutWhereTheSubjectIs()
    {
        var frame = DrawingAt(30);
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Eraser,
            Points = [new StrokePoint(500, 500, 1)],
        });
        var layer = new Layer { Cels = [new Cel { Frame = frame }] };

        var point = Assert.Single(MotionTrail.PointsAround(new Scene(), layer, 0, 0, 0));

        Assert.Equal(30, point.X);
        Assert.Equal(50, point.Y);
    }

    [Fact]
    public void AnEmptyDrawingHasNoTickAndTheDocumentPivotIsNotAFallback()
    {
        // Scene.Pivot is one point for the whole document; a trail through a
        // point that cannot move is a dot, so an empty drawing contributes
        // nothing rather than the pivot.
        var scene = new Scene { Pivot = new Pivot { X = 480, Y = 270 } };
        var layer = Row("AB", c => c == 'A' ? DrawingAt(10) : new Frame());

        var points = MotionTrail.PointsAround(scene, layer, index: 0, before: 0, after: 2);

        Assert.Equal(["A"], points.Select(p => p.FrameId));
    }
}
