using Lightbox.Core.Documents;
using Lightbox.Raster;
using Lightbox.Testing;
using SkiaSharp;
namespace Lightbox.Raster.Tests;
public class ZzOilSheet
{
    private const int W = 420, H = 110;
    private static BrushSettings Oil(Action<BrushSettings>? tweak = null)
    {
        var b = new BrushSettings
        {
            Size = 34, Hardness = 0.6, Opacity = 1, Flow = 0.9, Spacing = 0.06,
            TipId = "tip-builtin-bristle", AngleFollowsDirection = true, SizeJitter = 0.08, RotationJitter = 0.04,
            Mixing = new ColourMixing { Amount = 0.7, Length = 0.25, Reach = 0.9 },
            Medium = new MediumSettings
            {
                Kind = MediumKind.Oil, Wetness = 0.2, Viscosity = 0.9, Drag = 0.85, FlowSteps = 4,
                Absorbency = 0.9, EdgePull = 0.02, PigmentDensity = 1, Granularity = 0.1, Hiding = 0.95,
                Paper = PaperKind.Canvas, PaperScale = 8, PaperInfluence = 0.6, Body = 0.8, Relief = 0.6, PaintLoad = 0.55,
            },
        };
        tweak?.Invoke(b); return b;
    }
    private static Stroke Wave(BrushSettings brush, string colour = "#c8401c") => new()
    {
        Color = colour,
        Points = [.. Enumerable.Range(0, 30).Select(i => new StrokePoint(30 + i * 12.5, 55 + Math.Sin(i * 0.4) * 18, 0.5 + 0.45 * Math.Sin(i * 0.25)))],
        Brush = brush,
    };
    private static SKBitmap Render(params Stroke[] strokes)
    {
        using var raw = FrameRasterizer.Rasterize(strokes, W, H);
        using var flat = VisualSheet.OverPaper(raw);
        return VisualSheet.Zoom(flat, new SKRectI(0, 0, W, H), 2);
    }
    [Fact]
    public void Sheet()
    {
        if (!VisualSheet.Wanted) return;
        var blue = new Stroke { Color = "#2040c0", Points = [new StrokePoint(60, 20, 1), new StrokePoint(360, 95, 1)], Brush = Oil(b => { b.Mixing = null; b.Size = 30; }) };
        var panels = new List<VisualSheet.Panel>
        {
            new("Oil, shipped", Render(Wave(Oil()))),
            new("Oil, over a blue stroke", Render(blue, Wave(Oil()))),
            new("Oil, flow steps 0", Render(Wave(Oil(b => b.Medium.FlowSteps = 0)))),
            new("Oil, medium off (tip only)", Render(Wave(Oil(b => b.Medium.Kind = MediumKind.None)))),
            new("Oil, body 0 relief 0", Render(Wave(Oil(b => { b.Medium.Body = 0; b.Medium.Relief = 0; })))),
            new("Oil, relief 1 body 1", Render(Wave(Oil(b => { b.Medium.Body = 1; b.Medium.Relief = 1; })))),
        };
        VisualSheet.Write("oil-today", "Oil as shipped, 2x: what the bristle tip and the body shading give today", [.. panels]);
        foreach (var p in panels) p.Image.Dispose();
    }
}
