using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// Removing a whole frame from the scene — every layer's cel at that index —
/// and pulling the rest of the sheet back. Q88.
/// </summary>
/// <remarks>
/// The operation is as old as <c>DocumentEditor.DeleteFrame</c> and was
/// reachable from exactly one 🗑 button on the timeline bar, absent from
/// <c>ShortcutMap</c> and absent from the X-sheet's own right-click menu —
/// where the <em>Delete cel</em> beside it is row-scoped. So an artist looking
/// for "take this frame out of the scene" met the wrong verb first and
/// concluded the right one did not exist. Most of what these tests guard is
/// therefore reachability rather than arithmetic.
/// </remarks>
[Collection("BrushState")]
public class DeleteColumnTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "Lightbox.sln")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }
        return dir;
    }

    [AvaloniaFact]
    public void ItTakesTheFrameOutOfEveryLayer_AndUndoPutsItBack()
    {
        var vm = Vm();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        var before = vm.Doc.Scene.FrameCount;
        var perLayer = vm.Doc.Scene.Layers
            .Select(l => l.Cels.Select(c => c.Frame?.Id).ToList()).ToList();

        vm.DeleteColumnAt(1);

        Assert.Equal(before - 1, vm.Doc.Scene.FrameCount);
        // Every layer lost its cel at that index — that is what makes it a
        // column rather than the row-scoped Delete cel beside it.
        foreach (var (layer, was) in vm.Doc.Scene.Layers.Zip(perLayer))
        {
            Assert.Equal(was[0], layer.Cels[0].Frame?.Id);
            Assert.Equal(was[2], layer.Cels[1].Frame?.Id);
        }

        vm.UndoCommand.Execute(null);
        Assert.Equal(before, vm.Doc.Scene.FrameCount);
        Assert.Equal(
            perLayer,
            vm.Doc.Scene.Layers.Select(l => l.Cels.Select(c => c.Frame?.Id).ToList()).ToList());
    }

    [AvaloniaFact]
    public void ThePlayheadNeverLandsPastTheEnd()
    {
        var vm = Vm();
        vm.AddFrameCommand.Execute(null);
        vm.CurrentFrameIndex = vm.Doc.Scene.FrameCount - 1;

        vm.DeleteColumnAt(vm.CurrentFrameIndex);

        Assert.True(vm.CurrentFrameIndex < vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void TheLastFrameOfASceneIsRefused()
    {
        // A scene is never zero frames long, so the last column has nothing to
        // delete into.
        var vm = Vm();
        Assert.Equal(1, vm.Doc.Scene.FrameCount);

        vm.DeleteColumnAt(0);

        Assert.Equal(1, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void ALockedLayerRefusesTheWholeColumn()
    {
        // Deleting the frame from four layers and not the fifth would slide
        // those four out of step with it — worse than not deleting at all, and
        // the reason this refuses rather than skipping the locked one.
        var vm = Vm();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        var before = vm.Doc.Scene.FrameCount;
        vm.SetLayerLocked(vm.Doc.Scene.Layers[1], true);

        vm.DeleteColumnAt(0);

        Assert.Equal(before, vm.Doc.Scene.FrameCount);
        Assert.Contains("locked", vm.AiStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void ThePaperBeingLockedDoesNotRefuseIt()
    {
        // The trap in the rule above: a document with paper has a locked layer
        // in it from the moment it is created, so a lock check that did not
        // exempt the paper would refuse every column delete in the ordinary
        // case — and the refusal would name a layer the artist never locked.
        var vm = VmLayers.PaperVm();
        vm.AddFrameCommand.Execute(null);
        var before = vm.Doc.Scene.FrameCount;
        Assert.Contains(vm.Doc.Scene.Layers, l => l.IsBackground && l.Locked);

        vm.DeleteColumnAt(0);

        Assert.Equal(before - 1, vm.Doc.Scene.FrameCount);
    }

    [AvaloniaFact]
    public void ItIsInTheShortcutRegistry_SoItCanBeBoundAndFound()
    {
        // The whole of Q88: the capability existed and could not be reached.
        // A command wired straight to a button is invisible to the Configure
        // window's editor, so it cannot be searched or rebound.
        var map = new ShortcutMap();
        var entry = map.Definitions.SingleOrDefault(d => d.Id == "timeline.deleteColumn");
        Assert.NotNull(entry);
        Assert.Equal("Timeline", entry!.Category);
    }

    [AvaloniaFact]
    public void TheXsheetMenuOffersIt_AndSaysWhichDeleteItIs()
    {
        var xaml = File.ReadAllText(
            Path.Combine(RepoRoot(), "src/Lightbox.App/Views/MainWindow.axaml"));
        Assert.Contains("OnDeleteColumn", xaml);
        // Named apart from the row-scoped delete beside it, which is the
        // confusion that made this read as missing in the first place.
        var item = Regex.Match(xaml, @"<MenuItem Header=""Delete column[^""]*""");
        Assert.True(item.Success, "the X-sheet menu has no Delete column entry");
    }
}
