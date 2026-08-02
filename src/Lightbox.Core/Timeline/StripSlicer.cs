using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// How to cut a reference sheet into frames.
/// </summary>
/// <param name="Columns">
/// Force a grid this many cells wide, ignoring what the pixels say. Zero
/// means "work it out". The override exists because one arrangement genuinely
/// cannot be detected: a two-row sheet whose rows touch has no gutter between
/// them, and no amount of looking at pixels will find a boundary that is not
/// there. Guessing in that case would be worse than asking.
/// </param>
/// <param name="Alpha">
/// Alpha at or below this counts as background. Sheets exported from other
/// tools often carry a few units of noise in their transparent regions.
/// </param>
/// <param name="Tolerance">
/// How far a pixel may sit from the sheet's background colour and still count
/// as background, per channel. Only used for sheets with no alpha.
/// </param>
public readonly record struct SliceOptions(
    int Columns = 0,
    int Rows = 0,
    byte Alpha = 8,
    int Tolerance = 10);

/// <summary>
/// Finding the frames in a reference sheet.
/// </summary>
/// <remarks>
/// <para>
/// Pure, and separate from any image codec, so the interesting cases can be
/// written as a picture in a test rather than built as a PNG. What is
/// interesting here is not decoding — it is that the cells must <b>tile</b>
/// the sheet.
/// </para>
/// <para>
/// The naive reading of "find each frame" is to take the bounding box of each
/// blob of content. That is wrong in a way that only shows up once the
/// reference is playing: a runner drawn near the left of one cell and the
/// right of the next has, by construction, identical bounding boxes, so
/// aligning by bounding box holds the character perfectly still. Cutting at
/// the midpoint of the gutter instead keeps each drawing where it was on the
/// sheet, and the run cycle runs.
/// </para>
/// </remarks>
public static class StripSlicer
{
    /// <summary>Cut a sheet into cells. Reading order: left to right, top to bottom.</summary>
    /// <param name="occupied">One flag per pixel, row-major: is this content?</param>
    public static List<ReferenceCell> Slice(
        ReadOnlySpan<bool> occupied, int width, int height, SliceOptions options = default)
    {
        if (width <= 0 || height <= 0) return [];

        var xs = options.Columns > 0
            ? EvenCuts(width, options.Columns)
            : Cuts(ColumnsWithContent(occupied, width, height), width);
        var ys = options.Rows > 0
            ? EvenCuts(height, options.Rows)
            : Cuts(RowsWithContent(occupied, width, height), height);

        var cells = new List<ReferenceCell>();
        for (var r = 0; r + 1 < ys.Count; r++)
        {
            for (var c = 0; c + 1 < xs.Count; c++)
            {
                var cell = new ReferenceCell
                {
                    X = xs[c],
                    Y = ys[r],
                    Width = xs[c + 1] - xs[c],
                    Height = ys[r + 1] - ys[r],
                };
                // A grid the artist asked for is kept whole — an empty cell in
                // a declared 4×2 sheet is a beat, and dropping it would slide
                // every later frame one place earlier. A detected grid only
                // ever proposes cells that had content in them anyway.
                if (options.Columns > 0 || options.Rows > 0 || HasContent(occupied, width, cell))
                {
                    cells.Add(cell);
                }
            }
        }
        return cells;
    }

    /// <summary>Which pixels are content: opaque, and not the sheet's background colour.</summary>
    /// <param name="rgba">Row-major RGBA, four bytes per pixel.</param>
    public static bool[] Occupancy(
        ReadOnlySpan<byte> rgba, int width, int height, SliceOptions options = default)
    {
        var occupied = new bool[width * height];
        if (rgba.Length < width * height * 4) return occupied;

        // The background is whatever the corners agree on. A sheet with a
        // transparent surround says so through alpha; a JPEG of a page from a
        // book says so with paper white, and reading it off the corners costs
        // nothing and handles both.
        var (bg, opaqueBackground) = Background(rgba, width, height, options.Alpha);

        for (var i = 0; i < occupied.Length; i++)
        {
            var p = i * 4;
            if (rgba[p + 3] <= options.Alpha) continue;
            if (opaqueBackground && Near(rgba, p, bg, options.Tolerance)) continue;
            occupied[i] = true;
        }
        return occupied;
    }

    /// <summary>
    /// Find the drawings on a sheet that is a page rather than an atlas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Slice"/> projects occupancy onto the axes, which is exactly
    /// right for a clean sprite sheet and hopeless for a page with a title
    /// banner across the top: the banner is content in every column, the
    /// column projection never returns to zero, and the whole sheet reads as
    /// one cell. This finds the figures first (see <see cref="FigureFinder"/>),
    /// discards the furniture, and only then decides where the cuts go.
    /// </para>
    /// <para>
    /// Each row of figures becomes its own band of cells, all sharing the
    /// band's top and height. That is what keeps a figure's vertical position
    /// inside its cell — the thing tight-cropping would destroy — while
    /// letting a sheet whose rows are different heights still work.
    /// </para>
    /// </remarks>
    public static List<ReferenceCell> Detect(
        ReadOnlySpan<bool> occupied, int width, int height, SliceOptions options = default)
    {
        var figures = FigureFinder.Figures(occupied, width, height);
        if (figures.Count == 0) return [];

        var cells = new List<ReferenceCell>();
        foreach (var band in BandsOf(figures))
        {
            var top = band.Min(f => f.Y);
            var bottom = band.Max(f => f.Bottom);
            foreach (var (x, right) in ColumnCuts(band, width))
            {
                cells.Add(new ReferenceCell { X = x, Y = top, Width = right - x, Height = bottom - top });
            }
        }
        return cells;
    }

    /// <summary>Rows of figures, by vertical overlap. Same rule as the finder's.</summary>
    private static List<List<Box>> BandsOf(List<Box> figures)
    {
        var order = figures.OrderBy(f => f.Y).ToList();
        var bands = new List<List<Box>>();
        var current = new List<Box> { order[0] };
        var bottom = order[0].Bottom;
        foreach (var figure in order.Skip(1))
        {
            if (figure.Y < bottom)
            {
                current.Add(figure);
                bottom = Math.Max(bottom, figure.Bottom);
                continue;
            }
            bands.Add(current);
            current = [figure];
            bottom = figure.Bottom;
        }
        bands.Add(current);
        return bands;
    }

    /// <summary>
    /// Where to cut one row into cells, best answer first.
    /// </summary>
    /// <remarks>
    /// Three hypotheses, each checked rather than assumed:
    /// <list type="number">
    /// <item>The row divides the whole sheet evenly — a clean atlas, and the
    /// answer that preserves each drawing's position exactly.</item>
    /// <item>It divides the figures' own extent evenly — an atlas with a
    /// margin round the outside.</item>
    /// <item>Neither: a hand-cut page, so cut at the midpoints of the gaps.
    /// This is the answer that drifts (see <see cref="EvenGridHolds"/>), taken
    /// only when there is genuinely no grid to find.</item>
    /// </list>
    /// </remarks>
    private static List<(int Start, int End)> ColumnCuts(List<Box> band, int sheetWidth)
    {
        var order = band.OrderBy(f => f.X).ToList();
        var runs = order.ConvertAll(f => (Start: f.X, End: f.Right - 1));

        if (EvenGridHolds(runs, sheetWidth)) return Pairs(EvenCuts(sheetWidth, runs.Count));

        var start = order[0].X;
        var end = order[^1].Right;
        var shifted = runs.ConvertAll(r => (Start: r.Start - start, End: r.End - start));
        if (EvenGridHolds(shifted, end - start))
        {
            return Pairs(EvenCuts(end - start, runs.Count).ConvertAll(c => c + start));
        }

        var cuts = new List<int> { start };
        for (var i = 1; i < runs.Count; i++) cuts.Add((runs[i - 1].End + runs[i].Start + 1) / 2);
        cuts.Add(end);
        return Pairs(cuts);
    }

    private static List<(int Start, int End)> Pairs(List<int> cuts)
    {
        var pairs = new List<(int, int)>();
        for (var i = 0; i + 1 < cuts.Count; i++) pairs.Add((cuts[i], cuts[i + 1]));
        return pairs;
    }

    /// <summary>An exact grid, ignoring the pixels entirely.</summary>
    public static List<ReferenceCell> Grid(int width, int height, int columns, int rows)
    {
        var xs = EvenCuts(width, Math.Max(1, columns));
        var ys = EvenCuts(height, Math.Max(1, rows));
        var cells = new List<ReferenceCell>();
        for (var r = 0; r + 1 < ys.Count; r++)
        {
            for (var c = 0; c + 1 < xs.Count; c++)
            {
                cells.Add(new ReferenceCell
                {
                    X = xs[c],
                    Y = ys[r],
                    Width = xs[c + 1] - xs[c],
                    Height = ys[r + 1] - ys[r],
                });
            }
        }
        return cells;
    }

    // ---- the cutting ---------------------------------------------------------

    /// <summary>
    /// Cut positions for n runs of content: n + 1 of them, so the cells tile.
    /// </summary>
    /// <remarks>
    /// An even division first, and gutter midpoints only if that will not fit.
    /// See <see cref="EvenGridHolds"/> for why round that way.
    /// </remarks>
    private static List<int> Cuts(List<(int Start, int End)> runs, int extent)
    {
        if (runs.Count == 0) return [];
        if (EvenGridHolds(runs, extent)) return EvenCuts(extent, runs.Count);

        var cuts = new List<int> { 0 };
        for (var i = 1; i < runs.Count; i++)
        {
            cuts.Add((runs[i - 1].End + runs[i].Start + 1) / 2);
        }
        cuts.Add(extent);
        return cuts;
    }

    /// <summary>
    /// Whether dividing the sheet evenly into one cell per run puts each run
    /// inside its own cell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the sprite-sheet hypothesis, checked rather than assumed — and
    /// checking it is what makes the detection usable, because the obvious
    /// estimator is quietly wrong. Cutting at the midpoint of each gutter only
    /// lands on the true boundary when every drawing is centred in its cell.
    /// A runner leaning forward leaves 15px on one side and 34px on the other,
    /// so the midpoint sits 10px off; the error is different for every gutter,
    /// and by the end of a six-frame sheet the cells are 90, 82, 80, 78, 78, 72
    /// wide when they should all be 80.
    /// </para>
    /// <para>
    /// That is not a cosmetic difference. Each cell is drawn at the same place
    /// on the canvas, so cells of drifting widths shift the drawing inside them
    /// by a drifting amount — the reference crawls sideways across the cycle.
    /// It is the flicker invariant 2 forbids in rendering, arriving by a
    /// different door.
    /// </para>
    /// <para>
    /// A genuinely irregular sheet — hand-cut, unequal frames — fails this and
    /// gets the midpoints, which is the best available answer when there is no
    /// grid to find.
    /// </para>
    /// </remarks>
    private static bool EvenGridHolds(List<(int Start, int End)> runs, int extent)
    {
        var pitch = extent / (double)runs.Count;
        for (var i = 0; i < runs.Count; i++)
        {
            var low = (int)Math.Round(i * pitch);
            var high = (int)Math.Round((i + 1) * pitch);
            if (runs[i].Start < low || runs[i].End >= high) return false;
        }
        return true;
    }

    private static List<int> EvenCuts(int extent, int count)
    {
        var cuts = new List<int>(count + 1);
        for (var i = 0; i <= count; i++) cuts.Add((int)Math.Round(i * extent / (double)count));
        return cuts;
    }

    private static List<(int Start, int End)> ColumnsWithContent(
        ReadOnlySpan<bool> occupied, int width, int height)
    {
        var flags = new bool[width];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (occupied[row + x]) flags[x] = true;
            }
        }
        return Runs(flags);
    }

    private static List<(int Start, int End)> RowsWithContent(
        ReadOnlySpan<bool> occupied, int width, int height)
    {
        var flags = new bool[height];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                if (!occupied[row + x]) continue;
                flags[y] = true;
                break;
            }
        }
        return Runs(flags);
    }

    private static List<(int Start, int End)> Runs(bool[] flags)
    {
        var runs = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i < flags.Length; i++)
        {
            if (flags[i] && start < 0) start = i;
            else if (!flags[i] && start >= 0)
            {
                runs.Add((start, i - 1));
                start = -1;
            }
        }
        if (start >= 0) runs.Add((start, flags.Length - 1));
        return runs;
    }

    private static bool HasContent(ReadOnlySpan<bool> occupied, int width, ReferenceCell cell)
    {
        for (var y = cell.Y; y < cell.Y + cell.Height; y++)
        {
            var row = y * width;
            for (var x = cell.X; x < cell.X + cell.Width; x++)
            {
                if (occupied[row + x]) return true;
            }
        }
        return false;
    }

    // ---- the background ------------------------------------------------------

    private static ((byte R, byte G, byte B) Colour, bool Opaque) Background(
        ReadOnlySpan<byte> rgba, int width, int height, byte alpha)
    {
        Span<int> corners =
        [
            0,
            (width - 1) * 4,
            (height - 1) * width * 4,
            ((height - 1) * width + width - 1) * 4,
        ];

        foreach (var p in corners)
        {
            if (rgba[p + 3] <= alpha) return (default, false);
        }

        var first = (rgba[corners[0]], rgba[corners[0] + 1], rgba[corners[0] + 2]);
        foreach (var p in corners)
        {
            // Corners that disagree mean the sheet has no flat surround —
            // a photograph, say. Then everything is content, and the whole
            // image is one frame unless the artist says otherwise.
            if (!Near(rgba, p, first, 6)) return (default, false);
        }
        return (first, true);
    }

    private static bool Near(ReadOnlySpan<byte> rgba, int p, (byte R, byte G, byte B) c, int tolerance) =>
        Math.Abs(rgba[p] - c.R) <= tolerance
        && Math.Abs(rgba[p + 1] - c.G) <= tolerance
        && Math.Abs(rgba[p + 2] - c.B) <= tolerance;
}
