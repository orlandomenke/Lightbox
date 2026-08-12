using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Which stroke is under the cursor — the one primitive vector tooling needs
/// that the application has never had.
/// </summary>
/// <remarks>
/// Two rules carry more weight than the rest and are tested first.
/// <b>Topmost wins</b>, which reverses <see cref="StrokeIndex"/>'s ascending
/// contract on purpose — an artist clicks what they can see, and that is the
/// stroke painted last. And <b>ink beats erasers</b>, because an eraser's mark is
/// the absence of ink, so picking one where a line is plainly visible reads as
/// having selected nothing.
/// </remarks>
public class StrokePickerTests(ITestOutputHelper output)
{

    private static Stroke Line(
        double x0, double y0, double x1, double y1,
        double size = 10, ToolKind tool = ToolKind.Brush) => new()
        {
            Tool = tool,
            Color = "#000000",
            Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
            Brush = new BrushSettings { Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };

    private static Stroke Box(double x, double y, double w, double h) => new()
    {
        Tool = ToolKind.Fill,
        Color = "#ff0000",
        Points =
        [
            new StrokePoint(x, y, 1),
            new StrokePoint(x + w, y, 1),
            new StrokePoint(x + w, y + h, 1),
            new StrokePoint(x, y + h, 1),
        ],
        Brush = new BrushSettings { Size = 1, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static StrokeIndex IndexOf(IReadOnlyList<Stroke> strokes) => StrokeIndex.Of(strokes);

    // ---- the two rules -------------------------------------------------------

    /// <summary>
    /// Two lines crossing: the click belongs to the one painted later, because
    /// that is the one on top and the one the artist can see. Getting this
    /// backwards does not read as a wrong sort — it reads as the application
    /// selecting a different line from the one under the cursor.
    /// </summary>
    [Fact]
    public void TheStrokeOnTopIsThePickedOne()
    {
        // An X, crossing at (500, 500).
        List<Stroke> strokes = [Line(400, 400, 600, 600), Line(600, 400, 400, 600)];
        var hits = StrokePicker.At(strokes, IndexOf(strokes), 500, 500, tolerance: 2);

        output.WriteLine($"hits at the crossing: [{string.Join(", ", hits)}]");
        Assert.Equal(2, hits.Count);
        Assert.Equal(1, hits[0]); // the later stroke, not the earlier one
        Assert.Equal(0, hits[1]);
    }

    /// <summary>
    /// An eraser drawn across a visible line does not steal the click, even
    /// though it is later in the record and passes through the same point.
    /// </summary>
    [Fact]
    public void AnEraserDoesNotStealAClickFromInkUnderIt()
    {
        List<Stroke> strokes =
        [
            Line(400, 500, 600, 500),
            Line(500, 400, 500, 600, tool: ToolKind.Eraser),
        ];
        var hits = StrokePicker.At(strokes, IndexOf(strokes), 500, 500, tolerance: 2);

        output.WriteLine($"ink at 0, eraser at 1, hits: [{string.Join(", ", hits)}]");
        Assert.Equal(0, hits[0]);
    }

    /// <summary>
    /// But an eraser is still selectable, or a stray one could never be removed.
    /// </summary>
    [Fact]
    public void AnEraserWithNothingUnderItIsStillPicked()
    {
        List<Stroke> strokes = [Line(100, 100, 200, 200, tool: ToolKind.Eraser)];
        var hit = StrokePicker.TopmostAt(strokes, IndexOf(strokes), 150, 150, tolerance: 2);

        Assert.Equal(0, hit);
    }

    // ---- the tolerance -------------------------------------------------------

    /// <summary>
    /// A click outside the mark picks nothing. Without this the picker could
    /// "pass" every test above by returning everything the index offered it.
    /// </summary>
    [Fact]
    public void AClickBesideTheLinePicksNothing()
    {
        List<Stroke> strokes = [Line(400, 500, 600, 500, size: 10)];
        var index = IndexOf(strokes);

        // Half of a size-10 brush is 5, plus 1 of tolerance: 6 reaches, 20 does not.
        var near = StrokePicker.TopmostAt(strokes, index, 500, 505, tolerance: 1);
        var far = StrokePicker.TopmostAt(strokes, index, 500, 520, tolerance: 1);

        output.WriteLine($"5px away: {near?.ToString() ?? "nothing"}, 20px away: {far?.ToString() ?? "nothing"}");
        Assert.Equal(0, near);
        Assert.Null(far);
    }

    /// <summary>
    /// A wide brush is wide to click on too — the reach is the stroke's own
    /// half-width, not a fixed number.
    /// </summary>
    [Fact]
    public void AWideStrokeIsWideToHit()
    {
        List<Stroke> thin = [Line(400, 500, 600, 500, size: 4)];
        List<Stroke> wide = [Line(400, 500, 600, 500, size: 60)];

        var thinHit = StrokePicker.TopmostAt(thin, IndexOf(thin), 500, 520, tolerance: 1);
        var wideHit = StrokePicker.TopmostAt(wide, IndexOf(wide), 500, 520, tolerance: 1);

        output.WriteLine($"20px off centre — size 4: {thinHit?.ToString() ?? "miss"}, size 60: {wideHit?.ToString() ?? "miss"}");
        Assert.Null(thinHit);
        Assert.Equal(0, wideHit);
    }

    // ---- fills are areas -----------------------------------------------------

    /// <summary>
    /// A fill is picked by clicking anywhere inside it, not only on its outline —
    /// it is an area, and that is what the artist sees.
    /// </summary>
    [Fact]
    public void AFillIsPickedFromTheInside()
    {
        List<Stroke> strokes = [Box(200, 200, 400, 400)];
        var hit = StrokePicker.TopmostAt(strokes, IndexOf(strokes), 400, 400, tolerance: 1);

        Assert.Equal(0, hit);
    }

    /// <summary>
    /// And its hole is not part of it. Even-odd is the rule it was painted
    /// under, so a hit test that ignored holes would select a shape from a point
    /// where the shape is not.
    /// </summary>
    [Fact]
    public void AHoleInAFillIsNotPartOfIt()
    {
        var donut = Box(200, 200, 400, 400);
        donut.Holes = [[
            new StrokePoint(350, 350, 1),
            new StrokePoint(450, 350, 1),
            new StrokePoint(450, 450, 1),
            new StrokePoint(350, 450, 1),
        ]];

        List<Stroke> strokes = [donut];
        var index = IndexOf(strokes);
        var inRing = StrokePicker.TopmostAt(strokes, index, 250, 250, tolerance: 1);
        var inHole = StrokePicker.TopmostAt(strokes, index, 400, 400, tolerance: 1);

        output.WriteLine($"in the ring: {inRing?.ToString() ?? "nothing"}, in the hole: {inHole?.ToString() ?? "nothing"}");
        Assert.Equal(0, inRing);
        Assert.Null(inHole);
    }

    /// <summary>
    /// A gradient covers whatever it was drawn over, so treating it as an area
    /// would make it win every click on the layer. It is picked by its axis —
    /// the two points the artist dragged, which is also the gizmo already drawn
    /// for it.
    /// </summary>
    [Fact]
    public void AGradientIsPickedByItsAxisRatherThanItsCoverage()
    {
        var gradient = Line(400, 400, 600, 400, size: 1, tool: ToolKind.Gradient);
        gradient.GradientId = "g1";

        List<Stroke> strokes = [gradient];
        var index = IndexOf(strokes);
        var onAxis = StrokePicker.TopmostAt(strokes, index, 500, 400, tolerance: 3);
        var wellOff = StrokePicker.TopmostAt(strokes, index, 500, 460, tolerance: 3);

        output.WriteLine($"on the axis: {onAxis?.ToString() ?? "nothing"}, 60px off it: {wellOff?.ToString() ?? "nothing"}");
        Assert.Equal(0, onAxis);
        Assert.Null(wellOff);
    }

    // ---- the marquee ---------------------------------------------------------

    /// <summary>
    /// Touched, not enclosed — Illustrator's rule. Dragging a box across part of
    /// a limb grabs its lines; requiring containment would mean boxing the whole
    /// character every time.
    /// </summary>
    [Fact]
    public void AMarqueeCatchesWhatItTouchesRatherThanOnlyWhatItEncloses()
    {
        List<Stroke> strokes =
        [
            Line(100, 100, 900, 100),   // crosses the box, both ends outside
            Line(400, 400, 450, 450),   // wholly inside
            Line(100, 900, 200, 950),   // nowhere near
        ];

        var caught = StrokePicker.Within(
            strokes, IndexOf(strokes), SKRect.Create(300, 50, 300, 500));

        output.WriteLine($"caught: [{string.Join(", ", caught)}]");
        Assert.Equal([0, 1], caught);
    }

    /// <summary>
    /// A marquee returns record order rather than topmost-first: it is a set, not
    /// a choice, and a set that keeps paint order needs no second sort.
    /// </summary>
    [Fact]
    public void AMarqueeReturnsRecordOrder()
    {
        List<Stroke> strokes =
        [
            Line(400, 400, 450, 450),
            Line(410, 410, 460, 460),
            Line(420, 420, 470, 470),
        ];

        var caught = StrokePicker.Within(
            strokes, IndexOf(strokes), SKRect.Create(390, 390, 100, 100));

        Assert.Equal([0, 1, 2], caught);
    }

    /// <summary>
    /// A click is not a marquee. A zero-area drag must select nothing rather than
    /// everything, which is what an unguarded rectangle test would do.
    /// </summary>
    [Fact]
    public void AZeroAreaMarqueeCatchesNothing()
    {
        List<Stroke> strokes = [Line(400, 400, 450, 450)];
        var caught = StrokePicker.Within(strokes, IndexOf(strokes), SKRect.Create(420, 420, 0, 0));

        Assert.Empty(caught);
    }

    // ---- the seam with the index --------------------------------------------

    /// <summary>
    /// The picker must agree with the index it queries. A stroke whose bounds the
    /// index says reach the point, but whose geometry does not, is a near miss —
    /// and the index alone would report it as a hit, because bounds include
    /// scatter and bleed that the visible line does not.
    /// </summary>
    [Fact]
    public void BoundsAreNarrowedByGeometryRatherThanTrusted()
    {
        // An L: the corner's bounding box covers the inside of the elbow, where
        // there is no ink at all.
        var elbow = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [new StrokePoint(300, 300, 1), new StrokePoint(300, 600, 1), new StrokePoint(600, 600, 1)],
            Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };

        List<Stroke> strokes = [elbow];
        var index = IndexOf(strokes);
        var insideTheBox = new SKRectI(560, 320, 561, 321);

        var indexSays = index.Intersecting(insideTheBox).ToList();
        var pickerSays = StrokePicker.TopmostAt(strokes, index, 560, 320, tolerance: 1);

        output.WriteLine($"index offers [{string.Join(", ", indexSays)}], picker returns {pickerSays?.ToString() ?? "nothing"}");
        Assert.Single(indexSays);
        Assert.Null(pickerSays);
    }

    /// <summary>
    /// B134: a stroke lying entirely outside the document — at y=700 in a
    /// 960×540 world, exactly how the bug was found — is still under the
    /// cursor when the cursor is on it. Where a stroke <em>is</em> does not
    /// depend on where the paper ends; a stroke dragged past the edge, art beyond the
    /// nominal frame is the point.
    /// </summary>
    [Fact]
    public void AStrokeOutsideTheDocumentIsStillPickable()
    {
        List<Stroke> strokes = [Line(100, 700, 300, 700)];
        var index = IndexOf(strokes);

        var clicked = StrokePicker.TopmostAt(strokes, index, 200, 700, tolerance: 2);
        var caught = StrokePicker.Within(strokes, index, new SKRect(50, 650, 350, 750));

        output.WriteLine($"click returns {clicked?.ToString() ?? "nothing"}, marquee catches {caught.Count}");
        Assert.Equal(0, clicked);
        Assert.Equal([0], caught);
    }
}
