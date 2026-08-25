using Lightbox.Core.Documents;
using Lightbox.Raster.Text;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Every font an artist can reach, and the one place that turns a chosen face
/// into something a document can record.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two jobs, and separating them is what keeps the licence rule honest.</b>
/// Finding a face is <see cref="FacesAsync"/> and is the same for both sources;
/// <em>recording</em> one is <see cref="Reference"/>, which is where a
/// document either carries the bytes or does not. Every decision about what
/// travels in a file is in that one method, so there is a single place to read
/// to know what a Lightbox document can contain.
/// </para>
/// <para>
/// <b>Resolution order is embedded, then downloaded, then installed</b>
/// (<see cref="ResolveAsync"/>). A document that carried its font gets exactly
/// the cut its words were shaped with, even on a machine that has a different
/// version of the same family installed under the same name.
/// </para>
/// </remarks>
public sealed class FontLibrary : IDisposable
{
    private readonly SystemFontSource _installed = new();
    private readonly GoogleFontSource? _google;
    private readonly Dictionary<FontFace, SKTypeface?> _faces = [];
    private readonly Dictionary<FontFace, byte[]> _bytes = [];

    public FontLibrary(GoogleFontSource? google = null)
    {
        _google = google;
    }

    /// <summary>Whether Google Fonts are being offered at all.</summary>
    public bool HasGoogle => _google is not null;

    /// <summary>What went wrong reaching Google Fonts, if anything.</summary>
    public string? Trouble => _google?.Trouble;

    /// <summary>Everything on offer: installed first, then Google.</summary>
    /// <remarks>
    /// Installed first because it is instant and offline, so the list an artist
    /// sees is complete before any network call finishes — the Google entries
    /// arrive into the same list rather than the browser waiting on them.
    /// </remarks>
    public async Task<IReadOnlyList<FontFace>> FacesAsync(CancellationToken cancel = default)
    {
        var faces = new List<FontFace>(SystemFontSource.Faces());
        if (_google is not null) faces.AddRange(await _google.FacesAsync(cancel).ConfigureAwait(false));
        return faces;
    }

    /// <summary>Just the installed ones — no waiting, for the moment a tool opens.</summary>
    public static IReadOnlyList<FontFace> Installed() => SystemFontSource.Faces();

    /// <summary>Whether a Google face has already been downloaded.</summary>
    public bool IsReady(FontFace face) =>
        face.Origin != FontOrigin.Google || _google?.IsCached(face) == true;

    /// <summary>
    /// The typeface for a face, downloading it once if it is a Google font that
    /// has not been fetched yet.
    /// </summary>
    public async Task<SKTypeface?> LoadAsync(FontFace face, CancellationToken cancel = default)
    {
        if (_faces.TryGetValue(face, out var known)) return known;

        SKTypeface? typeface = null;
        if (face.Origin == FontOrigin.Installed)
        {
            typeface = FontRegistry.System(
                new FontRef { Family = face.Family, Weight = face.Weight, Italic = face.Italic });
        }
        else if (_google is not null
            && await _google.LoadAsync(face, cancel).ConfigureAwait(false) is { } bytes)
        {
            typeface = FromBytes(bytes);
            // Kept because embedding needs them later and must not be an
            // asynchronous step in the middle of committing a caption — see
            // Reference. A downloaded font is a few hundred kilobytes and there
            // are as many of these as the artist has picked fonts.
            if (typeface is not null) _bytes[face] = bytes;
        }

        _faces[face] = typeface;
        return typeface;
    }

    /// <summary>
    /// The typeface to retype an element with: what the document carries, else
    /// what has been downloaded, else what is installed, else nothing.
    /// </summary>
    /// <remarks>
    /// Null means the text can still be seen and moved and erased — it is a
    /// drawing — and cannot be edited as words. That is the state to report to
    /// the artist rather than quietly substituting a face, which would rewrite
    /// their title in a font they never chose the moment they touched it.
    /// </remarks>
    public async Task<SKTypeface?> ResolveAsync(FontRef font, CancellationToken cancel = default)
    {
        if (FontRegistry.Embedded(font.EmbeddedId) is { } carried) return carried;

        if (_google is not null)
        {
            var face = new FontFace(font.Family, font.Weight, font.Italic, FontOrigin.Google);
            if (_google.IsCached(face)
                && await _google.LoadAsync(face, cancel).ConfigureAwait(false) is { } bytes
                && FromBytes(bytes) is { } downloaded)
            {
                return downloaded;
            }
        }

        return FontRegistry.System(font);
    }

    /// <summary>
    /// What a document must record to set type in a face — and, when the
    /// licence allows it, the font to carry along with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Worked out without touching the document, because committing text is
    /// an undoable edit.</b> The apply and the revert both need to know exactly
    /// what was added, and a method that quietly inserted a font into the
    /// document as a side effect of being asked a question would leave the
    /// revert guessing. So this is the answer, and <see cref="RecordInto"/> is
    /// the act.
    /// </para>
    /// </remarks>
    public readonly record struct FontChoice(FontRef Reference, string? NewId, EmbeddedFont? Font)
    {
        /// <summary>Put the carried font in, if this choice brought a new one.</summary>
        public void RecordInto(Doc doc)
        {
            if (NewId is null || Font is null) return;
            doc.Fonts ??= [];
            doc.Fonts[NewId] = Font;
            FontRegistry.Register(doc.Fonts);
        }

        /// <summary>Take it back out — the undo half of <see cref="RecordInto"/>.</summary>
        /// <remarks>
        /// Only ever removes what this choice added: a font that was already in
        /// the document when the text was set belongs to whatever put it there,
        /// and undoing one caption must not strip the font another is using.
        /// </remarks>
        public void RemoveFrom(Doc doc)
        {
            if (NewId is null || doc.Fonts is null) return;
            doc.Fonts.Remove(NewId);
            if (doc.Fonts.Count == 0) doc.Fonts = null;
        }
    }

    /// <summary>
    /// The reference a document records for a face — carrying the font itself
    /// when the licence permits it and the artist has not asked otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole of the embedding policy is here.</b> Three conditions, all
    /// required: the source published a licence that allows redistribution
    /// (<see cref="FontFace.Embeddable"/>), the artist has left
    /// <see cref="FontSettings.EmbedOpenFonts"/> on, and the bytes are actually
    /// to hand. Any of them missing and the document names the font and carries
    /// nothing, which changes nothing about how it renders.
    /// </para>
    /// <para>
    /// One copy per face per document: a caption, a title and a sign set in the
    /// same font embed it once. The existing entry is found by what it is rather
    /// than by an id, because the second document to be pasted in will have
    /// allocated a different one.
    /// </para>
    /// <para>
    /// Synchronous, and that is the requirement rather than a convenience:
    /// committing a caption happens on a keystroke and must not be able to wait
    /// on a network. By then the bytes are in hand from having been loaded to
    /// draw with; if they are not, the document names the font and carries
    /// nothing, which costs portability and no pixels.
    /// </para>
    /// </remarks>
    public FontChoice Reference(FontFace face, Doc doc, bool embed)
    {
        var reference = new FontRef
        {
            Family = face.Family,
            Weight = face.Weight,
            Italic = face.Italic,
        };

        if (!embed || !face.Embeddable) return new FontChoice(reference, null, null);

        if (doc.Fonts is not null)
        {
            foreach (var (id, carried) in doc.Fonts)
            {
                if (carried.Family == face.Family
                    && carried.Weight == face.Weight
                    && carried.Italic == face.Italic)
                {
                    reference.EmbeddedId = id;
                    return new FontChoice(reference, null, null);
                }
            }
        }

        if (!_bytes.TryGetValue(face, out var bytes)) return new FontChoice(reference, null, null);

        var newId = Ids.NewId("fnt");
        reference.EmbeddedId = newId;
        return new FontChoice(
            reference,
            newId,
            new EmbeddedFont
            {
                Family = face.Family,
                Weight = face.Weight,
                Italic = face.Italic,
                Licence = face.Licence ?? "",
                Source = "google",
                Data = Convert.ToBase64String(bytes),
            });
    }

    /// <summary>
    /// <inheritdoc cref="Reference"/>
    /// </summary>
    /// <remarks>
    /// The same answer, having first made sure the bytes are here — for a
    /// caller that is not in the middle of a keystroke.
    /// </remarks>
    public async Task<FontChoice> ReferenceAsync(
        FontFace face, Doc doc, bool embed, CancellationToken cancel = default)
    {
        if (embed && face.Embeddable && _google is not null && !_bytes.ContainsKey(face))
        {
            if (await _google.LoadAsync(face, cancel).ConfigureAwait(false) is { } bytes)
            {
                _bytes[face] = bytes;
            }
        }
        return Reference(face, doc, embed);
    }

    private static SKTypeface? FromBytes(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        return SKTypeface.FromData(data);
    }

    public void Dispose()
    {
        foreach (var face in _faces.Values) face?.Dispose();
        _faces.Clear();
        _google?.Dispose();
    }
}
