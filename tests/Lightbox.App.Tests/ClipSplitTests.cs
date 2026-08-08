using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Audio;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Cutting a clip into sections and moving them about (Q57).
///
/// The promise underneath all of it is that splitting is something a document
/// opts into: an unsplit clip is the offset-and-trim record it always was,
/// writes no new key, and needs no migration. Everything else here is what a
/// section may do once one exists — and, as loudly, what it may not: sections
/// never ride over each other, and a cut at an edge is not a cut.
/// </summary>
public class ClipSectionModelTests
{
    private static AudioTrack Track() => new() { Path = "take.wav" };

    [Fact]
    public void AnUnsplitClipIsOneSectionAndWritesNoKey()
    {
        var doc = DocumentFactory.CreateDoc(64, 48, 12);
        doc.Scene.Audio = new AudioTrack { Path = "take.wav", OffsetFrames = 4 };

        var sections = doc.Scene.Audio.EffectiveSegments(totalSourceFrames: 10);

        var only = Assert.Single(sections);
        Assert.Equal(4, only.AtFrame);
        Assert.Equal(0, only.SourceStartFrames);
        Assert.Equal(10, only.LengthFrames);
        Assert.DoesNotContain("segments", DocJson.Serialize(doc), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplittingMaterialisesTheImplicitSectionIntoTwo()
    {
        var track = Track();

        Assert.True(track.SplitAt(5, totalSourceFrames: 12));

        Assert.NotNull(track.Segments);
        Assert.Equal(2, track.Segments!.Count);
        // The head keeps the source it had; the tail starts where the cut was.
        Assert.Equal((0, 5, 0), (track.Segments[0].SourceStartFrames, track.Segments[0].LengthFrames, track.Segments[0].AtFrame));
        Assert.Equal((5, 7, 5), (track.Segments[1].SourceStartFrames, track.Segments[1].LengthFrames, track.Segments[1].AtFrame));
    }

    [Fact]
    public void AnEdgeHasNothingToSplit()
    {
        var track = Track();

        Assert.False(track.SplitAt(0, 12));     // the very start
        Assert.False(track.SplitAt(12, 12));    // one past the end
        Assert.False(track.SplitAt(40, 12));    // nowhere near it
        Assert.Null(track.Segments);            // and nothing was materialised
    }

    [Fact]
    public void ASecondCutDividesOnlyTheSectionItLandsIn()
    {
        var track = Track();
        track.SplitAt(6, 12);

        Assert.True(track.SplitAt(9, 12));

        Assert.Equal(3, track.Segments!.Count);
        Assert.Equal([0, 6, 9], track.Segments.Select(s => s.AtFrame).ToArray());
        Assert.Equal([6, 3, 3], track.Segments.Select(s => s.LengthFrames).ToArray());
    }

    [Fact]
    public void PeaksReadEachSectionsOwnWindowOfTheSource()
    {
        // 20 samples at 10/s, 2 fps → 4 source frames. Loud in the first two
        // source frames, silent after, so a moved section is findable.
        var mono = new float[20];
        for (var i = 0; i < 10; i++) mono[i] = 0.8f;

        // Section A: source frames 0..1 at timeline 0. Section B: source
        // frames 0..1 again, parked at timeline 4 — the same sound twice.
        var peaks = AudioPeaks.Build(mono, sampleRate: 10, fps: 2, frameCount: 8,
        [
            new AudioSegment { SourceStartFrames = 0, LengthFrames = 2, AtFrame = 0 },
            new AudioSegment { SourceStartFrames = 0, LengthFrames = 2, AtFrame = 4 },
        ]);

        Assert.True(peaks[0].Max > 0.7f);
        Assert.Equal(0.0, (double)peaks[2].Max, 4);   // the gap between sections
        Assert.Equal(0.0, (double)peaks[3].Max, 4);
        Assert.True(peaks[4].Max > 0.7f);             // the section that moved
        Assert.Equal(0.0, (double)peaks[6].Max, 4);
    }
}

/// <summary>The video side: runs of assigned slots, divided at split points.</summary>
public class ClipRunTests
{
    private static ReferenceStrip StripOf(params int[] slots)
    {
        var strip = new ReferenceStrip { Name = "shot", VideoPath = "clip.mp4" };
        for (var i = 0; i < 8; i++) strip.Cells.Add(new ReferenceCell());
        strip.Slots = [.. slots];
        return strip;
    }

    [Fact]
    public void ContiguousFramesAreOneRunAndAGapMakesTwo()
    {
        var strip = StripOf(0, 1, -1, -1, 2, 3);

        Assert.Equal([(0, 1), (4, 5)], strip.AssignedRuns());
    }

    [Fact]
    public void ASplitPointDividesARunWithoutMovingAnything()
    {
        var strip = StripOf(0, 1, 2, 3);
        strip.SplitPoints = [2];

        Assert.Equal([(0, 1), (2, 3)], strip.AssignedRuns());
        // The frames themselves are untouched — a cut is a statement about
        // where the bars are, not an edit to the footage.
        Assert.Equal([0, 1, 2, 3], strip.Slots);
    }

    [Fact]
    public void APointThatStopsBeingInteriorIsDroppedRatherThanKept()
    {
        var strip = StripOf(0, 1, 2, 3);
        strip.SplitPoints = [2];

        // Slide the second section away: the gap now divides the two, so the
        // point has nothing left to do and would otherwise cut a run that is
        // no longer there.
        strip.SlideRange(2, 3, 3);

        Assert.Equal([(0, 1), (5, 6)], strip.AssignedRuns());
        Assert.Null(strip.SplitPoints);
    }

    [Fact]
    public void SlidingOneSectionLeavesTheOtherWhereItWas()
    {
        var strip = StripOf(0, 1, 2, 3);
        strip.SplitPoints = [2];

        strip.SlideRange(2, 3, 2);

        Assert.Equal(0, strip.Slots[0]);
        Assert.Equal(1, strip.Slots[1]);
        Assert.Equal(2, strip.Slots[4]);
        Assert.Equal(3, strip.Slots[5]);
    }
}

/// <summary>Splitting and rearranging through the view model (Q57).</summary>
[Collection("BrushState")]
public sealed class ClipSplitVmTests : BrushStateIsolated
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
        var sr = 8000;
        var fps = Math.Max(1, vm.Doc.Scene.Fps);
        var samples = new float[sr * sourceFrames / fps];
        Array.Fill(samples, 0.5f);
        var clip = new AudioClip { Samples = samples, Channels = 1, SampleRate = sr };
        vm.Doc.Scene.Audio = new AudioTrack { Path = "test.wav" };
        Set(vm, "_audioClip", clip);
        Set(vm, "_audioMono", clip.MonoMixdown());
        Set(vm, "_audioLoadedFrom", null);

        static void Set(MainViewModel vm, string field, object? value) =>
            typeof(MainViewModel).GetField(field,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .SetValue(vm, value);
    }

    private static ReferenceStrip GiveFootage(MainViewModel vm, int cells, int from)
    {
        var strip = new ReferenceStrip { Name = "shot", VideoPath = "clip.mp4" };
        for (var i = 0; i < cells; i++) strip.Cells.Add(new ReferenceCell());
        strip.LayOutFrom(from);
        vm.Doc.Scene.References = [strip];
        return strip;
    }

    [AvaloniaFact]
    public void SplittingAudioAtThePlayheadMakesTwoBars()
    {
        var (_, vm) = Open();
        GiveAudio(vm, sourceFrames: 12);
        vm.CurrentFrameIndex = 5;

        Assert.True(vm.SplitAudioAtPlayhead());

        var bars = vm.TimelineAudioClips;
        Assert.Equal(2, bars.Count);
        Assert.Equal((0, 4), (bars[0].Start, bars[0].End));
        Assert.Equal((5, 11), (bars[1].Start, bars[1].End));
        // Named apart, or two bars on one band are unreadable.
        Assert.NotEqual(bars[0].Name, bars[1].Name);
    }

    [AvaloniaFact]
    public void AudioSectionsNeverRideOverEachOther()
    {
        var (_, vm) = Open();
        GiveAudio(vm, sourceFrames: 12);
        vm.CurrentFrameIndex = 6;
        vm.SplitAudioAtPlayhead();

        // Drag the tail section far to the left: it stops where the head ends.
        vm.SlideAudioClip(1, -50);

        var bars = vm.TimelineAudioClips;
        Assert.Equal(2, bars.Count);
        Assert.True(bars[1].Start > bars[0].End,
            $"sections overlap: {bars[0].Start}..{bars[0].End} and {bars[1].Start}..{bars[1].End}");
    }

    [AvaloniaFact]
    public void MovingASectionMovesTheSoundUnderIt()
    {
        var (_, vm) = Open();
        GiveAudio(vm, sourceFrames: 12);
        vm.CurrentFrameIndex = 4;
        vm.SplitAudioAtPlayhead();

        vm.SlideAudioClip(1, 6);

        var tail = vm.Doc.Scene.Audio!.Segments![1];
        Assert.Equal(10, tail.AtFrame);
        // The source window is untouched — the sound moved, it was not re-cut.
        Assert.Equal(4, tail.SourceStartFrames);
        Assert.Equal(8, tail.LengthFrames);
    }

    [AvaloniaFact]
    public void ASplitAudioTrackExportsLaidOutOnTheTimeline()
    {
        var (_, vm) = Open();
        GiveAudio(vm, sourceFrames: 8);
        vm.Doc.Scene.FrameCount = 16;
        vm.CurrentFrameIndex = 4;
        vm.SplitAudioAtPlayhead();
        vm.SlideAudioClip(1, 6);   // a silent gap opens between the sections

        var path = vm.ResolvedAudioPathForExport();

        Assert.NotNull(path);
        var written = WavCodec.Decode(File.ReadAllBytes(path!));
        var fps = vm.Doc.Scene.Fps;
        // Frame 0 is inside the head section, frame 5 is in the gap the slide
        // opened, frame 11 is inside the tail where it now sits.
        Assert.True(Math.Abs(At(written, 0, fps)) > 0.2, "the head section is audible");
        Assert.True(Math.Abs(At(written, 5, fps)) < 0.01, "the gap is silent");
        Assert.True(Math.Abs(At(written, 11, fps)) > 0.2, "the moved section is audible where it landed");
        File.Delete(path!);

        static float At(AudioClip clip, int frame, int fps) =>
            clip.Samples[(int)((long)frame * clip.SampleRate / Math.Max(1, fps) * clip.Channels) + 4];
    }

    [AvaloniaFact]
    public void SplittingFootageAtThePlayheadMakesTwoBarsFromOne()
    {
        var (_, vm) = Open();
        GiveFootage(vm, cells: 6, from: 0);
        vm.CurrentFrameIndex = 3;

        Assert.True(vm.SplitVideoAtPlayhead(0));

        var bars = vm.TimelineVideoClips;
        Assert.Equal(2, bars.Count);
        Assert.Equal((0, 2), (bars[0].Start, bars[0].End));
        Assert.Equal((3, 5), (bars[1].Start, bars[1].End));
    }

    [AvaloniaFact]
    public void SlidingOneFootageSectionLeavesItsNeighbourAlone()
    {
        var (_, vm) = Open();
        var strip = GiveFootage(vm, cells: 6, from: 0);
        vm.CurrentFrameIndex = 3;
        vm.SplitVideoAtPlayhead(0);

        vm.SlideVideoClip(0, runStart: 3, deltaFrames: 4);

        var bars = vm.TimelineVideoClips;
        Assert.Equal(2, bars.Count);
        Assert.Equal((0, 2), (bars[0].Start, bars[0].End));
        Assert.Equal((7, 9), (bars[1].Start, bars[1].End));
        // The cells themselves never changed — only where they are exposed.
        Assert.Equal(3, strip.Slots[7]);
    }

    [AvaloniaFact]
    public void TheSplitIsRefusedWhereThereIsNothingToCut()
    {
        var (_, vm) = Open();
        GiveFootage(vm, cells: 4, from: 2);
        GiveAudio(vm, sourceFrames: 6);

        vm.CurrentFrameIndex = 0;   // before the footage starts
        Assert.False(vm.SplitVideoAtPlayhead(0));
        Assert.False(vm.SplitAudioAtPlayhead());   // the clip's own first frame
        Assert.Null(vm.Doc.Scene.Audio!.Segments);
        Assert.Null(vm.Doc.Scene.References![0].SplitPoints);
    }
}
