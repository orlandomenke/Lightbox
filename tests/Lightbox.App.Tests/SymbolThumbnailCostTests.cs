using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// B375: what the symbol grid costs to redraw.
/// </summary>
/// <remarks>
/// <para>
/// Two faults in one path. The tile is 64x48 on every document and was
/// materialized at the <em>document's</em> canvas size before being thrown away
/// down to 64x48 — so it cost 3.7 ms a symbol at 640x360 and 25 ms a symbol at
/// 4K. And <c>Refresh</c> cleared the rows before rebuilding them, while the
/// version check that would have skipped the work lives <em>on the row</em> — a
/// 0% hit rate by construction.
/// </para>
/// <para>
/// Neither mattered much on its own; together they are paid once per keystroke
/// in the search box, because that is bound with no delay and calls
/// <c>Refresh</c> directly.
/// </para>
/// <para>
/// <b>Sizes stay small deliberately.</b> These assert shape — that the cost has
/// stopped tracking the canvas — not milliseconds, which vary by an order of
/// magnitude across machines.
/// </para>
/// </remarks>
public sealed class SymbolThumbnailCostTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-thumbcost-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Stroke Bar(double x, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 120, y + 80, 1)],
        Brush = new BrushSettings { Size = 14, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static Symbol Prop(string name) => new()
    {
        Name = name,
        PivotX = 0,
        PivotY = 0,
        Layers = Symbol.Flat(name, [new Frame { Strokes = [Bar(10, 10)] }]),
    };

    private MainViewModel Loaded(int w, int h, int symbols)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings(
            "probe", w, h, 12, 72, Scene.DefaultBackgroundColor, false));
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

    /// <summary>
    /// A refresh that changes nothing must not redraw anything.
    /// </summary>
    /// <remarks>
    /// The regression. <c>Refresh</c> cleared <c>Rows</c> and built new
    /// <see cref="SymbolRow"/> objects, which start at <c>ThumbVersion == -1</c>
    /// with a null thumbnail — so the skip in <c>RefreshThumbs</c> could never
    /// fire and every symbol was re-rendered every time.
    /// </remarks>
    [AvaloniaFact]
    public void ASecondRefreshReusesTheThumbnailsItAlreadyHas()
    {
        var vm = Loaded(640, 360, symbols: 12);
        vm.SymbolBrowser.Refresh();

        var thumbs = vm.SymbolBrowser.Rows.Select(r => r.Thumb).ToList();
        Assert.All(thumbs, t => Assert.NotNull(t));

        vm.SymbolBrowser.Refresh();

        Assert.Equal(
            thumbs,
            vm.SymbolBrowser.Rows.Select(r => r.Thumb).ToList());
    }

    /// <summary>
    /// Editing a symbol still refreshes exactly its own tile.
    /// </summary>
    /// <remarks>
    /// The other half of reuse, and the thing it could break: a cache that never
    /// invalidates is as wrong as one that never hits. The version is what
    /// decides, exactly as before.
    /// </remarks>
    [AvaloniaFact]
    public void EditingASymbolRedrawsItsOwnTileAndNoOther()
    {
        var vm = Loaded(640, 360, symbols: 4);
        vm.SymbolBrowser.Refresh();

        var before = vm.SymbolBrowser.Rows.Select(r => r.Thumb).ToList();
        var edited = vm.SymbolBrowser.Rows[1];
        edited.Model.Layers[0].Cels[0].Frame!.Strokes.Add(Bar(60, 60));
        edited.Model.Version++;

        vm.SymbolBrowser.Refresh();

        var after = vm.SymbolBrowser.Rows.Select(r => r.Thumb).ToList();
        Assert.NotSame(before[1], after[1]);
        Assert.Same(before[0], after[0]);
        Assert.Same(before[2], after[2]);
        Assert.Same(before[3], after[3]);
    }

    /// <summary>
    /// The tile is the same size on every document, so it must cost the same on
    /// every document.
    /// </summary>
    /// <remarks>
    /// It used to be materialized at the canvas size and downsampled, which made
    /// a 64x48 picture cost 4x more on a canvas with 4x the area. A band rather
    /// than a number: this is a shape assertion on a shared runner.
    /// </remarks>
    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void ATileCostsTheSameWhateverTheCanvasSize()
    {
        double Build(int w, int h)
        {
            var vm = Loaded(w, h, symbols: 12);
            vm.SymbolBrowser.Refresh();
            // A fresh grid each time: reuse is the other test's subject, and
            // this one is about what a first render costs.
            return BestOf(3, () =>
            {
                foreach (var row in vm.SymbolBrowser.Rows)
                {
                    row.Thumb = null;
                    row.ThumbVersion = -1;
                }
                vm.SymbolBrowser.RefreshThumbs();
            });
        }

        var small = Build(640, 360);
        var large = Build(1920, 1080);

        output.WriteLine($"12 tiles, canvas  640x360 (0.23 Mpx): {small:0.0} ms");
        output.WriteLine($"12 tiles, canvas 1920x1080 (2.07 Mpx): {large:0.0} ms");
        output.WriteLine($"ratio {large / Math.Max(0.001, small):0.00}x for 9x the canvas area");

        Assert.True(large < small * 3,
            $"rendering 12 tiles cost {small:0.0} ms at 0.23 Mpx and {large:0.0} ms at 2.07 Mpx. "
            + "The tile is 64x48 either way, so nine times the canvas area must not cost three "
            + "times as much — if it does, the thumbnail is being materialized at document size "
            + "again (B375).");
    }

    /// <summary>
    /// A symbol replaced wholesale under the same id gets a new tile.
    /// </summary>
    /// <remarks>
    /// "Update from library" swaps the object rather than bumping the version, so
    /// reuse keyed on the id alone would keep a picture of the old drawing.
    /// </remarks>
    [AvaloniaFact]
    public void AReplacedSymbolDoesNotKeepTheOldPicture()
    {
        var vm = Loaded(640, 360, symbols: 3);
        vm.SymbolBrowser.Refresh();
        var before = vm.SymbolBrowser.Rows[0].Thumb;
        var id = vm.SymbolBrowser.Rows[0].Model.Id;

        var replacement = Prop(vm.SymbolBrowser.Rows[0].Model.Name);
        replacement.Id = id;
        replacement.Layers[0].Cels[0].Frame!.Strokes.Add(Bar(90, 20));
        vm.ProjectDocker.Project!.Symbols[id] = replacement;
        SymbolRegistry.Register(replacement);

        vm.SymbolBrowser.Refresh();

        Assert.Same(replacement, vm.SymbolBrowser.Rows.Single(r => r.Model.Id == id).Model);
        Assert.NotSame(before, vm.SymbolBrowser.Rows.Single(r => r.Model.Id == id).Thumb);
    }

    /// <summary>
    /// Searching does not deselect what you were searching for.
    /// </summary>
    /// <remarks>
    /// Clearing the rows dropped the selection, and the search box calls Refresh
    /// on every keystroke — so typing the name of the symbol you had selected
    /// unselected it, and the Place, Edit and Delete buttons greyed out under
    /// your hands.
    /// </remarks>
    [AvaloniaFact]
    public void TypingInTheSearchBoxKeepsTheSelection()
    {
        var vm = Loaded(640, 360, symbols: 6);
        vm.SymbolBrowser.Refresh();
        var chosen = vm.SymbolBrowser.Rows.Single(r => r.Model.Name == "prop03");
        vm.SymbolBrowser.Selected = chosen;

        vm.SymbolBrowser.Search = "prop0";

        Assert.Same(chosen, vm.SymbolBrowser.Selected);
        // Not merely still pointing at it: still pointing at a row that is
        // actually on show. Rebuilding the rows used to leave Selected holding a
        // row that had been removed from the collection, which reads as "kept"
        // to a view model test and as "deselected" to the ListBox bound to it.
        Assert.Contains(vm.SymbolBrowser.Selected!, vm.SymbolBrowser.Rows);
    }

    /// <summary>
    /// Filtered out of view, the selection goes — there is nothing to keep.
    /// </summary>
    [AvaloniaFact]
    public void ASelectionFilteredAwayIsCleared()
    {
        var vm = Loaded(640, 360, symbols: 6);
        vm.SymbolBrowser.Refresh();
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows.Single(r => r.Model.Name == "prop03");

        vm.SymbolBrowser.Search = "prop05";

        Assert.Null(vm.SymbolBrowser.Selected);
    }
}
