using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B450: repainting a rectangle of a drawing costs what the rectangle covers,
/// and still leaves exactly the pixels a whole render would.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured before any of this was written</b> (2026-10-12, the owner-shaped
/// fixture's first drawing, twenty strokes at 1920×1080): the whole drawing
/// rendered in 221 ms; a 200-pixel square of it that every stroke happened to
/// cross took 204 ms. A region repaint stamped each stroke it touched into a
/// scratch the size of the <em>stroke</em> and let the canvas clip throw the
/// rest away, so it was priced by strokes touched rather than by pixels
/// covered — which is why undoing, filling under line work and reshaping a
/// line all cost the drawing wherever strokes overlap.
/// </para>
/// <para>
/// <b>Two halves, and the first is the one that matters.</b> Every brush in
/// the application renders through this path, so the exactness tests run every
/// built-in brush and compare a patched drawing with a whole render pixel for
/// pixel: a patch that is fast and one grey level off is artwork quietly
/// changed. The cost test asks the engine how many scratch pixels it used
/// rather than a clock, for the reason the performance budgets give.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class RegionRepaintWindowTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 640, H = 400;

    public static IEnumerable<object[]> BuiltInBrushes() =>
        BuiltInPresets.Create().Select(p => new object[] { p.Id });

    /// <summary>
    /// Twelve strokes of one brush, fanned across the page so every region has
    /// several passing through it and several passing by.
    /// </summary>
    private static Frame Drawing(BrushSettings brush, bool eraseSome = false)
    {
        var frame = new Frame { Id = "drawing" };
        for (var i = 0; i < 12; i++)
        {
            var stroke = new Stroke
            {
                Tool = eraseSome && i % 5 == 4 ? ToolKind.Eraser : ToolKind.Brush,
                Color = (i % 3) switch { 0 => "#203040", 1 => "#a03020", _ => "#207040" },
                Brush = brush.Clone(),
            };
            // A long diagonal sweep whose slope changes per stroke, with a
            // wobble, and pressure that rises and falls along it.
            var y0 = 30 + i * 28;
            var y1 = H - 30 - i * 24;
            for (var k = 0; k <= 40; k++)
            {
                var t = k / 40.0;
                stroke.Points.Add(new StrokePoint
                {
                    X = 20 + t * (W - 40),
                    Y = y0 + (y1 - y0) * t + Math.Sin(t * 9 + i) * 14,
                    Pressure = 0.35 + 0.6 * Math.Sin(t * Math.PI),
                });
            }
            frame.Strokes.Add(stroke);
        }
        return frame;
    }

    private static readonly (string Name, SKRectI Rect)[] Regions =
    [
        ("a small square in the middle", new SKRectI(280, 160, 360, 240)),
        ("a strip down the left edge", new SKRectI(0, 0, 70, H)),
        ("an odd-sized piece off the corner", new SKRectI(411, 203, 640, 400)),
        ("most of the page", new SKRectI(40, 30, 600, 370)),
    ];

    private static (long Differing, int Worst) Compare(SKBitmap a, SKBitmap b)
    {
        long differing = 0;
        var worst = 0;
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        for (var i = 0; i < sa.Length; i++)
        {
            var d = Math.Abs(sa[i] - sb[i]);
            if (d == 0) continue;
            worst = Math.Max(worst, d);
            differing++;
        }
        return (differing, worst);
    }

    /// <summary>
    /// Clear a rectangle of a finished drawing and ask for it back: what comes
    /// back is the drawing, to the last bit of the last channel.
    /// </summary>
    [Theory]
    [MemberData(nameof(BuiltInBrushes))]
    public void APatchedDrawingIsTheWholeRenderForEveryBuiltInBrush(string presetId)
    {
        var preset = BuiltInPresets.Create().First(p => p.Id == presetId);
        var frame = Drawing(preset.Settings);
        using var whole = FrameRasterizer.Materialize(frame, W, H);

        foreach (var (name, rect) in Regions)
        {
            using var patched = whole.Copy();
            if (!FrameRasterizer.RepaintRegion(patched, frame, rect, W, H))
            {
                // A brush the patch refuses falls back to the whole render,
                // which is the safe direction; nothing to compare.
                output.WriteLine($"{presetId}, {name}: refused");
                continue;
            }
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"{presetId}, {name}: differing {differing}, worst {worst}");
            Assert.True(differing == 0, $"{presetId}, {name}: {differing} bytes differ, worst by {worst}");
        }
    }

    /// <summary>
    /// One brush for each option the window admits, so "exact" is asked of
    /// every path a windowed stroke can take through the engine and not only
    /// of the brushes that happen to ship.
    /// </summary>
    public static IEnumerable<object[]> Options() =>
    [
        ["plain"], ["soft"], ["large and hard"], ["large and soft"], ["wet edge"], ["scattered"],
        ["thin paint"], ["bristles"], ["aliased"], ["flat and turning"], ["colour jitter"],
        ["multiply"], ["no pressure"], ["tight spacing"],
    ];

    private static BrushSettings Option(string name)
    {
        var b = new BrushSettings { Size = 14, Hardness = 0.8, Opacity = 1, Flow = 1 };
        switch (name)
        {
            case "soft": b.Hardness = 0.15; break;
            case "large and hard": b.Size = 70; b.Hardness = 1; break;
            case "large and soft": b.Size = 90; b.Hardness = 0.1; b.Flow = 0.4; break;
            case "wet edge": b.WetEdge = 0.7; b.Hardness = 0.6; b.Size = 30; break;
            case "scattered": b.Scatter = 0.8; b.SizeJitter = 0.5; b.FlowJitter = 0.5; b.RotationJitter = 0.6; b.RoundnessJitter = 0.4; break;
            case "thin paint": b.Opacity = 0.4; b.Flow = 0.25; b.Size = 26; break;
            case "bristles": b.Bristles = 0.6; b.Size = 34; break;
            case "aliased": b.AntiAlias = false; b.Hardness = 1; break;
            case "flat and turning": b.Roundness = 0.35; b.AngleFollowsDirection = true; b.Size = 28; break;
            case "colour jitter": b.ColorJitter = 0.6; b.HueJitter = 0.4; b.BrightnessJitter = 0.3; b.SecondaryColor = "#3050c0"; break;
            case "multiply": b.Blend = LayerBlendMode.Multiply; b.Opacity = 0.7; b.Size = 30; break;
            case "no pressure": b.PressureEnabled = false; b.Size = 20; break;
            case "tight spacing": b.Spacing = 0.03; b.Size = 22; b.Hardness = 0.5; break;
        }
        return b;
    }

    /// <summary>
    /// Exact for every option, and — said out loud for each — whether it took
    /// the window or was stamped whole. An option quietly falling back would
    /// keep this green and give the fix away.
    /// </summary>
    [Theory]
    [MemberData(nameof(Options))]
    public void APatchedDrawingIsTheWholeRenderForEveryOptionTheWindowAdmits(string option)
    {
        var frame = Drawing(Option(option));
        var before = BrushEngine.ScratchPixels;
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        var forWhole = BrushEngine.ScratchPixels - before;

        foreach (var (name, rect) in Regions)
        {
            using var patched = whole.Copy();
            before = BrushEngine.ScratchPixels;
            Assert.True(FrameRasterizer.RepaintRegion(patched, frame, rect, W, H), $"{option}: refused");
            var share = (double)(BrushEngine.ScratchPixels - before) / forWhole;
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"{option}, {name}: differing {differing}, worst {worst}, scratch {share:P0} of the whole");
            Assert.True(differing == 0, $"{option}, {name}: {differing} bytes differ, worst by {worst}");
        }

        // The small square: every one of these options is one the window takes.
        using var small = whole.Copy();
        before = BrushEngine.ScratchPixels;
        FrameRasterizer.RepaintRegion(small, frame, Regions[0].Rect, W, H);
        Assert.True(
            BrushEngine.ScratchPixels - before < forWhole / 2,
            $"{option} was stamped whole: the window did not take it");
    }

    /// <summary>
    /// At twice the resolution — an export's render — a patch is still the
    /// whole render, because there the strokes are stamped whole.
    /// </summary>
    /// <remarks>
    /// <b>This test is why.</b> The first cut windowed every scale, and at 2x a
    /// dozen bytes of each patch came out one or two levels different: away
    /// from the document's own plane a stroke's scratch sits under a
    /// translation, a smaller scratch moves it, and Skia's gradients do not
    /// round the same way after a move. The engine says the same thing where
    /// it explains the plane. So the window is taken only on the plane, and
    /// this holds the rest to what they always did.
    /// </remarks>
    [Theory]
    [InlineData("plain")]
    [InlineData("soft")]
    [InlineData("wet edge")]
    [InlineData("scattered")]
    public void APatchAtTwiceTheResolutionIsTheWholeRender(string option)
    {
        var frame = Drawing(Option(option));
        using var whole = FrameRasterizer.Materialize(frame, W, H, outputScale: 2.0);
        foreach (var (name, rect) in Regions)
        {
            var surface = new SKRectI(rect.Left * 2 + 1, rect.Top * 2 + 1, rect.Right * 2 - 1, rect.Bottom * 2 - 1);
            using var patched = whole.Copy();
            Assert.True(FrameRasterizer.RepaintRegion(patched, frame, surface, W, H, outputScale: 2.0));
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"{option} at 2x, {name}: differing {differing}, worst {worst}");
            Assert.True(differing == 0, $"{option} at 2x, {name}: {differing} bytes differ, worst by {worst}");
        }
    }

    /// <summary>
    /// What the window refuses is still right: a brush whose passes seed from
    /// the scratch's corner is stamped whole, as it always was.
    /// </summary>
    [Theory]
    [InlineData(0.6, false)]
    [InlineData(0.0, true)]
    public void ABrushTheWindowRefusesIsStillExact(double granulation, bool textured)
    {
        var brush = new BrushSettings { Size = 26, Hardness = 0.6, Granulation = granulation };
        if (textured) brush.TextureSurface = PaperKind.Rough;
        var frame = Drawing(brush);
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        foreach (var (name, rect) in Regions)
        {
            using var patched = whole.Copy();
            if (!FrameRasterizer.RepaintRegion(patched, frame, rect, W, H)) continue;
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"granulation {granulation}, textured {textured}, {name}: differing {differing}, worst {worst}");
            Assert.True(differing == 0, $"{name}: {differing} bytes differ, worst by {worst}");
        }
    }

    /// <summary>
    /// Strokes painted inside a selection — hard-edged and feathered — are
    /// patched exactly.
    /// </summary>
    /// <remarks>
    /// The adversary's open angle on the window: a stroke's selection mask is
    /// rasterised in the scratch, under a translation by the scratch's corner,
    /// so a cut scratch moves the translation — the same kind of move that put
    /// a level's difference into a patch at 2x. A slanted contour with
    /// fractional corners is what would show it. Such strokes are stamped
    /// whole; this holds them to the whole render either way.
    /// </remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(6.0)]
    public void APatchedDrawingOfStrokesInsideASelectionIsTheWholeRender(double feather)
    {
        var clipId = $"clip_b450_{feather}";
        List<StrokePoint> triangle =
        [
            new() { X = 3.1, Y = 5.7 }, new() { X = 600.3, Y = 40.9 }, new() { X = 200.7, Y = 390.2 },
        ];
        ClipRegionRegistry.Register(clipId, new ClipRegion { Contours = [triangle], Feather = feather });

        var frame = Drawing(Option("plain"));
        foreach (var stroke in frame.Strokes) stroke.ClipId = clipId;
        using var whole = FrameRasterizer.Materialize(frame, W, H);

        // Squares the slanted edges cross, at offsets that are not multiples of anything.
        SKRectI[] regions =
        [
            new(271, 13, 331, 73), new(383, 197, 443, 257), new(87, 161, 147, 221), new(40, 30, 600, 370),
        ];
        foreach (var rect in regions)
        {
            using var patched = whole.Copy();
            Assert.True(FrameRasterizer.RepaintRegion(patched, frame, rect, W, H));
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"feather {feather}, region at {rect.Left},{rect.Top}: differing {differing}, worst {worst}");
            Assert.True(differing == 0, $"feather {feather} at {rect.Left},{rect.Top}: {differing} bytes differ, worst by {worst}");
        }
    }

    /// <summary>Alpha-locked strokes over earlier paint are patched exactly.</summary>
    [Fact]
    public void APatchedDrawingWithAlphaLockedStrokesIsTheWholeRender()
    {
        var frame = Drawing(Option("large and soft"));
        for (var i = 6; i < frame.Strokes.Count; i++)
        {
            frame.Strokes[i].AlphaLocked = true;
            frame.Strokes[i].Brush = Option("plain");
        }
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        foreach (var (name, rect) in Regions)
        {
            using var patched = whole.Copy();
            Assert.True(FrameRasterizer.RepaintRegion(patched, frame, rect, W, H));
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"alpha-locked, {name}: differing {differing}, worst {worst}");
            Assert.True(differing == 0, $"alpha-locked, {name}: {differing} bytes differ, worst by {worst}");
        }
    }

    /// <summary>The same, with eraser strokes among the paint: a patch has to take away what was taken away.</summary>
    [Theory]
    [InlineData("builtin-ink")]
    [InlineData("builtin-pencil")]
    [InlineData("builtin-soft-round")]
    public void APatchedDrawingWithErasingInItIsTheWholeRender(string presetId)
    {
        var preset = BuiltInPresets.Create().First(p => p.Id == presetId);
        var frame = Drawing(preset.Settings, eraseSome: true);
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        foreach (var (name, rect) in Regions)
        {
            using var patched = whole.Copy();
            if (!FrameRasterizer.RepaintRegion(patched, frame, rect, W, H)) continue;
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"{presetId} with erasing, {name}: differing {differing}, worst {worst}");
            Assert.True(differing == 0, $"{presetId}, {name}: {differing} bytes differ, worst by {worst}");
        }
    }

    /// <summary>
    /// A mirrored stroke's copies are patched too: the window is asked of each
    /// copy, not of the stroke as it was drawn.
    /// </summary>
    [Fact]
    public void APatchedDrawingOfMirroredStrokesIsTheWholeRender()
    {
        var preset = BuiltInPresets.Create().First(p => p.Id == "builtin-ink");
        var frame = Drawing(preset.Settings);
        foreach (var stroke in frame.Strokes)
        {
            stroke.Symmetry = new SymmetryAxis { CenterX = W / 2.0, CenterY = H / 2.0, Mirror = true, Order = 3 };
        }
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        foreach (var (name, rect) in Regions)
        {
            using var patched = whole.Copy();
            if (!FrameRasterizer.RepaintRegion(patched, frame, rect, W, H)) continue;
            var (differing, worst) = Compare(patched, whole);
            output.WriteLine($"mirrored ink, {name}: differing {differing}, worst {worst}");
            Assert.True(differing == 0, $"{name}: {differing} bytes differ, worst by {worst}");
        }
    }

    /// <summary>
    /// The cost half: a small patch on a busy drawing uses scratch in
    /// proportion to the patch, not to the strokes that cross it.
    /// </summary>
    /// <remarks>
    /// Asked of the engine's own count of scratch pixels rather than of a
    /// clock. All twelve strokes cross the square, so before the fix the patch
    /// used every stroke's whole scratch — the same number as the whole
    /// drawing, which is the measurement in this class's remarks seen from
    /// inside.
    /// </remarks>
    [Fact]
    public void ARegionRepaintStampsOnlyTheDabsThatReachIt()
    {
        var preset = BuiltInPresets.Create().First(p => p.Id == "builtin-ink");
        var frame = Drawing(preset.Settings);

        var before = BrushEngine.ScratchPixels;
        using var whole = FrameRasterizer.Materialize(frame, W, H);
        var forWhole = BrushEngine.ScratchPixels - before;

        var square = Regions[0].Rect;
        using var patched = whole.Copy();
        before = BrushEngine.ScratchPixels;
        Assert.True(FrameRasterizer.RepaintRegion(patched, frame, square, W, H));
        var forPatch = BrushEngine.ScratchPixels - before;

        var share = (double)forPatch / forWhole;
        var area = (double)square.Width * square.Height / (W * H);
        output.WriteLine(
            $"scratch pixels: whole drawing {forWhole:N0}, an {square.Width}×{square.Height} patch {forPatch:N0} "
            + $"({share:P1} of the drawing's, for {area:P1} of its area)");
        Assert.True(forWhole > 0);
        // Ten times the patch's share of the page is generous room for the
        // margin a brush needs round the window; before the fix this was 100%.
        Assert.True(share < area * 10, $"the patch used {share:P1} of the whole drawing's scratch");
    }
}
