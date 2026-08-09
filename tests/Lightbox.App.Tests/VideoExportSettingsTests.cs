using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The export window and the settings behind it (B146).
///
/// The defect these close is not an encoder bug. It is that
/// <c>File ▸ Export video…</c> asked for a filename, rendered the whole
/// timeline at the document's size with nothing to adjust, and reported every
/// answer it had — including "FFmpeg was not found" — into the AI bar, which
/// is hidden whenever assistance is switched off. So the assertions are about
/// two things: that the settings exist and mean what they say, and that the
/// window says out loud what it is doing.
/// </summary>
public class VideoExportSettingsTests
{
    private static Doc SceneOf(int width, int height, int frames, int fps = 12)
    {
        var doc = DocumentFactory.CreateDoc(width, height, fps);
        doc.Scene.FrameCount = frames;
        return doc;
    }

    [Fact]
    public void TheDefaultsAreTheWholeTimelineAtItsOwnSizeAndRate()
    {
        var doc = SceneOf(320, 240, 48, fps: 24);

        var settings = VideoExportSettings.For(doc.Scene);

        Assert.Equal((0, 47), settings.ClampedRange(doc.Scene));
        Assert.Equal(48, settings.FrameCount(doc.Scene));
        Assert.Equal(24, settings.Fps);
        Assert.Equal((320, 240), settings.OutputSize(doc.Scene));
        Assert.Equal(2.0, settings.DurationSeconds(doc.Scene), 3);
    }

    [Fact]
    public void TheVideoAndThePngSequenceRenderTheSamePixels()
    {
        // An odd-sided document at the window's own defaults. Rounding the
        // size to even here — which the codecs want — made the video 102x100
        // while the sequence stayed 101x99, breaking SequenceExporter's
        // promise that the two are the same pixels by construction, at the
        // settings nobody had touched. The encoder pads the odd edge instead:
        // padding adds a pixel, rounding changes what was rendered.
        var doc = SceneOf(101, 99, 1);
        var settings = VideoExportSettings.For(doc.Scene);

        Assert.Equal((101, 99), settings.OutputSize(doc.Scene));

        using var cache = new FrameBitmapCache();
        using var sequence = SequenceExporter.RenderFrame(doc, cache, 0);
        var (w, h) = settings.OutputSize(doc.Scene);
        using var video = SequenceExporter.RenderFrame(doc, cache, 0, settings.Scale, (w, h));

        Assert.Equal(sequence.Width, video.Width);
        Assert.Equal(sequence.Height, video.Height);
    }

    [Fact]
    public void EveryFormatPadsAnOddEdgeRatherThanResizingTheArt()
    {
        foreach (var format in (VideoFormat[])[VideoFormat.Mp4, VideoFormat.ProRes])
        {
            var args = VideoExporter.BuildArgs(format, 101, 99, 12, "/out/a" + VideoExporter.ExtensionOf(format));
            var at = args.IndexOf("-vf");
            Assert.True(at >= 0, $"{format} must pad an odd dimension");
            Assert.Contains("pad=", args[at + 1]);
        }
    }

    [Fact]
    public void AScaledRenderRoundsToWholePixels()
    {
        var doc = SceneOf(101, 99, 4);

        var half = VideoExportSettings.For(doc.Scene) with { Scale = 0.5 };

        Assert.Equal((51, 50), half.OutputSize(doc.Scene));
    }

    [Fact]
    public void ARangeIsOrderedAndClampedToWhatTheSceneHas()
    {
        var doc = SceneOf(64, 64, 10);

        var backwards = VideoExportSettings.For(doc.Scene) with { FromFrame = 7, ToFrame = 2 };
        var beyond = VideoExportSettings.For(doc.Scene) with { FromFrame = 8, ToFrame = 400 };

        Assert.Equal((2, 7), backwards.ClampedRange(doc.Scene));
        Assert.Equal((8, 9), beyond.ClampedRange(doc.Scene));
        Assert.Equal(2, beyond.FrameCount(doc.Scene));
    }

    [Fact]
    public void AnExcerptStartsTheSoundWhereTheExcerptStarts()
    {
        // The sound is anchored to frame zero of the document. Rendering from
        // frame 24 with the track at frame 6 must play the sound 18 frames
        // before the render's own start, or the dialogue is 24 frames late.
        var doc = SceneOf(64, 64, 100);
        doc.Scene.Audio = new AudioTrack { Path = "take.wav", OffsetFrames = 6 };

        var excerpt = VideoExportSettings.For(doc.Scene) with { FromFrame = 24, ToFrame = 47 };

        Assert.Equal(-18, excerpt.AudioOffsetFrames(doc.Scene));
        Assert.Equal(6, VideoExportSettings.For(doc.Scene).AudioOffsetFrames(doc.Scene));
    }

    [Fact]
    public void TurningTheSoundOffTakesItOutOfTheOffsetToo()
    {
        var doc = SceneOf(64, 64, 20);
        doc.Scene.Audio = new AudioTrack { Path = "take.wav", OffsetFrames = 6 };

        var silent = VideoExportSettings.For(doc.Scene) with { IncludeAudio = false };

        Assert.Equal(0, silent.AudioOffsetFrames(doc.Scene));
    }

    [Fact]
    public void TheQualityChoiceReachesTheEncoder()
    {
        var high = VideoExporter.BuildArgs(VideoFormat.Mp4, 64, 48, 12, "/out/a.mp4", quality: 16);
        var small = VideoExporter.BuildArgs(VideoFormat.Mp4, 64, 48, 12, "/out/a.mp4", quality: 26);
        var prores = VideoExporter.BuildArgs(VideoFormat.ProRes, 64, 48, 12, "/out/a.mov", quality: 2);

        Assert.Equal("16", high[high.IndexOf("-crf") + 1]);
        Assert.Equal("26", small[small.IndexOf("-crf") + 1]);
        Assert.Equal("2", prores[prores.IndexOf("-profile:v") + 1]);
        // Absent quality is the value the pipeline always used.
        var plain = VideoExporter.BuildArgs(VideoFormat.Mp4, 64, 48, 12, "/out/a.mp4");
        Assert.Equal("18", plain[plain.IndexOf("-crf") + 1]);
    }

    [Fact]
    public void RenderingBiggerScalesTheSurfaceAndNeverTheGeometry()
    {
        // Invariant 7: a 2× render is the same mark at twice the size. If the
        // coordinates were multiplied instead, Hash01 would re-roll every dab
        // dynamic and the frame would be a different drawing.
        var doc = SceneOf(64, 48, 1);
        var before = Lightbox.Core.Serialization.DocJson.Serialize(doc);

        using var cache = new FrameBitmapCache();
        using var plain = SequenceExporter.RenderFrame(doc, cache, 0);
        using var doubled = SequenceExporter.RenderFrame(doc, cache, 0, scale: 2.0);

        Assert.Equal(64, plain.Width);
        Assert.Equal(128, doubled.Width);
        Assert.Equal(96, doubled.Height);
        Assert.Equal(before, Lightbox.Core.Serialization.DocJson.Serialize(doc));
    }

    [Fact]
    public void AnExplicitOutputSizeWinsOverTheArithmetic()
    {
        // The encoder is told one size; the pipe must carry exactly that many
        // bytes, so the even-number rounding may only be decided in one place.
        var doc = SceneOf(101, 99, 1);
        var settings = VideoExportSettings.For(doc.Scene) with { Scale = 0.5 };
        var size = settings.OutputSize(doc.Scene);   // 51x50 — odd on a side

        using var cache = new FrameBitmapCache();
        using var image = SequenceExporter.RenderFrame(doc, cache, 0, 0.5, size);

        Assert.Equal(size.Width, image.Width);
        Assert.Equal(size.Height, image.Height);
    }

    [Fact]
    public void AMissingEncoderIsASentenceThatSaysWhatToDoInstead()
    {
        var notice = VideoExportReport.EncoderNotice(null);

        Assert.NotNull(notice);
        Assert.Contains("FFmpeg", notice);
        Assert.Contains("PNG", notice);   // the way out that works today
        Assert.False(VideoExportReport.CanExport(null));
        Assert.Null(VideoExportReport.EncoderNotice("/usr/bin/ffmpeg"));
        Assert.True(VideoExportReport.CanExport("/usr/bin/ffmpeg"));
    }

    [Fact]
    public void EveryReasonAnExportCannotStartIsSaidBeforeTheEncoderRuns()
    {
        var doc = SceneOf(64, 48, 12);
        var settings = VideoExportSettings.For(doc.Scene);

        Assert.Contains("where the file goes", VideoExportReport.Validate(settings, doc.Scene, "")!);
        Assert.Contains("does not exist",
            VideoExportReport.Validate(settings, doc.Scene, "/no/such/folder/take.mp4")!);
        Assert.Null(VideoExportReport.Validate(
            settings, doc.Scene, Path.Combine(Path.GetTempPath(), "take.mp4")));
    }

    [Fact]
    public void ASmallRenderIsMeasuredInKilobytesRatherThanRoundedToZero()
    {
        // "0 MB" after a two-second test render reads as a failed export.
        Assert.Equal("2 KB", VideoExportReport.FileSize(2048));
        Assert.Equal("1 KB", VideoExportReport.FileSize(12));
        Assert.Equal("1.5 MB", VideoExportReport.FileSize(1024 * 1024 * 3 / 2));
    }

    [Fact]
    public void TheSummaryNamesThePixelsTheFramesAndTheSeconds()
    {
        var doc = SceneOf(320, 240, 24, fps: 12);

        var summary = VideoExportReport.Summarise(VideoExportSettings.For(doc.Scene), doc.Scene);

        Assert.Contains("320×240", summary);
        Assert.Contains("24 frames", summary);
        Assert.Contains("2 s", summary);
    }

    [Fact]
    public void ARangeAtHalfSizeEncodesToTheSizeItPromised()
    {
        // Needs a real encoder, so it runs on a dev machine and not in CI —
        // the one test that proves the declared size and the piped bytes
        // agree, which is where a scale gets a raw pipeline killed.
        if (VideoExporter.FindFfmpeg() is null) return;

        var doc = SceneOf(101, 99, 20);
        var settings = VideoExportSettings.For(doc.Scene)
            with { Scale = 0.5, FromFrame = 4, ToFrame = 9 };
        var path = Path.Combine(Path.GetTempPath(), "lightbox-range-half.mp4");
        var seen = 0;
        try
        {
            var error = VideoExporter.Export(
                doc, settings, path, null, (done, _) => seen = done,
                TestContext.Current.CancellationToken);

            Assert.Null(error);
            Assert.Equal(6, seen);
            Assert.True(new FileInfo(path).Length > 200, "the encode produced a stub");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void TheWindowSaysTheEncoderIsMissingRatherThanFailingSilently()
    {
        // The whole bug in one assertion: the reason lands on a surface the
        // artist is looking at. Whether FFmpeg is present on the machine
        // running the tests decides which half is checked, and both halves
        // are the same promise — nothing is silent.
        var doc = SceneOf(64, 48, 6);
        var window = new VideoExportWindow(doc, () => null, "take");

        var banner = window.FindControl<Border>("EncoderBanner")!;
        var button = window.FindControl<Button>("ExportButton")!;
        if (VideoExporter.FindFfmpeg() is null)
        {
            Assert.True(banner.IsVisible);
            Assert.False(button.IsEnabled);
        }
        else
        {
            Assert.False(banner.IsVisible);
            Assert.True(button.IsEnabled);
        }
    }

    [AvaloniaFact]
    public void TheWindowOpensOnTheWholeTimelineAtTheDocumentsOwnSize()
    {
        var doc = SceneOf(320, 240, 36, fps: 24);
        var window = new VideoExportWindow(doc, () => null, "take");

        var settings = window.CurrentSettings();

        Assert.Equal(VideoFormat.Mp4, settings.Format);
        Assert.Equal(1.0, settings.Scale);
        Assert.Equal((0, 35), settings.ClampedRange(doc.Scene));
        Assert.Equal(24, settings.Fps);
        Assert.Equal(18, settings.Quality);
    }

    [AvaloniaFact]
    public void TheRangeFieldsCountFromOneTheWayTheTimelineDoes()
    {
        var doc = SceneOf(64, 48, 100);
        var window = new VideoExportWindow(doc, () => null, "take");

        window.FindControl<CheckBox>("WholeTimelineBox")!.IsChecked = false;
        window.FindControl<TextBox>("FromBox")!.Text = "10";
        window.FindControl<TextBox>("ToBox")!.Text = "20";

        // Frame 10 on screen is index 9 in the record — eleven frames, not ten.
        Assert.Equal((9, 19), window.CurrentSettings().ClampedRange(doc.Scene));
        Assert.Equal(11, window.CurrentSettings().FrameCount(doc.Scene));
    }

    [AvaloniaFact]
    public void ChoosingProResRenamesTheFileSoTheStreamMatchesTheContainer()
    {
        var doc = SceneOf(64, 48, 6);
        var window = new VideoExportWindow(doc, () => null, "take");
        var path = window.FindControl<TextBox>("PathBox")!;
        Assert.EndsWith(".mp4", path.Text);

        window.FindControl<ComboBox>("FormatBox")!.SelectedIndex = 1;

        Assert.EndsWith(".mov", path.Text);
        Assert.Equal(VideoFormat.ProRes, window.CurrentSettings().Format);
        Assert.Equal(3, window.CurrentSettings().Quality);   // 422 HQ, not H.264's CRF
    }

    [AvaloniaFact]
    public void ADocumentWithNoSoundCannotAskForSound()
    {
        var doc = SceneOf(64, 48, 6);
        var window = new VideoExportWindow(doc, () => null, "take");

        var audio = window.FindControl<CheckBox>("AudioBox")!;
        Assert.False(audio.IsEnabled);
        Assert.False(window.CurrentSettings().IncludeAudio);
    }

    [AvaloniaFact]
    public void ATrackWhoseFileCannotBeFoundDoesNotPromiseSound()
    {
        // Having a track and being able to read it are different questions. A
        // ticked box over a WAV that has moved renders a silent video and
        // reports success — which is B146 again, one feature along, so the
        // window asks the resolver rather than trusting the track.
        var doc = SceneOf(64, 48, 6);
        doc.Scene.Audio = new AudioTrack { Path = "/no/such/folder/take.wav" };

        var window = new VideoExportWindow(doc, () => null, "take");

        var audio = window.FindControl<CheckBox>("AudioBox")!;
        Assert.False(audio.IsChecked);
        Assert.False(audio.IsEnabled);
        Assert.Contains("cannot be found", audio.Content?.ToString());
        Assert.False(window.CurrentSettings().IncludeAudio);
    }

    [AvaloniaFact]
    public void ATrackThatCanBeReadIsOfferedAndOnByDefault()
    {
        var doc = SceneOf(64, 48, 6);
        doc.Scene.Audio = new AudioTrack { Path = "take.wav" };
        var wav = Path.Combine(Path.GetTempPath(), "lightbox-window-audio.wav");
        File.WriteAllBytes(wav, [1, 2, 3]);
        try
        {
            var window = new VideoExportWindow(doc, () => wav, "take");

            var audio = window.FindControl<CheckBox>("AudioBox")!;
            Assert.True(audio.IsChecked);
            Assert.True(audio.IsEnabled);
            Assert.True(window.CurrentSettings().IncludeAudio);
        }
        finally
        {
            File.Delete(wav);
        }
    }
}
