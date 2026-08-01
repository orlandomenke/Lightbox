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
