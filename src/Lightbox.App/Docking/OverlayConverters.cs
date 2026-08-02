using System.Globalization;
using Avalonia.Data.Converters;

namespace Lightbox.App.Docking;

/// <summary>Small view helpers for the overlay bar's template.</summary>
public static class OverlayConverters
{
    /// <summary>
    /// A side-edge bar stacks downwards.
    /// </summary>
    /// <remarks>
    /// Stacking rather than rotating the whole control. Rotating put the bar
    /// in the right shape and every glyph in it on its side — an icon read at
    /// a glance is the whole reason these are icons.
    /// </remarks>
    public static readonly IValueConverter VerticalToOrientation =
        new FuncValueConverter<bool, Avalonia.Layout.Orientation>(
            vertical => vertical ? Avalonia.Layout.Orientation.Vertical : Avalonia.Layout.Orientation.Horizontal);

    public static readonly IValueConverter CollapseGlyph =
        new FuncValueConverter<bool, string>(collapsed => collapsed ? "▸" : "▾");
}
