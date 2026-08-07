namespace Lightbox.Core.Projects;

/// <summary>
/// What a character <em>is</em> — true in every frame of every animation of it.
/// </summary>
/// <remarks>
/// The durable half of a subject reading. <c>docs/DESIGN-subject-reading.md</c>
/// splits the reading by lifetime: this half is per character and stable, the
/// placement half is per frame and disposable. Q16 answered where each lives —
/// taxonomy here, in the manifest, because an artist may correct it by hand and
/// a correction must survive a cache wipe; placement in a content-hashed cache
/// outside the document, because it is <em>derived from</em> the stroke record
/// and invariant 1 says the record is the document.
///
/// It is an input to <em>authoring</em> and never to rendering. Delete every
/// taxonomy from a project and the render must be byte-identical — the day that
/// fails, something is consulting a model's opinion at render time and
/// invariant 2 is gone.
/// </remarks>
public sealed class SubjectTaxonomy
{
    /// <summary>What kind of thing this is: "biped", "quadruped", "prop".</summary>
    public string Kind { get; set; } = "";

    public List<SubjectPart> Parts { get; set; } = [];

    /// <summary>
    /// Set once a person has edited this. A later re-read must not silently
    /// overwrite it — the rig rule pointed at the reading itself: a guess is a
    /// default, never an override of something somebody stated.
    /// </summary>
    public bool Reviewed { get; set; }
}

/// <summary>One named part of a subject, and where it sits relative to the others.</summary>
/// <remarks>
/// Deliberately carries no geometry. Geometry is <em>placement</em>, and a
/// taxonomy holding a polygon would be stale the first time the character
/// turned round — which is the confusion the two-halves split exists to prevent.
/// </remarks>
public sealed class SubjectPart
{
    /// <summary>Short, stable, hyphenated: "near-arm", "torso", "tail".</summary>
    public string Name { get; set; } = "";

    /// <summary>The part this hangs off, by name. Null for a root.</summary>
    public string? Parent { get; set; }

    /// <summary>
    /// Normal depth against siblings — higher is nearer. The near arm is
    /// usually in front of the torso, and saying so is most of what makes an
    /// inbetween of a swing look right.
    /// </summary>
    public int Depth { get; set; }
}
