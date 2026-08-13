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
}
