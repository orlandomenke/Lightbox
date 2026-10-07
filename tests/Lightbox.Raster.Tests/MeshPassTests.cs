using Lightbox.Raster;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The cage warp's preview pass (Q199): a bitmap stretched over a triangle
/// mesh lands its pixels where the vertices say, and a pass without a mesh is
/// untouched by the mesh code.
/// </summary>
public class MeshPassTests
{
    private static SKBitmap Line(int w, int h)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var black = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(20, 95, 160, 10, black);     // a bar at y = 95..105
        return bmp;
    }

    [Fact]
    public void AMeshPassStretchesTheBitmapOverItsTriangles()
    {
        using var bitmap = Line(200, 200);
        // Two triangles covering the bar's box (0,80)-(200,120), with the whole
        // box shifted down by 50 — the simplest mesh that moves pixels.
        SKPoint[] texs = [new(0, 80), new(200, 80), new(0, 120), new(200, 120)];
        SKPoint[] positions = [new(0, 130), new(200, 130), new(0, 170), new(200, 170)];
        ushort[] indices = [0, 1, 2, 1, 3, 2];
        var pass = new RenderPass(bitmap, null, 1.0, Mesh: new PassMesh(positions, texs, indices));

        using var image = SceneRenderer.Compose(200, 200, [pass], SKColors.White);
        using var pixels = SKBitmap.FromImage(image);

        Assert.True(pixels.GetPixel(100, 150).Red < 40, $"the bar did not land where the mesh put it ({pixels.GetPixel(100, 150)})");
        Assert.True(pixels.GetPixel(100, 100).Red > 200, $"the bar was also drawn where it was ({pixels.GetPixel(100, 100)})");
    }

    [Fact]
    public void AMeshPassHonoursOpacity()
    {
        using var bitmap = Line(200, 200);
        SKPoint[] quad = [new(0, 80), new(200, 80), new(0, 120), new(200, 120)];
        var pass = new RenderPass(bitmap, null, 0.5, Mesh: new PassMesh(quad, quad, [0, 1, 2, 1, 3, 2]));

        using var image = SceneRenderer.Compose(200, 200, [pass], SKColors.White);
        using var pixels = SKBitmap.FromImage(image);
        var red = pixels.GetPixel(100, 100).Red;
        Assert.InRange(red, 100, 160);
    }
}
