using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// What a smudge reads: its own layer, everything beneath it, or what was
/// beneath it when the stroke was made.
/// </summary>
/// <remarks>
/// The engine half of <c>QUESTIONS.md</c> Q6, answered (c) — the choice is
/// recorded per stroke, because a smudge blending into a background and a
/// smudge nudged until it looked right want opposite things from the same
/// gesture. Invariant 4: a setting that reaches pixels belongs to the stroke,
/// not to whatever the tool bar reads at render time.
/// </remarks>
[Collection("Registries")]
public class SampleSourceTests
{
    private const int W = 80, H = 60;

    private static SKImageInfo Info => new(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);

    /// <summary>A layer with a red bar across the top half of the smudge path.</summary>
    private static SKBitmap Layer()
    {
        var bmp = new SKBitmap(Info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(220, 40, 40) };
        canvas.DrawRect(10, 10, 60, 12, paint);
        canvas.Flush();
        return bmp;
    }

    /// <summary>A backdrop with a green block where the layer has nothing.</summary>
    private static SKBitmap Backdrop()
    {
        var bmp = new SKBitmap(Info);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(40, 200, 60) };
        canvas.DrawRect(10, 30, 60, 20, paint);
        canvas.Flush();
        return bmp;
    }

    private static Stroke Smudge(SampleSource source) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Brush = new BrushSettings
        {
            Kind = BrushKind.Smudge,
            Size = 18,
            Flow = 1,
            Spacing = 0.2,
            SmudgeLength = 0.9,
            ColorRate = 0,
            SampleSource = source,
        },
        // Runs through the backdrop's green block, well clear of the layer's bar.
        Points = [new StrokePoint(20, 40, 1), new StrokePoint(60, 40, 1)],
    };

    private static SKBitmap Run(Stroke stroke, SKBitmap? backdrop)
    {
        var layer = Layer();
        using var canvas = new SKCanvas(layer);
        BrushEngine.StampStroke(canvas, stroke, Info, layer, backdrop: backdrop);
        canvas.Flush();
        return layer;
    }

    /// <summary>How much green ended up on the layer — the tell that it read the backdrop.</summary>
    private static int GreenPixels(SKBitmap bmp)
    {
        var count = 0;
        for (var y = 0; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha > 20 && c.Green > c.Red + 20) count++;
            }
        }
        return count;
    }

    [Fact]
    public void ThisLayerIgnoresWhatIsUnderneath()
    {
        // The default, and what every stroke drawn before this option existed
        // is. Handing it a backdrop must change nothing.
        using var withBackdrop = Run(Smudge(SampleSource.ThisLayer), Backdrop());
        using var without = Run(Smudge(SampleSource.ThisLayer), null);

        Assert.Equal(0, GreenPixels(withBackdrop));
        Assert.Equal(without.GetPixelSpan().ToArray(), withBackdrop.GetPixelSpan().ToArray());
    }

    [Fact]
    public void AllLayersLiveReadsTheBackdrop()
    {
        using var bmp = Run(Smudge(SampleSource.AllLayersLive), Backdrop());

        Assert.True(GreenPixels(bmp) > 0, "the smudge should have picked up the backdrop's green");
    }

    [Fact]
    public void AllLayersLiveFollowsTheBackdropWhenItChanges()
    {
        // The whole point of Live. Same stroke, different thing underneath,
        // different mark — with nothing re-recorded in between.
        using var over = Run(Smudge(SampleSource.AllLayersLive), Backdrop());

        using var moved = new SKBitmap(Info);
        using (var canvas = new SKCanvas(moved))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = new SKColor(40, 200, 60) };
            canvas.DrawRect(10, 48, 60, 8, paint);   // the same green, lower down
            canvas.Flush();
        }
        using var after = Run(Smudge(SampleSource.AllLayersLive), moved);

        Assert.NotEqual(over.GetPixelSpan().ToArray(), after.GetPixelSpan().ToArray());
    }

    [Fact]
    public void WithNothingUnderneathAllLayersIsJustTheLayer()
    {
        // A render path that cannot supply a backdrop — a thumbnail, the bottom
        // layer — makes the mark the stroke would have made without the option
        // rather than no mark at all.
        using var all = Run(Smudge(SampleSource.AllLayersLive), null);
        using var own = Run(Smudge(SampleSource.ThisLayer), null);

        Assert.Equal(own.GetPixelSpan().ToArray(), all.GetPixelSpan().ToArray());
    }

    // ---- baked -----------------------------------------------------------------

    [Fact]
    public void ABakedStrokeCarriesWhatItSampled()
    {
        var stroke = Smudge(SampleSource.AllLayersBaked);
        using var backdrop = Backdrop();

        stroke.Baked = BrushEngine.BakeSample(stroke, backdrop, Info);

        Assert.NotNull(stroke.Baked);
        Assert.NotEmpty(stroke.Baked!.PngBase64);
    }

    [Fact]
    public void ABakedSampleIsCroppedToWhatTheStrokeCanReach()
    {
        // Not the canvas. A smudge reaches a few hundred pixels, and a
        // full-size PNG per stroke would put the whole document in the record
        // once per mark.
        var stroke = Smudge(SampleSource.AllLayersBaked);
        using var backdrop = Backdrop();

        var baked = BrushEngine.BakeSample(stroke, backdrop, Info)!;

        using var patch = PngCodec.Decode(baked.PngBase64);
        Assert.True(patch.Width < W, $"baked patch was {patch.Width} wide on an {W} canvas");
        Assert.True(baked.X > 0 || baked.Y > 0);
    }

    [Fact]
    public void ABakedStrokeRendersFromItsOwnSampleWithNoBackdropGiven()
    {
        // The property that makes Baked cost no plumbing: every render path
        // reproduces it, whether or not it knows what is underneath.
        var stroke = Smudge(SampleSource.AllLayersBaked);
        using var backdrop = Backdrop();
        stroke.Baked = BrushEngine.BakeSample(stroke, backdrop, Info);

        using var bmp = Run(stroke, null);

        Assert.True(GreenPixels(bmp) > 0, "a baked stroke should show the backdrop it froze");
    }

    [Fact]
    public void ABakedStrokeIgnoresABackdropThatChangedUnderIt()
    {
        // The other half of the choice: this one does not follow.
        var stroke = Smudge(SampleSource.AllLayersBaked);
        using var backdrop = Backdrop();
        stroke.Baked = BrushEngine.BakeSample(stroke, backdrop, Info);

        using var asBaked = Run(stroke, null);
        using var repainted = new SKBitmap(Info);
        using (var canvas = new SKCanvas(repainted))
        {
            canvas.Clear(new SKColor(20, 20, 200));
            canvas.Flush();
        }
        using var later = Run(stroke, repainted);

        Assert.Equal(asBaked.GetPixelSpan().ToArray(), later.GetPixelSpan().ToArray());
    }

    [Fact]
    public void ABakedStrokeWithNoSampleFallsBackToItsLayer()
    {
        // A stroke written by an agent, or one whose sample failed to decode.
        // Falling back to the layer is the recoverable failure; refusing to
        // render is not.
        var stroke = Smudge(SampleSource.AllLayersBaked);

        using var bmp = Run(stroke, null);
        using var own = Run(Smudge(SampleSource.ThisLayer), null);

        Assert.Equal(own.GetPixelSpan().ToArray(), bmp.GetPixelSpan().ToArray());
    }

    [Fact]
    public void SamplingReadsTheLayerOverTheBackdrop_NotTheBackdropAlone()
    {
        // "All layers" means what you can see, and where the layer has painted,
        // what you can see is the layer. Reading the backdrop alone would smudge
        // the background straight through the strokes being blended into it.
        //
        // Set up so the two answers are different colours in the same place:
        // red on the layer, blue directly under it. Then drag past the end of
        // both bars onto bare canvas and look at what got deposited — "the red
        // is still there" would prove nothing, since it was already there.
        using var layer = new SKBitmap(Info);
        using (var canvas = new SKCanvas(layer))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = new SKColor(230, 30, 30) };
            canvas.DrawRect(10, 10, 40, 14, paint);      // red, x 10..50
            canvas.Flush();
        }
        using var beneath = new SKBitmap(Info);
        using (var canvas = new SKCanvas(beneath))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = new SKColor(30, 30, 230) };
            canvas.DrawRect(10, 10, 40, 14, paint);      // blue, exactly under it
            canvas.Flush();
        }

        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#000000",
            Brush = new BrushSettings
            {
                Kind = BrushKind.Smudge, Size = 14, Flow = 1, Spacing = 0.15,
                SmudgeLength = 0.95, ColorRate = 0, SampleSource = SampleSource.AllLayersLive,
            },
            Points = [new StrokePoint(30, 17, 1), new StrokePoint(68, 17, 1)],
        };
        using (var canvas = new SKCanvas(layer))
        {
            BrushEngine.StampStroke(canvas, stroke, Info, layer, backdrop: beneath);
            canvas.Flush();
        }

        // Past x=50 both bars have ended, so whatever is here was dragged.
        int red = 0, blue = 0;
        for (var x = 54; x < 68; x++)
        {
            for (var y = 12; y < 24; y++)
            {
                var c = layer.GetPixel(x, y);
                if (c.Alpha <= 20) continue;
                if (c.Red > c.Blue + 20) red++;
                if (c.Blue > c.Red + 20) blue++;
            }
        }
        Assert.True(red > 0, "the smudge should have dragged the layer's own paint");
        Assert.True(red > blue, $"dragged {red} red and {blue} blue — it read the backdrop instead of the layer");
    }
}
