using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.Docking;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The Channels panel: red, green, blue and alpha of what the canvas shows,
/// each viewable alone. Solo is view-only — the same side of the line as zoom
/// and mirror — so nothing here touches the document.
/// </summary>
public class ChannelsDockerTests(ITestOutputHelper output)
{
    // ---- the filters, at the pixel ----------------------------------------

    /// <summary>Every solo turns a mixed colour into that channel's gray.</summary>
    [AvaloniaTheory]
    [InlineData(ChannelSolo.Red, 40)]
    [InlineData(ChannelSolo.Green, 80)]
    [InlineData(ChannelSolo.Blue, 120)]
    public void AColourSoloReadsThatChannelAsGray(ChannelSolo solo, byte expected)
    {
        var got = Filtered(new SKColor(40, 80, 120, 255), solo);
        output.WriteLine($"{solo}: {got}");
        Assert.Equal(expected, got.Red);
        Assert.Equal(expected, got.Green);
        Assert.Equal(expected, got.Blue);
        Assert.Equal(255, got.Alpha);
    }

    [AvaloniaFact]
    public void AlphaSoloShowsCoverageAsOpaqueGray()
    {
        // Transparent is black, solid is white — and the result itself is
        // opaque, or the solo would fade out exactly what it is showing.
        var half = Filtered(new SKColor(200, 10, 10, 128), ChannelSolo.Alpha);
        output.WriteLine($"alpha 128 reads {half}");
        Assert.Equal(255, half.Alpha);
        Assert.InRange(half.Red, 120, 136);
        Assert.Equal(half.Red, half.Green);
        Assert.Equal(half.Red, half.Blue);
    }

    [AvaloniaFact]
    public void NoSoloMeansNoFilterAtAll()
    {
        // None must cost nothing: the paint gets a null filter, not an
        // identity matrix evaluated per pixel.
        Assert.Null(ChannelSoloFilters.For(ChannelSolo.None));
    }

    private static SKColor Filtered(SKColor source, ChannelSolo solo)
    {
        using var bmp = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = source, ColorFilter = ChannelSoloFilters.For(solo) };
        canvas.DrawRect(0, 0, 1, 1, paint);
        return bmp.GetPixel(0, 0);
    }

    // ---- the view model ----------------------------------------------------

    [AvaloniaFact]
    public void SoloIsOneClickInAndOneClickOut()
    {
        var vm = new MainViewModel(null);
        vm.ToggleChannelSoloCommand.Execute(ChannelSolo.Red);
        Assert.Equal(ChannelSolo.Red, vm.ChannelSolo);
        Assert.True(vm.IsRedSolo);

        vm.ToggleChannelSoloCommand.Execute(ChannelSolo.Green);
        Assert.Equal(ChannelSolo.Green, vm.ChannelSolo);

        vm.ToggleChannelSoloCommand.Execute(ChannelSolo.Green);
        Assert.Equal(ChannelSolo.None, vm.ChannelSolo);
    }

    // ---- through the window -------------------------------------------------

    [AvaloniaFact]
    public void TheCanvasHearsTheSoloAndTheDrawingDoesNot()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        var before = vm.SerializeDocument();

        vm.ChannelSolo = ChannelSolo.Blue;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var canvas = window.GetVisualDescendants().OfType<CanvasControl>().First();
        Assert.Equal(ChannelSolo.Blue, canvas.Solo);
        // View-only, invariant 5: the record is byte-identical.
        Assert.Equal(before, vm.SerializeDocument());
    }

    [AvaloniaFact]
    public void OpeningThePanelDrawsItsThumbnails()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;

        // In the default layout the panel ships as a colour-family tab, so it
        // is already visible; hide it first to prove the open is the trigger.
        vm.Workspace.SetVisible(DockPanelId.Channels, false);
        vm.ChannelThumbRed = null;
        vm.Workspace.ChannelsDockerVisible = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(vm.ChannelThumbRed);
        Assert.NotNull(vm.ChannelThumbAlpha);
    }
}
