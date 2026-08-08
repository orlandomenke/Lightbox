using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Footage to draw against (Q56): frames extracted at the scene's fps into a
/// contact sheet the ordinary reference machinery draws, the document keeping
/// only the path.
/// </summary>
public class VideoReferenceModelTests
{
    [Fact]
    public void AnImageReferenceWritesNoVideoKey()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        doc.Scene.References =
        [
            new ReferenceStrip { Name = "sheet", Png = "abc", SheetWidth = 1, SheetHeight = 1 },
        ];

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("videoPath", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AVideoReferenceRoundTripsItsPathAndCarriesNoPixels()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        doc.Scene.References =
        [
            new ReferenceStrip { Name = "shot", VideoPath = "shots/take3.mp4", SheetWidth = 480, SheetHeight = 270 },
        ];

        var back = DocJson.Deserialize(DocJson.Serialize(doc))!;

        var strip = Assert.Single(back.Scene.References!);
        Assert.Equal("shots/take3.mp4", strip.VideoPath);
        Assert.Equal("", strip.Png);   // the footage is referenced, never embedded
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 3, 2)]
    [InlineData(240, 16, 15)]
    public void TheContactSheetGridIsNearSquare(int frames, int columns, int rows)
    {
        Assert.Equal((columns, rows), VideoReferenceImporter.GridFor(frames));
        Assert.True(columns * rows >= frames);
    }
}

[Collection("BrushState")]
public sealed class VideoReferenceImportTests : BrushStateIsolated
{
    [AvaloniaFact]
    public async Task AClipBecomesAReferenceMappedToTheTimeline()
    {
        // Needs a real FFmpeg — dev machines, not CI. The clip is generated
        // by the same binary, so there is nothing to check in.
        if (VideoExporter.FindFfmpeg() is not { } ffmpeg) return;

        var clip = Path.Combine(Path.GetTempPath(), "lightbox-ref-clip.mp4");
        var make = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-y -f lavfi -i testsrc=duration=1:size=64x48:rate=12 \"{clip}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        make.StandardError.ReadToEnd();
        make.WaitForExit();
        Assert.Equal(0, make.ExitCode);

        try
        {
            var window = new MainWindow();
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var vm = (MainViewModel)window.DataContext!;

            var error = await vm.ImportVideoReference(clip);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.Null(error);
            var strip = Assert.Single(vm.Doc.Scene.References!);
            Assert.NotNull(strip.VideoPath);
            Assert.Equal("", strip.Png);
            // One second at 12 fps: a dozen frames, one cell each, on the sheet.
            Assert.Equal(12, strip.Cells.Count);
            Assert.True(vm.Doc.Scene.FrameCount >= 12, "the timeline grew to hold the footage");
            Assert.NotNull(Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id));
        }
        finally
        {
            File.Delete(clip);
        }
    }
}
