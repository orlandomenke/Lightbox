using System.Security.Cryptography;
using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.Raster.Tests;

public class FloodFillTests
{
    private static SKBitmap Canvas(int w = 64, int h = 64)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);
        return bmp;
    }

    /// <summary>Draw an unfilled rectangle outline of the given wall thickness.</summary>
    private static void Outline(SKBitmap bmp, SKRectI rect, int wall, SKColor color)
    {
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var onWall = x < rect.Left + wall || x >= rect.Right - wall
                          || y < rect.Top + wall || y >= rect.Bottom - wall;
                if (onWall) bmp.SetPixel(x, y, color);
            }
        }
    }

    [Fact]
    public void Fill_StopsAtBarriers_WithinTolerance_AndCrossesThemBeyondIt()
    {
        using var bmp = Canvas();
        // right half is a mid-grey wash; left half stays empty
        for (var y = 0; y < 64; y++)
        {
            for (var x = 32; x < 64; x++) bmp.SetPixel(x, y, new SKColor(128, 128, 128, 255));
        }

        var strict = FloodFill.Fill(bmp, 4, 4, new FloodFill.Options(Tolerance: 32));
        Assert.NotNull(strict);
        Assert.Equal(32 * 64, strict!.Area); // only the empty left half

        var loose = FloodFill.Fill(bmp, 4, 4, new FloodFill.Options(Tolerance: 255));
        Assert.NotNull(loose);
        Assert.Equal(64 * 64, loose!.Area); // tolerance 255 sees no barrier at all
    }

    [Fact]
    public void Fill_TracesInnerContoursAsHoles()
    {
        using var bmp = Canvas();
        // a painted ring: filling the ring itself must report the empty middle as a hole
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false })
        {
            canvas.DrawCircle(32, 32, 24, paint);
            paint.Color = SKColors.Transparent;
            paint.BlendMode = SKBlendMode.Src;
            canvas.DrawCircle(32, 32, 10, paint);
        }

        var result = FloodFill.Fill(bmp, 32, 10, new FloodFill.Options(Tolerance: 32));
        Assert.NotNull(result);
        Assert.Single(result!.Holes);
        Assert.True(result.Outer.Count >= 8);
    }

    [Fact]
    public void GapClosing_SealsSmallOpenings_ButNotLargerOnes()
    {
        using var bmp = Canvas();
        Outline(bmp, new SKRectI(8, 8, 56, 56), 2, SKColors.Black);
        // cut a 4px opening in the top wall
        for (var y = 8; y < 10; y++)
        {
            for (var x = 30; x < 34; x++) bmp.SetPixel(x, y, SKColors.Transparent);
        }
        var inside = 44 * 44; // interior of the outline

        var open = FloodFill.Fill(bmp, 32, 32, new FloodFill.Options(Tolerance: 32, GapPx: 0));
        Assert.True(open!.Area > inside + 500, "without gap closing the fill must leak out");

        var closed = FloodFill.Fill(bmp, 32, 32, new FloodFill.Options(Tolerance: 32, GapPx: 4));
        Assert.InRange(closed!.Area, inside / 2, inside + 64); // stays inside (small tolerance for the gap mouth)

        // a 12px opening is a real doorway — GapPx 4 must NOT seal it
        for (var y = 8; y < 10; y++)
        {
            for (var x = 26; x < 38; x++) bmp.SetPixel(x, y, SKColors.Transparent);
        }
        var doorway = FloodFill.Fill(bmp, 32, 32, new FloodFill.Options(Tolerance: 32, GapPx: 4));
        Assert.True(doorway!.Area > inside + 500, "a wide opening must still leak");
    }

    [Fact]
    public void GrowAndShrink_OverfillAndUnderfillTheRegion()
    {
        using var bmp = Canvas();
        Outline(bmp, new SKRectI(8, 8, 56, 56), 3, SKColors.Black);

        var plain = FloodFill.Fill(bmp, 32, 32, new FloodFill.Options(Tolerance: 32))!;
        var grown = FloodFill.Fill(bmp, 32, 32, new FloodFill.Options(Tolerance: 32, GrowPx: 2))!;
        var shrunk = FloodFill.Fill(bmp, 32, 32, new FloodFill.Options(Tolerance: 32, GrowPx: -2))!;

        Assert.True(grown.Area > plain.Area, "grow must overfill under the line");
        Assert.True(shrunk.Area < plain.Area, "shrink must leave a margin");
    }

    [Fact]
    public void Fill_IsDeterministic()
    {
        using var bmp = Canvas();
        Outline(bmp, new SKRectI(4, 4, 60, 60), 2, SKColors.Black);

        var a = FloodFill.Fill(bmp, 30, 30, new FloodFill.Options(Tolerance: 32, GapPx: 2, GrowPx: 1))!;
        var b = FloodFill.Fill(bmp, 30, 30, new FloodFill.Options(Tolerance: 32, GapPx: 2, GrowPx: 1))!;
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
    }

    [Fact]
    public void Fill_StaysInsideASelectionMask()
    {
        using var bmp = Canvas();
        var selection = new bool[64 * 64];
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 28; x++) selection[y * 64 + x] = true; // left strip only
        }

        var result = FloodFill.Fill(bmp, 4, 4, new FloodFill.Options(Tolerance: 32), selection)!;
        Assert.Equal(28 * 64, result.Area);
        Assert.All(result.Outer, p => Assert.InRange(p.X, 0, 28.5));
    }
}

public class FillStrokeTests
{
    private static string Hash(SKBitmap bmp) => Convert.ToHexString(SHA256.HashData(bmp.GetPixelSpan()));

    private static Stroke Square(double left, double top, double size, string color = "#cc3333") => new()
    {
        Tool = ToolKind.Fill,
        Color = color,
        Brush = new BrushSettings { Opacity = 1 },
        Points =
        [
            new(left, top, 1), new(left + size, top, 1),
            new(left + size, top + size, 1), new(left, top + size, 1),
        ],
        Label = "fill",
    };

    [Fact]
    public void FillStroke_RendersItsRegion_WithHolesLeftEmpty()
    {
        var fill = Square(10, 10, 40);
        fill.Holes = [[new(22, 22, 1), new(38, 22, 1), new(38, 38, 1), new(22, 38, 1)]];

        using var bmp = FrameRasterizer.Rasterize([fill], 64, 64);
        Assert.True(bmp.GetPixel(15, 15).Alpha > 200);  // filled band
        Assert.Equal(0, bmp.GetPixel(30, 30).Alpha);    // inside the hole
        Assert.Equal(0, bmp.GetPixel(55, 55).Alpha);    // outside the region
    }

    [Fact]
    public void FillStroke_SurvivesDocumentSerialization_PixelForPixel()
    {
        var fill = Square(6, 6, 30);
        fill.Holes = [[new(12, 12, 1), new(20, 12, 1), new(20, 20, 1), new(12, 20, 1)]];
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var frame = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(fill);

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        var reloadedFrame = (PaintedFrame)reloaded.Scene.Layers[0].Cels[0].Frame!;
        Assert.NotNull(reloadedFrame.Strokes[0].Holes);

        using var a = FrameRasterizer.Rasterize(frame.Strokes, 64, 64);
        using var b = FrameRasterizer.Rasterize(reloadedFrame.Strokes, 64, 64);
        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void ClippedStroke_ReRendersIdenticallyFromJsonAlone()
    {
        // Provenance: a stroke painted under a selection carries the clip in
        // the document, and a fresh process rendering from JSON gets the
        // exact same pixels.
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        var region = new ClipRegion
        {
            Contours = [[new(0, 0, 1), new(32, 0, 1), new(32, 64, 1), new(0, 64, 1)]],
            Feather = 0,
        };
        doc.ClipRegions["clip_test"] = region;
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Brush = new BrushSettings { Size = 14, Hardness = 1, Opacity = 1 },
            Points = [new(8, 32, 1), new(56, 32, 1)],
            ClipId = "clip_test",
        };
        var frame = (PaintedFrame)doc.Scene.Layers[0].Cels[0].Frame!;
        frame.Strokes.Add(stroke);
        ClipRegionRegistry.Register(doc.ClipRegions);

        using var direct = FrameRasterizer.Rasterize(frame.Strokes, 64, 64);
        Assert.True(direct.GetPixel(16, 32).Alpha > 200, "inside the selection the stroke paints");
        Assert.Equal(0, direct.GetPixel(48, 32).Alpha);   // outside: clipped away

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));
        ClipRegionRegistry.Register(reloaded.ClipRegions);
        var reloadedFrame = (PaintedFrame)reloaded.Scene.Layers[0].Cels[0].Frame!;
        using var replay = FrameRasterizer.Rasterize(reloadedFrame.Strokes, 64, 64);
        Assert.Equal(Hash(direct), Hash(replay));
    }

    [Fact]
    public void Feather_SoftensTheClipEdge()
    {
        Stroke Clipped(string clipId) => new()
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1 },
            Points = [new(8, 32, 1), new(56, 32, 1)],
            ClipId = clipId,
        };
        List<List<StrokePoint>> contours =
        [
            [new(0, 0, 1), new(32, 0, 1), new(32, 64, 1), new(0, 64, 1)],
        ];
        ClipRegionRegistry.Register("clip_hard_t", new ClipRegion { Contours = contours, Feather = 0 });
        ClipRegionRegistry.Register("clip_soft_t", new ClipRegion { Contours = contours, Feather = 10 });

        using var hard = FrameRasterizer.Rasterize([Clipped("clip_hard_t")], 64, 64);
        using var soft = FrameRasterizer.Rasterize([Clipped("clip_soft_t")], 64, 64);

        // just past the clip edge: hard cut is empty, feather still carries paint
        Assert.Equal(0, hard.GetPixel(35, 32).Alpha);
        Assert.True(soft.GetPixel(35, 32).Alpha > 10, "feather must bleed past the edge");
        // and the feathered alpha fades with distance
        Assert.True(soft.GetPixel(34, 32).Alpha > soft.GetPixel(38, 32).Alpha);
    }
}
