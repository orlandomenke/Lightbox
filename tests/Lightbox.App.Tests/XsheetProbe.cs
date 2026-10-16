using Avalonia;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Where a cel is on the drawn X-sheet, for tests that click it. The sheet has
/// no control per cel (Q282), so a cel is found by asking its row.
/// </summary>
internal static class XsheetProbe
{
    /// <summary>The drawn row showing this cel.</summary>
    public static XsheetRowView RowOf(MainWindow window, FrameCell cell) =>
        window.GetVisualDescendants().OfType<XsheetRowView>()
            .First(v => v.Cells is { } cells && cells.Contains(cell) && v.IsEffectivelyVisible);

    /// <summary>The middle of a cel, in the window's coordinates.</summary>
    public static Point CentreOfCell(MainWindow window, FrameCell cell)
    {
        var row = RowOf(window, cell);
        var bounds = row.CellBounds(cell.Index);
        return row.TranslatePoint(bounds.Center, window)!.Value;
    }
}
