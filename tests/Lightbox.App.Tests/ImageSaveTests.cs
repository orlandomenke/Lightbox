using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Saving the drawing as an ordinary picture: PNG, JPEG, WebP.
/// </summary>
/// <remarks>
/// The point of interest is not that a file appears — it is that the bytes are
/// the format they claim to be, that transparency survives where the format has
/// somewhere to put it, and that where it does not the artist is told rather than
/// finding out from whoever they sent the file to.
/// </remarks>
public class ImageSaveTests(Xunit.ITestOutputHelper output) : IDisposable
{
    private readonly string _dir =
        Directory.CreateTempSubdirectory("lightbox-image-save").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Path(string name) => System.IO.Path.Combine(_dir, name);

    /// <summary>A document with one opaque red square over transparent paper.</summary>
    private static Doc Painted(int width = 8, int height = 8, int frames = 1)
    {
        var doc = DocumentFactory.CreateDoc(width, height, fps: 12);
        doc.Scene.TransparentBackground = true;
        doc.Scene.FrameCount = frames;
        var layer = doc.Scene.Layers[^1];
        layer.Cels.Clear();
        for (var i = 0; i < frames; i++)
        {
            layer.Cels.Add(new Cel
            {
                Frame = new Frame
                {
                    Strokes =
                    [
                        new Stroke
                        {
                            Tool = ToolKind.Fill,
                            Color = "#ff0000",
                            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
                            Points =
                            [
                                new StrokePoint(0, 0, 1),
                                new StrokePoint(width / 2.0, 0, 1),
                                new StrokePoint(width / 2.0, height / 2.0, 1),
                                new StrokePoint(0, height / 2.0, 1),
                            ],
                        },
                    ],
                },
            });
        }
        return doc;
    }

    // ---- the file is the format it claims ------------------------------------

    [Theory]
    [InlineData(ImageSaveFormat.Png, "out.png")]
    [InlineData(ImageSaveFormat.Jpeg, "out.jpg")]
    [InlineData(ImageSaveFormat.Webp, "out.webp")]
    public void EveryFormatWritesAFileSkiaCanReadBack(ImageSaveFormat format, string name)
    {
        var path = Path(name);

        var result = SaveAsImage.Write(Painted(), path, new ImageSaveOptions(format));

        Assert.Equal([path], result.Paths);
        Assert.True(File.Exists(path));
        using var codec = SKCodec.Create(path);
        Assert.NotNull(codec);
        output.WriteLine($"{format} → {codec!.EncodedFormat}, {new FileInfo(path).Length} bytes");
        Assert.Equal(format switch
        {
            ImageSaveFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            ImageSaveFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        }, codec.EncodedFormat);
    }

    [Fact]
    public void TheImageIsTheDocumentsSizeAndCarriesTheDrawing()
    {
        var path = Path("size.png");

        SaveAsImage.Write(Painted(12, 10), path);

        using var bitmap = SKBitmap.Decode(path);
        Assert.Equal(12, bitmap.Width);
        Assert.Equal(10, bitmap.Height);
        // The fill covers the top-left quadrant.
        Assert.Equal(255, bitmap.GetPixel(1, 1).Red);
        Assert.Equal(0, bitmap.GetPixel(1, 1).Green);
    }

    [Fact]
    public void ScaleRendersBiggerWithoutTouchingTheGeometry()
    {
        // Invariant 7: output scale is a surface scale. This only checks the size
        // it promises; OutputScaleTests holds the geometry half.
        var path = Path("big.png");

        SaveAsImage.Write(Painted(8, 8), path, new ImageSaveOptions(Scale: 2.0));

        using var bitmap = SKBitmap.Decode(path);
        Assert.Equal(16, bitmap.Width);
        Assert.Equal(16, bitmap.Height);
    }

    // ---- transparency ---------------------------------------------------------

    [Fact]
    public void PngKeepsTransparencyAndSaysNothing()
    {
        var path = Path("alpha.png");

        var result = SaveAsImage.Write(Painted(), path, new ImageSaveOptions(ImageSaveFormat.Png));

        using var bitmap = SKBitmap.Decode(path);
        Assert.Equal(0, bitmap.GetPixel(7, 7).Alpha);
        Assert.False(result.LostTransparency);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void WebpKeepsTransparencyToo()
    {
        // The reason WebP is in the list at all: lossy *and* alpha.
        var path = Path("alpha.webp");

        var result = SaveAsImage.Write(Painted(), path, new ImageSaveOptions(ImageSaveFormat.Webp));

        using var bitmap = SKBitmap.Decode(path);
        Assert.Equal(0, bitmap.GetPixel(7, 7).Alpha);
        Assert.False(result.LostTransparency);
    }

    [Fact]
    public void JpegFillsTheTransparencyAndWarnsThatItDid()
    {
        var path = Path("flat.jpg");

        var result = SaveAsImage.Write(Painted(), path, new ImageSaveOptions(ImageSaveFormat.Jpeg));

        using var bitmap = SKBitmap.Decode(path);
        var corner = bitmap.GetPixel(7, 7);
        output.WriteLine($"empty corner became ({corner.Red},{corner.Green},{corner.Blue},{corner.Alpha})");
        Assert.Equal(255, corner.Alpha);
        // White by default, not black: a drawing that fell out onto black is the
        // thing the matte exists to prevent.
        Assert.InRange(corner.Red, 250, 255);
        Assert.InRange(corner.Green, 250, 255);
        Assert.InRange(corner.Blue, 250, 255);
        Assert.True(result.LostTransparency);
        Assert.Contains("no transparency", result.Warning!);
    }

    [Fact]
    public void TheMatteColourIsHonoured()
    {
        var path = Path("green-matte.jpg");

        SaveAsImage.Write(Painted(), path, new ImageSaveOptions(ImageSaveFormat.Jpeg, Matte: "#00ff00"));

        using var bitmap = SKBitmap.Decode(path);
        var corner = bitmap.GetPixel(7, 7);
        Assert.InRange(corner.Green, 250, 255);
        Assert.InRange(corner.Red, 0, 5);
    }

    [Fact]
    public void AFullyPaintedCanvasSavedAsJpegWarnsAboutNothing()
    {
        // The warning is measured from the pixels, not guessed from the scene, so
        // a document with nothing transparent in it stays quiet.
        var doc = DocumentFactory.CreateDoc(6, 6, paperColor: "#202020");
        var path = Path("opaque.jpg");

        var result = SaveAsImage.Write(doc, path, new ImageSaveOptions(ImageSaveFormat.Jpeg));

        Assert.False(result.LostTransparency);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void QualityChangesAJpegAndIsIgnoredByPng()
    {
        var low = Path("low.jpg");
        var high = Path("high.jpg");
        var doc = Painted(64, 64);

        SaveAsImage.Write(doc, low, new ImageSaveOptions(ImageSaveFormat.Jpeg, Quality: 5));
        SaveAsImage.Write(doc, high, new ImageSaveOptions(ImageSaveFormat.Jpeg, Quality: 100));

        var lowSize = new FileInfo(low).Length;
        var highSize = new FileInfo(high).Length;
        output.WriteLine($"quality 5 → {lowSize} bytes, quality 100 → {highSize} bytes");
        Assert.True(lowSize < highSize, $"expected {lowSize} < {highSize}");
        Assert.False(ImageSaveFormats.HasQuality(ImageSaveFormat.Png));
    }

    // ---- every frame ----------------------------------------------------------

    [Fact]
    public void OneFrameByDefaultEvenOnASequence()
    {
        var path = Path("single.png");

        var result = SaveAsImage.Write(Painted(8, 8, frames: 5), path);

        Assert.Single(result.Paths);
        Assert.Equal(path, result.Paths[0]);
    }

    [Fact]
    public void AllFramesWritesNumberedFilesBesideTheChosenName()
    {
        var path = Path("walk.png");

        var result = SaveAsImage.Write(
            Painted(8, 8, frames: 3), path, new ImageSaveOptions(AllFrames: true));

        Assert.Equal(3, result.Paths.Count);
        Assert.All(result.Paths, p => Assert.True(File.Exists(p)));
        Assert.Equal(
            ["walk_0001.png", "walk_0002.png", "walk_0003.png"],
            result.Paths.Select(p => System.IO.Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public void AllFramesWorksForAFormatThePngSequenceExporterCannotWrite()
    {
        // The one real reason this overlaps Export at all.
        var path = Path("cycle.webp");

        var result = SaveAsImage.Write(
            Painted(8, 8, frames: 2), path,
            new ImageSaveOptions(ImageSaveFormat.Webp, AllFrames: true));

        Assert.Equal(2, result.Paths.Count);
        using var codec = SKCodec.Create(result.Paths[1]);
        Assert.Equal(SKEncodedImageFormat.Webp, codec!.EncodedFormat);
    }

    [Fact]
    public void NumberingKeepsTheExtensionAndPadsToFour()
    {
        Assert.Equal(
            System.IO.Path.Combine("a", "b_0042.jpg"),
            SaveAsImage.Numbered(System.IO.Path.Combine("a", "b.jpg"), 42));
    }

    [Fact]
    public void AChosenFrameIsTheOneWritten()
    {
        var doc = Painted(8, 8, frames: 3);
        // Make the last frame plainly different: nothing on it at all.
        doc.Scene.Layers[^1].Cels[2].Frame = new Frame();
        var first = Path("first.png");
        var last = Path("last.png");

        SaveAsImage.Write(doc, first, frameIndex: 0);
        SaveAsImage.Write(doc, last, frameIndex: 2);

        using var a = SKBitmap.Decode(first);
        using var b = SKBitmap.Decode(last);
        Assert.Equal(255, a.GetPixel(1, 1).Red);
        Assert.Equal(0, b.GetPixel(1, 1).Alpha);
    }

    [Fact]
    public void AFrameIndexPastTheEndIsClampedRatherThanThrowing()
    {
        var path = Path("clamped.png");

        var result = SaveAsImage.Write(Painted(8, 8, frames: 2), path, frameIndex: 99);

        Assert.Single(result.Paths);
        Assert.True(File.Exists(path));
    }

    // ---- what a save shares with an export ------------------------------------

    [Fact]
    public void ASavedPngIsTheSamePixelsAsThatFrameFromAnExportedSequence()
    {
        // The reason SaveAsImage renders through SequenceExporter rather than
        // compositing again: two paths would be free to drift, and the drift shows
        // up as "the export looks different from the save".
        var doc = Painted(16, 16, frames: 2);
        var saved = Path("saved.png");
        var exportDir = System.IO.Path.Combine(_dir, "sequence");

        SaveAsImage.Write(doc, saved, frameIndex: 1);
        var written = SequenceExporter.ExportPngSequence(doc, exportDir);

        var fromSave = File.ReadAllBytes(saved);
        var fromExport = File.ReadAllBytes(written[1]);
        Assert.Equal(fromExport.Length, fromSave.Length);
        Assert.Equal(fromExport, fromSave);
    }

    // ---- the format table -----------------------------------------------------

    [Theory]
    [InlineData("drawing.png", ImageSaveFormat.Png)]
    [InlineData("drawing.JPG", ImageSaveFormat.Jpeg)]
    [InlineData("drawing.jpeg", ImageSaveFormat.Jpeg)]
    [InlineData("drawing.webp", ImageSaveFormat.Webp)]
    public void TheExtensionDecidesTheFormat(string name, ImageSaveFormat expected)
    {
        Assert.Equal(expected, ImageSaveFormats.FromExtension(name));
    }

    [Theory]
    [InlineData("drawing.psd")]
    [InlineData("drawing.tif")]
    [InlineData("drawing.gif")]
    [InlineData("drawing")]
    public void AFormatLightboxCannotWriteResolvesToNothing(string name)
    {
        // Not a gap being papered over: Skia in this build has no encoder for
        // these, so a menu entry would write nothing at all.
        Assert.Null(ImageSaveFormats.FromExtension(name));
    }

    [Fact]
    public void EveryFormatInTheTableCanActuallyBeEncoded()
    {
        // The guard against the enum growing a member with no encoder behind it.
        foreach (var format in ImageSaveFormats.All)
        {
            var path = Path($"probe{ImageSaveFormats.Extension(format)}");
            var result = SaveAsImage.Write(Painted(4, 4), path, new ImageSaveOptions(format));
            Assert.True(new FileInfo(result.Paths[0]).Length > 0, $"{format} wrote nothing");
        }
    }
}
