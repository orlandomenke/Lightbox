using System.Globalization;
using Avalonia.Data.Converters;

namespace Lightbox.App.Docking;

/// <summary>Small view helpers for the overlay bar's template.</summary>
public static class OverlayConverters
{
    /// <summary>
    /// Turn the grip glyph back the other way when the bar has been rotated.
    /// </summary>
    /// <remarks>
    /// The bar as a whole is rotated a quarter turn on a side edge so its
    /// length runs along the edge. The grip is the one thing that should not
    /// come with it: a rotated ⠿ reads as a different symbol.
    /// </remarks>
    public static readonly IValueConverter VerticalToCounterAngle =
        new FuncValueConverter<bool, double>(vertical => vertical ? -90d : 0d);

    public static readonly IValueConverter CollapseGlyph =
        new FuncValueConverter<bool, string>(collapsed => collapsed ? "▸" : "▾");
}
