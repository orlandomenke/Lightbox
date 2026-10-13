using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Tests;

/// <summary>
/// Q265, a group as a unit: what moves a drawing moves the strokes inside its
/// groups, because the group entry itself has no points to move.
/// </summary>
public class StrokeGroupEditingTests
{
    private static Stroke Line(double x, double y, double size = 10) => new()
    {
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 50, y, 1)],
        Brush = new BrushSettings { Size = size },
    };

    private static Stroke Group(params Stroke[] strokes) => new()
    {
        Tool = ToolKind.Group,
        Group = new StrokeGroup { Strokes = [.. strokes] },
    };

    [Fact]
    public void TransformingADrawingMovesTheStrokesInsideItsGroups()
    {
        var inner = Line(10, 20);
        var innermost = Line(30, 40);
        var carve = Line(50, 60);
        var group = Group(inner, Group(innermost));
        group.Group!.Mask = new LayerMask { Frame = new Frame { Strokes = [carve] } };
        var frame = new Frame { Strokes = [Line(0, 0), group] };

        var moved = TransformOps.TransformFrame(frame, (x, y) => (x * 2 + 100, y * 2), sizeScale: 2);

        Assert.Equal(2, moved);
        Assert.Equal((120.0, 40.0, 20.0), (inner.Points[0].X, inner.Points[0].Y, inner.Brush.Size));
        Assert.Equal((160.0, 80.0, 20.0), (innermost.Points[0].X, innermost.Points[0].Y, innermost.Brush.Size));
        // The mask is the shape the group is carved by: left behind, the
        // drawing slides out from under its own carve.
        Assert.Equal((200.0, 120.0, 20.0), (carve.Points[0].X, carve.Points[0].Y, carve.Brush.Size));
    }

    [Fact]
    public void AGroupsSelectionClipsTravelWithIt()
    {
        var inner = Line(10, 20);
        inner.ClipId = "clip-a";
        var frame = new Frame { Strokes = [Group(inner)] };

        TransformOps.TransformFrame(frame, (x, y) => (x + 5, y + 5), mapClip: id => id + "-moved");

        Assert.Equal("clip-a-moved", inner.ClipId);
    }

    [Fact]
    public void AGroupIsAsBigAsTheStrokesInsideIt()
    {
        // The transform box is drawn round these bounds. A group measured by
        // its own (absent) points would leave the merged drawing outside it.
        var group = Group(Line(10, 20, size: 10), Group(Line(200, 150, size: 20)));

        var bounds = TransformOps.Bounds(group);
        var withLoose = TransformOps.Bounds([new Frame { Strokes = [Line(-40, 0, size: 2), group] }]);

        Assert.Equal((5.0, 15.0, 260.0, 160.0), bounds);
        Assert.Equal((-41.0, -1.0, 260.0, 160.0), withLoose);
        Assert.Null(TransformOps.Bounds(Group()));
    }

    [Fact]
    public void StoredPixelsInsideGroupsAreHandedToWhateverResamplesADrawings()
    {
        // A transform resamples a drawing's baseline in the app, where Skia
        // is. A group's baseline is the same kind of pixels one level down.
        var masked = Group(Line(0, 0));
        masked.Group!.PngBase64 = "inner";
        masked.Group.Mask = new LayerMask { Frame = new Frame { PngBase64 = "mask" } };
        var outer = Group(masked, Line(1, 1));
        outer.Group!.PngBase64 = "outer";
        var plain = Group(Line(2, 2));
        var strokes = new List<Stroke> { Line(3, 3), outer, plain };
        var seen = new List<string>();

        StrokeGroups.ResamplePixels(strokes, holder =>
        {
            seen.Add(holder.PngBase64!);
            holder.PngBase64 += "@moved";
        });

        Assert.Equal(["outer", "inner", "mask"], seen);
        Assert.Equal("outer@moved", outer.Group.PngBase64);
        Assert.Equal("inner@moved", masked.Group.PngBase64);
        Assert.Equal("mask@moved", masked.Group.Mask.Frame.PngBase64);
        Assert.Null(plain.Group!.PngBase64);
    }
}
