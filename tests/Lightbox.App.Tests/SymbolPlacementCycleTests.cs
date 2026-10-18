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
}
