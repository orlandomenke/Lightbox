using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Pillar 3, step two: a placement becomes pixels.
/// </summary>
/// <remarks>
/// The interesting test in here is not that a symbol draws. It is
/// <see cref="TheSameSymbolPlacedTwiceIsPixelIdentical"/>: every dab dynamic is
/// seeded from a dab's position, so a naive implementation that rewrote a
/// placement's coordinates would give the same sword a different grain in each
/// place it was put — fine on one drawing, boiling at twelve frames a second.
/// The design says so out loud, and says that if this test cannot be made to
/// pass without touching <c>Hash01</c> then the design is wrong rather than the
/// engine. It passes.
/// </remarks>
[Collection("Registries")]
public class SymbolRenderTests : IDisposable
{
    private const int W = 200, H = 160;

    public SymbolRenderTests() => SymbolRegistry.Clear();

    public void Dispose() => SymbolRegistry.Clear();

    /// <summary>
    /// A stroke with every dynamic that <c>Hash01</c> seeds turned up, so a
    /// re-rolled seed shows as different pixels rather than as nothing.
    /// </summary>
    private static Stroke Jittery(double x, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 30, y + 20, 1)],
        Brush = new BrushSettings
        {
            Size = 12,
            Hardness = 0.8,
            Opacity = 1,
            Flow = 1,
            Spacing = 0.15,
            Scatter = 0.6,
            SizeJitter = 0.5,
            HueJitter = 0.3,
        },
    };

    private static Symbol Sword(params Stroke[] strokes) => new()
    {
        Name = "Sword",
        PivotX = 10,
        PivotY = 10,
        Layers = Symbol.Flat("Sword", [new Frame { Strokes = [.. strokes] }]),
    };

    private static Symbol Cycle(int frames)
    {
        var symbol = new Symbol { Name = "Walk", PivotX = 0, PivotY = 0 };
        var drawings = new List<Frame>();
        for (var i = 0; i < frames; i++)
        {
            // One frame per row, so which frame rendered is readable from a
            // single pixel rather than from a hash.
            drawings.Add(new Frame
            {
                Strokes =
                [
                    new Stroke
                    {
                        Tool = ToolKind.Brush,
                        Color = "#000000",
                        Points = [new StrokePoint(10, 10 + i * 20, 1), new StrokePoint(40, 10 + i * 20, 1)],
                        Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
                    },
                ],
            });
        }
        symbol.Layers = Symbol.Flat("Walk", drawings);
        return symbol;
    }

    private static Frame Placing(params SymbolPlacement[] placements) =>
        new() { Placements = [.. placements] };

    private static SKBitmap Render(Frame frame, int celIndex = 0, double outputScale = 1.0) =>
        FrameRasterizer.Materialize(frame, W, H, outputScale, celIndex);

    /// <summary>Every non-transparent pixel, as a set of (x, y, colour).</summary>
    private static SKRectI? Ink(SKBitmap bitmap)
    {
        int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha == 0) continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
        return maxX < 0 ? null : new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    private static int InkCount(SKBitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha != 0) count++;
            }
        }
        return count;
    }

    // ---- the registry ------------------------------------------------------------

    [Fact]
    public void RegisteringASymbolMakesItResolvable()
    {
        var sword = Sword(Jittery(10, 10));

        SymbolRegistry.Register(sword);

        Assert.Same(sword, SymbolRegistry.Resolve(sword.Id));
    }

    [Fact]
    public void ResetDropsWhatTheLastProjectHad()
    {
        var first = Sword(Jittery(10, 10));
        SymbolRegistry.Register(first);

        var second = Sword(Jittery(10, 10));
        SymbolRegistry.Reset(new Dictionary<string, Symbol> { [second.Id] = second });

        Assert.Null(SymbolRegistry.Resolve(first.Id));
        Assert.Same(second, SymbolRegistry.Resolve(second.Id));
    }

    // ---- drawing -----------------------------------------------------------------

    [Fact]
    public void APlacedSymbolReachesThePixels()
    {
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);

        using var rendered = Render(Placing(new SymbolPlacement { SymbolId = sword.Id, X = 100, Y = 60 }));

        Assert.NotNull(Ink(rendered));
    }

    [Fact]
    public void APlacementLandsItsPivotWhereItWasPut()
    {
        // A hand symbol is placed by the wrist, not by the corner of its
        // bounding box. Stated as an equality rather than as a tolerance around
        // an expected position: the same drawing, once with its pivot at the
        // origin and once with the pivot moved to (40, 30), has to land in the
        // same place when the second placement is offset by the same (40, 30).
        // A renderer that ignored the pivot would put them 50 pixels apart, and
        // a tolerance wide enough for the scatter would not have noticed.
        var atOrigin = Sword(Jittery(10, 10));
        atOrigin.PivotX = 0;
        atOrigin.PivotY = 0;
        var offPivot = Sword(Jittery(10, 10));
        offPivot.PivotX = 40;
        offPivot.PivotY = 30;
        SymbolRegistry.Register(atOrigin);
        SymbolRegistry.Register(offPivot);

        using var a = Render(Placing(new SymbolPlacement { SymbolId = atOrigin.Id, X = 60, Y = 50 }));
        using var b = Render(Placing(new SymbolPlacement { SymbolId = offPivot.Id, X = 100, Y = 80 }));

        Assert.NotNull(Ink(a));
        Assert.True(Same(a, b));
    }

    [Fact]
    public void AFrameWithNoPlacementsRendersExactlyAsItAlwaysDid()
    {
        // The absence rule, in pixels: adding the pass must not have changed
        // what a painting looks like.
        var painted = new Frame { Strokes = [Jittery(40, 40)] };

        using var before = FrameRasterizer.Rasterize(painted.Strokes, W, H);
        using var after = Render(painted);

        Assert.True(Same(before, after));
    }

    [Fact]
    public void AnUnresolvedSymbolDrawsNothingAndSaysSo()
    {
        // Never a placeholder in the pixels. A missing asset that renders as a
        // pink box gets exported by somebody in a hurry.
        using var rendered = Render(Placing(new SymbolPlacement { SymbolId = "sym_gone", X = 50, Y = 50 }));

        Assert.Null(Ink(rendered));
        Assert.Contains("sym_gone", SymbolRegistry.Unresolved);
    }

    [Fact]
    public void AZeroOpacityPlacementDrawsNothing()
    {
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);

        using var rendered = Render(
            Placing(new SymbolPlacement { SymbolId = sword.Id, X = 100, Y = 60, Opacity = 0 }));

        Assert.Null(Ink(rendered));
    }

    // ---- the determinism the design rests on ---------------------------------------

    [Fact]
    public void TheSameSymbolPlacedTwiceIsPixelIdentical()
    {
        // The test the design named. Two placements of one sword, in different
        // places: the marks must be the same mark, moved — not two rolls of the
        // same dice. Rewriting the strokes' coordinates would re-roll scatter,
        // size and hue jitter from Hash01 and this would fail.
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);

        using var left = Render(Placing(new SymbolPlacement { SymbolId = sword.Id, X = 40, Y = 40 }));
        using var right = Render(Placing(new SymbolPlacement { SymbolId = sword.Id, X = 120, Y = 40 }));

        Assert.True(SameShiftedBy(left, right, 80, 0));
    }

    [Fact]
    public void TwoPlacementsOnOneCelAreTheSameMarkTwice()
    {
        // The same claim where it actually bites: one cel, one symbol, two
        // uses. This is the arrangement that boils.
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);

        using var both = Render(Placing(
            new SymbolPlacement { SymbolId = sword.Id, X = 30, Y = 40 },
            new SymbolPlacement { SymbolId = sword.Id, X = 110, Y = 40 }));
        using var one = Render(Placing(new SymbolPlacement { SymbolId = sword.Id, X = 30, Y = 40 }));

        Assert.Equal(InkCount(one) * 2, InkCount(both));
        Assert.True(SameShiftedBy(one, both, 0, 0, onlyWhereFirstHasInk: true));
    }

    [Fact]
    public void EditingTheSymbolChangesEveryPlacementOfIt()
    {
        // Pillar 3's headline, at the level the renderer can prove it: the
        // placement holds an id, so the next render sees the new drawing.
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);
        var frame = Placing(new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 60 });

        using var before = Render(frame);
        sword.AllFrames.First().Strokes.Add(Jittery(10, 40));
        sword.Version++;
        using var after = Render(frame);

        Assert.True(InkCount(after) > InkCount(before));
    }

    [Fact]
    public void AnEditThatForgetsToBumpTheVersionServesTheOldDrawing()
    {
        // Written down because it is a contract, not an accident: the render
        // cache is keyed by Symbol.Version, so whatever edits a symbol has to
        // bump it. Asserted rather than left implicit so the step that adds
        // symbol editing finds this instead of finding the bug.
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);
        var frame = Placing(new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 60 });
        using var before = Render(frame);

        sword.AllFrames.First().Strokes.Add(Jittery(10, 40));
        using var after = Render(frame);

        Assert.Equal(InkCount(before), InkCount(after));
    }

    // ---- the transform is a canvas transform ------------------------------------

    [Fact]
    public void ScalingAPlacementDoesNotRewriteItsGeometry()
    {
        // Invariant 7, on the symbol axis. A 2× placement covers about four
        // times the area; if the coordinates had been multiplied the mark would
        // also have re-rolled, and the ink count would land somewhere else
        // entirely rather than near 4×.
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);

        using var plain = Render(Placing(new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 50 }));
        using var big = Render(Placing(
            new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 50, ScaleX = 2, ScaleY = 2 }));

        var ratio = (double)InkCount(big) / InkCount(plain);
        Assert.InRange(ratio, 3.2, 4.8);
    }

    [Fact]
    public void RenderingAtTwiceTheOutputScaleIsTheSamePlacementSharper()
    {
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);
        var frame = Placing(new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 50 });

        using var one = Render(frame);
        using var two = Render(frame, outputScale: 2.0);

        Assert.Equal(W * 2, two.Width);
        var ink1 = Assert.IsType<SKRectI>(Ink(one));
        var ink2 = Assert.IsType<SKRectI>(Ink(two));
        // Same placement, twice the pixels: the ink sits in the same place in
        // document terms.
        Assert.InRange(ink2.Left / 2.0, ink1.Left - 2, ink1.Left + 2);
        Assert.InRange(ink2.Top / 2.0, ink1.Top - 2, ink1.Top + 2);
    }

    [Fact]
    public void ScalesThatDifferByNoiseShareOneCachedRender()
    {
        // What the render scale is rounded for. A slider or a drag produces
        // 1.0000001× as readily as 1×, and an unrounded scale would give each
        // of them its own rasterisation — a cache that grows with the number of
        // distinct floats anybody has ever dragged through.
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);
        SymbolRegistry.Clear();
        SymbolRegistry.Register(sword);

        using var a = Render(Placing(
            new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 50, ScaleX = 1.5, ScaleY = 1.5 }));
        var afterFirst = SymbolRasterizer.CachedFrames;
        using var b = Render(Placing(
            new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 50, ScaleX = 1.50004, ScaleY = 1.50004 }));

        Assert.Equal(1, afterFirst);
        Assert.Equal(afterFirst, SymbolRasterizer.CachedFrames);
    }

    [Fact]
    public void TheRenderCacheStaysBounded()
    {
        // Keyed partly by scale, so zooming or dragging a scale handle mints
        // entries. A cache with no ceiling is a leak with a nicer name.
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Clear();
        SymbolRegistry.Register(sword);

        for (var i = 1; i <= 90; i++)
        {
            var scale = 0.1 + i * 0.05;
            using var _ = Render(Placing(new SymbolPlacement
            {
                SymbolId = sword.Id, X = 60, Y = 50, ScaleX = scale, ScaleY = scale,
            }));
        }

        Assert.InRange(SymbolRasterizer.CachedFrames, 1, 64);
    }

    // ---- a cycle runs with the timeline -------------------------------------------

    [Fact]
    public void APlacedCycleAdvancesWithTheCelIndex()
    {
        var walk = Cycle(4);
        SymbolRegistry.Register(walk);
        var frame = Placing(new SymbolPlacement { SymbolId = walk.Id, X = 0, Y = 0 });

        var rows = new List<int>();
        for (var cel = 0; cel < 4; cel++)
        {
            using var rendered = Render(frame, celIndex: cel);
            rows.Add(Assert.IsType<SKRectI>(Ink(rendered)).Top);
        }

        // Each of the symbol's frames draws its bar twenty pixels lower.
        Assert.Equal(rows.OrderBy(r => r).ToList(), rows);
        Assert.Equal(4, rows.Distinct().Count());
    }

    [Fact]
    public void AnOffsetPlacementRunsTheSameCycleOutOfStep()
    {
        // One stored walk, two characters half a stride apart.
        var walk = Cycle(4);
        SymbolRegistry.Register(walk);

        using var onTime = Render(Placing(new SymbolPlacement { SymbolId = walk.Id }), celIndex: 2);
        using var behind = Render(
            Placing(new SymbolPlacement { SymbolId = walk.Id, FrameOffset = 2 }), celIndex: 0);

        Assert.True(Same(onTime, behind));
    }

    [Fact]
    public void APropShowsItsOneDrawingOnEveryCel()
    {
        var sword = Sword(Jittery(10, 10));
        SymbolRegistry.Register(sword);
        var frame = Placing(new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 50 });

        using var first = Render(frame, celIndex: 0);
        using var later = Render(frame, celIndex: 7);

        Assert.True(Same(first, later));
    }

    // ---- pixel comparison ------------------------------------------------------------

    // ---- the stack (Q171 / L3) ---------------------------------------------------

    /// <summary>A flat stroke of one colour, so a composite reads off one pixel.</summary>
    private static Stroke Flat(string colour, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = colour,
        Points = [new StrokePoint(20, y, 1), new StrokePoint(120, y, 1)],
        Brush = new BrushSettings { Size = 20, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1 },
    };

    private static Symbol Stacked(params Layer[] layers) =>
        new() { Name = "Head", PivotX = 0, PivotY = 0, Layers = [.. layers] };

    private static Layer Sheet(string name, Stroke stroke, double opacity = 1, bool visible = true) =>
        new()
        {
            Name = name,
            Opacity = opacity,
            Visible = visible,
            Cels = [new Cel { Frame = new Frame { Strokes = [stroke] } }],
        };

    /// <summary>
    /// A symbol's layers composite, and the one on top is on top.
    /// </summary>
    /// <remarks>
    /// Q171's headline in pixels. Two sheets of solid colour at the same place:
    /// what comes out is the upper one, which is the whole of "the stack is a
    /// stack" — a flattened frame list would have shown them as two frames of an
    /// animation and rendered only one per cel.
    /// </remarks>
    [Fact]
    public void ASymbolsLayersCompositeInStackOrder()
    {
        var symbol = Stacked(
            Sheet("Colour", Flat("#2040c0", 60)),
            Sheet("Lines", Flat("#c02040", 60)));
        SymbolRegistry.Register(symbol);

        using var bitmap = Render(Placing(new SymbolPlacement { SymbolId = symbol.Id, X = 0, Y = 0 }));

        var ink = Ink(bitmap);
        Assert.NotNull(ink);
        var middle = bitmap.GetPixel(ink!.Value.MidX, ink.Value.MidY);
        // The top layer's red, not the bottom layer's blue.
        Assert.True(middle.Red > middle.Blue, $"expected the upper layer on top, got {middle}");
    }

    /// <summary>Every visible layer reaches the pixels, and a hidden one does not.</summary>
    /// <remarks>
    /// <b>Two bands, deliberately.</b> Asserting one band would pass on the
    /// build before this one, which drew a symbol's <em>first</em> layer and
    /// called it done — so a test that only looked at the bottom stroke could
    /// not tell a composite from a single drawing. The upper band is the half
    /// that fails without the stack.
    /// </remarks>
    [Fact]
    public void EveryVisibleLayerReachesThePixelsAndAHiddenOneDoesNot()
    {
        var both = Stacked(Sheet("Colour", Flat("#2040c0", 40)), Sheet("Lines", Flat("#c02040", 110)));
        SymbolRegistry.Register(both);
        using var stacked = Render(Placing(new SymbolPlacement { SymbolId = both.Id }));

        Assert.True(InkInBand(stacked, 40) > 0, "the lower layer is missing");
        Assert.True(InkInBand(stacked, 110) > 0, "the upper layer is missing — this is a composite, not one drawing");

        SymbolRegistry.Clear();
        var hidden = Stacked(
            Sheet("Colour", Flat("#2040c0", 40)),
            Sheet("Lines", Flat("#c02040", 110), visible: false));
        SymbolRegistry.Register(hidden);
        using var one = Render(Placing(new SymbolPlacement { SymbolId = hidden.Id }));

        Assert.True(InkInBand(one, 40) > 0);
        Assert.Equal(0, InkInBand(one, 110));
    }

    /// <summary>
    /// A layer's opacity is honoured inside a symbol.
    /// </summary>
    /// <remarks>
    /// The cheap half of "the compositor came down into this assembly": before
    /// L2 a symbol could not have read this value at all, because the thing that
    /// knows what opacity means lived somewhere Raster could not reach.
    /// </remarks>
    [Fact]
    public void ALayersOpacityIsHonouredInsideASymbol()
    {
        var solid = Stacked(Sheet("Lines", Flat("#c02040", 60)));
        SymbolRegistry.Register(solid);
        using var opaque = Render(Placing(new SymbolPlacement { SymbolId = solid.Id }));
        var opaqueInk = InkCount(opaque);

        SymbolRegistry.Clear();
        var faded = Stacked(Sheet("Lines", Flat("#c02040", 60), opacity: 0.25));
        SymbolRegistry.Register(faded);
        using var thin = Render(Placing(new SymbolPlacement { SymbolId = faded.Id }));

        var ink = Ink(thin);
        Assert.NotNull(ink);
        Assert.True(InkCount(thin) > 0);
        // Same coverage, less alpha — a fade, not a smaller mark.
        Assert.Equal(opaqueInk, InkCount(thin));
        Assert.True(thin.GetPixel(ink!.Value.MidX, ink.Value.MidY).Alpha < 200);
    }

    /// <summary>
    /// Two layers of different lengths: the shorter one holds.
    /// </summary>
    /// <remarks>
    /// A colour layer holding one drawing under a moving line is the ordinary
    /// way to work, and it is the case a flattened frame list could not say at
    /// all — it would have made the symbol four frames long and shown the colour
    /// on its own for two of them.
    /// </remarks>
    [Fact]
    public void AShorterLayerHoldsUnderALongerOne()
    {
        // A colour layer holding one drawing under a moving line is the ordinary
        // way to work, and it is the case a flat frame list could not say at all:
        // it would have made this a two-frame symbol showing the colour alone on
        // one of them. Asserted in pixels on the *second* cel, where the hold is.
        // Three bands well clear of each other: a size-20 stroke reaches ten
        // rows either side, so bands any closer would read each other's ink.
        var colour = Sheet("Colour", Flat("#2040c0", 30));
        var lines = Sheet("Lines", Flat("#c02040", 80));
        lines.Cels.Add(new Cel { Frame = new Frame { Strokes = [Flat("#c02040", 130)] } });
        var symbol = Stacked(colour, lines);
        SymbolRegistry.Register(symbol);

        Assert.Equal(2, symbol.FrameCount);
        using var second = Render(Placing(new SymbolPlacement { SymbolId = symbol.Id }), celIndex: 1);

        // The colour held from cel one, and the line moved to its second pose.
        Assert.True(InkInBand(second, 30) > 0, "the held colour layer is missing");
        Assert.True(InkInBand(second, 130) > 0, "the moving line is missing");
        Assert.Equal(0, InkInBand(second, 80));
    }

    /// <summary>Non-transparent pixels within a few rows of a given y.</summary>
    private static int InkInBand(SKBitmap bitmap, int y, int reach = 14)
    {
        var count = 0;
        for (var yy = Math.Max(0, y - reach); yy < Math.Min(bitmap.Height, y + reach); yy++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, yy).Alpha != 0) count++;
            }
        }
        return count;
    }

    private static bool Same(SKBitmap a, SKBitmap b) => SameShiftedBy(a, b, 0, 0);

    /// <summary>
    /// Whether <paramref name="b"/> is <paramref name="a"/> moved by
    /// (dx, dy) — the comparison a "the same mark, somewhere else" claim needs.
    /// </summary>
    private static bool SameShiftedBy(
        SKBitmap a, SKBitmap b, int dx, int dy, bool onlyWhereFirstHasInk = false)
    {
        for (var y = 0; y < a.Height; y++)
        {
            var ty = y + dy;
            if (ty < 0 || ty >= b.Height) continue;
            for (var x = 0; x < a.Width; x++)
            {
                var tx = x + dx;
                if (tx < 0 || tx >= b.Width) continue;
                var left = a.GetPixel(x, y);
                if (onlyWhereFirstHasInk && left.Alpha == 0) continue;
                if (left != b.GetPixel(tx, ty)) return false;
            }
        }
        return true;
    }
}
