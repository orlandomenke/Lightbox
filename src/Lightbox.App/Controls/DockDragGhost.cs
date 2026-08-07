using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Lightbox.App.Controls;

/// <summary>
/// The panel you are carrying, drawn under the pointer while you drag it.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="DockDropIndicator"/> because the two answer
/// different questions: the indicator says <em>where it would land</em>, this
/// says <em>what is moving</em>. Before tabs you could infer the second from
/// the first; with tabs you cannot, because the difference between joining a
/// header and inserting below a panel is a few pixels of pointer travel and
/// both light up a region.
/// </para>
/// <para>
/// <b>Never hit-testable.</b> It sits under the pointer by definition, so a
/// ghost that takes input eats the drag it exists to illustrate — and the
/// symptom is "dragging stopped working", which points nowhere near a control
/// whose whole job is to be looked at.
/// </para>
/// <para>
/// A titled rectangle rather than a picture of the panel. The real docker stays
/// in its slot until the drop, so a live copy would mean rendering the panel
/// twice on every pointer event to show something the artist can already see a
/// few centimetres away.
/// </para>
/// </remarks>
public sealed class DockDragGhost : Control
{
    /// <summary>
    /// How big the ghost is, whatever the panel's real size.
    /// </summary>
    /// <remarks>
    /// A panel-sized ghost covers the drop indicator it is supposed to be read
    /// alongside, and a sidebar panel is tall enough to cover most of the
    /// window. Small enough to carry, large enough to name.
    /// </remarks>
    private const double GhostWidth = 168;
    private const double GhostHeight = 34;

    /// <summary>Offset from the pointer, so the cursor is not sitting on the label.</summary>
    private const double PointerGap = 12;

    private string? _title;
    private Point _at;

    public DockDragGhost()
    {
        // Both halves matter: IsHitTestVisible keeps it out of the pointer's
        // way, IsVisible keeps it from drawing when nothing is being dragged.
        IsHitTestVisible = false;
        IsVisible = false;
    }

    /// <summary>Start showing a panel's ghost, or move the one already showing.</summary>
    public void Show(string title, Point at)
    {
        _title = title;
        _at = at;
        IsVisible = true;
        InvalidateVisual();
    }

    /// <summary>Stop showing it. Safe to call when it is not showing.</summary>
    /// <remarks>
    /// Called from the end of a drag <em>and</em> from a cancelled one. A ghost
    /// left on screen reads as a rendering fault rather than as a stuck state,
    /// so the cheap thing is to clear it from every exit rather than to reason
    /// about which exits exist.
    /// </remarks>
    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        _title = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (!IsVisible || _title is null) return;

        var rect = new Rect(_at.X + PointerGap, _at.Y + PointerGap, GhostWidth, GhostHeight);

        var fill = this.FindResource("SurfacePanelBrush") as IBrush;
        var stroke = this.FindResource("BorderStrongBrush") as IBrush;
        var text = this.FindResource("TextPrimaryBrush") as IBrush;

        context.DrawRectangle(
            new ImmutableSolidColorBrush(
                (fill as ISolidColorBrush)?.Color ?? Colors.Black, 0.85),
            new Pen(stroke ?? Brushes.Gray, 1),
            rect,
            4, 4);

        var label = new FormattedText(
            _title,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            text ?? Brushes.White);

        context.DrawText(label, new Point(
            rect.X + 10,
            rect.Y + (rect.Height - label.Height) / 2));
    }

    private object? FindResource(string key) =>
        this.TryFindResource(key, out var found) ? found : null;
}
