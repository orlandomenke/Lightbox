using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The silhouette the on-canvas cursor is drawn from (B74).
/// </summary>
/// <remarks>
/// <para>
/// The cursor was two concentric circles whatever the brush was, so a chisel, a
/// bristle or any imported tip was previewed as something it is not — and "how big
/// and what shape is my brush" is the only question the gizmo exists to answer.
/// </para>
/// <para>
/// <b>These tests are the substance of that fix, and they live here rather than in
/// the app on purpose.</b> The suite runs headless with Avalonia's software
/// drawing, so a rendered frame cannot be captured and the ring cannot be
/// inspected as pixels; turning real rendering on for 1370 App tests to reach one
/// gizmo would be a large risk for a small assertion. What decides whether the
/// preview is honest is the traced outline, and that is pure, exact and testable
/// here. The App side is plumbing, and is tested as plumbing.
/// </para>
/// <para>
/// Shapes are chosen the way <c>EffectBrushTipTests</c> chooses them: things no
/// circle and no falloff can imitate, so "it differs from a circle" cannot be
/// confused with "it is a smaller or softer circle".
/// </para>
/// </remarks>
[Collection("Registries")]
public class BrushTipOutlineTests(ITestOutputHelper output)
{
    /// <summary>A tip whose alpha is a rectangle, given a name nothing else uses.</summary>
    private static string RegisterBar(int w, int h, SKRect bar, string suffix)
    {
        var id = $"tip-outline-{suffix}";
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(bar, paint);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        BrushTipRegistry.Register(new Dictionary<string, string>
        {
            [id] = Convert.ToBase64String(data.AsSpan()),
        });
        BrushTipOutline.Clear();
        return id;
    }

    private static (double MinX, double MaxX, double MinY, double MaxY) Extent(
        IReadOnlyList<IReadOnlyList<Core.Documents.StrokePoint>> contours)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        foreach (var contour in contours)
        {
            foreach (var p in contour)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }
        }
        return (minX, maxX, minY, maxY);
    }

    /// <summary>
    /// A bar tip outlines as a bar: the full width, and only the bar's height.
    /// </summary>
    /// <remarks>
    /// The shape a circle cannot be mistaken for. If this returned something
    /// square-ish the outline would be tracing the bitmap rather than the alpha,
    /// and if it filled the height it would be tracing nothing and falling back to
    /// the bounds.
    /// </remarks>
    [Fact]
    public void ABarTipOutlinesAsABarAndNotAsACircle()
    {
        // 64 wide, and alpha only in rows 26..38 — a tenth of the height.
        var id = RegisterBar(64, 64, new SKRect(0, 26, 64, 38), "bar");

        var contours = BrushTipOutline.Of(id);
        Assert.NotNull(contours);
        var (minX, maxX, minY, maxY) = Extent(contours!);
        output.WriteLine($"bar outline spans x {minX:F3}..{maxX:F3}, y {minY:F3}..{maxY:F3}");

        // Unit space is [-0.5, 0.5] about the centre, so the full width reaches
        // both edges and the bar's 12 of 64 rows is 0.19 of the height.
        Assert.True(maxX - minX > 0.9, $"the outline is only {maxX - minX:F3} wide — it is not spanning the tip");
        Assert.True(
            maxY - minY < 0.3,
            $"the outline is {maxY - minY:F3} tall against a bar that occupies 0.19 of the tip — it is "
            + "outlining the bitmap's bounds rather than its alpha (B74)");
    }

    /// <summary>
    /// A non-square tip keeps its aspect, because the engine scales by the larger
    /// dimension.
    /// </summary>
    /// <remarks>
    /// <b>The mistake this exists to catch was in the first draft of the fix.</b>
    /// Normalising x by width and y by height is the obvious thing and it squares
    /// every non-square tip: <c>BrushEngine.StampDab</c> scales by
    /// <c>radius * 2 / Math.Max(tip.Width, tip.Height)</c> — one factor for both
    /// axes — so a 64×32 tip stamps at 2:1 and its long axis is what brush size
    /// means. Outlining it 1:1 would have drawn a ring of the right size around a
    /// mark of the wrong shape, which is the disagreement B74 already is.
    /// </remarks>
    [Fact]
    public void ANonSquareTipKeepsTheAspectTheEngineStampsItAt()
    {
        // Filled completely, so the outline is the tip's own rectangle and nothing
        // about the alpha shape can explain the result.
        var id = RegisterBar(64, 32, new SKRect(0, 0, 64, 32), "wide");

        var contours = BrushTipOutline.Of(id);
        Assert.NotNull(contours);
        var (minX, maxX, minY, maxY) = Extent(contours!);
        var aspect = (maxX - minX) / (maxY - minY);
        output.WriteLine($"64x32 tip outlines at {maxX - minX:F3} x {maxY - minY:F3} (aspect {aspect:F2})");

        Assert.True(
            Math.Abs(aspect - 2.0) < 0.25,
            $"a 64x32 tip outlined at aspect {aspect:F2} instead of 2 — the axes are being normalised "
            + "independently, so every non-square tip is previewed square");

        // And the long axis is the one that fills unit space, since that is the
        // one brush size refers to.
        Assert.True(maxX - minX > 0.9, $"the long axis spans only {maxX - minX:F3} of the unit box");
        Assert.True(maxY - minY < 0.6, $"the short axis spans {maxY - minY:F3}, which is not half");
    }

    /// <summary>
    /// A tip with a hole outlines with the hole, because a bristle comb is mostly
    /// holes.
    /// </summary>
    [Fact]
    public void AHollowTipKeepsItsHole()
    {
        var id = $"tip-outline-ring";
        using (var bmp = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Premul))
        {
            using (var canvas = new SKCanvas(bmp))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { Color = SKColors.White };
                canvas.DrawRect(new SKRect(8, 8, 56, 56), paint);
                using var hole = new SKPaint { Color = SKColors.Transparent, BlendMode = SKBlendMode.Src };
                canvas.DrawRect(new SKRect(22, 22, 42, 42), hole);
                canvas.Flush();
            }
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            BrushTipRegistry.Register(new Dictionary<string, string>
            {
                [id] = Convert.ToBase64String(data.AsSpan()),
            });
        }
        BrushTipOutline.Clear();

        var contours = BrushTipOutline.Of(id);
        Assert.NotNull(contours);
        output.WriteLine($"ring tip traced {contours!.Count} contours");
        Assert.True(
            contours.Count >= 2,
            $"a tip with a hole traced {contours.Count} contour(s) — a silhouette without its holes is a "
            + "blob claiming to be a brush");
    }

    /// <summary>
    /// No tip, an unknown tip and an empty tip all say null rather than guessing.
    /// </summary>
    /// <remarks>
    /// Null rather than a circle, so the cursor decides what "no tip" looks like —
    /// and it draws the ellipse the round dab genuinely is, which a circle returned
    /// from here would have hidden.
    /// </remarks>
    [Fact]
    public void NothingToOutlineIsNullRatherThanACircle()
    {
        Assert.Null(BrushTipOutline.Of(null));
        Assert.Null(BrushTipOutline.Of(""));
        Assert.Null(BrushTipOutline.Of("tip-outline-does-not-exist"));

        var blank = RegisterBar(64, 64, SKRect.Empty, "blank");
        Assert.Null(BrushTipOutline.Of(blank));
        // Asked twice: an empty answer has to be remembered, or a tip with nothing
        // in it is re-traced on every frame precisely because there is nothing to find.
        Assert.Null(BrushTipOutline.Of(blank));
    }

    /// <summary>
    /// Tracing happens once per tip, because the cursor is drawn on every pointer
    /// move.
    /// </summary>
    /// <remarks>
    /// Measured as a ratio between the first call and a thousand after it, both on
    /// this machine at this moment, so contention is a common factor that cancels —
    /// the lesson <c>IncrementalDensifyTests</c> paid for. What matters is the order
    /// of magnitude: a cache miss traces a few hundred thousand pixels, a hit is a
    /// dictionary lookup.
    /// </remarks>
    [Fact]
    [Trait("Category", "Performance")]
    public void TheTraceIsPaidOncePerTipAndNotPerFrame()
    {
        var id = RegisterBar(512, 512, new SKRect(40, 200, 472, 312), "big");

        var cold = System.Diagnostics.Stopwatch.StartNew();
        Assert.NotNull(BrushTipOutline.Of(id));
        cold.Stop();

        var warm = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) BrushTipOutline.Of(id);
        warm.Stop();

        var perHit = warm.Elapsed.TotalMilliseconds / 1000;
        output.WriteLine(
            $"first trace {cold.Elapsed.TotalMilliseconds:F3} ms, "
            + $"1000 hits {warm.Elapsed.TotalMilliseconds:F3} ms ({perHit:F5} ms each)");

        // Not vacuous: the cold trace has to be real work, or "the hit is cheaper"
        // would pass on an Of() that returns null.
        Assert.True(cold.Elapsed.TotalMilliseconds > 0.5,
            $"the first trace took {cold.Elapsed.TotalMilliseconds:F3} ms — it is not tracing anything");
        Assert.True(
            perHit * 50 < cold.Elapsed.TotalMilliseconds,
            $"a cached lookup costs {perHit:F5} ms against {cold.Elapsed.TotalMilliseconds:F3} ms to trace — "
            + "the outline is being re-traced, which at 60 Hz is a stall while the pointer moves");
    }
}
