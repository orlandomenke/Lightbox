using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// Changing the paper without changing the drawing.
/// </summary>
/// <remarks>
/// The whole operation is three fields on the scene, and the tests that matter
/// are the ones asserting what it did <em>not</em> touch: every dab dynamic is
/// seeded from the bits of a dab position through <c>Hash01</c>, so a resize
/// that moved a coordinate would hand back a document whose grain had changed
/// everywhere. That failure renders, saves and reloads perfectly — nothing
/// goes red, the drawing just comes back subtly different — which is why it is
/// pinned here rather than left to the render tests.
/// </remarks>
public class CanvasResizeTests
{
    private static Scene Scene960() => new() { Width = 960, Height = 540 };

    [Fact]
    public void GrowingToTheRightLeavesTheOriginAlone()
    {
        var scene = Scene960();

        Assert.True(CanvasResize.Apply(scene, 1200, 540, ResizeAnchor.TopLeft));

        Assert.Equal(1200, scene.Width);
        Assert.Equal(0, scene.Left);
        // Not merely zero — absent. A document that only ever grew rightward
        // has no reason to carry the key.
        Assert.Null(scene.OriginX);
        Assert.Null(scene.OriginY);
    }

    [Fact]
    public void GrowingToTheLeftMovesTheOriginRatherThanTheDrawing()
    {
        var scene = Scene960();

        Assert.True(CanvasResize.Apply(scene, 1200, 540, ResizeAnchor.TopRight));

        // 240 px of new paper, all of it on the left.
        Assert.Equal(1200, scene.Width);
        Assert.Equal(-240, scene.Left);
        Assert.Equal(0, scene.Top);
        // The old content still occupies 0..960, which is where it was drawn.
        Assert.Equal(960, scene.Right);
    }

    [Fact]
    public void ACentredGrowthSplitsTheNewPaperBothWays()
    {
        var scene = Scene960();

        Assert.True(CanvasResize.Apply(scene, 1160, 740, ResizeAnchor.Center));

        Assert.Equal(-100, scene.Left);
        Assert.Equal(-100, scene.Top);
        Assert.Equal(1060, scene.Right);
        Assert.Equal(640, scene.Bottom);
    }

    [Fact]
    public void GrowingAndCroppingByTheSameOddAmountReturnsToWhereItStarted()
    {
        // Integer division truncates towards zero, so a naive `grow / 2` puts
        // the origin back one pixel out — a silent drift in where the artwork
        // sits, visible only after several resizes. Floor2 is what stops it.
        var scene = Scene960();

        CanvasResize.Apply(scene, 963, 543, ResizeAnchor.Center);
        CanvasResize.Apply(scene, 960, 540, ResizeAnchor.Center);

        Assert.Equal(0, scene.Left);
        Assert.Equal(0, scene.Top);
        Assert.Equal(960, scene.Width);
        Assert.Equal(540, scene.Height);
    }

    [Fact]
    public void CroppingKeepsTheAnchoredCorner()
    {
        var scene = Scene960();

        Assert.True(CanvasResize.Apply(scene, 500, 300, ResizeAnchor.BottomRight));

        // The bottom-right corner is kept, so the crop comes off the top-left
        // and the origin walks forward into the drawing.
        Assert.Equal(460, scene.Left);
        Assert.Equal(240, scene.Top);
        Assert.Equal(960, scene.Right);
        Assert.Equal(540, scene.Bottom);
    }

    [Fact]
    public void NotOneCoordinateInTheDocumentMoves()
    {
        // The reason the origin exists at all. If this ever fails, the resize
        // has started rewriting geometry and every mark in the document will
        // re-roll its grain.
        var doc = new Doc();
        doc.Scene.Layers.Add(new Layer
        {
            Cels = [new Cel { Frame = new Frame
            {
                Strokes = [new Stroke
                {
                    Points = [new StrokePoint(10, 20, 1), new StrokePoint(30.5, 40.25, 0.5)],
                    Brush = new BrushSettings { Size = 12 },
                }],
            } }],
        });
        doc.Scene.Guides = [new Guide { X = 100, Y = 200 }];
        doc.ClipRegions["clip"] = new ClipRegion
        {
            Contours = [[new StrokePoint(1, 2, 1), new StrokePoint(3, 4, 1), new StrokePoint(5, 6, 1)]],
        };

        CanvasResize.Apply(doc.Scene, 2000, 1200, ResizeAnchor.BottomRight);

        var stroke = doc.Scene.Layers[0].Cels[0].Frame!.Strokes[0];
        Assert.Equal(10, stroke.Points[0].X);
        Assert.Equal(20, stroke.Points[0].Y);
        Assert.Equal(30.5, stroke.Points[1].X);
        Assert.Equal(40.25, stroke.Points[1].Y);
        Assert.Equal(12, stroke.Brush.Size);
        Assert.Equal(100, doc.Scene.Guides![0].X);
        Assert.Equal(1, doc.ClipRegions["clip"].Contours[0][0].X);
    }

    [Fact]
    public void ADocumentThatNeverResizedWritesNoOriginKeys()
    {
        // "Optional means absent, not disabled." A non-nullable int would have
        // written originX and originY onto every document ever saved.
        var json = DocJson.Serialize(new Doc());

        Assert.DoesNotContain("\"originX\"", json);
        Assert.DoesNotContain("\"originY\"", json);
        // And the derived accessors must not sneak the same numbers back in
        // under a second name, which is exactly what BlendOrNormal did.
        Assert.DoesNotContain("\"left\"", json);
        Assert.DoesNotContain("\"top\"", json);
        Assert.DoesNotContain("\"right\"", json);
        Assert.DoesNotContain("\"bottom\"", json);
    }

    [Fact]
    public void AResizedOriginSurvivesASaveAndReload()
    {
        var doc = new Doc();
        CanvasResize.Apply(doc.Scene, 1200, 700, ResizeAnchor.BottomRight);

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.Equal(-240, reloaded.Scene.Left);
        Assert.Equal(-160, reloaded.Scene.Top);
        Assert.Equal(1200, reloaded.Scene.Width);
        Assert.Equal(700, reloaded.Scene.Height);
    }

    [Fact]
    public void ResizingToTheSameSizeChangesNothingAndSaysSo()
    {
        // The caller uses the false to skip pushing an undo step, so a dialog
        // opened and confirmed without typing does not raise the dirty badge.
        var scene = Scene960();

        Assert.False(CanvasResize.Apply(scene, 960, 540, ResizeAnchor.Center));
        Assert.False(CanvasResize.Apply(scene, 0, 540, ResizeAnchor.Center));
        Assert.False(CanvasResize.Apply(scene, 960, -1, ResizeAnchor.Center));
        Assert.Equal(960, scene.Width);
    }

    // ---- CropTo: the paper put at a rectangle somebody else worked out ----------

    [Fact]
    public void CroppingToARectangleMovesTheOriginToIt()
    {
        var scene = Scene960();

        Assert.True(CanvasResize.CropTo(scene, 100, 60, 400, 300));

        Assert.Equal(400, scene.Width);
        Assert.Equal(300, scene.Height);
        Assert.Equal(100, scene.Left);
        Assert.Equal(60, scene.Top);
        Assert.Equal(500, scene.Right);
    }

    [Fact]
    public void CroppingBackToTheCornerDropsTheOriginKeyRatherThanWritingZero()
    {
        var scene = Scene960();
        Assert.True(CanvasResize.CropTo(scene, 100, 60, 400, 300));

        Assert.True(CanvasResize.CropTo(scene, 0, 0, 200, 200));

        // Absent, not zero: a document sitting at the corner has no reason to
        // carry the key, and a serialized "originX": 0 on every cropped-back
        // document is exactly what Scene.OriginX is nullable to avoid.
        Assert.Null(scene.OriginX);
        Assert.Null(scene.OriginY);
    }

    [Fact]
    public void CroppingToTheRectangleItAlreadyHasChangesNothingAndSaysSo()
    {
        var scene = Scene960();
        Assert.True(CanvasResize.CropTo(scene, 100, 60, 400, 300));

        // The whole rectangle, not just the size: a scene 400x300 at (100, 60)
        // asked for 400x300 at (100, 60) is a no-op, and the caller needs to
        // hear so rather than push an undo step nobody can tell did anything.
        Assert.False(CanvasResize.CropTo(scene, 100, 60, 400, 300));
        // Same size somewhere else is a real move, and must not read as a no-op.
        Assert.True(CanvasResize.CropTo(scene, 101, 60, 400, 300));
    }

    [Fact]
    public void ACropWithNoAreaIsRefused()
    {
        var scene = Scene960();

        Assert.False(CanvasResize.CropTo(scene, 10, 10, 0, 300));
        Assert.False(CanvasResize.CropTo(scene, 10, 10, 400, -5));

        // Untouched — a refused crop leaves the scene exactly as it was.
        Assert.Equal(960, scene.Width);
        Assert.Equal(540, scene.Height);
        Assert.Null(scene.OriginX);
    }

    [Fact]
    public void CroppingLeavesEveryMarkWhereItWasDrawn()
    {
        // Q128: this is the whole design. A crop moves the paper and nothing
        // else, so ink outside the new edge stays in the record — which is what
        // makes cropping and growing back exact inverses. A destructive crop
        // could not be, because Hash01 seeds every dab dynamic from the bits of
        // its position: a mark deleted and drawn again is a different mark.
        var doc = DocumentFactory.CreateDoc(960, 540, 1);
        var frame = (Frame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#2050b0",
            Points = [new StrokePoint(20, 30, 1), new StrokePoint(900, 500, 1)],
            Brush = new BrushSettings { Size = 8, Opacity = 1, Flow = 1 },
        });
        string Ink() => System.Text.Json.JsonSerializer.Serialize(
            doc.Scene.Layers[0].Cels[0].Frame!, DocJson.Compact);
        var before = Ink();

        // A crop that leaves both ends of that stroke outside the new paper.
        Assert.True(CanvasResize.CropTo(doc.Scene, 300, 200, 100, 100));

        Assert.Equal(before, Ink());

        // And growing back returns the document to exactly where it started.
        Assert.True(CanvasResize.CropTo(doc.Scene, 0, 0, 960, 540));
        Assert.Equal(960, doc.Scene.Width);
        Assert.Equal(0, doc.Scene.Left);
        Assert.Equal(before, Ink());
    }
}
