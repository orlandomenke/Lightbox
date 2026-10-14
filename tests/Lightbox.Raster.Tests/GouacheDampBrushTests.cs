using Lightbox.Core.Documents;
using Lightbox.Raster;
using Lightbox.Raster.Tips;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// The damp brush for Gouache (Q255): body colour that has dried is worked
/// again - the colours under the brush blend into each other through the
/// brush's tip, a short way along the stroke - and the brush lays no colour
/// of its own.
/// </summary>
/// <remarks>
/// Chosen by the owner from a sheet: "the same but gentler", of the candidates
/// that blend through the tip. It is a smudge brush in its dulling mode with
/// the gouache brush's tip; these tests hold the three things the choice was
/// for, measured below saturation - flow is low on purpose, one pass moves a
/// little.
/// </remarks>
public class GouacheDampBrushTests(ITestOutputHelper output)
{
    private const int W = 520;
    private const int H = 220;

    /// <summary>The settings the shipped Damp brush has; the App suite holds the preset to these.</summary>
    internal static BrushSettings Damp(double size = 40) => new()
    {
        Size = size, Hardness = 0.5, Flow = 0.3, Spacing = 0.08, Kind = BrushKind.Smudge,
        SmudgeMode = SmudgeMode.Dulling, SmudgeLength = 0.45, SmudgeRadius = 0.7, ColorRate = 0,
        TipId = "tip-builtin-paintbrush", AngleFollowsDirection = true,
    };

    /// <summary>A blue bar and a red bar side by side, touching at x 260: hard, opaque.</summary>
    private static List<Stroke> Bars()
    {
        var ink = new BrushSettings { Size = 70, Hardness = 0.95, Opacity = 1, Flow = 1, Spacing = 0.08 };
        Stroke Bar(string colour, double x0, double x1)
        {
            var pts = new List<StrokePoint>();
            for (var x = x0; x <= x1; x += 6) pts.Add(new StrokePoint(x, 110, 1));
            return new Stroke { Tool = ToolKind.Brush, Color = colour, Brush = ink.Clone(), Points = pts };
        }
        return [Bar("#2040c0", 90, 225), Bar("#c03020", 295, 430)];
    }

    private static Stroke Scrub(BrushSettings brush, string colour = "#000000")
    {
        var pts = new List<StrokePoint>();
        // Back and forth across where the two bars meet, six times.
        for (var i = 0; i <= 120; i++) pts.Add(new StrokePoint(260 + (Math.Sin(i * 0.31) * 60), 100 + (i * 0.15), 0.8));
        return new Stroke { Tool = ToolKind.Brush, Color = colour, Brush = brush, Points = pts };
    }

    private static SKBitmap Render(params Stroke[] extra)
    {
        BrushTipRegistry.Register(TipCatalogue.All.ToDictionary(t => t.Id, t => t.Png));
        return FrameRasterizer.Rasterize([.. Bars(), .. extra], W, H, 1.0);
    }

    /// <summary>Mean unpremultiplied red and blue, and mean alpha, over a box.</summary>
    private static (double Red, double Blue, double Alpha) Mean(SKBitmap bitmap, int x0, int y0, int x1, int y1)
    {
        var px = bitmap.GetPixelSpan();
        double r = 0, b = 0, a = 0;
        var n = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = ((y * bitmap.Width) + x) * 4;
                r += px[i]; b += px[i + 2]; a += px[i + 3];
                n++;
            }
        }
        return (r / Math.Max(a, 1) * 255, b / Math.Max(a, 1) * 255, a / n);
    }

    [Fact]
    public void WhereTwoColoursMeetTheyBlend()
    {
        using var dry = Render();
        using var worked = Render(Scrub(Damp()));
        // Just inside the blue bar, and just inside the red, by the join.
        var blueBefore = Mean(dry, 225, 100, 250, 120);
        var blueAfter = Mean(worked, 225, 100, 250, 120);
        var redBefore = Mean(dry, 270, 100, 295, 120);
        var redAfter = Mean(worked, 270, 100, 295, 120);
        output.WriteLine(
            $"blue side: red {blueBefore.Red:0} to {blueAfter.Red:0}, blue {blueBefore.Blue:0} to {blueAfter.Blue:0}; "
            + $"red side: red {redBefore.Red:0} to {redAfter.Red:0}, blue {redBefore.Blue:0} to {redAfter.Blue:0}");
        // Gentle on purpose: a scrub of six passes moved each side nine levels toward
        // the other when this was written. With the flow at zero it moves none.
        Assert.True(blueAfter.Red > blueBefore.Red + 5, $"no red reached the blue side: {blueBefore.Red:0} to {blueAfter.Red:0}");
        Assert.True(redAfter.Blue > redBefore.Blue + 5, $"no blue reached the red side: {redBefore.Blue:0} to {redAfter.Blue:0}");
        // Blended, not replaced: each side is still mostly its own colour.
        Assert.True(blueAfter.Blue > blueAfter.Red, "the blue side turned red");
        Assert.True(redAfter.Red > redAfter.Blue, "the red side turned blue");
    }

    [Fact]
    public void ItLaysNoColourOfItsOwn()
    {
        // The colour in hand is not in the mark at all.
        using var black = Render(Scrub(Damp(), "#000000"));
        using var green = Render(Scrub(Damp(), "#00ff00"));
        Assert.True(black.GetPixelSpan().SequenceEqual(green.GetPixelSpan()), "the stroke's colour reached the canvas");

        // And over bare paper it leaves bare paper.
        var pts = new List<StrokePoint>();
        for (var x = 60; x <= 460; x += 8) pts.Add(new StrokePoint(x, 30, 0.8));
        using var dry = Render();
        using var far = Render(new Stroke { Tool = ToolKind.Brush, Color = "#000000", Brush = Damp(30), Points = pts });
        Assert.Equal(Mean(dry, 0, 0, W, 50).Alpha, Mean(far, 0, 0, W, 50).Alpha);
    }

    [Fact]
    public void OnePassIsGentle_AndGoingOverItDoesMore()
    {
        using var dry = Render();
        using var once = Render(Scrub(Damp()));
        using var twice = Render(Scrub(Damp()), Scrub(Damp()));
        double RedOnTheBlueSide(SKBitmap b) => Mean(b, 225, 100, 250, 120).Red;
        var none = RedOnTheBlueSide(dry);
        var one = RedOnTheBlueSide(once);
        var two = RedOnTheBlueSide(twice);
        output.WriteLine($"red on the blue side: {none:0} dry, {one:0} after one scrub, {two:0} after two");
        Assert.True(one < none + 40, $"one scrub took the blue side {one - none:0} levels toward red: not gentle");
        Assert.True(two > one + 1.5, $"a second scrub did no more than the first: {one:0} then {two:0}");
    }

    [Fact]
    public void WhatItDidNotTouchIsUntouched()
    {
        using var dry = Render();
        using var worked = Render(Scrub(Damp()));
        var a = dry.GetPixelSpan();
        var b = worked.GetPixelSpan();
        var changed = 0;
        for (var y = 0; y < H; y++)
        {
            // Left of x 150 and right of x 370: the scrub runs 200 to 320, a brush of 40.
            for (var x = 0; x < W; x++)
            {
                if (x is >= 150 and < 370) continue;
                var i = ((y * W) + x) * 4;
                if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) changed++;
            }
        }
        Assert.True(changed == 0, $"{changed} pixels changed where the brush never was");
    }
}
