namespace Lightbox.App.ViewModels;

/// <summary>The tools in the toolbar.</summary>
public enum ToolId
{
    Brush,
    Eraser,
    Fill,
    Select,

    /// <summary>
    /// The black arrow: picks <b>things</b> — a drawn line, a guide, a placed
    /// symbol, a rig anchor. Not areas of pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Illustrator's Selection tool, and deliberately a second tool rather than
    /// a mode of <see cref="Select"/> — Q48. That one selects a *region*, which
    /// becomes a clip a stroke is painted under; this one selects *records* you
    /// can then move, recolour or delete. Folding them together would make one
    /// click mean two things depending on what happened to be beneath it, which
    /// is the ambiguity the whole vector design exists to avoid.
    /// </para>
    /// <para>
    /// <b>It picks more than lines, and that is not scope creep — it is a
    /// revival.</b> <c>CanvasToolMode.Select</c> has carried a full hit-test
    /// chain for placements, guides, reference boxes, anchors and collision
    /// shapes since the selection manager landed, and <c>SyncCanvasToolMode</c>
    /// never assigned that mode — so none of it has ever been reachable. This is
    /// the tool that code was always for.
    /// </para>
    /// <para>
    /// Chrome beats artwork where they overlap: a guide crossing a line picks
    /// the guide. The drawing is the thing that is everywhere, so if it won, a
    /// guide would become unclickable wherever somebody had drawn.
    /// </para>
    /// </remarks>
    Arrow,

    /// <summary>Eyedropper: click the canvas to pick the color under the cursor.</summary>
    Picker,

    /// <summary>Drag to lay down the gradient selected in the gradient docker.</summary>
    Gradient,

    /// <summary>
    /// Drag to draw a line, rectangle, ellipse or polygon.
    /// </summary>
    /// <remarks>
    /// One tool with a shape choice rather than four tools, because the
    /// gesture is identical and four buttons for one drag is four things to
    /// find. Which shape is a tool option, like the select tool's variants.
    /// </remarks>
    Shape,

    /// <summary>
    /// Drag the drawing itself around, and pick guides up.
    /// </summary>
    /// <remarks>
    /// Photoshop's Move tool, and it exists here for Photoshop's reason:
    /// grabbing a guide and drawing along one are the same gesture in the
    /// same place, so something has to say which was meant. A tool says it
    /// unambiguously, is discoverable in the palette, and — unlike the
    /// rulers, which used to carry the job — it is still the answer when the
    /// rulers are down.
    /// </remarks>
    Move,
}

/// <summary>
/// How much detail the canvas composites while you work. The document is
/// always stored and exported at full resolution — this only changes what is
/// drawn on screen, which on a large canvas is the dominant cost per frame.
/// </summary>
public enum CanvasQuality
{
    /// <summary>Match the screen: full detail when zoomed in, less when zoomed out.</summary>
    Display,

    /// <summary>
    /// Twice what the screen shows, capped at document resolution. Sharpest —
    /// the margin supersamples stroke edges — and no longer pays for pixels
    /// the monitor cannot display: zoomed out on a large document this
    /// composites a fraction of what it used to, and at 100% zoom and above it
    /// is document resolution exactly as before.
    /// </summary>
    Full,

    /// <summary>Half of what the screen shows. Softer while drawing, fastest.</summary>
    Half,
}

/// <summary>What a transform session (Ctrl+T) operates on.</summary>
public enum TransformScope
{
    /// <summary>The exposed drawing on the active layer at the playhead.</summary>
    ActiveCel,

    /// <summary>Every visible layer's exposed drawing at the playhead.</summary>
    AllLayersAtFrame,

    /// <summary>Every drawing on the active layer, whatever the playhead is on.</summary>
    /// <remarks>
    /// What "move the whole cycle over" needs. Distinct from
    /// <see cref="EntireAnimation"/>, which would take the background with it.
    /// </remarks>
    ActiveLayerAllFrames,

    /// <summary>The cels marked with the timeline range selection.</summary>
    CelRange,

    /// <summary>Every drawing on every layer.</summary>
    EntireAnimation,
}

/// <summary>
/// Pixel solver used when a transform has to resample raster baseline
/// pixels (strokes are pure geometry and never resample).
/// </summary>
public enum TransformSampling
{
    /// <summary>Smooth and fast — the general-purpose default.</summary>
    Bilinear,

    /// <summary>Hard pixels, no new colors — pixel art and cel-shaded fills.</summary>
    Nearest,

    /// <summary>Highest quality for photos/painterly textures (Mitchell cubic).</summary>
    Bicubic,
}

/// <summary>Variants of the selection tool (press-and-hold or repeat S to switch).</summary>
public enum SelectVariant
{
    Freehand,
    Polygon,
    Box,
    Ellipse,

    /// <summary>Magic wand: click to select a connected color region (flood fill).</summary>
    Wand,
}

/// <summary>
/// What a mark on a held cel does.
/// </summary>
/// <remarks>
/// The two honest readings of the same gesture, and which one an artist means
/// depends on how they work rather than on what the app prefers. Hence a
/// setting, in Edit ▸ Configure ▸ Timeline.
/// </remarks>
public enum HoldDrawing
{
    /// <summary>
    /// Key the cel and draw on a new drawing. The default.
    /// </summary>
    /// <remarks>
    /// What every animation tool does, and what the timeline then shows: a
    /// drawing appears where you made one. The alternative silently edits the
    /// frame being held, so the mark shows up on the earlier frame too.
    /// </remarks>
    StartANewDrawing,

    /// <summary>
    /// Add the mark to the drawing being held, on every frame that holds it.
    /// </summary>
    /// <remarks>
    /// Right when the hold is deliberate and you are still working on that one
    /// drawing — touching up a held pose without breaking the hold.
    /// </remarks>
    EditTheHeldDrawing,
}
