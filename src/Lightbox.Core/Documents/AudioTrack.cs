namespace Lightbox.Core.Documents;

/// <summary>
/// The scene's scratch track: one sound file the animation is timed against.
///
/// A scene has one only if the artist asked for one (<see cref="Scene.Audio"/>
/// is null by default) — the same rule <see cref="Camera"/> follows. A
/// document that never adds audio writes no keys, shows no audio UI, and pays
/// nothing.
///
/// The sound is referenced, never embedded (Q59): the document stores a path
/// and the file stays where a DAW can keep editing it. A missing file
/// degrades to a silent badge, not an error — the timing marks it anchored
/// are still the artist's work.
/// </summary>
public sealed class AudioTrack
{
    /// <summary>
    /// Where the sound lives. Relative paths resolve against the document's
    /// own directory, so a project folder moves as one thing; absolute paths
    /// are kept as typed.
    /// </summary>
    public string Path { get; set; } = "";

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
}
