using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The crop rectangle's drag arithmetic, driven without a window.
/// </summary>
/// <remarks>
/// Every one of these is a gesture that is tedious to check by hand and silent
/// when it breaks: a handle that grabs from the wrong place, a rectangle that
/// squashes instead of stopping at the paper's edge, an aspect lock that quietly
/// stops holding at the moment somebody is watching the numbers.
/// </remarks>
public class CropSessionTests(ITestOutputHelper output)
{
    /// <summary>A session on a 960 × 540 page at the origin.</summary>
    private static CropSession Page()
    {
        var crop = new CropSession();
        crop.Begin(0, 0, 960, 540);
        return crop;
    }

    private static string Rect(CropSession c) =>
        $"({c.Left:0.##}, {c.Top:0.##}) → ({c.Right:0.##}, {c.Bottom:0.##})";

    [Fact]
    public void ItOpensOnTheWholePage()
    {
        var crop = Page();

        output.WriteLine(Rect(crop));
        Assert.Equal(0, crop.Left);
        Assert.Equal(960, crop.Right);
        Assert.Equal(540, crop.Bottom);
        // Nothing dragged yet, so applying it would be a no-op — and the tool
        // says so rather than offering an Apply that does nothing.
        Assert.False(crop.WouldChangeAnything);
    }

    // ---- picking up ---------------------------------------------------------------

    [Fact]
    public void TheHandlesAreWhereTheyLook()
    {
        var crop = Page();

        Assert.Equal(CropHandle.TopLeft, crop.HandleAt(2, 2, 6));
        Assert.Equal(CropHandle.BottomRight, crop.HandleAt(958, 538, 6));
        Assert.Equal(CropHandle.Top, crop.HandleAt(480, 3, 6));
        Assert.Equal(CropHandle.Left, crop.HandleAt(1, 270, 6));
        Assert.Equal(CropHandle.Inside, crop.HandleAt(480, 270, 6));
    }

    /// <summary>
    /// An edge is grabbable along its own span and nowhere else.
    /// </summary>
    /// <remarks>
    /// Testing the infinite line instead of the segment is the easy mistake,
    /// and it makes the left edge grabbable from anywhere above or below the
    /// rectangle — which reads as the tool snatching at empty canvas.
    /// </remarks>
    [Fact]
    public void AnEdgeIsNotGrabbableOffTheEndOfItself()
    {
        var crop = Page();
        crop.Press(200, 100, 6);
        crop.Move(600, 400);
        crop.Release();
        output.WriteLine(Rect(crop));

        // On the left edge, level with the rectangle: the edge.
        Assert.Equal(CropHandle.Left, crop.HandleAt(200, 250, 6));
        // On the same vertical line, far above it: nothing.
        Assert.Equal(CropHandle.None, crop.HandleAt(200, 20, 6));
    }

    // ---- dragging a new rectangle out ----------------------------------------------

    /// <summary>
    /// Pressing outside starts a fresh rectangle rather than doing nothing.
    /// </summary>
    /// <remarks>
    /// A crop tool where clicking off the frame does nothing looks broken the
    /// first time somebody wants to re-frame from scratch.
    /// </remarks>
    [Fact]
    public void PressingOutsideDragsANewRectangle()
    {
        var crop = Page();
        crop.Press(200, 100, 6);
        crop.Move(500, 300);
        crop.Release();

        output.WriteLine(Rect(crop));
        Assert.Equal(200, crop.Left);
        Assert.Equal(100, crop.Top);
        Assert.Equal(500, crop.Right);
        Assert.Equal(300, crop.Bottom);
        Assert.True(crop.WouldChangeAnything);
    }

    [Fact]
    public void DraggingAHandlePastTheOppositeEdgeFlipsRatherThanInverts()
    {
        var crop = Page();
        crop.Press(200, 100, 6);
        crop.Move(500, 300);
        crop.Release();

        // Take the left edge and pull it well past the right one.
        crop.Press(200, 200, 6);
        crop.Move(800, 200);
        crop.Release();

        output.WriteLine(Rect(crop));
        // Normalised, so the rectangle is still a rectangle — never a negative
        // width that the renderer draws as nothing and Apply refuses.
        Assert.True(crop.Left < crop.Right, $"inverted: {Rect(crop)}");
        Assert.Equal(500, crop.Left);
        Assert.Equal(800, crop.Right);
    }

    /// <summary>A press with no drag behind it leaves the page uncropped.</summary>
    /// <remarks>
    /// Otherwise a stray click on the canvas leaves a zero-size frame that
    /// cannot be seen and cannot be applied, and the artist has to work out
    /// what happened.
    /// </remarks>
    [Fact]
    public void AClickWithNoDragFallsBackToTheWholePage()
    {
        var crop = Page();
        crop.Press(300, 300, 6);
        crop.Release();

        output.WriteLine(Rect(crop));
        Assert.False(crop.WouldChangeAnything);
        Assert.Equal(960, crop.Right);
    }

    // ---- the paper is the limit -----------------------------------------------------

    [Fact]
    public void AHandleDraggedOffThePaperStopsAtTheEdge()
    {
        var crop = Page();
        crop.Press(0, 0, 6);            // the top-left handle
        crop.Move(-400, -300);          // drag it well off the page
        crop.Release();

        output.WriteLine(Rect(crop));
        Assert.Equal(0, crop.Left);
        Assert.Equal(0, crop.Top);
    }

    /// <summary>
    /// Moving the whole frame against the paper's edge stops it, and does not
    /// squash it.
    /// </summary>
    /// <remarks>
    /// The easy implementation shifts each edge on its own and clamps both,
    /// which pins the leading edge while the trailing one keeps coming — so the
    /// rectangle shrinks against the margin instead of stopping. It looks
    /// almost right, which is what makes it worth a test.
    /// </remarks>
    [Fact]
    public void MovingTheFrameOffThePaperStopsItRatherThanSquashingIt()
    {
        var crop = Page();
        crop.Press(200, 100, 6);
        crop.Move(500, 300);
        crop.Release();
        double w = crop.Width, h = crop.Height;

        crop.Press(350, 200, 6);        // inside: a move
        crop.Move(-500, -500);          // shove it hard into the top-left
        crop.Release();

        output.WriteLine($"{Rect(crop)} — was {w} × {h}, now {crop.Width} × {crop.Height}");
        Assert.Equal(0, crop.Left);
        Assert.Equal(0, crop.Top);
        Assert.Equal(w, crop.Width);
        Assert.Equal(h, crop.Height);
    }

    /// <summary>
    /// A drag that leaves the paper and comes back lands where the hand is.
    /// </summary>
    /// <remarks>
    /// This is why a move is a delta from the press rather than from the last
    /// position: accumulating deltas banks the clamped remainder, so the
    /// rectangle comes back offset by however far off the page you went.
    /// </remarks>
    [Fact]
    public void AMoveThatLeavesThePaperAndReturnsDoesNotDrift()
    {
        var crop = Page();
        crop.Press(200, 100, 6);
        crop.Move(400, 300);
        crop.Release();

        crop.Press(300, 200, 6);
        crop.Move(-900, 200);   // far off the left edge, clamped
        crop.Move(300, 200);    // and back to exactly where the press was
        crop.Release();

        output.WriteLine(Rect(crop));
        Assert.Equal(200, crop.Left);
        Assert.Equal(100, crop.Top);
    }

    // ---- Shift ----------------------------------------------------------------------

    /// <summary>
    /// Shift holds the ratio the rectangle had when the drag started — the same
    /// thing Shift means on every other tool here.
    /// </summary>
    [Fact]
    public void ShiftHoldsTheAspectRatioOnACorner()
    {
        var crop = Page();
        crop.Press(100, 100, 6);
        crop.Move(500, 300);        // 400 × 200, so 2:1
        crop.Release();
        var ratio = crop.Width / crop.Height;
        Assert.Equal(2.0, ratio, 3);

        crop.Press(500, 300, 6);    // the bottom-right corner
        crop.Move(900, 320, constrain: true);
        crop.Release();

        output.WriteLine($"{Rect(crop)} — ratio {crop.Width / crop.Height:0.###}");
        Assert.Equal(2.0, crop.Width / crop.Height, 3);
        // The anchor is the corner that did not move.
        Assert.Equal(100, crop.Left);
        Assert.Equal(100, crop.Top);
    }

    /// <summary>Shift on an edge does nothing — there is no ratio to hold with one axis.</summary>
    [Fact]
    public void ShiftOnAnEdgeIsIgnored()
    {
        var crop = Page();
        crop.Press(100, 100, 6);
        crop.Move(500, 300);
        crop.Release();

        crop.Press(500, 200, 6);    // the right edge
        crop.Move(700, 200, constrain: true);
        crop.Release();

        output.WriteLine(Rect(crop));
        Assert.Equal(700, crop.Right);
        Assert.Equal(100, crop.Top);
        Assert.Equal(300, crop.Bottom);   // untouched: an edge drag moves one axis
    }

    // ---- what the document is told --------------------------------------------------

    /// <summary>
    /// The rounded rectangle is the one the artist could see — left and width
    /// come from the same two roundings.
    /// </summary>
    /// <remarks>
    /// Rounding the width independently is the bug that hides: a rectangle from
    /// 10.6 to 20.4 then reports left 11 and width 10, which is a right edge of
    /// 21 — a pixel further out than anything that was dragged to.
    /// </remarks>
    [Fact]
    public void TheRoundedRectangleAgreesWithItsOwnEdges()
    {
        var crop = Page();
        crop.Press(10.6, 20.4, 6);
        crop.Move(20.4, 40.6);
        crop.Release();

        output.WriteLine($"{Rect(crop)} → {crop.RoundedLeft}, {crop.RoundedTop}, "
            + $"{crop.RoundedWidth} × {crop.RoundedHeight}");
        Assert.Equal(11, crop.RoundedLeft);
        Assert.Equal(20, crop.RoundedTop);
        Assert.Equal(20 - 11, crop.RoundedWidth);
        Assert.Equal(41 - 20, crop.RoundedHeight);
    }

    /// <summary>
    /// A page that does not start at the origin — the state every crop leaves
    /// behind — is handled in document coordinates throughout.
    /// </summary>
    /// <remarks>
    /// The whole class of bug this guards against is treating the page as
    /// (0, 0, W, H): every test above passes either way, because a fresh
    /// document has its origin at the corner. Cropping once moves it, and a
    /// second crop is where the mistake would show.
    /// </remarks>
    [Fact]
    public void ASecondCropWorksInDocumentCoordinates()
    {
        var crop = new CropSession();
        crop.Begin(100, 60, 400, 300);   // the page after one crop

        Assert.False(crop.WouldChangeAnything);
        Assert.Equal(100, crop.Left);
        Assert.Equal(500, crop.Right);
        Assert.Equal(360, crop.Bottom);

        // The top-left handle is at (100, 60), not at the origin.
        Assert.Equal(CropHandle.TopLeft, crop.HandleAt(102, 62, 6));
        Assert.Equal(CropHandle.Inside, crop.HandleAt(300, 200, 6));

        // And the clamp is to this page, not to a page at the origin.
        crop.Press(100, 60, 6);
        crop.Move(0, 0);
        crop.Release();
        output.WriteLine(Rect(crop));
        Assert.Equal(100, crop.Left);
        Assert.Equal(60, crop.Top);
    }
}
