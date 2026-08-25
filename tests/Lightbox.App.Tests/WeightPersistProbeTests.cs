using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

public class WeightPersistProbeTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "lb-wp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static Layer Anim(MainViewModel vm) => vm.Doc.Scene.Layers.First(l => !l.IsBackground);

    [AvaloniaFact]
    public void TheOwnersExactSequence()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Rig", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.FrameCount < 4) vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 0;

        // A drawing: a body line and a leg line.
        var frame0 = ExposureSheet.ExposedFrame(Anim(vm), 0)!;
        frame0.Strokes.Add(new Stroke { Points = [new StrokePoint(100, 100, 1), new StrokePoint(100, 150, 1)] });
        frame0.Strokes.Add(new Stroke { Points = [new StrokePoint(100, 150, 1), new StrokePoint(100, 200, 1)] });

        // 1. Position the armature to the drawing in bind mode.
        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 100, 100, 150);   // body
        var body = vm.SelectedBoneId!;
        vm.ExtrudeChildFrom(body, 100, 200);          // leg
        var leg = vm.SelectedBoneId!;

        // 2. Attach the layer to the whole skeleton.
        vm.RigLayerToSkeletonCommand.Execute(null);
        output.WriteLine($"layer BoneId={Anim(vm).BoneId ?? "(null)"} rigged={vm.Doc.Scene.IsLayerRigged(Anim(vm))}");

        // 3. Weight paint the drawing.
        vm.SelectedBoneId = leg;
        vm.ArmWeightBrush(WeightBrushMode.Add);
        vm.BeginWeightStroke(100, 175, 1);
        vm.WeightDab(100, 190, 1);
        vm.EndWeightStroke();
        var painted = ExposureSheet.ExposedFrame(Anim(vm), 0)!;
        output.WriteLine($"after paint: s0 weights={painted.Strokes[0].Weights?.Count}, s1 weights={painted.Strokes[1].Weights?.Count}");

        // 4. Frame 2: reposition a leg bone, then insert a drawing from pose.
        vm.CurrentFrameIndex = 1;
        vm.PosingMode = true;
        vm.PoseBoneTo(leg, 140, 190);
        vm.InsertDrawingFromPose();
        output.WriteLine($"frame1 own drawing={Anim(vm).Cels[1].Frame is not null}");

        var live = ExposureSheet.ExposedFrame(Anim(vm), 0)!;
        output.WriteLine($"before save: f0 s0={live.Strokes[0].Weights?.Count}, s1={live.Strokes[1].Weights?.Count}, layerBone={Anim(vm).BoneId ?? "(null)"}");

        // 5. Save to a real file and read it back.
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "walk.lightbox.json");
        File.WriteAllText(path, vm.SerializeDocument());
        var json = File.ReadAllText(path);
        output.WriteLine($"json: boneId key={json.Contains("\"boneId\"")}, poseTrack key={json.Contains("poseTrack")}, weights key={json.Contains("\"weights\"")}");
        var trackBefore = vm.Doc.Scene.PoseTrack;
        output.WriteLine($"before save: poseTrack keys={trackBefore?.Keys.Count}, frames=[{string.Join(",", trackBefore?.Keys.Select(k => k.Frame) ?? [])}]");
        var back = DocJson.Load(path);
        output.WriteLine($"after load: poseTrack keys={back.Scene.PoseTrack?.Keys.Count}, frames=[{string.Join(",", back.Scene.PoseTrack?.Keys.Select(k => k.Frame) ?? [])}]");

        var backLayer = back.Scene.Layers.First(l => !l.IsBackground);
        var backFrame = ExposureSheet.ExposedFrame(backLayer, 0)!;
        output.WriteLine($"after load: f0 s0={backFrame.Strokes[0].Weights?.Count}, s1={backFrame.Strokes[1].Weights?.Count}, layerBone={backLayer.BoneId ?? "(null)"}, bones={back.Armature?.Bones.Count}");

        // Now the half a DocJson round trip cannot see: open it in the app the
        // way reopening the file does, and ask what the artist would be looking at.
        var fresh = new MainViewModel(artist: null) { SmoothStrokes = false };
        fresh.OpenDocumentTab(back, path);
        fresh.SelectToolCommand.Execute(ToolId.Bone);
        fresh.ArmatureEditMode = true;
        fresh.CurrentFrameIndex = 0;
        fresh.SelectedBoneId = fresh.Doc.Armature!.Bones[1].Id;
        fresh.WeightPainting = true;
        var heat = fresh.HeatPoints;
        output.WriteLine($"reopened: heat points={heat.Count}, weights=[{string.Join(",", heat.Select(h => h.Weight.ToString("F2")))}]");
        output.WriteLine($"reopened: rigged={fresh.Doc.Scene.IsLayerRigged(fresh.Doc.Scene.Layers.First(l => !l.IsBackground))}");

        Assert.Equal("", backLayer.BoneId);
        Assert.NotNull(backFrame.Strokes[1].Weights);
        Assert.Contains(heat, h => h.Weight > 0);
    }
}
