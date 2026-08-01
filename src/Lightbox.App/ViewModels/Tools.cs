namespace Lightbox.App.ViewModels;

/// <summary>The tools in the toolbar.</summary>
public enum ToolId
{
    Brush,
    Eraser,
    Fill,
    Select,

    /// <summary>Eyedropper: click the canvas to pick the color under the cursor.</summary>
    Picker,
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

    /// <summary>Always composite at document resolution. Sharpest, slowest.</summary>
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
