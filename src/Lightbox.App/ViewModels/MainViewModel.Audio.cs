using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Audio;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The scratch track (Q55): one referenced sound file the animation is timed
/// against. Optional the way the camera is — a document without audio shows
/// none of this and pays for none of it.
/// </summary>
public partial class MainViewModel
{
    public bool HasAudio => Scene.Audio is not null;

    public string AudioFileName =>
        Scene.Audio is { } a && a.Path.Length > 0 ? System.IO.Path.GetFileName(a.Path) : "";

    /// <summary>
    /// The referenced file cannot be read. A missing file is a badge, not an
    /// error — the sound is a reference (Q55), and the timing anchored to it
    /// is still the artist's work.
    /// </summary>
    public bool AudioMissing
    {
        get
        {
            if (Scene.Audio is null) return false;
            EnsureAudioLoaded();
            return _audioClip is null;
        }
    }

    public bool AudioMuted
    {
        get => Scene.Audio?.Muted ?? false;
        set
        {
            if (Scene.Audio is not { } track || track.Muted == value) return;
            track.Muted = value;
            NotifyAudioSurface();
            _autosave.MarkDirty();
        }
    }

    public double AudioVolume
    {
        get => Scene.Audio?.Volume ?? 1.0;
        set
        {
            if (Scene.Audio is not { } track) return;
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(track.Volume - clamped) < 1e-9) return;
            track.Volume = clamped;
            NotifyAudioSurface();
            _autosave.MarkDirty();
        }
    }

    /// <summary>The frame the sound starts on; negative trims a lead-in.</summary>
    public int AudioOffsetFrames
    {
        get => Scene.Audio?.OffsetFrames ?? 0;
        set
        {
            if (Scene.Audio is not { } track || track.OffsetFrames == value) return;
            track.OffsetFrames = value;
            NotifyAudioSurface();
            _autosave.MarkDirty();
        }
    }

    /// <summary>
    /// The waveform under the timeline: one min/max pair per frame, or null
    /// when there is no audio (or the file is missing — a flat line would
    /// read as silence, and it is not silence, it is absence).
    /// </summary>
    public IReadOnlyList<AudioPeaks.Peak>? TimelineAudioPeaks
    {
        get
        {
            if (Scene.Audio is not { } track) return null;
            EnsureAudioLoaded();
            if (_audioMono is null || _audioClip is null) return null;

            var key = (track.OffsetFrames, TimelineFrameCount, Scene.Fps);
            if (_audioPeaks is null || _audioPeaksKey != key)
            {
                _audioPeaks = AudioPeaks.Build(
                    _audioMono, _audioClip.SampleRate, Scene.Fps, TimelineFrameCount, track.OffsetFrames);
                _audioPeaksKey = key;
            }
            return _audioPeaks;
        }
    }

    /// <summary>The decoded sound, for playback. Null when missing or absent.</summary>
    internal AudioClip? AudioClipNow
    {
        get
        {
            if (Scene.Audio is null) return null;
            EnsureAudioLoaded();
            return _audioClip;
        }
    }

    private string? _audioLoadedFrom;
    private AudioClip? _audioClip;
    private float[]? _audioMono;
    private AudioPeaks.Peak[]? _audioPeaks;
    private (int Offset, int Frames, int Fps) _audioPeaksKey;

    /// <summary>
    /// Import a sound file onto the document. Returns null on success, or a
    /// sentence saying why not. Paths under the document's own folder are
    /// stored relative, so a project directory moves as one thing.
    /// </summary>
    public string? ImportAudio(string path)
    {
        AudioClip clip;
        try
        {
            clip = WavCodec.Decode(File.ReadAllBytes(path));
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
        catch (IOException ex)
        {
            return ex.Message;
        }

        var stored = path;
        if (System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is { Length: > 0 } docDir)
        {
            var relative = System.IO.Path.GetRelativePath(docDir, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal)
                && !System.IO.Path.IsPathRooted(relative))
            {
                stored = relative;
            }
        }

        Scene.Audio = new AudioTrack { Path = stored };
        _audioLoadedFrom = path;
        _audioClip = clip;
        _audioMono = clip.MonoMixdown();
        _audioPeaks = null;
        NotifyAudioSurface();
        _autosave.MarkDirty();
        return null;
    }

    [RelayCommand]
    private void RemoveAudio()
    {
        if (Scene.Audio is null) return;
        Scene.Audio = null;
        DropAudioCache();
        NotifyAudioSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Decode the referenced file if the cache is not already holding it.
    /// Re-resolved on every read because the document (and its save path) can
    /// change under this partial without telling it.
    /// </summary>
    private void EnsureAudioLoaded()
    {
        if (Scene.Audio is not { } track)
        {
            DropAudioCache();
            return;
        }

        var resolved = ResolveAudioPath(track);
        if (resolved == _audioLoadedFrom) return;

        _audioLoadedFrom = resolved;
        _audioClip = null;
        _audioMono = null;
        _audioPeaks = null;
        if (resolved is null || !File.Exists(resolved)) return;
        try
        {
            _audioClip = WavCodec.Decode(File.ReadAllBytes(resolved));
            _audioMono = _audioClip.MonoMixdown();
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            _audioClip = null;
            _audioMono = null;
        }
    }

    private string? ResolveAudioPath(AudioTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.Path)) return null;
        if (System.IO.Path.IsPathRooted(track.Path)) return track.Path;
        return System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is { Length: > 0 } docDir
            ? System.IO.Path.Combine(docDir, track.Path)
            : null;
    }

    private void DropAudioCache()
    {
        _audioLoadedFrom = null;
        _audioClip = null;
        _audioMono = null;
        _audioPeaks = null;
    }

    private void NotifyAudioSurface()
    {
        OnPropertyChanged(nameof(HasAudio));
        OnPropertyChanged(nameof(AudioFileName));
        OnPropertyChanged(nameof(AudioMissing));
        OnPropertyChanged(nameof(AudioMuted));
        OnPropertyChanged(nameof(AudioVolume));
        OnPropertyChanged(nameof(AudioOffsetFrames));
        OnPropertyChanged(nameof(TimelineAudioPeaks));
    }
}
