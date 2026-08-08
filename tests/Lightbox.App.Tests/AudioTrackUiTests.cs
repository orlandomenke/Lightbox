using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The scratch track's surface (Q55): import fills the timeline's waveform,
/// a missing file is a badge rather than an error, and removal leaves the
/// document exactly as it was.
/// </summary>
[Collection("BrushState")]
public sealed class AudioTrackUiTests : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, (MainViewModel)window.DataContext!);
    }

    /// <summary>One second of full-scale square wave, 16-bit mono WAV, on disk.</summary>
    private static string WriteWav(string name, int sampleRate = 8000)
    {
        var data = new byte[sampleRate * 2];
        for (var i = 0; i < sampleRate; i++)
        {
            var v = i % 2 == 0 ? short.MaxValue : short.MinValue;
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }

        var path = Path.Combine(Path.GetTempPath(), name);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("RIFF"u8);
            w.Write(36 + data.Length);
            w.Write("WAVE"u8);
            w.Write("fmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(sampleRate);
            w.Write(sampleRate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(data.Length);
            w.Write(data);
        }
        File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    [AvaloniaFact]
    public void ImportingASoundFillsTheWaveform()
    {
        var (_, vm) = Open();
        var wav = WriteWav("lightbox-audio-import.wav");
        try
        {
            Assert.False(vm.HasAudio);
            Assert.Null(vm.TimelineAudioPeaks);

            Assert.Null(vm.ImportAudio(wav));

            Assert.True(vm.HasAudio);
            Assert.False(vm.AudioMissing);
            var peaks = vm.TimelineAudioPeaks;
            Assert.NotNull(peaks);
            // The default document is one frame long; the sound is loud under it.
            Assert.True(peaks![0].Max > 0.9f, $"waveform is flat (max {peaks[0].Max})");
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [AvaloniaFact]
    public void SomethingThatIsNotAWavReportsInsteadOfThrowing()
    {
        var (_, vm) = Open();
        var bad = Path.Combine(Path.GetTempPath(), "lightbox-audio-bad.wav");
        File.WriteAllText(bad, "not audio at all");
        try
        {
            var error = vm.ImportAudio(bad);

            Assert.NotNull(error);
            Assert.False(vm.HasAudio, "a failed import must not leave a half-attached track");
        }
        finally
        {
            File.Delete(bad);
        }
    }

    [AvaloniaFact]
    public void AMissingFileIsABadgeNotAnError()
    {
        // The shape a loaded document has when its referenced sound is gone:
        // the track is in the record, the file is not on disk.
        var (_, vm) = Open();
        vm.Doc.Scene.Audio = new Lightbox.Core.Documents.AudioTrack
        {
            Path = Path.Combine(Path.GetTempPath(), "lightbox-never-existed.wav"),
        };

        Assert.True(vm.HasAudio, "the track survives its file going missing");
        Assert.True(vm.AudioMissing);
        Assert.Null(vm.TimelineAudioPeaks);
    }

    [AvaloniaFact]
    public void RemovingTheTrackLeavesNoAudioBehind()
    {
        var (_, vm) = Open();
        var wav = WriteWav("lightbox-audio-remove.wav");
        try
        {
            Assert.Null(vm.ImportAudio(wav));
            vm.RemoveAudioCommand.Execute(null);

            Assert.False(vm.HasAudio);
            Assert.Null(vm.TimelineAudioPeaks);
            var json = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);
            Assert.DoesNotContain("\"audio\"", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [AvaloniaFact]
    public void PlaybackAndScrubbingAreSafeWhereverThereIsNoAudioDevice()
    {
        // CI and containers have no sound hardware; every playback path must
        // be a silent no-op there, never a throw (Q55).
        var (_, vm) = Open();
        var wav = WriteWav("lightbox-audio-play.wav");
        try
        {
            var cell = vm.FrameCells.First(c => c.Index == 0);
            for (var i = 0; i < 5; i++) vm.ExtendExposureAt(cell);
            Assert.Null(vm.ImportAudio(wav));

            vm.PlayCommand.Execute(null);
            vm.StepPlayback();
            vm.AudioMuted = true;    // mute mid-play
            vm.StepPlayback();
            vm.AudioMuted = false;
            vm.PauseCommand.Execute(null);
            Assert.False(vm.IsPlaying);

            vm.CurrentFrameIndex = 2;   // the scrub path
            vm.AudioVolume = 0.5;       // the live-gain path
        }
        finally
        {
            File.Delete(wav);
        }
    }

    [AvaloniaFact]
    public void TheOffsetSlidesTheWaveform()
    {
        var (_, vm) = Open();
        var wav = WriteWav("lightbox-audio-offset.wav");
        try
        {
            // Room for the slide: the default document is one frame long.
            var cell = vm.FrameCells.First(c => c.Index == 0);
            for (var i = 0; i < 5; i++) vm.ExtendExposureAt(cell);
            Assert.Null(vm.ImportAudio(wav));
            Assert.True(vm.TimelineAudioPeaks![0].Max > 0.9f);

            vm.AudioOffsetFrames = 3;

            var peaks = vm.TimelineAudioPeaks!;
            Assert.Equal(0.0, peaks[0].Max, 4);
            Assert.True(peaks[3].Max > 0.9f, "the sound did not move to its offset");
        }
        finally
        {
            File.Delete(wav);
        }
    }
}
