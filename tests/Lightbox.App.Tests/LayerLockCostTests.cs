using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Locking a layer moves no pixel, so it draws no frame.
/// </summary>
/// <remarks>
/// <para>
/// Measured in the running app on 2026-10-10 (the lab's <c>icon-buttons</c>
/// scenario, owner-shaped document, 1080p): a click on a layer's lock took 117 ms
/// from press to screen and a click on its transparency lock 81 ms, each with a
/// whole-canvas publish in it — for a flag that changes what the artist may do
/// to a layer and nothing about what it looks like. A tool switch, which also
/// changes no pixel, took 52.
/// </para>
/// <para>
/// The cause was the route, not the work: both are undoable document edits, and
/// every undoable edit that is not a stroke went through the listener's
/// document-wide branch, which begins by declaring every pixel stale. The
/// control here is visibility, which goes the same way and <em>must</em>
/// publish — a test that only showed "no publish" would pass on a canvas that
/// had stopped publishing altogether.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LayerLockCostTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static void Pump()
    {
        for (var i = 0; i < 6; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static MainViewModel WithAMark()
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 320, 240, 12, 72, "#ffffff", false));
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(200, 160, 1);
        vm.EndStroke();
        Pump();
        return vm;
    }

    [AvaloniaFact]
    public void LockingALayerPublishesNoFrame()
    {
        var vm = WithAMark();
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];

        var before = vm.PublishCount;
        vm.SetLayerLocked(layer, true);
        Pump();
        var afterLock = vm.PublishCount;
        vm.SetLayerAlphaLocked(layer, true);
        Pump();
        var afterAlpha = vm.PublishCount;
        // The control: the same kind of edit, and one that does change the picture.
        vm.SetLayerLocked(layer, false);
        Pump();
        var settled = vm.PublishCount;
        vm.SetLayerVisible(layer, false);
        Pump();
        var afterHide = vm.PublishCount;

        output.WriteLine(
            $"publishes: lock {afterLock - before}, transparency lock {afterAlpha - afterLock}, " +
            $"unlock {settled - afterAlpha}, hide {afterHide - settled}");
        Assert.True(layer.AlphaLocked);
        Assert.False(layer.Locked);
        Assert.Equal(0, afterLock - before);
        Assert.Equal(0, afterAlpha - afterLock);
        Assert.Equal(0, settled - afterAlpha);
        Assert.True(afterHide > settled, "hiding a layer published nothing, so this test cannot tell a quiet lock from a dead canvas");
    }

    /// <summary>
    /// The lock is still an undoable document edit, and undoing it still tells
    /// everything that shows it.
    /// </summary>
    [AvaloniaFact]
    public void ALockIsStillUndoneAndStillAnnounced()
    {
        var vm = WithAMark();
        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        var id = layer.Id;

        vm.SetLayerLocked(layer, true);
        Pump();
        Assert.True(vm.Doc.Scene.Layers.First(l => l.Id == id).Locked);
        Assert.True(vm.CanUndo);

        vm.UndoCommand.Execute(null);
        Pump();
        Assert.False(vm.Doc.Scene.Layers.First(l => l.Id == id).Locked);

        vm.RedoCommand.Execute(null);
        Pump();
        Assert.True(vm.Doc.Scene.Layers.First(l => l.Id == id).Locked);
        // And the row the artist is looking at agrees with the document.
        Assert.True(vm.LayerPanelItems.OfType<LayerRow>().First(r => r.Layer.Id == id).Locked);
    }
}
