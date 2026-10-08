using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The choices in <c>File ▸ Export image sequence…</c>, and the window that
/// shows them (Q219).
/// </summary>
/// <remarks>
/// The artist reads and types frame numbers from one, the way the timeline
/// shows them; the exporter counts from zero. Most of what is worth checking
/// here is that the two never meet anywhere but <c>ToSettings</c>.
/// </remarks>
public class ImageSequenceDialogTests(Xunit.ITestOutputHelper output)
{
    private static Scene Cycle(int frames = 24, bool transparent = true) => new()
    {
        Width = 200,
        Height = 100,
        Fps = 12,
        FrameCount = frames,
        TransparentBackground = transparent,
    };

    private static Scene Tagged()
    {
        var scene = Cycle();
        scene.Tags =
        [
            new AnimationTag { Name = "idle", Start = 0, End = 7 },
            new AnimationTag { Name = "walk", Start = 8, End = 19 },
        ];
        return scene;
    }

    // ---- the range -------------------------------------------------------------

    [Fact]
    public void ItOpensOnTheWholeTimelineCountedFromOne()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(24));

        Assert.Equal(1, vm.FromFrame);
        Assert.Equal(24, vm.ToFrame);
        Assert.Equal(24, vm.LastFrame);
        Assert.Equal(1, vm.Step);
        Assert.False(vm.UniqueOnly);
    }

    [Fact]
    public void TheRangeReachesTheSettingsCountedFromZero()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(24)) { FromFrame = 5, ToFrame = 12, Step = 2 };

        var settings = vm.ToSettings();

        Assert.Equal(4, settings.FromFrame);
        Assert.Equal(11, settings.ToFrame);
        Assert.Equal(2, settings.Step);
    }

    [Fact]
    public void ADocumentWithNoTagsOffersNoTagPicker()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle());

        Assert.False(vm.HasTags);
        Assert.Empty(vm.Tags);
    }

    [Fact]
    public void ChoosingATagFillsInItsRange()
    {
        var vm = new ImageSequenceDialogViewModel(Tagged());

        Assert.True(vm.HasTags);
        Assert.Equal(["idle", "walk"], vm.Tags.Select(t => t.Name).ToArray());
        Assert.Null(vm.SelectedTag);

        vm.SelectedTag = vm.Tags[1];

        Assert.Equal(9, vm.FromFrame);
        Assert.Equal(20, vm.ToFrame);
    }

    [Fact]
    public void TypingARangeAfterChoosingATagLetsGoOfTheTag()
    {
        // Otherwise the picker says "walk" over a range that is no longer walk.
        var vm = new ImageSequenceDialogViewModel(Tagged());
        vm.SelectedTag = vm.Tags[1];

        vm.ToFrame = 15;

        Assert.Null(vm.SelectedTag);
        Assert.Equal(9, vm.FromFrame);
        Assert.Equal(15, vm.ToFrame);
    }

    [Fact]
    public void ATagThatRunsPastTheEndIsShortenedToTheTimeline()
    {
        var scene = Cycle(12);
        scene.Tags = [new AnimationTag { Name = "long", Start = 6, End = 40 }];
        var vm = new ImageSequenceDialogViewModel(scene);

        vm.SelectedTag = vm.Tags[0];

        Assert.Equal(7, vm.FromFrame);
        Assert.Equal(12, vm.ToFrame);
    }

    // ---- what does not apply is absent -----------------------------------------

    [Fact]
    public void QualityAppearsForLossyFormatsOnly()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle());

        Assert.False(vm.HasQuality);
        vm.Format = ImageSaveFormat.Jpeg;
        Assert.True(vm.HasQuality);
    }

    [Fact]
    public void TheStartNumberGoesAwayWhenTheTimelineIsDoingTheNumbering()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle());

        Assert.True(vm.HasStartNumber);
        vm.TimelineNumbers = true;
        Assert.False(vm.HasStartNumber);
    }

    [Fact]
    public void TransparentPaperIsOnlyOfferedWhereTheFormatCanKeepIt()
    {
        // On JPEG the same tick would be a promise the format cannot keep; the
        // fill colour is what applies there instead.
        var vm = new ImageSequenceDialogViewModel(Cycle());

        Assert.True(vm.OffersTransparentPaper);
        Assert.False(vm.HasMatte);

        vm.Format = ImageSaveFormat.Jpeg;

        Assert.False(vm.OffersTransparentPaper);
        Assert.True(vm.HasMatte);
    }

    [Fact]
    public void TransparentPaperTickedOnPngDoesNotReachAJpeg()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle()) { TransparentPaper = true };
        Assert.True(vm.ToSettings().TransparentPaper);

        vm.Format = ImageSaveFormat.Jpeg;

        Assert.False(vm.ToSettings().TransparentPaper);
    }

    [Fact]
    public void AJpegOfATransparentDocumentWarnsBeforeTheExport()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(transparent: true));
        Assert.False(vm.MayLoseTransparency);

        vm.Format = ImageSaveFormat.Jpeg;

        Assert.True(vm.MayLoseTransparency);
    }

    // ---- the sentence ----------------------------------------------------------

    [Fact]
    public void TheSummaryCountsTheFilesTheRangeAndStepWillWrite()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(24));
        output.WriteLine(vm.Summary);
        Assert.Contains("24 files", vm.Summary);
        Assert.Contains("200×100", vm.Summary);
        Assert.Contains("PNG", vm.Summary);

        vm.FromFrame = 5;
        vm.ToFrame = 12;
        vm.Step = 2;
        output.WriteLine(vm.Summary);
        Assert.Contains("4 files", vm.Summary);

        vm.ScalePercent = 50;
        Assert.Contains("100×50", vm.Summary);
    }

    [Fact]
    public void TheSummaryDoesNotPromiseACountItCannotKnow()
    {
        // How many pictures are unique is only known once they are rendered.
        var vm = new ImageSequenceDialogViewModel(Cycle(24)) { UniqueOnly = true };

        output.WriteLine(vm.Summary);
        Assert.Contains("up to 24 files", vm.Summary);
        Assert.Contains("timing", vm.Summary);
    }

    [Fact]
    public void ADocumentWithNoFramesSaysSoRatherThanThrowing()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(frames: 0));

        output.WriteLine(vm.Summary);
        Assert.Contains("no frames", vm.Summary);
    }

    [Fact]
    public void AnAbsurdStepCannotHangTheSummary()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(24)) { FromFrame = 3, Step = int.MaxValue };

        Assert.Contains("1 file", vm.Summary);
    }

    [Fact]
    public void TheSummaryShowsTheFirstFileNameAsItWillBeWritten()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(24))
        {
            Prefix = "walk",
            Digits = 3,
            StartNumber = 10,
            Format = ImageSaveFormat.Webp,
        };

        output.WriteLine(vm.Summary);
        Assert.Contains("walk_010.webp", vm.Summary);

        vm.TimelineNumbers = true;
        vm.FromFrame = 5;
        Assert.Contains("walk_005.webp", vm.Summary);
    }

    // ---- everything reaches the exporter ---------------------------------------

    [Fact]
    public void EveryChoiceReachesTheSettingsRecord()
    {
        var vm = new ImageSequenceDialogViewModel(Cycle(24))
        {
            Format = ImageSaveFormat.Webp,
            Quality = 55,
            ScalePercent = 150,
            UniqueOnly = true,
            TimelineNumbers = true,
            Prefix = "run",
            Digits = 5,
            StartNumber = 7,
            TransparentPaper = true,
            Matte = "#ff00ff",
        };

        var settings = vm.ToSettings();

        Assert.Equal(ImageSaveFormat.Webp, settings.Format);
        Assert.Equal(55, settings.Quality);
        Assert.Equal(1.5, settings.Scale);
        Assert.True(settings.UniqueOnly);
        Assert.True(settings.TimelineNumbers);
        Assert.Equal("run", settings.Prefix);
        Assert.Equal(5, settings.Digits);
        Assert.Equal(7, settings.StartNumber);
        Assert.True(settings.TransparentPaper);
        Assert.Equal("#ff00ff", settings.Matte);
    }

    [Fact]
    public void WithACameraTheSizeIsWhatTheCameraSaw()
    {
        var scene = Cycle();
        scene.Camera = new Camera { OutputWidth = 320, OutputHeight = 180 };

        var vm = new ImageSequenceDialogViewModel(scene);

        Assert.Contains("320×180", vm.Summary);
    }

    // ---- the window, and where it is reachable from ----------------------------

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheWindowOpens()
    {
        var window = new Lightbox.App.Views.ImageSequenceWindow(Tagged());

        Assert.NotNull(window.DataContext);
        Assert.False(window.Confirmed);
        Assert.Equal(24, window.Choice.LastFrame);
        output.WriteLine($"\"{window.Title}\" — {window.Choice.Summary}");
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheParameterlessWindowOpensToo()
    {
        var window = new Lightbox.App.Views.ImageSequenceWindow();

        Assert.NotNull(window.DataContext);
    }

    private static Lightbox.App.Views.ImageSequenceWindow Shown(Scene scene)
    {
        var window = new Lightbox.App.Views.ImageSequenceWindow(scene);
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <remarks>
    /// The design system's rule for a dialog's two buttons: comparable things
    /// are the same size. Left to their content, "Cancel" and "Export…" are not.
    /// </remarks>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void TheTwoButtonsAreTheSameWidth()
    {
        var window = Shown(Tagged());

        var cancel = window.FindControl<Avalonia.Controls.Button>("CancelButton")!;
        var export = window.FindControl<Avalonia.Controls.Button>("ExportButton")!;

        output.WriteLine($"cancel {cancel.Bounds.Width}, export {export.Bounds.Width}");
        Assert.True(cancel.Bounds.Width > 0);
        Assert.Equal(cancel.Bounds.Width, export.Bounds.Width);
    }

    /// <remarks>
    /// Quality is hidden on PNG, in the middle of a grid that spaces its rows.
    /// A row that is not there must not leave its spacing behind, or the gap
    /// between Format and Size is twice every other gap in the window.
    /// </remarks>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void AHiddenRowLeavesNoGapWhereItWas()
    {
        var window = Shown(Cycle());
        var format = window.FindControl<Avalonia.Controls.ComboBox>("FormatBox")!;
        var size = window.FindControl<Avalonia.Controls.NumericUpDown>("SizeBox")!;
        var from = window.FindControl<Avalonia.Controls.NumericUpDown>("FromBox")!;
        var step = window.FindControl<Avalonia.Controls.NumericUpDown>("StepBox")!;

        // Each field's row, placed in the window: the rows are siblings in a
        // stack, so their own Bounds are not in one coordinate space.
        double Top(Control field) => ((Control)field.Parent!).TranslatePoint(new Point(0, 0), window)!.Value.Y;
        double Bottom(Control field) => Top(field) + ((Control)field.Parent!).Bounds.Height;

        var acrossHidden = Top(size) - Bottom(format);
        var ordinary = Top(step) - Bottom(from);

        output.WriteLine($"across the hidden row {acrossHidden}, between ordinary rows {ordinary}");
        Assert.True(ordinary > 0);
        Assert.Equal(ordinary, acrossHidden, 1);
    }

    [Fact]
    public void ExportingASequenceIsARegisteredCommand()
    {
        var map = new ShortcutMap();

        var definition = Assert.Single(map.Definitions, d => d.Id == "file.exportImageSequence");

        Assert.Equal("File", definition.Category);
    }

    // ---- one place for sequences (Q219) ----------------------------------------

    [Fact]
    public void SaveAsImageWritesOnePictureAndNoLongerOffersEveryFrame()
    {
        Assert.Null(typeof(ImageSaveOptions).GetProperty("AllFrames"));
        Assert.Null(typeof(SaveImageDialogViewModel).GetProperty("AllFrames"));
    }
}
