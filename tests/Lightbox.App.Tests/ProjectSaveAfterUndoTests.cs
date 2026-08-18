using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Tests;

/// <summary>
/// B257 — a project save wrote the document the artist had before their last
/// undo, and said it had saved. Reported as rig work vanishing over a restart:
/// *"I have now on multiple occasions realigned the bones to the drawing,
/// weight painted them. Then I close and reopen the application and both the
/// bone positions and the weights are gone."*
/// </summary>
/// <remarks>
/// Nothing about it is bone-specific. Undo and redo <b>swap</b> the editor's
/// document object (<c>SnapshotStep</c>, which is what makes redo exact), and
/// the project kept the reference it loaded — the one its save writes. One
/// undo split them, and every edit after it landed on a document nothing would
/// ever write. It surfaced on rigging because rigging is a long run of
/// snapshot steps with a lot of undoing, and because a displaced skeleton is
/// obvious in a way a missing minute of drawing is not.
/// </remarks>
public class ProjectSaveAfterUndoTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lb-b257-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private (MainViewModel Vm, Project Project, DocumentRef Reference) Opened()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewProject(_root, "Knight");
        var project = vm.ProjectDocker.Project!;
        var reference = ProjectIo.AddDocument(project, "Walk", vm.Doc);
        vm.OpenProjectDocument(reference, vm.Doc);
        return (vm, project, reference);
    }

    private Doc? OffDisk(DocumentRef reference)
    {
        var reloaded = ProjectIo.Load(_root);
        return ProjectIo.LoadDocument(
            reloaded, reloaded.Manifest.Documents.First(d => d.Id == reference.Id));
    }

    [AvaloniaFact]
    public void WorkAfterAnUndoReachesTheFile()
    {
        var (vm, project, reference) = Opened();

        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);

        // The undo is the whole bug: one, without a redo, and the artist
        // carries on working. Everything from here landed on an object the
        // project had already stopped pointing at.
        vm.UndoCommand.Execute(null);
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var bone = vm.SelectedBoneId!;
        vm.DragBoneBind(bone, BoneGrab.Origin, 105, 152);

        Assert.Same(vm.Doc, project.Loaded[reference.Id]);

        vm.SaveProject();

        var back = OffDisk(reference);
        output.WriteLine($"on disk: bones={back?.Armature?.Bones.Count}, origin={back?.Armature?.Bones.FirstOrDefault()?.X}");
        Assert.Equal(105, back!.Armature!.Bones[0].X, 3);
    }

    [AvaloniaFact]
    public void TheWeightsPaintedAfterAnUndoReachTheFileToo()
    {
        var (vm, _, reference) = Opened();

        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);
        ExposureSheet.ExposedFrame(layer, 0)!.Strokes.Add(
            new Stroke { Points = [new StrokePoint(110, 150, 1), new StrokePoint(150, 150, 1)] });

        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        vm.UndoCommand.Execute(null);
        vm.CreateBoneFromDrag(100, 150, 160, 150);

        vm.ArmWeightBrush(WeightBrushMode.Add);
        vm.BeginWeightStroke(120, 150, 1);
        vm.WeightDab(130, 150, 1);
        vm.EndWeightStroke();

        vm.SaveProject();

        var back = OffDisk(reference);
        var stroke = ExposureSheet.ExposedFrame(
            back!.Scene.Layers.First(l => !l.IsBackground), 0)!.Strokes[0];
        output.WriteLine($"on disk: weights={stroke.Weights?.Count}");
        Assert.NotNull(stroke.Weights);
        Assert.NotEmpty(stroke.Weights!);
    }

    [AvaloniaFact]
    public void ARedoLandsOnTheFileAsWell()
    {
        // The other half of the swap: redo swaps back, and a cache re-pointed
        // on only one of the two would be right by accident half the time.
        var (vm, project, reference) = Opened();

        vm.SelectToolCommand.Execute(ToolId.Bone);
        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        vm.UndoCommand.Execute(null);
        vm.RedoCommand.Execute(null);

        Assert.Same(vm.Doc, project.Loaded[reference.Id]);
        vm.SaveProject();

        Assert.Single(OffDisk(reference)!.Armature!.Bones);
    }

    [AvaloniaFact]
    public void ADrawingMadeAfterAnUndoReachesTheFile()
    {
        // Stated separately because the bug was never about bones: any edit
        // after an undo was being dropped, and lines are what an artist would
        // have lost next.
        var (vm, _, reference) = Opened();

        var layer = vm.Doc.Scene.Layers.First(l => !l.IsBackground);
        ExposureSheet.ExposedFrame(layer, 0)!.Strokes.Add(
            new Stroke { Points = [new StrokePoint(10, 10, 1), new StrokePoint(20, 20, 1)] });
        vm.CreateLayerFolderCommand.Execute(null);
        vm.UndoCommand.Execute(null);

        ExposureSheet.ExposedFrame(vm.Doc.Scene.Layers.First(l => !l.IsBackground), 0)!.Strokes.Add(
            new Stroke { Points = [new StrokePoint(30, 30, 1), new StrokePoint(40, 40, 1)] });
        vm.CreateLayerFolderCommand.Execute(null);

        vm.SaveProject();

        var back = OffDisk(reference);
        var strokes = ExposureSheet.ExposedFrame(
            back!.Scene.Layers.First(l => !l.IsBackground), 0)!.Strokes;
        Assert.Equal(2, strokes.Count);
    }
}
