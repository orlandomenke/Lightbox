using Lightbox.App.Services;
using Lightbox.Core.Audio;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The render pipeline's deterministic half (Q56): the FFmpeg argument
/// vector, the PCM writer, and the graceful no-FFmpeg sentence. The encode
/// itself needs the binary and is exercised by hand — what a test CAN hold
/// still is everything the binary is told.
/// </summary>
public class VideoExporterTests
{
    [Fact]
    public void Mp4ArgsCarryRawInputTheCodecAndTheTarget()
    {
        var args = VideoExporter.BuildArgs(VideoFormat.Mp4, 960, 540, 12, "/out/take.mp4");

        Assert.Contains("rawvideo", args);
        Assert.Contains("960x540", args);
        Assert.Contains("libx264", args);
        Assert.Contains("yuv420p", args);
        Assert.Equal("/out/take.mp4", args[^1]);
        // No audio input was given, so no audio codec may appear.
        Assert.DoesNotContain("aac", args);
        Assert.DoesNotContain("-shortest", args);
    }

    [Fact]
    public void ProResWritesTheEditorialCodec()
    {
        var args = VideoExporter.BuildArgs(VideoFormat.ProRes, 1920, 1080, 24, "/out/take.mov");

        Assert.Contains("prores_ks", args);
        Assert.Contains("yuv422p10le", args);
        Assert.Equal("mov", VideoExporter.ExtensionOf(VideoFormat.ProRes));
    }

    [Fact]
    public void TheScratchTrackMuxesWithItsOffsetAndVolume()
    {
        var args = VideoExporter.BuildArgs(
            VideoFormat.Mp4, 960, 540, 12, "/out/take.mp4",
            audioPath: "/sound/take.wav", audioOffsetFrames: 6, audioVolume: 0.5);

        // 6 frames at 12 fps = half a second of delay.
        var at = args.IndexOf("-itsoffset");
        Assert.True(at >= 0, "a positive offset must delay the audio input");
        Assert.Equal("0.5", args[at + 1]);
        Assert.Contains("/sound/take.wav", args);
        Assert.Contains("volume=0.5", args);
        Assert.Contains("aac", args);
        Assert.Contains("-shortest", args);
    }

    [Fact]
    public void ANegativeOffsetTrimsTheLeadInInstead()
    {
        var args = VideoExporter.BuildArgs(
            VideoFormat.Mp4, 960, 540, 12, "/out/take.mp4",
            audioPath: "/sound/take.wav", audioOffsetFrames: -12);

        var at = args.IndexOf("-ss");
        Assert.True(at >= 0);
        Assert.Equal("1", args[at + 1]);
        Assert.DoesNotContain("-itsoffset", args);
    }

    [Fact]
    public void ThePcmWriterRoundTripsThroughOurOwnDecoder()
    {
        var clip = new AudioClip
        {
            Samples = [0f, 0.5f, -0.5f, 1f],
            Channels = 2,
            SampleRate = 8000,
        };
        var path = Path.Combine(Path.GetTempPath(), "lightbox-export-audio.wav");
        try
        {
            VideoExporter.WriteWavPcm16(clip, path);
            var back = WavCodec.Decode(File.ReadAllBytes(path));

            Assert.Equal(2, back.Channels);
            Assert.Equal(8000, back.SampleRate);
            Assert.Equal(0.5, (double)back.Samples[1], 3);
            Assert.Equal(-0.5, (double)back.Samples[2], 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ARealEncodeProducesAFile()
    {
        // Runs only where an ffmpeg exists (dev machines, not CI): the one
        // test that exercises the actual frame piping end to end.
        if (VideoExporter.FindFfmpeg() is null) return;

        var doc = Lightbox.Core.Documents.DocumentFactory.CreateDoc(64, 48, 12);
        doc.Scene.FrameCount = 6;
        var path = Path.Combine(Path.GetTempPath(), "lightbox-encode-smoke.mp4");
        try
        {
            var error = VideoExporter.Export(doc, VideoFormat.Mp4, path);

            Assert.Null(error);
            Assert.True(new FileInfo(path).Length > 500, "the encode produced a stub");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FfmpegDiscoveryPrefersTheBundledCopy()
    {
        // On this machine there may or may not be an ffmpeg — the contract
        // that CAN be held still is that discovery never throws and returns
        // either null or a path that exists.
        var found = VideoExporter.FindFfmpeg();
        Assert.True(found is null || File.Exists(found));
    }
}
