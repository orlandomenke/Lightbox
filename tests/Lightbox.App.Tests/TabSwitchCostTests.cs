using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Switching tabs does not rebuild the document you switched to (B362).
/// </summary>
/// <remarks>
/// <para>
/// Reported as *"switching tabs between documents is really slow and has a
/// discernible time between clicking and switching to the new document"*.
/// <c>AttachEditor</c> ended in a wholesale <c>_cache.Clear()</c>, so every
/// crossing was a guaranteed total miss followed by a full frame render on the
/// thread that switched — B332's symptom by construction rather than by chance.
/// </para>
/// <para>
/// <b>Measured before the change, by counting rather than timing:</b> a
/// document warm in the frame cache held <b>4,147,200 bytes</b> at 1080p;
/// leaving it dropped every one of them; and six crossings of A→B→A→B→A→B each
/// took misses, none cheaper than the first. Afterwards the same six crossings
/// take <b>zero</b>.
/// </para>
/// <para>
/// <b>Counted, not timed</b>, for B353's reason and B259's: a duration measures
/// this box, where misses taken and bytes held are exact and cannot flake.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TabSwitchCostTests : BrushStateIsolated
{
    /// <summary>A few marks, so a render is not free.</summary>
    private static void Draw(MainViewModel vm, int strokes)
    {
        for (var i = 0; i < strokes; i++)
        {
            vm.BeginStroke(10 + i, 10, 1);
            vm.MoveStroke(90 + i, 60, 1);
            vm.EndStroke();
        }
    }

    private static Doc SecondDoc() =>
        DocumentFactory.CreateDoc(96, 96, paperColor: Scene.DefaultBackgroundColor);

    [AvaloniaFact]
    public void LeavingADocumentKeepsItsBitmapsForTheReturn()
    {
        var vm = VmLayers.PaperVm();
        Draw(vm, 6);
        vm.PublishSnapshot();
        var warm = vm.FrameCacheTraffic.Bytes;

        vm.OpenDocumentTab(SecondDoc(), null);

        Assert.True(warm > 0, $"the first document should have warmed the cache; it held {warm} bytes");
        // A frame stays valid for the document it belongs to however many tabs
        // you cross, because entries are keyed by frame identity. Dropping them
        // on a swap threw away work that was still good.
        Assert.True(
            vm.FrameCacheTraffic.Bytes >= warm,
            $"leaving kept {vm.FrameCacheTraffic.Bytes} of {warm} bytes — the return will rebuild the rest");
    }

    [AvaloniaFact]
    public void ComingBackCostsNothing()
    {
        // The number the report is about. Before: the return took misses and
        // rebuilt 4,147,200 bytes inside the switch. Now it takes none.
        var vm = VmLayers.PaperVm();
        Draw(vm, 6);
        var first = vm.Tabs[0];
        vm.PublishSnapshot();

        vm.OpenDocumentTab(SecondDoc(), null);
        vm.PublishSnapshot();

        var before = vm.FrameCacheTraffic.Misses;
        vm.ActiveTab = first;
        var onReturn = vm.FrameCacheTraffic.Misses - before;

        Assert.Equal(0, onReturn);
    }

    [AvaloniaFact]
    public void EveryCrossingIsFree()
    {
        // Three round trips, so the shape is visible rather than inferred from
        // one crossing. Measured before: every one paid. After: 0, 0, 0, 0, 0, 0.
        var vm = VmLayers.PaperVm();
        Draw(vm, 6);
        var first = vm.Tabs[0];
        vm.PublishSnapshot();
        vm.OpenDocumentTab(SecondDoc(), null);
        var second = vm.Tabs[1];
        vm.PublishSnapshot();

        var perCrossing = new List<long>();
        for (var i = 0; i < 3; i++)
        {
            foreach (var target in (DocumentTab[])[first, second])
            {
                var before = vm.FrameCacheTraffic.Misses;
                vm.ActiveTab = target;
                vm.PublishSnapshot();
                perCrossing.Add(vm.FrameCacheTraffic.Misses - before);
            }
        }

        Assert.All(perCrossing, misses => Assert.Equal(0, misses));
    }

    [AvaloniaFact]
    public void KeepingThemIsStillBoundedByTheBudget()
    {
        // The guard against fixing this by leaking. Nothing about holding more
        // documents escapes the cache's own byte budget and LRU — which is what
        // made dropping them on a schedule unnecessary in the first place.
        var vm = VmLayers.PaperVm();
        Draw(vm, 6);
        vm.PublishSnapshot();

        for (var i = 0; i < 4; i++)
        {
            vm.OpenDocumentTab(SecondDoc(), null);
            vm.PublishSnapshot();
        }

        Assert.True(
            vm.FrameCacheTraffic.Bytes <= FrameBitmapCache.ByteBudget,
            $"held {vm.FrameCacheTraffic.Bytes} against a budget of {FrameBitmapCache.ByteBudget}");
    }
}
