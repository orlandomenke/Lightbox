using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What B30 is about, as a number: opening a painting.
/// </summary>
/// <remarks>
/// <para>
/// <b>The budget is on the checkpointed open alone, and the replay is printed
/// beside it rather than asserted.</b> A ratio would look like the better test
/// and would be the worse one: the replay's cost is the thing this feature
/// exists because of, so pinning it would mean a future speed-up to the brush
/// engine — an unambiguous win — failing this test for making the two numbers
/// closer together. What must not regress is the fast path.
/// </para>
/// <para>
/// 1000 strokes rather than the ten thousand the bug quotes, because the
/// unchecked arm has to run too and B30 prices that at ~10.7 ms a painterly
/// stroke on the dev container. The shape is linear (<c>n^1.00</c>, no cliff),
/// so a thousand says everything a hundred thousand would, in seconds instead
/// of minutes.
/// </para>
/// </remarks>
[Trait("Category", "Performance")]
[Collection("Performance")]
public class CheckpointOpenCostTests(ITestOutputHelper o)
{
    private const int W = 1280, H = 720;

    private static Frame Painting(int strokes, int seed = 5)
    {
        var frame = new Frame();
        for (var s = 0; s < strokes; s++)
        {
            var a = (s * 2654435761u + (uint)seed * 40503u) % 10007;
            var b = (s * 1597334677u + (uint)seed * 22571u) % 10009;
            var x = 60 + a % (uint)(W - 120);
            var y = 60 + b % (uint)(H - 120);
            var points = new List<StrokePoint>();
            for (var i = 0; i < 8; i++)
            {
                var t = i / 7.0;
                points.Add(new StrokePoint(
                    x + Math.Sin(t * Math.PI) * 46 + t * 24,
                    y + Math.Cos(t * Math.PI * 1.5) * 34,
                    0.4 + (i % 4) * 0.2));
            }
            frame.Strokes.Add(new Stroke
            {
                Color = "#3a4a6a",
                Brush = new BrushSettings
                {
                    Size = 60, Hardness = 0.3, Opacity = 0.8, Flow = 0.5, Spacing = 0.05,
                },
                Points = points,
            });
        }
        return frame;
    }

    /// <summary>
    /// A checkpointed painting opens inside the per-action budget; the same
    /// painting without one is printed for comparison.
    /// </summary>
    [Fact]
    public void OpeningACheckpointedPaintingIsNotPayingForItsHistory()
    {
        var frame = Painting(1000);
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Scene.Layers.Clear();
        doc.Scene.Layers.Add(new Layer { Cels = { new Cel { Frame = frame } } });

        var replay = Bench.FastestMs(3, () =>
        {
            using var bitmap = FrameRasterizer.Materialize(frame, W, H);
        });

        var plan = FrameCheckpoints.Plan(doc, frame);
        Assert.NotNull(plan);
        frame.Checkpoint = FrameCheckpoints.Render(plan!);
        Assert.NotNull(frame.Checkpoint);

        var checkpoint = FrameCheckpoints.Usable(doc, frame);
        Assert.NotNull(checkpoint);

        var opened = Bench.FastestMs(3, () =>
        {
            using var bitmap = FrameRasterizer.Materialize(
                frame, W, H, checkpoint: FrameCheckpoints.Usable(doc, frame));
        });

        var bytes = frame.Checkpoint!.PixelsBase64.Length;
        o.WriteLine($"replay      {replay,8:F1} ms   ({replay / 1000:F2} ms a stroke)");
        o.WriteLine($"checkpoint  {opened,8:F1} ms   ({replay / opened:F0}x, {bytes / 1024:N0} KB stored)");

        // Measured at 30.6 ms against a replay of 8 452 ms on the dev container
        // — a 276x difference — so 200 ms is the charter's usual headroom over
        // the observation. Loose on purpose: this catches an order of magnitude,
        // not drift. The whole of the fast path is a fingerprint, a PNG decode
        // and a blit, none of which grows with the stroke count, so a failure
        // here means the checkpoint stopped being used rather than that it got
        // slower — and that failure arrives at 8 000 ms, not at 250.
        Assert.True(
            opened < 200,
            $"a checkpointed open cost {opened:F1} ms against a 200 ms budget "
            + $"(replay was {replay:F1} ms)");
    }

    /// <summary>
    /// The fast path does not grow with what came before it.
    /// </summary>
    /// <remarks>
    /// <b>The exponent is the durable claim and the milliseconds are not</b> — a
    /// time is a property of this container, a slope is a property of the code.
    /// Opening a checkpointed painting has to cost the same whether it holds five
    /// hundred strokes or four thousand, because none of what it does depends on
    /// the count. If this ever fails, something is walking the record again.
    /// </remarks>
    [Fact]
    public void ACheckpointedOpenCostsTheSameAtFourTimesTheHistory()
    {
        var small = Measure(500);
        var large = Measure(2000);
        o.WriteLine($"500 strokes {small,8:F1} ms");
        o.WriteLine($"2000        {large,8:F1} ms   ({large / small:F2}x for 4x the record)");

        // Two doublings of the record may not double the open. Generous because
        // it is a ratio of two small numbers on a shared runner; a genuine
        // regression here is linear and would read at 4x, not 2x.
        Assert.True(
            large < small * 2.5,
            $"a checkpointed open grew {large / small:F2}x for four times the strokes "
            + $"({small:F1} ms to {large:F1} ms)");
    }

    private static double Measure(int strokes)
    {
        var frame = Painting(strokes);
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Scene.Layers.Clear();
        doc.Scene.Layers.Add(new Layer { Cels = { new Cel { Frame = frame } } });
        frame.Checkpoint = FrameCheckpoints.Render(FrameCheckpoints.Plan(doc, frame)!);

        return Bench.FastestMs(3, () =>
        {
            using var bitmap = FrameRasterizer.Materialize(
                frame, W, H, checkpoint: FrameCheckpoints.Usable(doc, frame));
        });
    }
}
