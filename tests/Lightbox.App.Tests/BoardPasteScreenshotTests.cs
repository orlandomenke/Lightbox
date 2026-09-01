using System.Text.RegularExpressions;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Pasting a screenshot onto the reference board (B350).
/// </summary>
/// <remarks>
/// The board asked the clipboard for three image formats by name —
/// <c>image/png</c>, <c>image/jpeg</c>, <c>image/bmp</c> — through
/// <see cref="DataFormat.CreateBytesPlatformFormat"/>, whose own documentation
/// says the identifier "is passed AS IS to the underlying platform". Those are
/// freedesktop spellings. On Windows a screenshot is a device independent
/// bitmap and there is no clipboard format called <c>image/png</c>, so the
/// commonest paste there is matched nothing and said the clipboard was empty.
///
/// <see cref="DataFormat.Bitmap"/> is Avalonia's <em>universal</em> image
/// format — "cross-platform and supported directly by Avalonia" — and is the
/// one that resolves per platform. These pin that the board asks for it, that
/// it is asked for first, and that the bytes it yields survive the trip.
/// </remarks>
public class BoardPasteScreenshotTests
{
    private static string BoardWindowSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", "Views", "ReferenceBoardWindow.cs"));
    }

    private static string PasteSource()
    {
        var paste = Regex.Match(
            BoardWindowSource(), @"private async Task PasteImageAsync\(\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(paste.Success, "PasteImageAsync has moved — these guards need to follow it");
        return paste.Groups[1].Value;
    }

    [Fact]
    public void ThePasteAsksForAvaloniasOwnImageFormat()
    {
        // The whole bug in one assertion: without this the board only ever
        // asked for freedesktop MIME names, which a Windows screenshot is not.
        Assert.Contains("DataFormat.Bitmap", PasteSource());
    }

    [Fact]
    public void TheUniversalFormatIsAskedForBeforeThePlatformNamedOnes()
    {
        // Order matters and is not cosmetic. The MIME names are kept for X11,
        // where the bytes arrive already encoded — but a platform format is a
        // string handed over verbatim, so it can never be the primary question.
        var paste = PasteSource();

        var universal = paste.IndexOf("DataFormat.Bitmap", StringComparison.Ordinal);
        var named = paste.IndexOf("ClipboardImageFormats", StringComparison.Ordinal);

        Assert.True(universal >= 0 && named >= 0, "both routes must still be there");
        Assert.True(universal < named, "the universal format is the question; the MIME names are the fallback");
    }

    [Fact]
    public void ACopiedFileStillBeatsAPictureOnTheClipboard()
    {
        // A copied file keeps the picture's name, so it stays first — the new
        // route must not have jumped the queue ahead of it.
        var paste = PasteSource();

        Assert.True(
            paste.IndexOf("DataFormat.File", StringComparison.Ordinal)
                < paste.IndexOf("DataFormat.Bitmap", StringComparison.Ordinal),
            "a copied file names the picture and must still be tried first");
    }

    [AvaloniaFact]
    public void AnEncodeThisPlatformCannotDoIsNullRatherThanAThrow()
    {
        // The other half of the paste: DataFormat.Bitmap yields a decoded
        // Bitmap and every import below takes bytes, so an encode bridges them.
        //
        // **This suite cannot prove the encode works, and that is the harness
        // rather than the code.** TestAppBuilder runs
        // `new AvaloniaHeadlessPlatformOptions()`, whose UseHeadlessDrawing
        // defaults to true — "disable this option if you are using
        // Avalonia.Skia or another drawing backend" — so there is no encoder
        // behind Bitmap.Save here and it cannot return bytes. On a real
        // backend it does. Asserting bytes would be asserting about Xvfb.
        //
        // What *is* worth pinning is the failure mode, because it is the one
        // that decides whether a paste survives a platform that will not
        // encode: null, never an exception. A throw here would take down the
        // whole paste — and this is also the fallback a drag reaches for when
        // no address works (B294, promoted ahead of the page read by B344), so
        // it runs on the unhappy path by definition.
        using var bitmap = ClipboardShapedBitmap();

        var png = WebImageDrop.AsPng(bitmap);

        Assert.True(png is null || WebImageDrop.LooksLikeImage(png),
            "an encode either yields a picture or yields nothing — never bytes that are neither");
    }

    /// <summary>A bitmap shaped like what a screenshot puts on the clipboard.</summary>
    private static Avalonia.Media.Imaging.Bitmap ClipboardShapedBitmap()
    {
        using var bmp = new SKBitmap(32, 24, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(90, 140, 210));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Avalonia.Media.Imaging.Bitmap(stream);
    }
}
