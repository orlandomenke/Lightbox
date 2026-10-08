using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Paper grown to the left or upward, and what an export of it shows.
/// </summary>
/// <remarks>
/// <para>
/// Growing the paper leaves every stroke coordinate alone and moves the
/// document rectangle instead: <c>Scene.OriginX</c> goes negative, and the
/// paper is <c>[Left, Right) × [Top, Bottom)</c>. A drawing that sat at the
/// left edge now has the new paper to its left — so in a picture of the
/// document it has moved right by exactly what was added.
/// </para>
/// <para>
/// Every one of these passes on a document that was never grown, whichever way
/// the code is, which is why nothing saw it: the stroke's coordinates and its
/// place in the picture are the same number until an origin goes non-zero.
/// </para>
/// </remarks>
[Collection("Registries")]
public class GrownPaperExportTests(Xunit.ITestOutputHelper output)
{
    private const int Size = 64;
    private const int GrewX = 24, GrewY = 10;

    /// <summary>An opaque red 8×8 square whose corner is at stroke (4, 6).</summary>
    private static Doc Drawing()
    {
        var doc = DocumentFactory.CreateDoc(Size, Size, fps: 12);
        doc.Scene.TransparentBackground = true;
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#ff0000",
            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            Points = [new(4, 6, 1), new(12, 6, 1), new(12, 14, 1), new(4, 14, 1)],
        });
        return doc;
    }

    /// <summary>Where the square's top-left corner is in a rendered picture, or null.</summary>
    private static (int X, int Y)? CornerOf(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 127) return (x, y);
            }
        }
        return null;
    }

    private static SKBitmap Export(Doc doc)
    {
        using var cache = new FrameBitmapCache();
        using var image = SequenceExporter.RenderFrame(doc, cache, 0);
        return SKBitmap.FromImage(image);
    }

    [Fact]
    public void BeforeThePaperIsGrownTheDrawingIsWhereItsCoordinatesSay()
    {
        using var picture = Export(Drawing());

        Assert.Equal((4, 6), CornerOf(picture));
    }

    [Fact]
    public void PaperAddedOnTheLeftAndTopMovesTheDrawingRightAndDownInTheExport()
    {
        var doc = Drawing();
        // Anchored bottom-right: the new paper goes on the left and on top.
        Assert.True(CanvasResize.Apply(doc.Scene, Size + GrewX, Size + GrewY, ResizeAnchor.BottomRight));
        output.WriteLine(
            $"paper is now {doc.Scene.Width}x{doc.Scene.Height} with its corner at "
            + $"({doc.Scene.Left}, {doc.Scene.Top})");
        Assert.Equal((-GrewX, -GrewY), (doc.Scene.Left, doc.Scene.Top));

        using var picture = Export(doc);

        var corner = CornerOf(picture);
        output.WriteLine($"the square's corner in the export: {corner}; expected ({4 + GrewX}, {6 + GrewY})");
        Assert.Equal((Size + GrewX, Size + GrewY), (picture.Width, picture.Height));
        Assert.Equal((4 + GrewX, 6 + GrewY), corner);
    }

    [Fact]
    public void ADrawingInTheNewPaperIsInTheExport()
    {
        // The sharper half: a mark made in the margin after growing has
        // negative coordinates, and a render that starts at zero has nowhere
        // to put it at all.
        var doc = Drawing();
        CanvasResize.Apply(doc.Scene, Size + GrewX, Size + GrewY, ResizeAnchor.BottomRight);
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Clear();
        doc.Scene.Layers[^1].Cels[0].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#0000ff",
            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
            Points = [new(-20, -8, 1), new(-12, -8, 1), new(-12, -2, 1), new(-20, -2, 1)],
        });

        using var picture = Export(doc);

        var corner = CornerOf(picture);
        output.WriteLine($"a square at stroke (-20, -8) is in the export at: {corner?.ToString() ?? "nowhere"}");
        Assert.Equal((-20 + GrewX, -8 + GrewY), corner);
    }
}
