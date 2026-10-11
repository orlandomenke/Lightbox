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
    /// A frame composed after the locks is, byte for byte, the frame that was
    /// on screen before them — for several layers at once.
    /// </summary>
    /// <remarks>
    /// The test above counts publishes, and a count cannot tell a fresh picture
    /// from a stale one: it shows the publish was skipped, not that skipping it
    /// lost nothing. This one asks for the publish the lock no longer makes and
    /// compares what comes back, so a renderer that one day reads a layer's
    /// lock fails here rather than on an artist's screen.
    /// </remarks>
    [AvaloniaFact]
    public void TheFrameAfterLockingIsTheFrameBefore()
    {
        var vm = WithAMark();
        vm.AddPaintedLayerCommand.Execute(null);
        vm.BeginStroke(60, 180, 1);
        vm.MoveStroke(260, 60, 1);
        vm.EndStroke();
        Pump();
        Lightbox.App.Rendering.RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        byte[] Frame()
        {
            vm.PublishSnapshot();
            Pump();
            Assert.NotNull(latest);
            using var bitmap = SkiaSharp.SKBitmap.FromImage(latest!.Image);
            return bitmap.Bytes;
        }

        var before = Frame();
        var painted = vm.Doc.Scene.Layers.Where(l => !l.IsBackground).ToList();
        Assert.Equal(2, painted.Count);
        vm.SelectLayersById(painted.Select(l => l.Id).ToList());

        var publishes = vm.PublishCount;
        vm.SetLayerLocked(painted[0], true);
        vm.SetLayerAlphaLocked(painted[0], true);
        Pump();
        Assert.Equal(publishes, vm.PublishCount);
        Assert.All(painted, l => Assert.True(l.Locked && l.AlphaLocked, $"{l.Name} was not locked with the selection"));

        var after = Frame();
        var inked = before.Count(b => b != 255);
        output.WriteLine($"{before.Length} bytes a frame, {inked} of them not paper; identical after the locks: {before.AsSpan().SequenceEqual(after)}");
        Assert.True(inked > 1000, "the frame compared is blank, so its being unchanged proves nothing");
        Assert.True(before.AsSpan().SequenceEqual(after), "a frame composed after locking differs from the one before");
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
