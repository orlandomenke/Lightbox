namespace Lightbox.Core.Documents;

/// <summary>A colored tag on the timeline ruler ("walk starts", "blink", …).</summary>
public sealed class FrameMarker
{
    public int Frame { get; set; }

    public string Label { get; set; } = "";

    public string Color { get; set; } = "#e0a030";
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

    /// <summary>A layer's folder, or null.</summary>
    public LayerGroup? GroupOf(Layer layer) =>
        layer.GroupId is null ? null : LayerGroups.FirstOrDefault(g => g.Id == layer.GroupId);

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
