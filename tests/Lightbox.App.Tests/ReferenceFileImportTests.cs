using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The file half of importing a reference — the piece both the picker and a
/// drag-and-drop onto the window go through. The window's drop handler is a
/// thin adapter over this (extension routing plus <c>TryGetFiles</c>), which
/// is why the test lives at the view-model boundary.
/// </summary>
public class ReferenceFileImportTests
{
    private static string TempImage(int width = 40, int height = 20)
    {
        // Content on a plain ground, not a uniform colour: the slicer reads
        // the background off the corners, so a flat image is all background
        // and yields nothing.
        using var bmp = new SKBitmap(width, height);
        bmp.Erase(SKColors.White);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = new SKColor(200, 60, 60) })
        {
            canvas.DrawRect(SKRect.Create(8, 4, width - 16, height - 8), paint);
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-ref-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    [AvaloniaFact]
    public void AnImageFile_BecomesAnEmbeddedReference_NamedAfterTheFile()
    {
        var vm = VmLayers.PaperVm();
        var path = TempImage();
        try
        {
            Assert.True(vm.ImportReferenceImageFile(path));
            var strip = Assert.Single(vm.References);
            Assert.Equal(Path.GetFileNameWithoutExtension(path), strip.Name);
            // Embedded, not linked: the document must survive the file moving.
            Assert.NotEmpty(strip.Png);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void AFileThatIsNotAnImage_IsRefused_AndAddsNothing()
    {
        var vm = VmLayers.PaperVm();
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-ref-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "not a png at all");
        try
        {
            Assert.False(vm.ImportReferenceImageFile(path));
            Assert.Empty(vm.References);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
