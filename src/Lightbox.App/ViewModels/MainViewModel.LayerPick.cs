using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
public partial class MainViewModel
{
    /// <summary>
    /// How far a click may miss by, in document pixels, however far the canvas is
    /// zoomed out. Bounds the surface composed per layer to 33 px a side.
    /// </summary>
    private const int MaxLayerPickReach = 16;

    /// <summary>
    /// How opaque a pixel must be to count as something being there. Below this a
    /// soft brush's skirt and an anti-aliased edge's last step are picked by nobody
    /// who can see them.
    /// </summary>
    private const byte LayerPickMinAlpha = 8;

    /// <summary>
    /// Make the layer under a document point the active one: Ctrl+Shift+click on the
    /// canvas.
    /// </summary>
    /// <param name="tolerance">
    /// How far the click may miss by, in document pixels. The canvas's, because only
    /// it knows the zoom: a hairline is a pixel wide however small it is drawn.
    /// </param>
    /// <returns>Whether a layer was there to pick.</returns>
    /// <remarks>
    /// <para>
    /// <b>The topmost layer that shows something there</b>, which is the one the eye
    /// is on. A hidden layer shows nothing and is looked through; so is the
    /// background, which is paper rather than somewhere a mark lives, an
    /// adjustment layer, which has no pixels of its own, and a layer turned down
    /// so far (about 3% and under) that nothing of it can be seen. Onion-skin
    /// ghosts and reference images are not layers and are not found.
    /// </para>
    /// <para>
    /// <b>Known gap, shared with the eyedropper:</b> a layer displaced by the
    /// camera's parallax is asked about where it is in the document, not where the
    /// camera draws it. Both read the same passes and neither carries the camera.
    /// </para>
    /// <para>
    /// <b>A direct hit beats a near miss.</b> A layer with a pixel at the point
    /// itself wins over a higher one that only comes within the tolerance —
    /// otherwise a line on an upper layer that merely passes close by would take the
    /// click from the drawing the pointer is squarely on.
    /// </para>
    /// <para>
    /// <b>Read from what the compositor draws, not from the strokes.</b> A layer is
    /// "there" if it puts pixels there: a fill, a pasted image, a baseline and a
    /// masked or clipped layer all answer correctly without this knowing about any
    /// of them, and a stroke that was erased does not answer at all.
    /// </para>
    /// <para>
    /// Looking, not editing: nothing is written to the document and nothing goes on
    /// the undo stack. The active layer is a working state.
    /// </para>
    /// </remarks>
    public bool PickLayerAt(double x, double y, double tolerance)
    {
        var (px, py) = ToSurface(x, y);
        if (px < 0 || py < 0 || px >= Scene.Width || py >= Scene.Height) return false;

        var reach = double.IsFinite(tolerance)
            ? (int)Math.Clamp(Math.Ceiling(tolerance), 0, MaxLayerPickReach)
            : 0;
        if (TopmostLayerShowingAt(px, py, reach) is not { } index)
        {
            AiStatus = "No layer has anything there.";
            return false;
        }

        // Through the docker's own click where the row is on screen, so the
        // multi-selection collapses to this layer exactly as a click on it would.
        // A layer inside a collapsed folder has no row; it is made active directly.
        if (LayerPanelItems.OfType<LayerRow>().FirstOrDefault(r => r.SceneIndex == index) is { } row)
        {
            SelectLayer(row, toggle: false, range: false);
        }
        else
        {
            // No row to click, so the same three things a click does, by hand: the
            // folder pick is dropped, and making the layer active outside the
            // docker's own guard resets the layer selection to it alone.
            _selectedGroupIds.Clear();
            ActiveLayerIndex = index;
            RefreshGroupSelectionHighlights();
        }
        AiStatus = $"Layer: {Scene.Layers[index].Name}";
        return true;
    }

    /// <summary>
    /// The highest layer with a visible pixel at a surface point, or failing that
    /// the highest with one within <paramref name="reach"/> of it, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One small compose per layer, top down: proportional to the layers and never
    /// to the canvas (invariant 6). Each layer is composed alone over nothing, and
    /// with an ordinary blend whatever its own is — the question is whether it has
    /// pixels here, and a layer set to multiply over nothing would otherwise be
    /// asked what it does to a backdrop that is not there.
    /// </para>
    /// <para>
    /// <b>One walk, not one for the point and another for the tolerance.</b> Each
    /// layer is fetched once. A layer with a stroke that samples the layers under
    /// it is never cached, so every fetch of it is a whole render, and asking twice
    /// rendered it twice on exactly the click that found nothing (leak-hunter, in
    /// review). The walk stops at the first direct hit and otherwise remembers the
    /// first near one.
    /// </para>
    /// <para>
    /// <b>Once per click, and it must stay that way.</b> A layer that is not in the
    /// frame cache — after playback, which draws from tiles — is rendered whole to
    /// answer, the same cost the eyedropper's click accepts. That is a press, not a
    /// hover: nothing on the pointer-move path may call this.
    /// </para>
    /// </remarks>
    private int? TopmostLayerShowingAt(int x, int y, int reach)
    {
        var scene = Scene;
        var side = reach * 2 + 1;
        int? near = null;
        using var hold = _cache.HoldFetches();
        for (var index = scene.Layers.Count - 1; index >= 0; index--)
        {
            var layer = scene.Layers[index];
            if (layer.IsBackground || layer.IsAdjustment || !scene.IsLayerVisible(layer)) continue;
            if (LayerPass(scene, index) is not { } pass) continue;

            using var image = SceneRenderer.Compose(
                side, side, [pass with { Blend = SKBlendMode.SrcOver }], SKColors.Transparent,
                SKMatrix.CreateTranslation(-(x - reach), -(y - reach)));
            using var pixels = SKBitmap.FromImage(image);
            if (pixels.GetPixel(reach, reach).Alpha >= LayerPickMinAlpha) return index;
            if (near is null && ShowsWithin(pixels, reach)) near = index;
        }
        return near;
    }

    /// <summary>Whether any pixel within <paramref name="reach"/> of the centre is visible.</summary>
    private static bool ShowsWithin(SKBitmap pixels, int reach)
    {
        for (var dy = -reach; dy <= reach; dy++)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                // A disc, not the square it is composed in: the corners are further
                // away than the tolerance said.
                if (dx * dx + dy * dy > reach * reach) continue;
                if (pixels.GetPixel(dx + reach, dy + reach).Alpha >= LayerPickMinAlpha) return true;
            }
        }
        return false;
    }
}
