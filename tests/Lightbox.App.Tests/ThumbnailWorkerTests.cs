using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// Thumbnails rendered off the UI thread (the performance lab measured 5.9 s of
/// them inline on opening a 64-drawing document).
/// </summary>
/// <remarks>
/// The worker's results come back through <see cref="ThumbnailWorker.Post"/>;
/// here that is a queue the test drains itself, so when a result is installed is
/// the test's decision rather than the scheduler's.
/// </remarks>
[Collection("BrushState")]
public sealed class ThumbnailWorkerTests : BrushStateIsolated
{
    private readonly ConcurrentQueue<Action> _posted = new();

    public ThumbnailWorkerTests() => ThumbnailWorker.Post = _posted.Enqueue;

    public override void Dispose()
    {
        ThumbnailWorker.Post = null;
        base.Dispose();
    }

    private static Frame Drawing(string id, int i)
    {
        var frame = new Frame { Id = id };
        frame.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush, Color = "#101010",
            Points = [new StrokePoint(40 + i * 5, 40, 1), new StrokePoint(300, 250 - i * 5, 1)],
            Brush = new BrushSettings { Size = 12, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1 },
        });
        return frame;
    }

    private static Doc Drawings(int count)
    {
        var doc = DocumentFactory.CreateDoc(400, 300);
        doc.Scene.Layers.Clear();
        var layer = new Layer { Name = "Ink" };
        for (var i = 0; i < count; i++) layer.Cels.Add(new Cel { Frame = Drawing($"d{i}", i) });
        doc.Scene.Layers.Add(layer);
        doc.Scene.FrameCount = count;
        return doc;
    }

    /// <summary>Run whatever the worker posts until it has been quiet for half a second.</summary>
    private void DeliverUntilQuiet()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var quietSince = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline)
        {
            if (_posted.TryDequeue(out var arrive))
            {
                arrive();
                quietSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(500))
            {
                return;
            }
            else
            {
                Thread.Sleep(5);
            }
        }
    }

    private static IEnumerable<FrameCell> KeyedCells(MainViewModel vm) =>
        vm.LayerRows.SelectMany(r => r.Cells.Where(c =>
            Lightbox.Core.Timeline.ExposureSheet.FrameAtExactIndex(r.Layer, c.Index) is not null));

    /// <summary>
    /// Opening renders no thumbnail on the UI thread — the frame cache sees no
    /// thumbnail-sized miss — and every cell is filled once the worker is done,
    /// including any whose first render was thrown away as stale.
    /// </summary>
    [AvaloniaFact]
    public void OpeningRendersThumbnailsOffTheUiThreadAndFillsEveryCell()
    {
        var vm = new MainViewModel(null);
        vm.ReplaceDocument(Drawings(8));

        Assert.Equal(0, vm.FrameCache.RecentMisses.Count(m => m.Scale < 1));
        Assert.Contains(KeyedCells(vm), c => c.Thumb is null);

        DeliverUntilQuiet();
        Assert.All(KeyedCells(vm), c => Assert.NotNull(c.Thumb));
    }

    /// <summary>
    /// A render begun before the drawing changed is thrown away, never installed
    /// — it would otherwise also reach the canvas, through the cache they share —
    /// and the drawing is asked for again.
    /// </summary>
    [Fact]
    public void AResultForADrawingThatChangedSinceIsDiscardedAndAskedForAgain()
    {
        var posted = new ConcurrentQueue<Action>();
        var installed = new List<string>();
        var staleCalls = 0;
        using var worker = new ThumbnailWorker(
            posted.Enqueue, made => { installed.Add(made.Frame.Id); return false; }, () => staleCalls++);

        worker.Request(Drawing("a", 0), 400, 300, 0.25, 0);
        worker.Invalidate("a"); // the drawing changed while its thumbnail was being made

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (posted.IsEmpty && DateTime.UtcNow < deadline) Thread.Sleep(5);
        while (posted.TryDequeue(out var arrive)) arrive();

        Assert.Empty(installed);
        Assert.Equal(1, worker.Discarded);
        Assert.Equal(1, staleCalls);
    }
}
