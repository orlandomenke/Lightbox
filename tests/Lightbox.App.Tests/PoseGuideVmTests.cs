using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The pose guide in the app (Q274): Read pose asks the model for the pose in
/// the keys either side of the playhead and keeps it on key A; every frame of
/// the gap shows the pose at that point with each part's volume; a joint
/// dragged on a key corrects it, as one undo step.
/// </summary>
[Collection("BrushState")]
public sealed class PoseGuideVmTests : BrushStateIsolated
{
    private static void Draw(MainViewModel vm, params (double X, double Y)[] points)
    {
        vm.BeginStroke(points[0].X, points[0].Y, 0.5);
        foreach (var p in points.Skip(1)) vm.MoveStroke(p.X, p.Y, 0.5);
        vm.EndStroke();
    }

    /// <summary>
    /// Key A at frame 1: a shin hanging straight down, drawn as two lines 30
    /// apart. Key B at frame 3: the same shin swung forward. An empty cel for
    /// the inbetween between them; the playhead on key A.
    /// </summary>
    private static (MainViewModel Vm, FakeArtist Artist) Keys()
    {
        var artist = new FakeArtist();
        var vm = new MainViewModel(artist) { SmoothStrokes = false, TweenEasing = Easing.Linear };
        Draw(vm, (85, 200), (85, 300));
        Draw(vm, (115, 200), (115, 300));
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, (100, 185), (200, 185));
        Draw(vm, (100, 215), (200, 215));
        // The empty middle cel stands for the inbetween the artist will draw.
        vm.PaintLayer().Cels[1].Frame!.Role = FrameRole.Inbetween;
        vm.CurrentFrameIndex = 0;
        return (vm, artist);
    }

    /// <summary>The shin's pose: knee then ankle, down in key A, forward in key B.</summary>
    private static PoseGuide Shin() => new()
    {
        Parts =
        [
            new PosePart
            {
                Name = "shin",
                Joints =
                [
                    new PoseJoint { Name = "knee", A = new(100, 200), B = new(100, 200) },
                    new PoseJoint { Name = "ankle", A = new(100, 300), B = new(200, 200) },
                ],
            },
        ],
    };

    [AvaloniaFact]
    public async Task ReadingThePoseKeepsItOnKeyAInSceneCoordinates()
    {
        var (vm, artist) = Keys();
        // The fake answers at the pictures' own corners, so the mapping back
        // to the scene can be checked: the top-left corner of the picture is the
        // top-left of the crop, which sits a margin outside the keys' ink.
        artist.PoseAnswer = r => AiResult<PoseReading>.Success(new PoseReading(new PoseGuide
        {
            Parts = [new PosePart { Name = "shin", Joints =
            [
                new PoseJoint { Name = "knee", A = new(0, 0), B = new(0, 0) },
                new PoseJoint { Name = "ankle", A = new(r.Width, r.Height), B = new(r.Width, r.Height) },
            ] }],
        }, "estimated the ankle"));

        await vm.ReadPoseCommand.ExecuteAsync(null);

        var pose = vm.PaintLayer().Cels[0].Frame!.Pose!;
        var knee = pose.Parts[0].Joints[0].A;
        var ankle = pose.Parts[0].Joints[1].A;
        Assert.InRange(knee.X, 50, 85);     // left of the leftmost ink (85), by about the margin
        Assert.InRange(knee.Y, 150, 185);
        Assert.InRange(ankle.X, 200, 240);  // right of the rightmost ink (200)
        Assert.InRange(ankle.Y, 300, 340);
        Assert.Contains("estimated the ankle", vm.AiStatus);
        Assert.NotEqual(artist.LastPoseRequest!.KeyAPng, artist.LastPoseRequest.KeyBPng);
    }

    [AvaloniaFact]
    public async Task AFailedReadingSavesNothing()
    {
        var (vm, artist) = Keys();
        artist.PoseAnswer = _ => AiResult<PoseReading>.Error("no luck", true);

        await vm.ReadPoseCommand.ExecuteAsync(null);

        Assert.Null(vm.PaintLayer().Cels[0].Frame!.Pose);
        Assert.Contains("no luck", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TheInbetweenShowsThePoseAtItsPointWithEachPartsVolume()
    {
        var (vm, _) = Keys();
        vm.PaintLayer().Cels[0].Frame!.Pose = Shin();

        vm.CurrentFrameIndex = 1;   // the empty cel halfway

        var guide = vm.PoseGuideView!;
        var expected = PoseGuides.At(Shin(), 0.5)[0].Joints[1].At;
        var ankle = guide.Joints.Single(j => j.Name == "ankle").At;
        Assert.True(GeometryOps.Dist(ankle, expected) < 0.5, $"ankle at ({ankle.X:0.0}, {ankle.Y:0.0})");
        var shin = Assert.Single(guide.Bones);
        Assert.InRange(shin.Width, 25, 35);   // 30 in both keys
        Assert.False(guide.Editable);         // joints are dragged on the keys
    }

    [AvaloniaFact]
    public void OnAKeyTheGuideIsThatKeysPoseAndCanBeDragged()
    {
        var (vm, _) = Keys();
        vm.PaintLayer().Cels[0].Frame!.Pose = Shin();
        vm.CurrentFrameIndex = 0;

        Assert.True(vm.PoseGuideView!.Editable);
        Assert.True(vm.PosePress(100, 300, 6));      // the ankle in key A
        vm.PoseDrag(120, 310);
        vm.PoseRelease(120, 310);

        var ankle = vm.PaintLayer().Cels[0].Frame!.Pose!.Parts[0].Joints[1];
        Assert.Equal(new PosePoint(120, 310), ankle.A);
        Assert.Equal(new PosePoint(200, 200), ankle.B);   // key B's place untouched

        vm.UndoCommand.Execute(null);
        Assert.Equal(new PosePoint(100, 300), vm.PaintLayer().Cels[0].Frame!.Pose!.Parts[0].Joints[1].A);
    }

    [AvaloniaFact]
    public void OnKeyBADragMovesKeyBsPlace()
    {
        var (vm, _) = Keys();
        vm.PaintLayer().Cels[0].Frame!.Pose = Shin();
        vm.CurrentFrameIndex = 2;

        Assert.True(vm.PosePress(200, 200, 6));
        vm.PoseRelease(210, 190);

        Assert.Equal(new PosePoint(210, 190), vm.PaintLayer().Cels[0].Frame!.Pose!.Parts[0].Joints[1].B);
    }

    [AvaloniaFact]
    public void HidingTheGuideClearsTheOverlay()
    {
        var (vm, _) = Keys();
        vm.PaintLayer().Cels[0].Frame!.Pose = Shin();
        vm.CurrentFrameIndex = 1;
        Assert.NotNull(vm.PoseGuideView);

        vm.PoseGuideVisible = false;

        Assert.Null(vm.PoseGuideView);
    }

    [AvaloniaFact]
    public async Task ALockedLayerIsNotRead()
    {
        var (vm, artist) = Keys();
        vm.PaintLayer().Locked = true;
        artist.PoseAnswer = _ => throw new InvalidOperationException("must not be asked");

        await vm.ReadPoseCommand.ExecuteAsync(null);

        Assert.Contains("locked", vm.AiStatus);
        Assert.Null(artist.LastPoseRequest);
    }
}
