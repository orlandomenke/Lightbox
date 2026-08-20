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

    /// <summary>
    /// One timeline frame, composited exactly as export sees it.
    /// <paramref name="scale"/> renders bigger or smaller by scaling the
    /// surface, never the geometry (invariant 7): stroke coordinates are
    /// untouched, so a 2× render is the same mark at twice the size rather
    /// than a differently-seeded one.
    /// </summary>
    public static SKImage RenderFrame(
        Doc doc, FrameBitmapCache cache, int frameIndex,
        double scale = 1.0, (int Width, int Height)? outputSize = null)
    {
        var scene = doc.Scene;
        var camera = scene.Camera;
        var (outWidth, outHeight) = OutputSize(scene);

        // The framing is per-frame; a layer's parallax response to it is
        // per-layer, below. Null without a camera, which is also what makes
        // depth do nothing on an asset document — there is no framing for it
        // to respond to.
        var framing = camera is null
            ? (CameraFraming?)null
            : CameraOps.At(camera, frameIndex, scene.Width, scene.Height);
        var home = CameraFraming.Centred(scene.Width, scene.Height);

        var passes = new List<RenderPass>();
        var footageQueued = false;
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            // Production footage goes over the paper and under every drawing
            // (Q57) — the same slot the canvas gives it, so the export shows
            // exactly what the artist was looking at.
            if (!footageQueued && !layer.IsBackground)
            {
                passes.AddRange(ProductionPasses(scene, frameIndex));
                footageQueued = true;
            }
            var frame = ExposureSheet.ExposedFrame(layer, frameIndex);
            if (frame is null) continue;
            // Multiplane: a layer with a depth exports through its plane's
            // matrix — the same pass slot the canvas preview uses, so the
            // deliverable is what the artist was looking at.
            var parallax = framing is { } f
                ? ParallaxTransform.PassMatrix(layer.Depth, f, home, outWidth, outHeight)
                : null;
            passes.Add(new RenderPass(
                cache.Get(frame, scene.Width, scene.Height, celIndex: frameIndex),
                null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode),
                Matrix: parallax));
        }
        if (!footageQueued) passes.AddRange(ProductionPasses(scene, frameIndex));

        SKMatrix? transform = camera is null
            ? null
            : CameraTransform.Matrix(framing!.Value, outWidth, outHeight, scale);

        // Scale 1 with no explicit size takes exactly the arithmetic it always
        // did — a document exported at its own size must be byte-for-byte what
        // it was before these parameters existed. An explicit size wins over
        // the arithmetic so the encoder is never told one thing and piped
        // another: a codec's even-dimension rounding belongs in one place.
        var (width, height) = outputSize ?? (scale == 1.0
            ? (outWidth, outHeight)
            : (Math.Max(1, (int)Math.Round(outWidth * scale)), Math.Max(1, (int)Math.Round(outHeight * scale))));

        return SceneRenderer.Compose(
            width, height, passes, SceneRenderer.BackgroundOf(scene), transform, scale);
    }

    /// <summary>
    /// Small-production footage, beneath every drawing layer (Q57). A
    /// reference never reaches an exported pixel — that promise stands — but
    /// a strip the artist imported as production material composites into
    /// the deliverable exactly as it shows on the canvas, same matrix, same
    /// window, same opacity.
    /// </summary>
    private static List<RenderPass> ProductionPasses(Scene scene, int frameIndex)
    {
        var passes = new List<RenderPass>();
        if (scene.References is not { Count: > 0 } strips) return passes;

        foreach (var strip in strips)
        {
            if (!strip.RendersInExport || !strip.Visible || strip.Opacity <= 0) continue;
            if (strip.CellAt(frameIndex) is not { } cell) continue;
            if (ReferenceStripRegistry.Resolve(strip.Id) is not { } sheet) continue;

            var scale = (float)Math.Max(0.01, strip.Scale);
            var matrix = SKMatrix.CreateScaleTranslation(
                scale, scale,
                (float)(strip.OffsetX + cell.Dx),
                (float)(strip.OffsetY + cell.Dy));
            passes.Add(new RenderPass(
                sheet, null, strip.Opacity, SKBlendMode.SrcOver, null, matrix,
                SKRectI.Create(cell.X, cell.Y, cell.Width, cell.Height)));
        }
        return passes;
    }

    public static List<string> ExportPngSequence(Doc doc, string directory)
    {
        Directory.CreateDirectory(directory);
        var scene = doc.Scene;
        var written = new List<string>();

        using var cache = new FrameBitmapCache();
        cache.Rig = RigIndex.For(doc);
        cache.PoseResolver = (f, cel) => Skinning.PoseFrameForRender(doc, f, cel, cache.Rig);
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
