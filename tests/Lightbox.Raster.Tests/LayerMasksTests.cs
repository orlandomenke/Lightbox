using Lightbox.Core.Documents;
using Lightbox.Raster.Media;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Turning a painted layer into per-cell coverage. The half of Q125 that has to
/// know what a layer is, kept out of <see cref="SimBaker"/> for that reason.
/// </summary>
public class LayerMasksTests(ITestOutputHelper output)
{
    private static Doc DocWithMask(out Layer mask, out SimElement element, double scale = 4)
    {
        var doc = DocumentFactory.CreateDoc(400, 400, 12);

        element = new SimElement
        {
            Id = "e", GridWidth = 40, GridHeight = 40, OriginX = 0, OriginY = 0, Scale = scale,
        };

        mask = new Layer { Id = "mask-layer", Name = "emission", OmitFromExport = true };
        doc.Scene.Layers.Add(mask);
        return doc;
    }

    /// <summary>A fat horizontal bar, in document pixels.</summary>
    private static Frame Bar(double y, double fromX, double toX, double width = 20) => new()
    {
        Strokes =
        {
            new Stroke
            {
                Tool = ToolKind.Brush,
                Color = "#ffffff",
                Brush = new BrushSettings { Size = width, Hardness = 1, Flow = 1, Opacity = 1 },
                Points = [new StrokePoint(fromX, y, 1), new StrokePoint(toX, y, 1)],
            },
        },
    };

    private static Emitter Masked() => new() { Id = "em", MaskLayerId = "mask-layer" };

    [Fact]
    public void Ink_On_The_Layer_Becomes_Coverage_Where_It_Was_Drawn()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });

        var coverage = new LayerMasks(doc, element).For(Masked(), 0);

        Assert.NotNull(coverage);
        Assert.Equal(element.GridWidth * element.GridHeight, coverage!.Length);

        // The bar sits at document y 120 with scale 4, so grid row 30.
        var lit = new List<(int X, int Y)>();
        for (var y = 0; y < element.GridHeight; y++)
        {
            for (var x = 0; x < element.GridWidth; x++)
            {
                if (coverage[y * element.GridWidth + x] > 0.5f) lit.Add((x, y));
            }
        }

        Assert.NotEmpty(lit);
        output.WriteLine($"{lit.Count} cells lit, rows {lit.Min(p => p.Y)}..{lit.Max(p => p.Y)}, " +
                         $"columns {lit.Min(p => p.X)}..{lit.Max(p => p.X)}");
        Assert.InRange(lit.Average(p => p.Y), 27, 33);
        Assert.InRange(lit.Min(p => p.X), 7, 13);
        Assert.InRange(lit.Max(p => p.X), 27, 33);
    }

    /// <summary>
    /// The element's origin is where its grid starts, so a mask drawn at the same
    /// document position lands somewhere else when the element moves.
    /// </summary>
    [Fact]
    public void Coverage_Is_Read_Through_The_Elements_Own_Placement()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });

        var atZero = new LayerMasks(doc, element).For(Masked(), 0)!;

        element.OriginY = 40;   // ten cells down at scale 4
        var shifted = new LayerMasks(doc, element).For(Masked(), 0)!;

        static double MeanRow(float[] c, int w, int h)
        {
            double sy = 0, s = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++) { sy += c[y * w + x] * y; s += c[y * w + x]; }
            }
            return s > 0 ? sy / s : -1;
        }

        var before = MeanRow(atZero, 40, 40);
        var after = MeanRow(shifted, 40, 40);
        output.WriteLine($"mask row {before:F1} → {after:F1} after moving the element ten cells down");
        Assert.True(after < before - 8, $"the element's origin was ignored: {before:F1} → {after:F1}");
    }

    [Fact]
    public void A_Layer_That_Is_Gone_Reads_As_No_Mask()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });
        doc.Scene.Layers.Remove(mask);

        Assert.Null(new LayerMasks(doc, element).For(Masked(), 0));
    }

    [Fact]
    public void A_Blank_Cel_Reads_As_No_Mask_Rather_Than_Zeroes()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel());
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });

        Assert.Null(new LayerMasks(doc, element).For(Masked(), 0));
        Assert.NotNull(new LayerMasks(doc, element).For(Masked(), 1));
    }

    [Fact]
    public void A_Frame_Past_The_End_Reads_As_No_Mask()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });

        Assert.Null(new LayerMasks(doc, element).For(Masked(), 5));
        Assert.Null(new LayerMasks(doc, element).For(Masked(), -1));
    }

    [Fact]
    public void An_Emitter_With_No_Mask_Layer_Reads_As_No_Mask()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });

        Assert.Null(new LayerMasks(doc, element).For(new Emitter { Shape = EmitterShape.Disc }, 0));
    }

    /// <summary>
    /// A hold asks for the same cel on several frames and several emitters may
    /// read one layer, so rasterising is done once.
    /// </summary>
    [Fact]
    public void The_Same_Layer_And_Frame_Is_Rasterised_Once()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });

        var masks = new LayerMasks(doc, element);
        var first = masks.For(Masked(), 0);
        var again = masks.For(new Emitter { Id = "other", MaskLayerId = "mask-layer" }, 0);

        Assert.Same(first, again);
    }

    /// <summary>
    /// Invariant 7: the mask is made by rendering the same strokes onto a smaller
    /// surface, never by scaling their geometry — so a coarser grid gives a
    /// coarser mask of the same drawing rather than a different drawing.
    /// </summary>
    [Fact]
    public void A_Coarser_Grid_Covers_The_Same_Part_Of_The_Drawing()
    {
        var doc = DocWithMask(out var mask, out var element);
        mask.Cels.Add(new Cel { Frame = Bar(y: 120, fromX: 40, toX: 120) });

        var fine = new LayerMasks(doc, element).For(Masked(), 0)!;

        // Half the cells across the same document area.
        element.GridWidth = 20;
        element.GridHeight = 20;
        element.Scale = 8;
        var coarse = new LayerMasks(doc, element).For(Masked(), 0)!;

        static (double X, double Y) Centre(float[] c, int w, int h)
        {
            double sx = 0, sy = 0, s = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++) { sx += c[y * w + x] * x; sy += c[y * w + x] * y; s += c[y * w + x]; }
            }
            return s > 0 ? (sx / s, sy / s) : (-1, -1);
        }

        var a = Centre(fine, 40, 40);
        var b = Centre(coarse, 20, 20);
        output.WriteLine($"centre at 40×40 is ({a.X:F1}, {a.Y:F1}); at 20×20 it is ({b.X:F1}, {b.Y:F1})");

        // Same place in the element, expressed in each grid's own cells.
        Assert.Equal(a.X / 40, b.X / 20, 1);
        Assert.Equal(a.Y / 40, b.Y / 20, 1);
    }
}
