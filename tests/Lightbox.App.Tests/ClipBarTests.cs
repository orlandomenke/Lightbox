using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Audio;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>Slot arithmetic for the clip bars (Q57), held still.</summary>
public class ClipSlotTests
{
    private static ReferenceStrip StripWith(params int[] slots)
    {
        var strip = new ReferenceStrip();
        for (var i = 0; i < 6; i++) strip.Cells.Add(new ReferenceCell());
        strip.Slots = [.. slots];
        return strip;
    }

    [Fact]
    public void SlidingMovesEveryAssignmentTogether()
    {
        var strip = StripWith(-1, 0, 1, 2);

        strip.SlideSlots(2);

        Assert.Equal(3, strip.FirstAssignedSlot());
        Assert.Equal(5, strip.LastAssignedSlot());
        Assert.Equal(0, strip.Slots[3]);
        Assert.Equal(2, strip.Slots[5]);
    }

    [Fact]
    public void SlidingOffTheFrontDropsWhatFellOff()
    {
        var strip = StripWith(0, 1, 2);

        strip.SlideSlots(-2);

        // Cells 0 and 1 fell off frame zero's edge; cell 2 landed on frame 0.
        Assert.Equal(0, strip.FirstAssignedSlot());
        Assert.Equal(2, strip.Slots[0]);
        Assert.Equal(0, strip.LastAssignedSlot());
    }

    [Fact]
    public void TrimmedPeaksReadOnlyTheWindow()
    {
        // 20 loud samples at 10/s, 2 fps → 4 source frames of noise.
        var mono = new float[20];
        Array.Fill(mono, 0.8f);

        // Trim the first source frame off and keep two: frames 0..1 of the
        // timeline show source frames 1..2, and nothing shows after.
        var peaks = AudioPeaks.Build(
            mono, sampleRate: 10, fps: 2, frameCount: 4,
            offsetFrames: 0, trimStartFrames: 1, trimLengthFrames: 2);

        Assert.True(peaks[0].Max > 0.7f);
        Assert.True(peaks[1].Max > 0.7f);
        Assert.Equal(0.0, (double)peaks[2].Max, 4);   // past the out-point
    }
}

/// <summary>The clip bars' edits, applied through the view model (Q57).</summary>
[Collection("BrushState")]
public sealed class ClipBarVmTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    private static void GiveAudio(MainViewModel vm, int sourceFrames)
    {
        // One second per frame at 1 fps keeps the arithmetic legible.
        var sr = 8000;
        var samples = new float[sr * sourceFrames / Math.Max(1, vm.Doc.Scene.Fps) * vm.Doc.Scene.Fps];
        // Simpler: sourceFrames frames at scene fps → duration = sourceFrames/fps s.
        var fps = Math.Max(1, vm.Doc.Scene.Fps);
        samples = new float[sr * sourceFrames / fps];
        Array.Fill(samples, 0.5f);
        var clip = new AudioClip { Samples = samples, Channels = 1, SampleRate = sr };
        vm.Doc.Scene.Audio = new AudioTrack { Path = "test.wav" };
        // Feed the cache directly — the file never existed.
        typeof(MainViewModel).GetField("_audioClip",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, clip);
        typeof(MainViewModel).GetField("_audioMono",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, clip.MonoMixdown());
        // Null matches what EnsureAudioLoaded resolves for an unsaved
        // document, so the injected cache is kept rather than re-read.
        typeof(MainViewModel).GetField("_audioLoadedFrom",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, null);
    }

    [AvaloniaFact]
    public void TrimInEatsTheHeadAndAnchorsTheTail()
    {
        var (_, vm) = Open();
        GiveAudio(vm, sourceFrames: 12);
        var track = vm.Doc.Scene.Audio!;

        vm.TrimAudioClipIn(0, 3);

        Assert.Equal(3, track.TrimStartFrames);
        Assert.Equal(9, track.TrimLengthFrames);
        Assert.Equal(3, track.OffsetFrames);   // the left edge followed
        // The bar: starts at 3, nine frames long — the tail still ends at 11.
        var span = vm.AudioClipSpan!.Value;
        Assert.Equal((3, 9), span);

        // And back out again restores the head.
        vm.TrimAudioClipIn(0, -3);
        Assert.Equal(0, track.TrimStartFrames);
        Assert.Equal(12, track.TrimLengthFrames);
        Assert.Equal(0, track.OffsetFrames);
    }

    [AvaloniaFact]
    public void TrimOutBoundsTheTailAndNeverBelowOneFrame()
    {
        var (_, vm) = Open();
        GiveAudio(vm, sourceFrames: 12);
        var track = vm.Doc.Scene.Audio!;

        vm.TrimAudioClipOut(0, -4);
        Assert.Equal(8, track.TrimLengthFrames);

        vm.TrimAudioClipOut(0, -100);
        Assert.Equal(1, track.TrimLengthFrames);

        vm.TrimAudioClipOut(0, 100);
        Assert.Equal(12, track.TrimLengthFrames);
    }

    [AvaloniaFact]
    public void SlidingAVideoClipMovesItsBarAndGrowsTheTimeline()
    {
        var (_, vm) = Open();
        var strip = new ReferenceStrip { Name = "shot", VideoPath = "clip.mp4" };
        for (var i = 0; i < 4; i++) strip.Cells.Add(new ReferenceCell());
        strip.LayOutFrom(0);
        vm.Doc.Scene.References = [strip];

        vm.SlideVideoClip(0, runStart: 0, deltaFrames: 5);

        var bar = Assert.Single(vm.TimelineVideoClips);
        Assert.Equal(5, bar.Start);
        Assert.Equal(8, bar.End);
        Assert.True(vm.Doc.Scene.FrameCount >= 9, "the timeline grew to hold the slid clip");
    }

    [AvaloniaFact]
    public void TrimmingAVideoClipHidesFramesAndBringsThemBack()
    {
        var (_, vm) = Open();
        var strip = new ReferenceStrip { Name = "shot", VideoPath = "clip.mp4" };
        for (var i = 0; i < 6; i++) strip.Cells.Add(new ReferenceCell());
        strip.LayOutFrom(2);   // cells 0..5 at frames 2..7
        vm.Doc.Scene.References = [strip];

        vm.TrimVideoClipIn(0, runStart: 2, deltaFrames: 2);
        Assert.Equal(4, vm.TimelineVideoClips[0].Start);
        Assert.Null(strip.CellAt(2));
        Assert.Null(strip.CellAt(3));

        vm.TrimVideoClipIn(0, runStart: 4, deltaFrames: -2);
        Assert.Equal(2, vm.TimelineVideoClips[0].Start);
        Assert.NotNull(strip.CellAt(2));

        vm.TrimVideoClipOut(0, runStart: 2, deltaFrames: -3);
        Assert.Equal(4, vm.TimelineVideoClips[0].End);

        vm.TrimVideoClipOut(0, runStart: 2, deltaFrames: 3);
        Assert.Equal(7, vm.TimelineVideoClips[0].End);
    }
}
