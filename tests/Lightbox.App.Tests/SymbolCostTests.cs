using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// What making and placing a symbol actually costs, measured before anything
/// is changed about it.
/// </summary>
/// <remarks>
/// <para>
/// The report this answers was "creating, placing a symbol is really slow" and
/// "drag and drop of a symbol does not work". This file is the measurement half
/// of that: it does not fix anything, it establishes what the numbers are so a
/// fix can be shown to have moved them.
/// </para>
/// <para>
/// <b>Sizes are deliberately small.</b> 1080p renders inside the App suite have
/// killed the runner before (MSB4166), so the canvases here are 640x360 and
/// 1280x720 — enough to establish the shape of the cost, small enough not to be
/// the thing that breaks CI. What an artist feels at 4K is these numbers times
/// the area ratio, which is the point the scaling test makes.
/// </para>
/// </remarks>
public sealed class SymbolCostTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-symbolcost-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static Stroke Bar(double x, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 40, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    /// <summary>A symbol with <paramref name="frames"/> drawings on one layer.</summary>
    private static Symbol Prop(string name, int frames = 1) => new()
    {
        Name = name,
        PivotX = 0,
        PivotY = 0,
        Layers = Symbol.Flat(
            name,
            Enumerable.Range(0, frames).Select(i => new Frame { Strokes = [Bar(i, 0)] })),
    };

    /// <summary>A view model on a project holding <paramref name="symbols"/> symbols.</summary>
    private MainViewModel Loaded(int w, int h, int symbols)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings(
            "probe", w, h, 12, 72, Scene.DefaultBackgroundColor, false));
        vm.SmoothStrokes = false;
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;

        var project = ProjectIo.Create("Knight", _root);
        for (var i = 0; i < symbols; i++)
        {
            var s = Prop($"prop{i:00}");
            project.Symbols[s.Id] = s;
        }
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        return vm;
    }

    /// <summary>Draw something, so there is a drawing to capture.</summary>
    private static void Draw(MainViewModel vm)
    {
        vm.BeginStroke(30, 30, 1);
        vm.MoveStroke(90, 90, 1);
        vm.EndStroke();
    }

    /// <summary>Best of several, because a median carries the runner's contention.</summary>
    private static double BestOf(int runs, Action work)
    {
        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            work();
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }
        return best;
    }

    // ---- 1. the browser refresh ----------------------------------------------------

    /// <summary>
    /// A browser refresh costs one full-canvas rasterization per symbol, every
    /// time, because Refresh clears the rows and the version cache it checks
    /// lives on the rows it has just thrown away.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void ARefreshRerendersEveryThumbnailEveryTime()
    {
        var vm = Loaded(640, 360, symbols: 20);

        // Warm: the first pass pays for JIT and the Skia surface.
        vm.SymbolBrowser.Refresh();

        var first = BestOf(3, () => vm.SymbolBrowser.Refresh());
        // Nothing changed in between. If the thumbnails were cached this would
        // be free; the rows are new objects, so it is not.
        var second = BestOf(3, () => vm.SymbolBrowser.Refresh());

        // What it would cost if the cache held: refresh the thumbs without
        // rebuilding the rows.
        vm.SymbolBrowser.Refresh();
        var cached = BestOf(3, () => vm.SymbolBrowser.RefreshThumbs());

        output.WriteLine("20 symbols at 640x360");
        output.WriteLine($"  Refresh()       {first:0.0} ms, again {second:0.0} ms");
        output.WriteLine($"  RefreshThumbs() {cached:0.0} ms  (rows kept, so the version cache hits)");
        output.WriteLine($"  per symbol:     {first / 20:0.0} ms");

        Assert.True(cached < first / 4,
            $"a refresh that keeps its rows costs {cached:0.0} ms against {first:0.0} ms for one that "
            + "rebuilds them — if these are close, Refresh no longer re-renders every thumbnail and "
            + "this measurement is out of date");
    }

    /// <summary>
    /// The thumbnail is 64x48 on screen and is rendered at the size of the
    /// document canvas, so its cost is the canvas area rather than the tile.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void AThumbnailIsRenderedAtCanvasSizeNotTileSize()
    {
        double Refresh(int w, int h)
        {
            var vm = Loaded(w, h, symbols: 12);
            vm.SymbolBrowser.Refresh();
            return BestOf(3, () => vm.SymbolBrowser.Refresh());
        }

        var small = Refresh(640, 360);
        var large = Refresh(1280, 720);

        output.WriteLine("12 symbols, 64x48 tiles");
        output.WriteLine($"  canvas  640x360 (0.23 Mpx): {small:0.0} ms");
        output.WriteLine($"  canvas 1280x720 (0.92 Mpx): {large:0.0} ms");
        output.WriteLine($"  ratio {large / Math.Max(0.001, small):0.0}x for 4x the canvas area");

        Assert.True(large > small * 1.8,
            $"the refresh cost {small:0.0} ms at 0.23 Mpx and {large:0.0} ms at 0.92 Mpx — a tile that "
            + "is 64x48 either way should not care about the canvas, and if this fails the thumbnail "
            + "no longer materializes at document size");
    }

    // ---- 2. making one -------------------------------------------------------------

    /// <summary>
    /// Making a symbol costs more the more symbols the project already holds,
    /// because the capture ends in a browser refresh that re-renders all of them.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void MakingASymbolCostsMoreForEverySymbolAlreadyThere()
    {
        // Best of several fresh view models, because a single capture on a
        // shared runner carries whatever else the machine was doing.
        double Make(int existing)
        {
            var best = double.MaxValue;
            for (var i = 0; i < 5; i++)
            {
                var vm = Loaded(640, 360, existing);
                vm.SymbolBrowser.Refresh();
                Draw(vm);
                var sw = Stopwatch.StartNew();
                vm.MakeSymbolFromDrawing($"fresh{existing}");
                sw.Stop();
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        // What the grid refresh alone costs at each size, so the capture can be
        // told apart from the redraw that follows it.
        double RefreshOnly(int existing)
        {
            var vm = Loaded(640, 360, existing);
            vm.SymbolBrowser.Refresh();
            return BestOf(5, () => vm.SymbolBrowser.Refresh());
        }

        // Warm the pixel path before the first measured capture.
        Make(1);

        var few = Make(1);
        var some = Make(20);
        var many = Make(60);

        output.WriteLine("MakeSymbolFromDrawing, canvas 640x360, best of 5");
        output.WriteLine($"   1 symbol  in the project: {few:0.0} ms   (grid refresh alone {RefreshOnly(1):0.0} ms)");
        output.WriteLine($"  20 symbols in the project: {some:0.0} ms   (grid refresh alone {RefreshOnly(20):0.0} ms)");
        output.WriteLine($"  60 symbols in the project: {many:0.0} ms   (grid refresh alone {RefreshOnly(60):0.0} ms)");
        output.WriteLine($"  growth from 1 to 60: {many - few:0.0} ms");

        // Deliberately a shape assertion and a loose one. The first measurement
        // of this (34.6 / 54.5 / 47.3 ms, single runs) showed growth that a
        // stronger claim would have overstated: the capture is not dominated by
        // the grid, it merely carries it.
        Assert.True(many > few,
            $"capturing a symbol took {few:0.0} ms with one in the project and {many:0.0} ms with 60. "
            + "The capture itself does not depend on what else exists, so any growth here is the "
            + "browser refresh at the end of it re-rendering the whole grid");
    }

    // ---- 3. placing one ------------------------------------------------------------

    /// <summary>
    /// Placing a single-frame symbol, for the comparison the next test needs.
    /// </summary>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void PlacingASingleFrameSymbolIsCheap()
    {
        var vm = Loaded(640, 360, symbols: 10);
        var symbol = Prop("sword");
        vm.ProjectDocker.Project!.Symbols[symbol.Id] = symbol;
        SymbolRegistry.Register(symbol);
        vm.SymbolBrowser.Refresh();

        vm.PlaceSymbol(symbol.Id, 100, 100);
        var best = BestOf(5, () => vm.PlaceSymbol(symbol.Id, 100, 100));

        output.WriteLine($"PlaceSymbol, single frame, canvas 640x360: {best:0.0} ms");
        Assert.True(best < 2000, $"placing one frame took {best:0.0} ms");
    }

    /// <summary>
    /// Placing a multi-frame symbol from a real window, which is the path that
    /// goes through DetectAnimationAndAsk.
    /// </summary>
    /// <remarks>
    /// This is the one that needs a window. Without one,
    /// Application.Current.ApplicationLifetime is not a desktop lifetime, the
    /// dialog branch is skipped entirely, and the cost measured is a lie.
    /// </remarks>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void PlacingAMultiFrameSymbolFromAWindow()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;

        var project = ProjectIo.Create("Knight", _root);
        var walk = Prop("walk", frames: 8);
        project.Symbols[walk.Id] = walk;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        Pump();

        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;

        // Whether the branch that shows a dialog is even reachable from here.
        // It is gated on a desktop lifetime, and the headless host is not one —
        // so a fast number below means the dialog was skipped, NOT that the
        // wait inside it is cheap. Printed so the measurement cannot be read
        // as covering something it never ran.
        var desktop = Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        output.WriteLine($"desktop lifetime present: {desktop is not null}");
        output.WriteLine($"lifetime type: {Avalonia.Application.Current?.ApplicationLifetime?.GetType().Name ?? "none"}");
        output.WriteLine($"app.MainWindow is a MainWindow: {desktop?.MainWindow is MainWindow}");

        var sw = Stopwatch.StartNew();
        var placed = vm.PlaceSymbol(walk.Id, 100, 100);
        sw.Stop();

        output.WriteLine($"PlaceSymbol, 8-frame symbol: {sw.Elapsed.TotalMilliseconds:0} ms");
        output.WriteLine($"  placed: {(placed is null ? "nothing" : "a placement")}");
        output.WriteLine($"  status: {vm.AiStatus}");
        output.WriteLine($"  scene frame count after: {vm.Doc.Scene.FrameCount}");
    }
}
