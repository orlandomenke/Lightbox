using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// Q289: nesting is kept, and bounded. A symbol nested deeper than
/// <see cref="SymbolRegistry.MaxNesting"/> draws nothing.
/// </summary>
/// <remarks>
/// <para>
/// With loops refused (B487) every nesting is finite, but not every finite one
/// fits on a stack. A file can hold a chain thousands of symbols long, and each
/// level of it is several frames of the render's recursion. The limit is
/// generous for an artist and small for a stack.
/// </para>
/// <para>
/// <b>Measured on the structure, not on the render in progress.</b> Counting
/// depth as the render descends would cut a symbol differently depending on
/// where it was first drawn from, and the cache would keep that picture. Too
/// deep is a fact about the symbol, so the answer is the same wherever it is
/// asked.
/// </para>
/// </remarks>
[Collection("Registries")]
public class SymbolNestingDepthTests : IDisposable
{
    private const int W = 200, H = 160;

    public SymbolNestingDepthTests() => SymbolRegistry.Clear();

    public void Dispose() => SymbolRegistry.Clear();

    private static Stroke Bar(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(30, y, 1), new StrokePoint(80, y, 1)],
        Brush = new BrushSettings { Size = 6, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    /// <summary>
    /// A chain <paramref name="levels"/> placements deep: the returned symbol
    /// holds one that holds one … down to a leaf.
    /// </summary>
    private static Symbol Chain(int levels)
    {
        var inner = new Symbol { Name = "Leaf" };
        inner.Layers = Symbol.Flat("Leaf", [new Frame { Strokes = [Bar(30)] }]);
        SymbolRegistry.Register(inner);
        for (var i = 0; i < levels; i++)
        {
            var outer = new Symbol { Name = $"Level {i + 1}" };
            outer.Layers = Symbol.Flat(outer.Name, [new Frame
            {
                Placements = [new SymbolPlacement { SymbolId = inner.Id }],
            }]);
            SymbolRegistry.Register(outer);
            inner = outer;
        }
        return inner;
    }

    private static byte[] Render(params string[] placed)
    {
        var frame = new Frame
        {
            Strokes = [Bar(120)],
            Placements = [.. placed.Select(id => new SymbolPlacement { SymbolId = id })],
        };
        using var bmp = FrameRasterizer.Materialize(frame, W, H);
        return bmp.GetPixelSpan().ToArray();
    }

    [Fact]
    public void NestingDepthCountsTheLongestChain()
    {
        Assert.Equal(0, SymbolRegistry.NestingDepth(Chain(0)));
        Assert.Equal(3, SymbolRegistry.NestingDepth(Chain(3)));
    }

    [Fact]
    public void AChainAtTheLimitStillDraws()
    {
        var deepest = Chain(SymbolRegistry.MaxNesting);

        Assert.NotEqual(Render(), Render(deepest.Id));
    }

    [Fact]
    public void AChainPastTheLimitDrawsNothing()
    {
        var tooDeep = Chain(SymbolRegistry.MaxNesting + 1);

        Assert.Equal(Render(), Render(tooDeep.Id));
    }

    [Fact]
    public void AChainThousandsDeepDoesNotTakeTheProcessWithIt()
    {
        // The case the limit exists for: no loop, so B487's guard says yes,
        // and without a limit the render recursed until the stack ran out.
        var hostile = Chain(5000);

        Assert.Equal(Render(), Render(hostile.Id));
    }
}
