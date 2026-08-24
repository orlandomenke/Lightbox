using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The choices in <c>File ▸ Save as image…</c>, and the window that shows them.
/// </summary>
public class SaveImageDialogTests(Xunit.ITestOutputHelper output)
{
    private static Scene Painting(int width = 100, int height = 50, int frames = 1) => new()
    {
        Width = width,
        Height = height,
        FrameCount = frames,
        TransparentBackground = true,
    };

    // ---- what the format decides ----------------------------------------------

    [Fact]
    public void PngIsTheDefaultBecauseItIsTheOneThatLosesNothing()
    {
        var vm = new SaveImageDialogViewModel(Painting());

        Assert.Equal(ImageSaveFormat.Png, vm.Format);
        Assert.True(vm.KeepsTransparency);
        Assert.False(vm.HasQuality);
    }

    [Fact]
    public void QualityAppearsForLossyFormatsOnly()
    {
        var vm = new SaveImageDialogViewModel(Painting());

        Assert.False(vm.HasQuality);
        vm.Format = ImageSaveFormat.Jpeg;
        Assert.True(vm.HasQuality);
        vm.Format = ImageSaveFormat.Webp;
        Assert.True(vm.HasQuality);
        vm.Format = ImageSaveFormat.Png;
        Assert.False(vm.HasQuality);
    }

    [Fact]
    public void ChoosingJpegOnATransparentDocumentWarnsBeforeTheSave()
    {
        // The roadmap item's own sentence: "somebody exports a character on a
        // white box and finds out later". This is the "not later" part.
        var vm = new SaveImageDialogViewModel(Painting());

        Assert.False(vm.MayLoseTransparency);
        vm.Format = ImageSaveFormat.Jpeg;
        Assert.True(vm.MayLoseTransparency);
    }

    [Fact]
    public void WebpWarnsAboutNothingBecauseItKeepsAlpha()
    {
        var vm = new SaveImageDialogViewModel(Painting()) { Format = ImageSaveFormat.Webp };

        Assert.True(vm.KeepsTransparency);
        Assert.False(vm.MayLoseTransparency);
    }

    [Fact]
    public void AnOpaqueDocumentDoesNotWarnAboutJpeg()
    {
        var scene = Painting();
        scene.TransparentBackground = false;

        var vm = new SaveImageDialogViewModel(scene) { Format = ImageSaveFormat.Jpeg };

        Assert.False(vm.MayLoseTransparency);
    }

    [Fact]
    public void ADocumentWithAPaperLayerCountsAsHavingTransparencyToLose()
    {
        // A Background layer means the paper is real content that can be erased,
        // so the composite genuinely can have holes in it.
        var scene = Painting();
        scene.TransparentBackground = false;
        scene.Layers.Add(new Layer { Name = "Background", IsBackground = true });

        var vm = new SaveImageDialogViewModel(scene) { Format = ImageSaveFormat.Jpeg };

        Assert.True(vm.MayLoseTransparency);
    }

    // ---- size ------------------------------------------------------------------

    [Fact]
    public void TheOutputSizeFollowsTheScalePercentage()
    {
        var vm = new SaveImageDialogViewModel(Painting(100, 50));

        Assert.Equal(100, vm.OutputWidth);
        Assert.Equal(50, vm.OutputHeight);

        vm.ScalePercent = 200;
        Assert.Equal(200, vm.OutputWidth);
        Assert.Equal(100, vm.OutputHeight);

        vm.ScalePercent = 50;
        Assert.Equal(50, vm.OutputWidth);
        Assert.Equal(25, vm.OutputHeight);
    }

    [Fact]
    public void AnAbsurdScaleIsClampedRatherThanAskedFor()
    {
        var vm = new SaveImageDialogViewModel(Painting(100, 50)) { ScalePercent = 100000 };

        Assert.Equal(1600, vm.OutputWidth);
        Assert.True(vm.OutputWidth > 0 && vm.OutputHeight > 0);
    }

    [Fact]
    public void ATinyScaleStillProducesAtLeastOnePixel()
    {
        var vm = new SaveImageDialogViewModel(Painting(4, 4)) { ScalePercent = 1 };

        Assert.Equal(1, vm.OutputWidth);
        Assert.Equal(1, vm.OutputHeight);
    }

    // ---- every frame -----------------------------------------------------------

    [Fact]
    public void ASinglePaintingIsNotOfferedEveryFrame()
    {
        var vm = new SaveImageDialogViewModel(Painting(frames: 1));

        Assert.False(vm.IsSequence);
    }

    [Fact]
    public void EveryFrameIsIgnoredOnADocumentThatHasOnlyOne()
    {
        // Belt and braces: the checkbox is hidden, and ticking it anyway through
        // a binding cannot produce a numbered single file.
        var vm = new SaveImageDialogViewModel(Painting(frames: 1)) { AllFrames = true };

        Assert.False(vm.ToOptions().AllFrames);
    }

    [Fact]
    public void EveryFrameReachesTheOptionsOnASequence()
    {
        var vm = new SaveImageDialogViewModel(Painting(frames: 24)) { AllFrames = true };

        Assert.True(vm.IsSequence);
        Assert.True(vm.ToOptions().AllFrames);
    }

    // ---- the sentence ----------------------------------------------------------

    [Fact]
    public void TheSummarySaysHowManyFilesAndHowBig()
    {
        var vm = new SaveImageDialogViewModel(Painting(120, 80, frames: 3));

        output.WriteLine(vm.Summary);
        Assert.Contains("one file", vm.Summary);
        Assert.Contains("120×80", vm.Summary);
        Assert.Contains("PNG", vm.Summary);
        Assert.DoesNotContain("quality", vm.Summary);

        vm.AllFrames = true;
        vm.Format = ImageSaveFormat.Jpeg;
        output.WriteLine(vm.Summary);
        Assert.Contains("3 files", vm.Summary);
        Assert.Contains("quality 90", vm.Summary);
    }

    [Fact]
    public void TheSummaryTracksTheFormatItIsAskedFor()
    {
        var vm = new SaveImageDialogViewModel(Painting());

        foreach (var format in ImageSaveFormats.All)
        {
            vm.Format = format;
            Assert.Contains(ImageSaveFormats.Label(format), vm.Summary);
        }
    }

    // ---- the options handed to the writer --------------------------------------

    [Fact]
    public void TheChoicesReachTheOptionsRecord()
    {
        var vm = new SaveImageDialogViewModel(Painting(frames: 4))
        {
            Format = ImageSaveFormat.Webp,
            Quality = 55,
            ScalePercent = 150,
            AllFrames = true,
            Matte = "#101010",
        };

        var options = vm.ToOptions();

        Assert.Equal(ImageSaveFormat.Webp, options.Format);
        Assert.Equal(55, options.Quality);
        Assert.Equal(1.5, options.Scale, 3);
        Assert.True(options.AllFrames);
        Assert.Equal("#101010", options.Matte);
    }

    [Fact]
    public void TheExtensionFollowsTheFormatSoThePickerAgrees()
    {
        var vm = new SaveImageDialogViewModel(Painting());

        Assert.Equal(".png", vm.Extension);
        vm.Format = ImageSaveFormat.Jpeg;
        Assert.Equal(".jpg", vm.Extension);
        vm.Format = ImageSaveFormat.Webp;
        Assert.Equal(".webp", vm.Extension);
    }

    [Fact]
    public void EveryFormatOfferedIsOneTheWriterCanEncode()
    {
        var vm = new SaveImageDialogViewModel(Painting());

        Assert.Equal(ImageSaveFormats.All.Length, vm.Formats.Count);
        Assert.Equal<IEnumerable<ImageSaveFormat>>(ImageSaveFormats.All, vm.Formats);
    }

    // ---- the window itself -----------------------------------------------------

    /// <remarks>
    /// Everything above this line is a view model and shares one blind spot:
    /// none of it ever builds the window, so a broken binding or a static field
    /// initialiser would pass every test and produce a menu item that does
    /// nothing. This is B163's lesson, applied before it costs anything.
    /// </remarks>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheDialogOpens()
    {
        var dialog = new Lightbox.App.Views.SaveImageDialog(Painting(200, 100, frames: 6));

        Assert.NotNull(dialog.DataContext);
        Assert.False(dialog.Confirmed);
        Assert.Equal(6, dialog.Choice.FrameCount);
        output.WriteLine($"\"{dialog.Title}\" — {dialog.Choice.Summary}");
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheParameterlessDialogOpensToo()
    {
        // The XAML compiler and the designer both use it, so it has to survive.
        var dialog = new Lightbox.App.Views.SaveImageDialog();

        Assert.NotNull(dialog.DataContext);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheNoticeDialogCarriesItsHeadlineAndBody()
    {
        // What a refused PSD is shown in.
        var dialog = new Lightbox.App.Views.NoticeDialog()
            .Show("Cannot open this PSD", "Two features are unsupported.", "• A layer mask\n• Curves");

        Assert.Equal("Cannot open this PSD", dialog.Title);
    }

    /// <summary>
    /// The command is registered, so it can be found in the shortcut editor and
    /// rebound — the failure the whole registry exists to prevent.
    /// </summary>
    [Fact]
    public void SavingAnImageIsARegisteredCommand()
    {
        var map = new Lightbox.App.Services.ShortcutMap();

        var definition = Assert.Single(map.Definitions, d => d.Id == "file.saveAsImage");

        Assert.Equal("File", definition.Category);
        Assert.NotNull(definition.Current);
        output.WriteLine($"{definition.Id} → {definition.Current}");
    }
}
