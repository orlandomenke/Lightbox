namespace Lightbox.Core.Documents;

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

    /// <summary>Playback gain, 0..1. Stored per document: a scratch track set quiet stays quiet.</summary>
    public double Volume { get; set; } = 1.0;

    public bool Muted { get; set; }

    /// <summary>A copy holding no reference in common with this one.</summary>
    public AudioTrack Clone() => (AudioTrack)MemberwiseClone();
}
