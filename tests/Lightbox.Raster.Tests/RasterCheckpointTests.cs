using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B30's raster checkpoint: the shortcut must be a shortcut and never an
/// answer of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>One test here matters more than the rest and it is the bit-equality
/// one.</b> Everything else in this file is a refusal — cases where the
/// checkpoint is not used — and a refusal that goes wrong costs a slow open.
/// Bit-equality is the one that says the cache has not become the truth: if a
/// checkpointed render and a replayed render ever differ, invariant 1 has been
/// inverted and the document is no longer the strokes.
/// </para>
/// <para>
/// The fixtures paint a <b>curve</b> rather than a straight line, per the
/// charter's O8: a straight stroke is the one case where a mistake in the dab
/// walk costs nothing, so it is the one shape that cannot see a whole class of
/// defect.
/// </para>
/// </remarks>
public class RasterCheckpointTests(ITestOutputHelper o)
{
    private const int W = 480, H = 320;

    /// <summary>A painterly drawing of <paramref name="strokes"/> curved marks.</summary>
    private static Frame Painting(int strokes, int seed = 7, double size = 24)
    {
        var frame = new Frame();
        for (var s = 0; s < strokes; s++)
        {
            var a = (s * 2654435761u + (uint)seed * 40503u) % 10007;
            var b = (s * 1597334677u + (uint)seed * 22571u) % 10009;
            var x = 40 + a % (uint)(W - 80);
            var y = 40 + b % (uint)(H - 80);

            // A bend and the corner between two of them — a straight stroke
            // cannot see an error in how the dab walk cuts a corner (O8).
            var points = new List<StrokePoint>();
            for (var i = 0; i < 7; i++)
            {
                var t = i / 6.0;
                points.Add(new StrokePoint(
                    x + Math.Sin(t * Math.PI) * 34 + t * 18,
                    y + Math.Cos(t * Math.PI * 1.5) * 26,
                    0.35 + (i % 4) * 0.18));
            }

            frame.Strokes.Add(new Stroke
            {
                Tool = ToolKind.Brush,
                Color = $"#{a % 200 + 40:x2}{b % 200 + 40:x2}{(a + b) % 200 + 40:x2}",
                Brush = new BrushSettings
                {
                    Size = size, Hardness = 0.3, Opacity = 0.8, Flow = 0.5, Spacing = 0.05,
                },
                Points = points,
            });
        }
        return frame;
    }

    private static Doc DocFor(Frame frame, int width = W, int height = H)
    {
        var doc = DocumentFactory.CreateDoc(width, height);
        doc.Scene.Layers.Clear();
        doc.Scene.Layers.Add(new Layer { Cels = { new Cel { Frame = frame } } });
        return doc;
    }

    /// <summary>Render with a checkpoint if the document has a usable one.</summary>
    private static SKBitmap Open(Doc doc, Frame frame, double outputScale = 1.0) =>
        FrameRasterizer.Materialize(
            frame, doc.Scene.Width, doc.Scene.Height, outputScale,
            checkpoint: FrameCheckpoints.Usable(doc, frame));

    private static byte[] Bytes(SKBitmap b) => b.GetPixelSpan().ToArray();

    /// <summary>Give this drawing a checkpoint covering everything it has now.</summary>
    private static StrokeCheckpoint Checkpoint(Doc doc, Frame frame)
    {
        var plan = FrameCheckpoints.Plan(doc, frame);
        Assert.NotNull(plan);
        var made = FrameCheckpoints.Render(plan!);
        Assert.NotNull(made);
        frame.Checkpoint = made;
        return made!;
    }

    // ---- the one that matters ------------------------------------------------

    /// <summary>
    /// A checkpointed render and a replayed render are the same bytes.
    /// </summary>
    /// <remarks>
    /// <b>The property the whole design rests on</b>, and stated in
    /// <c>DESIGN-raster-checkpoint.md</c> as "render with and without, compare
    /// bytes — if that ever fails the cache has become the source of truth and
    /// invariant 1 is gone". Every other test in this file is a refusal, and a
    /// refusal that goes wrong costs a slow open; this one is the one that
    /// costs an artist a drawing they did not make.
    /// </remarks>
    [Fact]
    public void ACheckpointedRenderIsBitIdenticalToAReplay()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocFor(frame);

        using var replayed = Open(doc, frame);
        Checkpoint(doc, frame);
        using var shortcut = Open(doc, frame);

        Assert.Equal(FrameCheckpoints.MinStrokes, frame.Checkpoint!.Strokes);
        AssertSameBytes(replayed, shortcut);
    }

    /// <summary>
    /// The same, with strokes painted after the checkpoint was taken.
    /// </summary>
    /// <remarks>
    /// The case the feature actually lives in: painting appends, so an artist's
    /// document is almost always "a checkpoint plus a tail". The tail is stamped
    /// onto the stored pixels, which is the same thing the replay does to its
    /// own partial render, and the two must land in the same place.
    /// </remarks>
    [Fact]
    public void PaintingOnTopOfACheckpointLandsWhereAReplayWouldHave()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocFor(frame);
        Checkpoint(doc, frame);

        foreach (var stroke in Painting(12, seed: 99).Strokes) frame.Strokes.Add(stroke);

        using var shortcut = Open(doc, frame);
        var stored = frame.Checkpoint;
        frame.Checkpoint = null;
        using var replayed = Open(doc, frame);
        frame.Checkpoint = stored;

        Assert.Equal(FrameCheckpoints.MinStrokes, stored!.Strokes);
        Assert.Equal(FrameCheckpoints.MinStrokes + 12, frame.Strokes.Count);
        AssertSameBytes(replayed, shortcut);
    }

    /// <summary>Deleting every checkpoint changes no pixel.</summary>
    [Fact]
    public void DroppingTheCheckpointChangesNothingButTheTimeItTakes()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocFor(frame);
        Checkpoint(doc, frame);

        using var with = Open(doc, frame);
        frame.Checkpoint = null;
        using var without = Open(doc, frame);

        AssertSameBytes(without, with);
    }

    // ---- the codec -----------------------------------------------------------

    /// <summary>
    /// The pixels come back exactly, premultiplied alpha included.
    /// </summary>
    /// <remarks>
    /// The narrow version of <see cref="EveryLegalPremultipliedValueSurvives"/>,
    /// on real art rather than on a sweep: this is the one that would have
    /// caught a codec that was exact on synthetic values and wrong on a soft
    /// brush's edges.
    /// </remarks>
    [Fact]
    public void TheCodecCarriesPremultipliedPixelsWithoutTouchingThem()
    {
        using var render = FrameRasterizer.Rasterize(Painting(60).Strokes, W, H);
        var encoded = CheckpointCodec.Encode(render);
        Assert.NotNull(encoded);

        using var back = CheckpointCodec.Decode(encoded!, W, H);
        Assert.NotNull(back);
        Assert.Equal(SKAlphaType.Premul, back!.Info.AlphaType);
        AssertSameBytes(render, back);
    }

    /// <summary>
    /// Every legal premultiplied byte pair survives the round trip.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A sweep rather than a sample, because the failure this guards against
    /// lives at the ends.</b> Premultiplied channels are bounded by their alpha,
    /// so the dangerous values — where an unpremultiply would have to divide by
    /// something close to zero — are a vanishing share of any real drawing and
    /// would pass a fixture-based test by never being hit. This walks all 65 536
    /// of them.
    /// </para>
    /// <para>
    /// <b>It also records what the measurement corrected.</b> The codec was built
    /// believing a plain PNG round trip lost precision at low alpha; it does not,
    /// and this is what says so. What was really wrong was reading the decoder's
    /// own choice of channel order — see <c>CheckpointCodec</c>. The reason this
    /// route is still the one used is that matching alpha types on both sides
    /// gives the encoder and decoder nothing to convert by the format's own
    /// definition, rather than by an implementation detail that could be
    /// "fixed" one day.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryLegalPremultipliedValueSurvives()
    {
        const int N = 256;
        var info = new SKImageInfo(N, N, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var source = new SKBitmap(info);
        var bytes = new byte[N * N * 4];
        for (var alpha = 0; alpha < N; alpha++)
        {
            for (var channel = 0; channel < N; channel++)
            {
                var i = (alpha * N + channel) * 4;
                // Premultiplied means no channel may exceed its alpha; the
                // second channel runs the other way so the sweep covers both
                // ends of the range at every alpha rather than one diagonal.
                bytes[i] = (byte)Math.Min(channel, alpha);
                bytes[i + 1] = (byte)Math.Min(255 - channel, alpha);
                bytes[i + 2] = (byte)Math.Min(channel / 2, alpha);
                bytes[i + 3] = (byte)alpha;
            }
        }
        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, source.GetPixels(), bytes.Length);

        using var back = CheckpointCodec.Decode(CheckpointCodec.Encode(source)!, N, N);
        Assert.NotNull(back);
        AssertSameBytes(source, back!);
    }

    /// <summary>Pixels of the wrong shape are refused rather than stretched.</summary>
    [Fact]
    public void ACheckpointFromADifferentlySizedCanvasIsRefused()
    {
        using var render = FrameRasterizer.Rasterize(Painting(20).Strokes, W, H);
        var encoded = CheckpointCodec.Encode(render)!;
        Assert.Null(CheckpointCodec.Decode(encoded, W + 8, H));
    }

    /// <summary>Rubbish where the pixels should be is a slow open, not a crash.</summary>
    /// <remarks>
    /// B137's rule, which was filed for an unguarded decode on exactly this path.
    /// The whole record is replayed and the drawing is correct; only the
    /// shortcut is lost.
    /// </remarks>
    [Fact]
    public void PixelsThatWillNotDecodeFallBackToTheRecord()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocFor(frame);
        using var replayed = Open(doc, frame);

        var made = Checkpoint(doc, frame);
        made.PixelsBase64 = Convert.ToBase64String("not a png"u8.ToArray());
        // Still "usable" as far as the record is concerned — the fingerprint is
        // untouched — so this is the render path's guard being exercised, not
        // the fingerprint's.
        Assert.NotNull(FrameCheckpoints.Usable(doc, frame));

        using var opened = Open(doc, frame);
        AssertSameBytes(replayed, opened);
    }

    // ---- refusals ------------------------------------------------------------

    /// <summary>
    /// Above 1× the strokes are re-rasterised, never the stored image magnified.
    /// </summary>
    /// <remarks>
    /// Invariant 7 at the checkpoint's expense, and the trade the whole
    /// geometry-as-truth bet is for: Harmony caches a bitmap per textured stroke
    /// and its own manual warns those strokes pixelate when zoomed. Blowing up a
    /// checkpoint would buy the speed by giving up exactly that.
    /// </remarks>
    [Fact]
    public void AtTwiceTheOutputScaleTheCheckpointIsIgnored()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocFor(frame);
        Checkpoint(doc, frame);

        using var shortcut = Open(doc, frame, outputScale: 2.0);
        frame.Checkpoint = null;
        using var replayed = Open(doc, frame, outputScale: 2.0);

        Assert.Equal(W * 2, shortcut.Width);
        AssertSameBytes(replayed, shortcut);
    }

    [Fact]
    public void ASmallDrawingIsNotWorthAnImage()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes - 1);
        Assert.False(FrameCheckpoints.CanCheckpoint(frame));
        Assert.Null(FrameCheckpoints.Plan(DocFor(frame), frame));
    }

    [Fact]
    public void ADrawingWithImportedPixelsUnderItIsRefused()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        using var imported = FrameRasterizer.Rasterize(Painting(3, seed: 4).Strokes, W, H);
        frame.PngBase64 = PngCodec.Encode(imported);
        Assert.False(FrameCheckpoints.CanCheckpoint(frame));
    }

    /// <summary>
    /// A hand-made file carrying both a baseline and a checkpoint replays.
    /// </summary>
    /// <remarks>
    /// The application cannot produce this — <see cref="FrameCheckpoints.CanCheckpoint"/>
    /// refuses a frame with a baseline — so it is a guard rather than a case. It
    /// exists because the failure it prevents is the one ordering the render path
    /// can get wrong on its own: the baseline is drawn after the checkpoint and
    /// would cover it.
    /// </remarks>
    [Fact]
    public void ADrawingCarryingBothABaselineAndACheckpointReplaysInstead()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocFor(frame);
        Checkpoint(doc, frame);

        using var imported = FrameRasterizer.Rasterize(Painting(4, seed: 21).Strokes, W, H);
        frame.PngBase64 = PngCodec.Encode(imported);

        using var opened = Open(doc, frame);
        var stored = frame.Checkpoint;
        frame.Checkpoint = null;
        using var replayed = Open(doc, frame);
        frame.Checkpoint = stored;

        AssertSameBytes(replayed, opened);
    }

    [Fact]
    public void ADrawingThatSamplesTheLayersBeneathItLiveIsRefused()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        frame.Strokes[3].Brush.SampleSource = SampleSource.AllLayersLive;
        Assert.False(FrameCheckpoints.CanCheckpoint(frame));
    }

    /// <summary>
    /// A rigged drawing renders differently per timeline position, so it gets no
    /// checkpoint — one would nail it to whichever pose was showing at save.
    /// </summary>
    [Fact]
    public void ARigBoundDrawingIsRefused()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        frame.Strokes[0].Weights = [new BoneBinding { BoneId = "bone" }];
        Assert.True(frame.HasBoundStrokes);
        Assert.False(FrameCheckpoints.CanCheckpoint(frame));
    }

    /// <summary>
    /// A drawing whose checkpoint is already current plans nothing — the render
    /// would spend a worker to produce bytes that are already there.
    /// </summary>
    [Fact]
    public void ACurrentCheckpointIsNotRenderedAgain()
    {
        var frame = Painting(FrameCheckpoints.MinStrokes);
        var doc = DocFor(frame);
        Checkpoint(doc, frame);
        Assert.Null(FrameCheckpoints.Plan(doc, frame));

        frame.Strokes.Add(Painting(1, seed: 41).Strokes[0]);
        Assert.NotNull(FrameCheckpoints.Plan(doc, frame));
    }

    private void AssertSameBytes(SKBitmap expected, SKBitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var a = Bytes(expected);
        var b = Bytes(actual);
        if (a.AsSpan().SequenceEqual(b)) return;

        int differing = 0, worst = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var d = Math.Abs(a[i] - b[i]);
            if (d > 0) differing++;
            if (d > worst) worst = d;
        }
        // Printed rather than only failed: "how far off" is the number that says
        // whether this is a codec losing precision or a render losing a stroke.
        o.WriteLine($"{differing:N0} of {a.Length:N0} bytes differ, worst channel error {worst}");
        Assert.Fail($"{differing:N0} bytes differ, worst {worst}");
    }
}
