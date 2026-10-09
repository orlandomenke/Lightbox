using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Lightbox.Testing;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The pencil before and after, as a sheet to look at: the old hard round
/// with a little granulation against the pressure-gated tooth, at a light,
/// a medium and a hard press, and along a pressure ramp. Written only when
/// <c>LIGHTBOX_VISUALS</c> names a directory; otherwise a no-op, so it costs
/// the suite nothing.
/// </summary>
public class PencilToothSheetTests
{
    private const int W = 420, H = 90;

    private static BrushSettings OldPencil() => new()
    {
        Size = 3, Hardness = 0.9, Opacity = 1, Flow = 0.85, Spacing = 0.12,
        Granulation = 0.15, PressureFlowGamma = 1,
    };

    private static BrushSettings NewPencil() => new()
    {
        Size = 4, Hardness = 0.7, Opacity = 1, Flow = 0.9, Spacing = 0.1,
        PressureFlowGamma = 0.8,
        TextureSurface = PaperKind.ColdPress, TextureScale = 2, TextureDepth = 0.8,
        TexturePressure = 0.8,
    };

    private static Stroke Wave(BrushSettings brush, Func<int, double> pressure) => new()
    {
        Color = "#1a1a1a",
        Points = [.. Enumerable.Range(0, 40).Select(i => new StrokePoint(20 + i * 9.5, 45 + Math.Sin(i * 0.35) * 22, pressure(i)))],
        Brush = brush,
    };

    private static SKBitmap Render(Stroke stroke)
    {
        using var raw = FrameRasterizer.Rasterize([stroke], W, H);
        using var flat = VisualSheet.OverPaper(raw);
        return VisualSheet.Zoom(flat, new SKRectI(0, 0, W, H), 2);
    }

    [Fact]
    public void ThePencilBeforeAndAfter()
    {
        if (!VisualSheet.Wanted) return;

        var panels = new List<VisualSheet.Panel>();
        foreach (var (label, p) in new[] { ("light 0.25", 0.25), ("medium 0.55", 0.55), ("hard 0.95", 0.95) })
        {
            panels.Add(new($"old pencil, {label}", Render(Wave(OldPencil(), _ => p))));
            panels.Add(new($"new pencil, {label}", Render(Wave(NewPencil(), _ => p))));
        }
        panels.Add(new("old pencil, ramp 0.15 → 1", Render(Wave(OldPencil(), i => 0.15 + 0.85 * i / 39.0))));
        panels.Add(new("new pencil, ramp 0.15 → 1", Render(Wave(NewPencil(), i => 0.15 + 0.85 * i / 39.0))));

        VisualSheet.Write(
            "pencil-tooth",
            "Pencil: a hard round with granulation (old) against graphite on a pressure-gated cold-press tooth (new), 2x",
            [.. panels]);
        foreach (var panel in panels) panel.Image.Dispose();
    }
}
