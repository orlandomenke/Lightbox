using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.Raster.Tests;

/// <summary>
/// B487: a symbol that holds itself, at any depth, draws nothing — instead of
/// recursing until the process dies.
/// </summary>
/// <remarks>
/// <para>
/// Placing refuses a cycle, but a placement can arrive by more routes than
/// placing: a pasted cel, a copied frame, a file written by something else. So
/// the render has to survive one too, and a stack overflow is not an exception
/// anything can catch — it ends the process and the artist's unsaved work.
/// </para>
/// <para>
/// <b>Nothing rather than "as far as the loop goes", on purpose.</b> Cutting the
/// recursion where it repeats draws a different picture depending on which
/// symbol was asked for first, and the render cache would then keep whichever
/// came first. That is invariant 2 broken by the back door. A symbol in a cycle
/// has no picture, so it gets none, and the same answer every time.
/// </para>
/// </remarks>
[Collection("Registries")]
public class SymbolCycleTests : IDisposable
{
    private const int W = 200, H = 160;

    public SymbolCycleTests() => SymbolRegistry.Clear();

    public void Dispose() => SymbolRegistry.Clear();

    private static Stroke Bar(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(30, y, 1), new StrokePoint(80, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static Symbol Holding(string name, double y, params string[] placed)
    {
        var symbol = new Symbol { Name = name };
        symbol.Layers = Symbol.Flat(name, [new Frame
        {
            Strokes = [Bar(y)],
            Placements = placed.Length == 0
                ? null
                : [.. placed.Select(id => new SymbolPlacement { SymbolId = id })],
        }]);
        SymbolRegistry.Register(symbol);
        return symbol;
    }

    /// <summary>Add a placement after the fact: how a cycle is closed.</summary>
    private static void Place(Symbol host, string id) =>
        host.AllFrames.First().Placements = [.. host.AllFrames.First().Placements ?? [], new SymbolPlacement { SymbolId = id }];

    private static byte[] Render(Frame frame)
    {
        using var bmp = FrameRasterizer.Materialize(frame, W, H);
        return bmp.GetPixelSpan().ToArray();
    }

    private static Frame Drawing(params string[] placed) => new()
    {
        Strokes = [Bar(120)],
        Placements = [.. placed.Select(id => new SymbolPlacement { SymbolId = id, X = 60 })],
    };

    [Fact]
    public void ASymbolThatPlacesItselfDrawsNothing()
    {
        var mirror = Holding("Mirror", 30);
        Place(mirror, mirror.Id);

        Assert.Equal(Render(Drawing()), Render(Drawing(mirror.Id)));
    }

    [Fact]
    public void TwoSymbolsThatHoldEachOtherDrawNothing()
    {
        var a = Holding("A", 30);
        var b = Holding("B", 50, a.Id);
        Place(a, b.Id);

        Assert.Equal(Render(Drawing()), Render(Drawing(a.Id)));
        Assert.Equal(Render(Drawing()), Render(Drawing(b.Id)));
    }

    [Fact]
    public void ASymbolHoldingACycleStillDrawsItsOwnInk()
    {
        // The knight is not in the loop; the two things he holds are. He keeps
        // his own stroke, and the loop inside him draws nothing.
        var a = Holding("A", 30);
        var b = Holding("B", 50, a.Id);
        Place(a, b.Id);
        var knight = Holding("Knight", 70, a.Id);
        var plain = Holding("Plain knight", 70);

        Assert.Equal(Render(Drawing(plain.Id)), Render(Drawing(knight.Id)));
        Assert.NotEqual(Render(Drawing()), Render(Drawing(knight.Id)));
    }

    [Fact]
    public void ReachesFollowsPlacementsThroughEveryLevel()
    {
        var sword = Holding("Sword", 30);
        var hand = Holding("Hand", 50, sword.Id);
        var knight = Holding("Knight", 70, hand.Id);

        Assert.True(SymbolRegistry.Reaches(knight.Id, sword.Id));
        Assert.True(SymbolRegistry.Reaches(sword.Id, sword.Id));
        Assert.False(SymbolRegistry.Reaches(sword.Id, knight.Id));
        Assert.False(SymbolRegistry.HoldsItself(knight));
    }
}
