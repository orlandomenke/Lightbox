using Avalonia.Headless.XUnit;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The Scene panel's seams: a depth authored through a row is one undo step,
/// zero never reaches the file, and the docker is registered like any other.
/// </summary>
[Collection("BrushState")]
public class ScenePanelTests : BrushStateIsolated
{
    private static LayerDepthRow PaintRow(MainViewModel vm) =>
        vm.DepthRows.First(r => !r.IsBackground);

    [AvaloniaFact]
    public void ADepthAuthoredThroughTheRowIsOneUndoStep()
    {
        var vm = VmLayers.BareVm();
        var layer = vm.PaintLayer();

        PaintRow(vm).Depth = 800;
        Assert.Equal(800, layer.Depth);

        vm.UndoCommand.Execute(null);
        Assert.Null(vm.PaintLayer().Depth);
    }

    [AvaloniaFact]
    public void ZeroWritesNullNotZero()
    {
        // Zero and "no depth" are the same plane, and the distinction must
        // never reach a file — the serialization test in Core holds the file
        // side; this holds the authoring side.
        var vm = VmLayers.BareVm();
        PaintRow(vm).Depth = 500;
        PaintRow(vm).Depth = 0;
        Assert.Null(vm.PaintLayer().Depth);
    }

    [AvaloniaFact]
    public void TheRowsReadTopmostFirstLikeTheLayersDocker()
    {
        var vm = VmLayers.BareVm();
        vm.AddPaintedLayerCommand.Execute(null);

        var expected = vm.Doc.Scene.Layers.AsEnumerable().Reverse().Select(l => l.Name);
        Assert.Equal(expected, vm.DepthRows.Select(r => r.Name));
    }

    [AvaloniaFact]
    public void TheCameraPathSamplesTheEasedMove()
    {
        var vm = VmLayers.BareVm();
        Assert.Empty(vm.CameraPathPoints);
        Assert.Empty(vm.CameraKeyPoints);

        vm.Doc.Scene.FrameCount = 5;
        vm.Doc.Scene.Camera = new Camera();
        CameraOps.SetKey(vm.Doc.Scene.Camera, 0, new CameraFraming(100, 100, 1, 0));
        CameraOps.SetKey(vm.Doc.Scene.Camera, 4, new CameraFraming(300, 100, 1, 0));

        Assert.Equal(5, vm.CameraPathPoints.Count);          // one point per frame
        Assert.Equal(2, vm.CameraKeyPoints.Count);           // one dot per key
        Assert.Equal(100, vm.CameraPathPoints[0].X, 3);
        Assert.Equal(300, vm.CameraPathPoints[4].X, 3);
    }

    [AvaloniaFact]
    public void TheDockerIsRegisteredAndToggles()
    {
        Assert.Equal("Scene", DockPanels.TitleOf(DockPanelId.Scene));

        var vm = VmLayers.BareVm();
        // Absent by default: shot machinery appears only when asked for.
        Assert.False(vm.Workspace.SceneDockerVisible);

        vm.ToggleSceneDockerCommand.Execute(null);
        Assert.True(vm.Workspace.SceneDockerVisible);
        vm.ToggleSceneDockerCommand.Execute(null);
        Assert.False(vm.Workspace.SceneDockerVisible);
    }

    [AvaloniaFact]
    public void TheFactorLabelSpeaksTheArtistsTerms()
    {
        var vm = VmLayers.BareVm();
        Assert.Equal("on the picture plane", PaintRow(vm).FactorLabel);

        PaintRow(vm).Depth = 1000;
        Assert.Contains("50", PaintRow(vm).FactorLabel);

        PaintRow(vm).Depth = -500;
        Assert.Contains("in front", PaintRow(vm).FactorLabel);
    }
}
