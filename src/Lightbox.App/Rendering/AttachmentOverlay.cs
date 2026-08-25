using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The pixels a variant wears: resolves Q143 attachments for the document on
/// the canvas and renders them as one overlay above the layer stack.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ambient, like the palette and symbol registries, and for the same
/// reason.</b> The compose paths — the view's publish and the exporter's
/// <c>ComposeFrame</c> — are pure functions of a scene, and which variant is
/// being viewed is project state they must not learn individually.
/// <c>RegisterResources</c> points the resolver at the active document's
/// dressed variants exactly where it points the palettes, so the canvas and
/// an export of that document always agree about what is being worn — the
/// same contract the palette stand-in already keeps.
/// </para>
/// <para>
/// <b>Above the whole stack, in this cut.</b> Armor behind the near arm is a
/// real want and a real design question (which layer does an attachment sit
/// under?), and Q143 records it as unanswered rather than guessed at.
/// </para>
/// </remarks>
public static class AttachmentOverlay
{
    /// <summary>
    /// What the viewed variant wears at one timeline index of a scene, or
    /// null when nothing is being worn — the ordinary state, and the one that
    /// must cost nothing.
    /// </summary>
    public static Func<Scene, int, IReadOnlyList<SymbolPlacement>>? Resolver { get; set; }

    /// <summary>
    /// Render the overlay for one frame index, or null when nothing applies.
    /// The caller owns the bitmap.
    /// </summary>
    public static SKBitmap? Render(Scene scene, int index, double outputScale = 1.0)
    {
        if (Resolver?.Invoke(scene, index) is not { Count: > 0 } placements) return null;

        var info = new SKImageInfo(
            scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var scaled = outputScale == 1.0
            ? info
            : info.WithSize(
                (int)Math.Ceiling(info.Width * outputScale),
                (int)Math.Ceiling(info.Height * outputScale));
        var bitmap = new SKBitmap(scaled);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        // The same pass a stored placement takes, so the two kinds of symbol
        // cannot drift apart — and so a cycle advances with the index for free.
        SymbolRasterizer.StampPlacements(canvas, placements, info, index, outputScale);
        canvas.Flush();
        return bitmap;
    }
}
