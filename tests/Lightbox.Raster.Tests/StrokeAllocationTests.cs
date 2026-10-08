using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What one committed stroke leaves for the garbage collector (playback phase 2a,
/// docs/DESIGN-playback-first-loop.md).
/// </summary>
/// <remarks>
/// <para>
/// Phase 1 found tile renders not scaling across cores: 1.6x on 15 workers, with
/// GC pause growing from 6 s to 35 s as workers were added. Most of the garbage
/// was per dab — a new SKPaint and a gradient built from two new arrays for every
/// soft dab — and per stroke on the media path, where four stroke-sized arrays
/// went onto the large-object heap.
/// </para>
/// <para>
/// Bytes on this thread, so the count is exact rather than sampled, and bytes per
/// dab so a longer mark cannot hide a per-dab regression. The ceilings sit
/// between what the engine allocated before the change and after it, both
/// printed. Measured on this test before the change: soft 840 and watercolour
/// 1528 bytes a dab; after it, 536 and 510. Each ceiling sits between the two.
/// </para>
/// </remarks>
public class StrokeAllocationTests(ITestOutputHelper output)
{
    private static readonly SKImageInfo Info = new(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul);

    private static Stroke Mark(BrushSettings brush) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#204060",
        Points = Enumerable.Range(0, 40)
            .Select(i => new StrokePoint(120 + i * 18, 400 + Math.Sin(i * 0.4) * 90, 0.4 + i % 3 * 0.3))
            .ToList(),
        Brush = brush,
    };

    private long BytesPerDab(BrushSettings brush, string name)
    {
        var stroke = Mark(brush);
        var dabs = BrushEngine.WalkDabs(stroke).Count;
        using var target = new SKBitmap(Info);
        using var canvas = new SKCanvas(target);
        BrushEngine.StampStroke(canvas, stroke, Info, target); // warm: JIT, per-thread buffers, pools
        var least = long.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            BrushEngine.StampStroke(canvas, stroke, Info, target);
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        output.WriteLine($"{name}: {dabs} dabs, {least / 1024.0:0.0} KB a stroke, {least / Math.Max(1, dabs)} bytes a dab");
        return least / Math.Max(1, dabs);
    }

    [Fact]
    public void ASoftDabDoesNotBuildAPaintAndAGradientOfItsOwn()
    {
        var soft = new BrushSettings { Size = 24, Hardness = 0.5, Opacity = 1, Flow = 0.8, Spacing = 0.1 };
        Assert.True(BytesPerDab(soft, "soft round") < 690);
    }

    [Fact]
    public void AWatercolourStrokeDoesNotPutItsGridsOnTheLargeObjectHeap()
    {
        var wash = new BrushSettings
        {
            Size = 30, Hardness = 0.4, Opacity = 1, Flow = 0.7, Spacing = 0.1,
            Medium = new MediumSettings { Kind = MediumKind.Watercolour },
        };
        Assert.True(BytesPerDab(wash, "watercolour") < 1000);
    }
}
