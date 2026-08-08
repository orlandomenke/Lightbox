namespace Lightbox.Core.Audio;

/// <summary>
/// Decoded sound: interleaved float samples in −1..1, however many channels
/// the file carried. Mono mixdown is a method rather than a second copy —
/// waveform peaks and scrubbing want mono, playback wants the channels.
/// </summary>
public sealed class AudioClip
{
    public required float[] Samples { get; init; }

    public required int Channels { get; init; }

    public required int SampleRate { get; init; }

    public int SampleFrames => Channels == 0 ? 0 : Samples.Length / Channels;

    public double DurationSeconds => SampleRate == 0 ? 0 : (double)SampleFrames / SampleRate;

    /// <summary>Channels averaged into one, for peaks and scrub snippets.</summary>
    public float[] MonoMixdown()
    {
        if (Channels <= 1) return Samples;
        var mono = new float[SampleFrames];
        for (var i = 0; i < mono.Length; i++)
        {
            float sum = 0;
            for (var c = 0; c < Channels; c++) sum += Samples[i * Channels + c];
            mono[i] = sum / Channels;
        }
        return mono;
    }
}

/// <summary>
/// A plain RIFF/WAVE reader: PCM at 8, 16, 24 and 32 bits, and IEEE float 32.
/// Managed on purpose (Q55) — decoding is deterministic data work, and the
/// only native surface the audio feature is allowed is the output device.
/// </summary>
public static class WavCodec
{
    /// <summary>Decode a WAV byte stream, or throw <see cref="FormatException"/> saying what it is not.</summary>
    public static AudioClip Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WAVE"u8))
        {
            throw new FormatException("Not a RIFF/WAVE file.");
        }

        int channels = 0, sampleRate = 0, bitsPerSample = 0, format = 0;
        ReadOnlySpan<byte> data = default;

        // Walk the chunks; fmt and data are the two that matter, and a
        // well-formed file may put LIST or bext between them.
        var at = 12;
        while (at + 8 <= bytes.Length)
        {
            var id = bytes.Slice(at, 4);
            var size = ReadInt(bytes, at + 4);
            var body = at + 8;
            if (size < 0 || body + size > bytes.Length)
            {
                // A truncated final chunk: take what is actually there.
                size = bytes.Length - body;
                if (size <= 0) break;
            }

            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = ReadShort(bytes, body);
                channels = ReadShort(bytes, body + 2);
                sampleRate = ReadInt(bytes, body + 4);
                bitsPerSample = ReadShort(bytes, body + 14);
                // WAVE_FORMAT_EXTENSIBLE wraps the real format in a GUID whose
                // first two bytes are the plain code.
                if (format == 0xFFFE && size >= 26)
                {
                    format = ReadShort(bytes, body + 24);
                }
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = bytes.Slice(body, size);
            }

            at = body + size + (size & 1);   // chunks are word-aligned
        }

        if (channels <= 0 || sampleRate <= 0) throw new FormatException("WAV has no fmt chunk.");
        if (data.IsEmpty) throw new FormatException("WAV has no data chunk.");

        var samples = (format, bitsPerSample) switch
        {
            (1, 8) => From8(data),
            (1, 16) => From16(data),
            (1, 24) => From24(data),
            (1, 32) => From32(data),
            (3, 32) => FromFloat(data),
            _ => throw new FormatException(
                $"Unsupported WAV encoding (format {format}, {bitsPerSample}-bit). PCM and float files work."),
        };

        // Drop a trailing partial sample frame rather than misaligning every
        // channel after it.
        var whole = samples.Length / channels * channels;
        if (whole != samples.Length) Array.Resize(ref samples, whole);

        return new AudioClip { Samples = samples, Channels = channels, SampleRate = sampleRate };
    }

    private static float[] From8(ReadOnlySpan<byte> d)
    {
        // 8-bit WAV is unsigned, centred on 128.
        var s = new float[d.Length];
        for (var i = 0; i < d.Length; i++) s[i] = (d[i] - 128) / 128f;
        return s;
    }

    private static float[] From16(ReadOnlySpan<byte> d)
    {
        var s = new float[d.Length / 2];
        for (var i = 0; i < s.Length; i++)
        {
            s[i] = (short)(d[i * 2] | (d[i * 2 + 1] << 8)) / 32768f;
        }
        return s;
    }

    private static float[] From24(ReadOnlySpan<byte> d)
    {
        var s = new float[d.Length / 3];
        for (var i = 0; i < s.Length; i++)
        {
            var v = d[i * 3] | (d[i * 3 + 1] << 8) | (d[i * 3 + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
            s[i] = v / 8388608f;
        }
        return s;
    }

    private static float[] From32(ReadOnlySpan<byte> d)
    {
        var s = new float[d.Length / 4];
        for (var i = 0; i < s.Length; i++) s[i] = ReadInt(d, i * 4) / 2147483648f;
        return s;
    }

    private static float[] FromFloat(ReadOnlySpan<byte> d)
    {
        var s = new float[d.Length / 4];
        for (var i = 0; i < s.Length; i++)
        {
            s[i] = BitConverter.Int32BitsToSingle(ReadInt(d, i * 4));
        }
        return s;
    }

    private static int ReadInt(ReadOnlySpan<byte> b, int at) =>
        b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24);

    private static int ReadShort(ReadOnlySpan<byte> b, int at) => b[at] | (b[at + 1] << 8);
}
