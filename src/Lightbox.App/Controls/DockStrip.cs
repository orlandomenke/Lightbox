using Avalonia;
using Avalonia.Controls;
using Lightbox.App.Docking;

namespace Lightbox.App.Controls;

/// <summary>
/// One edge's worth of panels: an ordered stack with a draggable splitter
/// between each pair.
/// </summary>
/// <remarks>
/// Panels are sized in <b>pixels</b>, not proportions, with the last one taking
/// the slack. Starred rows divided a sidebar evenly, so opening a fifth panel
/// shrank all five until none was usable and no splitter could recover it —
/// the whole point of a panel is gone once it is 40px tall. Pixel extents mean
/// opening one more panel pushes the stack past the viewport and it scrolls,
/// which is a far better failure than five useless panels.
/// See <c>.claude/quality/DESIGN.md</c>.
///
/// The strip owns no state. It is rebuilt from the layout whenever the layout
/// changes, and it writes splitter drags back into the layout so a resize
/// survives the next rebuild.
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

        // The strip is inside a ScrollViewer, and a starred row inside one
        // takes the VIEWPORT's height rather than the content's. Left alone
        // that turns overflow into clipping: four panels asking for 900px in a
        // 330px sidebar produced two visible panels and two flattened to
        // nothing, with no scrollbar, because the star row had swallowed the
        // whole viewport before the pixel rows were measured.
        //
        // Asking for the sum as a minimum makes the strip taller than the
        // viewport when it has to be, so every panel keeps its size and the
        // ScrollViewer does its job. When there is room to spare the star row
        // still absorbs it.
        var wanted = 0.0;
        for (var i = 0; i < panels.Count; i++)
        {
            if (i > 0) wanted += SplitterSize;
            var e = layout.Place(panels[i].PanelId).Extent;
            wanted += e > 0 ? e : DockPanels.Of(panels[i].PanelId).DefaultExtent;
        }
        if (Vertical) { MinHeight = wanted; MinWidth = 0; }
        else { MinWidth = wanted; MinHeight = 0; }

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

            // The last panel absorbs the slack so a single-panel strip fills
            // its area instead of leaving dead space under it.
            var length = i == panels.Count - 1
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(extent, GridUnitType.Pixel);
            AddSlot(panel, length, info.MinExtent);
        }
    }

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
            if (size > 0) _layout.Place(docker.PanelId).Extent = size;
        }
        ExtentsChanged?.Invoke();
    }
}
