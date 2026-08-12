namespace Lightbox.Core.Audio;

/// <summary>
/// The waveform the timeline draws: one min/max pair per animation frame,
/// measured off the mono mixdown. Deliberately per-frame rather than
/// per-pixel — the X-sheet and the track timeline both address time in
/// frames, and a peak list keyed the same way survives every zoom without
/// being rebuilt.
/// </summary>
public static class AudioPeaks
{
    public readonly record struct Peak(float Min, float Max);

    /// <summary>
    /// Peaks for a split clip (Q57): each section reads its own window of
    /// the source at its own place on the timeline; frames no section
    /// covers are flat zero.
    /// </summary>
    public static Peak[] Build(
        ReadOnlySpan<float> mono, int sampleRate, int fps, int frameCount,
        IReadOnlyList<Documents.AudioSegment> segments)
    {
        var peaks = new Peak[Math.Max(0, frameCount)];
        if (sampleRate <= 0 || fps <= 0 || mono.IsEmpty) return peaks;

        foreach (var segment in segments)
        {
            var first = Math.Max(0, segment.AtFrame);
            var last = Math.Min(peaks.Length, segment.AtFrame + segment.LengthFrames);
            for (var f = first; f < last; f++)
            {
                var src = f - segment.AtFrame + segment.SourceStartFrames;
                var from = Math.Max(0, (long)src * sampleRate / fps);
                var to = Math.Min((long)(src + 1) * sampleRate / fps, mono.Length);
                if (from >= to) continue;

                float min = 0, max = 0;
                for (var i = (int)from; i < (int)to; i++)
                {
                    var v = mono[i];
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
                peaks[f] = new Peak(min, max);
            }
        }
        return peaks;
    }

    /// <summary>
    /// Peaks for <paramref name="frameCount"/> frames at <paramref name="fps"/>,
    /// the sound starting at frame <paramref name="offsetFrames"/>. Frames the
    /// sound does not reach are flat zero. <paramref name="trimStartFrames"/>
    /// cuts the head of the source and <paramref name="trimLengthFrames"/>
    /// bounds how much of it the timeline uses (Q57) — the source itself is
    /// never touched.
    /// </summary>
    public static Peak[] Build(
        ReadOnlySpan<float> mono, int sampleRate, int fps, int frameCount,
        int offsetFrames = 0, int trimStartFrames = 0, int? trimLengthFrames = null)
    {
        var peaks = new Peak[Math.Max(0, frameCount)];
        if (sampleRate <= 0 || fps <= 0 || mono.IsEmpty) return peaks;

        // The window of the source the timeline may read, in samples.
        var windowFrom = (long)trimStartFrames * sampleRate / fps;
        var windowTo = trimLengthFrames is { } len
            ? Math.Min((long)(trimStartFrames + len) * sampleRate / fps, mono.Length)
            : mono.Length;

        for (var f = 0; f < peaks.Length; f++)
        {
            // The slice of the sound under animation frame f, in samples.
            // Computed per frame from integers so rounding never drifts.
            var from = (long)(f - offsetFrames + trimStartFrames) * sampleRate / fps;
            var to = (long)(f - offsetFrames + trimStartFrames + 1) * sampleRate / fps;
            from = Math.Max(from, Math.Max(0, windowFrom));
            to = Math.Min(to, windowTo);
            if (from >= to) continue;

            float min = 0, max = 0;
            for (var i = (int)from; i < (int)to; i++)
            {
                var v = mono[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }
            peaks[f] = new Peak(min, max);
        }
        return peaks;
    }
}
