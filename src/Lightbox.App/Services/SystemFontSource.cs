using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// The fonts already on this machine.
/// </summary>
/// <remarks>
/// <para>
/// <b>The path that always works.</b> No network, no cache, no waiting — an
/// artist who opens the text tool on a laptop with no connection has every font
/// they own. That is why this source is listed first and why the tool never
/// blocks on the other one.
/// </para>
/// <para>
/// <b>It offers no bytes</b>, and that is the licence policy rather than a
/// missing feature: this application cannot read the terms of a font somebody
/// installed, so it will name it in a document and never copy it into one. See
/// <see cref="FontFace.Embeddable"/>.
/// </para>
/// </remarks>
public sealed class SystemFontSource : IFontSource
{
    public string Name => "Installed";

    public Task<IReadOnlyList<FontFace>> FacesAsync(CancellationToken cancel = default) =>
        Task.FromResult(Faces());

    /// <summary>
    /// Every installed family, at every style it ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Widths other than normal are folded onto the face they are closest to
    /// rather than offered separately, because <c>FontRef</c> records weight and
    /// slant only. A condensed cut therefore appears once under its own family
    /// name — which is how most of them are installed anyway ("Roboto
    /// Condensed") — and a family that ships width as a style would offer two
    /// faces that look identical in the list. Dropping the duplicates is the
    /// lesser wrong of the two, and the roadmap carries the real fix.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<FontFace> Faces()
    {
        var faces = new List<FontFace>();
        var seen = new HashSet<(string, int, bool)>();

        using var manager = SKFontManager.CreateDefault();
        foreach (var family in manager.GetFontFamilies())
        {
            if (string.IsNullOrWhiteSpace(family)) continue;

            using var styles = manager.GetFontStyles(family);
            for (var i = 0; i < styles.Count; i++)
            {
                var style = styles[i];
                if (style is null) continue;
                var italic = style.Slant != SKFontStyleSlant.Upright;
                if (!seen.Add((family, style.Weight, italic))) continue;
                faces.Add(new FontFace(family, style.Weight, italic, FontOrigin.Installed));
            }
        }

        faces.Sort(Compare);
        return faces;
    }

    /// <summary>Nothing, deliberately — see the type's remarks.</summary>
    public Task<byte[]?> LoadAsync(FontFace face, CancellationToken cancel = default) =>
        Task.FromResult<byte[]?>(null);

    internal static int Compare(FontFace a, FontFace b)
    {
        var family = string.Compare(a.Family, b.Family, StringComparison.CurrentCultureIgnoreCase);
        if (family != 0) return family;
        if (a.Weight != b.Weight) return a.Weight.CompareTo(b.Weight);
        return a.Italic.CompareTo(b.Italic);
    }
}
