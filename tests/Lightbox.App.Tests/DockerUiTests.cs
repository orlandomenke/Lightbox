using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

public class LayerRowTests
{
    [AvaloniaFact]
    public void Rows_ShowTopmostLayerFirst_AndTrackCells()
    {
        var vm = new MainViewModel(null);
        vm.AddVectorLayerCommand.Execute(null);

        Assert.Equal(2, vm.LayerRows.Count);
        Assert.Same(vm.Doc.Scene.Layers[1], vm.LayerRows[0].Layer);
        Assert.Same(vm.Doc.Scene.Layers[0], vm.LayerRows[1].Layer);
        Assert.Equal("V", vm.LayerRows[0].KindLabel);
        Assert.True(vm.LayerRows[0].IsActive); // new layer becomes active

        vm.AddFrameCommand.Execute(null);
        Assert.All(vm.LayerRows, r => Assert.Equal(2, r.Cells.Count));
    }

    [AvaloniaFact]
    public void RenameThroughRow_WritesToDocument_AndIsUndoable()
    {
        var vm = new MainViewModel(null);
        var original = vm.Doc.Scene.Layers[0].Name;

        vm.LayerRows[0].Name = "Roughs";
        Assert.Equal("Roughs", vm.Doc.Scene.Layers[0].Name);

        vm.UndoCommand.Execute(null);
        Assert.Equal(original, vm.Doc.Scene.Layers[0].Name);
        // The row re-synced to the restored document.
        Assert.Equal(original, vm.LayerRows[0].Name);
    }

    [AvaloniaFact]
    public void RenameToBlank_SnapsBack_WithoutAnUndoStep()
    {
        var vm = new MainViewModel(null);
        var original = vm.Doc.Scene.Layers[0].Name;

        vm.LayerRows[0].Name = "   ";

        Assert.Equal(original, vm.Doc.Scene.Layers[0].Name);
        Assert.Equal(original, vm.LayerRows[0].Name);
        vm.UndoCommand.Execute(null); // nothing to undo — name still intact
        Assert.Equal(original, vm.Doc.Scene.Layers[0].Name);
    }

    [AvaloniaFact]
    public void VisibilityToggleThroughRow_IsUndoable()
    {
        var vm = new MainViewModel(null);
        vm.LayerRows[0].Visible = false;
        Assert.False(vm.Doc.Scene.Layers[0].Visible);

        vm.UndoCommand.Execute(null);
        Assert.True(vm.Doc.Scene.Layers[0].Visible);
        Assert.True(vm.LayerRows[0].Visible);
    }

    [AvaloniaFact]
    public void SelectFrame_OnAnotherLayersCell_SelectsThatLayerAndFrame()
    {
        var vm = new MainViewModel(null);
        vm.AddVectorLayerCommand.Execute(null); // active layer = 1
        vm.AddFrameCommand.Execute(null);       // playhead = 1

        var bottomRowCell = vm.LayerRows[1].Cells[0]; // scene layer 0, frame 0
        vm.SelectFrameCommand.Execute(bottomRowCell);

        Assert.Equal(0, vm.ActiveLayerIndex);
        Assert.Equal(0, vm.CurrentFrameIndex);
        Assert.True(vm.LayerRows[1].IsActive);
    }

    [AvaloniaFact]
    public void AddLayerButton_FollowsKindDropdown()
    {
        var vm = new MainViewModel(null);

        vm.NewLayerKind = vm.NewLayerKindChoices[1]; // Vector
        vm.AddLayerOfSelectedKindCommand.Execute(null);
        Assert.Equal(LayerKind.Vector, vm.Doc.Scene.Layers[^1].Kind);

        vm.NewLayerKind = vm.NewLayerKindChoices[0]; // Raster (the default)
        vm.AddLayerOfSelectedKindCommand.Execute(null);
        Assert.Equal(LayerKind.Painted, vm.Doc.Scene.Layers[^1].Kind);
    }
}

public class PerLayerOnionTests
{
    private static SKColor PixelAt(RenderSnapshot snapshot, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        return bmp.GetPixel(x, y);
    }

    [AvaloniaFact]
    public void DisablingLayerOnion_RemovesItsGhosts_FromTheSnapshot()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, OnionSkin = true };

        // Key 1 carries a stroke; key 2 is empty; playhead on 2 → key 1 ghosts.
        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(140, 100, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);

        RenderSnapshot? last = null;
        vm.SnapshotChanged += s => last = s;
        vm.PublishSnapshot();
        Assert.NotNull(last);
        Assert.NotEqual(SKColors.White, PixelAt(last!, 100, 100)); // ghost visible

        vm.LayerRows[0].OnionEnabled = false; // publishes a fresh snapshot
        Assert.False(vm.Doc.Scene.Layers[0].OnionEnabled);
        Assert.Equal(SKColors.White, PixelAt(last!, 100, 100)); // ghost gone
    }
}

public class PlaybackSpeedTests
{
    [AvaloniaFact]
    public void SpeedPercent_ClampsToSaneRange()
    {
        var vm = new MainViewModel(null);
        vm.PlaybackSpeedPercent = 5;
        Assert.Equal(10, vm.PlaybackSpeedPercent);
        vm.PlaybackSpeedPercent = 5000;
        Assert.Equal(400, vm.PlaybackSpeedPercent);
    }

    [AvaloniaFact]
    public void ClockInterval_ScalesWithFpsAndSpeed()
    {
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 12), PlaybackClock.IntervalFor(12, 100));
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 6), PlaybackClock.IntervalFor(12, 50));
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 24), PlaybackClock.IntervalFor(12, 200));
    }
}

public class SidebarTests
{
    [AvaloniaFact]
    public void ToggleSidebar_FlipsVisibility()
    {
        var vm = new MainViewModel(null);
        Assert.True(vm.SidebarVisible);
        vm.ToggleSidebarCommand.Execute(null);
        Assert.False(vm.SidebarVisible);
        vm.ToggleSidebarCommand.Execute(null);
        Assert.True(vm.SidebarVisible);
    }

    [AvaloniaFact]
    public void SwitchSidebarSide_FlipsSide()
    {
        var vm = new MainViewModel(null);
        Assert.True(vm.SidebarOnRight);
        vm.SwitchSidebarSideCommand.Execute(null);
        Assert.False(vm.SidebarOnRight);
        vm.SwitchSidebarSideCommand.Execute(null);
        Assert.True(vm.SidebarOnRight);
    }

    [AvaloniaFact]
    public void ToggleTimeline_FlipsVisibility()
    {
        var vm = new MainViewModel(null);
        Assert.True(vm.TimelineVisible);
        vm.ToggleTimelineCommand.Execute(null);
        Assert.False(vm.TimelineVisible);
        vm.ToggleTimelineCommand.Execute(null);
        Assert.True(vm.TimelineVisible);
    }
}
