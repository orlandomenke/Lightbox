using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Lightbox.App.Rendering;

/// <summary>One icon as it sits on screen: which, how big, and where in its button.</summary>
/// <param name="Name">The <see cref="IconSet"/> name of the geometry drawn.</param>
/// <param name="Host">The button it sits in, described; empty when it is in none.</param>
/// <param name="Width">The icon's box, in layout units.</param>
/// <param name="Height">The icon's box, in layout units.</param>
/// <param name="HostWidth">The button's box; zero without a host.</param>
/// <param name="HostHeight">The button's box; zero without a host.</param>
/// <param name="OffCentreX">Icon centre minus button centre, in device pixels.</param>
/// <param name="OffCentreY">Icon centre minus button centre, in device pixels.</param>
/// <param name="Alone">Whether the icon is the button's whole content — no visible text beside it.</param>
/// <param name="OffPixel">How far the icon's corner is from a whole device pixel, 0 to 0.5.</param>
/// <param name="Spill">How far the icon reaches outside its button, in device pixels; zero when inside.</param>
/// <param name="Stroke">The line weight it is drawn at.</param>
internal sealed record IconPlacement(
    string Name, string Host, double Width, double Height, double HostWidth, double HostHeight,
    double OffCentreX, double OffCentreY, bool Alone, double OffPixel, double Spill, double Stroke);

/// <summary>
/// Every icon a window is showing, measured where it landed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists beside <see cref="IconSet"/>.</b> The registry answers
/// "does the icon exist"; nothing answered "where did it end up". An icon is a
/// stretched <c>Path</c> centred in a tile by layout, and layout rounds: a 13 px
/// icon in a 26 px tile wants to start at 6.5 and is put at 6 or at 7. Whether
/// that is a crisp icon half a pixel off centre or a blurred one on centre
/// depends on the display's scaling, which no XAML reading can tell — so the
/// question is asked of the laid-out tree, by the headless tests at 100% and by
/// the performance lab (<c>lab_icons</c>) on the real window at the real scale.
/// </para>
/// <para>
/// An icon here is a visible <c>Path</c> drawing a registered geometry. The
/// scroll bar's arrows, a check box's tick and the layer brackets are paths too
/// and are not icons; they are counted (<see cref="CountPaths"/>) and not placed.
/// </para>
/// </remarks>
internal static class IconCensus
{
    /// <summary>Every visible icon under <paramref name="root"/>.</summary>
    /// <param name="root">The window, or any part of one.</param>
    /// <param name="scale">Device pixels per layout unit — the window's render scaling.</param>
    public static IReadOnlyList<IconPlacement> Take(Visual root, double scale = 1.0)
    {
        var names = new Dictionary<Geometry, string>(ReferenceEqualityComparer.Instance);
        foreach (var name in IconSet.All)
        {
            if (IconSet.Resolve(name) is { } geometry) names[geometry] = name;
        }

        var top = TopLevel.GetTopLevel(root) as Visual ?? root;
        var found = new List<IconPlacement>();
        foreach (var path in root.GetVisualDescendants().OfType<ShapePath>())
        {
            if (!path.IsEffectivelyVisible || path.Data is null || !names.TryGetValue(path.Data, out var name)) continue;
            var size = path.Bounds.Size;
            var corner = path.TranslatePoint(default, top) ?? default;
            var offPixel = Math.Max(FromWhole(corner.X * scale), FromWhole(corner.Y * scale));

            var host = path.GetVisualAncestors().OfType<Button>().FirstOrDefault();
            if (host is null)
            {
                found.Add(new IconPlacement(name, "", size.Width, size.Height, 0, 0, 0, 0, false, offPixel, 0, path.StrokeThickness));
                continue;
            }

            var inHost = path.TranslatePoint(default, host) ?? default;
            var dx = inHost.X + size.Width / 2 - host.Bounds.Width / 2;
            var dy = inHost.Y + size.Height / 2 - host.Bounds.Height / 2;
            var spill = Math.Max(
                Math.Max(-inHost.X, -inHost.Y),
                Math.Max(inHost.X + size.Width - host.Bounds.Width, inHost.Y + size.Height - host.Bounds.Height));
            var alone = !host.GetVisualDescendants().OfType<TextBlock>()
                .Any(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text));
            found.Add(new IconPlacement(
                name, Describe(host), size.Width, size.Height, host.Bounds.Width, host.Bounds.Height,
                dx * scale, dy * scale, alone, offPixel, Math.Max(0, spill) * scale, path.StrokeThickness));
        }
        return found;
    }

    /// <summary>Every <c>Path</c> realized under <paramref name="root"/>, shown or not.</summary>
    /// <remarks>
    /// The structural cost: a hidden path is still a control that was built,
    /// styled and measured. Counted so a row template cannot quietly double it.
    /// </remarks>
    public static (int Realized, int Visible) CountPaths(Visual root)
    {
        var realized = 0;
        var visible = 0;
        foreach (var path in root.GetVisualDescendants().OfType<ShapePath>())
        {
            realized++;
            if (path.IsEffectivelyVisible) visible++;
        }
        return (realized, visible);
    }

    private static double FromWhole(double v) => Math.Abs(v - Math.Round(v));

    private static string Describe(Control c)
    {
        var classes = string.Join(".", c.Classes.Where(x => !x.StartsWith(':')));
        return c.GetType().Name
               + (string.IsNullOrEmpty(c.Name) ? "" : "#" + c.Name)
               + (classes.Length > 0 ? "." + classes : "");
    }
}
