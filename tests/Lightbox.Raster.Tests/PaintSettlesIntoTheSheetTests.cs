using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;
using Lightbox.Raster.Media;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Thin paint settles into the sheet: it covers everything and is denser in
/// the valleys, so the surface shows as tone inside the mark and not as gaps.
/// The owner's idea, with a reference of thin grey paint whose canvas weave
/// reads faintly inside a solid area: *"a thin ink paint layer that did fill
/// all crevices, as it is so thin the paper texture does show through."*
/// </summary>
/// <remarks>
/// The other half of how a sheet shows. A tooth gates the paint - a light or
/// dry brush catches the peaks and leaves the valleys empty. This has the
/// opposite sign and no gaps. Measured by sorting the pixels of a patch by the
/// sheet's own height at each, rather than by the look of the picture.
/// </remarks>
public class PaintSettlesIntoTheSheetTests(ITestOutputHelper output)
{
    private const int Width = 700;
    private const int Height = 200;
    private const PaperKind Sheet = PaperKind.Linen;
    private const double Grain = 6;

    private static BrushSettings Film(double? settles) => new()
    {
        Size = 70, Hardness = 0.9, Opacity = 1, Flow = 0.6, Spacing = 0.08,
        TextureSurface = Sheet, TextureScale = Grain, TextureSettle = settles,
    };

    private static SKBitmap Draw(BrushSettings brush)
    {
        var points = new List<StrokePoint>();
        for (var x = 60; x <= 640; x += 10) points.Add(new StrokePoint(x, 100, 0.8));
        return FrameRasterizer.Rasterize(
            [new Stroke { Tool = ToolKind.Brush, Color = "#404448", Brush = brush, Points = points }], Width, Height, 1.0);
    }

    /// <summary>Mean alpha over the sheet's valleys and over its peaks, and the least alpha anywhere, in the mark's middle.</summary>
    private static (double Valleys, double Peaks, int Least) Read(SKBitmap bitmap)
    {
        var px = bitmap.GetPixelSpan();
        long valleys = 0, peaks = 0;
        int nv = 0, np = 0, least = 255;
        for (var y = 85; y <= 115; y++)
        {
            for (var x = 200; x < 500; x++)
            {
                var alpha = px[(((y * Width) + x) * 4) + 3];
                least = Math.Min(least, alpha);
                var h = PaperField.HeightAt(x, y, Sheet, Grain);
                if (h < 0.25f) { valleys += alpha; nv++; }
                else if (h > 0.75f) { peaks += alpha; np++; }
            }
        }
        Assert.True(nv > 200 && np > 200, $"the patch has {nv} valley and {np} peak pixels: nothing to compare");
        return (valleys / (double)nv, peaks / (double)np, least);
    }

    [Fact]
    public void AThinFilmIsDarkerInTheValleys()
    {
        using var flat = Draw(Film(null));
        using var film = Draw(Film(0.45));
        var plain = Read(flat);
        var settled = Read(film);
        output.WriteLine($"no settling: valleys {plain.Valleys:0.0} peaks {plain.Peaks:0.0}; settles 0.45: valleys {settled.Valleys:0.0} peaks {settled.Peaks:0.0}");

        // Without it the sheet is not in the mark at all.
        Assert.True(Math.Abs(plain.Valleys - plain.Peaks) < 2, "the control already shows the sheet");
        Assert.True(settled.Valleys > settled.Peaks + 40,
            $"the film is not denser in the valleys: {settled.Valleys:0.0} there, {settled.Peaks:0.0} on the peaks");
        // The valleys keep nearly all of what was laid (they are not at height
        // zero, so a little goes: 254 to 236 when this was written); it is the
        // peaks that are thinner.
        Assert.True(settled.Valleys > plain.Valleys - 30, $"the valleys lost paint too: {plain.Valleys:0.0} to {settled.Valleys:0.0}");
    }

    /// <summary>A film, not a tooth: there is paint everywhere under the brush.</summary>
    [Fact]
    public void AFilmLeavesNoGaps()
    {
        using var film = Draw(Film(0.6));
        var settled = Read(film);
        output.WriteLine($"settles 0.6: least alpha in the mark's middle {settled.Least}, peaks {settled.Peaks:0.0}, valleys {settled.Valleys:0.0}");
        Assert.True(settled.Least >= 60, $"the film has gaps: alpha falls to {settled.Least}");
    }

    /// <summary>Absent from a record that does not use it (the optional-settings rule).</summary>
    [Fact]
    public void ABrushThatDoesNotSettleWritesNoKey()
    {
        var plain = JsonSerializer.Serialize(new BrushSettings { TextureSurface = PaperKind.Plain, TextureDepth = 0.4 }, DocJson.Options);
        var used = JsonSerializer.Serialize(Film(0.45), DocJson.Options);
        Assert.DoesNotContain("\"textureSettle\"", plain);
        Assert.Contains("\"textureSettle\"", used);
        Assert.Equal(0.45, JsonSerializer.Deserialize<BrushSettings>(used, DocJson.Options)!.TextureSettle);
    }
}
