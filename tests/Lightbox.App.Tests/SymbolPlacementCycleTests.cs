using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// B487: placing a symbol inside itself — directly, or inside a symbol that
/// already holds it — is refused, with a reason, before it can reach a render.
/// </summary>
/// <remarks>
/// The renderer survives a cycle by drawing nothing (<c>SymbolCycleTests</c> in
/// the raster suite). That is the safety net for a cycle that arrives some other
/// way. An artist placing one by hand should be told no, not handed a symbol
/// that silently vanishes.
/// </remarks>
public class SymbolPlacementCycleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-cycle-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Stroke Bar(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(30, y, 1), new StrokePoint(80, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    /// <summary>A sword, and a knight holding it.</summary>
    private MainViewModel WithKnight(out Symbol sword, out Symbol knight)
    {
        var vm = VmLayers.PaperVm();
        var project = ProjectIo.Create("Knight", _root);
        sword = new Symbol { Name = "Sword", Fps = 12 };
        sword.Layers = Symbol.Flat("Sword", [new Frame { Strokes = [Bar(30)] }]);
        knight = new Symbol { Name = "Knight", Fps = 12 };
        knight.Layers = Symbol.Flat("Knight", [new Frame
        {
            Strokes = [Bar(80)],
            Placements = [new SymbolPlacement { SymbolId = sword.Id }],
        }]);
        project.Symbols[sword.Id] = sword;
        project.Symbols[knight.Id] = knight;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        return vm;
    }

    private static int PlacementsIn(Symbol symbol) =>
        symbol.AllFrames.Sum(f => f.Placements?.Count ?? 0);

    [AvaloniaFact]
    public void PlacingASymbolInsideItselfIsRefused()
    {
        var vm = WithKnight(out var sword, out _);
        vm.OpenSymbol(sword);

        Assert.Null(vm.PlaceSymbol(sword.Id, 40, 40));
        Assert.Equal(0, PlacementsIn(sword));
        Assert.Contains("inside itself", vm.AiStatus);
    }

    [AvaloniaFact]
    public void PlacingTheKnightInsideHisOwnSwordIsRefused()
    {
        var vm = WithKnight(out var sword, out var knight);
        vm.OpenSymbol(sword);

        Assert.Null(vm.PlaceSymbol(knight.Id, 40, 40));
        Assert.Equal(0, PlacementsIn(sword));
        Assert.Contains("Knight", vm.AiStatus);
    }

    [AvaloniaFact]
    public void PlacingAnUnrelatedSymbolInsideASymbolStillWorks()
    {
        // The refusal is about loops, not about nesting: a sword inside the
        // knight is the ordinary case and must keep working.
        var vm = WithKnight(out var sword, out var knight);
        vm.OpenSymbol(knight);

        Assert.NotNull(vm.PlaceSymbol(sword.Id, 40, 40));
        Assert.Equal(2, PlacementsIn(knight));
    }

    [AvaloniaFact]
    public void PlacingASymbolThatWouldNestPastTheLimitIsRefused()
    {
        // Q289: nesting is bounded. A host that would end up deeper than the
        // renderer draws is refused at the drop, with the reason, rather than
        // left to vanish.
        var vm = WithKnight(out _, out _);
        var project = vm.ProjectDocker.Project!;
        var inner = new Symbol { Name = "Leaf", Fps = 12 };
        inner.Layers = Symbol.Flat("Leaf", [new Frame { Strokes = [Bar(30)] }]);
        project.Symbols[inner.Id] = inner;
        for (var i = 0; i < SymbolRegistry.MaxNesting; i++)
        {
            var outer = new Symbol { Name = $"Level {i + 1}", Fps = 12 };
            outer.Layers = Symbol.Flat(outer.Name, [new Frame
            {
                Placements = [new SymbolPlacement { SymbolId = inner.Id }],
            }]);
            project.Symbols[outer.Id] = outer;
            inner = outer;
        }
        var host = new Symbol { Name = "Host", Fps = 12 };
        host.Layers = Symbol.Flat("Host", [new Frame { Strokes = [Bar(80)] }]);
        project.Symbols[host.Id] = host;
        vm.RefreshProjectResources();
        vm.OpenSymbol(host);

        Assert.Null(vm.PlaceSymbol(inner.Id, 40, 40));
        Assert.Equal(0, PlacementsIn(host));
        Assert.Contains("deep", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ASwordOnlyTheKnightHoldsIsDescribedAsHeldByHim()
    {
        // Q289: what the delete prompt says. "Not placed anywhere" for a sword
        // the knight holds would be a delete that blanks every knight.
        var vm = WithKnight(out var sword, out _);

        var said = vm.DescribeUsage(sword);

        Assert.DoesNotContain("not placed anywhere", said);
        Assert.Contains("“Knight”", said);
    }

    /// <summary>A chain <paramref name="levels"/> deep under a fresh leaf; returns the top.</summary>
    /// <remarks>
    /// Added to the project top first, the order a file's author can choose,
    /// so a walk that visits the project's symbols in order meets the head of
    /// the chain before anything beneath it is known.
    /// </remarks>
    private static Symbol Chain(Project project, int levels)
    {
        var chain = new List<Symbol>();
        var inner = new Symbol { Name = "Leaf", Fps = 12 };
        inner.Layers = Symbol.Flat("Leaf", [new Frame { Strokes = [Bar(30)] }]);
        chain.Add(inner);
        for (var i = 0; i < levels; i++)
        {
            var outer = new Symbol { Name = $"Level {i + 1}", Fps = 12 };
            outer.Layers = Symbol.Flat(outer.Name, [new Frame
            {
                Placements = [new SymbolPlacement { SymbolId = inner.Id }],
            }]);
            chain.Add(outer);
            inner = outer;
        }
        for (var i = chain.Count - 1; i >= 0; i--) project.Symbols[chain[i].Id] = chain[i];
        return inner;
    }

    [AvaloniaFact]
    public void AnEditBesideAChainThousandsDeepDoesNotTakeTheProcessWithIt()
    {
        // Q289 made a long chain loadable, and B486's repaint walk recursed
        // through it on every edit in a symbol tab, even an edit to a symbol
        // the chain never touches.
        var vm = WithKnight(out var sword, out _);
        Chain(vm.ProjectDocker.Project!, 5000);
        vm.RefreshProjectResources();
        vm.OpenSymbol(sword);
        var version = sword.Version;

        vm.AppendExternalStrokes(vm.ActiveLayerForIpc.Id, 0, [Bar(60)]);

        Assert.True(sword.Version > version);
    }

    [AvaloniaFact]
    public void PlacingIntoAHostThatIsItselfHeldCountsWhatHoldsIt()
    {
        // The host is one level down inside the gate. A chain that fits under
        // the host alone carries the gate past the limit, and the gate would
        // then draw nothing.
        var vm = WithKnight(out _, out _);
        var project = vm.ProjectDocker.Project!;
        var top = Chain(project, SymbolRegistry.MaxNesting - 1);
        var host = new Symbol { Name = "Host", Fps = 12 };
        host.Layers = Symbol.Flat("Host", [new Frame { Strokes = [Bar(80)] }]);
        var gate = new Symbol { Name = "Gate", Fps = 12 };
        gate.Layers = Symbol.Flat("Gate", [new Frame
        {
            Placements = [new SymbolPlacement { SymbolId = host.Id }],
        }]);
        project.Symbols[host.Id] = host;
        project.Symbols[gate.Id] = gate;
        vm.RefreshProjectResources();
        vm.OpenSymbol(host);

        Assert.Null(vm.PlaceSymbol(top.Id, 40, 40));
        Assert.Equal(0, PlacementsIn(host));
        Assert.Contains("Gate", vm.AiStatus);
    }
}
