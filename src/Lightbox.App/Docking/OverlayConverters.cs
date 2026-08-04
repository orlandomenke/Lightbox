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

    /// <summary>
    /// The zoom readout needs a slot longer than a tile, along whichever way the bar runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <c>RenderTransform</c> cannot fix this on its own, which is what was wrong.</b>
    /// The readout was rotated to lie along a narrow bar, but rotation happens after layout:
    /// the text had already been measured and arranged inside the 26 px square every overlay
    /// tile gets, so "100%" was clipped to a couple of characters and *then* turned. Rotating
    /// something unreadable produces something unreadable at an angle.
    /// </para>
    /// <para>
    /// So the slot has to grow along the bar's long axis — taller on a side edge, wider on a
    /// top or bottom one — while the cross axis stays at the tile width so the bar does not
    /// bulge around one control. The text inside is given its natural width and allowed to
    /// overhang the narrow direction (overlay tiles set <c>ClipToBounds="False"</c> for the
    /// same reason glyphs need), and the rotation then lands it inside the tall slot.
    /// </para>
    /// </remarks>
    public const double ReadoutLength = 46;

    public static readonly IValueConverter ReadoutWidth =
        new FuncValueConverter<bool, double>(vertical => vertical ? OverlayTile : ReadoutLength);

    public static readonly IValueConverter ReadoutHeight =
        new FuncValueConverter<bool, double>(vertical => vertical ? ReadoutLength : OverlayTile);

    /// <summary>
    /// The overlay tile size, mirrored from <c>Density.axaml</c>.
    /// </summary>
    /// <remarks>
    /// Duplicated rather than read, and worth flagging: a style setter is not reachable from
    /// code, so this is the one number in the overlay bars that lives in two places.
    /// <c>OverlayBarLayoutTests</c> asserts the readout's cross axis equals the tile so the
    /// copy cannot drift silently — which is the cheapest available stand-in for sharing it.
    /// </remarks>
    public const double OverlayTile = 26;
}
