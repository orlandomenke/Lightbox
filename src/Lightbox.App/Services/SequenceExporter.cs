using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Renders every timeline frame to a numbered PNG (frame_0001.png, …) —
/// the stepping stone to GIF/MP4 export. Pure and headless-testable.
/// </summary>
public static class SequenceExporter
{
    public static List<string> ExportPngSequence(Doc doc, string directory)
    {
        Directory.CreateDirectory(directory);
        var scene = doc.Scene;
        var written = new List<string>();

        using var cache = new FrameBitmapCache();
        for (var i = 0; i < scene.FrameCount; i++)
        {
            var passes = new List<RenderPass>();
            foreach (var layer in scene.Layers)
            {
                if (!scene.IsLayerVisible(layer)) continue;
                var frame = ExposureSheet.ExposedFrame(layer, i);
                if (frame is null) continue;
                passes.Add(new RenderPass(cache.Get(frame, scene.Width, scene.Height), null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
            }

            using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SceneRenderer.BackgroundOf(scene));
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
