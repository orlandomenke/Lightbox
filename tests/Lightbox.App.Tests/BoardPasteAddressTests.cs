using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Input;
using Lightbox.App.Services;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Pasting an image <em>address</em> onto the reference board (B345).
/// </summary>
/// <remarks>
/// The board took a copied file and a copied picture and refused a copied
/// address — which is what "Copy image address" puts on the clipboard, and what
/// someone reaches for once a drag has already failed them. The whole fetch
/// already existed for drops; the clipboard simply was never read for one. A
/// clipboard carries the formats a drag carries, so the same reader answers
/// both — these drive that reader through a stand-in payload.
/// </remarks>
public class BoardPasteAddressTests
{
    // ---- a stand-in for what a clipboard hands over -------------------------------

    private sealed class FakeAsyncItem(Dictionary<DataFormat, object?> values) : IAsyncDataTransferItem
    {
        public IReadOnlyList<DataFormat> Formats => values.Keys.ToArray();

        public Task<object?> TryGetRawAsync(DataFormat format) =>
            Task.FromResult(values.GetValueOrDefault(format));
    }

    /// <summary>An item that refuses everything, the way a stale platform handle does.</summary>
    private sealed class ThrowingAsyncItem : IAsyncDataTransferItem
    {
        public IReadOnlyList<DataFormat> Formats => [];

        public Task<object?> TryGetRawAsync(DataFormat format) =>
            throw new InvalidOperationException("gone");
    }

    private sealed class FakeClipboard : IAsyncDataTransfer
    {
        private readonly Dictionary<DataFormat, object?> _values = [];
        private readonly List<IAsyncDataTransferItem> _before = [];

        public IReadOnlyList<DataFormat> Formats => _values.Keys.ToArray();

        public IReadOnlyList<IAsyncDataTransferItem> Items => [.. _before, new FakeAsyncItem(_values)];

        public void Dispose()
        {
        }

        public FakeClipboard BehindAnItemThatThrows()
        {
            _before.Add(new ThrowingAsyncItem());
            return this;
        }

        public FakeClipboard WithText(string format, string text)
        {
            _values[DataFormat.CreateStringPlatformFormat(format)] = text;
            return this;
        }

        public FakeClipboard WithBytes(string format, byte[] bytes)
        {
            _values[DataFormat.CreateBytesPlatformFormat(format)] = bytes;
            return this;
        }
    }

    private static byte[] PngBytes()
    {
        using var bmp = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(180, 30, 30));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // ---- reading the clipboard ---------------------------------------------------

    [Fact]
    public async Task ACopiedImageAddressIsACandidate()
    {
        // What "Copy image address" leaves behind: the bare URL, as plain text.
        var clipboard = new FakeClipboard().WithText(
            "text/plain", "https://i.pinimg.com/1200x/0a/e7/0e/0ae70ecdae6a543db6d96a2fef663316.jpg");

        var uris = await WebImageDrop.ImageUrisInAsync(clipboard);

        Assert.Single(uris);
        Assert.Equal(
            "https://i.pinimg.com/1200x/0a/e7/0e/0ae70ecdae6a543db6d96a2fef663316.jpg", uris[0].AbsoluteUri);
    }

    [Fact]
    public async Task TheClipboardIsReadByWhatItCarriesNotByAFormatNameChosenInAdvance()
    {
        // The same lesson as B294, on the other door. Windows spells this
        // UniformResourceLocatorW and hands it over as UTF-16 with a trailing
        // NUL; asking for "text/plain" by name would find nothing here.
        var url = "https://example.com/art/pose.jpg";
        var clipboard = new FakeClipboard()
            .BehindAnItemThatThrows()
            .WithBytes("UniformResourceLocatorW", Encoding.Unicode.GetBytes(url + "\0"));

        var uris = await WebImageDrop.ImageUrisInAsync(clipboard);

        Assert.Single(uris);
        Assert.Equal(url, uris[0].AbsoluteUri);
    }

    [Fact]
    public async Task AClipboardWithNothingAddressableYieldsNothing()
    {
        Assert.Empty(await WebImageDrop.ImageUrisInAsync(
            new FakeClipboard().WithText("text/plain", "a nice picture of a horse")));
        Assert.Empty(await WebImageDrop.ImageUrisInAsync(null));
    }

    [Fact]
    public async Task ACopiedAddressFetchesTheSameWayADroppedOneDoes()
    {
        var png = PngBytes();
        var clipboard = new FakeClipboard().WithText(
            "text/plain", "data:image/png;base64," + Convert.ToBase64String(png));

        var uris = await WebImageDrop.ImageUrisInAsync(clipboard);
        var got = await WebImageDrop.FetchFirstImageAsync(uris);

        Assert.NotNull(got);
        Assert.Equal(png, got.Value.Bytes);
    }

    // ---- and the paste actually reaches it ---------------------------------------

    [Fact]
    public void PastingFallsBackToAnAddressWhenNoPictureIsOnTheClipboard()
    {
        var paste = Regex.Match(
            BoardWindowSource(), @"private async Task PasteImageAsync\(\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(paste.Success, "PasteImageAsync has moved — this guard needs to follow it");
        var body = paste.Groups[1].Value;
        // A copied file and a copied picture still come first; the address is
        // the fallback, and it goes through the same reader a drop uses.
        Assert.Contains("ImageUrisInAsync", body);
        Assert.Contains("FetchFirstImageAsync", body);
        Assert.True(
            body.IndexOf("ClipboardImageFormats", StringComparison.Ordinal)
                < body.IndexOf("ImageUrisInAsync", StringComparison.Ordinal),
            "the picture on the clipboard must still beat the address on it");
    }

    private static string BoardWindowSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", "Views", "ReferenceBoardWindow.cs"));
    }
}
