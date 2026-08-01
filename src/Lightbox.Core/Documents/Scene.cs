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

    /// <summary>Paper color composited behind all layers.</summary>
    public string BackgroundColor { get; set; } = "#ffffff";

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
    /// The shot camera, or null — and null is the default and the common case.
    ///
    /// The app serves two output targets. A game's character animation has no
    /// camera at all: the canvas is the sprite. A film shot has one. Absent
    /// rather than present-and-disabled is the difference between a sprite
    /// document that saves exactly as it always did and one that carries a
    /// camera key in every diff forever.
    /// </summary>
    public Camera? Camera { get; set; }

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
