using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Growing the paper must not change the drawing — one property, asserted on
/// bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is one property test and not a dozen unit tests.</b> A document
/// rectangle is no longer <c>(0, 0, W, H)</c>: <see cref="Scene.OriginX"/> goes
/// negative when the canvas grows leftward, and every conversion from a
/// document coordinate to a pixel in a layer bitmap has to subtract it. There
/// are a dozen such conversions in <c>BrushEngine</c> and they are not alike —
/// canvas transforms, clamps, raw pixel spans, tiled shader phases. Asserting
/// each one separately means writing down the answer twice and being wrong in
/// the same direction both times.
/// </para>
/// <para>
/// So instead: render a document at origin zero, render the <em>same strokes at
/// the same coordinates</em> in a document grown leftward and upward, and
/// require the overlapping region to be <b>bit-identical</b>. Any site that
/// forgets the origin moves ink relative to the paper and fails this; any site
/// that seeds a dynamic from a shifted coordinate re-rolls its grain and fails
/// it too.
/// </para>
/// <para>
/// <b>The fixture is chosen to reach the sites that differ.</b> A paint stroke
/// with every jitter on covers the canvas-transform path and all ten
/// position-seeded dynamics. Granulation and a paper texture cover the two
/// effects whose noise is anchored to a rect rather than to a dab — they seed
/// a repeating field from <c>rect.Left/Top</c>, so they fail if that rect is
/// quietly converted to surface coordinates. Smudge and blur cover raw pixel
/// access, which bypasses the canvas transform altogether, and smudge is the
/// one that reads and writes spans while compositing through the canvas.
/// </para>
/// <para>
/// This is invariant 2 and Q61 stated as an executable sentence: <em>changes
/// the paper, the mark must not change.</em>
/// </para>
/// </remarks>
[Collection("Registries")]
public class DocumentOriginTests(ITestOutputHelper output)
{
    private const int W = 160, H = 120;

    /// <summary>How much paper is added on the left and on the top.</summary>
    /// <remarks>
    /// Deliberately not equal and deliberately odd, so a site that mixes the
    /// two axes up, or that happens to work for an even shift because a tile
    /// repeats every 8 px, still fails.
    /// </remarks>
    private const int PadX = 37, PadY = 11;

    private const string BarTip = "tip-origin-bar";

    /// <summary>
    /// A tip that is one off-centre bar, so rotation and roundness are visible
    /// in the pixels rather than only in a number.
    /// </summary>
    /// <remarks>
    /// <b>Needed for the smudge fixture to guard anything at all.</b>
    /// <c>DabShape</c> is where a smudge dab's rotation jitter is seeded, and
    /// its whole body is inert when the brush has no tip — <c>At</c> returns the
    /// falloff untouched and roundness only ever reaches the tip's sampling. A
    /// tipless smudge therefore passes whatever that seed is, which is how the
    /// first version of this file failed to notice the seed being wrong on
    /// purpose.
    /// </remarks>
    private static void RegisterBarTip()
    {
        using var bmp = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(new SKRect(0, 22, 64, 34), paint);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        BrushTipRegistry.Register(new Dictionary<string, string>
        {
            [BarTip] = Convert.ToBase64String(data.AsSpan()),
        });
    }

    private static List<StrokePoint> Path() =>
        [.. Enumerable.Range(0, 14).Select(
            i => new StrokePoint(22 + i * 9, 60 + Math.Sin(i * 0.6) * 28, 0.85))];

    /// <summary>Everything stochastic at once — the hardest case to keep stable.</summary>
    private static BrushSettings Jittery() => new()
    {
        Size = 20,
        Hardness = 0.8,
        Opacity = 1,
        Flow = 0.9,
        Spacing = 0.2,
        AntiAlias = true,
        Scatter = 0.35,
        SizeJitter = 0.5,
        MinimumDiameter = 0.3,
        FlowJitter = 0.4,
        RoundnessJitter = 0.3,
        RotationJitter = 0.5,
        ColorJitter = 0.4,
        SecondaryColor = "#c04020",
        HueJitter = 0.3,
        SaturationJitter = 0.3,
        BrightnessJitter = 0.3,
    };

    private static Stroke Paint(BrushSettings brush, string color = "#2050b0") => new()
    {
        Tool = ToolKind.Brush,
        Color = color,
        Points = Path(),
        Brush = brush,
    };

    /// <summary>
    /// The same strokes rendered into paper of two different sizes, aligned on
    /// the region they share.
    /// </summary>
    private static (SKBitmap Flush, SKBitmap Grown) BothWays(IReadOnlyList<Stroke> strokes)
    {
        var flush = FrameRasterizer.Rasterize(strokes, W, H);
        var grown = FrameRasterizer.Rasterize(
            strokes, W + PadX, H + PadY, origin: new SKPointI(-PadX, -PadY));
        return (flush, grown);
    }

    /// <summary>
    /// Compare the shared region byte for byte, and report what differs rather
    /// than only that something does.
    /// </summary>
    private void AssertSameInk(SKBitmap flush, SKBitmap grown, string what)
    {
        Assert.Equal(W, flush.Width);
        Assert.Equal(W + PadX, grown.Width);

        var differing = 0;
        var firstAt = (X: -1, Y: -1);
        SKColor firstFlush = default, firstGrown = default;
        var inked = 0;

        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var a = flush.GetPixel(x, y);
                var b = grown.GetPixel(x + PadX, y + PadY);
                if (a.Alpha > 0) inked++;
                if (a == b) continue;
                if (differing == 0)
                {
                    firstAt = (x, y);
                    firstFlush = a;
                    firstGrown = b;
                }
                differing++;
            }
        }

        output.WriteLine(
            $"{what}: {inked} inked px of {W * H}, {differing} differ" +
            (differing == 0
                ? ""
                : $" — first at ({firstAt.X},{firstAt.Y}): flush {firstFlush} vs grown {firstGrown}"));

        // A fixture that painted nothing would pass the comparison and prove
        // nothing, which is the failure mode a bare `differing == 0` invites.
        Assert.True(inked > 200, $"{what}: fixture painted almost nothing ({inked} px)");
        Assert.Equal(0, differing);
    }

    // ---- the canvas-transform path ------------------------------------------------------

    [Fact]
    public void GrowingThePaperMovesNoInkAndReRollsNoJitter()
    {
        var (flush, grown) = BothWays([Paint(Jittery())]);
        using (flush)
        using (grown) AssertSameInk(flush, grown, "jittery paint");
    }

    /// <summary>
    /// Granulation and a paper texture anchor a <em>repeating field</em> to the
    /// stroke's rect, not to each dab.
    /// </summary>
    /// <remarks>
    /// So they are the two effects that a surface-coordinate rect breaks
    /// silently: the mark keeps its shape and the paper underneath it slides,
    /// which is a re-grain of exactly the kind invariant 2 forbids and which no
    /// per-dab assertion would catch.
    /// </remarks>
    [Fact]
    public void TheGrainOfThePaperDoesNotSlideWhenThePaperGrows()
    {
        var brush = Jittery();
        brush.Granulation = 0.8;
        var (flush, grown) = BothWays([Paint(brush)]);
        using (flush)
        using (grown) AssertSameInk(flush, grown, "granulation");
    }

    [Fact]
    public void APaperTextureIsAnchoredToTheDocumentRatherThanToTheSurface()
    {
        var brush = Jittery();
        brush.TextureSurface = PaperKind.ColdPress;
        brush.TextureScale = 1.0;
        var (flush, grown) = BothWays([Paint(brush)]);
        using (flush)
        using (grown) AssertSameInk(flush, grown, "paper texture");
    }

    // ---- raw pixel access, which bypasses the canvas transform ---------------------------

    /// <summary>
    /// Smudge is the awkward one: it reads and writes the layer through raw
    /// pixel spans, composites the patch through the canvas, and seeds its
    /// roundness and rotation from the same position it indexes with.
    /// </summary>
    /// <remarks>
    /// Those two uses have to be split — document coordinate for the
    /// <c>Hash01</c> seed, surface coordinate for the span — and getting it
    /// wrong re-grains every smudge. This is the test that notices.
    /// </remarks>
    [Fact]
    public void ASmudgeReadsTheSamePixelsWhateverThePaperIsCalled()
    {
        RegisterBarTip();
        var under = Paint(Jittery());
        var smudge = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [.. Enumerable.Range(0, 10).Select(
                i => new StrokePoint(30 + i * 11, 55 + i * 2, 0.9))],
            Brush = new BrushSettings
            {
                Kind = BrushKind.Smudge,
                Size = 26,
                Hardness = 0.6,
                Opacity = 1,
                Flow = 1,
                Spacing = 0.12,
                // The tip is what makes the two jitters below reach pixels at
                // all — see RegisterBarTip.
                TipId = BarTip,
                RoundnessJitter = 0.3,
                RotationJitter = 0.5,
            },
        };

        var (flush, grown) = BothWays([under, smudge]);
        using (flush)
        using (grown) AssertSameInk(flush, grown, "smudge");
    }

    [Fact]
    public void ABlurReadsTheSamePixelsWhateverThePaperIsCalled()
    {
        var under = Paint(Jittery());
        var blur = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Points = [.. Enumerable.Range(0, 8).Select(
                i => new StrokePoint(35 + i * 13, 70 - i * 3, 0.9))],
            Brush = new BrushSettings
            {
                Kind = BrushKind.Blur,
                Size = 24,
                Hardness = 0.7,
                Opacity = 1,
                Flow = 1,
                Spacing = 0.15,
            },
        };

        var (flush, grown) = BothWays([under, blur]);
        using (flush)
        using (grown) AssertSameInk(flush, grown, "blur");
    }

    // ---- the two tools that are not dabs at all -----------------------------------------

    [Fact]
    public void AFillLandsOnTheSamePixelsWhateverThePaperIsCalled()
    {
        var outline = Paint(new BrushSettings { Size = 6, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1 });
        var fill = new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#20a060",
            // A fill's own Points ARE its outer contour.
            Points = [.. new[] { (30.0, 20.0), (120.0, 20.0), (120.0, 95.0), (30.0, 95.0) }
                .Select(p => new StrokePoint(p.Item1, p.Item2, 1))],
            Brush = new BrushSettings { Size = 1, Opacity = 1, Flow = 1 },
        };

        var (flush, grown) = BothWays([outline, fill]);
        using (flush)
        using (grown) AssertSameInk(flush, grown, "fill");
    }

    // ---- the new paper itself ------------------------------------------------------------

    /// <summary>
    /// The half the overlap comparison cannot see: a stroke reaching into paper
    /// that only exists in the grown document has to actually be drawn there.
    /// </summary>
    /// <remarks>
    /// Without this, clamping the stroke bounds to <c>(0, 0, W, H)</c> instead
    /// of to the document rect passes every assertion above — it throws away
    /// only the half of the stroke sitting in the new margin, which the shared
    /// region does not contain. That is the exact bug the roadmap names, and it
    /// would otherwise ship green.
    /// </remarks>
    [Fact]
    public void AStrokeInTheNewPaperIsDrawnRatherThanClippedAway()
    {
        var brush = new BrushSettings { Size = 18, Hardness = 0.9, Opacity = 1, Flow = 1, Spacing = 0.15 };
        // Entirely inside the added left margin: every x is negative, so this
        // stroke does not exist at all in the flush document.
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#b03030",
            Points = [.. Enumerable.Range(0, 6).Select(
                i => new StrokePoint(-30 + i * 3, 40 + i * 6, 0.9))],
            Brush = brush,
        };

        using var grown = FrameRasterizer.Rasterize(
            [stroke], W + PadX, H + PadY, origin: new SKPointI(-PadX, -PadY));

        var inked = 0;
        for (var y = 0; y < grown.Height; y++)
        {
            for (var x = 0; x < grown.Width; x++)
            {
                if (grown.GetPixel(x, y).Alpha > 0) inked++;
            }
        }

        output.WriteLine($"stroke in the new margin: {inked} inked px");
        Assert.True(inked > 200, $"the stroke in the added paper was clipped away ({inked} px)");
    }
}
