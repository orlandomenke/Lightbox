using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B290 — a region-limited transform that catches an eraser stroke must not
/// resurrect the ink that eraser rubbed out. The marquee's majority test reads
/// geometry, so it picks up erasures like any line; carrying one away used to
/// reveal every stroke it had been holding down, as a ghost in the preview and
/// permanently on apply. The commit now leaves a stay copy behind wherever the
/// moved erasure was still erasing something that stays.
/// </summary>
public class TransformErasuresTests(ITestOutputHelper output)
{
    private static Stroke Line(double x0, double y0, double x1, double y1, ToolKind tool = ToolKind.Brush)
        => new()
        {
            Color = "#000000",
            Tool = tool,
            Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.15 },
            Points = [new(x0, y0, 1), new((x0 + x1) / 2, (y0 + y1) / 2, 1), new(x1, y1, 1)],
        };

    private static readonly TransformOps.PointMap MoveRight = (x, y) => (x + 200, y);

    [Fact]
    public void AMovedErasureLeavesACopyHoldingStayingInkDown()
    {
        var ink = Line(10, 50, 90, 50);
        var erasure = Line(40, 10, 40, 90, ToolKind.Eraser);
        var frame = new Frame { Strokes = [ink, erasure] };

        var moved = TransformErasures.TransformFrame(
            frame, MoveRight, 1, s => ReferenceEquals(s, erasure));

        Assert.Equal(1, moved);
        Assert.Equal(3, frame.Strokes.Count);
        // The copy sits directly beneath the moved original, at the original spot.
        Assert.Equal(ToolKind.Eraser, frame.Strokes[1].Tool);
        Assert.Equal(40, frame.Strokes[1].Points[0].X, 3);
        Assert.Equal(240, frame.Strokes[2].Points[0].X, 3);
        Assert.NotEqual(frame.Strokes[1].Id, frame.Strokes[2].Id);

        using var bmp = FrameRasterizer.Rasterize(frame.Strokes, 300, 100);
        var crossing = bmp.GetPixel(40, 50).Alpha;
        var intact = bmp.GetPixel(15, 50).Alpha;
        output.WriteLine($"erased crossing {crossing}, intact ink {intact}");
        Assert.Equal(0, crossing);     // the ghost this bug was about
        Assert.True(intact > 200);     // the staying line is otherwise untouched
    }

    [Fact]
    public void AnErasureCarvingOnlyMovingInkLeavesNoStrayCopy()
    {
        // An artist carves a shape with the eraser and moves the whole thing:
        // the carving travels, and nothing stays behind — a copy that erases
        // nothing is exactly the stray Q102 exists to stop.
        var ink = Line(10, 50, 90, 50);
        var erasure = Line(40, 10, 40, 90, ToolKind.Eraser);
        var frame = new Frame { Strokes = [ink, erasure] };

        var moved = TransformErasures.TransformFrame(frame, MoveRight, 1, _ => true);

        Assert.Equal(2, moved);
        Assert.Equal(2, frame.Strokes.Count);

        using var bmp = FrameRasterizer.Rasterize(frame.Strokes, 300, 100);
        var carved = bmp.GetPixel(240, 50).Alpha;
        var movedInk = bmp.GetPixel(215, 50).Alpha;
        output.WriteLine($"carved hole {carved}, moved ink {movedInk}");
        Assert.Equal(0, carved);          // the carving followed the shape
        Assert.True(movedInk > 200);      // and the shape itself arrived
        Assert.Equal(0, bmp.GetPixel(40, 50).Alpha);  // nothing left behind
        Assert.Equal(0, bmp.GetPixel(15, 50).Alpha);
    }

    [Fact]
    public void ABaselineFrameAlwaysKeepsTheCopy()
    {
        // Baseline pixels never move under a region-limited transform, and the
        // stroke record cannot say where their ink is — so the safe answer is
        // the only answer: the erasure's copy stays.
        using var paper = new SKBitmap(new SKImageInfo(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        paper.Erase(SKColors.Black);
        var erasure = Line(40, 10, 40, 90, ToolKind.Eraser);
        var frame = new Frame { PngBase64 = PngCodec.Encode(paper), Strokes = [erasure] };

        TransformErasures.TransformFrame(frame, MoveRight, 1, _ => true);

        Assert.Equal(2, frame.Strokes.Count);
        Assert.Equal(40, frame.Strokes[0].Points[0].X, 3);
        Assert.Equal(240, frame.Strokes[1].Points[0].X, 3);
    }

    [Fact]
    public void AClearedRegionGetsTheSameTreatment()
    {
        // ClearRegion is the area form of the same act (B233's grouping): a
        // moved clear leaves its copy exactly as a moved eraser path does.
        var ink = Line(10, 50, 90, 50);
        var clear = new Stroke
        {
            Tool = ToolKind.ClearRegion,
            Points = [new(30, 30, 1), new(60, 30, 1), new(60, 70, 1), new(30, 70, 1)],
        };
        var frame = new Frame { Strokes = [ink, clear] };

        TransformErasures.TransformFrame(frame, MoveRight, 1, s => ReferenceEquals(s, clear));

        Assert.Equal(3, frame.Strokes.Count);
        using var bmp = FrameRasterizer.Rasterize(frame.Strokes, 300, 100);
        var cleared = bmp.GetPixel(45, 50).Alpha;
        var intact = bmp.GetPixel(15, 50).Alpha;
        output.WriteLine($"cleared {cleared}, intact ink {intact}");
        Assert.Equal(0, cleared);
        Assert.True(intact > 200);
    }

    // ---- MovingWithin: the region filter judges visible ink (B297) ----------

    /// <summary>A full-height mask over the column x0..x1.</summary>
    private static bool[] MaskColumn(int w, int h, int x0, int x1)
    {
        var mask = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = x0; x <= x1; x++) mask[y * w + x] = true;
        }
        return mask;
    }

    // ---- Q265: a group under a region ------------------------------------------

    private static Stroke Group(params Stroke[] strokes) => new()
    {
        Tool = ToolKind.Group,
        Group = new StrokeGroup { Strokes = [.. strokes] },
    };

    [Fact]
    public void ARegionOverAGroupsInkCatchesTheGroup()
    {
        // The entry has no points of its own. Judged by them, a selection
        // drawn straight over a merged drawing found nothing to move — where
        // the pixels a merge used to leave would have moved.
        var group = Group(Line(30, 50, 70, 50));
        var elsewhere = Group(Line(120, 150, 170, 150));

        var moving = TransformErasures.MovingWithin([group, elsewhere], MaskColumn(200, 200, 20, 80), 200, 200);

        Assert.Equal([0], moving);
    }

    [Fact]
    public void InkAGroupErasedInsideItselfIsNotThere()
    {
        // Rule three, inside the group: its own eraser took the line, so no
        // region can mean it.
        var group = Group(Line(30, 50, 70, 50), Line(-40, 50, 110, 50, ToolKind.Eraser));

        var moving = TransformErasures.MovingWithin([group], MaskColumn(200, 200, 20, 80), 200, 200);

        Assert.Empty(moving);
    }

    [Fact]
    public void AGroupPartlyInsideARegionCrossesIt()
    {
        var mask = MaskColumn(200, 200, 20, 80);
        var straddling = Group(Line(30, 50, 70, 50), Line(120, 50, 170, 50));
        var within = Group(Line(30, 50, 70, 50), Line(30, 70, 70, 70));
        var reading = new TransformErasures.RegionReading([straddling, within], mask, 200, 200);

        Assert.True(reading.Crosses(straddling));
        Assert.False(reading.Crosses(within));
    }

    [Fact]
    public void AGroupCutByARegionMovesItsInsideAndLeavesItsOutside()
    {
        // What flattened pixels did under a selection, kept as strokes: two
        // copies of the group under complementary clips, as a single stroke
        // the region cuts through becomes two.
        ClipRegionRegistry.Register("q265_in", new ClipRegion
        {
            Contours = [[new(0, 0, 1), new(100, 0, 1), new(100, 100, 1), new(0, 100, 1)]],
        });
        ClipRegionRegistry.Register("q265_out", new ClipRegion
        {
            Contours = [[new(100, 0, 1), new(300, 0, 1), new(300, 100, 1), new(100, 100, 1)]],
        });
        // Carried to where the moving half lands.
        ClipRegionRegistry.Register("q265_in_moved", new ClipRegion
        {
            Contours = [[new(0, 100, 1), new(100, 100, 1), new(100, 200, 1), new(0, 200, 1)]],
        });
        var inner = Line(20, 50, 180, 50);
        var group = Group(inner);
        var frame = new Frame { Strokes = [group] };
        TransformOps.PointMap down = (x, y) => (x, y + 100);

        var moved = TransformErasures.TransformFrame(
            frame, down, 1, s => ReferenceEquals(s, group),
            split: _ => new TransformErasures.RegionClips("q265_in_moved", "q265_out"));

        Assert.Equal(1, moved);
        Assert.Equal(2, frame.Strokes.Count);
        Assert.Equal("q265_out", frame.Strokes[0].ClipId);
        Assert.Equal("q265_in_moved", frame.Strokes[1].ClipId);
        Assert.Equal(50, frame.Strokes[0].Group!.Strokes[0].Points[0].Y);
        Assert.Equal(150, frame.Strokes[1].Group!.Strokes[0].Points[0].Y);

        using var bmp = FrameRasterizer.Rasterize(frame.Strokes, 300, 200);
        var (leftOld, rightOld) = (bmp.GetPixel(50, 50).Alpha, bmp.GetPixel(150, 50).Alpha);
        var (leftNew, rightNew) = (bmp.GetPixel(50, 150).Alpha, bmp.GetPixel(150, 150).Alpha);
        output.WriteLine($"old row: left {leftOld}, right {rightOld}; new row: left {leftNew}, right {rightNew}");
        Assert.Equal(0, leftOld);          // the part inside the region left
        Assert.True(rightOld > 200);       // the part outside it stayed
        Assert.True(leftNew > 200);        // and arrived
        Assert.Equal(0, rightNew);         // without bringing the rest along
    }

    [Fact]
    public void TheBoxRoundADrawingWrapsWhatIsInsideItsGroups()
    {
        // The handles go round this box, and a drawing with no box is refused
        // as "nothing to transform". A merged drawing that is one group
        // measured as empty by its own (absent) points.
        var page = new SKRect(0, 0, 300, 300);
        var rubbed = Line(200, 200, 260, 200);
        var group = Group(
            Line(30, 50, 70, 50),
            Group(Line(100, 120, 140, 120)),
            rubbed,
            Line(190, 200, 270, 200, ToolKind.Eraser));
        group.Group!.Strokes[3].Brush.Size = 30;

        var whole = VisibleDrawingBounds.Of([new Frame { Strokes = [group] }], null, page);
        var caught = VisibleDrawingBounds.Of([new Frame { Strokes = [group] }], s => ReferenceEquals(s, group), page);
        var missed = VisibleDrawingBounds.Of([new Frame { Strokes = [group] }], _ => false, page);

        output.WriteLine($"whole {whole}, caught {caught}");
        // Brush size 10: five either side of the points. The line the group's
        // own eraser took is not in the box.
        Assert.Equal(new SKRect(25, 45, 145, 125), whole);
        Assert.Equal(whole, caught);
        Assert.Null(missed);
    }

    [Fact]
    public void AGroupHoldingPixelsIsAsBigAsThePage()
    {
        var page = new SKRect(0, 0, 300, 300);
        var group = Group();
        group.Group!.PngBase64 = "AAAA";

        Assert.Equal(page, VisibleDrawingBounds.Of([new Frame { Strokes = [group] }], null, page));
    }

    [Fact]
    public void AClipAStrokeInsideAGroupAlreadyHadTravelsWithIt()
    {
        var inner = Line(20, 50, 80, 50);
        inner.ClipId = "inner-clip";
        var group = Group(inner);
        var frame = new Frame { Strokes = [group] };

        TransformErasures.TransformFrame(
            frame, MoveRight, 1, s => ReferenceEquals(s, group),
            split: _ => new TransformErasures.RegionClips("moved", "stayed"),
            mapClip: id => id + "-carried");

        Assert.Equal("inner-clip", frame.Strokes[0].Group!.Strokes[0].ClipId);
        Assert.Equal("inner-clip-carried", frame.Strokes[1].Group!.Strokes[0].ClipId);
    }

    [Fact]
    public void AWhollyErasedStrokeIsNeverCaughtByARegion()
    {
        // Rule three (StrokePicker): a line rubbed out along its whole length
        // is not on the canvas, so no region can mean it. Its raw points sit
        // inside the mask, which is exactly how the old filter lifted it out
        // from under its eraser and made it visible again.
        var old = Line(30, 50, 70, 50);
        var erasure = Line(-40, 50, 110, 50, ToolKind.Eraser);  // reaches past the mask, covers all of `old`
        var moving = TransformErasures.MovingWithin(
            [old, erasure], MaskColumn(100, 100, 20, 80), 100, 100);

        output.WriteLine($"moving: [{string.Join(", ", moving)}]");
        // The erased line is the subject, and it stays put whatever else does.
        Assert.DoesNotContain(0, moving);
    }

    /// <summary>
    /// The erasure beside it <em>is</em> caught now, and that is the change
    /// B319 made rather than a hole in the rule above.
    /// </summary>
    /// <remarks>
    /// Under the majority filter this erasure stayed — its midpoint is inside
    /// the mask and its two ends are not. Under "any point inside" it is
    /// caught, and being caught no longer means being carried off whole: it
    /// straddles the boundary, so <see cref="TransformErasures.CrossesRegion"/>
    /// says so and the commit splits it, leaving the outside part exactly
    /// where it was to keep holding down the ink it rubbed out there. Nothing
    /// the region does not cover moves, which is the promise the old majority
    /// rule was standing in for.
    /// </remarks>
    [Fact]
    public void AnErasureAcrossTheBoundaryIsCaughtAndReportedAsCrossing()
    {
        var old = Line(30, 50, 70, 50);
        var erasure = Line(-40, 50, 110, 50, ToolKind.Eraser);
        var mask = MaskColumn(100, 100, 20, 80);

        var moving = TransformErasures.MovingWithin([old, erasure], mask, 100, 100);

        Assert.Contains(1, moving);
        Assert.True(
            new TransformErasures.RegionReading([old, erasure], mask, 100, 100).Crosses(erasure));
    }

    [Fact]
    public void APartiallyErasedStrokeIsJudgedByItsSurvivingInk()
    {
        // The dual failure: the artist boxes the visible end of a half-rubbed
        // line, and the erased points — which they cannot see — pull the raw
        // majority outside, so the line they plainly selected refuses to move.
        var line = Line(10, 50, 90, 50);
        line.Points = [new(10, 50, 1), new(30, 50, 1), new(50, 50, 1), new(70, 50, 1), new(90, 50, 1)];
        var erasure = Line(0, 50, 52, 50, ToolKind.Eraser);   // rubs out the left three points
        var mask = MaskColumn(100, 100, 60, 99);              // the surviving end only

        var moving = TransformErasures.MovingWithin([line, erasure], mask, 100, 100);

        // Raw majority is 2 of 5 inside — the old filter said "stays".
        Assert.Contains(0, moving);
    }

    [Fact]
    public void AnErasureStillTravelsByWhereItSits()
    {
        // Erasures keep the raw test: they have no surviving ink to judge, and
        // they must travel with the strokes they carve (the stay-copy machinery
        // above decides what they leave behind). Pinned so nobody later applies
        // the survivors rule to them and quietly un-carves every moved shape.
        var ink = Line(30, 50, 70, 50);
        var erasure = Line(40, 30, 60, 70, ToolKind.Eraser);
        var moving = TransformErasures.MovingWithin(
            [ink, erasure], MaskColumn(100, 100, 20, 80), 100, 100);

        Assert.Equal([0, 1], moving);
    }

    /// <summary>
    /// B432: a region strictly inside a fill reaches it. A fill's points are
    /// its outline, so a walk along them never enters a region drawn inside the
    /// colour — and the region plainly holds colour.
    /// </summary>
    [Fact]
    public void ARegionInsideAFillReachesIt()
    {
        var fill = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#e0b080",
            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            Points = [new(10, 10, 1), new(90, 10, 1), new(90, 90, 1), new(10, 90, 1)],
        };
        const int w = 100, h = 100;
        var mask = new bool[w * h];
        for (var y = 40; y < 60; y++) for (var x = 40; x < 60; x++) mask[(y * w) + x] = true;

        var moving = TransformErasures.MovingWithin([fill], mask, w, h);
        var reading = new TransformErasures.RegionReading([fill], mask, w, h);
        output.WriteLine($"moving: {moving.Count}; crosses: {reading.Crosses(fill)}");
        Assert.Single(moving);
        // Colour inside the region and colour outside it: cut at the edge (Q233).
        Assert.True(reading.Crosses(fill));
    }

    /// <summary>
    /// The half of the rule that keeps the one above honest: a region beside a
    /// line, close enough that a generous reading would take it, does not reach
    /// a line whose ink stops short of it.
    /// </summary>
    [Fact]
    public void ARegionClearOfTheInkDoesNotReachIt()
    {
        var line = Line(10, 50, 90, 50);   // size 10: ink from y 45 to 55
        const int w = 100, h = 100;
        var mask = new bool[w * h];
        for (var y = 60; y < 80; y++) for (var x = 10; x < 90; x++) mask[(y * w) + x] = true;

        Assert.Empty(TransformErasures.MovingWithin([line], mask, w, h));
    }

    /// <summary>
    /// A line whose ink overlaps the region while its centre stays outside it
    /// is reached, and cut: the overlapping band moves, the rest stays.
    /// </summary>
    [Fact]
    public void ARegionOverlappingALinesInkButNotItsCentreReachesIt()
    {
        var line = Line(10, 50, 90, 50);   // ink from y 45 to 55, centre at 50
        const int w = 100, h = 100;
        var mask = new bool[w * h];
        for (var y = 52; y < 80; y++) for (var x = 10; x < 90; x++) mask[(y * w) + x] = true;

        var reading = new TransformErasures.RegionReading([line], mask, w, h);
        Assert.Single(TransformErasures.MovingWithin([line], mask, w, h));
        Assert.True(reading.Crosses(line));
    }

    /// <summary>
    /// A light pencil line is ink, not fringe: the bar for counting a pixel
    /// scales with the stroke's opacity, or a 15% line overlapping a region
    /// beside its centre would never be found.
    /// </summary>
    [Fact]
    public void AFaintLineOverlappingTheRegionIsStillFound()
    {
        var line = Line(10, 50, 90, 50);
        line.Brush.Opacity = 0.15;
        const int w = 100, h = 100;
        var mask = new bool[w * h];
        for (var y = 52; y < 80; y++) for (var x = 10; x < 90; x++) mask[(y * w) + x] = true;

        Assert.Single(TransformErasures.MovingWithin([line], mask, w, h));
    }
}
