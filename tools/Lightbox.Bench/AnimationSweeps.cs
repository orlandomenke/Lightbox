using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.Bench;

/// <summary>
/// The animation half of the map: what happens as a sequence gets longer,
/// deeper and more layered.
/// </summary>
/// <remarks>
/// This half was chosen first because it had no coverage at all. Every
/// existing budget measures one stroke on one frame — which is drawing, not
/// animating — and the unit of work in this application is a sequence.
/// </remarks>
public static class AnimationSweeps
{
    /// <summary>A frame an artist would actually be drawing on.</summary>
    private const int W = 1920, H = 1080;

    /// <summary>
    /// The sequence sweeps run smaller, and say so in the report. A 384-frame
    /// scene at 1080p is a genuine workload but building one cold takes minutes
    /// before a single thing is timed, and the shape of the curve — which is
    /// the result — is the same at either size. The absolute figures on those
    /// rows are 720p figures and must not be compared with the per-frame ones.
    /// </summary>
    private const int SeqW = 1280, SeqH = 720;

    /// <summary>
    /// A drawing of roughly the density of real line art: strokes spread over
    /// the frame, not one blob in the middle.
    /// </summary>
    private static PaintedFrame Drawing(int strokes, int seed) => Drawing(strokes, seed, W, H);

    private static PaintedFrame Drawing(int strokes, int seed, int w, int h)
    {
        var frame = new PaintedFrame();
        for (var s = 0; s < strokes; s++)
        {
            // Deterministic placement — the harness must measure the same
            // workload on every run, and an RNG here would make two reports
            // incomparable for a reason that has nothing to do with the code.
            var a = (s * 2654435761u + (uint)seed * 40503u) % 10007;
            var b = (s * 1597334677u + (uint)seed * 22571u) % 10009;
            var x = 60 + a % (uint)(w - 120);
            var y = 60 + b % (uint)(h - 120);

            var points = new List<StrokePoint>();
            for (var i = 0; i < 8; i++)
            {
                points.Add(new StrokePoint(
                    x + i * 14 + (a % 7) * i,
                    y + i * 9 - (b % 5) * i,
                    0.4 + (i % 4) * 0.2));
            }

            frame.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#20202a",
                Points = points,
                Brush = new BrushSettings { Size = 9, Hardness = 0.7, Opacity = 1, Flow = 0.9, Spacing = 0.1 },
            });
        }
        return frame;
    }

    private static Layer LayerOf(int frames, int strokesPerFrame, int seed, int w, int h)
    {
        var layer = new Layer { Name = $"L{seed}" };
        for (var f = 0; f < frames; f++)
        {
            layer.Cels.Add(new Cel { Frame = Drawing(strokesPerFrame, seed * 97 + f, w, h) });
        }
        return layer;
    }

    private static Scene SceneOf(int layers, int frames, int strokesPerFrame, int w = W, int h = H)
    {
        var scene = new Scene { Width = w, Height = h, FrameCount = frames };
        scene.Layers.Clear();
        for (var l = 0; l < layers; l++) scene.Layers.Add(LayerOf(frames, strokesPerFrame, l + 1, w, h));
        return scene;
    }

    /// <summary>
    /// The surface every sweep composites into, held for the life of a value
    /// rather than made per call.
    /// </summary>
    /// <remarks>
    /// <b>This started as a bug in the harness and is worth the comment.</b>
    /// The first run created a fresh surface inside the timed region and
    /// reported that compositing <em>one</em> cached layer missed a 16 ms
    /// frame budget — which is not a fact about compositing, it is the cost of
    /// allocating and zeroing eight megabytes. The application does not do
    /// that: <c>ComposeRing</c> holds its surfaces across repaints, and that is
    /// the whole reason it exists. A harness that allocates where the app
    /// reuses is measuring the harness.
    /// </remarks>
    private sealed class Target(int w, int h) : IDisposable
    {
        public SKSurface Surface { get; } =
            SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul))!;

        public void Dispose() => Surface.Dispose();
    }

    /// <summary>Composite one frame of a scene the way the canvas does.</summary>
    private static void Composite(Target target, Scene scene, FrameBitmapCache cache, int index)
    {
        int w = scene.Width, h = scene.Height;
        var canvas = target.Surface.Canvas;
        canvas.Clear(SKColors.White);
        foreach (var layer in scene.Layers)
        {
            if (!layer.Visible) continue;
            if (ExposureSheet.ExposedFrame(layer, index) is not { } frame) continue;
            var bmp = cache.Get(frame, w, h);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(layer.Opacity * 255)) };
            canvas.DrawBitmap(bmp, 0, 0, paint);
        }
        canvas.Flush();
    }

    /// <summary>A cache and a surface, disposed together when a value is done.</summary>
    private sealed class Rig(int w, int h) : IDisposable
    {
        public FrameBitmapCache Cache { get; } = new();

        public Target Target { get; } = new(w, h);

        public void Dispose()
        {
            Cache.Dispose();
            Target.Dispose();
        }
    }

    public static IEnumerable<Scenario> All()
    {
        yield return Layers();
        yield return OnionDepth();
        yield return StrokesPerFrame();
        yield return SceneLength();
        yield return Playback();
        yield return Scrubbing();
    }

    // ---- how many layers can a frame carry -----------------------------------

    private static Scenario Layers()
    {
        Scene? scene = null;
        Rig? rig = null;

        return new Scenario(
            "Recomposite the whole frame",
            "layers",
            [1, 2, 4, 8, 16, 24],
            Cadence.WhilePlaying,
            Setup: n =>
            {
                scene = SceneOf(n, frames: 4, strokesPerFrame: 40);
                rig = new Rig(W, H);
                Composite(rig.Target, scene, rig.Cache, 0); // warm: this measures blending, not rasterising
                return rig;
            },
            Work: _ => Composite(rig!.Target, scene!, rig.Cache, 0),
            Note: "Cache warm and the surface reused, so this is the per-layer blend and nothing else. "
                + "Judged against the playback budget rather than the pointer-event one, and that is not a "
                + "softening: a full recomposite is what a frame CHANGE costs — scrubbing, playback, undo. "
                + "Drawing does not pay it, because ComposeRing repaints only the region the stroke touched, "
                + "which is the whole reason that class exists. Measuring a full recomposite against 16 ms "
                + "would be scoring the application for work it is careful never to do.")
        {
            Gauge = () => rig?.Cache.CachedBytes ?? 0,
            GaugeUnit = "cache MB",
        };
    }

    // ---- how deep can onion skin go ------------------------------------------

    private static Scenario OnionDepth()
    {
        Scene? scene = null;
        Rig? rig = null;

        return new Scenario(
            "Draw a frame with onion skin",
            "ghosts each side",
            [0, 1, 2, 3, 4, 6, 8],
            Cadence.WhileDrawing,
            Setup: n =>
            {
                scene = SceneOf(layers: 3, frames: 20, strokesPerFrame: 40);
                rig = new Rig(W, H);
                for (var i = 0; i < 20; i++) Composite(rig.Target, scene, rig.Cache, i);
                return rig;
            },
            Work: n =>
            {
                var canvas = rig!.Target.Surface.Canvas;
                canvas.Clear(SKColors.White);

                foreach (var layer in scene!.Layers)
                {
                    foreach (var ghost in OnionSkin.Ghosts(layer, 10, n, n, keysOnly: false))
                    {
                        var alpha = OnionSkin.OpacityAt(ghost.Steps, 0.5, 0.6);
                        var bmp = rig.Cache.Get(ghost.Frame, W, H);
                        using var paint = new SKPaint
                        {
                            Color = SKColors.White.WithAlpha((byte)(alpha * 255)),
                        };
                        canvas.DrawBitmap(bmp, 0, 0, paint);
                    }
                }
                Composite(rig.Target, scene, rig.Cache, 10);
            },
            Note: "3 layers, so the ghost count is 3× the depth on each side. The multiplier nobody had measured.")
        {
            Gauge = () => rig?.Cache.CachedBytes ?? 0,
            GaugeUnit = "cache MB",
        };
    }

    // ---- how busy can one drawing get ----------------------------------------

    private static Scenario StrokesPerFrame()
    {
        PaintedFrame? frame = null;

        return new Scenario(
            "Rasterise a frame from its strokes",
            "strokes",
            [25, 50, 100, 200, 400, 800],
            Cadence.PerAction,
            Setup: n =>
            {
                frame = Drawing(n, seed: 7);
                return null;
            },
            Work: _ =>
            {
                using var bmp = FrameRasterizer.Rasterize(frame!.Strokes, W, H);
            },
            Note: "A cache miss, an undo, and every frame of an export pay this.");
    }

    // ---- how long can a scene get --------------------------------------------

    private static Scenario SceneLength()
    {
        Scene? scene = null;
        Rig? rig = null;

        return new Scenario(
            "Hold a whole scene in the frame cache",
            "frames",
            [12, 24, 48, 96],
            Cadence.PerSession,
            Setup: n =>
            {
                scene = SceneOf(3, n, 20, SeqW, SeqH);
                rig = new Rig(SeqW, SeqH);
                return rig;
            },
            Work: n =>
            {
                for (var i = 0; i < n; i++) Composite(rig!.Target, scene!, rig.Cache, i);
            },
            Note: "720p, 3 layers, 20 strokes a frame — 3.5 MB a cel, so 48 frames is 506 MB against a "
                + "512 MB cache and 96 frames is 1 GB. Stopped at 96 rather than run to 384: past the "
                + "ceiling a sequential walk misses EVERY time, because by the time it comes round again "
                + "the LRU has evicted everything it is about to ask for. Longer values only demonstrate "
                + "patience. The memory gauge is the number to read here, not the milliseconds.")
        {
            Gauge = () => rig?.Cache.CachedBytes ?? 0,
            GaugeUnit = "cache MB",
            // A cold walk is the measurement, so there is nothing to warm and
            // three passes is as much as a p95 needs.
            Iterations = 3,
            Warmup = 0,
        };
    }

    // ---- can it play ----------------------------------------------------------

    private static Scenario Playback()
    {
        Scene? scene = null;
        Rig? rig = null;
        var at = 0;

        return new Scenario(
            "Show the next frame during playback",
            "frames in the scene",
            [12, 24, 48, 96],
            Cadence.WhilePlaying,
            Setup: n =>
            {
                scene = SceneOf(3, n, 20, SeqW, SeqH);
                rig = new Rig(SeqW, SeqH);
                for (var i = 0; i < n; i++) Composite(rig.Target, scene, rig.Cache, i); // one pass round
                at = 0;
                return rig;
            },
            Work: n => Composite(rig!.Target, scene!, rig.Cache, at++ % n),
            Note: "720p. Second time round the loop — past the point where the cache stops fitting, this is where it shows.")
        {
            Gauge = () => rig?.Cache.CachedBytes ?? 0,
            GaugeUnit = "cache MB",
        };
    }

    // ---- scrubbing, which is what an artist actually does ---------------------

    private static Scenario Scrubbing()
    {
        Scene? scene = null;
        Rig? rig = null;
        var step = 0;

        return new Scenario(
            "Scrub to a frame across the sheet",
            "frames in the scene",
            [12, 24, 48, 96],
            Cadence.WhileDrawing,
            Setup: n =>
            {
                scene = SceneOf(3, n, 20, SeqW, SeqH);
                rig = new Rig(SeqW, SeqH);
                for (var i = 0; i < n; i++) Composite(rig.Target, scene, rig.Cache, i);
                step = 0;
                return rig;
            },
            // A large stride, so consecutive scrubs land far apart and a cache
            // that has begun evicting is caught. Scrubbing to the next frame
            // would hit warm every time and measure nothing.
            Work: n => Composite(rig!.Target, scene!, rig.Cache, step++ * 7 % n),
            Note: "720p. Dragging the playhead — a miss here is felt directly, so it takes the drawing budget.")
        {
            Gauge = () => rig?.Cache.CachedBytes ?? 0,
            GaugeUnit = "cache MB",
        };
    }
}
