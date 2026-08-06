using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;

namespace Lightbox.App.Tests;

/// <summary>
/// Rig marks following the pointer while a drag is in flight (B112).
/// </summary>
/// <remarks>
/// <para>
/// Anchors and collision boxes never moved on screen until the pointer was
/// released — not in the group path, and not in the single-mark one either.
/// <c>DragRig</c>'s own remark had already described the intended arrangement,
/// "live feedback during the drag is the overlay's own business and touches
/// nothing", and the overlay had simply never been given the other half: it
/// painted straight from the document, and the document does not change until
/// the release.
/// </para>
/// <para>
/// So the fix is chrome rather than a second write path — the same bargain
/// <c>DraftGuide</c> makes. These read what the overlay would draw rather than
/// driving synthetic pointer input, because a dropped click through Xvfb looks
/// exactly like a bug (see <c>MANUAL_TESTING.md</c>).
/// </para>
/// </remarks>
public class RigPreviewTests
{
    private static RigMark Anchor(string id, double x, double y) =>
        new(id, RigMarkKind.Anchor, id, x, y, 0, 0, false);

    private static RigMark Box(string id, double x, double y, double w, double h) =>
        new(id, RigMarkKind.Shape, id, x, y, w, h, false);

    private static RigMark Of(IReadOnlyList<RigMark> marks, string id) =>
        marks.First(m => m.Id == id);

    // ---- the group drag, which is what B112 was filed for --------------------------

    [AvaloniaFact]
    public void AnchorsFollowThePointerWhileTheGroupIsBeingDragged()
    {
        var canvas = new CanvasControl
        {
            RigMarks = [Anchor("hand", 40, 40), Anchor("foot", 90, 120), Anchor("hip", 10, 10)],
        };

        canvas.PreviewRig(["hand", "foot"], RigCorner.None, 12, -5);

        var shown = canvas.WithRigPreview(canvas.RigMarks)!;
        Assert.Equal(52, Of(shown, "hand").X);
        Assert.Equal(35, Of(shown, "hand").Y);
        Assert.Equal(102, Of(shown, "foot").X);
        Assert.Equal(115, Of(shown, "foot").Y);

        // The one that was not selected stays exactly where the record has it.
        Assert.Equal(10, Of(shown, "hip").X);
        Assert.Equal(10, Of(shown, "hip").Y);
    }

    [AvaloniaFact]
    public void CollisionBoxesFollowThePointerWhileTheGroupIsBeingDragged()
    {
        var canvas = new CanvasControl
        {
            RigMarks = [Box("body", 20, 20, 40, 60), Box("head", 80, 10, 30, 30)],
        };

        canvas.PreviewRig(["body", "head"], RigCorner.None, -8, 14);

        var shown = canvas.WithRigPreview(canvas.RigMarks)!;
        Assert.Equal(12, Of(shown, "body").X);
        Assert.Equal(34, Of(shown, "body").Y);
        Assert.Equal(72, Of(shown, "head").X);

        // A move is two numbers. A preview that resized while translating would
        // show the artist a hurtbox the commit is not going to write.
        Assert.Equal(40, Of(shown, "body").W);
        Assert.Equal(60, Of(shown, "body").H);
        Assert.Equal(30, Of(shown, "head").W);
    }

    // ---- chrome, not data ---------------------------------------------------------

    [AvaloniaFact]
    public void ThePreviewLeavesTheMarksItWasGivenAlone()
    {
        var canvas = new CanvasControl { RigMarks = [Anchor("hand", 40, 40)] };

        canvas.PreviewRig(["hand"], RigCorner.None, 100, 100);
        _ = canvas.WithRigPreview(canvas.RigMarks);

        // The source list is what the document said, and a preview that edited it
        // would be a second write path wearing a different name.
        Assert.Equal(40, canvas.RigMarks![0].X);
        Assert.Equal(40, canvas.RigMarks[0].Y);
    }

    [AvaloniaFact]
    public void ClearingThePreviewGoesBackToWhatTheRecordSays()
    {
        var canvas = new CanvasControl { RigMarks = [Anchor("hand", 40, 40)] };

        canvas.PreviewRig(["hand"], RigCorner.None, 25, 25);
        Assert.Equal(65, Of(canvas.WithRigPreview(canvas.RigMarks)!, "hand").X);

        canvas.ClearRigPreview();

        // Cleared on release before the commit fires, so the next paint reads the
        // record. Clearing afterwards would double the move for one frame.
        Assert.Equal(40, Of(canvas.WithRigPreview(canvas.RigMarks)!, "hand").X);
    }

    [AvaloniaFact]
    public void WithNoDragInFlightTheListIsHandedBackUntouched()
    {
        var marks = new[] { Anchor("hand", 40, 40) };
        var canvas = new CanvasControl { RigMarks = marks };

        // Same instance, not a copy: the overlay must pay nothing at all for a
        // preview that does not exist, and this is a per-paint path.
        Assert.Same(marks, canvas.WithRigPreview(canvas.RigMarks));
    }

    [AvaloniaFact]
    public void AnEmptyOrAbsentOverlayPreviewsNothing()
    {
        var canvas = new CanvasControl { RigMarks = null };
        canvas.PreviewRig(["hand"], RigCorner.None, 10, 10);
        Assert.Null(canvas.WithRigPreview(canvas.RigMarks));

        canvas.RigMarks = [];
        Assert.Empty(canvas.WithRigPreview(canvas.RigMarks)!);
    }

    // ---- the single-mark drag, which had the same gap ------------------------------

    [AvaloniaFact]
    public void ADraggedCornerPreviewsAResizeRatherThanAMove()
    {
        var canvas = new CanvasControl { RigMarks = [Box("body", 20, 20, 40, 60)] };

        canvas.PreviewRig(["body"], RigCorner.BottomRight, 10, 10);

        // Through RigOverlay.Drag, so the preview is the arithmetic the commit will
        // use rather than a second guess at it: a corner resizes, None translates.
        var shown = Of(canvas.WithRigPreview(canvas.RigMarks)!, "body");
        Assert.Equal(20, shown.X);
        Assert.Equal(20, shown.Y);
        Assert.Equal(50, shown.W);
        Assert.Equal(70, shown.H);
    }

    // ---- the arithmetic the pointer handler feeds it -------------------------------

    [AvaloniaFact]
    public void AGroupPreviewIsMeasuredFromWhereTheDragWasPickedUp()
    {
        var canvas = new CanvasControl { RigMarks = [Anchor("hand", 40, 40)] };
        var selection = new ViewModels.SelectionManager();
        selection.SelectAnchor("hand");
        canvas.SetSelectionManager(selection);

        canvas.BeginRigGroupPreview(10, 10, shapes: false);

        // Three events, as a real drag arrives. Absolute from the press, so the
        // total is the last position minus the first — not a sum of what the
        // handler reported, which is the arithmetic B109 got wrong.
        canvas.TrackRigGroupPreview(15, 12, shapes: false);
        canvas.TrackRigGroupPreview(20, 14, shapes: false);
        canvas.TrackRigGroupPreview(25, 18, shapes: false);

        var shown = Of(canvas.WithRigPreview(canvas.RigMarks)!, "hand");
        Assert.Equal(55, shown.X);   // 40 + (25 - 10)
        Assert.Equal(48, shown.Y);   // 40 + (18 - 10)
    }

    [AvaloniaFact]
    public void AnchorsAndBoxesMeasureFromTheirOwnPress()
    {
        var canvas = new CanvasControl
        {
            RigMarks = [Anchor("hand", 40, 40), Box("body", 20, 20, 40, 60)],
        };
        var selection = new ViewModels.SelectionManager();
        canvas.SetSelectionManager(selection);

        // One gesture then the other, which is the only way it happens: selecting a
        // shape clears the anchors, so the two kinds are never dragged together.
        // Each keeps its own start, and a second drag that measured from the first
        // one's press would jump on its opening event.
        selection.SelectAnchor("hand");
        canvas.BeginRigGroupPreview(0, 0, shapes: false);
        canvas.TrackRigGroupPreview(5, 10, shapes: false);
        Assert.Equal(45, Of(canvas.WithRigPreview(canvas.RigMarks)!, "hand").X);
        canvas.ClearRigPreview();

        selection.SelectShape("body");
        canvas.BeginRigGroupPreview(100, 100, shapes: true);
        canvas.TrackRigGroupPreview(105, 110, shapes: true);

        var shown = canvas.WithRigPreview(canvas.RigMarks)!;
        Assert.Equal(25, Of(shown, "body").X);
        Assert.Equal(30, Of(shown, "body").Y);
        // And the anchor is back where the record has it, because it is no longer
        // in the selection the preview reads.
        Assert.Equal(40, Of(shown, "hand").X);
    }

    [AvaloniaFact]
    public void AGroupPreviewWithNoSelectionManagerDoesNothing()
    {
        // The canvas is constructed before the window wires the selection up, and a
        // pointer event in that window must not throw.
        var canvas = new CanvasControl { RigMarks = [Anchor("hand", 40, 40)] };
        canvas.TrackRigGroupPreview(50, 50, shapes: false);
        Assert.Equal(40, Of(canvas.WithRigPreview(canvas.RigMarks)!, "hand").X);
    }

    [AvaloniaFact]
    public void AnAnchorIgnoresTheCornerBecauseItHasNoSize()
    {
        var canvas = new CanvasControl { RigMarks = [Anchor("hand", 40, 40)] };

        canvas.PreviewRig(["hand"], RigCorner.TopLeft, 7, 3);

        // An anchor is a point: every corner is a move. This is the same reason the
        // overlay is shared between the two kinds rather than written twice.
        var shown = Of(canvas.WithRigPreview(canvas.RigMarks)!, "hand");
        Assert.Equal(47, shown.X);
        Assert.Equal(43, shown.Y);
        Assert.Equal(0, shown.W);
    }
}
