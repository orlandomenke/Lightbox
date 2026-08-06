namespace Lightbox.Core.Documents;

/// <summary>A colored tag on the timeline ruler ("walk starts", "blink", …).</summary>
public sealed class FrameMarker
{
    public int Frame { get; set; }

    public string Label { get; set; } = "";

    public string Color { get; set; } = "#e0a030";

    /// <summary>
    /// Export this marker as an engine animation event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A "frame event" is a named point on a frame, and that is exactly what a
    /// marker already is — so this is a flag rather than a second record. Adding a
    /// parallel `FrameEvent` type would have been the mistake Q11's "reusable
    /// animation presets" was struck for: a feature nothing can distinguish from a
    /// shipped one.
    /// </para>
    /// <para>
    /// Opt-in, because the two uses genuinely differ. Most markers are notes to
    /// the animator — "contact", "check the hand" — and exporting those as engine
    /// callbacks would fill somebody's `AnimationClip` with events nothing
    /// handles. Ticking it says <em>this one is for the game</em>.
    /// </para>
    /// <para>
    /// Nullable so an untouched marker writes no key.
    /// </para>
    /// </remarks>
    public bool? IsEvent { get; set; }

    /// <summary>
    /// Prose about this frame, for a person. Absent unless written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one thing a <see cref="Label"/> is not. A label is a chip on the ruler
    /// and has to stay short to fit; a note is "the hand pops here, fix it on 2s"
    /// — several lines if it wants to be, shown in a list and on hover, never
    /// drawn on the ruler and <b>never exported as an event</b>.
    /// </para>
    /// <para>
    /// On the marker rather than in a record of its own, and that is the whole
    /// point: <em>frame tagging</em>, <em>timeline bookmarks</em> and
    /// <em>animation notes</em> were three roadmap items for one thing seen three
    /// ways. A separate note record would have made an artist choose between two
    /// near-identical features before knowing the difference, and forced the UI to
    /// explain both. One place to put text; what is filled in decides how it reads.
    /// </para>
    /// </remarks>
    public string? Note { get; set; }

    /// <summary>Whether this marker carries prose. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>Whether this marker is exported as an event. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ExportsAsEvent => IsEvent == true;
}

/// <summary>Which way an engine plays a tagged range.</summary>
public enum TagDirection
{
    Forward,
    Reverse,

    /// <summary>Out and back. Aseprite's <c>pingpong</c>, and the name engines expect.</summary>
    PingPong,
}

/// <summary>
/// A named range of frames: "walk", "run", "idle" — one animation clip.
/// </summary>
/// <remarks>
/// <para>
/// This <em>is</em> new, and the reason is the one thing a marker cannot be: a
/// marker is a point and a tag is a **range**. Everything an engine calls a clip
/// is a named frame range and nothing more, so this is also what "export animation
/// clips" means — there is no separate clip record to build.
/// </para>
/// <para>
/// It is what makes one sheet holding several animations usable at all: without
/// "walk = 0..7, run = 8..15" a packed multi-animation atlas is a pile of frames
/// an importer cannot divide. That is why grouped export depends on this and not
/// the other way round.
/// </para>
/// <para>
/// Frame <em>indices</em> rather than frame ids, unlike an anchor — and the
/// difference is real. An anchor belongs to a drawing; a tag names a stretch of
/// the timeline, which is a property of the sheet's layout rather than of any
/// drawing in it. Re-timing a range is meant to move a tag's boundaries, because
/// the clip is however long the animator made it.
/// </para>
/// </remarks>
public sealed class AnimationTag
{
    public string Id { get; set; } = Ids.NewId("tag");

    public string Name { get; set; } = "Animation";

    public int Start { get; set; }

    /// <summary>Inclusive, like every exchange format's <c>to</c>.</summary>
    public int End { get; set; }

    public TagDirection Direction { get; set; } = TagDirection.Forward;

    /// <summary>Whether an engine should loop the clip. Most cycles should.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>
    /// Prose about this range. Absent unless written.
    /// </summary>
    /// <remarks>
    /// The same field as <see cref="FrameMarker.Note"/> and for the same reason,
    /// on the record that can hold a <em>range</em> — because "the whole run cycle
    /// reads too heavy" is a note about a stretch of frames and a marker is a
    /// point. Never exported: a clip's name is the contract, an artist's note is
    /// not.
    /// </remarks>
    public string? Note { get; set; }

    /// <summary>Whether this tag carries prose. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>Frames the tag covers, at least one.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Length => Math.Max(1, Math.Abs(End - Start) + 1);
}

public sealed class Scene
{
    public string Id { get; set; } = Ids.NewId("scene");

    public string Name { get; set; } = "Scene 1";

    public int Width { get; set; } = 960;

    public int Height { get; set; } = 540;

    public int Fps { get; set; } = 12;

    public int FrameCount { get; set; } = 1;

    /// <summary>
    /// The paper a document opens on unless told otherwise. Shared with
    /// <see cref="DocumentFactory"/> and the New-Document dialog so the scene's
    /// declared paper colour and the layer that actually provides it cannot
    /// drift apart — they did, and the app opened on a transparent canvas
    /// while claiming to be white.
    /// </summary>
    public const string DefaultBackgroundColor = "#ffffff";

    /// <summary>Paper color composited behind all layers.</summary>
    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    /// <summary>Render with no paper at all (transparent PNG exports).</summary>
    public bool TransparentBackground { get; set; }

    /// <summary>Pixels per inch — metadata for future print/export work.</summary>
    public int Ppi { get; set; } = 72;

    public List<Layer> Layers { get; set; } = [];

    /// <summary>Colored tags on the timeline ruler, at most one per frame.</summary>
    public List<FrameMarker> Markers { get; set; } = [];

    /// <summary>Layer folders (see <see cref="LayerGroup"/>).</summary>
    public List<LayerGroup> LayerGroups { get; set; } = [];

    /// <summary>
    /// Animation frame groups created by expanding multi-frame symbols across the timeline.
    /// Null unless used, so a document that never places animated symbols writes no key.
    /// </summary>
    public List<FrameGroup>? FrameGroups { get; set; }

    /// <summary>
    /// Frames pinned as ghosts, shown wherever the playhead is.
    /// </summary>
    /// <remarks>
    /// The drawing equivalent of leaving two sheets on the pegs while you work
    /// on the one between them. Ordinary onion skin follows the playhead, so
    /// the extremes disappear the moment you look at the breakdown — which is
    /// exactly when you need them.
    /// </remarks>
    /// <para>
    /// In the document rather than in settings because a pin names a frame of
    /// <i>this</i> sequence and means nothing anywhere else, and because
    /// pinning the extremes is work that spans a session. Null unless used, so
    /// a document that never pins anything writes no key — the same discipline
    /// the camera follows. It reaches no pixels and is never exported.
    /// </para>
    public List<int>? GhostFrames { get; set; }

    /// <summary>Whether any frame is pinned.</summary>
    public bool HasGhostFrames => GhostFrames is { Count: > 0 };

    /// <summary>
    /// Imported animation references laid against this timeline, or null.
    /// </summary>
    /// <remarks>
    /// Null unless one is imported, the same discipline the camera and the
    /// ghost pins follow: a document that never uses references must serialize
    /// exactly as it does today. See <see cref="ReferenceStrip"/> for why they
    /// belong to the document rather than to settings.
    /// </remarks>
    public List<ReferenceStrip>? References { get; set; }

    /// <summary>Whether any reference has been imported.</summary>
    /// <remarks>
    /// Ignored by the serializer. It is derived from <see cref="References"/>
    /// and has no setter, so writing it put a key in every document that
    /// nothing ever read — and one that said "hasReferences: false" on the
    /// documents whose whole point is that they carry no reference machinery.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasReferences => References is { Count: > 0 };

    /// <summary>
    /// Rulers, grids and vanishing points placed on this scene, or null.
    /// </summary>
    /// <remarks>
    /// Null until one is placed — the camera's rule again. A guide is authored
    /// and saved like a camera, and like a camera it never reaches a pixel: it
    /// changes where the artist's input lands, and the snapped result is what
    /// the stroke records. See <see cref="Guide"/>.
    /// </remarks>
    public List<Guide>? Guides { get; set; }

    /// <summary>Whether any guide has been placed. Derived; see <see cref="HasReferences"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasGuides => Guides is { Count: > 0 };

    /// <summary>
    /// The shot camera, or null — and null is the default and the common case.
    ///
    /// The app serves two output targets. A game's character animation has no
    /// camera at all: the canvas is the sprite. A film shot has one. Absent
    /// rather than present-and-disabled is the difference between a sprite
    /// document that saves exactly as it always did and one that carries a
    /// camera key in every diff forever.
    /// </summary>
    public Camera? Camera { get; set; }

    /// <summary>
    /// The sprite's origin in document coordinates, or null — and null is the
    /// default, exactly like <see cref="Camera"/>.
    ///
    /// This is what a game engine positions the character by: feet centre for
    /// a walk cycle, and the point every exported offset is measured from. It
    /// is also what makes trimming safe — offsets are relative to the pivot,
    /// so tightening the bounds can never shift the character.
    ///
    /// A shot document carries no pivot and a sprite document carries no
    /// camera. Neither pays for the other.
    /// </summary>
    public Pivot? Pivot { get; set; }

    /// <summary>
    /// Named anchors this document declares — the rig, not the positions.
    /// </summary>
    /// <remarks>
    /// The names live here because a name is a property of the rig: renaming
    /// "left hand" must not touch a drawing. Where each one *is* on a given
    /// drawing is <see cref="Frame.Anchors"/>. Null until one is declared, so a
    /// document that never uses them writes no key — the same rule
    /// <see cref="Camera"/> and <see cref="Guides"/> follow.
    /// </remarks>
    public List<Anchor>? Anchors { get; set; }

    /// <summary>
    /// Named frame ranges — the clips an engine will import.
    /// </summary>
    /// <remarks>
    /// Null until one is made, so a document that never tags anything writes no
    /// key. See <see cref="AnimationTag"/> for why a tag is a new record where a
    /// frame event is only a flag on a marker.
    /// </remarks>
    public List<AnimationTag>? Tags { get; set; }

    /// <summary>
    /// Collision shapes this document declares — the names and roles, not the
    /// rectangles.
    /// </summary>
    /// <remarks>
    /// The same split as <see cref="Anchors"/>, and null until one is declared, so
    /// a document that never needs a hitbox writes no key.
    /// </remarks>
    public List<CollisionShape>? Shapes { get; set; }

    /// <summary>A layer's folder, or null.</summary>
    public LayerGroup? GroupOf(Layer layer) =>
        layer.GroupId is null ? null : LayerGroups.FirstOrDefault(g => g.Id == layer.GroupId);

    /// <summary>The frame group containing a symbol placement, or null.</summary>
    public FrameGroup? GroupOf(string placementId) =>
        FrameGroups?.FirstOrDefault(g => g.PlacementIds.Contains(placementId));

    /// <summary>Layer visibility including its folder's (what compositing must use).</summary>
    public bool IsLayerVisible(Layer layer) =>
        layer.Visible && GroupOf(layer) is not { Visible: false };

    /// <summary>
    /// Whether a layer accepts edits: not locked itself, and not inside a
    /// locked folder. Every path that changes pixels or geometry must ask
    /// this — the hidden-layer precedent only guarded three of them, which is
    /// how transform, cel edits and the external writers went unguarded.
    /// </summary>
    public bool IsLayerEditable(Layer layer) =>
        !layer.Locked && GroupOf(layer) is not { Locked: true };
}
