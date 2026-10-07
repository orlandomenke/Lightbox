using Avalonia.Controls;
using Avalonia.Media;

namespace Lightbox.App.Controls;

/// <summary>
/// The theme's colours for chrome drawn in code (Q203): the timeline, the
/// rulers, the graph. A control that draws its own text and grid cannot name a
/// DynamicResource, so it asks here at render time, and repaints when the theme
/// switches. Data colours (a track's hue, the playhead, range markers) are not
/// chrome and stay where they are.
/// </summary>
internal static class ThemeColour
{
    /// <summary>The colour of a solid brush in the control's current theme, or the fallback.</summary>
    public static Color Of(Control on, string brushKey, Color fallback) =>
        on.TryFindResource(brushKey, on.ActualThemeVariant, out var found) && found is ISolidColorBrush brush
            ? brush.Color
            : fallback;

    /// <summary>
    /// A hairline that reads on either ground: the theme's text colour at a
    /// low alpha. White at the same alpha vanished on Studio grey.
    /// </summary>
    public static Color Hairline(Control on, byte alpha)
    {
        var ink = Of(on, "TextPrimaryBrush", Colors.White);
        return Color.FromArgb(alpha, ink.R, ink.G, ink.B);
    }

    /// <summary>
    /// Redraw a code-drawn control when the theme under it changes, first
    /// dropping whatever it cached of the old theme. A control that renders on
    /// every playback frame caches its chrome rather than looking it up per
    /// frame (leak review, Q203).
    /// </summary>
    public static void RepaintOnThemeChange(Control control, Action? forget = null) =>
        control.ActualThemeVariantChanged += (_, _) =>
        {
            forget?.Invoke();
            control.InvalidateVisual();
        };
}
