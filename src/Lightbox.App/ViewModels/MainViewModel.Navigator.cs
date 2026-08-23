using Avalonia.Media.Imaging;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// The navigator: the whole drawing at a glance, with the part of it you are
/// looking at drawn on top, and a click anywhere to go there (Q110).
/// </remarks>
public partial class MainViewModel
{
    // ---- the navigator ---------------------------------------------------------

    /// <summary>The longest edge of the navigator's picture, in pixels.</summary>
    /// <remarks>
    /// Large enough to make out where you are in a drawing, small enough that
    /// re-rendering it is a thumbnail rather than a frame: a 4K document comes
    /// down to this before anything else touches it.
    /// </remarks>
    private const int NavigatorLongEdge = 256;

    private Bitmap? _navigatorThumb;

    /// <summary>
    /// The current frame, composited small. Null with nothing open.
    /// </summary>
    /// <remarks>
    /// <b>Rendered on the same funnel as the timeline's thumbnails and no more
    /// often.</b> A navigator that re-composited per pointer event while
    /// panning would be a full-canvas compose in a live path, which is the one
    /// thing the charter forbids outright — and the picture does not change
    /// when the view moves, only when the drawing does.
    /// </remarks>
    public Bitmap? NavigatorThumb
    {
        get => _navigatorThumb;
        private set
        {
            if (ReferenceEquals(_navigatorThumb, value)) return;
            _navigatorThumb = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The document rectangle the canvas is showing, or null when it fills the view.</summary>
    /// <remarks>
    /// Reported by the canvas, which is the only thing that knows how much of
    /// the document is on screen — zoom, rotation, mirror and pan are view
    /// state and never touch the record (invariant 5).
    /// </remarks>
    public SKRectI? NavigatorViewport => _publish.Viewport;

    /// <summary>The document's own size, for the navigator to scale against.</summary>
    /// <remarks>
    /// A bindable <c>Size</c> rather than the scene's two ints, so the panel
    /// follows a document being opened or resized without the window pushing
    /// it. Re-announced from <see cref="RefreshNavigatorThumb"/>, which already
    /// runs whenever the picture could have changed.
    /// </remarks>
    public Avalonia.Size NavigatorDocumentSize =>
        new(Math.Max(1, Scene.Width), Math.Max(1, Scene.Height));

    /// <summary>The artist clicked or dragged in the navigator: take the view there.</summary>
    /// <remarks>
    /// Document coordinates. The window turns them into a pan, because moving
    /// the view is the canvas's business and the panel's job stops at saying
    /// where.
    /// </remarks>
    public event Action<double, double>? NavigatorCentreRequested;

    /// <summary>Ask to look at a point, in document coordinates.</summary>
    public void NavigateTo(double x, double y) => NavigatorCentreRequested?.Invoke(x, y);

    /// <summary>The viewport moved: only the rectangle changed, never the picture.</summary>
    private void RefreshNavigatorViewport() => OnPropertyChanged(nameof(NavigatorViewport));

    /// <summary>
    /// Refresh the navigator's picture — composing one only when the picture
    /// would actually differ.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Through the same cache the timeline's thumbnails use, and that is not
    /// tidiness.</b> The first version composed every visible layer on every
    /// call, and this is called from the thumbnail funnel — which runs on every
    /// edit, every playhead move and every document change. Across a test run
    /// that made thousands of surfaces and thousands of short-lived bitmaps, and
    /// the suite went from 3,221 tests to a native crash partway through. A
    /// panel that is glanced at must not cost a full compose to look at.
    /// </para>
    /// <para>
    /// The key is what the picture is <em>of</em>: the document's size and the
    /// drawings actually exposed at the playhead. Two calls with the same answer
    /// share one bitmap, the cache owns it, and its eviction bounds how many can
    /// exist — which is what B202 established for the thumbnails beside it.
    /// </para>
    /// </remarks>
    private void RefreshNavigatorThumb()
    {
        OnPropertyChanged(nameof(NavigatorDocumentSize));
        if (!Workspace.NavigatorVisible)
        {
            // A panel nobody has open pays nothing: this is a compose of every
            // visible layer, and the cheapest version of it is the one that
            // does not run.
            NavigatorThumb = null;
            return;
        }

        var scene = Scene;
        var exposed = new List<(Layer Layer, Frame Frame)>();
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            if (ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } frame) continue;
            exposed.Add((layer, frame));
        }

        var key = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"nav:{scene.Width}x{scene.Height}:{string.Join(',', exposed.Select(e => e.Frame.Id))}:{CarveStamp(scene, CurrentFrameIndex)}");
        NavigatorThumb = _thumbs.Get(key, () => ComposeNavigatorThumb(scene, exposed));
    }

    /// <summary>
    /// Compose the picture, holding every cached bitmap it draws.
    /// </summary>
    /// <remarks>
    /// <b>The pins are the whole of why this method exists separately, and
    /// leaving them out crashed the process.</b> <see cref="FrameBitmapCache"/>
    /// owns what <c>Get</c> returns and may evict it at any time — including
    /// during the very next <c>Get</c> in this loop, which is how a bitmap
    /// gathered for a pass list gets disposed before the compose reads it. That
    /// is a use-after-free in native memory: the App suite went from 3,221 tests
    /// to a SIGSEGV partway through, landing on whichever test was running
    /// rather than on this code. Pin while gathering, unpin in a finally — the
    /// protocol the publish path already follows for the same reason.
    /// </remarks>
    /// <summary>
    /// What the frame-id key cannot see: masks and effects change the picture
    /// without changing any exposed frame's id, so their observable state —
    /// mask drawings and stroke counts, clip flags, effect kinds and their
    /// values at this frame — salts the key. Stroke count is a proxy, not a
    /// hash; the one edit it misses (replacing a mask stroke with a different
    /// one in the same gesture) heals on the next edit, which a navigator
    /// thumbnail can afford.
    /// </summary>
    private static string CarveStamp(Scene scene, int frame)
    {
        var sb = new System.Text.StringBuilder();
        void Stack(Lightbox.Core.Effects.EffectStack? fx)
        {
            if (fx is null) return;
            foreach (var use in fx.Uses)
            {
                sb.Append(use.Kind).Append(use.Applies ? '+' : '-');
                foreach (var (key, param) in use.Params)
                {
                    sb.Append(key).Append(
                        param.At(frame).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            if (layer.IsClipped) sb.Append('c');
            if (layer.Mask is { } mask)
            {
                sb.Append('m').Append(mask.Frame.Id).Append(mask.Frame.Strokes.Count)
                    .Append(mask.Applies ? '+' : '-').Append(mask.IsInverted ? '!' : '.');
            }
            if (layer.HasLiveEffects)
            {
                sb.Append(layer.IsAdjustment ? 'a' : 'f');
                Stack(layer.Effects);
            }
        }
        Stack(scene.Effects);
        return sb.Length == 0 ? "" : sb.ToString();
    }

    private Bitmap ComposeNavigatorThumb(Scene scene, List<(Layer Layer, Frame Frame)> exposed)
    {
        var held = new List<SKBitmap>(exposed.Count);
        try
        {
            var passes = new List<RenderPass>(exposed.Count);
            void Hold(SKBitmap bmp)
            {
                _cache.Pin(bmp);
                held.Add(bmp);
            }
            // Every cached bitmap the compose will read is pinned — the pass
            // cels AND the shape masks, because a later fetch in this same
            // loop can evict an earlier one, which is the whole reason the
            // pin protocol exists.
            void HoldShapes(IReadOnlyList<PassShape>? shapes)
            {
                if (shapes is null) return;
                foreach (var shape in shapes) Hold(shape.Mask);
            }
            for (var layerIndex = 0; layerIndex < scene.Layers.Count; layerIndex++)
            {
                var layer = scene.Layers[layerIndex];
                if (!scene.IsLayerVisible(layer)) continue;
                if (layer.IsAdjustment)
                {
                    if (EffectPasses.AdjustmentPass(scene, layerIndex, CurrentFrameIndex, _cache) is { } adj)
                    {
                        HoldShapes(adj.Shapes);
                        passes.Add(adj);
                    }
                    continue;
                }
                if (Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(layer, CurrentFrameIndex)
                    is not { } frame)
                {
                    continue;
                }
                var shapes = LayerShapes.For(scene, layerIndex, CurrentFrameIndex);
                if (shapes is { Count: 0 }) continue;
                var bmp = _cache.Get(frame, scene.Width, scene.Height, celIndex: CurrentFrameIndex);
                Hold(bmp);
                var resolved = LayerShapes.Resolve(
                    shapes, _cache, scene.Width, scene.Height, CurrentFrameIndex);
                HoldShapes(resolved);
                passes.Add(new RenderPass(
                    bmp, null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode),
                    Shapes: resolved,
                    Effect: EffectPasses.SelfFilter(layer, CurrentFrameIndex)));
            }
            if (EffectPasses.SceneStackPass(scene, CurrentFrameIndex) is { } grade) passes.Add(grade);

            using var image = SceneRenderer.Compose(
                scene.Width, scene.Height, passes, SceneRenderer.BackgroundOf(scene));
            using var bitmap = SKBitmap.FromImage(image);
            var scale = NavigatorLongEdge / (double)Math.Max(1, Math.Max(scene.Width, scene.Height));
            return ThumbnailRenderer.RenderChecker(
                bitmap,
                Math.Max(1, (int)Math.Round(scene.Width * scale)),
                Math.Max(1, (int)Math.Round(scene.Height * scale)));
        }
        finally
        {
            foreach (var bmp in held) _cache.Unpin(bmp);
        }
    }
}
