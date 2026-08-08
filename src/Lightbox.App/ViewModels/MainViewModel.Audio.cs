using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Audio;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The scratch track (Q59): one referenced sound file the animation is timed
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
    /// error — the sound is a reference (Q59), and the timing anchored to it
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
            TickAudio();   // muting mid-play stops the sound now, not next tick
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
            _audioPlayback.SetGain(clamped);   // live, so the slider is audible
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

            var key = (track.OffsetFrames, TimelineFrameCount, Scene.Fps,
                track.TrimStartFrames, track.TrimLengthFrames, _audioSegmentsVersion);
            if (_audioPeaks is null || _audioPeaksKey != key)
            {
                _audioPeaks = AudioPeaks.Build(
                    _audioMono, _audioClip.SampleRate, Scene.Fps, TimelineFrameCount,
                    AudioSegmentsNow);
                _audioPeaksKey = key;
            }
            return _audioPeaks;
        }
    }

    /// <summary>The source's length in timeline frames at the scene's fps.</summary>
    internal int AudioSourceFrames =>
        AudioClipNow is { } clip
            ? (int)Math.Ceiling(clip.DurationSeconds * Math.Max(1, Scene.Fps))
            : 0;

    /// <summary>
    /// The clip bar's span on the timeline (Q57): where the trimmed clip
    /// starts and how many frames of it play. Null without decodable audio.
    /// </summary>
    public (int Start, int Length)? AudioClipSpan
    {
        get
        {
            if (Scene.Audio is not { } track) return null;
            var total = AudioSourceFrames;
            if (total <= 0) return null;
            var start = Math.Clamp(track.TrimStartFrames, 0, total - 1);
            var length = Math.Clamp(track.TrimLengthFrames ?? total - start, 1, total - start);
            return (track.OffsetFrames, length);
        }
    }

    /// <summary>The clip's sections in timeline order (Q57): stored when split, implicit before.</summary>
    internal IReadOnlyList<AudioSegment> AudioSegmentsNow =>
        Scene.Audio is { } track ? track.EffectiveSegments(AudioSourceFrames) : [];

    /// <summary>Bumped by every section edit — the peak cache's invalidation key.</summary>
    private int _audioSegmentsVersion;

    /// <summary>One bar per section (Q57); the bar's StripIndex carries the section index.</summary>
    public IReadOnlyList<Controls.ClipBar> TimelineAudioClips
    {
        get
        {
            var segments = AudioSegmentsNow;
            var bars = new List<Controls.ClipBar>(segments.Count);
            for (var i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                bars.Add(new Controls.ClipBar(
                    segments.Count == 1 ? "Audio" : $"Audio {i + 1}",
                    s.AtFrame, s.AtFrame + s.LengthFrames - 1, i));
            }
            return bars;
        }
    }

    /// <summary>
    /// Split the section under the playhead in two (Q57). The first split
    /// materialises the implicit section; false when the playhead sits on an
    /// edge or outside the clip — an edge has nothing to split.
    /// </summary>
    public bool SplitAudioAtPlayhead()
    {
        if (Scene.Audio is not { } track) return false;
        var total = AudioSourceFrames;
        if (total <= 0 || !track.SplitAt(CurrentFrameIndex, total)) return false;
        _audioSegmentsVersion++;
        NotifyAudioSurface();
        _autosave.MarkDirty();
        return true;
    }

    /// <summary>
    /// A section's body drag: it slides along the timeline, stopped by its
    /// neighbours — sections never overlap, because two sounds under one
    /// frame is not a timing, it is a mix, and this is a scratch track.
    /// </summary>
    public void SlideAudioClip(int segmentIndex, int deltaFrames)
    {
        if (Scene.Audio is not { } track || deltaFrames == 0) return;
        if (track.Segments is not { Count: > 0 })
        {
            if (segmentIndex != 0) return;
            track.OffsetFrames += deltaFrames;   // unsplit: the field is the record
        }
        else
        {
            var segments = OrderedSegments(track);
            if (segmentIndex < 0 || segmentIndex >= segments.Count) return;
            var d = ClampSegmentSlide(segments, segmentIndex, deltaFrames);
            if (d == 0) return;
            segments[segmentIndex].AtFrame += d;
        }
        _audioSegmentsVersion++;
        NotifyAudioSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Drag a section's IN edge (Q57): +d eats d more source frames off its
    /// head and the left edge follows; the tail stays anchored. −d restores
    /// hidden source, bounded by the source itself and by the section before.
    /// </summary>
    public void TrimAudioClipIn(int segmentIndex, int deltaFrames)
    {
        if (Scene.Audio is not { } track || deltaFrames == 0) return;
        var total = AudioSourceFrames;
        if (total <= 0) return;

        if (track.Segments is not { Count: > 0 })
        {
            if (segmentIndex != 0) return;
            var start = Math.Clamp(track.TrimStartFrames, 0, total - 1);
            var length = Math.Clamp(track.TrimLengthFrames ?? total - start, 1, total - start);
            var d = Math.Clamp(deltaFrames, -start, length - 1);
            if (d == 0) return;
            track.TrimStartFrames = start + d;
            track.TrimLengthFrames = length - d;
            track.OffsetFrames += d;
        }
        else
        {
            var segments = OrderedSegments(track);
            if (segmentIndex < 0 || segmentIndex >= segments.Count) return;
            var s = segments[segmentIndex];
            var minByTimeline = segmentIndex > 0
                ? segments[segmentIndex - 1].AtFrame + segments[segmentIndex - 1].LengthFrames - s.AtFrame
                : int.MinValue / 2;
            var d = Math.Clamp(deltaFrames, Math.Max(-s.SourceStartFrames, minByTimeline), s.LengthFrames - 1);
            if (d == 0) return;
            s.SourceStartFrames += d;
            s.LengthFrames -= d;
            s.AtFrame += d;
        }
        _audioSegmentsVersion++;
        NotifyAudioSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Drag a section's OUT edge: +d plays d more source frames, bounded by
    /// the source's end and by the section after it.
    /// </summary>
    public void TrimAudioClipOut(int segmentIndex, int deltaFrames)
    {
        if (Scene.Audio is not { } track || deltaFrames == 0) return;
        var total = AudioSourceFrames;
        if (total <= 0) return;

        if (track.Segments is not { Count: > 0 })
        {
            if (segmentIndex != 0) return;
            var start = Math.Clamp(track.TrimStartFrames, 0, total - 1);
            var length = Math.Clamp(track.TrimLengthFrames ?? total - start, 1, total - start);
            var grown = Math.Clamp(length + deltaFrames, 1, total - start);
            if (grown == length) return;
            track.TrimLengthFrames = grown;
        }
        else
        {
            var segments = OrderedSegments(track);
            if (segmentIndex < 0 || segmentIndex >= segments.Count) return;
            var s = segments[segmentIndex];
            var maxBySource = total - s.SourceStartFrames;
            var maxByTimeline = segmentIndex < segments.Count - 1
                ? segments[segmentIndex + 1].AtFrame - s.AtFrame
                : int.MaxValue / 2;
            var grown = Math.Clamp(s.LengthFrames + deltaFrames, 1, Math.Min(maxBySource, maxByTimeline));
            if (grown == s.LengthFrames) return;
            s.LengthFrames = grown;
        }
        _audioSegmentsVersion++;
        NotifyAudioSurface();
        _autosave.MarkDirty();
    }

    private static List<AudioSegment> OrderedSegments(AudioTrack track)
    {
        track.Segments!.Sort((a, b) => a.AtFrame.CompareTo(b.AtFrame));
        return track.Segments;
    }

    private static int ClampSegmentSlide(List<AudioSegment> segments, int index, int delta)
    {
        var s = segments[index];
        var min = index > 0
            ? segments[index - 1].AtFrame + segments[index - 1].LengthFrames - s.AtFrame
            : int.MinValue / 2;
        var max = index < segments.Count - 1
            ? segments[index + 1].AtFrame - s.LengthFrames - s.AtFrame
            : int.MaxValue / 2;
        return Math.Clamp(delta, min, max);
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
    private string? _audioLoadedData;   // reference identity of the embedded bytes the cache decoded
    private AudioClip? _audioClip;
    private float[]? _audioMono;
    private AudioPeaks.Peak[]? _audioPeaks;
    private (int Offset, int Frames, int Fps, int TrimStart, int? TrimLength, int Version) _audioPeaksKey;

    /// <summary>
    /// Import a sound file onto the document. Returns null on success, or a
    /// sentence saying why not. Paths under the document's own folder are
    /// stored relative, so a project directory moves as one thing.
    /// <paramref name="embed"/> stores the bytes in the document instead
    /// (Q57) — the self-contained choice, made knowingly at import.
    /// </summary>
    public string? ImportAudio(string path, bool embed = false)
    {
        byte[] bytes;
        AudioClip clip;
        try
        {
            bytes = File.ReadAllBytes(path);
            clip = WavCodec.Decode(bytes);
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

        var track = new AudioTrack { Path = stored };
        if (embed) track.Data = Convert.ToBase64String(bytes);
        Scene.Audio = track;
        _audioLoadedFrom = embed ? null : path;
        _audioLoadedData = track.Data;
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

        // Embedded bytes win over the path (Q57). Reference identity is the
        // cache key: the base64 string only changes when the track does.
        if (track.Data is { } data)
        {
            if (ReferenceEquals(data, _audioLoadedData) && _audioClip is not null) return;
            DropAudioCache();
            _audioLoadedData = data;
            try
            {
                _audioClip = WavCodec.Decode(Convert.FromBase64String(data));
                _audioMono = _audioClip.MonoMixdown();
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                _audioClip = null;
                _audioMono = null;
            }
            return;
        }

        var resolved = ResolveAudioPath(track);
        if (resolved == _audioLoadedFrom && _audioLoadedData is null) return;

        DropAudioCache();
        _audioLoadedFrom = resolved;
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

    /// <summary>
    /// The sound file a video export should mux, or null when there is
    /// nothing to hear — no track, muted, or the file is missing. Embedded
    /// audio (Q57) has no file, so its bytes are written to a temp WAV for
    /// FFmpeg to read; the temp lives until process exit, which outlasts the
    /// encode.
    /// </summary>
    internal string? ResolvedAudioPathForExport()
    {
        if (Scene.Audio is not { } track || track.Muted) return null;
        if (track.Data is { } data)
        {
            try
            {
                var temp = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"lightbox-export-{Scene.Id}.wav");
                File.WriteAllBytes(temp, Convert.FromBase64String(data));
                return temp;
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                return null;
            }
        }
        var resolved = ResolveAudioPath(track);
        return resolved is not null && File.Exists(resolved) ? resolved : null;
    }

    private void DropAudioCache()
    {
        _audioLoadedFrom = null;
        _audioLoadedData = null;
        _audioClip = null;
        _audioMono = null;
        _audioPeaks = null;
    }

    // ---- playback (Q59: OpenAL-soft, silent where there is no device) ---------

    private readonly Services.AudioPlayback _audioPlayback = new();
    private bool _audioRunning;

    /// <summary>
    /// Keep the sound where the playhead is. Called when playback starts and
    /// on every step; cheap when nothing changed. Backwards playback is
    /// silent on purpose — reversed audio is noise, not information.
    /// </summary>
    private void TickAudio()
    {
        if (!IsPlaying || _playDirection < 0
            || Scene.Audio is not { } track || track.Muted || AudioClipNow is not { } clip)
        {
            StopAudio();
            return;
        }

        // The section under the playhead decides what plays (Q57): its own
        // window of the source, at its own place. Crossing a cut or a gap
        // restarts the stream at the right sample — a straight run through
        // one section stays one uninterrupted play.
        if (AudioSourcePositionAt(CurrentFrameIndex, clip) is not var (t, segmentIndex) || segmentIndex < 0)
        {
            StopAudio();
            return;
        }
        if (_audioRunning && _audioRunningSegment == segmentIndex) return;

        _audioPlayback.Play(clip, t, track.Volume, Math.Clamp(PlaybackSpeedPercent / 100.0, 0.05, 8));
        _audioRunning = true;
        _audioRunningSegment = segmentIndex;
    }

    /// <summary>
    /// The source position (seconds) and section index under a timeline
    /// frame, or index -1 when no section covers it.
    /// </summary>
    private (double Seconds, int SegmentIndex) AudioSourcePositionAt(int frame, AudioClip clip)
    {
        var fps = Math.Max(1, Scene.Fps);
        var segments = AudioSegmentsNow;
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (frame < s.AtFrame || frame >= s.AtFrame + s.LengthFrames) continue;
            var seconds = (frame - s.AtFrame + s.SourceStartFrames) / (double)fps;
            if (seconds < 0 || seconds >= clip.DurationSeconds) return (0, -1);
            return (seconds, i);
        }
        return (0, -1);
    }

    private int _audioRunningSegment = -1;

    private void StopAudio()
    {
        if (!_audioRunning) return;
        _audioPlayback.Stop();
        _audioRunning = false;
        _audioRunningSegment = -1;
    }

    /// <summary>
    /// One frame's worth of sound under the playhead while scrubbing — the
    /// track read a syllable at a time. Playback has its own path above.
    /// </summary>
    private void ScrubAudioTick()
    {
        if (IsPlaying || _switchingTabs) return;
        if (Scene.Audio is not { } track || track.Muted || AudioClipNow is not { } clip) return;
        if (AudioSourcePositionAt(CurrentFrameIndex, clip) is not var (t, segmentIndex) || segmentIndex < 0) return;
        _audioPlayback.ScrubTick(clip, t, 1.0 / Math.Max(1, Scene.Fps), track.Volume);
    }

    private void NotifyAudioSurface()
    {
        OnPropertyChanged(nameof(HasAudio));
        OnPropertyChanged(nameof(AudioFileName));
        OnPropertyChanged(nameof(AudioMissing));
        OnPropertyChanged(nameof(AudioMuted));
        OnPropertyChanged(nameof(AudioVolume));
        OnPropertyChanged(nameof(AudioOffsetFrames));
        OnPropertyChanged(nameof(AudioClipSpan));
        OnPropertyChanged(nameof(TimelineAudioClips));
        OnPropertyChanged(nameof(TimelineAudioPeaks));
    }
}
