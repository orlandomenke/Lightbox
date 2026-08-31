using System.Net;
using System.Net.Sockets;
using System.Text;
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

    // ---- the picture, or the page it sat on (B344) -------------------------------

    /// <summary>A distinguishable picture, so a test can say which one arrived.</summary>
    private static byte[] PngBytes(byte red)
    {
        using var bmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(red, 10, 10));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static Uri ImageUri(byte[] png) =>
        new("data:image/png;base64," + Convert.ToBase64String(png));

    [Fact]
    public async Task ADirectAddressBeatsAPageThatOnlyNamesOne()
    {
        // B344 in miniature. A drag off a site that wraps its pictures in links
        // carries two addresses: the <a>’s page and the <img>’s picture. The
        // page names a picture of its own — its og:image — and on a site whose
        // pages all share one social card that is the *site’s* graphic rather
        // than this page’s. Resolving the candidates strictly in order pinned
        // the card: Pinterest’s facebook_share_image.png, a collage of stock
        // photographs, in place of the pin the artist dragged.
        var pin = PngBytes(200);
        var siteCard = PngBytes(40);
        var page = PageUri($"""<meta property="og:image" content="{ImageUri(siteCard).OriginalString}">""");

        var got = await WebImageDrop.FetchFirstImageAsync([page, ImageUri(pin)]);

        Assert.NotNull(got);
        Assert.Equal(pin, got.Value.Bytes);
        Assert.False(got.Value.NamedByAPage);
    }

    [Fact]
    public async Task APageIsStillReadWhenNoAddressIsItselfAPicture()
    {
        // B285 stands. The pass order only decides which answer wins where
        // there is a choice, and a drag carrying nothing but a page has none.
        var named = PngBytes(90);
        var page = PageUri($"""<meta property="og:image" content="{ImageUri(named).OriginalString}">""");

        var got = await WebImageDrop.FetchFirstImageAsync([page]);

        Assert.NotNull(got);
        Assert.Equal(named, got.Value.Bytes);
        Assert.True(got.Value.NamedByAPage, "a picture found by reading a page is a guess and must say so");
    }

    [Fact]
    public void ADragOffALinkWrappedPictureListsThePageBeforeThePicture()
    {
        // Why the fix is two fetch passes and not a reordering of this list:
        // this *is* the order a browser hands over. The platform’s URL format
        // carries the anchor’s page, and the picture is only in the HTML
        // fragment. The list is not wrong — resolving it strictly in order was.
        var uris = WebImageDrop.ImageUris(
            "https://www.pinterest.com/pin/1100989440182181227/",
            "https://www.pinterest.com/pin/1100989440182181227/",
            """<img src="https://i.pinimg.com/1200x/0a/e7/0e/0ae70ecdae6a543db6d96a2fef663316.jpg">""");

        Assert.Equal(2, uris.Count);
        Assert.Equal("https://www.pinterest.com/pin/1100989440182181227/", uris[0].AbsoluteUri);
        Assert.Equal(
            "https://i.pinimg.com/1200x/0a/e7/0e/0ae70ecdae6a543db6d96a2fef663316.jpg", uris[1].AbsoluteUri);
    }

    [Fact]
    public async Task TheOrderWithinAPassStandsSoALinkStraightToTheFileBeatsItsThumbnail()
    {
        // The other half of why it is two passes: a gallery whose link points
        // straight at the full-resolution file should still win over the
        // thumbnail it wraps, because both of those are pictures outright.
        var full = PngBytes(220);
        var thumb = PngBytes(60);
        var uris = WebImageDrop.ImageUris(
            ImageUri(full).OriginalString, null, $"""<img src="{ImageUri(thumb).OriginalString}">""");

        var got = await WebImageDrop.FetchFirstImageAsync(uris);

        Assert.NotNull(got);
        Assert.Equal(full, got.Value.Bytes);
        Assert.False(got.Value.NamedByAPage);
    }

    [Fact]
    public async Task ADragWithNoPictureAnywhereResolvesToNothing()
    {
        Assert.Null(await WebImageDrop.FetchFirstImageAsync(
            [PageUri("<html><body>no picture here</body></html>")]));
        Assert.Null(await WebImageDrop.FetchFirstImageAsync([]));
    }

    // ---- the face of a site is not the picture on a page (B344) ------------------

    /// <summary>
    /// A stand-in web site on the loopback interface, so the whole chain — a
    /// page, the front page it shares a card with, and the pictures both name —
    /// can be exercised for real without reaching the network.
    /// </summary>
    /// <remarks>
    /// A raw <see cref="TcpListener"/> rather than <c>HttpListener</c>: the
    /// latter wants a URL reservation on Windows and would make this test a
    /// question of who is running it. Port 0 means the OS picks, so tests never
    /// collide with each other or with anything already listening.
    /// </remarks>
    private sealed class LoopbackSite : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Dictionary<string, (string Type, byte[] Body)> _routes = [];
        private readonly CancellationTokenSource _stop = new();

        public LoopbackSite()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(ServeAsync);
        }

        private int Port { get; }

        public Uri At(string path) => new($"http://127.0.0.1:{Port}{path}");

        public Uri Png(string path, byte[] bytes)
        {
            _routes[path] = ("image/png", bytes);
            return At(path);
        }

        public Uri Page(string path, string html)
        {
            _routes[path] = ("text/html", Encoding.UTF8.GetBytes(html));
            return At(path);
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception)
                {
                    return;
                }
                _ = Task.Run(async () =>
                {
                    using (client) await AnswerAsync(client);
                });
            }
        }

        private async Task AnswerAsync(TcpClient client)
        {
            try
            {
                using var stream = client.GetStream();
                var buffer = new byte[8192];
                var read = await stream.ReadAsync(buffer);
                if (read <= 0) return;
                var parts = Encoding.ASCII.GetString(buffer, 0, read).Split(' ');
                var path = parts.Length > 1 ? parts[1] : "/";
                var known = _routes.TryGetValue(path, out var route);
                var body = known ? route.Body : [];
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {(known ? "200 OK" : "404 Not Found")}\r\n"
                    + $"Content-Type: {(known ? route.Type : "text/plain")}\r\n"
                    + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
            catch (Exception)
            {
                // A test server that cannot answer fails the assertion, not the run.
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }

    [Fact]
    public async Task APageIsNotAnsweredWithTheCardTheSitePutsOnEveryPage()
    {
        // The reported failure, in miniature. Pinterest serves one og:image —
        // a collage under its logo — from its feed, its search results and its
        // boards alike, so reading any of them "for the image it names" answers
        // with the Pinterest logo. The front page's own og:image gives that
        // away, and knowing it needs no list of sites.
        using var site = new LoopbackSite();
        var card = PngBytes(20);
        var real = PngBytes(230);
        var cardAt = site.Png("/card.png", card);
        var realAt = site.Png("/pin.png", real);
        site.Page("/", $"""<meta property="og:image" content="{cardAt}">""");
        var feed = site.Page(
            "/feed", $"""<meta property="og:image" content="{cardAt}"><img src="{realAt}">""");

        WebImageDrop.ForgetSiteCards();
        var got = await WebImageDrop.FetchFirstImageAsync([feed]);

        Assert.NotNull(got);
        Assert.Equal(real, got.Value.Bytes);
        Assert.Equal(realAt, got.Value.Source);
    }

    [Fact]
    public async Task APageWhoseCardIsItsOnlyPictureIsRefusedRatherThanAnswered()
    {
        // When rejecting the card leaves nothing, a refusal is the honest
        // outcome. A wrong picture is worse than none: the artist cannot tell
        // it from Lightbox working, which is how this went unreported for so
        // long and then needed a screenshot.
        using var site = new LoopbackSite();
        var cardAt = site.Png("/card.png", PngBytes(20));
        site.Page("/", $"""<meta property="og:image" content="{cardAt}">""");
        var feed = site.Page("/feed", $"""<meta property="og:image" content="{cardAt}">""");

        WebImageDrop.ForgetSiteCards();

        Assert.Null(await WebImageDrop.FetchFirstImageAsync([feed]));
    }

    [Fact]
    public async Task ASiteWithNoCardOfItsOwnLosesNothing()
    {
        // B285 is untouched where the test does not apply: a front page naming
        // no image means no card is known, so nothing is rejected and the
        // page's og:image answers exactly as it did before.
        using var site = new LoopbackSite();
        var hero = PngBytes(140);
        var heroAt = site.Png("/hero.png", hero);
        site.Page("/", "<html><body>a front page naming nothing</body></html>");
        var post = site.Page("/post", $"""<meta property="og:image" content="{heroAt}">""");

        WebImageDrop.ForgetSiteCards();
        var got = await WebImageDrop.FetchFirstImageAsync([post]);

        Assert.NotNull(got);
        Assert.Equal(hero, got.Value.Bytes);
    }

    [Fact]
    public async Task AFrontPageThatCannotBeReachedRejectsNothing()
    {
        // The failure mode that would be worst: a site whose root 404s must not
        // start refusing every picture its pages name.
        using var site = new LoopbackSite();
        var hero = PngBytes(150);
        var heroAt = site.Png("/hero.png", hero);
        var post = site.Page("/post", $"""<meta property="og:image" content="{heroAt}">""");

        WebImageDrop.ForgetSiteCards();
        var got = await WebImageDrop.FetchFirstImageAsync([post]);

        Assert.NotNull(got);
        Assert.Equal(hero, got.Value.Bytes);
    }
}
