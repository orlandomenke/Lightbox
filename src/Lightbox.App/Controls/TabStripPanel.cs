using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Lightbox.App.Docking;

namespace Lightbox.App.Controls;

/// <summary>
/// A docker header's tab strip: tabs left to right at their natural widths,
/// and, when they do not fit, the widest give way first (Q281).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a panel of its own.</b> The strip was a horizontal
/// <see cref="StackPanel"/>, which measures every child with unlimited width,
/// so it was as wide as its titles whatever the header had. At sidebar width
/// the colour group's tabs ran on under the header's buttons — transparent
/// until hover and still taking clicks — and the end of a tab could be neither
/// clicked nor dragged (measured: tabs to 422 px, buttons from 226).
/// </para>
/// <para>
/// <b>The widest give way first</b>, as a browser's tabs do: a single width cap
/// is found such that every tab at <c>min(natural, cap)</c> fills exactly the
/// room there is. A short title — "Color" — keeps its whole width while
/// "Gradient" and "Channels" are cut to meet it, which reads better than every
/// tab losing the same share. Each cut tab is measured again at its width, so
/// its title can trim itself with an ellipsis.
/// </para>
/// <para>
/// <b>The tab in front stays whole first</b> (Q281, the owner): the panel being
/// looked at is named in full and the tabs behind it are cut harder to pay for
/// it. It gives way only when keeping it whole would leave the others less
/// than <see cref="BehindFloor"/> each, so a long front title can never squeeze
/// the rest off the strip.
/// </para>
/// <para>
/// <b>No floor.</b> The cap is never below an equal share of the room (the
/// tabs that keep their width are each under it), so a tab is only ever cut
/// as far as sharing the room demands. An earlier 44 px floor that always won
/// pushed the last tabs past the strip's clip, where they took no click while
/// the drop arithmetic still aimed at them: every tab stays on the strip,
/// however little of it shows, and the header's menu (Q276) names them all.
/// </para>
/// <para>
/// <b>A cut tab sheds padding first.</b> At the normal padding a 50 px tab
/// had room for one letter and an ellipsis ("C… P… G… C…", rendered), so a cut
/// tab is marked <see cref="IsCutProperty"/> and styled with the narrower
/// <c>TabPaddingCut</c>. Whether a tab is cut is always judged at the full
/// padding: judged at the padding it currently has, a cut tab would measure
/// narrow enough to fit, be uncut, measure wide again, and flip on every pass.
/// </para>
/// </remarks>
public class TabStripPanel : Panel
{
    private double[] _widths = [];

    /// <summary>
    /// What each tab behind the front one keeps before the front tab gives way:
    /// a tab's cut padding and a couple of letters, enough to aim at.
    /// </summary>
    public const double BehindFloor = 40;

    static TabStripPanel()
    {
        // Which tab is in front decides the widths, so a change of front tab
        // is a change of layout. The list box does not say so on its own.
        // The visual parent: an item's logical parent is the list box, and
        // asking that one re-laid nothing.
        ListBoxItem.IsSelectedProperty.Changed.AddClassHandler<ListBoxItem>(
            (item, _) => (item.GetVisualParent() as TabStripPanel)?.InvalidateMeasure());
    }

    /// <summary>Set on a tab the strip has cut, so its style can shed padding.</summary>
    public static readonly AttachedProperty<bool> IsCutProperty =
        AvaloniaProperty.RegisterAttached<TabStripPanel, Control, bool>("IsCut");

    public static bool GetIsCut(Control tab) => tab.GetValue(IsCutProperty);

    public static void SetIsCut(Control tab, bool value) => tab.SetValue(IsCutProperty, value);

    public TabStripPanel() => ClipToBounds = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Children;
        var natural = new double[children.Count];
        var height = 0.0;
        // What a cut tab's narrower padding saves, so a tab measured while cut
        // is judged at the width it would have uncut (see the remarks).
        var saved = Across("TabPadding") - Across("TabPaddingCut");
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(double.PositiveInfinity, availableSize.Height));
            natural[i] = children[i].DesiredSize.Width + (GetIsCut(children[i]) ? saved : 0);
            height = Math.Max(height, children[i].DesiredSize.Height);
        }

        var front = -1;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is ListBoxItem { IsSelected: true }) front = i;
        }
        _widths = Fit(natural, availableSize.Width, front);
        var total = 0.0;
        for (var i = 0; i < children.Count; i++)
        {
            var cut = _widths[i] < natural[i] - 0.5;
            SetIsCut(children[i], cut);
            if (cut) children[i].Measure(new Size(_widths[i], availableSize.Height));
            // The whole title where it is cut, and nothing where it is not:
            // a tooltip on every tab would repeat what the tab already says.
            ToolTip.SetTip(children[i], cut ? TitleOf(children[i]) : null);
            total += _widths[i];
        }
        return new Size(double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            var width = i < _widths.Length ? _widths[i] : Children[i].DesiredSize.Width;
            Children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }
        return finalSize;
    }

    /// <summary>
    /// Every tab's width: natural when they all fit, otherwise capped at the
    /// one width that makes them fill <paramref name="room"/> exactly.
    /// </summary>
    internal static double[] Fit(IReadOnlyList<double> natural, double room, int front = -1)
    {
        var widths = natural.ToArray();
        if (double.IsInfinity(room) || widths.Sum() <= room) return widths;

        if (front >= 0 && front < widths.Length && widths.Length > 1)
        {
            // The front tab first, whole if the rest can still have their floor
            // each; then the rest share what is left, by the same rule.
            var others = Enumerable.Range(0, widths.Length).Where(i => i != front).ToArray();
            var keep = Math.Min(natural[front],
                Math.Max(room - BehindFloor * others.Length, Math.Floor(room / widths.Length)));
            keep = Math.Floor(keep);
            var rest = Fit(others.Select(i => natural[i]).ToArray(), room - keep);
            widths[front] = keep;
            for (var k = 0; k < others.Length; k++) widths[others[k]] = rest[k];
            return widths;
        }

        // Water-fill: walk the tabs narrowest first. Each one that fits under
        // an even share of what is left keeps its width; the first that does
        // not sets the cap, which is that share, for it and every wider tab.
        var order = Enumerable.Range(0, widths.Length).OrderBy(i => natural[i]).ToArray();
        var left = room;
        // If no tab breaks the walk below, every one fit after all (a sum a
        // rounding error over the room): nothing is cut.
        var cap = natural.Max();
        for (var k = 0; k < order.Length; k++)
        {
            var share = left / (order.Length - k);
            if (natural[order[k]] <= share)
            {
                left -= natural[order[k]];
                continue;
            }
            cap = share;
            break;
        }
        // Whole pixels, rounded down: with layout rounding a fractional cap
        // came out a pixel wider per tab, overlapping neighbours and pushing
        // the last tab a pixel under the header's buttons.
        cap = Math.Floor(cap);
        for (var i = 0; i < widths.Length; i++) widths[i] = Math.Min(natural[i], cap);
        return widths;
    }

    private static string? TitleOf(Control tab) => (tab.DataContext as DockPanelInfo)?.Title;

    /// <summary>A padding token's left plus right, or nothing if it is not found.</summary>
    private double Across(string key) =>
        this.TryFindResource(key, out var value) && value is Thickness t ? t.Left + t.Right : 0;
}
