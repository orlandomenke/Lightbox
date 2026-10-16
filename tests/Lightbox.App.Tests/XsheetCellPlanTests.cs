using Lightbox.App.Controls;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// What each cel of a row says on the drawn sheet (Q282): the drawing's number
/// where it lands, the same number carried along its hold, nothing before the
/// row's first drawing and nothing past the end of the scene.
/// </summary>
/// <remarks>
/// Numbers count the drawings along the row, 1 first. Until one drawing can be
/// exposed in several cels (Q282's second answer) every exposure is its own
/// drawing, so a running count is exactly the drawing's identity on this row.
/// </remarks>
public class XsheetCellPlanTests
{
    /// <summary>A row from a pattern: K keyed, B blank keyed, . hold, ~ past the end of the scene.</summary>
    private static List<FrameCell> Row(string pattern) =>
        pattern.Select((c, i) => new FrameCell(i)
        {
            IsKeyed = c is 'K' or 'B',
            IsBlank = c == 'B',
            IsVirtual = c == '~',
        }).ToList();

    [Fact]
    public void ANumberCountsTheDrawingsAlongTheRow()
    {
        var plan = XsheetCellPlan.Of(Row("K.K..K"));

        Assert.Equal([1, 1, 2, 2, 2, 3], plan.Select(g => g.Number));
        Assert.Equal([true, false, true, false, false, true], plan.Select(g => g.Starts));
    }

    [Fact]
    public void AHoldRunsToTheCelBeforeTheNextDrawing()
    {
        var plan = XsheetCellPlan.Of(Row("K.K..K"));

        Assert.Equal([1, 1, 4, 4, 4, 5], plan.Select(g => g.HoldEnd));
    }

    [Fact]
    public void TheLastHoldStopsAtTheEndOfTheScene()
    {
        var plan = XsheetCellPlan.Of(Row("K..~~"));

        Assert.Equal(2, plan[0].HoldEnd);
        Assert.Equal(0, plan[3].Number);
        Assert.Equal(0, plan[4].Number);
    }

    [Fact]
    public void NothingIsNumberedBeforeTheFirstDrawing()
    {
        var plan = XsheetCellPlan.Of(Row("..K."));

        Assert.Equal([0, 0, 1, 1], plan.Select(g => g.Number));
        Assert.False(plan[0].Starts);
    }

    [Fact]
    public void ABlankDrawingIsCountedAndMarkedBlank()
    {
        // A blank keyframe is a drawing with nothing on it (Q206): it takes a
        // number like any other, and the sheet has to be able to tell it apart.
        var plan = XsheetCellPlan.Of(Row("KB.K"));

        Assert.Equal([1, 2, 2, 3], plan.Select(g => g.Number));
        Assert.Equal([false, true, true, false], plan.Select(g => g.Blank));
    }

    [Fact]
    public void AnEmptyRowPlansNothing() => Assert.Empty(XsheetCellPlan.Of([]));
}
