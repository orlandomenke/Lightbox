using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// Finding the frames in a reference sheet.
///
/// Written against a picture rather than a PNG: each test draws its sheet as
/// rows of characters, where <c>#</c> is content and <c>.</c> is background.
/// The interesting mistakes here are geometric, not about decoding — and the
/// most interesting one of all is silent, because a sheet sliced the wrong way
/// still produces the right <em>number</em> of frames.
/// </summary>
public class StripSlicerTests
{
    /// <summary>A sheet drawn as text. Every row must be the same length.</summary>
    private static (bool[] Occupied, int W, int H) Sheet(params string[] rows)
    {
        var h = rows.Length;
        var w = rows[0].Length;
        var occupied = new bool[w * h];
        for (var y = 0; y < h; y++)
        {
            Assert.Equal(w, rows[y].Length);
            for (var x = 0; x < w; x++) occupied[y * w + x] = rows[y][x] == '#';
        }
        return (occupied, w, h);
    }

    private static List<ReferenceCell> Slice(SliceOptions options, params string[] rows)
    {
        var (occupied, w, h) = Sheet(rows);
        return StripSlicer.Slice(occupied, w, h, options);
    }

    private static List<ReferenceCell> Slice(params string[] rows) => Slice(default, rows);

    private static string Boxes(IEnumerable<ReferenceCell> cells) =>
        string.Join(" ", cells.Select(c => $"{c.X},{c.Y},{c.Width}x{c.Height}"));

    // ---- the shape of a strip ---------------------------------------------------

    [Fact]
    public void AStripOfFramesIsCutAtTheGutters()
    {
        var cells = Slice(
            "..##...##...##..",
            "..##...##...##..");

        Assert.Equal(3, cells.Count);
        Assert.Equal("0,0,5x2 5,0,6x2 11,0,5x2", Boxes(cells));
    }

    [Fact]
    public void TheCellsTileTheSheetWithNothingLeftOver()
    {
        // Every pixel of the sheet belongs to exactly one cell. This is what
        // makes the frames comparable: a gap between them would be art that
        // silently vanished, and an overlap would be art shown twice.
        var cells = Slice(
            "..#...#...#...#.",
            "..#...#...#...#.");

        Assert.Equal(0, cells[0].X);
        Assert.Equal(16, cells[^1].X + cells[^1].Width);
        for (var i = 1; i < cells.Count; i++)
        {
            Assert.Equal(cells[i - 1].X + cells[i - 1].Width, cells[i].X);
        }
    }

    [Fact]
    public void AFrameKeepsWhereItsDrawingSatOnTheSheet()
    {
        // The bug this exists to prevent, and the reason cells are windows
        // rather than crops. Here the figure is at the left of frame 1, the
        // middle of frame 2 and the right of frame 3 — a character walking.
        // Bounding-box slicing would give three identical cells and the
        // playback would stand perfectly still.
        const string row = "#........#........#.";
        var cells = Slice(row, row);

        Assert.Equal(3, cells.Count);
        // Content offset inside each cell: left, middle, right.
        var offsets = cells.Select(c => FirstContentColumn(c, row)).ToList();
        Assert.True(offsets[0] < offsets[1] && offsets[1] < offsets[2],
            $"the figure must move across its cell, got offsets {string.Join(",", offsets)}");
    }

    private static int FirstContentColumn(ReferenceCell cell, string row)
    {
        for (var x = cell.X; x < cell.X + cell.Width; x++)
        {
            if (row[x] == '#') return x - cell.X;
        }
        return -1;
    }

    [Fact]
    public void UnevenGuttersAreSnappedToAnEvenGrid()
    {
        // A frame where the character throws an arm out has a narrower gutter
        // beside it, so the detected cut lands a pixel or two off. Left alone
        // that reads as the reference twitching once per cycle — the flicker
        // invariant 2 forbids in rendering, arriving by a different door.
        // Four frames on a 20-wide sheet, so the cuts belong at 5, 10 and 15.
        // Detection puts them at 4, 10 and 14, because the gutters either side
        // of frame 2 are eaten by the arm.
        var cells = Slice(
            "####.#####.###.####.",
            ".##...###..##...##..");

        Assert.Equal(4, cells.Count);
        Assert.All(cells, c => Assert.Equal(5, c.Width));
        Assert.Equal("0,0,5x2 5,0,5x2 10,0,5x2 15,0,5x2", Boxes(cells));
    }

    [Fact]
    public void AGridIsFoundInBothDirections()
    {
        var cells = Slice(
            ".##..##.",
            ".##..##.",
            "........",
            ".##..##.",
            ".##..##.");

        Assert.Equal(4, cells.Count);
        Assert.Equal("0,0,4x2 4,0,4x2 0,2,4x3 4,2,4x3", Boxes(cells));
    }

    [Fact]
    public void ReadingOrderIsLeftToRightThenTopToBottom()
    {
        var cells = Slice(
            "#...#...",
            "........",
            "#...#...");

        Assert.Equal([(0, 0), (4, 0), (0, 2), (4, 2)], cells.Select(c => (c.X, c.Y)).ToList());
    }

    [Fact]
    public void AnEmptySheetHasNoFrames()
    {
        Assert.Empty(Slice(
            "....",
            "...."));
    }

    [Fact]
    public void ASingleDrawingIsOneFrameCoveringTheSheet()
    {
        var cells = Slice(
            "..##..",
            "..##..");

        Assert.Equal("0,0,6x2", Boxes(cells));
    }

    // ---- the override ------------------------------------------------------------

    [Fact]
    public void AskingForColumnsIgnoresWhatThePixelsSay()
    {
        // The arrangement detection genuinely cannot find: two rows that touch,
        // with no gutter between them to find. Guessing would be worse than
        // letting the artist say so.
        var cells = Slice(new SliceOptions(Columns: 2, Rows: 2),
            "#..#",
            "#..#",
            "#..#",
            "#..#");

        Assert.Equal(4, cells.Count);
        Assert.Equal("0,0,2x2 2,0,2x2 0,2,2x2 2,2,2x2", Boxes(cells));
    }

    [Fact]
    public void ADeclaredGridKeepsItsEmptyCells()
    {
        // An empty cell in a sheet the artist declared 4 wide is a beat — a
        // held pose, or a frame they have not drawn yet. Dropping it would
        // slide every later frame one place earlier, which is the timing
        // going wrong rather than a frame going missing.
        var cells = Slice(new SliceOptions(Columns: 4),
            "#.#..#.#",
            "#.#..#.#");

        Assert.Equal(4, cells.Count);
    }

    [Fact]
    public void ADetectedGridDropsCellsWithNothingInThem()
    {
        // The other side of the same coin: nothing declared the arrangement,
        // so an empty box in the cross product is an artefact of crossing rows
        // with columns, not a frame anybody drew.
        var cells = Slice(
            "##...##.",
            "##...##.",
            "........",
            "##......",
            "##......");

        Assert.Equal(3, cells.Count);
        Assert.DoesNotContain(cells, c => c.X >= 4 && c.Y >= 2);
    }

    [Fact]
    public void ADrawingOffCentreInItsCellDoesNotDragTheCutWithIt()
    {
        // The bug that only shows up on a real sheet. Cutting at the midpoint
        // of each gutter is only right when every drawing is centred in its
        // cell; a figure leaning one way leaves an uneven gutter, and the cut
        // lands off by half the difference. On a six-frame run cycle the cells
        // came out 90, 82, 80, 78, 78, 72 wide instead of six even 80s — and
        // since each cell is drawn at the same place on the canvas, the
        // reference crawls sideways across the cycle.
        //
        // Here every figure sits hard against the left of its 5-wide cell, so
        // midpoints would give cuts at 3, 8, 13 rather than 5, 10, 15.
        var cells = Slice(
            "##...##...##...##...",
            "#....#....#....#....");

        Assert.Equal(4, cells.Count);
        Assert.Equal("0,0,5x2 5,0,5x2 10,0,5x2 15,0,5x2", Boxes(cells));
    }

    [Fact]
    public void AGenuinelyIrregularSheetFallsBackToTheGutters()
    {
        // Three frames of wildly different widths: no even division of the
        // sheet holds them one to a cell, so there is no grid to find and the
        // gutter midpoints are the best answer available.
        var cells = Slice(
            "###..####...#######.",
            ".#...##.....#####...");

        Assert.Equal(3, cells.Count);
        Assert.Equal("0,0,4x2 4,0,6x2 10,0,10x2", Boxes(cells));
    }

    [Fact]
    public void GridSlicesWithoutLookingAtTheImageAtAll()
    {
        var cells = StripSlicer.Grid(100, 40, 4, 2);

        Assert.Equal(8, cells.Count);
        Assert.All(cells, c => Assert.Equal(25, c.Width));
        Assert.All(cells, c => Assert.Equal(20, c.Height));
    }

    [Fact]
    public void AGridThatDoesNotDivideEvenlyStillTilesTheSheet()
    {
        var cells = StripSlicer.Grid(10, 3, 3, 1);

        Assert.Equal(3, cells.Count);
        Assert.Equal(0, cells[0].X);
        Assert.Equal(10, cells[^1].X + cells[^1].Width);
        for (var i = 1; i < cells.Count; i++)
        {
            Assert.Equal(cells[i - 1].X + cells[i - 1].Width, cells[i].X);
        }
    }

    // ---- reading the pixels --------------------------------------------------------

    [Fact]
    public void TransparentSurroundIsBackground()
    {
        // ..#.  as RGBA: two clear pixels, one opaque, one clear.
        byte[] rgba =
        [
            0, 0, 0, 0,
            0, 0, 0, 0,
            20, 30, 40, 255,
            0, 0, 0, 0,
        ];

        Assert.Equal([false, false, true, false], StripSlicer.Occupancy(rgba, 4, 1));
    }

    [Fact]
    public void AFlatOpaqueSurroundIsBackgroundToo()
    {
        // A scan of a page, or a sheet exported onto white. Alpha says nothing
        // here, so the corners have to.
        var rgba = new byte[4 * 4];
        for (var i = 0; i < 4; i++)
        {
            rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = 255;
        }
        rgba[8] = rgba[9] = rgba[10] = 10; // one dark pixel in the middle

        Assert.Equal([false, false, true, false], StripSlicer.Occupancy(rgba, 4, 1));
    }

    [Fact]
    public void ASheetWithNoFlatSurroundIsAllContent()
    {
        // A photograph. There is no background to subtract, so the honest
        // answer is one frame — and the artist can say otherwise.
        byte[] rgba =
        [
            200, 10, 10, 255,
            10, 200, 10, 255,
            10, 10, 200, 255,
            200, 200, 10, 255,
        ];

        Assert.All(StripSlicer.Occupancy(rgba, 4, 1), Assert.True);
    }

    [Fact]
    public void NearlyTransparentNoiseIsNotContent()
    {
        byte[] rgba = [0, 0, 0, 3, 0, 0, 0, 0, 90, 90, 90, 255, 0, 0, 0, 2];

        Assert.Equal([false, false, true, false], StripSlicer.Occupancy(rgba, 4, 1));
    }
}
