using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Input;

namespace Lightbox.App.Services;

/// <summary>
/// The web half of "any image dropped on the window becomes a reference": a
/// picture dragged out of a browser arrives as a URL, a <c>data:</c> URI or an
/// HTML fragment rather than a file, and this turns those into image bytes the
/// ordinary import can decode.
/// </summary>
/// <remarks>
/// <para>
/// Parsing lives apart from the window because it is the half a headless test
/// can hold still: what browsers put in a drag varies by browser and platform
/// (<c>text/uri-list</c> on Linux, plain text on Windows, <c>text/x-moz-url</c>
/// from Firefox, an HTML fragment from most of them), and each shape is a case
/// here rather than a conditional in the drop handler.
/// </para>
/// <para>
/// The fetch deliberately does not filter by file extension: half the images
/// on the web live behind extensionless CDN URLs, and the decoder is the one
/// honest judge of whether bytes are an image. A URL that fetches but does not
/// decode fails exactly like a corrupt file on disk.
/// </para>
/// </remarks>
public static partial class WebImageDrop
{
    /// <summary>
    /// The image candidates in a browser drag, best first: every
    /// <c>data:image</c> or http(s) URI found in the uri-list, the plain text
    /// and the HTML fragment, in that order, de-duplicated.
    /// </summary>
    /// <param name="uriList"><c>text/uri-list</c>: one URI per line, <c>#</c> lines are comments.</param>
    /// <param name="text">The plain-text member, often the bare URL on Windows.</param>
    /// <param name="html">The HTML fragment; the <c>src</c> of its first images.</param>
    public static IReadOnlyList<Uri> ImageUris(string? uriList, string? text, string? html)
    {
        var found = new List<Uri>();

        foreach (var line in Lines(uriList))
        {
            if (!line.StartsWith('#')) Admit(found, line);
        }
        // Firefox's text/x-moz-url ("url\ntitle") reads correctly through the
        // same path: the URL line admits, the title line does not.
        foreach (var line in Lines(text)) Admit(found, line);
        if (html is not null)
        {
            foreach (Match m in ImgSrc().Matches(html))
            {
                Admit(found, System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));
            }
        }
        return found;
    }

    // ---- what the drag actually carries (B294) ------------------------------------

    /// <summary>
    /// The image candidates in a drag, read from <b>every</b> format it carries
    /// rather than from three format names chosen in advance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asking for <c>text/uri-list</c>, plain text and <c>text/html</c> by name
    /// is an X11 spelling, and a browser on another platform spells the same
    /// three things differently: Windows offers <c>UniformResourceLocatorW</c>
    /// and <c>HTML Format</c>, macOS <c>public.url</c> and <c>public.html</c>,
    /// Firefox <c>text/x-moz-url</c>. A drag whose formats were none of the
    /// three read as carrying nothing at all — which is how this was reported:
    /// *"sometimes I am able to drag and drop an image but oftentimes Lightbox
    /// states that drop had no picture in it"*. It worked when a browser
    /// happened to also offer a real file, and not otherwise.
    /// </para>
    /// <para>
    /// So the format list is enumerated and every textual value in it is read.
    /// Identifiers are sorted into the three roles by what their names contain,
    /// because that is the one thing every platform's spelling has in common,
    /// and anything unrecognised is treated as plain text — the bucket whose
    /// only cost is a candidate that does not fetch.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Uri> ImageUrisIn(IDataTransfer? data) =>
        data is null ? [] : ImageUrisFrom(TextValuesIn(data));

    /// <summary>
    /// The same, for a clipboard (B345). A clipboard hands its values over
    /// asynchronously, but it carries the formats a drag carries, so a copied
    /// image address reads exactly like a dropped one — including the rule
    /// that a picture beats the page it sat on (B344).
    /// </summary>
    public static async Task<IReadOnlyList<Uri>> ImageUrisInAsync(IAsyncDataTransfer? data)
    {
        if (data is null) return [];
        var values = new List<(string Format, string Value)>();
        foreach (var format in data.Formats)
        {
            foreach (var item in data.Items)
            {
                object? raw;
                try
                {
                    raw = await item.TryGetRawAsync(format);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // One unreadable format must not cost the paste the others.
                    continue;
                }
                if (AsText(raw) is { Length: > 0 } value) values.Add((format.Identifier, value));
            }
        }
        return ImageUrisFrom(values);
    }

    /// <summary>
    /// The image candidates in whatever formats a transfer turned out to hold,
    /// sorted into the three roles by what each identifier’s name contains
    /// — the one thing every platform’s spelling has in common.
    /// </summary>
    private static IReadOnlyList<Uri> ImageUrisFrom(IEnumerable<(string Format, string Value)> values)
    {
        var urls = new StringBuilder();
        var html = new StringBuilder();
        var text = new StringBuilder();
        foreach (var (format, value) in values)
        {
            var id = format.ToLowerInvariant();
            var bucket = id.Contains("uri") || id.Contains("url") ? urls
                : id.Contains("html") ? html
                : text;
            bucket.Append(value).Append('\n');
        }
        return ImageUris(urls.ToString(), text.ToString(), html.ToString());
    }

    /// <summary>Every format in a drag paired with its value read as text, where it is text at all.</summary>
    public static IEnumerable<(string Format, string Value)> TextValuesIn(IDataTransfer data)
    {
        foreach (var format in data.Formats)
        {
            foreach (var item in data.Items)
            {
                object? raw;
                try
                {
                    raw = item.TryGetRaw(format);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    // One unreadable format must not cost the drag the others.
                    continue;
                }
                if (AsText(raw) is { Length: > 0 } value) yield return (format.Identifier, value);
            }
        }
    }

    /// <summary>Text no bigger than this is read out of a drag; past it the value is not prose.</summary>
    private const int MaxTextValueBytes = 4 * 1024 * 1024;

    /// <summary>
    /// A dragged value as text, or null where it is not text.
    /// </summary>
    /// <remarks>
    /// Bytes are included because the platform formats that carry a URL often
    /// arrive as bytes rather than as a string — <c>UniformResourceLocatorW</c>
    /// is UTF-16 with a trailing NUL, <c>HTML Format</c> is a UTF-8 blob with a
    /// header in front of the fragment. Both read correctly once decoded, and
    /// the <c>&lt;img src&gt;</c> pass does not mind the header.
    /// </remarks>
    private static string? AsText(object? raw) => raw switch
    {
        string s => Tidy(s),
        byte[] { Length: > 0 and <= MaxTextValueBytes } b => Tidy(DecodeText(b)),
        _ => null,
    };

    /// <summary>
    /// The padding a platform wraps text in, off both ends.
    /// </summary>
    /// <remarks>
    /// <b>The byte-order mark leads, it does not trail.</b> Trimming it off the
    /// end only — which this did — leaves <c>"﻿https://…"</c>, which
    /// <see cref="Uri.TryCreate(string, UriKind, out Uri?)"/> rejects, so the
    /// one candidate a Windows or macOS drag carried was silently dropped and
    /// the drop reported carrying nothing: B294's own symptom, reintroduced
    /// inside B294's fix. Found by the adversary pass, not by the tests, which
    /// is what that pass is for. A URL begins with neither a mark nor a NUL, so
    /// both come off both ends.
    /// </remarks>
    private static string Tidy(string text) => text.Trim('\0', '﻿');

    private static string DecodeText(byte[] bytes)
    {
        // UTF-16LE announces itself in ASCII text as a zero in every other
        // byte, which no UTF-8 payload we would want to read looks like.
        var probe = Math.Min(bytes.Length, 64);
        var zeros = 0;
        for (var i = 1; i < probe; i += 2)
        {
            if (bytes[i] == 0) zeros++;
        }
        return zeros > probe / 4
            ? Encoding.Unicode.GetString(bytes)
            : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// The picture a drag carries in its own right — a bitmap, or any member
    /// whose bytes decode — or null.
    /// </summary>
    /// <remarks>
    /// The last resort, and deliberately behind the URLs (B294). What a browser
    /// embeds may be the drag thumbnail rather than the original, and reference
    /// is drawn against, so the full-resolution fetch is worth trying first.
    /// When it fails — a CDN that refuses us, or no network — this is the
    /// difference between a wall with the picture on it and a refusal.
    /// </remarks>
    public static byte[]? EmbeddedImageIn(IDataTransfer? data)
    {
        if (data is null) return null;
        foreach (var format in data.Formats)
        {
            foreach (var item in data.Items)
            {
                object? raw;
                try
                {
                    raw = item.TryGetRaw(format);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    continue;
                }
                switch (raw)
                {
                    case byte[] { Length: > 0 } bytes when LooksLikeImage(bytes):
                        return bytes;
                    case Avalonia.Media.Imaging.Bitmap bitmap:
                        if (AsPng(bitmap) is { } png) return png;
                        break;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// A clipboard or drag bitmap as PNG bytes, or null where it will not
    /// encode. Internal because the paste door needs it too (B350): what the
    /// platform hands over for a screenshot is a decoded bitmap, and every
    /// import below this takes bytes.
    /// </summary>
    internal static byte[]? AsPng(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            return stream.Length > 0 ? stream.ToArray() : null;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a drag was carrying, for the diagnostics log — the format names and
    /// how big each value was, never the values themselves.
    /// </summary>
    /// <remarks>
    /// The format names are the whole diagnosis when a drop reads as empty, and
    /// they are what no bug report can supply from memory. The values are left
    /// out on purpose: a drag carries the address of whatever the artist was
    /// looking at, and a log is a file that gets attached to bug reports.
    /// </remarks>
    public static string DescribeFormats(IDataTransfer? data)
    {
        if (data is null) return "no data transfer at all";
        var parts = new List<string>();
        foreach (var format in data.Formats)
        {
            // The best answer any item gives, not the first item's (B294). An
            // item that throws used to end the search and report "unreadable"
            // over a later item that would have read perfectly — degrading the
            // one diagnostic this exists to provide.
            var size = -1;
            foreach (var item in data.Items)
            {
                int found;
                try
                {
                    found = item.TryGetRaw(format) switch
                    {
                        string s => s.Length,
                        byte[] b => b.Length,
                        null => -1,
                        _ => -2,
                    };
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    found = -3;
                }
                // Larger is better, and a real size beats every complaint.
                if (found >= 0)
                {
                    size = found;
                    break;
                }
                if (size == -1) size = found;
            }
            parts.Add(size switch
            {
                -1 => $"{format.Identifier} (empty)",
                -2 => $"{format.Identifier} (an object)",
                -3 => $"{format.Identifier} (unreadable)",
                _ => $"{format.Identifier} ({size})",
            });
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "no formats at all";
    }

    private static IEnumerable<string> Lines(string? text) =>
        text is null
            ? []
            : text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void Admit(List<Uri> found, string candidate)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return;
        var keep = uri.Scheme is "http" or "https"
            || (uri.Scheme == "data" && uri.AbsolutePath.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
        if (keep && !found.Contains(uri)) found.Add(uri);
    }

    [GeneratedRegex("""<img[^>]+src\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrc();

    /// <summary>
    /// The images a fetched page names, best first: <c>og:image</c> and
    /// <c>twitter:image</c> metadata, then <c>&lt;link rel="image_src"&gt;</c>,
    /// then every <c>&lt;img src&gt;</c>, resolved against the page's own URL.
    /// </summary>
    /// <remarks>
    /// This is the answer to a URL that fetches but does not decode (B285): on
    /// any site that wraps its pictures in links — Pinterest, most galleries —
    /// the drag carries the <em>page</em> URL, and the page is where the image's
    /// real address is written down. The metadata comes first because it is the
    /// one the site chose to represent the page, usually at full resolution,
    /// where the <c>&lt;img&gt;</c> tags are thumbnails and chrome.
    /// </remarks>
    public static IReadOnlyList<Uri> ImageUrisInPage(string html, Uri page)
    {
        var found = new List<Uri>(MetaImagesInPage(html, page));
        foreach (Match m in LinkTag().Matches(html))
        {
            if (string.Equals(AttrValue(m.Value, "rel"), "image_src", StringComparison.OrdinalIgnoreCase))
            {
                AdmitOnPage(found, page, AttrValue(m.Value, "href"));
            }
        }
        foreach (Match m in ImgSrc().Matches(html))
        {
            AdmitOnPage(found, page, m.Groups[1].Value);
        }
        return found;
    }

    /// <summary>Only the images a page names in its <c>&lt;meta&gt;</c> tags, in order.</summary>
    private static List<Uri> MetaImagesInPage(string html, Uri page)
    {
        var found = new List<Uri>();
        foreach (Match m in MetaTag().Matches(html))
        {
            var key = AttrValue(m.Value, "property") ?? AttrValue(m.Value, "name");
            if (key?.ToLowerInvariant()
                is "og:image" or "og:image:secure_url" or "twitter:image" or "twitter:image:src")
            {
                AdmitOnPage(found, page, AttrValue(m.Value, "content"));
            }
        }
        return found;
    }

    private static void AdmitOnPage(List<Uri> found, Uri page, string? candidate)
    {
        if (candidate is null) return;
        var text = System.Net.WebUtility.HtmlDecode(candidate).Trim();
        // Already an address: kept verbatim, because a data: URI's base64 does
        // not survive being rewritten. Anything else is resolved against the
        // page, so a relative src is still an address.
        if (Uri.TryCreate(text, UriKind.Absolute, out var abs) && abs.Scheme is "http" or "https" or "data")
        {
            Admit(found, text);
        }
        else if (Uri.TryCreate(page, text, out var resolved))
        {
            Admit(found, resolved.AbsoluteUri);
        }
    }

    /// <summary>An attribute's value inside one tag's text, or null.</summary>
    private static string? AttrValue(string tag, string attribute)
    {
        var m = Regex.Match(tag, $"""(?:^|\s){attribute}\s*=\s*["']([^"']*)["']""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("<meta[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex("<link[^>]+>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    /// <summary>
    /// A reference name from the URL — the file name without its extension,
    /// URL-decoded, or a plain fallback for URLs with nothing legible in them.
    /// </summary>
    public static string NameFor(Uri uri)
    {
        if (uri.Scheme == "data") return "Web image";
        var last = uri.AbsolutePath.TrimEnd('/').Split('/')[^1];
        var name = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(last)).Trim();
        return name.Length > 0 ? name : "Web image";
    }

    /// <summary>The bytes of a <c>data:image/…;base64,</c> URI, or null.</summary>
    public static byte[]? TryDecodeDataUri(Uri uri)
    {
        if (uri.Scheme != "data") return null;
        var raw = uri.OriginalString;
        var comma = raw.IndexOf(',');
        if (comma < 0 || !raw[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return Convert.FromBase64String(raw[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Bytes a fetch refuses to exceed. A reference is a picture, not an
    /// archive; past this the download is more likely a mistake than an image.
    /// </summary>
    public const long MaxFetchBytes = 64 * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
            MaxResponseContentBufferSize = MaxFetchBytes,
        };
        // Some image hosts refuse clientless requests outright; a plain,
        // honest product token is all they are asking for.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lightbox/1.0");
        return client;
    }

    /// <summary>
    /// The bytes behind a dropped URI: decoded in place for a <c>data:</c>
    /// URI, fetched for http(s). Null for anything that could not be got —
    /// the caller's answer is the same whatever the reason, and the reason is
    /// for the status line.
    /// </summary>
    public static async Task<byte[]?> FetchAsync(Uri uri)
    {
        if (TryDecodeDataUri(uri) is { } inline) return inline;
        if (uri.Scheme is not ("http" or "https")) return null;
        try
        {
            return await Http.GetByteArrayAsync(uri);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Whether these bytes open as a picture — the decoder's answer, not a name's.</summary>
    public static bool LooksLikeImage(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var codec = SkiaSharp.SKCodec.Create(stream);
        return codec is not null;
    }

    /// <summary>How many of a page's image candidates a resolution will try before giving up.</summary>
    /// <remarks>The metadata candidate is nearly always the first; this bounds a page of thumbnails.</remarks>
    public const int MaxPageCandidates = 8;

    /// <summary>Pages bigger than this are not read for their image — a page is text, not an archive.</summary>
    public const int MaxPageParseBytes = 8 * 1024 * 1024;

    /// <summary>
    /// The picture behind a single dropped URI, with the address it was finally
    /// found at — an address that is a picture, or failing that the picture a
    /// page at that address names (B285).
    /// </summary>
    public static async Task<(byte[] Bytes, Uri Source)?> FetchImageAsync(Uri uri) =>
        await FetchFirstImageAsync([uri]) is { } got ? (got.Bytes, got.Source) : null;

    /// <summary>How many of a drag’s candidate addresses are fetched before it gives up.</summary>
    /// <remarks>A drag carries one picture described a few ways; this bounds a pathological one.</remarks>
    public const int MaxDropCandidates = 8;

    /// <summary>
    /// A drag’s addresses, fetched as far as the point where the caller gets
    /// to weigh what the drag was carrying <em>itself</em> against what a page
    /// merely names (B344).
    /// </summary>
    public sealed class WebImageSearch
    {
        /// <summary>The picture found at one of the addresses outright, or null.</summary>
        public (byte[] Bytes, Uri Source)? Direct { get; init; }

        /// <summary>Pages met on the way, which may yet name a picture.</summary>
        internal IReadOnlyList<(byte[]? Kept, Uri At)> Pages { get; init; } = [];

        /// <summary>Whether any address turned out to be a page worth reading.</summary>
        public bool AnyPages => Pages.Count > 0;
    }

    /// <summary>
    /// Every address a drag carried, tried as a picture — and the pages met on
    /// the way, kept for a caller that decides to read them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An address that <em>is</em> a picture beats one that merely names one
    /// (B344).</b> A drag off a site that wraps its images in links carries
    /// both: the <c>&lt;a&gt;</c>’s page URL in the platform’s URL format, and
    /// the <c>&lt;img&gt;</c>’s real address in the HTML fragment. Resolving them
    /// strictly in order fetched the page first, B285’s page read answered it
    /// with <c>og:image</c>, that decoded, and the real picture was never tried.
    /// </para>
    /// <para>
    /// Within this pass the original order stands, because it is still the
    /// better guess: a gallery whose link points straight at the
    /// full-resolution file should beat the thumbnail it wraps, and both of
    /// those are pictures outright.
    /// </para>
    /// </remarks>
    public static async Task<WebImageSearch> SearchAddressesAsync(IReadOnlyList<Uri> candidates)
    {
        // A page met here is kept rather than re-fetched later: the bytes are
        // already in hand. What is *held* is capped at one page’s worth in
        // total, so a drag naming several large pages cannot cost more memory
        // than reading a single page always did; past the cap the address is
        // remembered and fetched again if it is ever reached, which costs a
        // fetch and never a wrong answer.
        var pages = new List<(byte[]? Kept, Uri At)>();
        var held = 0L;
        var tried = 0;
        foreach (var uri in candidates)
        {
            if (++tried > MaxDropCandidates) break;
            if (await FetchAsync(uri) is not { } bytes) continue;
            if (LooksLikeImage(bytes)) return new WebImageSearch { Direct = (bytes, uri), Pages = pages };
            if (bytes.Length > MaxPageParseBytes) continue;
            var room = held + bytes.Length <= MaxPageParseBytes;
            if (room) held += bytes.Length;
            pages.Add((room ? bytes : null, uri));
        }
        return new WebImageSearch { Pages = pages };
    }

    /// <summary>
    /// The picture a page named, for a search that found none at an address
    /// outright (B285). The <em>last</em> resort, behind even the picture the
    /// drag was carrying itself — see the remarks.
    /// </summary>
    /// <remarks>
    /// <b>This is a guess and the others are not (B344).</b> <c>og:image</c> is
    /// a page’s answer to “what is this page about”, which on a great many
    /// sites is one social card reused on every page. It is worth reading when
    /// there is nothing else — that is B285 — and it must not outrank a picture
    /// the artist was actually pointing at.
    /// </remarks>
    public static async Task<(byte[] Bytes, Uri Source)?> ImageNamedByAPageAsync(WebImageSearch search)
    {
        foreach (var (kept, at) in search.Pages)
        {
            if ((kept ?? await FetchAsync(at)) is not { } page) continue;
            if (await ImageNamedByPageAsync(page, at) is { } named) return named;
        }
        return null;
    }

    /// <summary>
    /// The picture a drag was pointing at, addresses first and the page read
    /// last. For callers with nothing of their own to weigh in between.
    /// </summary>
    public static async Task<(byte[] Bytes, Uri Source, bool NamedByAPage)?> FetchFirstImageAsync(
        IReadOnlyList<Uri> candidates)
    {
        var search = await SearchAddressesAsync(candidates);
        if (search.Direct is { } direct) return (direct.Bytes, direct.Source, false);
        if (await ImageNamedByAPageAsync(search) is { } named) return (named.Bytes, named.Source, true);
        return null;
    }

    /// <summary>
    /// The best image a fetched page names, fetched and decoded (B285), or null.
    /// One level only, never a page named by a page — and never the card the
    /// site puts on every page (B344).
    /// </summary>
    private static async Task<(byte[] Bytes, Uri Source)?> ImageNamedByPageAsync(byte[] page, Uri at)
    {
        var card = await SiteCardAsync(at);
        var tried = 0;
        foreach (var candidate in ImageUrisInPage(Encoding.UTF8.GetString(page), at))
        {
            if (candidate == at) continue;
            // The face of the whole site is not the picture on this page.
            if (card is not null && candidate == card) continue;
            if (++tried > MaxPageCandidates) break;
            var inner = await FetchAsync(candidate);
            if (inner is not null && LooksLikeImage(inner)) return (inner, candidate);
        }
        return null;
    }

    /// <summary>What each host puts on its own front page, looked up once and remembered.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<Uri?>> SiteCards =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The picture a site serves as the face of <em>every</em> page on it, or
    /// null where it has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a site’s front page is worth one fetch (B344).</b> A page’s
    /// <c>og:image</c> is what that page says it is about, and a great many
    /// sites answer that the same way everywhere: one social card, the logo
    /// over some stock photographs. Pinterest is the plain case — its feed, its
    /// search results and its boards all name
    /// <c>s.pinimg.com/images/facebook_share_image.png</c>, so reading any of
    /// them “for the image it names” yields the Pinterest logo rather than
    /// anything the artist was looking at. That collage is what was reported.
    /// </para>
    /// <para>
    /// The front page’s own <c>og:image</c> is the cheap general test, and it
    /// needs no list of sites: an image that is also the front page’s is a card
    /// about the <em>site</em>, so it cannot be the picture on this page. A site
    /// whose front page names nothing, or cannot be reached, simply has no card
    /// and nothing is rejected — the answer is then exactly what it was before.
    /// </para>
    /// <para>
    /// One fetch per host, remembered for the session, and only ever on the
    /// page-reading path, which is already the slow one and already a guess.
    /// </para>
    /// </remarks>
    private static Task<Uri?> SiteCardAsync(Uri page) =>
        SiteCards.GetOrAdd(page.GetLeftPart(UriPartial.Authority), FetchSiteCardAsync);

    private static async Task<Uri?> FetchSiteCardAsync(string authority)
    {
        if (!Uri.TryCreate(authority + "/", UriKind.Absolute, out var root)) return null;
        if (await FetchAsync(root) is not { } bytes) return null;
        if (bytes.Length > MaxPageParseBytes || LooksLikeImage(bytes)) return null;
        var meta = MetaImagesInPage(Encoding.UTF8.GetString(bytes), root);
        return meta.Count > 0 ? meta[0] : null;
    }

    /// <summary>Forget the remembered site cards. Tests only.</summary>
    internal static void ForgetSiteCards() => SiteCards.Clear();
}
