using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// B202: undoing a layer-structure edit used to re-rasterize every drawing in
/// the timeline, for a document in which no drawing had changed.
/// </summary>
/// <remarks>
/// <para>
/// Two independent faults produced one symptom, and both are pinned here.
/// <c>SyncLayerRows</c> maps row <i>i</i> to <c>layers.Count - 1 - i</c>, so
/// adding or removing a layer slides every row onto a different layer; staleness
/// was then decided by comparing the cell's remembered frame id against the frame
/// now under it, so every cell mismatched. And <c>ApplyEditScope</c> had no way
/// to hear that a step changed no drawing, so it dropped every cached bitmap.
/// </para>
/// <para>
/// Measured before the fix, on the 60-frame scene below: 61 full-resolution frame
/// rasterizations and ~1,312 ms per Ctrl+Z, against 3 and ~97 ms with the
/// thumbnail work removed entirely. The assertions here are counts rather than
/// milliseconds, for B151's reason — the property is "this does not happen", not
/// "this got faster", and a count cannot go flaky.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class ThumbnailCacheTests : BrushStateIsolated
{
    private static Stroke Bar(double x0, double y0, double x1, double y1) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#101010",
        Points = [new StrokePoint(x0, y0, 1), new StrokePoint(x1, y1, 1)],
        Brush = new BrushSettings { Size = 24, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1 },
    };

    private static (MainWindow Window, MainViewModel Vm) OpenWithFrames(int count)
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;

        var layer = vm.Doc.Scene.Layers[Math.Clamp(vm.ActiveLayerIndex, 0, vm.Doc.Scene.Layers.Count - 1)];
        var cels = new List<Cel>();
        for (var i = 0; i < count; i++)
        {
            var frame = new Frame();
            frame.Strokes.Add(Bar(50, 50 + i, 300, 400 - i));
            cels.Add(new Cel { Frame = frame });
        }
        layer.Cels = cels;
        vm.Doc.Scene.FrameCount = count;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public void UndoingAnAddedLayerRerastersNothing()
    {
        var (_, vm) = OpenWithFrames(12);

        vm.AddPaintedLayerCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var before = vm.FrameCacheTraffic.Misses;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The layer went away; not one drawing changed. Before the fix this was
        // one rasterization per keyed cel in the scene.
        var rasterized = vm.FrameCacheTraffic.Misses - before;
        Assert.True(rasterized <= 1, $"undo re-rasterized {rasterized} frames; expected at most 1");
    }

    [AvaloniaFact]
    public void ReorderingLayersRerastersNothing()
    {
        // The same fault without any undo in it: moving a layer re-points every
        // row at a different layer, which is what invalidated the thumbnails.
        var (_, vm) = OpenWithFrames(12);
        vm.AddPaintedLayerCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var before = vm.FrameCacheTraffic.Misses;
        vm.MoveLayer(vm.LayerRows[0], -1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var rasterized = vm.FrameCacheTraffic.Misses - before;
        Assert.True(rasterized <= 1, $"a reorder re-rasterized {rasterized} frames; expected at most 1");
    }

    [AvaloniaFact]
    public void UndoingAStrokeStillRefreshesThatOneThumbnail()
    {
        // The other half of correctness: a step that DID change a drawing must
        // still drop that drawing's thumbnail, or the timeline shows the stroke
        // after it has been undone.
        var (_, vm) = OpenWithFrames(12);
        vm.CurrentFrameIndex = 5;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.BeginStroke(120, 120, 1);
        vm.MoveStroke(340, 260, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var before = vm.ThumbnailTraffic.Renders;
        vm.UndoCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.ThumbnailTraffic.Renders > before,
            "undoing a stroke must re-render its thumbnail, not serve the pre-undo one");
    }

    [AvaloniaFact]
    public void ClearingALayersContentStillDropsEveryThumbnailItHad()
    {
        // A structure-shaped operation that is NOT content-preserving. If this
        // were ever tagged frameContentUnchanged the timeline would keep showing
        // drawings the document no longer has.
        var (_, vm) = OpenWithFrames(8);
        var layer = vm.Doc.Scene.Layers[Math.Clamp(vm.ActiveLayerIndex, 0, vm.Doc.Scene.Layers.Count - 1)];

        var before = vm.ThumbnailTraffic.Renders;
        vm.ClearLayerContent(layer);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.True(vm.ThumbnailTraffic.Renders > before,
            "clearing a layer changes every drawing on it, so every thumbnail must be remade");
    }
}
