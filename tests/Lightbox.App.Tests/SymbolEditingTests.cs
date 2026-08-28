using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Pillar 3, step six: edit the sword once, every animation holding it
/// updates.
/// </summary>
/// <remarks>
/// The headline, and deliberately last — it is the cheapest step once the
/// record, the render pass and the flattener hold, and worthless before them.
/// The load-bearing assertion in here is
/// <see cref="EditingASymbolChangesEveryPlacementOfIt"/>, in pixels: everything
/// else is plumbing around that one promise.
/// </remarks>
public class SymbolEditingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-edit-{Guid.NewGuid():N}.lbproj");

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

    private MainViewModel WithSymbol(out Symbol symbol, int frames = 1)
    {
        // An animation to place the symbol into. It used to arrive free, from a
        // startup document; the application now opens with nothing, and a test
        // that reads `ActiveTab!` on an empty one gets a null it then assigns
        // back — which is how this file went from failing to hanging.
        var vm = VmLayers.PaperVm();
        var project = ProjectIo.Create("Knight", _root);
        symbol = new Symbol { Name = "Sword", Fps = 12 };
        var drawings = new List<Frame>();
        for (var i = 0; i < frames; i++)
        {
            drawings.Add(new Frame { Strokes = [Bar(30 + i * 10)] });
        }
        symbol.Layers = Symbol.Flat("Sword", drawings);
        project.Symbols[symbol.Id] = symbol;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        return vm;
    }

    private static Frame FrameOf(MainViewModel vm) =>
        (Frame)vm.Doc.Scene.Layers[^1].Cels[0].Frame!;

    /// <summary>
    /// A real edit on whatever tab is active, through the ordinary funnel.
    /// </summary>
    /// <remarks>
    /// Not a test seam. What is being asserted is that committing an edit is
    /// what syncs the symbol, so reaching past the commit and calling the sync
    /// directly would prove nothing about when it happens.
    /// </remarks>
    private static void Edit(MainViewModel vm, Stroke stroke) =>
        vm.AppendExternalStrokes(vm.ActiveLayerForIpc.Id, 0, [stroke]);

    // ---- opening -------------------------------------------------------------

    [AvaloniaFact]
    public void OpeningASymbolMakesATabForIt()
    {
        var vm = WithSymbol(out var sword);

        vm.OpenSymbol(sword);

        Assert.Equal(DocumentTabKind.Symbol, vm.ActiveTab!.Kind);
        Assert.Same(sword, vm.ActiveTab.Symbol);
        Assert.Contains("Sword", vm.ActiveTab.Title);
    }

    [AvaloniaFact]
    public void TheTabEditsTheSymbolsOwnFramesRatherThanCopies()
    {
        // The whole arrangement. A copy would need an apply step, and an apply
        // step is a thing to forget.
        var vm = WithSymbol(out var sword);

        vm.OpenSymbol(sword);

        Assert.Same(sword.Layers[0].Cels[0].Frame, vm.Doc.Scene.Layers[0].Cels[0].Frame);
    }

    [AvaloniaFact]
    public void OpeningTheSameSymbolTwiceFocusesTheTabItAlreadyHas()
    {
        var vm = WithSymbol(out var sword);
        vm.OpenSymbol(sword);
        var tabs = vm.Tabs.Count;

        vm.OpenSymbol(sword);

        Assert.Equal(tabs, vm.Tabs.Count);
    }

    [AvaloniaFact]
    public void ACycleOpensWithACelPerFrame()
    {
        var vm = WithSymbol(out var walk, frames: 4);

        vm.OpenSymbol(walk);

        Assert.Equal(4, vm.Doc.Scene.Layers[0].Cels.Count);
        Assert.Equal(4, vm.Doc.Scene.FrameCount);
        Assert.Equal(12, vm.Doc.Scene.Fps);
    }

    // ---- one layer, and layers are never frames -----------------------------------

    /// <summary>
    /// Adding a layer inside a symbol tab is refused.
    /// </summary>
    /// <remarks>
    /// <b>What this stops is not an error message — it is silent corruption.</b>
    /// A symbol's frame list is *time*, so before the guard a second layer here
    /// was folded into it and became frame 2 of the animation, for every
    /// placement of the symbol in the project. Measured before the fix: one
    /// frame and one layer in, two frames out.
    /// </remarks>
    [AvaloniaFact]
    public void AddingALayerInsideASymbolTabIsRefused()
    {
        var vm = WithSymbol(out var sword);
        vm.OpenSymbol(sword);

        vm.AddPaintedLayerCommand.Execute(null);

        Assert.Single(vm.Doc.Scene.Layers);
        Assert.Single(sword.AllFrames);
    }

    [AvaloniaFact]
    public void AddingALayerIsStillOrdinaryOutsideASymbolTab()
    {
        // The guard is about the symbol tab, not about symbols existing. A
        // project with a symbol in it must still make layers the ordinary way.
        var vm = WithSymbol(out _);
        var before = vm.Doc.Scene.Layers.Count;

        vm.AddPaintedLayerCommand.Execute(null);

        Assert.Equal(before + 1, vm.Doc.Scene.Layers.Count);
    }

    /// <summary>
    /// A layer that arrives by some other door still does not become a frame.
    /// </summary>
    /// <remarks>
    /// Refusing the gesture is not the same as making the fold impossible —
    /// a paste inserts a layer too, and so will the next feature nobody has
    /// written yet. The sync is the sink, so the sync is where it is closed.
    /// </remarks>
    [AvaloniaFact]
    public void ALayerArrivingByAnotherRouteDoesNotBecomeAFrame()
    {
        var vm = WithSymbol(out var sword, frames: 2);
        vm.OpenSymbol(sword);
        var own = vm.ActiveTab!.SymbolLayerId!;

        // Straight into the scene, the way a paste would put it there.
        vm.Doc.Scene.Layers.Add(new Layer
        {
            Name = "Pasted",
            Cels = [new Cel { Frame = new Frame { Strokes = [Bar(90)] } },
                    new Cel { Frame = new Frame { Strokes = [Bar(95)] } }],
        });

        vm.AppendExternalStrokes(own, 0, [Bar(70)]);

        // Two frames in, two frames out — not four.
        Assert.Equal(2, sword.FrameCount);
    }

    /// <summary>
    /// The symbol's own layer is read even when another is inserted above it.
    /// </summary>
    /// <remarks>
    /// Why <see cref="DocumentTab.SymbolLayerId"/> is an id and not the number
    /// zero: a paste inserts at the active index, so index 0 would quietly make
    /// the pasted work the symbol and the artist's drawing an extra frame of it
    /// — the same corruption wearing a different hat.
    /// </remarks>
    [AvaloniaFact]
    public void TheSymbolsOwnLayerIsReadEvenWithAnotherInsertedAboveIt()
    {
        var vm = WithSymbol(out var sword);
        vm.OpenSymbol(sword);
        var own = vm.ActiveTab!.SymbolLayerId!;
        var intruder = new Frame { Strokes = [Bar(95)] };
        vm.Doc.Scene.Layers.Insert(0, new Layer { Name = "Pasted", Cels = [new Cel { Frame = intruder }] });

        vm.AppendExternalStrokes(own, 0, [Bar(70)]);

        Assert.Single(sword.AllFrames);
        Assert.DoesNotContain(intruder, sword.AllFrames);
    }

    /// <summary>
    /// Placing a multi-frame symbol still imports it as an animation.
    /// </summary>
    /// <remarks>
    /// The guard is on the symbol's own tab and must not reach the document
    /// side of the feature: importing a cycle expands the *timeline*
    /// (<c>AppendFrame</c>), which is a different axis from the one the guard
    /// protects. When Q171's layer stack lands this is the path that grows to
    /// carry it, so it is worth a test that says it still works today.
    /// </remarks>
    [AvaloniaFact]
    public void ImportingACycleStillLandsItAcrossTheTimeline()
    {
        var vm = WithSymbol(out var walk, frames: 4);

        vm.PlaceSymbol(walk.Id, 40, 40);

        Assert.True(vm.Doc.Scene.FrameCount >= 4);
    }

    [AvaloniaFact]
    public void ASymbolTabHasNoPaperBehindIt()
    {
        // A symbol is placed over a drawing. A white sheet behind it while
        // editing would be a lie about what it looks like in use.
        var vm = WithSymbol(out var sword);

        vm.OpenSymbol(sword);

        Assert.True(vm.Doc.Scene.TransparentBackground);
    }

    [AvaloniaFact]
    public void ASymbolTabIsNotSomethingToSaveAsAFile()
    {
        // It belongs to the project, and the project's save writes it. Offering
        // Save As here would produce a document nothing references.
        var vm = WithSymbol(out var sword);

        vm.OpenSymbol(sword);

        Assert.Null(vm.SaveTargetTab);
    }

    [AvaloniaFact]
    public void AnEmptySymbolStillOpens()
    {
        var vm = WithSymbol(out _);
        var blank = new Symbol { Name = "Blank" };
        vm.ProjectDocker.Project!.Symbols[blank.Id] = blank;

        vm.OpenSymbol(blank);

        Assert.Single(vm.Doc.Scene.Layers[0].Cels);
    }

    // ---- the promise ------------------------------------------------------------

    [AvaloniaFact]
    public void EditingASymbolChangesEveryPlacementOfIt()
    {
        // Pillar 3's one sentence, in pixels. Two placements on an animation,
        // then a stroke added to the symbol in its own tab: both have to change
        // when we come back.
        var vm = WithSymbol(out var sword);
        var animation = vm.ActiveTab!;
        vm.PlaceSymbol(sword.Id, 40, 40);
        vm.PlaceSymbol(sword.Id, 160, 40);
        var before = InkCount(vm);
        Assert.True(before > 0);

        vm.OpenSymbol(sword);
        Edit(vm, Bar(70));

        vm.ActiveTab = animation;
        Assert.True(InkCount(vm) > before);
    }

    [AvaloniaFact]
    public void AnEditBumpsTheVersion()
    {
        // What the render cache is keyed by, so this is the mechanism the test
        // above is really asserting.
        var vm = WithSymbol(out var sword);
        vm.OpenSymbol(sword);
        var version = sword.Version;

        Edit(vm, Bar(70));

        Assert.True(sword.Version > version);
    }

    [AvaloniaFact]
    public void EditingAnAnimationDoesNotBumpAnySymbol()
    {
        // The bump only belongs to a symbol tab. Firing it on every edit
        // anywhere would invalidate every symbol's cached render on every
        // stroke of ordinary drawing.
        var vm = WithSymbol(out var sword);
        var version = sword.Version;

        Edit(vm, Bar(70));

        Assert.Equal(version, sword.Version);
    }

    [AvaloniaFact]
    public void AddingACelInTheSymbolTabAddsAFrameToTheSymbol()
    {
        // The list is not shared — only the frame objects are — so it has to be
        // re-read rather than assumed.
        var vm = WithSymbol(out var sword);
        vm.OpenSymbol(sword);

        vm.Doc.Scene.Layers[0].Cels.Add(new Cel { Frame = new Frame { Strokes = [Bar(90)] } });
        Edit(vm, Bar(95));

        Assert.Equal(2, sword.FrameCount);
    }

    [AvaloniaFact]
    public void ChangingTheSymbolsFpsSticksToTheSymbol()
    {
        var vm = WithSymbol(out var walk, frames: 4);
        vm.OpenSymbol(walk);

        vm.Doc.Scene.Fps = 24;
        Edit(vm, Bar(95));

        Assert.Equal(24, walk.Fps);
    }

    [AvaloniaFact]
    public void TheBrowserTileFollowsTheEdit()
    {
        var vm = WithSymbol(out var sword);
        vm.SymbolBrowser.Refresh();
        var row = vm.SymbolBrowser.Rows[0];
        var stale = row.ThumbVersion;
        vm.OpenSymbol(sword);

        Edit(vm, Bar(70));

        Assert.NotEqual(stale, row.ThumbVersion);
    }

    [AvaloniaFact]
    public void EditingTheSelectedSymbolOpensIt()
    {
        var vm = WithSymbol(out var sword);
        vm.SymbolBrowser.Refresh();
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows[0];

        vm.EditSelectedSymbolCommand.Execute(null);

        Assert.Same(sword, vm.ActiveTab!.Symbol);
    }

    // ---- what changed under you ---------------------------------------------------

    [AvaloniaFact]
    public void APlacementMadeBeforeAnEditIsReportedAsOutdated()
    {
        // S7. Reported, never acted on: the placement already shows the new
        // sword, which is the pillar working. What an animator needs is to know
        // which drawings changed under them.
        var vm = WithSymbol(out var sword);
        var animation = vm.ActiveTab!;
        vm.PlaceSymbol(sword.Id, 40, 40);
        Assert.Empty(vm.OutdatedPlacements);
        Assert.Null(vm.StalePlacementReport);

        vm.OpenSymbol(sword);
        Edit(vm, Bar(70));
        vm.ActiveTab = animation;

        Assert.Single(vm.OutdatedPlacements);
        Assert.Contains("Sword", vm.StalePlacementReport);
        Assert.Contains("changed since", vm.StalePlacementReport);
    }

    [AvaloniaFact]
    public void APlacementMadeAfterTheEditIsNotOutdated()
    {
        var vm = WithSymbol(out var sword);
        var animation = vm.ActiveTab!;
        vm.OpenSymbol(sword);
        Edit(vm, Bar(70));

        vm.ActiveTab = animation;
        vm.PlaceSymbol(sword.Id, 40, 40);

        Assert.Empty(vm.OutdatedPlacements);
    }

    [AvaloniaFact]
    public void TheReportCountsPlacementsAndNamesSymbols()
    {
        var vm = WithSymbol(out var sword);
        var animation = vm.ActiveTab!;
        vm.PlaceSymbol(sword.Id, 40, 40);
        vm.PlaceSymbol(sword.Id, 160, 40);

        vm.OpenSymbol(sword);
        Edit(vm, Bar(70));
        vm.ActiveTab = animation;

        Assert.Equal(2, vm.OutdatedPlacements.Count);
        Assert.Contains("2 placements", vm.StalePlacementReport);
    }

    [AvaloniaFact]
    public void AcknowledgingQuietensTheReportWithoutChangingTheDrawing()
    {
        // Acknowledgement, not repair. Nothing about what renders moves.
        var vm = WithSymbol(out var sword);
        var animation = vm.ActiveTab!;
        vm.PlaceSymbol(sword.Id, 40, 40);
        vm.OpenSymbol(sword);
        Edit(vm, Bar(70));
        vm.ActiveTab = animation;
        // The pixels, not a count of them: a count survives the placement being
        // nudged, which is exactly the kind of quiet repair this must not do.
        var before = Pixels(vm);

        Assert.Equal(1, vm.AcknowledgeOutdatedPlacements());

        Assert.Empty(vm.OutdatedPlacements);
        Assert.Null(vm.StalePlacementReport);
        Assert.Equal(before, Pixels(vm));
    }

    [AvaloniaFact]
    public void AcknowledgingIsAnUndoStep()
    {
        // The record changed, so it belongs in the history like anything else.
        var vm = WithSymbol(out var sword);
        var animation = vm.ActiveTab!;
        vm.PlaceSymbol(sword.Id, 40, 40);
        vm.OpenSymbol(sword);
        Edit(vm, Bar(70));
        vm.ActiveTab = animation;
        vm.AcknowledgeOutdatedPlacements();

        vm.UndoCommand.Execute(null);

        Assert.Single(vm.OutdatedPlacements);
    }

    [AvaloniaFact]
    public void AcknowledgingNothingIsNotAnEdit()
    {
        var vm = WithSymbol(out var sword);
        vm.PlaceSymbol(sword.Id, 40, 40);

        Assert.Equal(0, vm.AcknowledgeOutdatedPlacements());
    }

    private static byte[] Pixels(MainViewModel vm)
    {
        using var bmp = FrameRasterizer.Materialize(
            FrameOf(vm), vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        return bmp.GetPixelSpan().ToArray();
    }

    private static int InkCount(MainViewModel vm)
    {
        using var bmp = FrameRasterizer.Materialize(
            FrameOf(vm), vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        var count = 0;
        var pixels = bmp.GetPixelSpan();
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) count++;
        }
        return count;
    }
}
