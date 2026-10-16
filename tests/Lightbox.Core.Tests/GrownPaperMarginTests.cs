using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// What fills the new margin when the paper grows (Q283).
/// </summary>
/// <remarks>
/// <para>
/// <b>The owner's rule, which is Photoshop's: a locked Background layer is
/// filled, everything else is left transparent.</b> The fill is a new
/// <see cref="ToolKind.Fill"/> stroke inserted beneath the drawing's existing
/// marks and covering the whole new paper — never an existing stroke stretched
/// to fit, because <c>Hash01</c> seeds every dab dynamic from the bits of a
/// position and rewriting a coordinate re-rolls the mark.
/// </para>
/// <para>
/// <b>Every drawing on the layer gets one, and no empty cel does.</b> The
/// Background usually holds its paper on cel 0 and nothing after it; a cel
/// with no drawing of its own shows nothing, and giving it paper would make
/// a frame the artist left bare suddenly opaque. A drawing exposed on several
/// cels is one drawing and gets one fill.
/// </para>
/// </remarks>
public class GrownPaperMarginTests
{
    private const string Grey = "#808080";

    /// <summary>The owner's document: a locked grey Background on cel 0 only, empty cels after it.</summary>
    private static Doc JumpDoc(bool locked = true, int cels = 3)
    {
        var doc = DocumentFactory.CreateDoc(1920, 1080, 12, Grey);
        var paper = doc.Scene.Layers[0];
        Assert.True(paper.IsBackground);
        paper.Locked = locked;
        for (var i = 1; i < cels; i++) paper.Cels.Add(new Cel());
        doc.Scene.FrameCount = cels;
        return doc;
    }

    private static Frame PaperDrawing(Doc doc) => doc.Scene.Layers[0].Cels[0].Frame!;

    private static List<(double X, double Y)> Corners(Stroke s) =>
        s.Points.Select(p => (p.X, p.Y)).ToList();

    private static (double L, double T, double R, double B) Box(IEnumerable<StrokePoint> pts) =>
        (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));

    [Fact]
    public void GrowingTheCanvasFillsTheMarginOfALockedBackground()
    {
        var doc = JumpDoc();
        var oldPaper = PaperDrawing(doc).Strokes[0];
        var oldCorners = Corners(oldPaper);

        Assert.True(CanvasResize.Apply(doc.Scene, 2120, 1180, ResizeAnchor.Center));

        var strokes = PaperDrawing(doc).Strokes;
        Assert.Equal(2, strokes.Count);
        var fill = strokes[0];
        Assert.Equal(ToolKind.Fill, fill.Tool);
        Assert.Equal(Grey, fill.Color);
        // The whole new paper, in stroke coordinates: the origin went to
        // (-100, -50) and nothing was drawn there before.
        var s = doc.Scene;
        Assert.Equal((-100, -50, 2020, 1130), (s.Left, s.Top, s.Right, s.Bottom));
        var xs = fill.Points.Select(p => p.X).ToList();
        var ys = fill.Points.Select(p => p.Y).ToList();
        Assert.Equal((double)s.Left, xs.Min());
        Assert.Equal((double)s.Right, xs.Max());
        Assert.Equal((double)s.Top, ys.Min());
        Assert.Equal((double)s.Bottom, ys.Max());
        // The margin only: the old paper is a hole, so nothing already there
        // changes, whatever the drawing had or lacked in it.
        var hole = Assert.Single(fill.Holes!);
        Assert.Equal((0.0, 0.0, 1920.0, 1080.0), Box(hole));
        // Beneath, and the old paper untouched — same object, same corners.
        Assert.Same(oldPaper, strokes[1]);
        Assert.Equal(oldCorners, Corners(strokes[1]));
    }

    [Fact]
    public void AnUnlockedBackgroundIsAnOrdinaryLayerAndItsMarginStaysTransparent()
    {
        var doc = JumpDoc(locked: false);

        Assert.True(CanvasResize.Apply(doc.Scene, 2120, 1180, ResizeAnchor.Center));

        Assert.Single(PaperDrawing(doc).Strokes);
    }

    [Fact]
    public void OnlyTheBackgroundIsFilledNeverAnotherLockedLayer()
    {
        var doc = JumpDoc();
        var ink = doc.Scene.Layers[1];
        ink.Locked = true;
        ink.Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#ff0000",
            Points = [new(0, 0, 1), new(10, 0, 1), new(10, 10, 1), new(0, 10, 1)],
        });

        Assert.True(CanvasResize.Apply(doc.Scene, 2120, 1180, ResizeAnchor.Center));

        Assert.Single(ink.Cels[0].Frame!.Strokes);
    }

    [Fact]
    public void ACropAddsNothing()
    {
        var doc = JumpDoc();

        Assert.True(CanvasResize.Apply(doc.Scene, 1000, 600, ResizeAnchor.Center));

        Assert.Single(PaperDrawing(doc).Strokes);
    }

    [Fact]
    public void GrowingOneWayWhileCroppingTheOtherStillFillsTheNewStrip()
    {
        // Wider and shorter: there is new paper on the right even though the
        // height went down, and the fill covers the paper as it now is.
        var doc = JumpDoc();

        Assert.True(CanvasResize.Apply(doc.Scene, 2400, 800, ResizeAnchor.TopLeft));

        var fill = PaperDrawing(doc).Strokes[0];
        Assert.Equal(2, PaperDrawing(doc).Strokes.Count);
        Assert.Equal(2400.0, fill.Points.Max(p => p.X));
        Assert.Equal(800.0, fill.Points.Max(p => p.Y));
        // The hole is the old paper cut to the new: left whole, it would
        // fill the strip below the paper by even-odd, out of sight until a
        // later grow brought it back.
        Assert.Equal((0.0, 0.0, 1920.0, 800.0), Box(Assert.Single(fill.Holes!)));
    }

    [Fact]
    public void EmptyCelsGetNoPaperAndAHeldDrawingGetsOneFill()
    {
        var doc = JumpDoc(cels: 4);
        var paper = doc.Scene.Layers[0];
        // Cel 2 exposes the same drawing again; cel 3 has a drawing of its own
        // with no fill in it at all.
        paper.Cels[2].Frame = paper.Cels[0].Frame;
        paper.Cels[3].Frame = new Frame();

        Assert.True(CanvasResize.Apply(doc.Scene, 2000, 1100, ResizeAnchor.TopLeft));

        Assert.Equal(2, paper.Cels[0].Frame!.Strokes.Count);
        Assert.Null(paper.Cels[1].Frame);
        Assert.Same(paper.Cels[0].Frame, paper.Cels[2].Frame);
        // No paper fill to copy, so the scene's paper colour.
        var own = Assert.Single(paper.Cels[3].Frame!.Strokes);
        Assert.Equal(ToolKind.Fill, own.Tool);
        Assert.Equal(doc.Scene.BackgroundColor, own.Color);
    }

    [Fact]
    public void ABucketFillThatIsNotThePaperDoesNotColourTheMargin()
    {
        // The paper fill erased away and a small red fill left lowest: the
        // first fill in the list is not the paper, so the scene colour is.
        var doc = JumpDoc();
        PaperDrawing(doc).Strokes[0] = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#ff0000",
            Brush = new BrushSettings { Opacity = 0.5, AntiAlias = false },
            Points = [new(10, 10, 1), new(50, 10, 1), new(50, 50, 1), new(10, 50, 1)],
        };

        Assert.True(CanvasResize.Apply(doc.Scene, 2000, 1100, ResizeAnchor.TopLeft));

        var fill = PaperDrawing(doc).Strokes[0];
        Assert.Equal(doc.Scene.BackgroundColor, fill.Color);
        Assert.Equal(1, fill.Brush.Opacity);
    }

    [Fact]
    public void TheFillFollowsThePaperSwatchSoARecolourStillReachesTheMargin()
    {
        var doc = JumpDoc();
        var oldPaper = PaperDrawing(doc).Strokes[0];
        oldPaper.SwatchId = DocumentFactory.WhiteSwatchId;

        Assert.True(CanvasResize.Apply(doc.Scene, 2000, 1100, ResizeAnchor.TopLeft));

        var strokes = PaperDrawing(doc).Strokes;
        Assert.Equal(2, strokes.Count);
        Assert.NotSame(oldPaper, strokes[0]);
        Assert.Equal(DocumentFactory.WhiteSwatchId, strokes[0].SwatchId);
    }

    [Fact]
    public void ADocumentThatNeverResizedSerializesExactlyAsBefore()
    {
        var doc = JumpDoc();
        var before = DocJson.Serialize(doc);

        // A dialog confirmed on the size it already was is not a resize.
        Assert.False(CanvasResize.Apply(doc.Scene, 1920, 1080, ResizeAnchor.Center));

        Assert.Equal(before, DocJson.Serialize(doc));
    }
}
