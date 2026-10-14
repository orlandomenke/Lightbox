using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Tests.Inbetween;

public class StrokeRecordCleanerTests
{
    private static Stroke Line(double x0, double y0, double x1, double y1, double size = 6, ToolKind tool = ToolKind.Brush) => new()
    {
        Tool = tool,
        Brush = new BrushSettings { Size = size },
        Points = [new(x0, y0, 1), new(x1, y1, 1)],
    };

    [Fact]
    public void FullyErasedStroke_IsDropped_ErasersToo()
    {
        var victim = Line(10, 50, 90, 50);
        // Fat eraser drawn right along the victim.
        var eraser = Line(5, 50, 95, 50, size: 20, tool: ToolKind.Eraser);
        var result = StrokeRecordCleaner.EffectiveStrokes([victim, eraser]);
        Assert.Empty(result);
    }

    [Fact]
    public void PartiallyErasedStroke_IsKept()
    {
        var victim = Line(0, 50, 100, 50);
        // Small eraser dab only touches the middle.
        var eraser = Line(48, 50, 52, 50, size: 10, tool: ToolKind.Eraser);
        var result = StrokeRecordCleaner.EffectiveStrokes([victim, eraser]);
        var s = Assert.Single(result);
        Assert.Same(victim, s);
    }

    [Fact]
    public void EraserBeforeStroke_DoesNotAffectIt()
    {
        // Erase first, paint after — the paint is on top and stays.
        var eraser = Line(0, 50, 100, 50, size: 30, tool: ToolKind.Eraser);
        var stroke = Line(10, 50, 90, 50);
        var result = StrokeRecordCleaner.EffectiveStrokes([eraser, stroke]);
        var s = Assert.Single(result);
        Assert.Same(stroke, s);
    }

    [Fact]
    public void UntouchedStrokes_SurviveInOrder()
    {
        var s1 = Line(0, 10, 100, 10);
        var s2 = Line(0, 90, 100, 90);
        var eraser = Line(0, 50, 100, 50, size: 8, tool: ToolKind.Eraser); // hits neither
        var result = StrokeRecordCleaner.EffectiveStrokes([s1, eraser, s2]);
        Assert.Equal(2, result.Count);
        Assert.Same(s1, result[0]);
        Assert.Same(s2, result[1]);
    }

    // ---- Q265: a merged drawing is read through its groups ----------------------

    private static Stroke Group(params Stroke[] strokes) => new()
    {
        Tool = ToolKind.Group,
        Group = new StrokeGroup { Strokes = [.. strokes] },
    };

    [Fact]
    public void AGroupIsReadAsTheStrokesInsideIt()
    {
        // Sent as it stands, a group reaches a model as one "brush" stroke
        // with no points — and the merged line art it holds as nothing.
        var below = Line(0, 10, 100, 10);
        var inner = Line(0, 50, 100, 50);
        var innermost = Line(0, 70, 100, 70);
        var after = Line(0, 90, 100, 90);

        var result = StrokeRecordCleaner.EffectiveStrokes([below, Group(inner, Group(innermost)), after]);

        Assert.Equal([below, inner, innermost, after], result);
        Assert.DoesNotContain(result, s => s.Tool == ToolKind.Group);
    }

    [Fact]
    public void AnEraserInsideAGroupErasesOnlyInsideIt()
    {
        // The same line drawn on both layers; the upper layer rubbed its own
        // out. The lower one is still on the page.
        var below = Line(10, 50, 90, 50);
        var above = Line(10, 50, 90, 50);
        var eraser = Line(5, 50, 95, 50, size: 20, tool: ToolKind.Eraser);

        var result = StrokeRecordCleaner.EffectiveStrokes([below, Group(above, eraser)]);

        Assert.Same(below, Assert.Single(result));
    }

    [Fact]
    public void AnEraserAfterAGroupErasesWhatIsInsideIt()
    {
        // Drawn after the merge, it acts on the whole drawing.
        var below = Line(10, 20, 90, 20);
        var inner = Line(10, 50, 90, 50);
        var eraser = Line(5, 50, 95, 50, size: 20, tool: ToolKind.Eraser);

        var result = StrokeRecordCleaner.EffectiveStrokes([below, Group(inner), eraser]);

        Assert.Same(below, Assert.Single(result));
    }

    [Fact]
    public void AGroupNobodyCanSeeIsNotRead()
    {
        // Q266. A group at zero opacity, or behind a mask with nothing painted
        // on it, shows no ink at all — and a model sent its lines would
        // inbetween a drawing that is not on the page.
        var shown = Line(0, 10, 100, 10);
        var transparent = Group(Line(0, 50, 100, 50));
        transparent.Group!.Opacity = 0;
        var maskedOut = Group(Line(0, 70, 100, 70));
        maskedOut.Group!.Mask = new LayerMask();
        var nested = Group(Group(Line(0, 80, 100, 80)));
        nested.Group!.Strokes[0].Group!.Opacity = 0;

        Assert.Equal([shown], StrokeRecordCleaner.EffectiveStrokes([shown, transparent, maskedOut, nested]));
    }

    [Fact]
    public void AGroupThatIsOnlyPartlyHiddenIsStillReadWhole()
    {
        // What was decided, and its limit: the reader does not cut lines by a
        // mask. A faint group, a partly masked one, a mask that is switched
        // off and an inverted empty mask (which hides nothing) are all read.
        var faint = Group(Line(0, 10, 100, 10));
        faint.Group!.Opacity = 0.05;
        var partly = Group(Line(0, 30, 100, 30));
        partly.Group!.Mask = new LayerMask { Frame = new Frame { Strokes = [Line(0, 30, 10, 30)] } };
        var off = Group(Line(0, 50, 100, 50));
        off.Group!.Mask = new LayerMask { Disabled = true };
        var inverted = Group(Line(0, 70, 100, 70));
        inverted.Group!.Mask = new LayerMask { Inverted = true };

        Assert.Equal(4, StrokeRecordCleaner.EffectiveStrokes([faint, partly, off, inverted]).Count);
    }

    [Fact]
    public void ADrawingWithNoGroupIsReadExactlyAsBefore()
    {
        var a = Line(0, 10, 100, 10);
        var b = Line(0, 90, 100, 90);

        Assert.Equal([a, b], StrokeRecordCleaner.EffectiveStrokes([a, b]));
    }

    // ---- B233: a cleared region is an erasure too -----------------------------

    /// <summary>A square emptied out of the drawing — the "clear selection" stroke.</summary>
    private static Stroke Cleared(double x, double y, double w, double h) => new()
    {
        Tool = ToolKind.ClearRegion,
        Brush = new BrushSettings { Size = 1 },
        Points =
        [
            new(x, y, 1), new(x + w, y, 1), new(x + w, y + h, 1), new(x, y + h, 1),
        ],
    };

    /// <summary>
    /// B233: clearing a selection took the paint away and left both halves of
    /// it in the effective record — the strokes it emptied, and the clear
    /// itself. Anything asking what is on the drawing was told about a shape
    /// that is not there and a line that was rubbed out.
    /// </summary>
    [Fact]
    public void ClearedRegion_IsDropped_AndSoIsWhatItCleared()
    {
        var victim = Line(20, 50, 80, 50);
        var survivor = Line(20, 300, 80, 300);
        var cleared = Cleared(0, 0, 100, 100);

        var result = StrokeRecordCleaner.EffectiveStrokes([victim, survivor, cleared]);

        var s = Assert.Single(result);
        Assert.Same(survivor, s);
    }

    /// <summary>
    /// A cleared region is an <em>area</em>, not a path along its outline.
    /// Reading its points the way an eraser's are read would only cover what
    /// sits near the edge, so a shape cleared out of the middle of a drawing
    /// would come back — which is the failure this test would catch and a
    /// tests-only-the-outline implementation would not.
    /// </summary>
    [Fact]
    public void ClearedRegion_EmptiesItsInsideRatherThanItsOutline()
    {
        // Well inside a 200-square clear, and nowhere near any of its edges.
        var deepInside = Line(95, 100, 105, 100);
        var cleared = Cleared(0, 0, 200, 200);

        Assert.Empty(StrokeRecordCleaner.EffectiveStrokes([deepInside, cleared]));
    }

    /// <summary>
    /// And it only empties its own inside: a hole in the cleared contour is not
    /// part of it, under the same even-odd rule the render composited it with.
    /// </summary>
    [Fact]
    public void ClearedRegion_LeavesWhatIsInsideItsHoles()
    {
        var inTheHole = Line(95, 100, 105, 100);
        var cleared = Cleared(0, 0, 200, 200);
        cleared.Holes = [[new(80, 80, 1), new(120, 80, 1), new(120, 120, 1), new(80, 120, 1)]];

        var s = Assert.Single(StrokeRecordCleaner.EffectiveStrokes([inTheHole, cleared]));
        Assert.Same(inTheHole, s);
    }

    /// <summary>
    /// Order still decides, as it does for an eraser: ink drawn after a clear
    /// was never cleared, and dropping it would delete work an artist can see.
    /// </summary>
    [Fact]
    public void ClearedRegion_DoesNotTouchInkDrawnAfterIt()
    {
        var cleared = Cleared(0, 0, 200, 200);
        var after = Line(95, 100, 105, 100);

        var s = Assert.Single(StrokeRecordCleaner.EffectiveStrokes([cleared, after]));
        Assert.Same(after, s);
    }
}

public class EraserResurrectionTests
{
    private static Stroke Line(double x0, double y0, double x1, double y1, string? label = null, double size = 6, ToolKind tool = ToolKind.Brush) => new()
    {
        Tool = tool,
        Label = label,
        Brush = new BrushSettings { Size = size },
        Points = [new(x0, y0, 1), new(x1, y1, 1)],
    };

    [Fact]
    public void ErasedStroke_DoesNotAppearInInbetweens()
    {
        // Key A: a kept stroke + an erased stroke (the user "deleted" it).
        var kept = Line(0, 10, 100, 10, "kept");
        var deleted = Line(0, 200, 100, 200);
        var eraser = Line(-5, 200, 105, 200, size: 24, tool: ToolKind.Eraser);
        var a = new[] { kept, deleted, eraser };

        // Key B: only the kept stroke, moved.
        var b = new[] { Line(0, 60, 100, 60, "kept") };

        foreach (var t in new[] { 0.25, 0.5, 0.75 })
        {
            var tween = Inbetweener.Inbetween(a, b, t, Easing.Linear);
            var s = Assert.Single(tween); // ONLY the kept stroke — nothing resurrected
            Assert.Equal("kept", s.Label);
            Assert.All(s.Points, p => Assert.InRange(p.Y, 9, 61));
        }
    }

    [Fact]
    public void TweenOutput_PreservesPaintOrder()
    {
        // Deliberately positioned so greedy matching visits them out of order.
        var a = new[]
        {
            Line(0, 0, 10, 0, "bottom"),
            Line(500, 500, 510, 500, "top"),
        };
        var b = new[]
        {
            Line(0, 20, 10, 20, "bottom"),
            Line(500, 480, 510, 480, "top"),
        };
        var tween = Inbetweener.Inbetween(a, b, 0.5, Easing.Linear);
        Assert.Equal(2, tween.Count);
        Assert.Equal("bottom", tween[0].Label); // painted first, stamped first
        Assert.Equal("top", tween[1].Label);
    }
}
