using System.Security.Cryptography;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Multiplane parallax, stage 1 of docs/DESIGN-3d-space.md: a layer's depth
/// attenuates its response to camera moves, and does nothing anywhere else.
/// </summary>
/// <remarks>
/// The two load-bearing tests are the negatives — a document without a camera
/// ignores depth entirely (the asset target must never pay for the shot
/// machinery), and a camera sitting at home renders a deep layer exactly where
/// a flat one sits. The parallax itself is measured, not eyeballed: the red
/// blob rides the picture plane, the blue one a plane at depth 1000 (factor
/// 0.5), and the pan shift of each is read off the exported pixels.
/// </remarks>
public class MultiplaneParallaxTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-parallax-" + Guid.NewGuid().ToString("N"));

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

    /// <summary>
    /// Two drawing planes over the paper: red on the picture plane (rows
    /// 40–90), blue on a plane at depth 1000 (rows 110–160), both blobs
    /// centred on document x = 300 so any difference in where they land is
    /// the parallax and nothing else.
    /// </summary>
    private static Doc TwoPlaneScene()
    {
        var doc = DocumentFactory.CreateDoc(800, 200, 12);
        doc.Scene.FrameCount = 4;
        var front = doc.Scene.Layers.First(l => !l.IsBackground);
        front.Name = "Front";

        var back = new Layer { Name = "Back", Depth = 1000 };
        back.Cels.Add(new Cel { Frame = new Frame() });
        doc.Scene.Layers.Insert(doc.Scene.Layers.IndexOf(front), back);

        static Stroke Blob(double x, double top, double bottom, string colour) => new()
        {
            Tool = ToolKind.Fill,
            Color = colour,
            Points =
            [
                new StrokePoint(x - 40, top, 1), new StrokePoint(x + 40, top, 1),
                new StrokePoint(x + 40, bottom, 1), new StrokePoint(x - 40, bottom, 1),
            ],
            Brush = new BrushSettings { Opacity = 1, AntiAlias = false },
        };

        front.Cels[0].Frame!.Strokes.Add(Blob(300, 40, 90, "#ff0000"));
        back.Cels[0].Frame!.Strokes.Add(Blob(300, 110, 160, "#0000ff"));
        return doc;
    }

    private static string Hash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Mean x of the pixels a predicate accepts, or -1 when none do.</summary>
    private static double CentroidX(string path, Func<SKColor, bool> keep)
    {
        using var bmp = SKBitmap.Decode(path);
        double sum = 0;
        var count = 0;
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            if (!keep(bmp.GetPixel(x, y))) continue;
            sum += x;
            count++;
        }
        return count == 0 ? -1 : sum / count;
    }

    /// <summary>Width in columns of the pixels a predicate accepts, 0 when none.</summary>
    private static int SpanX(string path, Func<SKColor, bool> keep)
    {
        using var bmp = SKBitmap.Decode(path);
        int min = int.MaxValue, max = -1;
        for (var y = 0; y < bmp.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            if (!keep(bmp.GetPixel(x, y))) continue;
            if (x < min) min = x;
            if (x > max) max = x;
        }
        return max < min ? 0 : max - min + 1;
    }

    private static bool Red(SKColor c) => c.Red > 200 && c.Blue < 80 && c.Green < 80;

    private static bool Blue(SKColor c) => c.Blue > 200 && c.Red < 80 && c.Green < 80;

    [Fact]
    public void PanningTheCameraMovesADeepLayerLess()
    {
        var doc = TwoPlaneScene();
        doc.Scene.Camera = new Camera { OutputWidth = 400, OutputHeight = 200 };
        // Home is the scene centre (400, 100); frame 3 pans 60 to the right.
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(400, 100, 1, 0), Easing.Linear);
        CameraOps.SetKey(doc.Scene.Camera, 3, new CameraFraming(460, 100, 1, 0), Easing.Linear);

        var files = SequenceExporter.ExportPngSequence(doc, Sub("pan"));

        // At home both blobs sit where the flat mapping puts them: doc 300 →
        // output 100. Nothing separates the planes until the camera moves.
        Assert.Equal(100, CentroidX(files[0], Red), 1.0);
        Assert.Equal(100, CentroidX(files[0], Blue), 1.0);

        // After a 60 px pan the picture plane shifts the full 60; the plane at
        // depth 1000 (factor 0.5) shifts 30.
        Assert.Equal(40, CentroidX(files[3], Red), 1.0);
        Assert.Equal(70, CentroidX(files[3], Blue), 1.0);
    }

    [Fact]
    public void AZoomScalesADeepLayerLess()
    {
        var doc = TwoPlaneScene();
        doc.Scene.Camera = new Camera { OutputWidth = 400, OutputHeight = 200 };
        // Centred on the blobs (doc x 300), not on the scene, so the push
        // grows them in place instead of driving them out of frame.
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(300, 100, 1, 0), Easing.Linear);
        CameraOps.SetKey(doc.Scene.Camera, 3, new CameraFraming(300, 100, 2, 0), Easing.Linear);

        var files = SequenceExporter.ExportPngSequence(doc, Sub("zoom"));

        // The 80 px blob doubles on the picture plane; the deep plane zooms
        // geometrically by the factor — 2^0.5 ≈ 1.414 — so it grows less.
        Assert.Equal(80, SpanX(files[0], Red), 1.0);
        Assert.Equal(80, SpanX(files[0], Blue), 1.0);
        Assert.Equal(160, SpanX(files[3], Red), 2.0);
        Assert.Equal(80 * Math.Sqrt(2), SpanX(files[3], Blue), 2.0);
    }

    [Fact]
    public void ADocumentWithoutACameraIgnoresDepth()
    {
        // Depth without a camera does nothing — the rule that keeps the asset
        // target untouched by construction. Byte-for-byte, not "looks close".
        var doc = TwoPlaneScene();
        var with = SequenceExporter.ExportPngSequence(doc, Sub("depth"))
            .Select(Hash).ToList();

        doc.Scene.Layers.First(l => l.Name == "Back").Depth = null;
        var without = SequenceExporter.ExportPngSequence(doc, Sub("flat"))
            .Select(Hash).ToList();

        Assert.Equal(without, with);
    }

    [Fact]
    public void ExportThroughTheCameraCarriesParallax()
    {
        var doc = TwoPlaneScene();
        doc.Scene.Camera = new Camera { OutputWidth = 400, OutputHeight = 200 };
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(400, 100, 1, 0), Easing.Linear);
        CameraOps.SetKey(doc.Scene.Camera, 3, new CameraFraming(460, 100, 1, 0), Easing.Linear);

        var deep = SequenceExporter.ExportPngSequence(doc, Sub("deep"))
            .Select(Hash).ToList();

        doc.Scene.Layers.First(l => l.Name == "Back").Depth = null;
        var flat = SequenceExporter.ExportPngSequence(doc, Sub("noscroll"))
            .Select(Hash).ToList();

        // With the camera at home the planes coincide; once it moves, the
        // deliverable carries the parallax.
        Assert.Equal(flat[0], deep[0]);
        Assert.NotEqual(flat[3], deep[3]);
    }

    [Fact]
    public void ThePreviewPassListCarriesTheSamePlanes()
    {
        // The canvas preview (view through camera) rides ScenePassBuilder, not
        // the exporter — so the matrix has to be on the described pass too, and
        // only when the composite will actually be drawn under the camera.
        var doc = TwoPlaneScene();
        doc.Scene.Camera = new Camera { OutputWidth = 400, OutputHeight = 200 };
        CameraOps.SetKey(doc.Scene.Camera, 0, new CameraFraming(460, 100, 1, 0));

        using var cache = new FrameBitmapCache();
        var onion = new OnionSettings { Enabled = false };

        ScenePassBuilder.Result Build(bool throughCamera) => ScenePassBuilder.Build(
            doc.Scene,
            new ScenePassBuilder.State(
                0, null, false, false, false, onion, ThroughCamera: throughCamera),
            cache, new TileFallbackTally(), ScenePassBuilder.LiveEdit.None);

        var through = Build(throughCamera: true).Passes;
        // Paper, back, front: exactly one pass — the deep layer's — has a matrix.
        Assert.Equal(1, through.Count(p => p.Matrix is not null));

        var world = Build(throughCamera: false).Passes;
        // The world view shows every plane head-on (design rule 2).
        Assert.All(world, p => Assert.Null(p.Matrix));
    }
}
