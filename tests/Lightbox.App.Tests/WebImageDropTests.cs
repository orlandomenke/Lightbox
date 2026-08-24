using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A picture dragged out of a browser becomes a reference. The drag arrives
/// as text rather than a file — a uri-list, a bare URL, an HTML fragment or a
/// <c>data:</c> URI, depending on browser and platform — and these pin the
/// parsing of each shape plus the bytes-in import the drop lands on.
/// </summary>
public class WebImageDropTests
{
    // ---- what counts as a candidate ---------------------------------------------

    [Fact]
    public void AUriListYieldsItsUrlsAndSkipsComments()
    {
        var uris = WebImageDrop.ImageUris(
            "# dragged from a browser\r\nhttps://example.com/art/pose.jpg\r\n", null, null);

        Assert.Single(uris);
        Assert.Equal("https://example.com/art/pose.jpg", uris[0].AbsoluteUri);
    }

    [Fact]
    public void PlainTextWithABareUrlIsACandidate_ProseIsNot()
    {
        Assert.Single(WebImageDrop.ImageUris(null, "https://example.com/a.png", null));
        Assert.Empty(WebImageDrop.ImageUris(null, "a nice picture of a horse", null));
    }

    [Fact]
    public void AMozUrlPayloadReadsTheUrlLineAndDropsTheTitleLine()
    {
        // Firefox's text/x-moz-url is "url\ntitle"; the title is not a URI.
        var uris = WebImageDrop.ImageUris(null, "https://example.com/a.png\nA horse", null);

        Assert.Single(uris);
    }

    [Fact]
    public void AnHtmlFragmentYieldsItsImageSource()
    {
        var uris = WebImageDrop.ImageUris(
            null, null, """<meta charset="utf-8"><img alt="pose" src="https://example.com/img/pose.webp?w=800">""");

        Assert.Single(uris);
        Assert.Equal("https://example.com/img/pose.webp?w=800", uris[0].AbsoluteUri);
    }

    [Fact]
    public void TheSamePictureDescribedThreeWaysIsOneCandidate()
    {
        var uris = WebImageDrop.ImageUris(
            "https://example.com/a.png",
            "https://example.com/a.png",
            """<img src="https://example.com/a.png">""");

        Assert.Single(uris);
    }

    [Fact]
    public void ADataImageUriIsACandidate_OtherDataUrisAreNot()
    {
        Assert.Single(WebImageDrop.ImageUris(null, "data:image/png;base64,AAAA", null));
        Assert.Empty(WebImageDrop.ImageUris(null, "data:text/plain;base64,AAAA", null));
    }

    [Fact]
    public void NonWebSchemesAreNotCandidates()
    {
        // A file:// URI in a browser drag is a local page, and local files
        // already have their own door with its own extension filter.
        Assert.Empty(WebImageDrop.ImageUris("file:///home/me/a.png", "javascript:void(0)", null));
    }

    // ---- naming -----------------------------------------------------------------

    [Fact]
    public void TheReferenceIsNamedAfterTheFileInTheUrl()
    {
        Assert.Equal("pose-01", WebImageDrop.NameFor(new Uri("https://example.com/img/pose-01.jpg?w=800")));
        Assert.Equal("two words", WebImageDrop.NameFor(new Uri("https://example.com/two%20words.png")));
    }

    [Fact]
    public void AUrlWithNothingLegibleFallsBackToAPlainName()
    {
        Assert.Equal("Web image", WebImageDrop.NameFor(new Uri("https://example.com/")));
        Assert.Equal("Web image", WebImageDrop.NameFor(new Uri("data:image/png;base64,AAAA")));
    }

    // ---- the bytes --------------------------------------------------------------

    private static byte[] PngBytes()
    {
        using var bmp = new SKBitmap(24, 16, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(200, 40, 40));
        // The slicer needs content on a background to find a frame in.
        for (var y = 4; y < 12; y++)
        for (var x = 6; x < 18; x++)
        {
            bmp.SetPixel(x, y, new SKColor(20, 20, 60));
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void ADataUriRoundTripsItsBytes()
    {
        var bytes = PngBytes();
        var uri = new Uri("data:image/png;base64," + Convert.ToBase64String(bytes));

        Assert.Equal(bytes, WebImageDrop.TryDecodeDataUri(uri));
    }

    [Fact]
    public async Task FetchingADataUriNeedsNoNetwork()
    {
        var bytes = PngBytes();
        var uri = new Uri("data:image/png;base64," + Convert.ToBase64String(bytes));

        Assert.Equal(bytes, await WebImageDrop.FetchAsync(uri));
    }

    [AvaloniaFact]
    public void FetchedBytesImportLikeADroppedFile()
    {
        var vm = VmLayers.PaperVm();

        Assert.True(vm.ImportReferenceImageBytes("pose-01", PngBytes()));

        Assert.True(vm.HasReferences);
        Assert.Equal("pose-01", vm.ActiveReference!.Name);
    }

    [AvaloniaFact]
    public void BytesThatAreNotAnImageImportNothing()
    {
        var vm = VmLayers.PaperVm();

        Assert.False(vm.ImportReferenceImageBytes("broken", [1, 2, 3, 4]));

        Assert.False(vm.HasReferences);
    }

    // ---- a page URL instead of the picture (B285) -------------------------------
    //
    // On any site that wraps its pictures in links — Pinterest, most galleries —
    // the drag carries the *page* URL. The page is where the image's address is
    // written down, so a fetch that does not decode reads it once. Pinned with
    // data: URIs so the whole chain runs without a network.

    private static Uri PageUri(string html) =>
        new("data:text/html;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html)));

    [Fact]
    public void ThePageNamesItsImage_MetadataFirstThenLinkThenImgTags()
    {
        var page = new Uri("https://example.com/pin/1100989440182181227/");
        var uris = WebImageDrop.ImageUrisInPage(
            """
            <html><head>
            <meta property="og:image" content="https://cdn.example.com/originals/pose.jpg"/>
            <meta content="https://cdn.example.com/tw/pose.jpg" name="twitter:image"/>
            <link rel="image_src" href="/legacy/pose.jpg">
            </head><body><img src="thumbs/pose-236x.jpg"><img src="https://cdn.example.com/originals/pose.jpg"></body></html>
            """, page);

        // Metadata first — it is the image the site chose, usually full size —
        // then the rest, relative addresses resolved against the page, all
        // de-duplicated.
        Assert.Equal(
            new[]
            {
                "https://cdn.example.com/originals/pose.jpg",
                "https://cdn.example.com/tw/pose.jpg",
                "https://example.com/legacy/pose.jpg",
                "https://example.com/pin/1100989440182181227/thumbs/pose-236x.jpg",
            },
            uris.Select(u => u.AbsoluteUri).ToArray());
    }

    [Fact]
    public void AttributeOrderAndEntitiesDoNotHideTheImage()
    {
        var uris = WebImageDrop.ImageUrisInPage(
            """<meta content="https://cdn.example.com/a.jpg?w=800&amp;h=600" property="og:image">""",
            new Uri("https://example.com/"));

        Assert.Single(uris);
        Assert.Equal("https://cdn.example.com/a.jpg?w=800&h=600", uris[0].AbsoluteUri);
    }

    [Fact]
    public async Task APageUrlIsResolvedToTheImageItNames()
    {
        // The B285 repro in miniature: the dropped URI fetches, does not
        // decode, and the page it fetched as names the real picture.
        var png = PngBytes();
        var image = "data:image/png;base64," + Convert.ToBase64String(png);
        var page = PageUri(
            $"""<html><head><meta property="og:image" content="{image}"/></head><body>a pin page</body></html>""");

        var got = await WebImageDrop.FetchImageAsync(page);

        Assert.NotNull(got);
        Assert.Equal(png, got.Value.Bytes);
        Assert.Equal(image, got.Value.Source.OriginalString);
    }

    [Fact]
    public async Task ADirectImageStillComesBackAsItself()
    {
        var png = PngBytes();
        var uri = new Uri("data:image/png;base64," + Convert.ToBase64String(png));

        var got = await WebImageDrop.FetchImageAsync(uri);

        Assert.NotNull(got);
        Assert.Equal(png, got.Value.Bytes);
        Assert.Equal(uri, got.Value.Source);
    }

    [Fact]
    public async Task APageThatNamesNoImageResolvesToNothing()
    {
        // One level only: a page naming another *page* must not recurse, and a
        // page naming nothing fails the way a corrupt file does.
        var inner = PageUri("<html><body>still not a picture</body></html>");
        var outer = PageUri($"""<meta property="og:image" content="{inner.OriginalString}">""");

        Assert.Null(await WebImageDrop.FetchImageAsync(outer));
    }

    [Fact]
    public void TheDecoderIsTheJudgeOfWhatIsAnImage()
    {
        Assert.True(WebImageDrop.LooksLikeImage(PngBytes()));
        Assert.False(WebImageDrop.LooksLikeImage(System.Text.Encoding.UTF8.GetBytes("<html>a page</html>")));
    }
}
