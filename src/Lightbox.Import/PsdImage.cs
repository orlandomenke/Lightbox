using SkiaSharp;

namespace Lightbox.Import;

/// <summary>What a layer entry in a PSD's layer list actually is.</summary>
/// <remarks>
/// Photoshop stores a folder as <b>two</b> entries — a divider that opens it and
/// a hidden one that closes it — rather than by nesting, so the list is a flat
/// sequence with brackets in it. Keeping that shape here rather than building a
/// tree is deliberate: the reader's job is to say faithfully what the file
/// contains, and turning brackets into Lightbox's own grouping is a decision
/// about Lightbox that belongs where <c>LayerGroup</c> does.
/// </remarks>
public enum PsdLayerRole
{
    /// <summary>Ordinary pixels.</summary>
    Raster,

    /// <summary>Opens a folder (expanded in Photoshop's panel).</summary>
    GroupOpen,

    /// <summary>Opens a folder (collapsed in Photoshop's panel).</summary>
    GroupClosed,

    /// <summary>Closes the folder most recently opened.</summary>
    GroupEnd,
}

/// <summary>One entry from a PSD's layer list, as the file states it.</summary>
/// <param name="BlendKey">
/// The raw four-character Photoshop key (<c>norm</c>, <c>mul </c>, …), trailing
/// space included. Deliberately not mapped to <c>LayerBlendMode</c> here:
/// <c>Lightbox.Import</c> does not reference the document model, and keeping the
/// source format's own vocabulary means the reader cannot quietly lose a mode it
/// did not recognise — the mapping refuses loudly instead.
/// </param>
/// <param name="Pixels">
/// The layer's own pixels at its own <see cref="Left"/>/<see cref="Top"/>, not
/// canvas-sized, and null on a folder marker or a layer whose bounds are empty.
/// A PSD layer is only as big as its content, which is why the offset matters.
/// </param>
public sealed record PsdLayer(
    string Name,
    int Left,
    int Top,
    int Right,
    int Bottom,
    string BlendKey,
    double Opacity,
    bool Visible,
    bool Locked,
    PsdLayerRole Role,
    SKBitmap? Pixels)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>Whether this entry brackets a folder rather than holding pixels.</summary>
    public bool IsGroupMarker => Role is not PsdLayerRole.Raster;
}

/// <summary>A PSD read into memory: canvas size, layers bottom-first, composite.</summary>
/// <param name="Layers">
/// <b>Bottom-first</b>, the order the file stores them, which is also Lightbox's
/// compositing order. Photoshop's own panel shows the reverse.
/// </param>
/// <param name="Composite">
/// The flattened image Photoshop saves beside the layer data. Kept because it is
/// the only content of a PSD written without layers — and because it is the
/// honest answer for a file whose layers we can read but whose stack we could
/// not reproduce.
/// </param>
public sealed record PsdImage(
    int Width,
    int Height,
    IReadOnlyList<PsdLayer> Layers,
    SKBitmap? Composite) : IDisposable
{
    /// <summary>
    /// Conversions that were performed rather than refused, in the artist's words.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="PsdUnsupported"/>, and the line between them
    /// is whether the picture changed. A 16-bit file reduced to 8 shows the same
    /// image, so it is imported and noted; a mask dropped would not, so it is
    /// refused. Saying so anyway is the point — a silent lossy conversion is still
    /// a lossy conversion, and the artist should hear it from us rather than
    /// discover it later.
    /// </remarks>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Release every decoded bitmap this image owns.</summary>
    /// <remarks>
    /// A record with <see cref="SKBitmap"/> in it owns unmanaged pixels, and a
    /// PSD is the one import where forgetting costs canvas-area-per-layer rather
    /// than a tip-sized rounding error.
    /// </remarks>
    public void Dispose()
    {
        foreach (var layer in Layers) layer.Pixels?.Dispose();
        Composite?.Dispose();
    }
}

/// <summary>One reason a PSD was refused, and what to do about it.</summary>
/// <param name="Feature">The Photoshop feature found, in the artist's words.</param>
/// <param name="LayerName">Which layer carries it, or null when it is the file.</param>
/// <param name="Remedy">
/// The step that makes the file importable, named the way Photoshop's own menus
/// name it. This is the whole value of refusing rather than guessing: "cannot
/// open this" sends somebody away, and "flatten these three layers" does not.
/// </param>
public sealed record PsdUnsupported(string Feature, string? LayerName, string Remedy)
{
    public override string ToString() =>
        LayerName is null ? $"{Feature} — {Remedy}" : $"{Feature} on \"{LayerName}\" — {Remedy}";
}

/// <summary>
/// A PSD that parsed cleanly and uses features Lightbox has no model for.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="FormatException"/>, which means the bytes are wrong.
/// This means the bytes are <em>right</em> and we decline: importing anyway would
/// put a drawing on screen that is not the drawing the artist saved, because a
/// mask we ignore or an adjustment we drop changes what every pixel beneath it
/// looks like.
/// </para>
/// <para>
/// <b>Every reason is collected before this is thrown</b>, never just the first.
/// An artist fixing one problem per attempt gives up before a production file
/// opens, so the refusal has to be a list.
/// </para>
/// </remarks>
public sealed class PsdUnsupportedException(IReadOnlyList<PsdUnsupported> reasons)
    : Exception(Describe(reasons))
{
    public IReadOnlyList<PsdUnsupported> Reasons { get; } = reasons;

    private static string Describe(IReadOnlyList<PsdUnsupported> reasons)
    {
        var lines = reasons.Select(r => "  • " + r);
        return $"This PSD uses {reasons.Count} feature{(reasons.Count == 1 ? "" : "s")} "
            + "Lightbox cannot represent:\n" + string.Join("\n", lines);
    }
}
