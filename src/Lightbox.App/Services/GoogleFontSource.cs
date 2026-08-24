using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Lightbox.App.Services;

/// <summary>
/// Google Fonts: a few thousand families that are the same on every machine and
/// licensed to travel inside a document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside the installed fonts.</b> An installed font is
/// whatever this particular computer happens to have, under terms nobody can
/// read — send the file to another artist and the text can no longer be retyped.
/// Every family here is under the OFL, Apache-2.0 or the Ubuntu Font Licence,
/// all of which permit redistribution, so a document that uses one can carry it
/// (<see cref="EmbeddedFont"/>) and stay editable anywhere.
/// </para>
/// <para>
/// <b>No API key, and that is a deliberate constraint.</b> The developer API
/// needs one, which would mean an artist signing up to a cloud console before
/// they can set a title — so this uses the two endpoints that need nothing: the
/// catalogue the Google Fonts website itself reads, and the documented CSS
/// endpoint that every web page in the world uses. The cost is that the first is
/// not a published contract, which is why <see cref="ParseCatalogue"/> reads it
/// defensively and a failure degrades to "what is cached" rather than to an
/// error dialog.
/// </para>
/// <para>
/// <b>Nothing here happens until an artist asks.</b> No fetch on startup, none
/// on opening a document; the first request is made when somebody opens the font
/// browser and looks at this source, and <c>FontSettings.UseGoogleFonts</c>
/// turns it off entirely. A drawing application that phones home to lay out a
/// caption would be one nobody should trust offline.
/// </para>
/// </remarks>
public sealed class GoogleFontSource : IFontSource, IDisposable
{
    /// <summary>The catalogue the Google Fonts site reads — families, styles and licences.</summary>
    private const string CatalogueUrl = "https://fonts.google.com/metadata/fonts";

    /// <summary>
    /// The documented stylesheet endpoint, which answers with whatever format
    /// the caller can read.
    /// </summary>
    private const string CssUrl = "https://fonts.googleapis.com/css2";

    /// <summary>
    /// What we claim to be, so that the answer is a TrueType file.
    /// </summary>
    /// <remarks>
    /// <b>This is the one piece of this file that is a trick, so it is written
    /// down rather than left to be discovered.</b> The CSS endpoint serves the
    /// newest format the requesting browser supports, which today is woff2 —
    /// a compressed container Skia cannot open. A user agent with no woff
    /// support at all gets plain TrueType, which is what a rendering engine
    /// wants. If Google ever stops honouring that, <see cref="ParseCss"/> finds
    /// no <c>.ttf</c> and the download reports that it could not, rather than
    /// handing the font machinery bytes it cannot read.
    /// </remarks>
    private const string TrueTypeAgent = "Mozilla/5.0";

    /// <summary>How long a fetched catalogue is trusted before asking again.</summary>
    /// <remarks>
    /// A day. The list changes a few times a month, and an artist who opened the
    /// browser yesterday should not wait on a network round trip to type a word
    /// today. A stale catalogue is never wrong about a font that exists — only
    /// about one added since.
    /// </remarks>
    private static readonly TimeSpan CatalogueLife = TimeSpan.FromDays(1);

    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private IReadOnlyList<FontFace>? _faces;

    public GoogleFontSource(HttpMessageHandler? transport = null, string? cacheDir = null)
    {
        _http = transport is null ? new HttpClient() : new HttpClient(transport);
        _http.Timeout = TimeSpan.FromSeconds(20);
        _http.DefaultRequestHeaders.Add("User-Agent", TrueTypeAgent);
        _cacheDir = cacheDir ?? DefaultCacheDir;
    }

    public static string DefaultCacheDir =>
        Path.Combine(
            Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "fonts");

    public string Name => "Google Fonts";

    /// <summary>Why the last attempt came back empty, for the browser to show.</summary>
    /// <remarks>
    /// A string rather than an exception because it is a line of interface text,
    /// not a fault: being offline is an ordinary state for this source and the
    /// artist wants to be told, not stopped.
    /// </remarks>
    public string? Trouble { get; private set; }

    public async Task<IReadOnlyList<FontFace>> FacesAsync(CancellationToken cancel = default)
    {
        if (_faces is not null) return _faces;

        var cached = Path.Combine(_cacheDir, "catalogue.json");
        var json = ReadIfFresh(cached);

        if (json is null)
        {
            try
            {
                json = await _http.GetStringAsync(CatalogueUrl, cancel).ConfigureAwait(false);
                Write(cached, json);
                Trouble = null;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
                // Fall back to any catalogue we have, however old: a year-old
                // list of families is worth incomparably more than an empty one.
                json = ReadIfFresh(cached, ignoreAge: true);
                Trouble = json is null
                    ? "Could not reach Google Fonts, and nothing is cached yet."
                    : "Could not reach Google Fonts — showing the list from last time.";
            }
        }

        _faces = json is null ? [] : ParseCatalogue(json);
        return _faces;
    }

    public async Task<byte[]?> LoadAsync(FontFace face, CancellationToken cancel = default)
    {
        var path = CachedFile(face);
        if (File.Exists(path))
        {
            try
            {
                return await File.ReadAllBytesAsync(path, cancel).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A half-written file from an interrupted download. Fetch again.
            }
        }

        try
        {
            var css = await _http.GetStringAsync(CssRequest(face), cancel).ConfigureAwait(false);
            if (ParseCss(css) is not { } url)
            {
                Trouble = $"Google Fonts did not offer {face} in a format this can read.";
                return null;
            }

            var bytes = await _http.GetByteArrayAsync(url, cancel).ConfigureAwait(false);
            Directory.CreateDirectory(_cacheDir);
            await File.WriteAllBytesAsync(path, bytes, cancel).ConfigureAwait(false);
            Trouble = null;
            return bytes;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            Trouble = $"Could not download {face}.";
            return null;
        }
    }

    /// <summary>Whether a face is already on disk — the browser badges the difference.</summary>
    public bool IsCached(FontFace face) => File.Exists(CachedFile(face));

    internal string CachedFile(FontFace face)
    {
        var name = new StringBuilder();
        foreach (var c in face.Family)
        {
            name.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }
        name.Append('-').Append(face.Weight);
        if (face.Italic) name.Append('i');
        name.Append(".ttf");
        return Path.Combine(_cacheDir, name.ToString());
    }

    internal Uri CssRequest(FontFace face)
    {
        var family = face.Family.Replace(' ', '+');
        var italic = face.Italic ? 1 : 0;
        return new Uri(
            $"{CssUrl}?family={family}:ital,wght@{italic},{face.Weight}",
            UriKind.Absolute);
    }

    /// <summary>
    /// The TrueType file a stylesheet points at, or null if it offers none.
    /// </summary>
    /// <remarks>
    /// Deliberately not a CSS parser. The answer is a handful of
    /// <c>@font-face</c> blocks and all that is wanted from them is a URL ending
    /// in a format this can open — so anything else in the file, now or later, is
    /// ignored rather than being something that can go wrong.
    /// </remarks>
    internal static string? ParseCss(string css)
    {
        var at = 0;
        while ((at = css.IndexOf("url(", at, StringComparison.Ordinal)) >= 0)
        {
            at += 4;
            var end = css.IndexOf(')', at);
            if (end < 0) break;
            var url = css[at..end].Trim('\'', '"', ' ');
            if (url.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                || url.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
            at = end;
        }
        return null;
    }

    /// <summary>
    /// The faces a catalogue response describes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read defensively, because this endpoint is not a published contract.</b>
    /// Every field is optional as far as this is concerned: a family with no
    /// styles listed is offered at regular, a family with no licence is offered
    /// and simply cannot be carried in a document, and an entry that makes no
    /// sense at all is skipped. The failure this avoids is the one where Google
    /// adds a field and Lightbox stops being able to set type.
    /// </para>
    /// <para>
    /// Style keys are the compact form the site uses — <c>400</c>, <c>700i</c> —
    /// and the developer API's spelling (<c>regular</c>, <c>700italic</c>) is
    /// accepted too, so that swapping the source of the catalogue later is a
    /// change in one method.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<FontFace> ParseCatalogue(string json)
    {
        var faces = new List<FontFace>();

        // Some Google endpoints prefix JSON with an anti-hijacking guard.
        var start = json.IndexOf('{');
        if (start < 0) return faces;

        try
        {
            using var document = JsonDocument.Parse(json[start..]);
            if (!document.RootElement.TryGetProperty("familyMetadataList", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return faces;
            }

            foreach (var entry in list.EnumerateArray())
            {
                if (!entry.TryGetProperty("family", out var name)
                    || name.ValueKind != JsonValueKind.String
                    || name.GetString() is not { Length: > 0 } family)
                {
                    continue;
                }

                var licence = entry.TryGetProperty("license", out var l) && l.ValueKind == JsonValueKind.String
                    ? Licence(l.GetString())
                    : null;

                var styles = 0;
                if (entry.TryGetProperty("fonts", out var fonts) && fonts.ValueKind == JsonValueKind.Object)
                {
                    foreach (var style in fonts.EnumerateObject())
                    {
                        var (weight, italic) = Style(style.Name);
                        faces.Add(new FontFace(family, weight, italic, FontOrigin.Google, licence));
                        styles++;
                    }
                }

                if (styles == 0)
                {
                    faces.Add(new FontFace(family, 400, false, FontOrigin.Google, licence));
                }
            }
        }
        catch (JsonException)
        {
            return faces;
        }

        faces.Sort(SystemFontSource.Compare);
        return faces;
    }

    /// <summary>A style key as weight and slant. Unreadable keys become regular.</summary>
    private static (int Weight, bool Italic) Style(string key)
    {
        var italic = key.EndsWith('i')
            || key.EndsWith("italic", StringComparison.OrdinalIgnoreCase);
        var digits = new string([.. key.TakeWhile(char.IsDigit)]);
        var weight = digits.Length > 0 && int.TryParse(digits, CultureInfo.InvariantCulture, out var w)
            ? w
            : 400;
        return (Math.Clamp(weight, 1, 1000), italic);
    }

    /// <summary>
    /// The catalogue's licence shorthand as the identifier a document records.
    /// </summary>
    /// <remarks>
    /// Null for anything unrecognised, which makes the font usable and not
    /// carryable — the safe direction. A licence this application has not been
    /// taught about is exactly the case where it must not copy the bytes.
    /// </remarks>
    private static string? Licence(string? name) => name?.ToUpperInvariant() switch
    {
        "OFL" => "OFL-1.1",
        "APACHE2" or "APACHE-2.0" => "Apache-2.0",
        "UFL" => "UFL-1.0",
        _ => null,
    };

    private static string? ReadIfFresh(string path, bool ignoreAge = false)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (!ignoreAge && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > CatalogueLife) return null;
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void Write(string path, string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        catch (IOException)
        {
            // An unwritable cache costs a fetch next time and nothing else.
        }
    }

    public void Dispose() => _http.Dispose();
}
