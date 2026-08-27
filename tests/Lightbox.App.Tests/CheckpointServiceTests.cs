using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The service that renders raster checkpoints after a save (B30), and the
/// cache hook that spends them.
/// </summary>
/// <remarks>
/// The render runs on a worker while the artist keeps drawing, so the cases
/// worth pinning are the ones where the document moved underneath it. Two of
/// them, and they must go opposite ways: appending strokes leaves the covered
/// prefix alone and the render is still good, while editing a covered stroke
/// makes it a picture of something that no longer exists.
/// </remarks>
public class CheckpointServiceTests
{
    private const int W = 300, H = 200;

    private static Frame Painting(int strokes, int seed = 3)
    {
        var frame = new Frame();
        for (var s = 0; s < strokes; s++)
        {
            var a = (s * 2654435761u + (uint)seed * 40503u) % 10007;
            var b = (s * 1597334677u + (uint)seed * 22571u) % 10009;
            var points = new List<StrokePoint>();
            for (var i = 0; i < 6; i++)
            {
                var t = i / 5.0;
                points.Add(new StrokePoint(
                    20 + a % (uint)(W - 40) + Math.Sin(t * Math.PI) * 18,
                    20 + b % (uint)(H - 40) + Math.Cos(t * Math.PI * 1.5) * 14,
                    0.4 + (i % 3) * 0.2));
            }
            frame.Strokes.Add(new Stroke
            {
                Color = "#2a3a5a",
                Brush = new BrushSettings { Size = 18, Hardness = 0.4, Opacity = 0.8, Flow = 0.5 },
                Points = points,
            });
        }
        return frame;
    }

    private static Doc DocWith(Frame frame)
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Scene.Layers.Clear();
        doc.Scene.Layers.Add(new Layer { Cels = { new Cel { Frame = frame } } });
        return doc;
    }

    /// <summary>
    /// A service whose "post back to the UI thread" step runs where it is told
    /// to, so a test can decide when the attach happens.
    /// </summary>
    private static (CheckpointService Service, Func<Task> Deliver) ServiceFor(Doc doc)
    {
        Action? pending = null;
        var service = new CheckpointService(() => doc, action => pending = action);
        return (service, async () =>
        {
            await service.InFlight;
            pending?.Invoke();
            pending = null;
        });
    }

    [Fact]
    public async Task ASavedPaintingGetsACheckpoint()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);
        var (service, deliver) = ServiceFor(doc);

        service.Request();
        await deliver();

        Assert.NotNull(frame.Checkpoint);
        Assert.Equal(FrameCheckpoints.MinStrokes, frame.Checkpoint!.Strokes);
        Assert.NotNull(FrameCheckpoints.Usable(doc, frame));
    }

    /// <summary>An animation cel is nowhere near worth a full-canvas image.</summary>
    [Fact]
    public async Task AnOrdinaryDrawingIsLeftAlone()
    {
        var frame = Painting(20);
        var doc = DocWith(frame);
        var (service, deliver) = ServiceFor(doc);

        service.Request();
        await deliver();

        Assert.Null(frame.Checkpoint);
    }

    /// <summary>
    /// Strokes painted while the render was running do not throw it away.
    /// </summary>
    /// <remarks>
    /// The ordinary case, and the one that has to work or the feature is
    /// theatre: a render takes seconds on the documents that need it, and an
    /// artist does not stop painting for them. The checkpoint covers a prefix,
    /// and the prefix is exactly what did not change.
    /// </remarks>
    [Fact]
    public async Task PaintingWhileItRendersDoesNotWasteTheRender()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);
        var (service, deliver) = ServiceFor(doc);

        service.Request();
        await service.InFlight;
        foreach (var stroke in Painting(5, seed: 88).Strokes) frame.Strokes.Add(stroke);
        await deliver();

        Assert.NotNull(frame.Checkpoint);
        Assert.Equal(FrameCheckpoints.MinStrokes, frame.Checkpoint!.Strokes);
        Assert.NotNull(FrameCheckpoints.Usable(doc, frame));
    }

    /// <summary>
    /// Editing a covered stroke while it renders throws the render away.
    /// </summary>
    [Fact]
    public async Task EditingWhileItRendersThrowsTheRenderAway()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);
        var (service, deliver) = ServiceFor(doc);

        service.Request();
        await service.InFlight;
        frame.Strokes[7].Color = "#ff0000";
        await deliver();

        Assert.Null(frame.Checkpoint);
    }

    /// <summary>
    /// The worker reads its own copy, so a stroke list growing underneath it is
    /// not a torn read.
    /// </summary>
    /// <remarks>
    /// The plan deep-copies the covered strokes on the calling thread for exactly
    /// this: without it the render walks a <c>List&lt;Stroke&gt;</c> the artist is
    /// appending to, and the failure would be an intermittent exception nobody
    /// can reproduce. Pinned by mutating the live list and checking the plan does
    /// not see it.
    /// </remarks>
    [Fact]
    public void ThePlanHoldsItsOwnCopyOfTheStrokes()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);

        var plan = FrameCheckpoints.Plan(doc, frame);
        Assert.NotNull(plan);

        var before = plan!.Strokes.Count;
        frame.Strokes.Clear();

        Assert.Equal(before, plan.Strokes.Count);
        Assert.NotNull(FrameCheckpoints.Render(plan));
    }

    /// <summary>Turning it off takes the stored pixels out, not just the taking.</summary>
    [Fact]
    public async Task TurningItOffRemovesWhatIsAlreadyStored()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);
        var (service, deliver) = ServiceFor(doc);
        service.Request();
        await deliver();
        Assert.NotNull(frame.Checkpoint);

        CheckpointService.Clear(doc);

        Assert.Null(frame.Checkpoint);
        Assert.DoesNotContain("\"checkpoint\"", DocJson.Serialize(doc));
    }

    [Fact]
    public async Task ADisabledServiceTakesNothing()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);
        var (service, deliver) = ServiceFor(doc);
        service.Enabled = false;

        service.Request();
        await deliver();

        Assert.Null(frame.Checkpoint);
    }

    /// <summary>
    /// A document does not spend more than the budget on stored renderings, and
    /// the biggest drawings get it.
    /// </summary>
    /// <remarks>
    /// The case the stroke threshold's reasoning does not cover: a painted
    /// sequence where every cel qualifies. Without a byte budget that is a
    /// full-canvas image per drawing, in the file and resident. With one, the
    /// drawings that gain most are served and the rest replay — which is what
    /// they did before the feature existed.
    /// </remarks>
    [Fact]
    public async Task ADocumentDoesNotSpendMoreThanTheBudgetOnStoredRenderings()
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Scene.Layers.Clear();
        var layer = new Layer();
        doc.Scene.Layers.Add(layer);

        // Four qualifying drawings of visibly different sizes, added smallest
        // first so document order and size order disagree.
        var frames = new List<Frame>();
        foreach (var strokes in new[]
                 {
                     FrameCheckpoints.MinStrokes, FrameCheckpoints.MinStrokes + 40,
                     FrameCheckpoints.MinStrokes + 80, FrameCheckpoints.MinStrokes + 120,
                 })
        {
            var frame = Painting(strokes, seed: strokes);
            frames.Add(frame);
            layer.Cels.Add(new Cel { Frame = frame });
        }

        var previous = CheckpointService.ByteBudget;
        try
        {
            // A budget no single rendering can fit inside. One still lands, by
            // design — see `ByteBudget` — and nothing after it does.
            CheckpointService.ByteBudget = 1;

            var (service, deliver) = ServiceFor(doc);
            service.Request();
            await deliver();

            Assert.Equal(1, frames.Count(f => f.Checkpoint is not null));
            // And it is the largest drawing, not the first one in the document.
            Assert.NotNull(frames[^1].Checkpoint);
        }
        finally
        {
            CheckpointService.ByteBudget = previous;
        }
    }

    /// <summary>With room for them all, they all get one.</summary>
    /// <remarks>
    /// The other side of the budget, and worth its own test: a cap that silently
    /// applied itself to ordinary documents would be indistinguishable from the
    /// feature not working, and nothing else here would notice.
    /// </remarks>
    [Fact]
    public async Task WithRoomForThemAllEveryQualifyingDrawingGetsOne()
    {
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Scene.Layers.Clear();
        var layer = new Layer();
        doc.Scene.Layers.Add(layer);
        var frames = new List<Frame>();
        for (var i = 0; i < 3; i++)
        {
            var frame = Painting(FrameCheckpoints.MinStrokes + i * 20, seed: 10 + i);
            frames.Add(frame);
            layer.Cels.Add(new Cel { Frame = frame });
        }

        var (service, deliver) = ServiceFor(doc);
        service.Request();
        await deliver();

        Assert.All(frames, f => Assert.NotNull(f.Checkpoint));
    }

    // ---- the cache hook ------------------------------------------------------

    /// <summary>
    /// A cache miss spends the checkpoint, and lands on the same pixels a replay
    /// would have.
    /// </summary>
    /// <remarks>
    /// The end-to-end version of the bit-equality property, through the object
    /// that actually renders frames for the canvas. The resolver is the whole of
    /// the wiring — a cache with none set replays the record, which is what every
    /// export path deliberately does.
    /// </remarks>
    [Fact]
    public void TheFrameCacheStartsFromACheckpointWhenOneIsOffered()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);
        frame.Checkpoint = FrameCheckpoints.Render(FrameCheckpoints.Plan(doc, frame)!);

        using var plain = new FrameBitmapCache();
        using var withCheckpoint = new FrameBitmapCache
        {
            CheckpointResolver = f => FrameCheckpoints.Usable(doc, f),
        };

        var replayed = plain.Get(frame, W, H).GetPixelSpan().ToArray();
        var shortcut = withCheckpoint.Get(frame, W, H).GetPixelSpan().ToArray();

        Assert.True(replayed.AsSpan().SequenceEqual(shortcut), "a checkpointed frame differs from a replayed one");
    }

    /// <summary>
    /// A cache with no resolver replays the record — which is what an export is.
    /// </summary>
    /// <remarks>
    /// Not a redundant restatement of the test above: it is the property that
    /// makes "a checkpoint is never exported" true by construction rather than by
    /// a rule somebody has to follow. The exporters build their own caches and set
    /// only <c>PoseResolver</c>, so they cannot accidentally acquire this one.
    /// </remarks>
    [Fact]
    public void ACacheWithNoResolverNeverTouchesAStoredCheckpoint()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocWith(frame);
        frame.Checkpoint = FrameCheckpoints.Render(FrameCheckpoints.Plan(doc, frame)!);
        // Pixels that would be obvious if they were ever drawn.
        using var wrong = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(wrong)) canvas.Clear(SKColors.Red);
        frame.Checkpoint!.PixelsBase64 = CheckpointCodec.Encode(wrong)!;

        using var cache = new FrameBitmapCache();
        var pixels = cache.Get(frame, W, H).GetPixelSpan().ToArray();

        using var replayed = FrameRasterizer.Materialize(frame, W, H);
        Assert.True(replayed.GetPixelSpan().SequenceEqual(pixels));
    }
}
