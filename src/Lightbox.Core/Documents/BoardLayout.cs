namespace Lightbox.Core.Documents;

/// <summary>
/// Laying every tile out neatly in the space there is — the board's tidy-up.
/// </summary>
/// <remarks>
/// <para>
/// <b>A grid of equal cells, each tile fitted inside its own.</b> Not a packer:
/// a packer produces a different arrangement for the same board depending on
/// which tile it happened to place first, and an artist who presses tidy twice
/// expects the same wall both times. The grid is a pure function of how many
/// tiles there are, their aspect ratios and the size of the view, which is also
/// what makes it testable without a window.
/// </para>
/// <para>
/// <b>Order is preserved, never sorted.</b> The list order is the z-order and
/// the reading order at once (<see cref="ReferenceBoard"/>), so re-arranging must
/// not reshuffle it — a tidy-up that moved the tile you were looking at to a
/// different place every time would be worse than no tidy-up.
/// </para>
/// <para>
/// <b>Aspect is preserved and never upscaled past the cell.</b> A tall
/// turnaround and a wide walk-cycle strip get the same cell and fill different
/// amounts of it; stretching either to fill its cell would misrepresent the
/// drawing, which is the one thing reference must not do.
/// </para>
/// </remarks>
public static class BoardLayout
{
    /// <summary>Breathing room between cells, and around the edge.</summary>
    public const double Gap = 16;

    /// <summary>
    /// The grid a given number of tiles is laid out on: as square as it can be,
    /// widened by one column at a time rather than growing a long single row.
    /// </summary>
    public static (int Columns, int Rows) GridFor(int count)
    {
        if (count <= 0) return (0, 0);
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling(count / (double)columns);
        return (columns, rows);
    }

    /// <summary>
    /// Fit every tile into <paramref name="viewWidth"/> × <paramref name="viewHeight"/>,
    /// in place, keeping the list order and each tile's aspect.
    /// </summary>
    /// <remarks>
    /// A view smaller than the gaps still lays out — the cells clamp to a
    /// positive size rather than collapsing — because the alternative is a
    /// button that appears to do nothing when a window is small.
    /// </remarks>
    public static void Arrange(IList<BoardTile> tiles, double viewWidth, double viewHeight)
    {
        if (tiles.Count == 0) return;
        var (columns, rows) = GridFor(tiles.Count);

        var cellWidth = Math.Max(1, (viewWidth - Gap * (columns + 1)) / columns);
        var cellHeight = Math.Max(1, (viewHeight - Gap * (rows + 1)) / rows);

        for (var i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            var column = i % columns;
            var row = i / columns;

            // Uniform fit: the smaller of the two ratios, capped at 1 so a small
            // image is shown at its own size rather than blown up into blur.
            var scale = Math.Min(cellWidth / Math.Max(1, tile.Width), cellHeight / Math.Max(1, tile.Height));
            scale = Math.Min(scale, 1.0);
            var width = Math.Max(1, tile.Width * scale);
            var height = Math.Max(1, tile.Height * scale);

            var cellX = Gap + column * (cellWidth + Gap);
            var cellY = Gap + row * (cellHeight + Gap);
            tile.X = cellX + (cellWidth - width) / 2;
            tile.Y = cellY + (cellHeight - height) / 2;
            tile.Width = width;
            tile.Height = height;
        }
    }

    /// <summary>
    /// Where a newly pinned tile goes when the board already has a layout:
    /// below everything on it, at the left edge.
    /// </summary>
    /// <remarks>
    /// Not the middle, and not on top of the last one. A new reference that
    /// landed over an existing one would look like it had replaced it, and one
    /// dropped in the centre would have to be dragged off whatever it covered
    /// before it could be read. Underneath is the one place that is always free.
    /// </remarks>
    public static (double X, double Y) NextFreeSpot(IReadOnlyList<BoardTile> tiles)
    {
        if (tiles.Count == 0) return (Gap, Gap);
        var bottom = tiles.Max(t => t.Y + t.Height);
        return (Gap, bottom + Gap);
    }

    /// <summary>
    /// A tile's starting size from the picture's own pixels, capped so one large
    /// import does not arrive filling the whole board.
    /// </summary>
    public static (double Width, double Height) StartingSize(int pixelWidth, int pixelHeight, double longEdge = 480)
    {
        var w = Math.Max(1, pixelWidth);
        var h = Math.Max(1, pixelHeight);
        var scale = Math.Min(1.0, longEdge / Math.Max(w, h));
        return (Math.Max(1, w * scale), Math.Max(1, h * scale));
    }
}
