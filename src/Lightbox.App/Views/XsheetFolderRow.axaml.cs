using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lightbox.App.Input;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>A folder's row on the X-sheet; see the markup for why it is its own control.</summary>
/// <remarks>
/// <para>
/// The one gesture it owns is dragging a mark along the strip, which retimes
/// every drawing inside the folder on that frame
/// (<see cref="SheetFolderRow.DragMark"/>). The arithmetic is
/// <see cref="FolderSummaryDrag"/>'s; what is here is the press, the capture
/// and the release around it.
/// </para>
/// <para>
/// A captured pointer rather than OS drag-and-drop, which is what a single cel
/// uses: this drag never leaves its own row, so there is no drop target to
/// negotiate with, and capture gives the release back to this control wherever
/// the pointer has wandered.
/// </para>
/// </remarks>
public partial class XsheetFolderRow : UserControl
{
    private int _from = -1;
    private int _over = -1;
    private Point _pressedAt;
    private bool _dragging;

    public XsheetFolderRow() => InitializeComponent();

    private SheetFolderRow? Folder => DataContext as SheetFolderRow;

    /// <summary>The frame under a pointer on the strip, from the strip's own measurements.</summary>
    private int FrameUnder(PointerEventArgs e)
    {
        if (Folder is not { Cells.Count: > 0 } folder) return -1;
        var width = Strip.ContainerFromIndex(0)?.Bounds.Width ?? 0;
        var gap = (Strip.ItemsPanelRoot as StackPanel)?.Spacing ?? 0;
        return width <= 0 ? -1 : FolderSummaryDrag.FrameAt(e.GetPosition(Strip).X, width, gap, folder.Cells.Count);
    }

    private void OnStripPressed(object? sender, PointerPressedEventArgs e)
    {
        Reset();
        if (!e.GetCurrentPoint(Strip).Properties.IsLeftButtonPressed) return;
        var frame = FrameUnder(e);
        // Only a mark can be picked up: an empty stretch of the strip has
        // nothing under it to move.
        if (frame < 0 || Folder is not { } folder || !folder.Cells[frame].IsKeyed) return;
        _from = frame;
        _pressedAt = e.GetPosition(Strip);
        e.Pointer.Capture(Strip);
        e.Handled = true;
    }

    private void OnStripMoved(object? sender, PointerEventArgs e)
    {
        if (_from < 0) return;
        if (!_dragging && !FolderSummaryDrag.IsDrag(_pressedAt, e.GetPosition(Strip))) return;
        _dragging = true;
        Mark(FrameUnder(e));
    }

    private void OnStripReleased(object? sender, PointerReleasedEventArgs e)
    {
        var (from, to, dragged) = (_from, _over, _dragging);
        Reset();
        e.Pointer.Capture(null);
        if (dragged && from >= 0 && to >= 0 && to != from) Folder?.DragMark(from, to);
    }

    private void OnStripCaptureLost(object? sender, PointerCaptureLostEventArgs e) => Reset();

    /// <summary>Light the cell the mark would land on, and only that one.</summary>
    private void Mark(int frame)
    {
        if (frame == _over) return;
        CellAt(_over)?.Classes.Set("dropTarget", false);
        _over = frame;
        if (frame != _from) CellAt(frame)?.Classes.Set("dropTarget", true);
    }

    private Border? CellAt(int frame) =>
        frame < 0 ? null : (Strip.ContainerFromIndex(frame) as ContentPresenter)?.Child as Border;

    private void Reset()
    {
        CellAt(_over)?.Classes.Set("dropTarget", false);
        _from = -1;
        _over = -1;
        _dragging = false;
    }
}
