namespace Lightbox.Core.Timeline;

/// <summary>A rectangle in sheet pixels.</summary>
public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public long Area => (long)Width * Height;

    public Box Union(Box other)
    {
        var x = Math.Min(X, other.X);
        var y = Math.Min(Y, other.Y);
        return new Box(x, y, Math.Max(Right, other.Right) - x, Math.Max(Bottom, other.Bottom) - y);
    }
}

/// <summary>
/// Telling the drawings on a reference sheet apart from everything else on it.
/// </summary>
/// <remarks>
/// <para>
/// A sheet an artist actually has is not a clean sprite atlas. It is a page
/// with a title banner across the top, a watermark repeated in the corners,
/// and eight figures in the middle. Projecting occupancy onto the axes — which
/// is all <see cref="StripSlicer"/> does — cannot tell those apart: the banner
/// is content in every column, so the column projection never returns to zero
/// and the whole sheet reads as one cell.
/// </para>
/// <para>
/// So: find the blobs first, throw away the ones that are not drawings, and
/// only then look at where the survivors sit. Every threshold here is relative
/// to what is on the sheet rather than absolute, because a sheet may be 400px
/// wide or 4000 and "small" means the same thing in both.
/// </para>
/// <para>
/// Pure, and separate from any image codec, so a sheet with a banner and a
/// watermark on it can be written as a picture in a test.
/// </para>
/// </remarks>
public static class FigureFinder
{
    /// <summary>
    /// A blob is noise below this share of the biggest blob's area.
    /// </summary>
    /// <remarks>
    /// Generous. A letter of a title, a watermark, a stray speck and a dust
    /// scan are all one to two orders of magnitude smaller than a figure; a
    /// figure's detached hand is not. Two per cent sits comfortably between.
    /// </remarks>
    private const double NoiseArea = 0.02;

    /// <summary>
    /// A band is not a row of drawings below this share of the tallest band.
    /// </summary>
    /// <remarks>
    /// The title banner's survival case: capital letters set large enough can
    /// clear the area filter, but a line of text is still a fraction of the
    /// height of a row of figures. Half is deliberately lenient — a row of
    /// crouching poses under a row of standing ones must not be thrown away.
    /// </remarks>
    private const double ThinBand = 0.4;

    /// <summary>
    /// Every connected region of content, 8-connected.
    /// </summary>
    /// <param name="occupied">One flag per pixel, row-major.</param>
    /// <remarks>
    /// Iterative rather than recursive: a figure on a 4000px sheet is tens of
    /// thousands of pixels deep and a recursive flood fill overflows the stack
    /// on exactly the sheets this feature exists for.
    /// </remarks>
    public static List<Box> Blobs(ReadOnlySpan<bool> occupied, int width, int height)
    {
        var boxes = new List<Box>();
        if (width <= 0 || height <= 0 || occupied.Length < width * height) return boxes;

        var seen = new bool[width * height];
        var stack = new Stack<int>();
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || !occupied[start]) continue;
            seen[start] = true;
            stack.Push(start);
            int minX = width, minY = height, maxX = -1, maxY = -1;
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % width;
                var y = index / width;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height) continue;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= width) continue;
                        var next = ny * width + nx;
                        if (seen[next] || !occupied[next]) continue;
                        seen[next] = true;
                        stack.Push(next);
                    }
                }
            }
            boxes.Add(new Box(minX, minY, maxX - minX + 1, maxY - minY + 1));
        }
        return boxes;
    }

    /// <summary>
    /// The drawings on the sheet, in reading order, with the furniture removed.
    /// </summary>
    /// <remarks>
    /// Three passes, each removing a different kind of thing that is not a
    /// drawing: specks and letters go on area, the title banner goes as a thin
    /// band, and what is left in each band is merged so that a figure drawn in
    /// several strokes counts once.
    /// </remarks>
    public static List<Box> Figures(ReadOnlySpan<bool> occupied, int width, int height)
    {
        var blobs = Blobs(occupied, width, height);
        if (blobs.Count == 0) return blobs;

        var biggest = blobs.Max(b => b.Area);
        var kept = blobs.Where(b => b.Area >= biggest * NoiseArea).ToList();
        if (kept.Count == 0) return [];

        var bands = Bands(kept);
        // Only judge bands against each other when there is more than one; a
        // single band is the whole sheet and cannot be "thin" relative to
        // anything.
        if (bands.Count > 1)
        {
            var tallest = bands.Max(b => b.Max(x => x.Bottom) - b.Min(x => x.Y));
            bands = bands
                .Where(b => b.Max(x => x.Bottom) - b.Min(x => x.Y) >= tallest * ThinBand)
                .ToList();
        }

        var figures = new List<Box>();
        foreach (var band in bands) figures.AddRange(MergeAcross(band));
        return figures;
    }

    /// <summary>
    /// Group boxes into rows: two boxes are in the same row if their vertical
    /// spans overlap at all.
    /// </summary>
    /// <remarks>
    /// Overlap rather than proximity, because a row of drawings is exactly a
    /// set of things that occupy the same horizontal strip, whatever their
    /// individual heights. Merged transitively, so a tall figure can pull two
    /// short neighbours into one row.
    /// </remarks>
    private static List<List<Box>> Bands(List<Box> boxes)
    {
        var order = boxes.OrderBy(b => b.Y).ToList();
        var bands = new List<List<Box>>();
        var current = new List<Box> { order[0] };
        var bottom = order[0].Bottom;
        for (var i = 1; i < order.Count; i++)
        {
            if (order[i].Y < bottom)
            {
                current.Add(order[i]);
                bottom = Math.Max(bottom, order[i].Bottom);
                continue;
            }
            bands.Add(current);
            current = [order[i]];
            bottom = order[i].Bottom;
        }
        bands.Add(current);
        return bands;
    }

    /// <summary>
    /// Within one row, merge boxes whose horizontal spans overlap.
    /// </summary>
    /// <remarks>
    /// A figure drawn with a detached hand, a dropped shadow or a separate
    /// head is several blobs standing in the same place. Overlap is the test
    /// rather than a gap threshold: a gap threshold would need a length, and
    /// any length that joins a hand to its arm on one sheet joins two figures
    /// on another.
    /// </remarks>
    private static List<Box> MergeAcross(List<Box> band)
    {
        var order = band.OrderBy(b => b.X).ToList();
        var merged = new List<Box> { order[0] };
        foreach (var box in order.Skip(1))
        {
            var last = merged[^1];
            if (box.X < last.Right) merged[^1] = last.Union(box);
            else merged.Add(box);
        }
        return merged;
    }
}
