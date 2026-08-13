using System.Net.Http;
using System.Text.RegularExpressions;

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
}
