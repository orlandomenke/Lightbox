using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Renders every timeline frame to a numbered PNG (frame_0001.png, …) —
/// the stepping stone to GIF/MP4 export. Pure and headless-testable.
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
    public static List<string> ExportPngSequence(Doc doc, string directory)
    {
        Directory.CreateDirectory(directory);
        var scene = doc.Scene;
        var written = new List<string>();

        var camera = scene.Camera;
        var outWidth = camera?.OutputWidth ?? scene.Width;
        var outHeight = camera?.OutputHeight ?? scene.Height;

        using var cache = new FrameBitmapCache();
        for (var i = 0; i < scene.FrameCount; i++)
        {
            var passes = new List<RenderPass>();
            foreach (var layer in scene.Layers)
            {
                if (!scene.IsLayerVisible(layer)) continue;
                var frame = ExposureSheet.ExposedFrame(layer, i);
                if (frame is null) continue;
                passes.Add(new RenderPass(cache.Get(frame, scene.Width, scene.Height, celIndex: i), null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
            }

            SKMatrix? transform = camera is null
                ? null
                : CameraTransform.Matrix(
                    CameraOps.At(camera, i, scene.Width, scene.Height), outWidth, outHeight, 1.0);

            using var image = SceneRenderer.Compose(
                outWidth, outHeight, passes, SceneRenderer.BackgroundOf(scene), transform);
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
