namespace Lightbox.App.Services;

/// <summary>Where a font came from.</summary>
public enum FontOrigin
{
    /// <summary>Installed on this machine. Fast, offline, and unknown licence.</summary>
    Installed,

    /// <summary>Google Fonts. Fetched once, cached, and licensed to travel.</summary>
    Google,
}

/// <summary>
/// One face an artist can set type in: a family at a weight and a slant.
/// </summary>
/// <param name="Licence">
/// The licence under which the bytes may be redistributed — "OFL-1.1",
/// "Apache-2.0", "UFL-1.0" — or null when this application does not know,
/// which is every installed font.
/// </param>
/// <param name="Location">
/// How the source finds it again: a file for a cached download, and nothing at
/// all for an installed face, which the font manager resolves by name.
/// </param>
/// <remarks>
/// <para>
/// <b>A face rather than a family</b>, because weight and slant are what an
/// artist picks and what the shaper needs. Photoshop's family-plus-style pair is
/// the same information with the second half spelled inconsistently by every
/// foundry — see <see cref="Lightbox.Core.Documents.FontRef"/> for why that
/// spelling is not stored anywhere.
/// </para>
/// </remarks>
public sealed record FontFace(
    string Family,
    int Weight,
    bool Italic,
    FontOrigin Origin,
    string? Licence = null,
    string? Location = null)
{
    /// <summary>
    /// Whether a document may carry this font so its text stays editable
    /// elsewhere.
    /// </summary>
    /// <remarks>
    /// <b>Knowing the licence is the condition, and not knowing is a "no".</b>
    /// An installed font is somebody's property under terms this application
    /// cannot read, so the honest answer is to name it and not copy it. A Google
    /// font publishes its licence in the catalogue, and all of them permit
    /// redistribution — which is what makes the difference real rather than
    /// bureaucratic.
    /// </remarks>
    public bool Embeddable => Licence is not null;

    /// <summary>The weight and slant, as an artist would say them.</summary>
    public string StyleName
    {
        get
        {
            var weight = Weight switch
            {
                <= 100 => "Thin",
                <= 200 => "Extra Light",
                <= 300 => "Light",
                <= 400 => "Regular",
                <= 500 => "Medium",
                <= 600 => "Semi Bold",
                <= 700 => "Bold",
                <= 800 => "Extra Bold",
                _ => "Black",
            };
            if (!Italic) return weight;
            return weight == "Regular" ? "Italic" : weight + " Italic";
        }
    }

    public override string ToString() => $"{Family} {StyleName}";
}

/// <summary>
/// Somewhere faces come from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two sources today and the interface is the point.</b> Installed fonts and
/// Google Fonts answer the same two questions — what is there, and give me the
/// bytes — and differ in every other way: one is instant, offline and unknown
/// licence, the other is a network round trip with a licence attached. Anything
/// that knows the difference beyond this interface (the licence policy, the
/// download progress) is a leak worth noticing.
/// </para>
/// </remarks>
public interface IFontSource
{
    /// <summary>The heading an artist sees this source's fonts grouped under.</summary>
    string Name { get; }

    /// <summary>What this source offers. Never throws: a source that cannot answer offers nothing.</summary>
    Task<IReadOnlyList<FontFace>> FacesAsync(CancellationToken cancel = default);

    /// <summary>
    /// The font file for a face, or null when it cannot be had.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception for a source that is simply unreachable —
    /// an artist with no network still gets a font browser, showing what is
    /// cached and what is installed.
    /// </remarks>
    Task<byte[]?> LoadAsync(FontFace face, CancellationToken cancel = default);
}
