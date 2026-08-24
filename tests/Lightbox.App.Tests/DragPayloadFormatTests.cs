using System.Text;
using Avalonia.Input;
using Lightbox.App.Services;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Reading a browser drag by what it carries rather than by three format names
/// chosen in advance (B293).
/// </summary>
/// <remarks>
/// Reported as *"sometimes I am able to drag and drop an image but oftentimes
/// Lightbox states: that drop had no picture in it that Lightbox could read"*,
/// with Pinterest as the site. The three names asked for — <c>text/uri-list</c>,
/// <c>text/html</c> and the text member — are an X11 spelling; Windows and macOS
/// browsers spell the same three things differently, and a drag spelled any
/// other way read as empty. It worked when the browser happened to also offer a
/// real file, and not otherwise, which is exactly "sometimes".
///
/// These drive the reading itself through a stand-in payload, so the behaviour
/// is exercised rather than asserted about: the format names below are the ones
/// real browsers use, and the shapes (UTF-16 with a trailing NUL, a CF_HTML
/// header in front of the fragment) are the ones they arrive in.
/// </remarks>
public class DragPayloadFormatTests
{
    // ---- a stand-in for what the platform hands over ------------------------------

    private sealed class FakeItem(Dictionary<DataFormat, object?> values) : IDataTransferItem
    {
        public IReadOnlyList<DataFormat> Formats => values.Keys.ToArray();

        public object? TryGetRaw(DataFormat format) => values.GetValueOrDefault(format);
    }

    /// <summary>An item that refuses everything, the way a stale platform handle does.</summary>
    private sealed class ThrowingItem : IDataTransferItem
    {
        public IReadOnlyList<DataFormat> Formats => [];

        public object? TryGetRaw(DataFormat format) => throw new InvalidOperationException("gone");
    }

    private sealed class FakeTransfer : IDataTransfer
    {
        private readonly Dictionary<DataFormat, object?> _values = [];

        /// <summary>Items in front of the real one — a drag of several things at once.</summary>
        private readonly List<IDataTransferItem> _before = [];

        public IReadOnlyList<DataFormat> Formats => _values.Keys.ToArray();

        public IReadOnlyList<IDataTransferItem> Items => [.. _before, new FakeItem(_values)];

        public FakeTransfer BehindAnItemThatThrows()
        {
            _before.Add(new ThrowingItem());
            return this;
        }

        public void Dispose()
        {
        }

        public FakeTransfer WithBytes(string format, byte[] bytes)
        {
            _values[DataFormat.CreateBytesPlatformFormat(format)] = bytes;
            return this;
        }

        public FakeTransfer WithText(string format, string text)
        {
            _values[DataFormat.CreateStringPlatformFormat(format)] = text;
            return this;
        }

        /// <summary>A format whose value is neither text nor bytes — a file, say.</summary>
        public FakeTransfer WithObject(string format, object value)
        {
            _values[DataFormat.CreateBytesPlatformFormat(format)] = value;
            return this;
        }
    }

    private static byte[] Utf16(string s) => Encoding.Unicode.GetBytes(s + "\0");

    private static byte[] Png(SKColor colour, int w = 20, int h = 12)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(colour);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ---- the addresses ------------------------------------------------------------

    [Fact]
    public void AWindowsBrowserDragIsReadThroughItsOwnFormatNames()
    {
        // What Chrome and Edge put on the clipboard on Windows: the URL as
        // UTF-16 with a trailing NUL under a name nothing here asked for, and
        // CF_HTML, whose fragment sits behind a plain-text header.
        var drag = new FakeTransfer()
            .WithBytes("UniformResourceLocatorW", Utf16("https://i.pinimg.com/originals/pose.jpg"))
            .WithBytes("HTML Format", Encoding.UTF8.GetBytes(
                "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000200\r\n"
                + """<html><body><!--StartFragment--><img src="https://i.pinimg.com/236x/pose.jpg"><!--EndFragment--></body></html>"""));

        var uris = WebImageDrop.ImageUrisIn(drag);

        // Both are found, and the URL format comes first: the HTML fragment on
        // a pin page is the thumbnail, the URL member is the picture.
        Assert.Equal(
            new[] { "https://i.pinimg.com/originals/pose.jpg", "https://i.pinimg.com/236x/pose.jpg" },
            uris.Select(u => u.AbsoluteUri).ToArray());
    }

    [Fact]
    public void AMacBrowserDragIsReadThroughItsOwnFormatNames()
    {
        var drag = new FakeTransfer()
            .WithText("public.url", "https://example.com/img/pose.png")
            .WithText("public.utf8-plain-text", "A pose");

        var uris = WebImageDrop.ImageUrisIn(drag);

        Assert.Single(uris);
        Assert.Equal("https://example.com/img/pose.png", uris[0].AbsoluteUri);
    }

    [Fact]
    public void FirefoxsOwnUrlFormatIsReadAndItsTitleLineIgnored()
    {
        var drag = new FakeTransfer()
            .WithBytes("text/x-moz-url", Utf16("https://example.com/a.png\nA horse"));

        Assert.Single(WebImageDrop.ImageUrisIn(drag));
    }

    [Fact]
    public void AFormatNobodyHasHeardOfStillYieldsItsUrl()
    {
        // The point of the sweep: a name that matches none of the roles is read
        // as plain text rather than skipped, because the cost of being wrong is
        // a candidate that does not fetch.
        var drag = new FakeTransfer().WithText("chromium/x-renderer-taint", "https://example.com/a.png");

        Assert.Single(WebImageDrop.ImageUrisIn(drag));
    }

    [Fact]
    public void ADragCarryingNoAddressYieldsNothing()
    {
        var drag = new FakeTransfer()
            .WithText("public.utf8-plain-text", "a nice picture of a horse")
            .WithObject("FileGroupDescriptorW", new object());

        Assert.Empty(WebImageDrop.ImageUrisIn(drag));
    }

    [Fact]
    public void NothingAtAllIsNotACrash()
    {
        Assert.Empty(WebImageDrop.ImageUrisIn(null));
        Assert.Null(WebImageDrop.EmbeddedImageIn(null));
        Assert.Equal("no data transfer at all", WebImageDrop.DescribeFormats(null));
    }

    // ---- the picture the drag carries itself --------------------------------------

    [Fact]
    public void APictureCarriedInTheDragIsFoundWhateverFormatHoldsIt()
    {
        // Windows FileContents, a virtual file: the bytes are the picture, and
        // no local path exists for the file half to open.
        var png = Png(SKColors.Teal);
        var drag = new FakeTransfer()
            .WithText("UniformResourceLocatorW", "https://example.com/a.png")
            .WithBytes("FileContents", png);

        Assert.Equal(png, WebImageDrop.EmbeddedImageIn(drag));
    }

    [Fact]
    public void BytesThatAreNotAPictureAreNotMistakenForOne()
    {
        var drag = new FakeTransfer()
            .WithBytes("HTML Format", Encoding.UTF8.GetBytes("<html><body>not a picture</body></html>"));

        Assert.Null(WebImageDrop.EmbeddedImageIn(drag));
    }

    // ---- what the log is told -----------------------------------------------------

    [Fact]
    public void TheLogIsToldTheFormatsAndTheirSizes_NeverTheirValues()
    {
        var drag = new FakeTransfer()
            .WithText("UniformResourceLocatorW", "https://example.com/private-moodboard/a.png");

        var described = WebImageDrop.DescribeFormats(drag);

        Assert.Contains("UniformResourceLocatorW", described);
        Assert.Matches(@"\(\d+\)", described);
        // A drag carries the address of whatever the artist was looking at, and
        // this file gets attached to bug reports.
        Assert.DoesNotContain("private-moodboard", described);
    }

    [Fact]
    public void OneItemThatRefusesDoesNotDecideForTheRest()
    {
        // The first item threw and the search stopped there, so the log said
        // "unreadable" over an item that would have read perfectly — degrading
        // the one diagnostic this exists to give.
        var drag = new FakeTransfer()
            .BehindAnItemThatThrows()
            .WithText("UniformResourceLocatorW", "https://example.com/a.png");

        var described = WebImageDrop.DescribeFormats(drag);

        Assert.Matches(@"\(\d+\)", described);
        Assert.DoesNotContain("unreadable", described);
    }

    [Fact]
    public void AnItemThatRefusesDoesNotCostTheDragItsPicture()
    {
        var drag = new FakeTransfer()
            .BehindAnItemThatThrows()
            .WithText("public.url", "https://example.com/a.png");

        Assert.Single(WebImageDrop.ImageUrisIn(drag));
    }

    // ---- the mark leads, it does not trail ---------------------------------------

    [Fact]
    public void AByteOrderMarkInFrontOfTheUrlDoesNotHideIt()
    {
        // Trimmed off the end only, a leading mark stays on and Uri.TryCreate
        // rejects the string — the one candidate is dropped and the drop reads
        // as carrying nothing, which is B293's own symptom inside B293's fix.
        var drag = new FakeTransfer().WithBytes(
            "public.url",
            [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("https://example.com/pose.jpg")]);

        var uris = WebImageDrop.ImageUrisIn(drag);

        Assert.Single(uris);
        Assert.Equal("https://example.com/pose.jpg", uris[0].AbsoluteUri);
    }

    [Fact]
    public void AUtf16UrlWithItsOwnMarkAndNulReadsCleanly()
    {
        var drag = new FakeTransfer().WithBytes(
            "UniformResourceLocatorW",
            [.. Encoding.Unicode.GetPreamble(), .. Utf16("https://example.com/pose.jpg")]);

        Assert.Single(WebImageDrop.ImageUrisIn(drag));
    }
}
