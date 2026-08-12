namespace Lightbox.Core.Documents;

/// <summary>
/// One section of the scratch track on the timeline (Q57): which part of the
/// source plays (<see cref="SourceStartFrames"/>, <see cref="LengthFrames"/>)
/// and where it sits (<see cref="AtFrame"/>). Splitting a clip at the
/// playhead makes two of these; each then slides and trims on its own.
/// </summary>
public sealed class AudioSegment
{
    /// <summary>First source frame this section plays, at the scene's fps.</summary>
    public int SourceStartFrames { get; set; }

    /// <summary>How many source frames this section plays.</summary>
    public int LengthFrames { get; set; }

    /// <summary>The timeline frame this section starts on.</summary>
    public int AtFrame { get; set; }

    public AudioSegment Clone() => (AudioSegment)MemberwiseClone();
}

/// <summary>
/// The scene's scratch track: one sound file the animation is timed against.
///
/// A scene has one only if the artist asked for one (<see cref="Scene.Audio"/>
/// is null by default) — the same rule <see cref="Camera"/> follows. A
/// document that never adds audio writes no keys, shows no audio UI, and pays
/// nothing.
///
/// The sound is referenced by default (Q59): the document stores a path and
/// the file stays where a DAW can keep editing it. A missing file degrades
/// to a silent badge, not an error — the timing marks it anchored are still
/// the artist's work. Since Q57 the artist can instead choose to embed the
/// sound (<see cref="Data"/>), for a document that has to survive being
/// shared without the WAV beside it.
/// </summary>
public sealed class AudioTrack
{
    /// <summary>
    /// Where the sound lives. Relative paths resolve against the document's
    /// own directory, so a project folder moves as one thing; absolute paths
    /// are kept as typed. Kept even when <see cref="Data"/> is set, as the
    /// name of where the sound came from.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The sound itself, base64 of the original file, or null for a
    /// reference-by-path track — and null is the default (Q57). Embedding is
    /// the artist's explicit choice at import: it makes the document
    /// self-contained at the cost of carrying the bytes through every save.
    /// When set, this wins over <see cref="Path"/>.
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// The frame the sound starts on. Negative starts the sound before frame
    /// zero — trimming a lead-in without editing the file.
    /// </summary>
    public int OffsetFrames { get; set; }

    /// <summary>
    /// Frames cut from the head of the source (Q57): the clip bar's in-trim.
    /// The source is never edited — this is which part of it the timeline
    /// uses. Zero is the default and writes; the field is cheap and the
    /// timing it carries is not optional once a clip has been trimmed.
    /// </summary>
    public int TrimStartFrames { get; set; }

    /// <summary>
    /// How many source frames the timeline uses from
    /// <see cref="TrimStartFrames"/>, or null for the rest of the clip —
    /// the out-trim. Null is the default and writes no key.
    /// </summary>
    public int? TrimLengthFrames { get; set; }

    /// <summary>Playback gain, 0..1. Stored per document: a scratch track set quiet stays quiet.</summary>
    public double Volume { get; set; } = 1.0;

    public bool Muted { get; set; }

    /// <summary>
    /// The clip cut into sections (Q57), or null — and null is the default
    /// and the unsplit case: the whole trimmed clip at
    /// <see cref="OffsetFrames"/>, exactly as before splitting existed. The
    /// promise this keeps is that a document never splits writes no key and
    /// no migration ever runs; the offset/trim fields stay authoritative
    /// until the first split materialises them into segments.
    /// </summary>
    public List<AudioSegment>? Segments { get; set; }

    /// <summary>
    /// The sections the timeline actually plays, in timeline order: the
    /// stored list when the clip has been split, or the offset/trim fields
    /// as one implicit section. <paramref name="totalSourceFrames"/> is the
    /// decoded clip's length at the scene's fps.
    /// </summary>
    public IReadOnlyList<AudioSegment> EffectiveSegments(int totalSourceFrames)
    {
        if (Segments is { Count: > 0 } stored)
        {
            return [.. stored.OrderBy(s => s.AtFrame)];
        }
        if (totalSourceFrames <= 0) return [];
        var start = Math.Clamp(TrimStartFrames, 0, totalSourceFrames - 1);
        var length = Math.Clamp(TrimLengthFrames ?? totalSourceFrames - start, 1, totalSourceFrames - start);
        return [new AudioSegment { SourceStartFrames = start, LengthFrames = length, AtFrame = OffsetFrames }];
    }

    /// <summary>
    /// Split the section under <paramref name="frame"/> in two (Q57). The
    /// first split materialises the implicit section into
    /// <see cref="Segments"/>. False when the frame is not strictly inside a
    /// section — an edge has nothing to split.
    /// </summary>
    public bool SplitAt(int frame, int totalSourceFrames)
    {
        var segments = EffectiveSegments(totalSourceFrames).Select(s => s.Clone()).ToList();
        for (var i = 0; i < segments.Count; i++)
        {
            var s = segments[i];
            if (frame <= s.AtFrame || frame >= s.AtFrame + s.LengthFrames) continue;
            var cut = frame - s.AtFrame;
            segments.Insert(i + 1, new AudioSegment
            {
                SourceStartFrames = s.SourceStartFrames + cut,
                LengthFrames = s.LengthFrames - cut,
                AtFrame = frame,
            });
            s.LengthFrames = cut;
            Segments = segments;
            return true;
        }
        return false;
    }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public AudioTrack Clone()
    {
        var copy = (AudioTrack)MemberwiseClone();
        // The section list is the one reference type here (Q57): shared, an
        // edit in one document would move sound in another.
        copy.Segments = Segments?.ConvertAll(s => s.Clone());
        return copy;
    }
}
