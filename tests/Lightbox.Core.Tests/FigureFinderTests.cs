using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.Core.Tests;

/// <summary>
/// Telling the drawings on a reference sheet apart from the furniture.
/// </summary>
/// <remarks>
/// The sheets this exists for are pages, not atlases: a title banner across
/// the top, a watermark in the corners, figures in the middle. Every one of
/// those defeats the projection-based slicer, and each is written here as a
/// picture so the reason stays legible.
/// </remarks>
public class FigureFinderTests
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

    private static List<Box> Figures(params string[] rows)
    {
        var (occupied, w, h) = Sheet(rows);
        return FigureFinder.Figures(occupied, w, h);
    }

    private static List<ReferenceCell> Detect(params string[] rows)
    {
        var (occupied, w, h) = Sheet(rows);
        return StripSlicer.Detect(occupied, w, h);
    }

    private static string Boxes(IEnumerable<ReferenceCell> cells) =>
        string.Join(" ", cells.Select(c => $"{c.X},{c.Y},{c.Width}x{c.Height}"));

    // ---- blobs ------------------------------------------------------------------

    [Fact]
    public void TouchingDiagonallyIsStillOneThing()
    {
        // A pen line that steps a pixel sideways is 8-connected, not 4. Reading
        // it as two blobs splits half the drawings on a hand-inked sheet.
        var (occupied, w, h) = Sheet(
            "#...",
            ".#..",
            "..#.");

        Assert.Single(FigureFinder.Blobs(occupied, w, h));
    }

    [Fact]
    public void ABlobIsBoundedByWhatItActuallyCovers()
    {
        var (occupied, w, h) = Sheet(
            "....",
            ".##.",
            ".##.",
            "....");

        var box = Assert.Single(FigureFinder.Blobs(occupied, w, h));
        Assert.Equal(new Box(1, 1, 2, 2), box);
    }

    // ---- the furniture ----------------------------------------------------------

    [Fact]
    public void AWatermarkIsTooSmallToBeADrawing()
    {
        // Specks, dust and a signature in the corner. Everything here is
        // relative to the biggest thing on the sheet, because "small" means
        // the same on a 400px sheet and a 4000px one.
        var figures = Figures(
            "#.....................",
            "..........####........",
            "..........####........",
            "..........####........",
            "..........####........",
            ".....................#");

        var one = Assert.Single(figures);
        Assert.Equal(new Box(10, 1, 4, 4), one);
    }

    [Fact]
    public void ATitleBannerIsNotARowOfFrames()
    {
        // The case that defeats projection slicing outright: the banner is
        // content in every column, so the column projection never returns to
        // zero and the entire sheet reads as one cell.
        var figures = Figures(
            "######################",
            "......................",
            "..####......####......",
            "..####......####......",
            "..####......####......",
            "..####......####......");

        Assert.Equal(2, figures.Count);
        Assert.All(figures, f => Assert.Equal(2, f.Y));
    }

    [Fact]
    public void AFigureDrawnInSeveralPiecesCountsOnce()
    {
        // The second figure's head is a separate blob with a gap under it. It
        // still counts as one drawing, because the row it is in holds the
        // pieces together and within a row anything that overlaps horizontally
        // is the same figure.
        //
        // Overlap is the test rather than a gap threshold: a threshold needs a
        // length, and any length that joins a head to its shoulders on one
        // sheet joins two rows on another.
        var figures = Figures(
            "..##........##........",
            "..##........##........",
            "..##..................",
            "..##........##........",
            "..##........##........",
            "..##........##........");

        Assert.Equal(2, figures.Count);
        Assert.Equal(new Box(2, 0, 2, 6), figures[0]);
        Assert.Equal(new Box(12, 0, 2, 6), figures[1]);
    }

    [Fact]
    public void APieceSeparatedFromEveryOtherRowBecomesItsOwnRow()
    {
        // The limit of the rule above, written down rather than discovered. A
        // sheet whose every figure has a floating head with a clear gap under
        // it reads as two rows of half-figures, because nothing spans the gap
        // to hold them together. Set the grid by hand for that sheet.
        var figures = Figures(
            "..##........##........",
            "......................",
            "..##........##........",
            "..##........##........",
            "..##........##........",
            "..##........##........");

        // The floating row is thin next to the bodies, so it is discarded as
        // furniture rather than treated as frames.
        Assert.Equal(2, figures.Count);
        Assert.All(figures, f => Assert.Equal(2, f.Y));
    }

    [Fact]
    public void ASheetWithOnlyOneRowKeepsIt()
    {
        // A single band cannot be thin relative to anything, and judging it
        // against itself would throw away every one-row sheet.
        var figures = Figures(
            "..####......####......",
            "..####......####......");

        Assert.Equal(2, figures.Count);
    }

    [Fact]
    public void AnEmptySheetFindsNothingRatherThanOneHugeFrame()
    {
        Assert.Empty(Figures("....", "...."));
    }

    // ---- what the slicer makes of it --------------------------------------------

    [Fact]
    public void DetectionCutsAnEvenAtlasIntoEqualCells()
    {
        // The good case, and the one that has to stay exact: cells of drifting
        // widths shift the drawing inside them by a drifting amount, and the
        // reference crawls sideways across the cycle.
        var cells = Detect(
            "..##....##....##....##..",
            "..##....##....##....##..");

        Assert.Equal(4, cells.Count);
        Assert.Equal("0,0,6x2 6,0,6x2 12,0,6x2 18,0,6x2", Boxes(cells));
    }

    [Fact]
    public void TheBannerIsGoneFromTheCellsToo()
    {
        var cells = Detect(
            "######################",
            "......................",
            "..####......####......",
            "..####......####......",
            "..####......####......",
            "..####......####......");

        Assert.Equal(2, cells.Count);
        // The row of cells starts where the figures do, not at the top of the
        // page — a cell that included the banner would draw it over the canvas
        // on every frame.
        Assert.All(cells, c => Assert.Equal(2, c.Y));
        Assert.All(cells, c => Assert.Equal(4, c.Height));
    }

    [Fact]
    public void TwoRowsBecomeTwoBandsOfCells()
    {
        var cells = Detect(
            "..##....##..",
            "..##....##..",
            "............",
            "..##....##..",
            "..##....##..");

        Assert.Equal(4, cells.Count);
        Assert.Equal(2, cells.Count(c => c.Y == 0));
        Assert.Equal(2, cells.Count(c => c.Y == 3));
    }

    [Fact]
    public void CellsInARowShareTheirTopAndHeightEvenWhenThePosesDoNot()
    {
        // A crouch next to a stand. If each cell hugged its own drawing the
        // two would be pinned to the same place on the canvas and the pose
        // change would read as a jump.
        var cells = Detect(
            "..##........",
            "..##....##..",
            "..##....##..");

        Assert.Equal(2, cells.Count);
        Assert.Equal(cells[0].Y, cells[1].Y);
        Assert.Equal(cells[0].Height, cells[1].Height);
    }

    // ---- pivots -----------------------------------------------------------------

    [Fact]
    public void ACellWithNoPivotAssumesTheMiddleOfItsFoot()
    {
        // Where a standing figure's contact point usually is, and a better
        // guess than the centre for the thing pivots are for.
        var cell = new ReferenceCell { X = 10, Y = 20, Width = 40, Height = 60 };

        Assert.False(cell.HasPivot);
        Assert.Equal((30.0, 80.0), cell.Pivot);
    }

    [Fact]
    public void APlacedPivotIsAbsoluteSoResizingTheCellDoesNotMoveIt()
    {
        // The pivot is a mark on the drawing; the cell is a window that happens
        // to contain it. Storing it relative would drag the registration point
        // every time somebody adjusted the box.
        var cell = new ReferenceCell { X = 10, Y = 20, Width = 40, Height = 60, PivotX = 25, PivotY = 74 };

        cell.Width = 50;
        cell.Height = 70;

        Assert.True(cell.HasPivot);
        Assert.Equal((25.0, 74.0), cell.Pivot);
    }
}
