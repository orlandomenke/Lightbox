using System.Security.Cryptography;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Export is where the two output targets actually diverge. A shot exports
/// what the camera saw; an asset exports the canvas. The load-bearing test
/// here is the second one: adding a camera feature must not change a single
/// byte of what a sprite-sheet document produces.
/// </summary>
public class CameraExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-camera-export-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string Sub(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A wide scene with a distinct blob at each end, over four frames.</summary>
    private static Doc WideScene()
    {
        var doc = DocumentFactory.CreateDoc(800, 200, 12);
        doc.Scene.FrameCount = 4;
        var layer = doc.Scene.Layers.First(l => !l.IsBackground);

        Stroke Blob(double x, string colour) => new()
        {
            Tool = ToolKind.Fill,
            Color = colour,
            Points =
            [
                new StrokePoint(x - 60, 40, 1), new StrokePoint(x + 60, 40, 1),
                new StrokePoint(x + 60, 160, 1), new StrokePoint(x - 60, 160, 1),
            ],
            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
        };

        foreach (var cel in layer.Cels)
        {
            if (cel.Frame is Frame p)
            {
                p.Strokes.Add(Blob(100, "#ff0000"));
                p.Strokes.Add(Blob(700, "#0000ff"));
            }
        }
        return doc;
    }

    private static SKColor Centre(string path)
    {
        using var bmp = SKBitmap.Decode(path);
        return bmp.GetPixel(bmp.Width / 2, bmp.Height / 2);
    }

    private static (int W, int H) SizeOf(string path)
    {
        using var bmp = SKBitmap.Decode(path);
        return (bmp.Width, bmp.Height);
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public void WithoutACamera_TheExportIsByteForByteWhatItAlwaysWas()
    {
        // The test that protects the asset workflow from the shot workflow.
        // The reference is an export taken with Scene.Camera null — the exact
        // state every existing document is in — and the comparison is a
        // camera added and then removed again. Anything that leaked a matrix
        // concat, a resample or a rounding change into the no-camera path
        // fails here.
        var doc = WideScene();
        var before = SequenceExporter.ExportPngSequence(doc, Sub("before"))
            .Select(Hash).ToList();

        doc.Scene.Camera = new Camera { OutputWidth = 400, OutputHeight = 200 };
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(100, 100, 1, 0));
        SequenceExporter.ExportPngSequence(doc, Sub("with"));

        doc.Scene.Camera = null;
        var after = SequenceExporter.ExportPngSequence(doc, Sub("after"))
            .Select(Hash).ToList();

        Assert.Equal(before, after);
    }

    [Fact]
    public void WithoutACamera_TheOutputIsTheCanvas()
    {
        var doc = WideScene();
        var files = SequenceExporter.ExportPngSequence(doc, Sub("canvas"));
        Assert.Equal(4, files.Count);
        Assert.Equal((800, 200), SizeOf(files[0]));
    }

    [Fact]
    public void TheOutputSizeComesFromTheCamera()
    {
        var doc = WideScene();
        doc.Scene.Camera = new Camera { OutputWidth = 320, OutputHeight = 180 };
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(400, 100, 1, 0));

        var files = SequenceExporter.ExportPngSequence(doc, Sub("sized"));
        Assert.Equal((320, 180), SizeOf(files[0]));
    }

    [Fact]
    public void APanProducesADifferentFramingOnEachFrame()
    {
        var doc = WideScene();
        doc.Scene.Camera = new Camera { OutputWidth = 200, OutputHeight = 200 };
        // Frame 0 sits on the red blob, frame 3 on the blue one.
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(100, 100, 1, 0), Easing.Linear);
        CameraOps.SetKey(doc.Scene.Camera, 3, new CameraFraming(700, 100, 1, 0), Easing.Linear);

        var files = SequenceExporter.ExportPngSequence(doc, Sub("pan"));

        Assert.Equal(SKColors.Red, Centre(files[0]));
        Assert.Equal(SKColors.Blue, Centre(files[3]));
        // And the middle of the pan is over the empty stretch between them.
        Assert.Equal(SKColors.White, Centre(files[1]));
    }

    [Fact]
    public void AStaticCameraFramesTheSameThingOnEveryFrame()
    {
        var doc = WideScene();
        doc.Scene.Camera = new Camera { OutputWidth = 200, OutputHeight = 200 };
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(700, 100, 1, 0));

        var files = SequenceExporter.ExportPngSequence(doc, Sub("static"));
        var hashes = files.Select(Hash).Distinct().ToList();

        Assert.Single(hashes);
        Assert.Equal(SKColors.Blue, Centre(files[0]));
    }

    [Fact]
    public void ACameraWithNoKeysFramesTheSceneCentre()
    {
        // Adding a camera before authoring anything must not produce a black
        // frame or a crash — it frames the middle of the scene at 1:1.
        var doc = WideScene();
        doc.Scene.Camera = new Camera { OutputWidth = 200, OutputHeight = 200 };

        var files = SequenceExporter.ExportPngSequence(doc, Sub("nokeys"));
        Assert.Equal((200, 200), SizeOf(files[0]));
        // The scene centre (400, 100) is the empty stretch between the blobs.
        Assert.Equal(SKColors.White, Centre(files[0]));
    }

    [Fact]
    public void APushInEnlargesWhatTheFrameShows()
    {
        var doc = WideScene();
        doc.Scene.Camera = new Camera { OutputWidth = 200, OutputHeight = 200 };
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(100, 100, 1, 0), Easing.Linear);
        CameraOps.SetKey(doc.Scene.Camera, 3, new CameraFraming(100, 100, 4, 0), Easing.Linear);

        var files = SequenceExporter.ExportPngSequence(doc, Sub("push"));

        // The blob is 120 px wide. At 1x its edges are inside a 200 px frame,
        // so the frame corners show paper; at 4x it more than fills the frame.
        using var wide = SKBitmap.Decode(files[0]);
        using var close = SKBitmap.Decode(files[3]);
        Assert.Equal(SKColors.White, wide.GetPixel(5, 100));
        Assert.Equal(SKColors.Red, close.GetPixel(5, 100));
    }
}
