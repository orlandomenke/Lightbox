using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B486: an edit to a symbol, made in its own tab, shows on every open document
/// that places it — read off the published picture, not off a fresh render.
/// </summary>
/// <remarks>
/// <para>
/// <c>SymbolEditingTests.EditingASymbolChangesEveryPlacementOfIt</c> makes the
/// same promise and kept passing while it was broken, because its ink count
/// renders the frame with <c>FrameRasterizer.Materialize</c> — straight from
/// the record, past the frame cache and the parked picture. Since B362 those
/// keep an open document's pictures across a tab switch, and nothing told them
/// a symbol had changed: the edit bumps the symbol's version, and the host
/// frame's cache key carries no symbol version. Measured on main: 1,975 inked
/// pixels before the edit and 1,975 after.
/// </para>
/// <para>
/// So every test here reads what the switch <em>publishes</em>.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class SymbolEditReachesOpenTabsTests : BrushStateIsolated
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-symboltabs-{Guid.NewGuid():N}.lbproj");

    public override void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private static Stroke Bar(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(30, y, 1), new StrokePoint(80, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static void Stroke(MainViewModel vm, float y)
    {
        vm.BeginStroke(100, y, 1);
        vm.MoveStroke(400, y, 1);
        vm.EndStroke();
    }

    private static SKBitmap Published(MainViewModel vm)
    {
        SKBitmap? grabbed = null;
        void Capture(RenderSnapshot s)
        {
            using var img = s.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            grabbed = bmp;
        }
        vm.SnapshotChanged += Capture;
        try
        {
            vm.PublishSnapshot();
        }
        finally
        {
            vm.SnapshotChanged -= Capture;
        }
        return grabbed ?? throw new InvalidOperationException("nothing was published");
    }

    /// <summary>The picture the next tab switch publishes, caught as it goes out.</summary>
    private static SKBitmap Arrive(MainViewModel vm, DocumentTab tab)
    {
        SKBitmap? grabbed = null;
        void Capture(RenderSnapshot s)
        {
            grabbed?.Dispose();
            using var img = s.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            grabbed = bmp;
        }
        vm.SnapshotChanged += Capture;
        try
        {
            vm.ActiveTab = tab;
        }
        finally
        {
            vm.SnapshotChanged -= Capture;
        }
        return grabbed ?? Published(vm);
    }

    /// <summary>Pixels of the published picture that are not the paper.</summary>
    private static int Ink(SKBitmap shot)
    {
        var paper = shot.GetPixel(shot.Width - 2, shot.Height - 2);
        var count = 0;
        for (var y = 0; y < shot.Height; y += 2)
        {
            for (var x = 0; x < shot.Width; x += 2)
            {
                if (shot.GetPixel(x, y) != paper) count++;
            }
        }
        return count;
    }

    private MainViewModel WithSword(out Symbol sword, out Project project)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        project = ProjectIo.Create("Knight", _root);
        sword = new Symbol { Name = "Sword", Fps = 12 };
        sword.Layers = Symbol.Flat("Sword", [new Frame { Strokes = [Bar(30)] }]);
        project.Symbols[sword.Id] = sword;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        return vm;
    }

    /// <summary>
    /// Place it, and draw on a second layer so the stack beneath bakes and the
    /// bake is parked with the tab.
    /// </summary>
    private static int PlaceAndDraw(MainViewModel vm, string symbolId)
    {
        vm.PlaceSymbol(symbolId, 40, 40);
        vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 0; i < 4; i++) Stroke(vm, 300 + i * 20);
        using var shot = Published(vm);
        return Ink(shot);
    }

    private static void EditInItsTab(MainViewModel vm, Symbol symbol)
    {
        vm.OpenSymbol(symbol);
        vm.AppendExternalStrokes(vm.ActiveLayerForIpc.Id, 0, [Bar(70)]);
    }

    [AvaloniaFact]
    public void AnEditToASymbolShowsOnTheReturn()
    {
        var vm = WithSword(out var sword, out _);
        var animation = vm.ActiveTab!;
        var before = PlaceAndDraw(vm, sword.Id);

        EditInItsTab(vm, sword);

        using var back = Arrive(vm, animation);
        var after = Ink(back);
        Assert.True(after > before, $"the symbol's new stroke is missing on the return: {after} inked against {before} before");
    }

    [AvaloniaFact]
    public void AnEditToANestedSymbolShowsOnTheReturn()
    {
        // The sword is held by a knight, and the knight is what the animation
        // places: nothing on the animation names the sword at all.
        var vm = WithSword(out var sword, out var project);
        var knight = new Symbol { Name = "Knight", Fps = 12 };
        knight.Layers = Symbol.Flat("Knight", [new Frame
        {
            Strokes = [Bar(120)],
            Placements = [new SymbolPlacement { SymbolId = sword.Id, SeenVersion = sword.Version }],
        }]);
        project.Symbols[knight.Id] = knight;
        vm.RefreshProjectResources();
        var animation = vm.ActiveTab!;
        var before = PlaceAndDraw(vm, knight.Id);

        EditInItsTab(vm, sword);

        using var back = Arrive(vm, animation);
        var after = Ink(back);
        Assert.True(after > before, $"the inner symbol's new stroke is missing on the return: {after} inked against {before} before");
    }

    [AvaloniaFact]
    public void AnEditToANestedSymbolRedrawsTheTileOfTheOneHoldingIt()
    {
        // A tab switch resets the registry, which drops every symbol render, so
        // the return above is answered either way. The browser is not: its
        // tiles are keyed by version and redrawn in place, and the knight's
        // tile held the old sword until the knight's own version moved.
        var vm = WithSword(out var sword, out var project);
        var knight = new Symbol { Name = "Knight", Fps = 12 };
        knight.Layers = Symbol.Flat("Knight", [new Frame
        {
            Strokes = [Bar(120)],
            Placements = [new SymbolPlacement { SymbolId = sword.Id, SeenVersion = sword.Version }],
        }]);
        project.Symbols[knight.Id] = knight;
        vm.RefreshProjectResources();
        vm.SymbolBrowser.Refresh();
        vm.SymbolBrowser.RefreshThumbs();
        var tile = vm.SymbolBrowser.Rows.Single(r => r.Model.Id == knight.Id);
        var before = tile.Thumb;
        Assert.NotNull(before);

        EditInItsTab(vm, sword);

        Assert.NotSame(before, tile.Thumb);
    }

    [AvaloniaFact]
    public void AnEditToASymbolShowsInEveryDocumentThatPlacesIt()
    {
        var vm = WithSword(out var sword, out _);
        var first = vm.ActiveTab!;
        var firstBefore = PlaceAndDraw(vm, sword.Id);
        vm.NewDocument(new NewDocumentSettings("Second", 960, 540, 12, 72, Scene.DefaultBackgroundColor, false));
        var second = vm.ActiveTab!;
        Assert.NotSame(first, second);
        var secondBefore = PlaceAndDraw(vm, sword.Id);

        EditInItsTab(vm, sword);

        using (var back = Arrive(vm, first))
        {
            var after = Ink(back);
            Assert.True(after > firstBefore, $"first document: {after} inked against {firstBefore} before");
        }
        using (var back = Arrive(vm, second))
        {
            var after = Ink(back);
            Assert.True(after > secondBefore, $"second document: {after} inked against {secondBefore} before");
        }
    }
}
