namespace Lightbox.Import;

/// <summary>The Photoshop blend keys a PSD may carry into Lightbox.</summary>
/// <remarks>
/// <para>
/// Photoshop names a blend mode with a four-character key, space-padded — the
/// trailing space in <c>"mul "</c> is part of the key, not a typo. This is the
/// set <see cref="PsdReader"/> will hand on; anything else is refused by name
/// rather than silently rendered as Normal, because a Linear Burn layer
/// composited as Normal is a different picture and nothing on screen would say
/// so.
/// </para>
/// <para>
/// <b>The list lives here and the mapping to <c>LayerBlendMode</c> lives with
/// the document model</b>, which cannot be referenced from this project. That
/// split can drift, so the mapping's own tests walk this set and assert every
/// key resolves — a mode added here without a home there fails a test instead of
/// reaching an artist.
/// </para>
/// <para>
/// <c>pass</c> — Photoshop's pass-through, and the <em>default</em> for a new
/// folder — is included on purpose. It means "do not isolate this group", which
/// is already how a Lightbox folder behaves: members stay ordinary layers in the
/// scene and compositing order is unchanged. Refusing it would refuse almost
/// every grouped PSD in existence for a difference that does not exist here.
/// </para>
/// </remarks>
public static class PsdBlend
{
    /// <summary>Every key this reader will emit.</summary>
    public static readonly string[] SupportedKeys =
    [
        "norm", "pass", "mul ", "scrn", "over", "dark", "lite",
        "div ", "idiv", "hLit", "sLit", "diff", "smud",
        "hue ", "sat ", "colr", "lum ",
    ];

    /// <summary>
    /// Keys Photoshop writes that have no Lightbox equivalent, with the name an
    /// artist would recognise from the blend-mode dropdown.
    /// </summary>
    private static readonly Dictionary<string, string> KnownUnsupported = new()
    {
        ["diss"] = "Dissolve",
        ["lbrn"] = "Linear Burn",
        ["lddg"] = "Linear Dodge",
        ["vLit"] = "Vivid Light",
        ["lLit"] = "Linear Light",
        ["pLit"] = "Pin Light",
        ["hMix"] = "Hard Mix",
        ["fsub"] = "Subtract",
        ["fdiv"] = "Divide",
        ["dkCl"] = "Darker Color",
        ["lgCl"] = "Lighter Color",
    };

    public static bool IsSupported(string key) => Array.IndexOf(SupportedKeys, key) >= 0;

    /// <summary>The dropdown name for a key, for a refusal an artist can act on.</summary>
    public static string Describe(string key) =>
        KnownUnsupported.TryGetValue(key, out var name) ? name : $"\"{key.Trim()}\"";
}
