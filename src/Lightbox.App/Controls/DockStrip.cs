using Avalonia;
using Avalonia.Controls;
using Lightbox.App.Docking;

namespace Lightbox.App.Controls;

/// <summary>
/// One edge's worth of panels: an ordered stack with a draggable splitter
/// between each pair.
/// </summary>
/// <remarks>
/// <para>
/// A vertical strip never scrolls — the owner's call, reversing the earlier
/// pixel-extents-and-scroll design. Panels take <b>weighted star</b> rows
/// (the saved extent is the weight), so opening another panel shrinks the
/// others proportionally, scalable content like the colour wheel scales
/// with its panel, and a docker's own bars stay visible because it is the
/// docker's <em>content</em> that scrolls when squeezed (see the Docker
/// template). The floor per row is the chrome, not the content, or five
/// panels could not fit where four did.
/// </para>
/// <para>
/// The bottom strip keeps pixel sizing across: its panels sit side by side
/// and the timeline's width is not a proportion of its neighbours'.
/// </para>
/// <para>
/// The strip owns no state. It is rebuilt from the layout whenever the layout
/// changes, and it writes splitter drags back into the layout so a resize
/// survives the next rebuild — with star rows the pixels come back as
/// weights, which preserves exactly the proportions the artist set.
/// </para>
/// </remarks>
public class DockStrip : Grid
{
    public DockSide Side { get; set; } = DockSide.Right;

    /// <summary>Left and right strips stack downwards; top and bottom ones across.</summary>
    public bool Vertical => Side is DockSide.Left or DockSide.Right;

    /// <summary>Splitter thickness, matching the ones already in the window.</summary>
    private const double SplitterSize = 5;

    private DockLayout? _layout;

    /// <summary>A splitter was let go: the layout has new extents to remember.</summary>
    public event Action? ExtentsChanged;

    public void Rebuild(IReadOnlyList<Docker> panels, DockLayout layout)
    {
        _layout = layout;
        Children.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();

        // An area with nothing in it collapses rather than leaving a gutter.
        IsVisible = panels.Count > 0;
        if (panels.Count == 0)
        {
            MinWidth = 0;
            MinHeight = 0;
            return;
        }

        // A strip always fits the area it is given — this is what "a sidebar
        // never scrolls" means, and the bottom strip follows the same rule so
        // its bars can run their overflow arithmetic against a real width
        // instead of a scrolled one. A minimum here would put the ▾ menu in
        // the part of the bar nobody can see.
        MinWidth = 0;
        MinHeight = 0;

        for (var i = 0; i < panels.Count; i++)
        {
            if (i > 0) AddSlot(new GridSplitter
            {
                ResizeDirection = Vertical ? GridResizeDirection.Rows : GridResizeDirection.Columns,
                Height = Vertical ? SplitterSize : double.NaN,
                Width = Vertical ? double.NaN : SplitterSize,
            }, GridLength.Auto, 0);

            var panel = panels[i];
            var info = DockPanels.Of(panel.PanelId);
            var extent = layout.Place(panel.PanelId).Extent;
            if (extent <= 0) extent = info.DefaultExtent;

            // Weighted stars: the saved extent is the weight, so the panels
            // keep their proportions while always fitting, and another panel
            // arriving shrinks everyone proportionally.
            var length = new GridLength(extent, GridUnitType.Star);
            // The floor is the panel's CHROME when it shares a strip: bars
            // stay visible however hard the strip is squeezed, and the
            // squeeze lands on the content, which scrolls inside the panel.
            // Alone there is nothing to share with, so no floor is needed.
            var min = panels.Count > 1 ? ChromeFloor : 0;
            AddSlot(panel, length, min);
        }
    }

    /// <summary>
    /// The least a docker sharing a sidebar may shrink to: its title strip,
    /// its option bars and a sliver of content — enough that every bar stays
    /// visible and reachable, which is the promise the no-scroll sidebar
    /// makes. Content past this scrolls inside the docker.
    /// </summary>
    private const double ChromeFloor = 96;

    private void AddSlot(Control child, GridLength length, double min)
    {
        var index = Vertical ? RowDefinitions.Count : ColumnDefinitions.Count;
        if (Vertical)
        {
            RowDefinitions.Add(new RowDefinition(length) { MinHeight = min });
            SetRow(child, index);
        }
        else
        {
            ColumnDefinitions.Add(new ColumnDefinition(length) { MinWidth = min });
            SetColumn(child, index);
        }
        if (child is GridSplitter splitter) splitter.DragCompleted += (_, _) => CaptureExtents();
        Children.Add(child);
    }

    /// <summary>
    /// Read the current sizes back into the layout. Without this a splitter
    /// drag lasts until the next time anything reopens a panel.
    /// </summary>
    private void CaptureExtents()
    {
        if (_layout is null) return;
        foreach (var child in Children)
        {
            if (child is not Docker docker) continue;
            var index = Vertical ? GetRow(docker) : GetColumn(docker);
            var size = Vertical ? RowDefinitions[index].ActualHeight : ColumnDefinitions[index].ActualWidth;
            // Through the layout rather than onto the placement, so every panel
            // tabbed into this slot gets the height and switching tabs does not
            // resize it.
            if (size > 0) _layout.SetExtent(docker.PanelId, size);
        }
        ExtentsChanged?.Invoke();
    }
}
