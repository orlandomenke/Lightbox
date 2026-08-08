using Lightbox.App.Controls;

namespace Lightbox.App.Tests;

/// <summary>
/// How many controls fit in the tool options bar before the rest go into the
/// ▾. Arithmetic, so it is tested without a window.
/// </summary>
public class OverflowBarTests
{
    private const double Spacing = 10;
    private const double More = 24;

    [Fact]
    public void EverythingFitsWhenThereIsRoom()
    {
        // And no room is reserved for a ▾ that would have nothing in it.
        Assert.Equal(3, OverflowBar.Split([50, 50, 50], available: 170, Spacing, More));
        Assert.Equal(3, OverflowBar.Split([50, 50, 50], available: 1000, Spacing, More));
    }

    [Fact]
    public void ExactlyEnoughRoomStillFits()
    {
        // 3 × 50 + 2 × 10 = 170. One pixel less and the last one goes.
        Assert.Equal(3, OverflowBar.Split([50, 50, 50], 170, Spacing, More));
        Assert.Equal(2, OverflowBar.Split([50, 50, 50], 169, Spacing, More));
    }

    [Fact]
    public void TheOverflowButtonTakesItsOwnRoomOnceItIsNeeded()
    {
        // 160 would hold two items and a gap (110) with 50 spare — but the ▾
        // has to go somewhere, so the budget is 160 − 24 − 10 = 126.
        Assert.Equal(2, OverflowBar.Split([50, 50, 50], 160, Spacing, More));
        // Tighten it until even two will not fit.
        Assert.Equal(1, OverflowBar.Split([50, 50, 50], 120, Spacing, More));
    }

    [Fact]
    public void AnItemTooWideForTheBarAtAllIsPushedIntoTheMenu()
    {
        // Rather than clipped in place, where it would be a control you can
        // see half of and cannot use.
        Assert.Equal(0, OverflowBar.Split([500], 100, Spacing, More));
    }

    [Fact]
    public void AnEmptyBarOverflowsNothing()
    {
        Assert.Equal(0, OverflowBar.Split([], 100, Spacing, More));
    }

    [Fact]
    public void TheOrderIsKept()
    {
        // The split is a prefix — the items that fit are always the first ones,
        // so the bar reads in the order it was written and the menu continues
        // where the bar stopped.
        var widths = new double[] { 30, 40, 50, 60 };
        for (var available = 0.0; available < 300; available += 7)
        {
            var shown = OverflowBar.Split(widths, available, Spacing, More);
            Assert.InRange(shown, 0, widths.Length);
        }
    }

    // The flexible child — the owner's rule for the timeline bar: "decrease
    // the slider first, then hide the option under a dropdown."

    [Fact]
    public void TheFlexibleChildIsAtFullWidthWhenEverythingFits()
    {
        var widths = new double[] { 30, 100, 30 };
        var (shown, flex) = OverflowBar.Split(widths, 200, Spacing, More, flexIndex: 1, flexMin: 50);
        Assert.Equal(3, shown);
        Assert.Equal(100, flex);
    }

    [Fact]
    public void TheFlexibleChildGivesWayBeforeAnythingOverflows()
    {
        // 30+100+30 + 2x10 spacing = 180 wants; 150 available; 30 over. The
        // slider absorbs all of it: nothing goes to the menu.
        var widths = new double[] { 30, 100, 30 };
        var (shown, flex) = OverflowBar.Split(widths, 150, Spacing, More, flexIndex: 1, flexMin: 50);
        Assert.Equal(3, shown);
        Assert.Equal(70, flex);
    }

    [Fact]
    public void OnlyAtTheFloorDoesTheMenuStart()
    {
        // 118 wanted even with the slider at its 50 floor; 100 available —
        // now the ordinary split runs, with the flexible child held at the
        // floor rather than its natural width.
        var widths = new double[] { 30, 100, 30 };
        var (shown, flex) = OverflowBar.Split(widths, 100, Spacing, More, flexIndex: 1, flexMin: 50);
        Assert.Equal(50, flex);
        Assert.True(shown < 3, "everything claimed to fit in a bar two-thirds the floor total");
    }

    [Fact]
    public void NoFlexIndexBehavesExactlyAsBefore()
    {
        var widths = new double[] { 30, 40, 50, 60 };
        for (var available = 0.0; available < 300; available += 7)
        {
            var plain = OverflowBar.Split(widths, available, Spacing, More);
            var (shown, _) = OverflowBar.Split(widths, available, Spacing, More, flexIndex: -1, flexMin: 0);
            Assert.Equal(plain, shown);
        }
    }
}
