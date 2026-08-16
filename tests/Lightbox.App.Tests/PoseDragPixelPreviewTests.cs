using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Q81 decision 5, the pixel half: during a pose drag the bound strokes
/// re-render through the provisional pose per pointer event, exactly —
/// through the same cache miss path the committed pose uses — and strokes
/// badged expensive ghost as their posed centreline, landing exactly on
/// release. The record never changes until the release (invariant 1).
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because the end-to-end test sets
/// brush parameters, and those live in a process-wide store: running beside
/// a test that assumes defaults hands it this one's brush.
/// </remarks>
[Collection("BrushState")]
public class PoseDragPixelPreviewTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 200, H = 200;

    private static Doc RiggedDoc()
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Armature = new Armature
        {
            Bones = [new Bone { Id = "b", Name = "arm", X = 100, Y = 100, Length = 60 }],
        };
        return doc;
    }

    private static Frame BoundFrame(BrushSettings? brush = null)
    {
        var stroke = new Stroke
        {
            Color = "#a03020",
            Points = [new StrokePoint(105, 100, 1), new StrokePoint(150, 100, 1), new StrokePoint(155, 105, 1)],
            Brush = brush ?? new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
        };
        Skinning.AssignAll(stroke, "b");
        return new Frame { Strokes = [stroke] };
    }

    private static bool Identical(SKBitmap a, SKBitmap b)
    {
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
            if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
        return true;
    }

    [Fact]
    public void ThePreviewPoseRendersTheReleasePixels()
    {
        // The drag's pixels come from PoseFrameForRender with a pose override;
        // the release's come from the same call reading the track it just
        // wrote. Same construction, same bytes.
        var doc = RiggedDoc();
        var provisional = new Dictionary<string, BonePose> { ["b"] = new() { RotationDeg = 45 } };

        var previewFrame = BoundFrame();
        using var previewCache = new FrameBitmapCache { Rig = RigIndex.For(doc) };
        previewCache.PoseResolver = (f, cel) =>
            Skinning.PoseFrameForRender(doc, f, cel, previewCache.Rig, provisional);
        var previewBmp = previewCache.Get(previewFrame, W, H, celIndex: 0);

        doc.Scene.PoseTrack = new PoseTrack
        {
            Keys = [new PoseKey { Frame = 0, Bones = { ["b"] = new BonePose { RotationDeg = 45 } } }],
        };
        var committedFrame = BoundFrame();
        using var committedCache = new FrameBitmapCache { Rig = RigIndex.For(doc) };
        committedCache.PoseResolver = (f, cel) =>
            Skinning.PoseFrameForRender(doc, f, cel, committedCache.Rig);
        var committedBmp = committedCache.Get(committedFrame, W, H, celIndex: 0);

        Assert.True(Identical(previewBmp, committedBmp),
            "the drag showed different pixels than the release landed");
    }

    [Fact]
    public void AnExpensiveBrushGhostsDuringTheDragAndTheRecordNeverLearns()
    {
        var doc = RiggedDoc();
        var watercolour = new BrushSettings
        {
            Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2,
            Medium = new MediumSettings { Kind = MediumKind.Watercolour },
        };
        Assert.Equal(BrushCost.Expressive, BrushCostOf.Settings(watercolour));
        var frame = BoundFrame(watercolour);
        var provisional = new Dictionary<string, BonePose> { ["b"] = new() { RotationDeg = 30 } };

        var ghosted = Skinning.PoseFrameForRender(
            doc, frame, 0, RigIndex.For(doc), provisional, ghostOverBudget: true);
        var exact = Skinning.PoseFrameForRender(
            doc, frame, 0, RigIndex.For(doc), provisional);

        var ghost = ghosted.Strokes[0].Brush;
        output.WriteLine(
            $"ghost: kind {ghost.Kind}, medium {ghost.Medium.Kind}, size {ghost.Size}, opacity {ghost.Opacity}; "
            + $"exact medium {exact.Strokes[0].Brush.Medium.Kind}");

        // The ghost is a fast centreline…
        Assert.Equal(BrushCost.Fast, BrushCostOf.Settings(ghost));
        Assert.Equal(MediumKind.None, ghost.Medium.Kind);
        Assert.True(ghost.Size <= 2.5, $"ghost is not a centreline: size {ghost.Size}");
        // …the exact render still carries the medium…
        Assert.Equal(MediumKind.Watercolour, exact.Strokes[0].Brush.Medium.Kind);
        // …and the record's own brush was never touched by either.
        Assert.Equal(MediumKind.Watercolour, frame.Strokes[0].Brush.Medium.Kind);
        Assert.Equal(20, frame.Strokes[0].Brush.Size);
    }

    [AvaloniaFact]
    public void APoseDragMovesThePublishedPixelsBeforeRelease()
    {
        // End to end through the view model: the drag alone must move ink in
        // the published snapshot — the defect was pixels frozen until pen-up.
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Rig", W, H, 12, 72, "#ffffff", false));
        vm.ColorHex = "#000000";
        vm.BrushSize = 10;
        vm.BeginStroke(105, 100, 1);
        vm.MoveStroke(155, 100, 1);
        vm.EndStroke();

        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 160, 100);
        var boneId = vm.Doc.Armature!.Bones[0].Id;
        Skinning.AssignAll(vm.PaintedCel().Strokes[0], boneId);
        vm.RebuildRigIndex();
        vm.PosingMode = true;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        using var before = SKBitmap.FromImage(latest!.Image);

        // Drag the tip straight down: the stroke should swing off the row it
        // was painted on, before any release.
        vm.PreviewBoneGesture(boneId, BoneGrab.Tip, 160, 100, 100, 170, extruding: false);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the coalesced publish

        using var during = SKBitmap.FromImage(latest!.Image);
        var moved = !Identical(before, during);
        output.WriteLine($"pixels moved during the drag: {moved}");
        Assert.True(moved, "the published pixels did not move until release");
        Assert.Null(vm.Doc.Scene.PoseTrack); // and the record still has no key

        // A cleared preview puts the committed pixels back.
        vm.ClearBoneGesturePreview();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var after = SKBitmap.FromImage(latest!.Image);
        Assert.True(Identical(before, after), "cancelling the drag left preview pixels behind");
    }
}
