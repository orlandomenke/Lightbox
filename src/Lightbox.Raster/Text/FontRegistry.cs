using System.Collections.Concurrent;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Text;

/// <summary>
/// Fonts a document carries, keyed by their document id. Documents register
/// their <c>Fonts</c> on load; retyping resolves by id.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement as <see cref="TextureRegistry"/> and
/// <see cref="BrushTipRegistry"/>, with one difference that matters: those are
/// resolved <em>while rendering</em>, and this is not. A text element's contours
/// are already in the record, so nothing here is on any paint path — a document
/// whose fonts all fail to resolve renders perfectly and simply cannot be
/// retyped. That is why a miss returns null instead of substituting a face: a
/// silent substitution would reshape the artist's words in a font they did not
/// choose, which is worse than being told the font is gone.
/// </para>
/// <para>
/// Decoded once and kept, because a typeface is expensive to build and an artist
/// editing a caption rebuilds the text on every keystroke.
/// </para>
/// </remarks>
public static class FontRegistry
{
    private static readonly ConcurrentDictionary<string, SKTypeface> Faces = new();

    /// <summary>Take a document's embedded fonts, replacing any registered under the same ids.</summary>
    public static void Register(IReadOnlyDictionary<string, EmbeddedFont>? fonts)
    {
        if (fonts is null) return;
        foreach (var (id, font) in fonts)
        {
            if (Faces.ContainsKey(id)) continue;
            if (Decode(font) is { } face) Faces[id] = face;
        }
    }

    /// <summary>The typeface behind an embedded id, or null if this document never carried it.</summary>
    public static SKTypeface? Embedded(string? id) =>
        id is not null && Faces.TryGetValue(id, out var face) ? face : null;

    /// <summary>
    /// The installed face closest to a reference, or null when nothing on this
    /// machine claims the family.
    /// </summary>
    /// <remarks>
    /// <b>Null rather than a substitute, and the family match is exact.</b> Skia
    /// will happily hand back its default face for a family it has never heard
    /// of, which would mean a caption silently retyping itself in something else
    /// — so the returned face is checked against what was asked for. Weight and
    /// slant are allowed to be approximate, because that is what a font manager
    /// is for: asking for 500 where the family ships 400 and 700 should give the
    /// nearer one rather than nothing.
    /// </remarks>
    public static SKTypeface? System(FontRef font)
    {
        if (string.IsNullOrWhiteSpace(font.Family)) return null;

        var face = SKFontManager.Default.MatchFamily(
            font.Family,
            new SKFontStyle(
                font.Weight,
                (int)SKFontStyleWidth.Normal,
                font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright));

        if (face is null) return null;
        if (!string.Equals(face.FamilyName, font.Family, StringComparison.OrdinalIgnoreCase))
        {
            face.Dispose();
            return null;
        }
        return face;
    }

    /// <summary>
    /// The face to retype an element with: the one the document carries, else
    /// the one installed here, else nothing.
    /// </summary>
    /// <remarks>
    /// Embedded first on purpose. A document that carries its font was made to
    /// travel, and the bytes it carries are the ones the words were shaped
    /// with — an installed face of the same name can be a different cut, a
    /// different version, or a different foundry's idea of "Roboto".
    /// </remarks>
    public static SKTypeface? Resolve(FontRef font) => Embedded(font.EmbeddedId) ?? System(font);

    /// <summary>Forget everything — closing a document, and the test seam.</summary>
    public static void Clear()
    {
        foreach (var face in Faces.Values) face.Dispose();
        Faces.Clear();
    }

    private static SKTypeface? Decode(EmbeddedFont font)
    {
        try
        {
            var bytes = Convert.FromBase64String(font.Data);
            using var data = SKData.CreateCopy(bytes);
            return SKTypeface.FromData(data);
        }
        catch (FormatException)
        {
            // A hand-edited document, or one truncated in transit. The text
            // still renders from its contours; only retyping is lost, which is
            // the same outcome as a font that is simply not installed.
            return null;
        }
    }
}
