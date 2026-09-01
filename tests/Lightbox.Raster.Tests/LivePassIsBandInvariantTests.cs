using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The live post-process gives the same pixels over a band as over the whole
/// mark (B313).
/// </summary>
/// <remarks>
/// <para>
/// <b>B293 closed saying this was a pixel decision, and it was not.</b> Its
/// entry recorded that "granulation seeds its field from the rect corner, so a
/// smaller rect moves the grain" — read off the comment in
/// <c>PostProcessRegion</c>, which says <c>rect</c> is a <em>document</em> rect
/// because the field seeds from the corner. That is the reason it is safe. The
/// field is a repeat shader under a document-space transform and
/// <c>PaperField.Fill</c> indexes <c>mod tile</c> from the same document
/// corner, so the corner matters only through coordinates that are the same
/// whichever rect is asked about.
/// </para>
/// <para>
/// <b>So this asks the rect instead of arguing about it.</b> One stroke, one
/// dab scratch, processed twice — once over the whole mark and once over a band
/// inside it — and the overlap compared. That is the only question that decides
/// whether the pass can be made incremental, and it had been answered from a
/// comment.
/// </para>
/// <para>
/// <b>Two of the three effects needed something.</b> The footprint ceiling is
/// pointwise only when the caller carries it: rebuilt inside each rect it
/// re-renders the tip's gradients at a different device offset and moves
/// pixels by 2-3/255. The wet edge is a blur and needs a skirt, which is what
/// <see cref="BrushEngine.LivePassHalo"/> sizes. Both are asserted below in the
/// form the live path actually uses them.
/// </para>
/// </remarks>
public class LivePassIsBandInvariantTests
{
    private const int Width = 1600;
    private const int Height = 900;

    private readonly ITestOutputHelper _out;

    public LivePassIsBandInvariantTests(ITestOutputHelper output) => _out = output;

    private static Stroke Arc(BrushSettings brush, int points = 200)
    {
        var pts = new List<StrokePoint>(points);
        double x = 120, y = 400, heading = -0.2;
        for (var i = 0; i < points; i++)
        {
            pts.Add(new StrokePoint(x, y, 1));
            heading += 0.003;
            x += 6.0 * Math.Cos(heading);
            y += 6.0 * Math.Sin(heading);
        }

        return new Stroke { Tool = ToolKind.Brush, Color = "#203040", Brush = brush, Points = pts };
    }

    private static SKBitmap StampAll(Stroke stroke)
    {
        var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        var dabs = BrushEngine.WalkDabs(stroke);
        BrushEngine.StampDabRange(canvas, stroke, dabs, 0, dabs.Count);
        canvas.Flush();
        return bmp;
    }

    /// <summary>A real copy of one rect, as the view model hands the worker.</summary>
    private static SKBitmap? Crop(SKBitmap? src, SKRectI r)
    {
        if (src is null) return null;
        var bmp = new SKBitmap(new SKImageInfo(
            r.Width, r.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var sub = new SKBitmap();
        if (!src.ExtractSubset(sub, r)) { bmp.Dispose(); return null; }
        using var px = sub.PeekPixels();
        using var view = px is null ? null : SKImage.FromPixels(px);
        if (view is null) { bmp.Dispose(); return null; }
        using (var canvas = new SKCanvas(bmp))
        using (var replace = new SKPaint { BlendMode = SKBlendMode.Src })
        {
            canvas.DrawImage(view, 0, 0, replace);
            canvas.Flush();
        }

        return bmp;
    }

    /// <summary>
    /// The canvas-sized running maximum the live path carries across events
    /// (B293), or null to make the engine rebuild it inside its own rect.
    /// </summary>
    private static SKBitmap? CarriedFootprint(Stroke stroke, bool carry)
    {
        if (!carry) return null;
        var bmp = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        BrushEngine.AccumulateFootprint(canvas, stroke, BrushEngine.WalkDabs(stroke), 0, int.MaxValue);
        canvas.Flush();
        return bmp;
    }

    /// <summary>
    /// Worst absolute channel difference between the band's pixels as the whole
    /// mark renders them and as a band-sized pass renders them.
    /// </summary>
    /// <param name="at">
    /// Where the band starts, as a fraction along the mark. Several positions,
    /// because a single one can agree by luck — and a band at the far end sits
    /// where the mark's own cap is, which is the interesting neighbourhood.
    /// </param>
    private int WorstOverBand(BrushSettings brush, string name, double at, bool carry = true)
    {
        var stroke = Arc(brush);
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var dabs = StampAll(stroke);
        using var carried = CarriedFootprint(stroke, carry);

        var whole = BrushEngine.PostProcessBounds(stroke, info)!.Value;
        var left = whole.Left + (int)(whole.Width * at);
        var band = new SKRectI(
            left, whole.Top + whole.Height / 4,
            Math.Min(whole.Right, left + 180), whole.Bottom - whole.Height / 4);

        // The skirt the pass computes and throws away, which is what makes a
        // neighbourhood effect see real pixels either side of the band.
        var halo = BrushEngine.LivePassHalo(brush);
        var grown = SKRectI.Intersect(
            new SKRectI(band.Left - halo, band.Top - halo, band.Right + halo, band.Bottom + halo),
            whole);

        // B349: the footprint crop reaches one brush radius past the pass, the
        // way the live worker's does, because the swept ceiling measures each
        // pixel's distance to the edge of the stroke's support and that edge
        // can lie outside the pass. The space offsets say where the pass sits
        // inside the larger crop — the same arithmetic MainViewModel uses.
        var reach = carry ? BrushEngine.CeilingReachPx(brush, 1.0) : 0;
        var cropRect = SKRectI.Intersect(
            new SKRectI(grown.Left - reach, grown.Top - reach, grown.Right + reach, grown.Bottom + reach),
            new SKRectI(0, 0, Width, Height));
        var partSpace = new FootprintSpace(1.0, grown.Left - cropRect.Left, grown.Top - cropRect.Top);

        using var fullPrint = Crop(carried, whole);
        using var partPrint = Crop(carried, cropRect);
        using var full = BrushEngine.PostProcessRegion(
            dabs, stroke, whole, null, default, default, fullPrint)!;
        using var part = BrushEngine.PostProcessRegion(
            dabs, stroke, grown, null, default, default, partPrint, carry ? partSpace : null)!;
        using var fullPx = SKBitmap.FromImage(full);
        using var partPx = SKBitmap.FromImage(part);

        var worst = 0;
        for (var y = band.Top; y < band.Bottom; y++)
        {
            for (var x = band.Left; x < band.Right; x++)
            {
                var a = fullPx.GetPixel(x - whole.Left, y - whole.Top);
                var b = partPx.GetPixel(x - grown.Left, y - grown.Top);
                var d = Math.Max(
                    Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                    Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
                if (d > worst) worst = d;
            }
        }

        _out.WriteLine(
            $"{name,-22} at {at:0.00}  halo {halo,3}  reach {reach,3}  pass {grown.Width}x{grown.Height}"
            + $" of {whole.Width}x{whole.Height}  worst {worst}/255");
        return worst;
    }

    private static BrushSettings Pencil() => new()
    {
        Size = 3, Hardness = 0.9, Opacity = 1, Flow = 0.85, Spacing = 0.12,
        Granulation = 0.15, PressureFlowGamma = 1, AntiAlias = true,
    };

    private static BrushSettings WatercolorFlat() => new()
    {
        Size = 22, Hardness = 0.4, Opacity = 0.65, Flow = 0.5, Spacing = 0.1,
        WetEdge = 0.7, Granulation = 0.35, PressureFlowGamma = 0.8, AntiAlias = true,
    };

    private static BrushSettings GouacheFlat() => new()
    {
        Size = 18, Hardness = 0.75, Opacity = 0.95, Flow = 0.9, Spacing = 0.12,
        WetEdge = 0.15, Granulation = 0.25, AntiAlias = true,
    };

    private static BrushSettings OilFlat() => new()
    {
        Size = 34, Hardness = 0.6, Opacity = 1, Flow = 0.92, Spacing = 0.06,
        TextureSurface = PaperKind.Canvas, TextureDepth = 0.5, TextureScale = 1.0,
        AntiAlias = true,
    };

    private static BrushSettings ByName(string name) => name switch
    {
        "pencil" => Pencil(),
        "watercolor" => WatercolorFlat(),
        "gouache" => GouacheFlat(),
        _ => OilFlat(),
    };

    [Theory]
    [InlineData("pencil")]
    [InlineData("watercolor")]
    [InlineData("gouache")]
    [InlineData("oil")]
    public void ABandGivesTheSamePixelsAsTheWholeMark(string name)
    {
        var brush = ByName(name);
        foreach (var at in new[] { 0.15, 0.45, 0.75 })
        {
            Assert.Equal(0, WorstOverBand(brush, name, at));
        }
    }

    /// <summary>
    /// The sensitivity half: the skirt is load-bearing, not decoration.
    /// </summary>
    /// <remarks>
    /// Without this the assertions above would pass just as well on a build
    /// where <see cref="BrushEngine.LivePassHalo"/> returned some enormous
    /// number and the "band" was quietly the whole mark. Here the wet edge is
    /// asked with no skirt at all, and it has to disagree — a blur shown a cut
    /// edge invents a rim along it, which is the entire reason the halo exists.
    /// </remarks>
    [Fact]
    public void WithoutItsSkirtTheWetEdgeInventsARim()
    {
        var brush = WatercolorFlat();
        Assert.True(BrushEngine.LivePassHalo(brush) > 0, "a wet brush must ask for a skirt");

        var bare = WatercolorFlat();
        bare.WetEdge = 0;
        Assert.Equal(0, BrushEngine.LivePassHalo(bare));

        // Same brush, same band, with the skirt taken away by telling the halo
        // there is no wet edge to protect.
        var worst = WorstOverBandWithHalo(brush, 0.45, halo: 0);
        _out.WriteLine($"watercolor with no skirt: worst {worst}/255");
        Assert.True(worst > 4, $"expected a visible rim artefact, got {worst}/255");
    }

    /// <summary>The same comparison with the skirt forced to a given width.</summary>
    private int WorstOverBandWithHalo(BrushSettings brush, double at, int halo)
    {
        var stroke = Arc(brush);
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var dabs = StampAll(stroke);
        using var carried = CarriedFootprint(stroke, true);

        var whole = BrushEngine.PostProcessBounds(stroke, info)!.Value;
        var left = whole.Left + (int)(whole.Width * at);
        var band = new SKRectI(
            left, whole.Top + whole.Height / 4,
            Math.Min(whole.Right, left + 180), whole.Bottom - whole.Height / 4);
        var grown = SKRectI.Intersect(
            new SKRectI(band.Left - halo, band.Top - halo, band.Right + halo, band.Bottom + halo),
            whole);

        using var fullPrint = Crop(carried, whole);
        using var partPrint = Crop(carried, grown);
        using var full = BrushEngine.PostProcessRegion(
            dabs, stroke, whole, null, default, default, fullPrint)!;
        using var part = BrushEngine.PostProcessRegion(
            dabs, stroke, grown, null, default, default, partPrint)!;
        using var fullPx = SKBitmap.FromImage(full);
        using var partPx = SKBitmap.FromImage(part);

        var worst = 0;
        for (var y = band.Top; y < band.Bottom; y++)
        {
            for (var x = band.Left; x < band.Right; x++)
            {
                var a = fullPx.GetPixel(x - whole.Left, y - whole.Top);
                var b = partPx.GetPixel(x - grown.Left, y - grown.Top);
                var d = Math.Max(
                    Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                    Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
                if (d > worst) worst = d;
            }
        }

        return worst;
    }

    /// <summary>
    /// The ceiling has to be carried, not rebuilt inside the band.
    /// </summary>
    /// <remarks>
    /// This is the one that was nearly missed. Rebuilding the footprint means
    /// re-rendering the tip's gradients into a surface at a different device
    /// offset, and the answer is not quite the same — 2-3/255 on a handful of
    /// pixels of a big soft tip, mean 0.000, which is invisible in any single
    /// render and is exactly the kind of drift that makes a live preview
    /// disagree with its commit. It was found by taking the paper away and
    /// watching the difference stay.
    ///
    /// <para>
    /// <b>Swept rather than sampled, because the drift depends on where the
    /// band starts.</b> The first draft of this test asserted at one position
    /// and failed: at 0.45 the rebuilt ceiling happens to agree exactly. That is
    /// the shape of the defect — a rect offset that lands the gradients on the
    /// same device phase gives the same pixels, and one that does not, does not
    /// — so the honest assertion is that <em>some</em> band disagrees while
    /// <em>every</em> band agrees once the ceiling is carried.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARebuiltCeilingDoesNotSurviveTheBandButACarriedOneDoes()
    {
        var brush = OilFlat();
        Assert.True(BrushEngine.NeedsFootprintCap(brush), "the case needs a brush that caps");

        var positions = new[] { 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80 };
        var worstRebuilt = 0;
        foreach (var at in positions)
        {
            worstRebuilt = Math.Max(worstRebuilt, WorstOverBand(brush, "oil, rebuilt", at, carry: false));
        }

        foreach (var at in positions)
        {
            Assert.Equal(0, WorstOverBand(brush, "oil, carried", at, carry: true));
        }

        _out.WriteLine($"rebuilt ceiling, worst over {positions.Length} bands: {worstRebuilt}/255");
        Assert.True(
            worstRebuilt > 0,
            "a rebuilt ceiling was expected to move with the rect at some band position");
    }
}
