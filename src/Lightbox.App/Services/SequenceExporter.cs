using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Renders every timeline frame to a numbered PNG (frame_0001.png, …) —
/// and lends its per-frame compose to <see cref="VideoExporter"/>, so a video
/// and a sequence of the same document are the same pixels by construction.
///
/// When the scene has a camera, the deliverable is what the camera saw: the
/// output size is the camera's and each frame composites through that frame's
/// framing. When it does not — the asset target, where the canvas IS the
/// output — this takes exactly the path it always did. Not an identity matrix:
/// a document that never asked for a camera must export byte-for-byte as it
/// did before the feature existed, and a test holds it to that.
/// </summary>
public static class SequenceExporter
{
    /// <summary>The exported size: the camera's when there is one, the canvas otherwise.</summary>
    public static (int Width, int Height) OutputSize(Scene scene) =>
        scene.Camera is { } camera ? (camera.OutputWidth, camera.OutputHeight) : (scene.Width, scene.Height);

    /// <summary>One timeline frame, composited exactly as export sees it.</summary>
    public static SKImage RenderFrame(Doc doc, FrameBitmapCache cache, int frameIndex)
    {
        var scene = doc.Scene;
        var camera = scene.Camera;
        var (outWidth, outHeight) = OutputSize(scene);

        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, frameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(
                cache.Get(frame, scene.Width, scene.Height, celIndex: frameIndex),
                null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
        }

        SKMatrix? transform = camera is null
            ? null
            : CameraTransform.Matrix(
                CameraOps.At(camera, frameIndex, scene.Width, scene.Height), outWidth, outHeight, 1.0);

        return SceneRenderer.Compose(
            outWidth, outHeight, passes, SceneRenderer.BackgroundOf(scene), transform);
    }

    public static List<string> ExportPngSequence(Doc doc, string directory)
    {
        Directory.CreateDirectory(directory);
        var scene = doc.Scene;
        var written = new List<string>();

        using var cache = new FrameBitmapCache();
        for (var i = 0; i < scene.FrameCount; i++)
        {
            using var image = RenderFrame(doc, cache, i);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new InvalidOperationException("PNG encode failed.");
            var path = Path.Combine(directory, $"frame_{i + 1:D4}.png");
            using (var file = File.Create(path))
            {
                data.SaveTo(file);
            }
            written.Add(path);
        }
        return written;
    }
}
