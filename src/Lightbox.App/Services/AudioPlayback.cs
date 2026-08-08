using Lightbox.Core.Audio;
using Silk.NET.OpenAL;

namespace Lightbox.App.Services;

/// <summary>
/// The one native surface the audio feature is allowed (Q59): OpenAL-soft,
/// bound through Silk.NET, playing the scratch track and the scrub ticks.
///
/// Everything degrades to silence rather than failure. A machine with no
/// audio device — CI, a container, a headless test run — gets a playback
/// object whose every method is a no-op, and the rest of the application
/// never knows. Sound is an aid here, not a dependency.
/// </summary>
public sealed class AudioPlayback : IDisposable
{
    private AL? _al;
    private ALContext? _alc;
    private unsafe Device* _device;
    private unsafe Context* _context;
    private bool _initTried;

    private uint _source;
    private uint _buffer;
    private AudioClip? _buffered;

    private uint _scrubSource;
    private uint _scrubBuffer;

    /// <summary>Whether a device opened. False is a working silent mode, not an error.</summary>
    public bool Available
    {
        get
        {
            EnsureInit();
            return _al is not null;
        }
    }

    private unsafe void EnsureInit()
    {
        if (_initTried) return;
        _initTried = true;
        try
        {
            var alc = ALContext.GetApi(soft: true);
            var al = AL.GetApi(soft: true);
            var device = alc.OpenDevice("");
            if (device == null) return;
            var context = alc.CreateContext(device, null);
            if (context == null)
            {
                alc.CloseDevice(device);
                return;
            }
            alc.MakeContextCurrent(context);
            _device = device;
            _context = context;
            _alc = alc;
            _al = al;
            _source = al.GenSource();
            _scrubSource = al.GenSource();
        }
        catch
        {
            // No native library, no device, no permission: silent mode.
            _al = null;
        }
    }

    /// <summary>
    /// Play the clip from <paramref name="fromSeconds"/> at the given gain and
    /// rate. The whole clip is uploaded once and cached; replaying the same
    /// clip only seeks.
    /// </summary>
    public void Play(AudioClip clip, double fromSeconds, double volume, double rate = 1.0)
    {
        EnsureInit();
        if (_al is not { } al) return;
        if (fromSeconds < 0 || fromSeconds >= clip.DurationSeconds) return;

        al.SourceStop(_source);
        if (!ReferenceEquals(_buffered, clip))
        {
            al.SetSourceProperty(_source, SourceInteger.Buffer, 0);
            if (_buffer != 0) al.DeleteBuffer(_buffer);
            _buffer = UploadWhole(al, clip);
            _buffered = clip;
            if (_buffer == 0)
            {
                _buffered = null;
                return;
            }
            al.SetSourceProperty(_source, SourceInteger.Buffer, (int)_buffer);
        }

        al.SetSourceProperty(_source, SourceFloat.Gain, (float)Math.Clamp(volume, 0, 1));
        al.SetSourceProperty(_source, SourceFloat.Pitch, (float)Math.Clamp(rate, 0.05, 8));
        al.SetSourceProperty(_source, SourceFloat.SecOffset, (float)fromSeconds);
        al.SourcePlay(_source);
    }

    public void Stop()
    {
        if (_al is not { } al) return;
        al.SourceStop(_source);
    }

    /// <summary>Adjust the playing track's gain without restarting it.</summary>
    public void SetGain(double volume)
    {
        if (_al is not { } al) return;
        al.SetSourceProperty(_source, SourceFloat.Gain, (float)Math.Clamp(volume, 0, 1));
    }

    /// <summary>
    /// The scrub tick: the slice of sound under one frame, played as its own
    /// tiny buffer so it stops by running out — dragging the playhead reads
    /// the track a syllable at a time, the way scrubbing always has.
    /// </summary>
    public void ScrubTick(AudioClip clip, double atSeconds, double lengthSeconds, double volume)
    {
        EnsureInit();
        if (_al is not { } al) return;
        if (atSeconds < 0 || atSeconds >= clip.DurationSeconds) return;

        var mono = clip.MonoMixdown();
        var from = (int)(atSeconds * clip.SampleRate);
        var count = Math.Min((int)(Math.Clamp(lengthSeconds, 0.01, 0.25) * clip.SampleRate),
            mono.Length - from);
        if (count <= 0) return;

        var pcm = new short[count];
        for (var i = 0; i < count; i++)
        {
            pcm[i] = (short)Math.Clamp(mono[from + i] * 32767f, short.MinValue, short.MaxValue);
        }

        al.SourceStop(_scrubSource);
        al.SetSourceProperty(_scrubSource, SourceInteger.Buffer, 0);
        if (_scrubBuffer != 0) al.DeleteBuffer(_scrubBuffer);
        _scrubBuffer = al.GenBuffer();
        unsafe
        {
            fixed (short* p = pcm)
            {
                al.BufferData(_scrubBuffer, BufferFormat.Mono16, p, count * sizeof(short), clip.SampleRate);
            }
        }
        al.SetSourceProperty(_scrubSource, SourceInteger.Buffer, (int)_scrubBuffer);
        al.SetSourceProperty(_scrubSource, SourceFloat.Gain, (float)Math.Clamp(volume, 0, 1));
        al.SourcePlay(_scrubSource);
    }

    /// <summary>The whole clip as one 16-bit buffer, mono or stereo as it came.</summary>
    private static unsafe uint UploadWhole(AL al, AudioClip clip)
    {
        var channels = Math.Min(clip.Channels, 2);
        var samples = clip.Channels <= 2 ? clip.Samples : clip.MonoMixdown();
        if (clip.Channels > 2) channels = 1;

        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            pcm[i] = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
        }

        var buffer = al.GenBuffer();
        fixed (short* p = pcm)
        {
            al.BufferData(
                buffer, channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16,
                p, pcm.Length * sizeof(short), clip.SampleRate);
        }
        return al.GetError() == AudioError.NoError ? buffer : 0;
    }

    public unsafe void Dispose()
    {
        if (_al is { } al)
        {
            al.SourceStop(_source);
            al.SourceStop(_scrubSource);
            al.DeleteSource(_source);
            al.DeleteSource(_scrubSource);
            if (_buffer != 0) al.DeleteBuffer(_buffer);
            if (_scrubBuffer != 0) al.DeleteBuffer(_scrubBuffer);
        }
        if (_alc is { } alc)
        {
            alc.MakeContextCurrent(null);
            if (_context != null) alc.DestroyContext(_context);
            if (_device != null) alc.CloseDevice(_device);
        }
        _al = null;
        _alc = null;
    }
}
