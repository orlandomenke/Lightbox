using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Q144: an anchor's direction — the stalk that authors it, the writes that
/// carry it, and the pixels that show it.
/// </summary>
/// <remarks>
/// The model half (round trip, no-key-when-null, retiming) is in
/// <c>AnchorTests</c>; the sidecar half in <c>SpriteSheetExportTests</c>. What
/// these guard is the gesture path: a direction an artist can neither see nor
/// grab is B58's mistake repeated with one more field.
/// </remarks>
[Collection("BrushState")]
public class AnchorDirectionTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static RigMark Aimed(
        string id, double x, double y, double? angle, bool selected = false) =>
        new(id, RigMarkKind.Anchor, id, x, y, 0, 0, selected, angle);

    // ---- the pure geometry ---------------------------------------------------------

    [Fact]
    public void TheSelectedAnchorsStalkTipGrabsRotation()
    {
        var marks = new[] { Aimed("a", 50, 50, 0, selected: true) };

        var hit = RigOverlay.Hit(marks, 50 + RigOverlay.StalkLength, 50, scale: 1);

        Assert.Equal("a", hit.Id);
        Assert.Equal(RigCorner.Stalk, hit.Corner);
    }

    [Fact]
    public void AnUnselectedAnchorOffersNoStalk()
    {
        // The tip only exists on the anchor being worked on — otherwise every
        // aimed socket sprouts a grab target over the drawing.
        var marks = new[] { Aimed("a", 50, 50, 0) };

        var hit = RigOverlay.Hit(marks, 50 + RigOverlay.StalkLength, 50, scale: 1);

        Assert.False(hit.Any);
    }

    [Fact]
    public void AnUnaimedSelectedAnchorStillOffersTheGhostStub()
    {
        // Grabbing the stub IS how a direction is given; an affordance that
        // only exists once you have used it is not one.
        var marks = new[] { Aimed("a", 50, 50, null, selected: true) };

        var hit = RigOverlay.Hit(marks, 50 + RigOverlay.StalkLength, 50, scale: 1);

        Assert.Equal(RigCorner.Stalk, hit.Corner);
    }

    [Fact]
    public void DraggingTheStalkAimsTheAnchorAtThePointer()
    {
        // Tip starts on +X; dragged to straight below the anchor. Degrees,
        // y-down, so straight down is +90.
        var mark = Aimed("a", 50, 50, 0, selected: true);

        var moved = RigOverlay.Drag(mark, RigCorner.Stalk, -RigOverlay.StalkLength, 36);

        output.WriteLine($"angle {moved.AngleDeg}");
        Assert.Equal(90, moved.AngleDeg!.Value, 6);
        // Rotation never moves the anchor — the two gestures must not blur.
        Assert.Equal(50, moved.X);
        Assert.Equal(50, moved.Y);
    }

    [Fact]
    public void DraggingTheTipOntoTheAnchorKeepsTheOldAim()
    {
        // The pointer exactly on the anchor says nothing about direction, and
        // snapping to atan2's zero would un-aim the socket on a slip.
        var mark = Aimed("a", 50, 50, 45, selected: true);
        var (tx, ty) = RigOverlay.StalkTipOf(mark);

        var moved = RigOverlay.Drag(mark, RigCorner.Stalk, 50 - tx, 50 - ty);

        Assert.Equal(45, moved.AngleDeg);
    }

    [Fact]
    public void AnotherAnchorsCrossBeatsTheGhostStub()
    {
        // Refuted before it shipped: the selected anchor's stub tip sits a
        // stalk-length along +X, and two sockets that far apart is an
        // ordinary rig — a wrist beside a hand. A press meant for the other
        // anchor's cross must pick it up, not rotate the selected one.
        var marks = new[]
        {
            Aimed("a", 0, 0, null, selected: true),
            Aimed("b", RigOverlay.StalkLength, 3, null),
        };

        var hit = RigOverlay.Hit(marks, RigOverlay.StalkLength, 3, scale: 1);

        Assert.Equal("b", hit.Id);
        Assert.Equal(RigCorner.None, hit.Corner);
    }

    [Fact]
    public void AMoveDragCarriesTheAimAlong()
    {
        var mark = Aimed("a", 50, 50, 30, selected: true);

        var moved = RigOverlay.Drag(mark, RigCorner.None, 5, -3);

        Assert.Equal((55, 47, 30d), (moved.X, moved.Y, moved.AngleDeg!.Value));
    }

    // ---- through the view model to the drawing --------------------------------------

    private static MainViewModel OnOnes(int frames = 4)
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Rig", 200, 160, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < frames) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;
        vm.RigEditMode = true;
        return vm;
    }

    private static AnchorPoint AnchorAt(MainViewModel vm, string id) =>
        Anchors.ResolvedAt(vm.Doc.Scene, vm.CurrentFrameIndex)[id];

    [AvaloniaFact]
    public void TheStalkDragWritesTheAngleToTheDrawing()
    {
        var vm = OnOnes();
        var id = vm.AddAnchorAt("hand", 40, 40);
        vm.SelectedRigMarkId = id;

        // The canvas reports the whole drag on release; straight down from the
        // ghost stub's tip.
        vm.DragRig(id, RigCorner.Stalk, -RigOverlay.StalkLength, 20);

        var point = AnchorAt(vm, id);
        Assert.Equal(90, point.AngleDeg!.Value, 6);
        Assert.Equal((40d, 40d), (point.X, point.Y));
    }

    [AvaloniaFact]
    public void MovingAnAimedSocketKeepsItsAim()
    {
        // The write path re-records the whole point, so a move that dropped
        // the angle would silently un-aim the socket — the exact regression
        // this exists to catch.
        var vm = OnOnes();
        var id = vm.AddAnchorAt("hand", 40, 40);
        vm.SelectedRigMarkId = id;
        vm.DragRig(id, RigCorner.Stalk, -RigOverlay.StalkLength, 20);

        vm.DragRig(id, RigCorner.None, 10, 5);

        var point = AnchorAt(vm, id);
        Assert.Equal((50d, 45d), (point.X, point.Y));
        Assert.Equal(90, point.AngleDeg!.Value, 6);
    }

    [AvaloniaFact]
    public void PushingAcrossCarriesTheAimToEveryDrawing()
    {
        var vm = OnOnes(4);
        var id = vm.AddAnchorAt("hand", 40, 40);
        vm.SelectedRigMarkId = id;
        vm.DragRig(id, RigCorner.Stalk, -RigOverlay.StalkLength, 20);

        Assert.Equal(4, vm.PushRigAcross(0, 4));

        for (var i = 0; i < 4; i++)
        {
            var point = Anchors.ResolvedAt(vm.Doc.Scene, i)[id];
            Assert.Equal(90, point.AngleDeg!.Value, 6);
        }
    }

    [AvaloniaFact]
    public void AGroupMoveKeepsEveryAim()
    {
        // The second move path beside WriteRig — the selection tool's group
        // drag — rebuilt the point from X and Y and dropped the angle on both
        // apply and undo. Found by an adversarial pass, so it gets its test.
        var vm = OnOnes();
        var id = vm.AddAnchorAt("hand", 40, 40);
        vm.SelectedRigMarkId = id;
        vm.DragRig(id, RigCorner.Stalk, -RigOverlay.StalkLength, 20);
        vm.Selection.SelectAnchor(id);

        vm.BeginAnchorsMove();
        vm.UpdateAnchorsMove(5, 5);
        vm.EndAnchorsMove();

        var moved = AnchorAt(vm, id);
        Assert.Equal((45d, 45d), (moved.X, moved.Y));
        Assert.Equal(90, moved.AngleDeg!.Value, 6);

        // And the undo lambda is a second reconstruction, not the same one.
        vm.UndoCommand.Execute(null);
        var back = AnchorAt(vm, id);
        Assert.Equal((40d, 40d), (back.X, back.Y));
        Assert.Equal(90, back.AngleDeg!.Value, 6);
    }

    [AvaloniaFact]
    public void ClearDirectionTakesTheAngleAndOnlyTheAngle()
    {
        var vm = OnOnes();
        var id = vm.AddAnchorAt("hand", 40, 40);
        vm.SelectedRigMarkId = id;
        vm.DragRig(id, RigCorner.Stalk, -RigOverlay.StalkLength, 20);

        vm.ClearSelectedAnchorDirection();

        var point = AnchorAt(vm, id);
        Assert.Null(point.AngleDeg);
        Assert.Equal((40d, 40d), (point.X, point.Y));
    }

    // ---- the pixels -----------------------------------------------------------------

    /// <summary>Alpha at a document point after painting these marks.</summary>
    private static byte AlphaAt(IReadOnlyList<RigMark> marks, int x, int y)
    {
        var bmp = new SKBitmap(200, 160, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            RigOverlayPainter.Paint(canvas, marks, scale: 1);
            canvas.Flush();
        }
        return bmp.GetPixel(x, y).Alpha;
    }

    [Fact]
    public void AnAimedAnchorShowsItsStalkWithoutBeingSelected()
    {
        // A direction nobody can see without selecting is not a direction.
        // Sampled halfway along the stalk, well past the cross's arms.
        var marks = new[] { Aimed("a", 50, 50, 0) };

        var half = (int)(50 + RigOverlay.StalkLength / 2);
        var on = AlphaAt(marks, half, 50);
        var off = AlphaAt(new[] { Aimed("a", 50, 50, null) }, half, 50);

        Assert.True(on > 8, $"stalk missing: alpha {on}");
        Assert.Equal(0, off);
    }
}
