using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Which channel the canvas shows on its own, as grayscale. View-only, the
/// same side of the line as zoom and mirror (invariant 5): it never touches
/// the document, only what a render shows.
/// </summary>
public enum ChannelSolo
{
    None,
    Red,
    Green,
    Blue,
    Alpha,

    /// <summary>
    /// The pose-reading view (Q135): the ink as solid black on white paper —
    /// the classic silhouette check. Unlike the channels above it also changes
    /// what gets COMPOSITED (no paper, no references, no ghosts — see
    /// <c>ScenePassBuilder.State.Silhouette</c>), because the paper is baked
    /// into the composite and no colour matrix can take it back out.
    /// </summary>
    Silhouette,
}

/// <summary>The colour matrices that isolate one channel as grayscale.</summary>
public static class ChannelSoloFilters
{
    // Built once: a filter per draw would be an allocation on every frame of
    // a soloed session. SKColorFilter is immutable and thread-safe to share.
    private static readonly SKColorFilter Red = Matrix(r: 1);
    private static readonly SKColorFilter Green = Matrix(g: 1);
    private static readonly SKColorFilter Blue = Matrix(b: 1);

    /// <summary>
    /// Alpha reads as opaque grayscale — transparent is black, solid is white.
    /// The alpha row is constant 1, or the solo would fade itself out.
    /// </summary>
    private static readonly SKColorFilter Alpha = SKColorFilter.CreateColorMatrix(
    [
        0, 0, 0, 1, 0,
        0, 0, 0, 1, 0,
        0, 0, 0, 1, 0,
        0, 0, 0, 0, 1,
    ]);

    /// <summary>
    /// Every colour to black, coverage kept — what flattens ink to a
    /// silhouette while the paper (composited out) stays white underneath.
    /// </summary>
    private static readonly SKColorFilter SilhouetteInk = SKColorFilter.CreateColorMatrix(
    [
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 0, 0,
        0, 0, 0, 1, 0,
    ]);

    public static SKColorFilter? For(ChannelSolo solo) => solo switch
    {
        ChannelSolo.Red => Red,
        ChannelSolo.Green => Green,
        ChannelSolo.Blue => Blue,
        ChannelSolo.Alpha => Alpha,
        ChannelSolo.Silhouette => SilhouetteInk,
        _ => null,
    };

    /// <summary>One source channel copied to R, G and B; alpha kept.</summary>
    private static SKColorFilter Matrix(float r = 0, float g = 0, float b = 0) =>
        SKColorFilter.CreateColorMatrix(
        [
            r, g, b, 0, 0,
            r, g, b, 0, 0,
            r, g, b, 0, 0,
            0, 0, 0, 1, 0,
        ]);
}
