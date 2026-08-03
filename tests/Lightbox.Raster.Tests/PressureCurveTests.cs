using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Pressure curves and brush blend modes, measured in pixels rather than in
/// the arithmetic that produces them.
/// </summary>
public class PressureCurveTests(ITestOutputHelper output)
{
    private const int W = 400, H = 120;

    /// <summary>A stroke whose pressure ramps from nothing to full along its length.</summary>
    private static Stroke Ramp(Action<BrushSettings> tweak, ToolKind tool = ToolKind.Brush)
    {
        var brush = new BrushSettings
        {
            Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.05,
            PressureSizeGamma = 0,
        };
        tweak(brush);

        return new Stroke
        {
            Tool = tool,
            Color = "#202020",
            Points = Enumerable.Range(0, 40)
                .Select(i => new StrokePoint(20 + i * 9, 60, i / 39.0))
                .ToList(),
            Brush = brush,
        };
    }

    private static SKBitmap Render(params Stroke[] strokes)
    {
        var info = new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        var layer = new SKBitmap(info);
        using var canvas = new SKCanvas(layer);
        canvas.Clear(SKColors.Transparent);
        foreach (var stroke in strokes) BrushEngine.StampStroke(canvas, stroke, info, layer);
        canvas.Flush();
        return layer;
    }

    /// <summary>Painted pixels down a column — the stroke's thickness there.</summary>
    private static int Thickness(SKBitmap bmp, int x)
    {
        var n = 0;
        for (var y = 0; y < H; y++)
        {
            if (bmp.GetPixel(x, y).Alpha > 8) n++;
        }
        return n;
    }

    private static double Alpha(SKBitmap bmp, int x) => bmp.GetPixel(x, 60).Alpha / 255.0;

    // ---- curves -------------------------------------------------------------

    [Fact]
    public void ACurveDrivesTheDabWhereAGammaWouldHave()
    {
        // The substitution, proved in pixels: a curve that is the identity
        // paints what gamma 1 paints, because both say "multiply by pressure".
        using var byGamma = Render(Ramp(b => b.PressureSizeGamma = 1));
        using var byCurve = Render(Ramp(b => b.Curves = new()
        {
            [BrushDynamic.Size] = ResponseCurve.Linear(),
        }));

        for (var x = 40; x < 360; x += 20)
        {
            Assert.Equal(Thickness(byGamma, x), Thickness(byCurve, x));
        }
    }

    [Fact]
    public void AnArtistDrawnCurveDoesWhatNoGammaCould()
    {
        // The reason curves exist at all. p^γ is monotone for every γ, so no
        // exponent gives a brush that is thin at a feather touch, thick in the
        // middle and thin again when leaned on — and that is an ordinary thing
        // to want from an ink brush.
        using var bmp = Render(Ramp(b => b.Curves = new()
        {
            [BrushDynamic.Size] = new ResponseCurve { Points = [new(0, 0.15), new(0.5, 1), new(1, 0.2)] },
        }));

        int light = Thickness(bmp, 40), middle = Thickness(bmp, 190), heavy = Thickness(bmp, 350);
        output.WriteLine($"thickness: light {light}, middle {middle}, heavy {heavy}");

        Assert.True(middle > light + 4, $"the middle is not the fat part: {light} -> {middle}");
        Assert.True(middle > heavy + 4, $"it did not come back down: {middle} -> {heavy}");
    }

    [Fact]
    public void ACurveOnFlowChangesHowDarkTheMarkIs()
    {
        // Measured well below saturation. At spacing 0.05 a 20 px brush lays
        // about twenty dabs on every pixel, and 1-(1-a)^20 is already 0.92 at
        // a flow of 0.12 — so a "faint" curve of 0.1 comes out indistinguishable
        // from a full one and the test passes on a brush that is doing nothing.
        using var faint = Render(Ramp(b => b.Curves = new()
        {
            [BrushDynamic.Flow] = new ResponseCurve { Points = [new(0, 0.01), new(1, 0.02)] },
        }));
        using var full = Render(Ramp(b => b.Curves = new()
        {
            [BrushDynamic.Flow] = new ResponseCurve { Points = [new(0, 1), new(1, 1)] },
        }));

        output.WriteLine($"faint {Alpha(faint, 200):F3}, full {Alpha(full, 200):F3}");
        Assert.True(Alpha(faint, 200) < Alpha(full, 200) * 0.7,
            $"flow did nothing: {Alpha(faint, 200):F3} vs {Alpha(full, 200):F3}");
        Assert.True(Alpha(faint, 200) > 0.02, "the faint mark is not there at all");
    }

    [Fact]
    public void PressureCanOpenTheScatterWithoutReshufflingIt()
    {
        // Scatter's direction stays the hash's, so a harder press throws the
        // same pattern further rather than picking a new one. That is what
        // keeps a scattered brush from boiling between frames: the dabs move
        // outward, they do not jump about.
        using var still = Render(Ramp(b =>
        {
            b.Scatter = 0.8;
            b.Curves = new() { [BrushDynamic.Scatter] = new ResponseCurve { Points = [new(0, 0), new(1, 0)] } };
        }));
        using var spread = Render(Ramp(b =>
        {
            b.Scatter = 0.8;
            b.Curves = new() { [BrushDynamic.Scatter] = ResponseCurve.Linear() };
        }));

        int lightStill = Thickness(still, 40), lightSpread = Thickness(spread, 40);
        int heavyStill = Thickness(still, 350), heavySpread = Thickness(spread, 350);
        output.WriteLine($"light {lightStill} vs {lightSpread}; heavy {heavyStill} vs {heavySpread}");

        Assert.True(heavySpread > heavyStill + 4, $"pressure did not open the scatter: {heavyStill} -> {heavySpread}");
        Assert.True(lightSpread <= lightStill + 2, $"a feather touch scattered anyway: {lightStill} -> {lightSpread}");
    }

    [Fact]
    public void ADrivenDynamicIsStillAsRepeatableAsEverythingElse()
    {
        // Invariant 2. A curve is arithmetic on the stroke's own record, so two
        // renders are identical — which is what lets the inbetweener replay one.
        var stroke = Ramp(b => b.Curves = new()
        {
            [BrushDynamic.Size] = new ResponseCurve { Points = [new(0, 0.2), new(0.4, 0.9), new(1, 0.5)] },
            [BrushDynamic.Flow] = ResponseCurve.Linear(),
        });

        using var first = Render(stroke);
        using var again = Render(stroke);
        for (var y = 0; y < H; y += 2)
        for (var x = 0; x < W; x += 2)
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));
    }

    [Fact]
    public void TheMasterSwitchStillTurnsEverythingOff()
    {
        // A curve must not be a way back in when the artist has said the tablet
        // is ignored, or the checkbox lies.
        using var bmp = Render(Ramp(b =>
        {
            b.PressureEnabled = false;
            b.Curves = new() { [BrushDynamic.Size] = new ResponseCurve { Points = [new(0, 0), new(1, 0)] } };
        }));

        Assert.True(Thickness(bmp, 200) > 10, "the master switch did not defeat the curve");
    }

    [Fact]
    public void TheBrushRingAgreesWithTheStrokeTheCurveProduces()
    {
        // RadiusAt is what the cursor draws and what the engine stamps, and a
        // ring that disagrees with the mark is worse than no ring. Curves went
        // through the same door as gammas so this stays true by construction —
        // this test is what says so out loud.
        var brush = new BrushSettings
        {
            Size = 40,
            Curves = new() { [BrushDynamic.Size] = new ResponseCurve { Points = [new(0, 0.25), new(1, 1)] } },
        };

        Assert.Equal(5, BrushEngine.RadiusAt(brush, 0), 1e-9);
        Assert.Equal(20, BrushEngine.RadiusAt(brush, 1), 1e-9);
    }

    // ---- blend modes --------------------------------------------------------

    private static Stroke Flat(string colour, double size, LayerBlendMode? blend = null) => new()
    {
        Tool = ToolKind.Brush,
        Color = colour,
        Points = [new StrokePoint(20, 60, 1), new StrokePoint(380, 60, 1)],
        Brush = new BrushSettings
        {
            Size = size, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.05, Blend = blend,
        },
    };

    [Fact]
    public void ABrushBlendModeChangesHowTheStrokeMeetsWhatIsUnderIt()
    {
        using var normal = Render(Flat("#c0c0c0", 40), Flat("#808080", 20));
        using var multiply = Render(Flat("#c0c0c0", 40), Flat("#808080", 20, LayerBlendMode.Multiply));

        int n = normal.GetPixel(200, 60).Red, m = multiply.GetPixel(200, 60).Red;
        output.WriteLine($"normal {n}, multiply {m}");

        // 0x80 over 0xc0: normal replaces it, multiply darkens past both.
        Assert.True(m < n - 20, $"multiply did not darken: {m} vs {n}");
    }

    [Fact]
    public void ABrushThatSetsNoBlendModePaintsExactlyAsItAlwaysDid()
    {
        // The compatibility claim, in pixels.
        using var absent = Render(Flat("#c0c0c0", 40), Flat("#808080", 20));
        using var explicitly = Render(Flat("#c0c0c0", 40), Flat("#808080", 20, LayerBlendMode.Normal));

        for (var y = 0; y < H; y += 2)
        for (var x = 0; x < W; x += 2)
            Assert.Equal(absent.GetPixel(x, y), explicitly.GetPixel(x, y));
    }

    [Fact]
    public void AnEraserIgnoresTheBlendModeEntirely()
    {
        // Erasing takes alpha away, which no separable blend does. A Multiply
        // eraser that "blended" would paint instead of erase.
        var rub = Flat("#202020", 20, LayerBlendMode.Multiply);
        rub.Tool = ToolKind.Eraser;

        using var bmp = Render(Flat("#c0c0c0", 40), rub);

        Assert.True(bmp.GetPixel(200, 60).Alpha < 40,
            $"the eraser did not erase: alpha {bmp.GetPixel(200, 60).Alpha}");
    }

    [Fact]
    public void ABlendedStrokeCarriesItsBlendThroughACopy()
    {
        // The blend is on the stroke, not on the tool bar, so a stroke copied
        // into an inbetween or restored by undo composites the same way.
        // Invariant 4, from the blend mode's side.
        var over = Flat("#808080", 20, LayerBlendMode.Screen);
        var copy = new Stroke
        {
            Tool = over.Tool, Color = over.Color, Points = over.Points, Brush = over.Brush.Clone(),
        };

        using var first = Render(Flat("#404040", 40), over);
        using var again = Render(Flat("#404040", 40), copy);

        Assert.Equal(LayerBlendMode.Screen, copy.Brush.Blend);
        for (var y = 0; y < H; y += 2)
        for (var x = 0; x < W; x += 2)
            Assert.Equal(first.GetPixel(x, y), again.GetPixel(x, y));
    }
}
