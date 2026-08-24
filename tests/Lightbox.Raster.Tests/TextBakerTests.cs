using Lightbox.Core.Documents;
using Lightbox.Raster.Text;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Type becoming drawing: shaping, glyph outlines, and the promise that the
/// picture never depends on the font again afterwards.
/// </summary>
/// <remarks>
/// <b>These run against whatever face this machine offers</b>, so nothing here
/// asserts a coordinate. Every assertion is either structural (a counter is a
/// hole, a space makes no stroke) or a comparison between two bakes on the same
/// machine (moving the baseline moves the glyphs by exactly that much) — which
/// is what the design actually promises. A test pinned to one foundry's outlines
/// would fail on a machine with a different default font and say nothing about
/// whether the baker works.
/// </remarks>
public class TextBakerTests(ITestOutputHelper output)
{
    /// <summary>A real face with real outlines — the default, which every platform has.</summary>
    private static SKTypeface Face() => SKTypeface.Default;

    private static TextElement Set(
        string words, double size = 64, double x = 20, double y = 100,
        TextAlign align = TextAlign.Left, double tracking = 0, double? lineHeight = null) =>
        new()
        {
            Id = "txt1",
            Text = words,
            Size = size,
            X = x,
            Y = y,
            Align = align,
            Tracking = tracking,
            LineHeight = lineHeight,
            Font = new FontRef { Family = SKTypeface.Default.FamilyName },
        };

    private static Stroke Paint() => new() { Color = "#101010" };

    private static List<Stroke> Bake(TextElement text) => TextBaker.Bake(text, Face(), Paint());

    [Fact]
    public void EachGlyphIsOneContourFillCarryingItsElement()
    {
        var strokes = Bake(Set("AB"));

        Assert.Equal(2, strokes.Count);
        Assert.All(strokes, s =>
        {
            Assert.Equal(ToolKind.Text, s.Tool);
            Assert.Equal("txt1", s.TextId);
            Assert.True(s.Points.Count >= 3, $"a glyph contour needs 3 points, had {s.Points.Count}");
            Assert.Equal("#101010", s.Color);
        });
        // Distinct ids: they are separate marks, and a duplicated id would make
        // undo, picking and the stroke index disagree about how many there are.
        Assert.Equal(2, strokes.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void ASpaceMakesNoStroke()
    {
        Assert.Equal(2, Bake(Set("A B")).Count);
        Assert.Empty(Bake(Set("   ")));
        Assert.Empty(Bake(Set("")));
    }

    [Fact]
    public void ACounterBecomesAHole()
    {
        var o = Assert.Single(Bake(Set("o")));

        // The middle of an "o" is a second contour, read even-odd exactly as a
        // flood fill's holes are — which is the whole reason type needed no new
        // rendering path.
        Assert.NotNull(o.Holes);
        Assert.NotEmpty(o.Holes!);
        output.WriteLine($"o: outer {o.Points.Count} points, {o.Holes!.Count} hole(s)");
    }

    [Fact]
    public void BakingTheSameWordsTwiceGivesTheSameGeometry()
    {
        var first = Bake(Set("Hamburgefonstiv"));
        var second = Bake(Set("Hamburgefonstiv"));

        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Points.Count, second[i].Points.Count);
            for (var k = 0; k < first[i].Points.Count; k++)
            {
                // To the bit. Flattening is a closed formula over the control
                // points, so there is no adaptive recursion whose depth could
                // differ — see GlyphOutline.
                Assert.Equal(first[i].Points[k].X, second[i].Points[k].X);
                Assert.Equal(first[i].Points[k].Y, second[i].Points[k].Y);
            }
        }
    }

    [Fact]
    public void MovingTheBaselineMovesEveryGlyphByExactlyThat()
    {
        var here = Bake(Set("type", y: 100));
        var lower = Bake(Set("type", y: 130));

        Assert.Equal(here.Count, lower.Count);
        for (var i = 0; i < here.Count; i++)
        {
            for (var k = 0; k < here[i].Points.Count; k++)
            {
                Assert.Equal(here[i].Points[k].X, lower[i].Points[k].X, 6);
                Assert.Equal(here[i].Points[k].Y + 30, lower[i].Points[k].Y, 6);
            }
        }
    }

    [Fact]
    public void CentringPutsHalfTheLineEitherSideOfTheOrigin()
    {
        var left = Bake(Set("centre", x: 200, align: TextAlign.Left));
        var centred = Bake(Set("centre", x: 200, align: TextAlign.Centre));
        var right = Bake(Set("centre", x: 200, align: TextAlign.Right));

        var leftStart = left.Min(s => s.Points.Min(p => p.X));
        var centredStart = centred.Min(s => s.Points.Min(p => p.X));
        var rightEnd = right.Max(s => s.Points.Max(p => p.X));

        Assert.True(centredStart < leftStart, "centred type starts left of left-aligned type");
        Assert.True(rightEnd <= 200.5, $"right-aligned type ends at its origin, ended at {rightEnd}");
        output.WriteLine($"left starts {leftStart:F1}, centred starts {centredStart:F1}, right ends {rightEnd:F1}");
    }

    [Fact]
    public void TrackingPushesTheLettersApart()
    {
        var tight = Bake(Set("spacing"));
        var loose = Bake(Set("spacing", tracking: 200));

        var tightWidth = tight.Max(s => s.Points.Max(p => p.X)) - tight.Min(s => s.Points.Min(p => p.X));
        var looseWidth = loose.Max(s => s.Points.Max(p => p.X)) - loose.Min(s => s.Points.Min(p => p.X));

        // 200/1000 em at 64px is 12.8px per glyph, over six gaps.
        Assert.True(looseWidth > tightWidth + 60, $"tracked {looseWidth:F1} vs {tightWidth:F1}");
        output.WriteLine($"tracking 0: {tightWidth:F1}px, tracking 200: {looseWidth:F1}px");
    }

    [Fact]
    public void ANewlineStartsASecondLineBelowTheFirst()
    {
        var one = Bake(Set("no"));
        var two = Bake(Set("no\nno"));

        Assert.Equal(one.Count * 2, two.Count);
        var firstLineBottom = two.Take(one.Count).Max(s => s.Points.Max(p => p.Y));
        var secondLineTop = two.Skip(one.Count).Min(s => s.Points.Min(p => p.Y));
        Assert.True(secondLineTop > firstLineBottom - 1, "the second line sits below the first");
    }

    [Fact]
    public void SettingTheLineHeightSetsTheDistanceBetweenBaselines()
    {
        var text = Set("A\nA", lineHeight: 100);
        var strokes = Bake(text);

        var first = strokes[0].Points.Min(p => p.Y);
        var second = strokes[1].Points.Min(p => p.Y);
        Assert.Equal(100, second - first, 3);
    }

    [Fact]
    public void TheDefaultFaceCanSetType()
    {
        // If this ever fails, the machine has no usable font at all and every
        // other assertion here is meaningless — worth saying separately.
        Assert.True(TextBaker.CanSetType(SKTypeface.Default));
    }

    [Fact]
    public void AFaceTheProbeRejectsWouldHaveSetNothing()
    {
        // The guard the text tool leans on, stated as the property that makes it
        // safe: a "no" here must mean typing in that face really would have
        // produced nothing, rather than the probe being shy. The other direction
        // is deliberately not asserted — a font with no Latin in it passes the
        // probe and still sets no Latin letters, which is correct for both.
        var rejected = 0;
        foreach (var family in SKFontManager.Default.GetFontFamilies())
        {
            using var face = SKTypeface.FromFamilyName(family);
            if (face is null || TextBaker.CanSetType(face)) continue;
            rejected++;
            Assert.Empty(TextBaker.Bake(Set("Hamburgefonstiv 123"), face, Paint()));
        }
        output.WriteLine($"{rejected} installed families have no outlines this can read");
    }

    [Fact]
    public void TypeRendersFromTheRecordWithNoFontAnywhere()
    {
        // The promise the whole design rests on: bake once, and the picture no
        // longer depends on the typeface being available. Rendering happens
        // through the ordinary contour fill with nothing font-shaped in reach.
        var strokes = Bake(Set("Ink", size: 80, x: 10, y: 90));
        FontRegistry.Clear();

        using var bitmap = FrameRasterizer.Rasterize(strokes, 200, 120);
        var inked = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 8) inked++;
            }
        }

        Assert.True(inked > 200, $"three letters at 80px should ink well over 200 pixels, inked {inked}");
        output.WriteLine($"inked {inked} pixels");
    }

    [Fact]
    public void GlyphsAreOpaqueInsideAndNotOutside()
    {
        // A contour fill, so the inside of a stem is solid colour rather than a
        // stroked outline — the thing that would be wrong if the glyph path were
        // walked as a brush path instead of filled.
        var strokes = Bake(Set("H", size: 200, x: 20, y: 200));
        using var bitmap = FrameRasterizer.Rasterize(strokes, 240, 240);

        var solid = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 255) solid++;
            }
        }

        Assert.True(solid > 500, $"a 200px H should have a solid interior, had {solid} opaque pixels");
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void RenderingBiggerSharpensTypeRatherThanScalingIt()
    {
        // Invariant 7 from the type side: output scale is a canvas transform, so
        // the same contours rasterise sharp at 2× rather than being magnified.
        // The geometry is untouched, which is what makes that safe.
        var strokes = Bake(Set("O", size: 100, x: 10, y: 110));
        var before = strokes[0].Points.Select(p => (p.X, p.Y)).ToList();

        using var doubled = FrameRasterizer.Rasterize(strokes, 140, 140, outputScale: 2.0);

        Assert.Equal(280, doubled.Width);
        Assert.Equal(before, strokes[0].Points.Select(p => (p.X, p.Y)).ToList());
    }
}
