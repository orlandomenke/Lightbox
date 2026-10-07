using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Seamless tile painting (Q192): the toggle, what the next stroke records,
/// and the one piece of input arithmetic it needs. Every field this file uses
/// is declared here or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- seamless tiles -------------------------------------------------------

    /// <summary>
    /// Whether the next stroke wraps around the page: a mark that leaves one
    /// edge comes in on the opposite one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Session state, and nothing on the scene.</b> The tile is the page
    /// itself, so there is nothing to author or place; what a finished mark
    /// was painted under lives on the stroke (<see cref="Stroke.Wrap"/>),
    /// taken from the scene at <c>BeginStroke</c>. Turning this off afterwards
    /// changes the next stroke and never the ones already down, exactly as
    /// the symmetry toggle does.
    /// </para>
    /// <para>
    /// The canvas shows the page's eight neighbours while this is on, so the
    /// seam can be judged where it will be seen — view-only (invariant 5), and
    /// gone with the toggle.
    /// </para>
    /// </remarks>
    [ObservableProperty]
    private bool _tileWrapEnabled;

    /// <summary>Fired when tiling is switched on or off; the canvas shows or hides the neighbours from this.</summary>
    public event Action? TileWrapChanged;

    partial void OnTileWrapEnabledChanged(bool value) => TileWrapChanged?.Invoke();

    /// <summary>
    /// How far the stroke in progress is shifted to land inside the page, in
    /// whole tiles — zero for a stroke begun on the page.
    /// </summary>
    /// <remarks>
    /// Set once per stroke from where it <em>began</em> and applied to every
    /// point after, so a stroke started in a neighbour of the tiled preview is
    /// recorded as the same mark started on the page, and a stroke that then
    /// crosses an edge stays one continuous mark rather than jumping to the
    /// far side — the copies render the part that left. Per point would break
    /// every edge-crossing stroke in two.
    /// </remarks>
    private (double Dx, double Dy) _wrapShift;

    /// <summary>
    /// The tile the next stroke records, or null when tiling is off or the
    /// page cannot wrap (a zero-sized scene).
    /// </summary>
    private TileWrap? WrapForNextStroke() =>
        TileWrapEnabled
        // Blur and smudge read pixels, which a canvas translation does not
        // carry, so the engine ignores a tile on them (Q185's reason). A
        // stroke must not record what will not be honoured — and a recorded
        // tile is what tells the export the document wraps.
        && CurrentToolSettings.Kind is not (BrushKind.Blur or BrushKind.Smudge)
        && TileWrap.OfScene(Scene) is { IsIdentity: false } tile
            ? tile
            : null;

    /// <summary>
    /// Decide the stroke's shift from its first point and return that point
    /// moved onto the page — or, when the stroke joins from
    /// <paramref name="joinTo"/>, moved to the tile nearest that point.
    /// </summary>
    /// <remarks>
    /// The join case is why the anchor is a parameter: the last stroke ended
    /// at the page's right edge and the artist Shift+clicks just past it, in
    /// the neighbour. Onto the page, that click is at the LEFT edge and the
    /// joining segment runs the whole page width; nearest the anchor, it is a
    /// ten-pixel hop over the seam, and the copies draw the part that left.
    /// </remarks>
    private (double X, double Y) EnterTile(double x, double y, (double X, double Y)? joinTo = null)
    {
        _wrapShift = default;
        if (WrapForNextStroke() is not { } tile) return (x, y);
        double dx, dy;
        if (joinTo is { } anchor)
        {
            dx = -Math.Round((x - anchor.X) / tile.Width) * tile.Width;
            dy = -Math.Round((y - anchor.Y) / tile.Height) * tile.Height;
        }
        else
        {
            dx = -Math.Floor((x - tile.Left) / tile.Width) * tile.Width;
            dy = -Math.Floor((y - tile.Top) / tile.Height) * tile.Height;
        }
        _wrapShift = (dx, dy);
        return (x + dx, y + dy);
    }

    /// <summary>The shift decided at the stroke's start, applied to a later point.</summary>
    private (double X, double Y) ShiftIntoTile(double x, double y) =>
        _wrapShift == default ? (x, y) : (x + _wrapShift.Dx, y + _wrapShift.Dy);
}
