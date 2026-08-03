using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Lightbox.App.Controls;

/// <summary>
/// A single row of controls that never scrolls: whatever does not fit goes
/// into a ▾ at the end.
/// </summary>
/// <remarks>
/// <para>
/// The tool options bar is directly above the thing you are aiming at, so it
/// is the one piece of chrome that must not move or grow. Scrolling it is the
/// wrong answer twice over — a horizontal scrollbar hides controls behind a
/// gesture nobody thinks to try, and a vertical one changes the bar's height,
/// which moves the canvas.
/// </para>
/// <para>
/// So: as many as fit, and the rest one click away in a menu that is always in
/// the same place. Which items fit is arithmetic and lives in
/// <see cref="Split"/>, where it can be tested without a window.
/// </para>
/// </remarks>
public class OverflowBar : Panel
{
    /// <summary>Gap between items, matching the row it replaces.</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<OverflowBar, double>(nameof(Spacing), defaultValue: 10);

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private readonly Button _more;
    private readonly StackPanel _overflowHost;
    private readonly List<Control> _overflowing = [];
    private bool _menuOpen;

    public OverflowBar()
    {
        _overflowHost = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(4) };
        var flyout = new Flyout { Content = _overflowHost, Placement = PlacementMode.BottomEdgeAlignedRight };
        // Reparenting is only safe at a discrete moment, and opening the menu
        // is one: nothing is mid-layout and nothing is mid-drag.
        flyout.Opening += (_, _) => MoveToMenu();
        flyout.Closed += (_, _) => MoveBack();
        _more = new Button
        {
            // Named rather than a bare ▾. The bar's last control sits directly
            // beside the workspace picker, and two adjacent chevrons read as a
            // pair belonging to whichever is on the right — so the one that
            // belongs to *this* bar says which bar it belongs to.
            Content = "More tool options  ▾",
            FontSize = 11,
            Flyout = flyout,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        ToolTip.SetTip(_more, "The tool options that do not fit on the bar");
        Children.Add(_more);
    }

    /// <summary>Everything except the overflow button itself.</summary>
    private IEnumerable<Control> Items => Children.Where(c => !ReferenceEquals(c, _more));

    /// <summary>
    /// How many of <paramref name="widths"/> fit in <paramref name="available"/>.
    /// </summary>
    /// <param name="moreWidth">
    /// Room the ▾ needs. Only reserved when something actually overflows —
    /// reserving it always would push an item out to make space for a button
    /// that would then have nothing in it.
    /// </param>
    public static int Split(
        IReadOnlyList<double> widths, double available, double spacing, double moreWidth)
    {
        if (widths.Count == 0) return 0;

        var total = widths.Sum() + spacing * (widths.Count - 1);
        if (total <= available) return widths.Count;

        var budget = available - moreWidth - spacing;
        var used = 0.0;
        var shown = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            var next = used + widths[i] + (shown > 0 ? spacing : 0);
            if (next > budget) break;
            used = next;
            shown++;
        }
        return shown;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);

        var height = Children.Where(c => c.IsVisible).Select(c => c.DesiredSize.Height)
            .DefaultIfEmpty(0).Max();
        var visible = Items.Where(c => c.IsVisible).ToList();
        var width = visible.Sum(c => c.DesiredSize.Width)
            + Spacing * Math.Max(0, visible.Count - 1);
        // Ask for what the row wants, but never insist: the bar's job is to
        // fit whatever it is given.
        return new Size(
            double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Items.Where(c => c.IsVisible).ToList();
        var widths = visible.ConvertAll(c => c.DesiredSize.Width);
        var shown = _menuOpen
            ? visible.Count
            : Split(widths, finalSize.Width, Spacing, _more.DesiredSize.Width);

        var x = 0.0;
        for (var i = 0; i < visible.Count; i++)
        {
            if (i < shown)
            {
                visible[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
                x += widths[i] + Spacing;
            }
            else
            {
                // Out of the way rather than hidden: IsVisible is the caller's
                // to set, and overwriting it would fight every binding in the
                // bar. ClipToBounds keeps it off screen.
                visible[i].Arrange(new Rect(finalSize.Width + 10, 0, widths[i], finalSize.Height));
            }
        }

        var overflows = shown < visible.Count;
        _more.IsVisible = overflows;
        if (overflows)
        {
            var width = _more.DesiredSize.Width;
            _more.Arrange(new Rect(Math.Max(0, finalSize.Width - width), 0, width, finalSize.Height));
        }
        else
        {
            _more.Arrange(new Rect(finalSize.Width + 10, 0, 0, 0));
        }

        _overflowing.Clear();
        for (var i = shown; i < visible.Count; i++) _overflowing.Add(visible[i]);
        ClipToBounds = true;
        return finalSize;
    }

    private void MoveToMenu()
    {
        _menuOpen = true;
        foreach (var item in _overflowing.ToList())
        {
            Children.Remove(item);
            _overflowHost.Children.Add(item);
        }
    }

    private void MoveBack()
    {
        _menuOpen = false;
        foreach (var item in _overflowHost.Children.OfType<Control>().ToList())
        {
            _overflowHost.Children.Remove(item);
            // Before the ▾, so the row keeps the order it was written in.
            Children.Insert(Math.Max(0, Children.Count - 1), item);
        }
        InvalidateArrange();
    }
}
