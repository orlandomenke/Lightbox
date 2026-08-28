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
    /// The render side of a checkpointed open does not grow with what came
    /// before it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The exponent is the durable claim and the milliseconds are not</b> — a
    /// time is a property of this container, a slope is a property of the code.
    /// Decoding a checkpoint and blitting it has to cost the same whether the
    /// frame holds five hundred strokes or two thousand, because none of what it
    /// does depends on the count. If this fails, something is walking the record
    /// again.
    /// </para>
    /// <para>
    /// <b>B339: what this used to time, and why that made it flake.</b> It timed
    /// the whole open — <c>Usable</c> and then <c>Materialize</c> — and asserted
    /// the total was constant. But <c>Usable</c> hashes every stroke the
    /// checkpoint covers: it is <em>deliberately</em> linear in the record,
    /// because serializing the strokes is how the fast path earns the right to
    /// skip replaying them. So the assertion was that a quantity half of which
    /// grows with n shall not grow with n, and it passed only while the constant
    /// half was still the larger one.
    /// </para>
    /// <para>
    /// Measured 2026-08-28 at 1280x720, fastest of several runs each:
    /// </para>
    /// <code>
    ///   strokes    whole open    fingerprint    decode+blit
    ///       500      30.41 ms    12.53 (41%)       15.21 ms
    ///      1000      29.55 ms    15.30 (52%)       12.87 ms
    ///      2000      32.22 ms    18.53 (58%)       12.18 ms
    ///      4000      52.08 ms    38.83 (75%)       16.39 ms
    /// </code>
    /// <para>
    /// The right-hand column is flat, which is the claim. The middle one is not,
    /// and by four thousand strokes it is three quarters of what was being
    /// asserted constant — so the old ratio was headed for a permanent failure
    /// as paintings got bigger, and contention only decided which run got there
    /// first. Three consecutive trials of the old shape on an <em>idle</em> box
    /// read 1.45, 1.97 and 2.40 against its 2.5 ceiling.
    /// </para>
    /// <para>
    /// <b>The fingerprint is not now unwatched.</b> It is an absolute cost that
    /// grows linearly, so it belongs under an absolute ceiling rather than a
    /// constancy ratio — which is exactly what
    /// <see cref="OpeningACheckpointedPaintingIsNotPayingForItsHistory"/> is:
    /// 200 ms for the whole open, fingerprint included. A linear term is fine
    /// under a ceiling and can never be fine under "this shall not grow".
    /// </para>
    /// <para>
    /// <b>Why 1.5x.</b> Paired, this reads 0.83-1.05 idle and 0.84-0.89 with
    /// every core deliberately busy — a true 1.0 with noise either side. The
    /// failure it exists to catch is not marginal: with the checkpoint refused
    /// entirely, a 500-stroke open costs 1 835 ms against 15 ms, or 122x, and
    /// the mildest version — per-stroke work creeping into the blit — reads 4x
    /// for four times the record. 1.5 is well clear of the worst honest
    /// measurement and nowhere near the cheapest dishonest one.
    /// </para>
    /// </remarks>
    [Fact]
    public void ACheckpointedOpenCostsTheSameAtFourTimesTheHistory()
    {
        var small = Ready(500);
        var large = Ready(2000);

        // Alternated rather than one side and then the other: this is a ratio,
        // and two minima taken apart are two measurements of two machines.
        var (s, l) = Bench.PairedFastestMs(
            5,
            () => { using var bitmap = FrameRasterizer.Materialize(small.Frame, W, H, checkpoint: small.Resolved); },
            () => { using var bitmap = FrameRasterizer.Materialize(large.Frame, W, H, checkpoint: large.Resolved); });

        // The cost that legitimately grows, printed beside the one that must
        // not, so a reader can see where the rest of an open goes.
        var printSmall = Bench.FastestMs(3, () =>
            GC.KeepAlive(FrameCheckpoints.Usable(small.Doc, small.Frame)));
        var printLarge = Bench.FastestMs(3, () =>
            GC.KeepAlive(FrameCheckpoints.Usable(large.Doc, large.Frame)));

        o.WriteLine($"decode+blit   500 {s,8:F1} ms   2000 {l,8:F1} ms   ({l / s:F2}x for 4x the record)");
        o.WriteLine($"fingerprint   500 {printSmall,8:F1} ms   2000 {printLarge,8:F1} ms   "
            + $"({printLarge / printSmall:F2}x — linear on purpose, budgeted absolutely elsewhere)");

        Assert.True(
            l < s * 1.5,
            $"the render side of a checkpointed open grew {l / s:F2}x for four times the "
            + $"strokes ({s:F1} ms to {l:F1} ms) — something is walking the record again");
    }

    /// <summary>
    /// A painting with a checkpoint already rendered, and that checkpoint
    /// already resolved against it.
    /// </summary>
    /// <remarks>
    /// Resolving is <see cref="FrameCheckpoints.Usable"/>, and it is hoisted out
    /// here on purpose: it hashes the record, so leaving it inside a timed
    /// region is what B339 was.
    /// </remarks>
    private static (Frame Frame, Doc Doc, StrokeCheckpoint Resolved) Ready(int strokes)
    {
        var frame = Painting(strokes);
        var doc = DocumentFactory.CreateDoc(W, H);
        doc.Scene.Layers.Clear();
        doc.Scene.Layers.Add(new Layer { Cels = { new Cel { Frame = frame } } });
        frame.Checkpoint = FrameCheckpoints.Render(FrameCheckpoints.Plan(doc, frame)!);

        var resolved = FrameCheckpoints.Usable(doc, frame);
        Assert.NotNull(resolved);
        return (frame, doc, resolved!);
    }
}
