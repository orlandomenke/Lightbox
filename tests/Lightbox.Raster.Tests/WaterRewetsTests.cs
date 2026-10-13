using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using Lightbox.Raster;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A rewetting brush (<see cref="WetSettings"/>, Q255): the paint it passes
/// over bleeds out into the wet area and thins; it lays none of its own.
/// </summary>
/// <remarks>
/// <para>
/// The owner's words for it, against a first sheet of smears that was wrong:
/// <em>"the rewetting is more a bleed/spread and diluting the pigment."</em>
/// Each test below is one clause of that, measured on a hard-edged patch of
/// paint with water scrubbed over its left end and the bare paper beside it.
/// </para>
/// <para>
/// And one property the artist never sees and the live path depends on: any
/// rectangle of the result rendered on its own is the whole render's, to the
/// byte. It is what will let the pen redo only the band it has reached and
/// still show what the record replays.
/// </para>
/// </remarks>
public class WaterRewetsTests(ITestOutputHelper output)
{
    private const int W = 700;
    private const int H = 460;

    /// <summary>Four hard bands of opaque paint: x 215 to 510, y 110 to 355.</summary>
    private static List<Stroke> Patch()
    {
        var ink = new BrushSettings { Size = 80, Hardness = 0.95, Opacity = 1, Flow = 1, Spacing = 0.08 };
        var strokes = new List<Stroke>();
        for (var k = 0; k < 4; k++)
        {
            var y = 150 + (k * 55);
            var pts = new List<StrokePoint>();
            for (var i = 0; i <= 50; i++) pts.Add(new StrokePoint(250 + (i * 4.5), y, 1));
            strokes.Add(new Stroke { Tool = ToolKind.Brush, Color = "#d8356a", Brush = ink.Clone(), Points = pts });
        }
        return strokes;
    }

    private static BrushSettings Water(Action<WetSettings>? tune = null, double size = 110)
    {
        var wet = new WetSettings();
        tune?.Invoke(wet);
        return new BrushSettings { Size = size, Hardness = 0.4, Flow = 1, Spacing = 0.15, Kind = BrushKind.Blur, Wet = wet };
    }

    /// <summary>Water scrubbed down the patch's left end and the paper beside it: x 55 to 345.</summary>
    private static Stroke Scrub(BrushSettings brush, double pressure = 0.9)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i <= 100; i++) pts.Add(new StrokePoint(200 + (Math.Sin(i * 0.2) * 90), 110 + (i * 2.5), pressure));
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = brush, Points = pts };
    }

    private static SKBitmap Render(params Stroke[] extra) =>
        FrameRasterizer.Rasterize([.. Patch(), .. extra], W, H, 1.0);

    private static long Alpha(SKBitmap bitmap, int x0, int y0, int x1, int y1)
    {
        var px = bitmap.GetPixelSpan();
        long sum = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++) sum += px[(((y * bitmap.Width) + x) * 4) + 3];
        }
        return sum;
    }

    [Fact]
    public void ThePaintBleedsOutIntoTheWetPaper()
    {
        using var dry = Render();
        using var wet = Render(Scrub(Water()));
        // Bare paper to the left of the patch, inside where the water went.
        var before = Alpha(dry, 120, 160, 195, 320);
        var after = Alpha(wet, 120, 160, 195, 320);
        output.WriteLine($"paper beside the patch: alpha {before} dry, {after} after water");
        Assert.Equal(0, before);
        // A mean alpha of five over a strip 75 px wide beside the edge; it was ten when written.
        Assert.True(after > 75 * 160 * 5, $"hardly any paint reached the wet paper beside the patch: {after}");
    }

    [Fact]
    public void WhereItWasItIsThinner_AndNoPaintIsMade()
    {
        using var dry = Render();
        using var wet = Render(Scrub(Water()));
        // The left end of the patch, under the water.
        var before = Alpha(dry, 225, 160, 300, 320);
        var after = Alpha(wet, 225, 160, 300, 320);
        Assert.True(after < before * 0.9, $"the rewetted paint is not thinner: {before} to {after}");
        // Water moves paint and carries some off; over everything, there is no more than there was.
        var all = Alpha(dry, 0, 0, W, H);
        var allAfter = Alpha(wet, 0, 0, W, H);
        output.WriteLine($"under the water {before} to {after}; the whole sheet {all} to {allAfter}");
        Assert.True(allAfter <= all, $"the water made paint: {all} to {allAfter}");
        Assert.True(allAfter > all * 0.75, $"the water took most of the paint away: {all} to {allAfter}");
    }

    [Fact]
    public void WhatTheWaterDidNotTouchIsUntouched_AndTheEdgeThereStaysHard()
    {
        using var dry = Render();
        using var wet = Render(Scrub(Water()));
        var a = dry.GetPixelSpan();
        var b = wet.GetPixelSpan();
        var changed = 0;
        for (var y = 0; y < H; y++)
        {
            // Right of where the water reached: the water's dabs end at x 345.
            for (var x = 350; x < W; x++)
            {
                var i = ((y * W) + x) * 4;
                if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) changed++;
            }
        }
        Assert.True(changed == 0, $"{changed} pixels changed where the brush never was");
    }

    [Fact]
    public void WaterOnBarePaperLeavesItBare()
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i <= 40; i++) pts.Add(new StrokePoint(80 + (i * 8), 425, 0.9));
        // Along the foot of the sheet, further from the patch than the water can
        // carry paint: the patch ends at y 355, the brush reaches up to 410 and
        // its spread another 27.
        var far = new Stroke { Tool = ToolKind.Brush, Brush = Water(size: 30), Points = pts };
        using var wet = Render(far);
        Assert.Equal(0, Alpha(wet, 0, 395, W, H));
    }

    [Fact]
    public void AFirmTouchWetsMoreThanALightOne()
    {
        // Wetness follows the brush's flow dynamic, as a paint brush's flow does.
        BrushSettings Pressed() { var b = Water(); b.PressureFlowGamma = 1; return b; }
        using var dry = Render();
        using var light = Render(Scrub(Pressed(), pressure: 0.25));
        using var firm = Render(Scrub(Pressed(), pressure: 0.95));
        long Moved(SKBitmap wet) => Alpha(wet, 120, 160, 195, 320);
        output.WriteLine($"paint moved onto the paper: {Moved(light)} at a light touch, {Moved(firm)} at a firm one");
        Assert.True(Moved(light) > 0, "a light touch did nothing at all");
        Assert.True(Moved(firm) > Moved(light) * 1.5, $"pressure does not set how wet: {Moved(light)} light, {Moved(firm)} firm");
        _ = dry;
    }

    [Fact]
    public void EachPartOfItCanBeSwitchedOff()
    {
        using var all = Render(Scrub(Water()));
        using var even = Render(Scrub(Water(w => w.Cloud = 0)));
        using var kept = Render(Scrub(Water(w => w.Thin = 0)));
        using var noLine = Render(Scrub(Water(w => w.Gather = 0)));
        using var still = Render(Scrub(Water(w => w.Spread = 0)));
        Assert.False(all.GetPixelSpan().SequenceEqual(even.GetPixelSpan()), "Cloud changes nothing");
        Assert.False(all.GetPixelSpan().SequenceEqual(noLine.GetPixelSpan()), "Gather changes nothing");
        Assert.True(Alpha(kept, 0, 0, W, H) > Alpha(all, 0, 0, W, H), "Thin takes nothing away");
        // With no spread nothing leaves the patch.
        Assert.Equal(0, Alpha(still, 120, 160, 195, 320));
    }

    /// <summary>
    /// The property the live path will stand on: a rectangle rendered alone is
    /// the whole render's, to the byte - cut anywhere, including through the
    /// wettest part and off the sheet's edge.
    /// </summary>
    [Fact]
    public void APieceIsTheWholeToTheByte()
    {
        using var layer = Render();
        var brush = Water();
        brush.TextureSurface = PaperKind.Fibre;
        brush.TextureScale = 8;
        brush.TextureSettle = 0.4;
        var dabs = BrushEngine.WalkDabs(Scrub(brush));
        var pre = layer.GetPixelSpan();

        var whole = new byte[W * H * 4];
        WetBleed.Render(pre, W, H, layer.RowBytes, dabs, brush, new SKRectI(0, 0, W, H), whole);

        var pieces = new[]
        {
            new SKRectI(0, 0, 200, 230), new SKRectI(200, 0, 347, 230), new SKRectI(347, 0, W, 230),
            new SKRectI(0, 230, 211, H), new SKRectI(211, 230, 212, H), new SKRectI(212, 230, W, H),
        };
        var stitched = new byte[W * H * 4];
        foreach (var piece in pieces)
        {
            var part = new byte[piece.Width * piece.Height * 4];
            WetBleed.Render(pre, W, H, layer.RowBytes, dabs, brush, piece, part);
            for (var y = 0; y < piece.Height; y++)
            {
                part.AsSpan(y * piece.Width * 4, piece.Width * 4)
                    .CopyTo(stitched.AsSpan((((piece.Top + y) * W) + piece.Left) * 4, piece.Width * 4));
            }
        }

        var differing = 0;
        for (var i = 0; i < whole.Length; i++)
        {
            if (whole[i] != stitched[i]) differing++;
        }
        Assert.True(differing == 0, $"{differing} bytes differ between the whole render and the same render in six pieces");
        // And it is the render a replay makes.
        using var replay = Render(Scrub(brush));
        Assert.True(replay.GetPixelSpan().SequenceEqual(whole), "the engine's stroke is not this render");
    }

    [Fact]
    public void ItRendersTheSameTwice()
    {
        using var a = Render(Scrub(Water()));
        using var b = Render(Scrub(Water()));
        Assert.True(a.GetPixelSpan().SequenceEqual(b.GetPixelSpan()));
    }

    /// <summary>Absent from a record that does not use it, and a plain blur is still a plain blur.</summary>
    [Fact]
    public void ABrushThatDoesNotRewetWritesNoKey()
    {
        var blur = JsonSerializer.Serialize(new BrushSettings { Kind = BrushKind.Blur, Flow = 0.35 }, DocJson.Options);
        Assert.DoesNotContain("\"wet\"", blur);
        var water = JsonSerializer.Serialize(Water(w => w.Thin = 0.3), DocJson.Options);
        Assert.Contains("\"wet\"", water);
        var back = JsonSerializer.Deserialize<BrushSettings>(water, DocJson.Options)!;
        Assert.Equal(0.3, back.Wet!.Thin);
        Assert.Equal(BrushKind.Blur, back.Kind);
    }
}
