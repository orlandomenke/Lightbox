using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The main menu's Layer and Animation entries reach the same document edits
/// the dockers do.
/// </summary>
/// <remarks>
/// The menus mirror docker-resident commands (Photoshop's and Krita's
/// convention: every panel verb has a menu address), and the mirrors are new
/// surface: playhead-aimed twins of the cel context menu, and two-way
/// properties behind the Layer menu's checkboxes. Each test here is one of
/// those seams — a menu item that silently did nothing would read as the app
/// being inconsistent, which is exactly the failure the mirroring exists to
/// end.
/// </remarks>
[Collection("BrushState")]
public class MainMenuSurfaceTests : BrushStateIsolated
{
    // ---- Animation menu: playhead-aimed twins of the cel context menu ------------

    [AvaloniaFact]
    public void InsertFrameAtPlayheadMarksTheActiveLayersCel()
    {
        var vm = VmLayers.BareVm();
        vm.CurrentFrameIndex = 3;

        vm.InsertFrameAtPlayhead(FrameRole.Breakdown);

        var cel = vm.PaintLayer().Cels[3];
        Assert.NotNull(cel.Frame);
        Assert.Equal(FrameRole.Breakdown, cel.Frame!.Role);
    }

    [AvaloniaFact]
    public void ExposureAtPlayheadHoldsTheDrawingUnderThePlayhead()
    {
        var vm = VmLayers.BareVm();
        vm.InsertFrameAtPlayhead(FrameRole.Key); // a drawing at frame 0
        var before = vm.Doc.Scene.FrameCount;

        vm.CurrentFrameIndex = 0;
        vm.ExtendExposureAtPlayhead();

        Assert.Equal(before + 1, vm.Doc.Scene.FrameCount);

        // Reduce takes the empty hold cel back off the layer. The sheet itself
        // keeps its length — reducing never shortens the scene under other layers.
        var cels = vm.PaintLayer().Cels.Count;
        vm.ReduceExposureAtPlayhead();
        Assert.Equal(cels - 1, vm.PaintLayer().Cels.Count);
    }

    [AvaloniaFact]
    public void PlaybackRangeFollowsThePlayhead()
    {
        var vm = VmLayers.BareVm();
        for (var i = 1; i < 6; i++)
            vm.InsertFrameAt(vm.LayerRows.First(r => r.SceneIndex == 0).Cells.First(c => c.Index == i), FrameRole.Key);

        vm.CurrentFrameIndex = 2;
        vm.SetPlaybackStartAtPlayhead();
        vm.CurrentFrameIndex = 4;
        vm.SetPlaybackEndAtPlayhead();

        Assert.Equal(2, vm.PlaybackStartFrame);
        Assert.Equal(4, vm.PlaybackEndFrame);

        vm.ClearPlaybackRange();
        Assert.Equal(-1, vm.PlaybackStartFrame);
        Assert.Equal(-1, vm.PlaybackEndFrame);
    }

    // ---- Layer menu: two-way checkbox properties over the active layer -----------

    [AvaloniaFact]
    public void TheLayerMenuCheckboxesWriteTheActiveLayerAndAreUndoable()
    {
        var vm = VmLayers.BareVm();
        var layer = vm.PaintLayer();

        vm.ActiveLayerVisible = false;
        Assert.False(layer.Visible);

        vm.ActiveLayerLocked = true;
        Assert.True(layer.Locked);

        vm.ActiveLayerAlphaLocked = true;
        Assert.True(layer.AlphaLocked);

        // Undoable, like the docker's own toggles: three steps back restores
        // all. Through the properties rather than the captured layer, because
        // undo restores a snapshot and the old instance is no longer the document.
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.True(vm.ActiveLayerVisible);
        Assert.False(vm.ActiveLayerLocked);
        Assert.False(vm.ActiveLayerAlphaLocked);
    }

    [AvaloniaFact]
    public void TogglingVisibilityNotifiesTheMenuCheckbox()
    {
        // The menu checkbox binds ActiveLayerVisible two-way; without a change
        // notification from the toggle path it would stick at its first value.
        var vm = VmLayers.BareVm();
        var heard = new List<string?>();
        vm.PropertyChanged += (_, e) => heard.Add(e.PropertyName);

        vm.ToggleActiveLayerVisibleCommand.Execute(null);

        Assert.False(vm.ActiveLayerVisible);
        Assert.Contains(nameof(MainViewModel.ActiveLayerVisible), heard);
        Assert.Contains(nameof(MainViewModel.ActiveLayerLocked), heard);
    }

    [AvaloniaFact]
    public void SwitchingLayersRefreshesTheMenuCheckboxes()
    {
        var vm = VmLayers.BareVm();
        vm.ActiveLayerLocked = true;
        vm.AddPaintedLayerCommand.Execute(null); // new active layer, unlocked

        Assert.False(vm.ActiveLayerLocked);

        var heard = new List<string?>();
        vm.PropertyChanged += (_, e) => heard.Add(e.PropertyName);
        vm.ActiveLayerIndex = 0; // back to the locked one

        Assert.True(vm.ActiveLayerLocked);
        Assert.Contains(nameof(MainViewModel.ActiveLayerLocked), heard);
    }

    [AvaloniaFact]
    public void MoveActiveLayerUpAndDownReorderTheStack()
    {
        var vm = VmLayers.BareVm();
        vm.AddPaintedLayerCommand.Execute(null);
        var moved = vm.PaintLayer();
        var startIndex = vm.ActiveLayerIndex;

        vm.MoveActiveLayerDownCommand.Execute(null);
        Assert.Equal(startIndex - 1, vm.Doc.Scene.Layers.IndexOf(moved));
        Assert.Same(moved, vm.PaintLayer()); // the moved layer stays active

        vm.MoveActiveLayerUpCommand.Execute(null);
        Assert.Equal(startIndex, vm.Doc.Scene.Layers.IndexOf(moved));
    }
}
