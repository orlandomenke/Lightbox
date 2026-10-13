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

    /// <summary>A scrub that runs off every edge of the layer, for the cells that are partly outside it.</summary>
    private static Stroke Edges(BrushSettings brush)
    {
        var pts = new List<StrokePoint>();
        for (var i = 0; i <= 120; i++) pts.Add(new StrokePoint(-20 + (i * 6.2), -15 + (Math.Sin(i * 0.11) * 230) + (i * 4.1), 0.5 + (0.4 * Math.Sin(i * 0.05))));
        return new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = brush, Points = pts };
    }

    /// <summary>
    /// The property the live path stands on: a rectangle rendered alone is the
    /// whole render's, to the byte - cut anywhere, through the wettest part,
    /// at every offset from the spread's cells, one pixel wide or tall, and at
    /// the layer's edges - and the whole render is what the engine's stroke is.
    /// At three sizes, because the spread is worked on a grid of 2, 4 and 8
    /// pixels for them (review: one size left the others untested).
    /// </summary>
    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(110)]
    public void APieceIsTheWholeToTheByte(int size)
    {
        using var layer = Render();
        var brush = Water(size: size);
        brush.TextureSurface = PaperKind.Fibre;
        brush.TextureScale = 8;
        brush.TextureSettle = 0.4;
        var stroke = Edges(brush);
        var dabs = BrushEngine.WalkDabs(stroke);
        var pre = layer.GetPixelSpan();

        var whole = new byte[W * H * 4];
        WetBleed.Render(pre, W, H, layer.RowBytes, dabs, brush, new SKRectI(0, 0, W, H), whole);

        // Columns cut at every offset from an 8-pixel cell, rows likewise, with a
        // one-pixel column and a one-pixel row among them.
        var xs = new[] { 0, 1, 2, 9, 13, 14, 15, 16, 61, 62, 200, 201, 333, 450, 451, 452, 605, W - 3, W };
        var ys = new[] { 0, 1, 7, 8, 27, 100, 101, 230, 300, 311, 312, H - 1, H };
        var stitched = new byte[W * H * 4];
        var kept = new WetBleed.Spread(W, H, WetBleed.SpreadRadius(brush));
        for (var yi = 0; yi + 1 < ys.Length; yi++)
        {
            for (var xi = 0; xi + 1 < xs.Length; xi++)
            {
                var piece = new SKRectI(xs[xi], ys[yi], xs[xi + 1], ys[yi + 1]);
                var part = new byte[piece.Width * piece.Height * 4];
                // Half the pieces through a spread kept across them, as the live path keeps one for a stroke.
                WetBleed.Render(pre, W, H, layer.RowBytes, dabs, brush, piece, part, spread: (xi + yi) % 2 == 0 ? kept : null);
                for (var y = 0; y < piece.Height; y++)
                {
                    part.AsSpan(y * piece.Width * 4, piece.Width * 4)
                        .CopyTo(stitched.AsSpan((((piece.Top + y) * W) + piece.Left) * 4, piece.Width * 4));
                }
            }
        }

        var differing = 0;
        for (var i = 0; i < whole.Length; i++)
        {
            if (whole[i] != stitched[i]) differing++;
        }
        var wet = 0;
        for (var i = 3; i < whole.Length; i += 4)
        {
            if (whole[i] != pre[i]) wet++;
        }
        output.WriteLine($"size {size}: {wet} pixels changed, {differing} bytes differ between the whole and {(xs.Length - 1) * (ys.Length - 1)} pieces");
        Assert.True(wet > 1500, $"the stroke changed only {wet} pixels: nothing was tested");
        Assert.True(differing == 0, $"{differing} bytes differ between the whole render and the same render in pieces");
        using var replay = Render(stroke);
        Assert.True(replay.GetPixelSpan().SequenceEqual(whole), "the engine's stroke is not this render");
    }

    /// <summary>
    /// The spread against a plain one written separately here: the layer
    /// averaged onto cells, three box passes on an untiled grid with nothing
    /// outside the layer, read back between cell centres. The engine's version
    /// is tiled and cached, and a tiling or margin fault would make both a
    /// whole render and its pieces wrong the same way - which only a reference
    /// that does not tile can catch (review).
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(20)]
    [InlineData(40)]
    public void TheSpreadIsAPlainBlurOfTheLayer(int k)
    {
        const int w = 150, h = 110;
        var pre = new byte[w * h * 4];
        var random = new Random(k);
        for (var i = 0; i < w * h; i++)
        {
            var a = random.Next(3) == 0 ? 0 : random.Next(256);
            pre[(i * 4) + 3] = (byte)a;
            for (var c = 0; c < 3; c++) pre[(i * 4) + c] = (byte)(a == 0 ? 0 : random.Next(a + 1));
        }

        var engine = new byte[w * h * 4];
        new WetBleed.Spread(w, h, k).Read(pre, w * 4, new SKRectI(0, 0, w, h), engine);
        var reference = PlainSpread(pre, w, h, k);
        var worst = 0;
        for (var i = 0; i < engine.Length; i++) worst = Math.Max(worst, Math.Abs(engine[i] - reference[i]));
        output.WriteLine($"k {k}: worst difference from the plain blur {worst}");
        Assert.Equal(0, worst);
    }

    private static byte[] PlainSpread(byte[] pre, int w, int h, int k)
    {
        var d = WetBleed.Cell(k);
        var ks = Math.Max(1, (int)Math.Round(k / (double)d));
        // Enough cells around the layer that three passes from nothing reach no further.
        var pad = (ks * 3) + 2;
        int cw = ((w + d - 1) / d) + (pad * 2), ch = ((h + d - 1) / d) + (pad * 2);
        var result = new byte[w * h * 4];
        for (var c = 0; c < 4; c++)
        {
            var grid = new int[cw * ch];
            for (var cy = 0; cy < ch; cy++)
            {
                for (var cx = 0; cx < cw; cx++)
                {
                    int px0 = (cx - pad) * d, py0 = (cy - pad) * d, sum = 0;
                    for (var y = Math.Max(0, py0); y < Math.Min(h, py0 + d); y++)
                    {
                        for (var x = Math.Max(0, px0); x < Math.Min(w, px0 + d); x++) sum += pre[(((y * w) + x) * 4) + c];
                    }
                    grid[(cy * cw) + cx] = ((sum << 4) + ((d * d) >> 1)) / (d * d);
                }
            }
            var window = (2 * ks) + 1;
            for (var pass = 0; pass < 3; pass++)
            {
                var rows = new int[grid.Length];
                for (var cy = 0; cy < ch; cy++)
                {
                    for (var cx = 0; cx < cw; cx++)
                    {
                        var sum = 0;
                        for (var t = -ks; t <= ks; t++) sum += cx + t >= 0 && cx + t < cw ? grid[(cy * cw) + cx + t] : 0;
                        rows[(cy * cw) + cx] = (sum + (window >> 1)) / window;
                    }
                }
                for (var cy = 0; cy < ch; cy++)
                {
                    for (var cx = 0; cx < cw; cx++)
                    {
                        var sum = 0;
                        for (var t = -ks; t <= ks; t++) sum += cy + t >= 0 && cy + t < ch ? rows[((cy + t) * cw) + cx] : 0;
                        grid[(cy * cw) + cx] = (sum + (window >> 1)) / window;
                    }
                }
            }
            for (var y = 0; y < h; y++)
            {
                var v = ((y + 0.5) / d) - 0.5;
                var iy = (int)Math.Floor(v);
                var fy = (float)(v - iy);
                for (var x = 0; x < w; x++)
                {
                    var u = ((x + 0.5) / d) - 0.5;
                    var ix = (int)Math.Floor(u);
                    var fx = (float)(u - ix);
                    int at(int gx, int gy) => grid[((gy + pad) * cw) + gx + pad];
                    var top = at(ix, iy) + ((at(ix + 1, iy) - at(ix, iy)) * fx);
                    var bottom = at(ix, iy + 1) + ((at(ix + 1, iy + 1) - at(ix, iy + 1)) * fx);
                    result[(((y * w) + x) * 4) + c] = (byte)Math.Clamp(((top + ((bottom - top) * fy)) / 16f) + 0.5f, 0f, 255f);
                }
            }
        }
        return result;
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
