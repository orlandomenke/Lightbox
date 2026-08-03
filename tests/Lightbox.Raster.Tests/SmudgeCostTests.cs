using System.Diagnostics;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The smudge deposit is a pixel loop, so it gets a budget.
/// </summary>
/// <remarks>
/// B38 replaced the smudge's per-dab draw with an explicit lerp, because no
/// Skia blend mode expresses "move the canvas toward this colour, alpha
/// included". A managed loop over pixels is exactly the shape this engine has
/// been burned by, so it is measured rather than assumed.
///
/// The first cut used <c>GetPixel</c>/<c>SetPixel</c> — a P/Invoke each — and
/// cost **10,810 ms** for one 4K stroke. Spans over <c>PeekPixels</c> brought
/// it to **273 ms**, a 40x difference, which is the same lesson
/// <c>DESIGN-performance.md</c> records from the first time round. Budgets are
/// set loose, per the charter: they catch an order of magnitude, not drift.
/// </remarks>
[Trait("Category", "Performance")]
public class SmudgeCostTests(ITestOutputHelper o)
{
    [Theory]
    [InlineData(960, 540, 40, 200)]
    [InlineData(3840, 2160, 180, 2000)]
    public void AWholeSmudgeStrokeStaysAffordable(int w, int h, double size, int budgetMs)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        using (var p = new SKPaint { Color = new SKColor(0, 0, 0, 60) })
        { c.Clear(SKColors.Transparent); c.DrawRect(new SKRect(0, h / 3f, w, 2 * h / 3f), p); c.Flush(); }

        var stroke = new Stroke
        {
            Color = "#000000",
            Brush = new BrushSettings { Kind = BrushKind.Smudge, Size = size, Hardness = 0.5, Flow = 0.6, Spacing = 0.1 },
            Points = Enumerable.Range(0, 80).Select(i => new StrokePoint(w * 0.1 + i * w * 0.01, h / 2.0, 0.8)).ToList(),
        };

        FrameRasterizer.Append(bmp, stroke); // warm
        var sw = Stopwatch.StartNew();
        FrameRasterizer.Append(bmp, stroke);
        var ms = sw.Elapsed.TotalMilliseconds;
        bmp.Dispose();

        // Both numbers, always.
        o.WriteLine($"{w}x{h}, size {size}: whole smudge stroke {ms:F0} ms of {budgetMs} ms");
        Assert.True(ms < budgetMs, $"a smudge stroke at {w}x{h} size {size} took {ms:F0} ms, budget {budgetMs} ms");
    }
}
