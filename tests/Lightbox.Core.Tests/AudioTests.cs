using Lightbox.Core.Audio;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>The audio track is optional the way the camera is (Q59).</summary>
public class AudioTrackTests
{
    [Fact]
    public void ANewSceneHasNoAudio()
    {
        Assert.Null(new Scene().Audio);
        Assert.Null(DocumentFactory.CreateDoc(100, 100, 12).Scene.Audio);
    }

    [Fact]
    public void ADocumentWithoutAudio_SerializesWithNoAudioKeyAtAll()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        var json = DocJson.Serialize(doc);

        // Not "audio": null — absent. A document that never adds sound must
        // not carry audio machinery in every diff because the feature exists.
        Assert.DoesNotContain("\"audio\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddingThenRemovingAudio_ReturnsTheDocumentToItsOriginalBytes()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        var before = DocJson.Serialize(doc);

        doc.Scene.Audio = new AudioTrack { Path = "take3.wav", OffsetFrames = 4, Volume = 0.8 };
        Assert.NotEqual(before, DocJson.Serialize(doc));

        doc.Scene.Audio = null;
        Assert.Equal(before, DocJson.Serialize(doc));
    }

    [Fact]
    public void TheTrackRoundTrips()
    {
        var doc = DocumentFactory.CreateDoc(100, 100, 12);
        doc.Scene.Audio = new AudioTrack { Path = "dialogue.wav", OffsetFrames = -3, Volume = 0.5, Muted = true };

        var back = DocJson.Deserialize(DocJson.Serialize(doc))!;

        Assert.Equal("dialogue.wav", back.Scene.Audio!.Path);
        Assert.Equal(-3, back.Scene.Audio.OffsetFrames);
        Assert.Equal(0.5, back.Scene.Audio.Volume, 6);
        Assert.True(back.Scene.Audio.Muted);
    }
}

public class WavCodecTests
{
    /// <summary>A minimal PCM WAV around the given data chunk.</summary>
    private static byte[] Wav(int channels, int sampleRate, int bits, byte[] data, int format = 1)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + data.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)format);
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * bits / 8);
        w.Write((short)(channels * bits / 8));
        w.Write((short)bits);
        w.Write("data"u8);
        w.Write(data.Length);
        w.Write(data);
        return ms.ToArray();
    }

    [Fact]
    public void SixteenBitPcmDecodesToUnitFloats()
    {
        // Three samples: silence, full positive, full negative.
        var data = new byte[6];
        BitConverter.GetBytes((short)0).CopyTo(data, 0);
        BitConverter.GetBytes(short.MaxValue).CopyTo(data, 2);
        BitConverter.GetBytes(short.MinValue).CopyTo(data, 4);

        var clip = WavCodec.Decode(Wav(1, 44100, 16, data));

        Assert.Equal(1, clip.Channels);
        Assert.Equal(44100, clip.SampleRate);
        Assert.Equal(3, clip.SampleFrames);
        Assert.Equal(0.0, (double)clip.Samples[0], 4);
        Assert.Equal(1.0, (double)clip.Samples[1], 3);
        Assert.Equal(-1.0, (double)clip.Samples[2], 4);
    }

    [Fact]
    public void EightBitPcmIsUnsignedAndCentredOn128()
    {
        var clip = WavCodec.Decode(Wav(1, 8000, 8, [128, 255, 0]));

        Assert.Equal(0.0, (double)clip.Samples[0], 4);
        Assert.True(clip.Samples[1] > 0.9f);
        Assert.Equal(-1.0, (double)clip.Samples[2], 4);
    }

    [Fact]
    public void FloatWavDecodesVerbatim()
    {
        var data = new byte[8];
        BitConverter.GetBytes(0.25f).CopyTo(data, 0);
        BitConverter.GetBytes(-0.75f).CopyTo(data, 4);

        var clip = WavCodec.Decode(Wav(1, 48000, 32, data, format: 3));

        Assert.Equal(0.25, (double)clip.Samples[0], 5);
        Assert.Equal(-0.75, (double)clip.Samples[1], 5);
    }

    [Fact]
    public void StereoInterleavesAndMixesDown()
    {
        // One sample frame: left +1, right −1 — the mixdown is silence.
        var data = new byte[4];
        BitConverter.GetBytes(short.MaxValue).CopyTo(data, 0);
        BitConverter.GetBytes(short.MinValue).CopyTo(data, 2);

        var clip = WavCodec.Decode(Wav(2, 44100, 16, data));

        Assert.Equal(2, clip.Channels);
        Assert.Equal(1, clip.SampleFrames);
        Assert.Equal(0.0, clip.MonoMixdown()[0], 3);
    }

    [Fact]
    public void SomethingThatIsNotAWavSaysSo()
    {
        Assert.Throws<FormatException>(() => WavCodec.Decode("OggS this is not a wav"u8.ToArray()));
    }

    [Fact]
    public void ACompressedWavNamesTheEncodingInsteadOfGuessing()
    {
        // Format 85 is MP3-in-WAV; the reader must refuse it legibly.
        var ex = Record.Exception(() => WavCodec.Decode(Wav(1, 44100, 16, new byte[4], format: 85)));
        Assert.IsType<FormatException>(ex);
        Assert.Contains("format 85", ex.Message);
    }
}

public class AudioPeaksTests
{
    [Fact]
    public void PeaksLandOnTheFramesTheSoundPlaysUnder()
    {
        // 1 second at 10 samples/s, 2 fps → 5 samples per animation frame.
        // Loud in the first half, silent in the second.
        var mono = new float[10];
        for (var i = 0; i < 5; i++) mono[i] = i % 2 == 0 ? 0.8f : -0.8f;

        var peaks = AudioPeaks.Build(mono, sampleRate: 10, fps: 2, frameCount: 3);

        Assert.Equal(0.8, (double)peaks[0].Max, 4);
        Assert.Equal(-0.8, (double)peaks[0].Min, 4);
        Assert.Equal(0.0, (double)peaks[1].Max, 4);
        Assert.Equal(0.0, (double)peaks[2].Max, 4);   // past the sound: flat
    }

    [Fact]
    public void TheOffsetSlidesTheSoundDownTheTimeline()
    {
        var mono = new float[5];
        Array.Fill(mono, 0.5f);

        var peaks = AudioPeaks.Build(mono, sampleRate: 10, fps: 2, frameCount: 4, offsetFrames: 2);

        Assert.Equal(0.0, peaks[0].Max, 4);
        Assert.Equal(0.0, peaks[1].Max, 4);
        Assert.Equal(0.5, peaks[2].Max, 4);
    }

    [Fact]
    public void DegenerateInputsProduceSilenceNotCrashes()
    {
        Assert.All(AudioPeaks.Build([], 44100, 12, 5), p => Assert.Equal(0f, p.Max));
        Assert.Empty(AudioPeaks.Build(new float[10], 44100, 12, 0));
        Assert.All(AudioPeaks.Build(new float[10], 0, 12, 3), p => Assert.Equal(0f, p.Max));
    }
}
