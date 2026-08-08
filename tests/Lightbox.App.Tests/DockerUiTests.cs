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
        vm.AddPaintedLayerCommand.Execute(null);

        // Paper, the layer the document opened on, and the new one.
        Assert.Equal(3, vm.LayerRows.Count);
        Assert.Same(vm.PaintLayer(), vm.LayerRows[0].Layer);
        Assert.Same(vm.Doc.Scene.Layers[1], vm.LayerRows[1].Layer);
        Assert.Same(vm.Doc.Scene.Layers[0], vm.LayerRows[2].Layer);
        Assert.True(vm.LayerRows[0].IsActive); // new layer becomes active

        vm.AddFrameCommand.Execute(null);
        Assert.All(vm.LayerRows, r => Assert.Equal(2, r.Cells.Count(c => !c.IsVirtual)));
    }

    [AvaloniaFact]
    public void RenameThroughRow_WritesToDocument_AndIsUndoable()
    {
        var vm = new MainViewModel(null);
        var original = vm.PaintLayer().Name;

        vm.LayerRows[0].Name = "Roughs";
        Assert.Equal("Roughs", vm.PaintLayer().Name);

        vm.UndoCommand.Execute(null);
        Assert.Equal(original, vm.PaintLayer().Name);
        // The row re-synced to the restored document.
        Assert.Equal(original, vm.LayerRows[0].Name);
    }

    [AvaloniaFact]
    public void RenameToBlank_SnapsBack_WithoutAnUndoStep()
    {
        var vm = new MainViewModel(null);
        var original = vm.PaintLayer().Name;

        vm.LayerRows[0].Name = "   ";

        Assert.Equal(original, vm.PaintLayer().Name);
        Assert.Equal(original, vm.LayerRows[0].Name);
        vm.UndoCommand.Execute(null); // nothing to undo — name still intact
        Assert.Equal(original, vm.PaintLayer().Name);
    }

    [AvaloniaFact]
    public void VisibilityToggleThroughRow_IsUndoable()
    {
        var vm = new MainViewModel(null);
        vm.LayerRows[0].Visible = false;
        Assert.False(vm.PaintLayer().Visible);

        vm.UndoCommand.Execute(null);
        Assert.True(vm.PaintLayer().Visible);
        Assert.True(vm.LayerRows[0].Visible);
    }

    [AvaloniaFact]
    public void SelectFrame_OnAnotherLayersCell_SelectsThatLayerAndFrame()
    {
        // Bare: this is about row/layer addressing, so paper would only shift
        // every index without changing what is being checked.
        var vm = VmLayers.BareVm();
        vm.AddPaintedLayerCommand.Execute(null); // active layer = 1
        vm.AddFrameCommand.Execute(null);       // playhead = 1

        var bottomRowCell = vm.LayerRows[1].Cells[0]; // scene layer 0, frame 0
        vm.SelectFrameCommand.Execute(bottomRowCell);

        Assert.Equal(0, vm.ActiveLayerIndex);
        Assert.Equal(0, vm.CurrentFrameIndex);
        Assert.True(vm.LayerRows[1].IsActive);
    }

    /// <remarks>
    /// Was <c>AddLayerButton_FollowsKindDropdown</c>, and pinned a Raster/Vector
    /// picker beside the + button. The picker is gone (Q52) — every layer can hold
    /// strokes, imported pixels and symbol placements, so the question had no
    /// answer an artist could act on. Kept pointed the other way rather than
    /// deleted: the + button still has to add a usable layer, and the choice must
    /// not creep back.
    /// </remarks>
    [AvaloniaFact]
    public void AddLayerButton_AddsADrawableLayer_WithNoKindToChoose()
    {
        var vm = new MainViewModel(null);
        var before = vm.Doc.Scene.Layers.Count;

        vm.AddPaintedLayerCommand.Execute(null);

        Assert.Equal(before + 1, vm.Doc.Scene.Layers.Count);
        var added = vm.Doc.Scene.Layers[^1];
        Assert.Equal(LayerKind.Painted, added.Kind);   // provenance: created in-app
        Assert.NotNull(added.Cels[0].Frame);           // keyed and ready to draw on
        Assert.Empty(added.Cels[0].Frame!.Strokes);
        Assert.False(added.Cels[0].Frame!.HasBaseline);

        // The view model no longer offers a kind at all. A dropdown here was the
        // only thing that ever set it away from Painted.
        Assert.Null(typeof(MainViewModel).GetProperty("NewLayerKindChoices"));
        Assert.Null(typeof(MainViewModel).GetProperty("NewLayerKind"));
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
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.OnionSkin = true;

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
        Assert.False(vm.PaintLayer().OnionEnabled);
        // Nothing left at that pixel: the document is transparent, so "ghost
        // gone" means empty rather than paper-coloured.
        Assert.Equal(0, PixelAt(last!, 100, 100).Alpha);
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
