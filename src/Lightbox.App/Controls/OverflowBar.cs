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

    /// <summary>
    /// What the overflow button says. Named per bar, because the button sits
    /// beside other chrome and a bare ▾ reads as belonging to whichever
    /// neighbour is on the right.
    /// </summary>
    public static readonly StyledProperty<string> MoreLabelProperty =
        AvaloniaProperty.Register<OverflowBar, string>(nameof(MoreLabel), defaultValue: "More  ▾");

    public string MoreLabel
    {
        get => GetValue(MoreLabelProperty);
        set => SetValue(MoreLabelProperty, value);
    }

    /// <summary>
    /// Where the overflow menu opens. A bar at the bottom of the window sends
    /// it up; the default suits a bar above the canvas.
    /// </summary>
    public static readonly StyledProperty<PlacementMode> MenuPlacementProperty =
        AvaloniaProperty.Register<OverflowBar, PlacementMode>(
            nameof(MenuPlacement), defaultValue: PlacementMode.BottomEdgeAlignedRight);

    public PlacementMode MenuPlacement
    {
        get => GetValue(MenuPlacementProperty);
        set => SetValue(MenuPlacementProperty, value);
    }

    /// <summary>
    /// Marks the one child that gives way before anything overflows: it is
    /// squeezed from its natural width (its MaxWidth) down to its MinWidth
    /// first, and only when it is at the floor does the bar start moving
    /// items into the menu. The owner's rule for the timeline: "decrease the
    /// slider first, then hide the option under a dropdown."
    /// </summary>
    public static readonly AttachedProperty<bool> FlexProperty =
        AvaloniaProperty.RegisterAttached<OverflowBar, Control, bool>("Flex");

    public static bool GetFlex(Control c) => c.GetValue(FlexProperty);
    public static void SetFlex(Control c, bool value) => c.SetValue(FlexProperty, value);

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
            Content = MoreLabel,
            FontSize = 11,
            Flyout = flyout,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        _more.Bind(ContentControl.ContentProperty, this.GetObservable(MoreLabelProperty));
        ToolTip.SetTip(_more, "The controls that do not fit on the bar");
        Children.Add(_more);
    }

    static OverflowBar()
    {
        MenuPlacementProperty.Changed.AddClassHandler<OverflowBar>((bar, _) =>
        {
            if (bar._more.Flyout is Flyout f) f.Placement = bar.MenuPlacement;
        });
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

    /// <summary>
    /// The flexible variant: before anything is sent to the menu, the child
    /// at <paramref name="flexIndex"/> is squeezed from its natural width
    /// down to <paramref name="flexMin"/>. Only when that is not enough does
    /// the ordinary split run, with the flexible child held at its floor.
    /// </summary>
    public static (int Shown, double FlexWidth) Split(
        IReadOnlyList<double> widths, double available, double spacing, double moreWidth,
        int flexIndex, double flexMin)
    {
        if (flexIndex < 0 || flexIndex >= widths.Count)
        {
            return (Split(widths, available, spacing, moreWidth), 0);
        }

        var natural = widths[flexIndex];
        var total = widths.Sum() + spacing * (widths.Count - 1);
        if (total <= available) return (widths.Count, natural);

        var over = total - available;
        var give = natural - flexMin;
        if (give >= over) return (widths.Count, natural - over);

        var squeezed = widths.ToArray();
        squeezed[flexIndex] = flexMin;
        return (Split(squeezed, available, spacing, moreWidth), flexMin);
    }

    /// <summary>
    /// A flexible child's natural width is its MaxWidth when it names one —
    /// a Slider has no intrinsic width, so the cap is what "unsqueezed"
    /// means for it. Everything else is its measured desire.
    /// </summary>
    private static double NaturalWidth(Control c) =>
        GetFlex(c) && !double.IsInfinity(c.MaxWidth) ? c.MaxWidth : c.DesiredSize.Width;

    /// <summary>
    /// The children that actually occupy the row.
    /// </summary>
    /// <remarks>
    /// <b>Visible and wanting width, not merely visible.</b> A tool-options
    /// group is a <c>UserControl</c> whose contents are gated on the tool, so
    /// with another tool in hand it stays visible and measures to nothing — and
    /// a zero-width child was still costing a <see cref="Spacing"/> gap and a
    /// place in the row. Seven such groups sat in the quick bar spending 98px of
    /// a 273px row on nothing, which is what pushed the text tool's options into
    /// the ▾ at every ordinary window size. A child that wants no width should
    /// cost no gap; the ones that gate the control itself were always free, and
    /// this makes the other kind free too.
    /// </remarks>
    private List<Control> Occupying() =>
        Items.Where(c => c.IsVisible && NaturalWidth(c) > 0).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(Size.Infinity);

        var height = Children.Where(c => c.IsVisible).Select(c => c.DesiredSize.Height)
            .DefaultIfEmpty(0).Max();
        var visible = Occupying();
        var width = visible.Sum(NaturalWidth)
            + Spacing * Math.Max(0, visible.Count - 1);
        // Ask for what the row wants, but never insist: the bar's job is to
        // fit whatever it is given.
        return new Size(
            double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Occupying();
        var widths = visible.ConvertAll(NaturalWidth);
        var flexIndex = visible.FindIndex(GetFlex);
        var flexMin = flexIndex >= 0 ? Math.Max(visible[flexIndex].MinWidth, 40) : 0;
        var (split, flexWidth) = Split(
            widths, finalSize.Width, Spacing, _more.DesiredSize.Width, flexIndex, flexMin);
        var shown = _menuOpen ? visible.Count : split;
        if (flexIndex >= 0 && flexIndex < shown) widths[flexIndex] = flexWidth;

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
