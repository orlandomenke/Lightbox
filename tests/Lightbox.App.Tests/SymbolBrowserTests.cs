using Avalonia.Headless.XUnit;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Pillar 3, step five: the panel that makes a symbol something you can find
/// and put down.
/// </summary>
/// <remarks>
/// The six libraries the roadmap asks for — pose, expression, hand, face, prop,
/// FX — are the kind filter in here, not six panels. That is the whole reason
/// they land together.
/// </remarks>
public class SymbolBrowserTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-browse-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static Stroke Bar(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(40, y, 1), new StrokePoint(90, y + 20, 1)],
        Brush = new BrushSettings
        {
            Size = 12, Hardness = 0.8, Opacity = 1, Flow = 1, Spacing = 0.15,
            Scatter = 0.6, SizeJitter = 0.5, HueJitter = 0.3,
        },
    };

    private MainViewModel WithProject()
    {
        var vm = new MainViewModel(null);
        vm.ProjectDocker.Project = ProjectIo.Create("Knight", _root);
        vm.RefreshProjectResources();
        vm.SymbolBrowser.Refresh();
        return vm;
    }

    private static Frame FrameOf(MainViewModel vm) =>
        (Frame)vm.Doc.Scene.Layers[^1].Cels[0].Frame!;

    private static Symbol Add(MainViewModel vm, string name, SymbolKind kind, params string[] tags)
    {
        var symbol = new Symbol
        {
            Name = name,
            Kind = kind,
            Tags = [.. tags],
            Frames = [new Frame { Strokes = [Bar(40)] }],
        };
        vm.ProjectDocker.Project!.Symbols[symbol.Id] = symbol;
        vm.RefreshProjectResources();
        vm.SymbolBrowser.Refresh();
        return symbol;
    }

    // ---- the panel exists only when it can ------------------------------------

    [AvaloniaFact]
    public void WithNoProjectThereIsNothingToBrowse()
    {
        // Optional means absent. A single painting has nowhere to keep a symbol,
        // so the panel is not a disabled panel — it is no panel.
        var vm = new MainViewModel(null);

        vm.SymbolBrowser.Refresh();

        Assert.Empty(vm.SymbolBrowser.Rows);
        Assert.False(vm.SymbolBrowser.HasAny);
    }

    [AvaloniaFact]
    public void TheSymbolsPanelIsInTheCatalogueAndCanBeToggled()
    {
        var vm = WithProject();
        var visible = vm.Workspace.SymbolsPanelVisible;

        vm.ToggleSymbolsPanelCommand.Execute(null);

        Assert.NotEqual(visible, vm.Workspace.SymbolsPanelVisible);
        Assert.Contains(DockPanels.All, p => p.Id == DockPanelId.Symbols);
    }

    [AvaloniaFact]
    public void OpeningAProjectFillsTheBrowser()
    {
        // The browser follows the project, and adopting one is not an edit —
        // the same relay the project panel needed.
        var vm = new MainViewModel(null);
        var project = ProjectIo.Create("Knight", _root);
        var sword = new Symbol { Name = "Sword", Frames = [new Frame { Strokes = [Bar(40)] }] };
        project.Symbols[sword.Id] = sword;

        vm.ProjectDocker.Project = project;

        Assert.Single(vm.SymbolBrowser.Rows);
    }

    // ---- finding one ---------------------------------------------------------------

    [AvaloniaFact]
    public void TheKindFilterIsWhatTheSixLibrariesAre()
    {
        var vm = WithProject();
        Add(vm, "Sword", SymbolKind.Prop);
        Add(vm, "Grin", SymbolKind.Expression);
        Add(vm, "Fist", SymbolKind.Hand);

        vm.SymbolBrowser.KindFilter = SymbolKind.Expression;

        Assert.Equal("Grin", Assert.Single(vm.SymbolBrowser.Rows).Name);
    }

    [AvaloniaFact]
    public void NoFilterShowsEverything()
    {
        // The default, and deliberately so: a filing system is only worth using
        // once there is too much to look at.
        var vm = WithProject();
        Add(vm, "Sword", SymbolKind.Prop);
        Add(vm, "Grin", SymbolKind.Expression);

        Assert.Null(vm.SymbolBrowser.KindFilter);
        Assert.Equal(2, vm.SymbolBrowser.Rows.Count);
    }

    [AvaloniaFact]
    public void SearchMatchesANameOrATag()
    {
        var vm = WithProject();
        Add(vm, "Sword", SymbolKind.Prop, "knight", "act two");
        Add(vm, "Lantern", SymbolKind.Prop, "act one");

        vm.SymbolBrowser.Search = "swo";
        Assert.Equal("Sword", Assert.Single(vm.SymbolBrowser.Rows).Name);

        // A sword is a prop, and it is also "knight", and also "act two".
        // Filing it once would make the other two searches fail.
        vm.SymbolBrowser.Search = "act two";
        Assert.Equal("Sword", Assert.Single(vm.SymbolBrowser.Rows).Name);
    }

    [AvaloniaFact]
    public void SearchAndKindNarrowTogether()
    {
        var vm = WithProject();
        Add(vm, "Sword", SymbolKind.Prop, "knight");
        Add(vm, "Sword hand", SymbolKind.Hand, "knight");

        vm.SymbolBrowser.Search = "sword";
        vm.SymbolBrowser.KindFilter = SymbolKind.Hand;

        Assert.Equal("Sword hand", Assert.Single(vm.SymbolBrowser.Rows).Name);
    }

    [AvaloniaFact]
    public void AnEmptyGridSaysWhichKindOfEmptyItIs()
    {
        // Two different emptinesses, and telling them apart is the whole value
        // of the message.
        var vm = WithProject();
        Assert.Contains("No symbols yet", vm.SymbolBrowser.EmptyMessage);

        Add(vm, "Sword", SymbolKind.Prop);
        vm.SymbolBrowser.Search = "nothing like this";

        Assert.Contains("No symbol matches", vm.SymbolBrowser.EmptyMessage);
    }

    [AvaloniaFact]
    public void EveryTileGetsAThumbnail()
    {
        var vm = WithProject();
        Add(vm, "Sword", SymbolKind.Prop);

        Assert.NotNull(Assert.Single(vm.SymbolBrowser.Rows).Thumb);
    }

    // ---- making one ------------------------------------------------------------------

    [AvaloniaFact]
    public void MakingASymbolTakesTheDrawingAndLeavesAPlacement()
    {
        var vm = WithProject();
        FrameOf(vm).Strokes.Add(Bar(40));

        var symbol = vm.MakeSymbolFromDrawing("Sword");

        Assert.NotNull(symbol);
        var frame = FrameOf(vm);
        Assert.Empty(frame.Strokes);
        Assert.Equal(symbol!.Id, Assert.Single(frame.Placements!).SymbolId);
        Assert.Single(vm.ProjectDocker.Project!.Symbols);
    }

    [AvaloniaFact]
    public void MakingASymbolDoesNotChangeWhatTheDrawingLooksLike()
    {
        // The promise the gesture makes. The strokes keep their coordinates and
        // the placement sits at the origin, so no Hash01 seed moves and the
        // mark is the same mark — which is exactly what would break if the
        // strokes were re-based to their own bounding box.
        var vm = WithProject();
        FrameOf(vm).Strokes.Add(Bar(40));
        var before = Pixels(vm);
        // Not a comparison of two blank canvases.
        Assert.Contains(before.Where((_, i) => i % 4 == 3), a => a != 0);

        vm.MakeSymbolFromDrawing("Sword");

        Assert.Equal(before, Pixels(vm));
    }

    [AvaloniaFact]
    public void MakingASymbolIsOneUndoStep()
    {
        var vm = WithProject();
        FrameOf(vm).Strokes.Add(Bar(40));
        vm.MakeSymbolFromDrawing("Sword");

        vm.UndoCommand.Execute(null);

        var frame = FrameOf(vm);
        Assert.Single(frame.Strokes);
        Assert.Null(frame.Placements);
    }

    [AvaloniaFact]
    public void MakingASymbolFromNothingSaysSo()
    {
        var vm = WithProject();

        Assert.Null(vm.MakeSymbolFromDrawing("Sword"));
        Assert.Contains("Nothing on this drawing", vm.AiStatus);
    }

    [AvaloniaFact]
    public void WithoutAProjectThereIsNowhereToPutASymbol()
    {
        var vm = new MainViewModel(null);
        FrameOf(vm).Strokes.Add(Bar(40));

        Assert.Null(vm.MakeSymbolFromDrawing("Sword"));
        Assert.Contains("project", vm.AiStatus);
    }

    [AvaloniaFact]
    public void AnUnnamedSymbolStillGetsAName()
    {
        var vm = WithProject();
        FrameOf(vm).Strokes.Add(Bar(40));

        Assert.Equal("Symbol", vm.MakeSymbolFromDrawing("   ")!.Name);
    }

    // ---- placing and removing ----------------------------------------------------------

    [AvaloniaFact]
    public void PlacingTheSelectedSymbolPutsItOnTheDrawing()
    {
        var vm = WithProject();
        Add(vm, "Sword", SymbolKind.Prop);
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows[0];

        var placement = vm.PlaceSelectedSymbol();

        Assert.NotNull(placement);
        Assert.Single(FrameOf(vm).Placements!);
    }

    [AvaloniaFact]
    public void PlaceGoesToTheMiddleAndADropGoesWhereItWasDropped()
    {
        // The two routes are deliberately different, and that difference is the
        // whole reason dragging exists: Place is the keyboard answer and cannot
        // know where you wanted it, a drop can. Collapsing them would make the
        // drag pointless.
        var vm = WithProject();
        var sword = Add(vm, "Sword", SymbolKind.Prop);
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows[0];

        var placed = vm.PlaceSelectedSymbol()!;
        var dropped = vm.PlaceSymbol(sword.Id, 12, 34)!;

        Assert.Equal(vm.Doc.Scene.Width / 2.0, placed.X);
        Assert.Equal(vm.Doc.Scene.Height / 2.0, placed.Y);
        Assert.Equal(12, dropped.X);
        Assert.Equal(34, dropped.Y);
    }

    [AvaloniaFact]
    public void PlacingWithNothingSelectedDoesNothing()
    {
        var vm = WithProject();
        Add(vm, "Sword", SymbolKind.Prop);

        Assert.Null(vm.PlaceSelectedSymbol());
        Assert.Null(FrameOf(vm).Placements);
    }

    [AvaloniaFact]
    public void DeletingASymbolLeavesItsPlacementsAloneAndTheyStopDrawing()
    {
        // Walking every document in the project to tidy up — including the ones
        // not loaded — is not something a delete may do. An unresolved
        // placement draws nothing and is reported, which is the recoverable
        // failure of the two.
        var vm = WithProject();
        var sword = Add(vm, "Sword", SymbolKind.Prop);
        vm.SymbolBrowser.Selected = vm.SymbolBrowser.Rows[0];
        vm.PlaceSelectedSymbol();

        Assert.True(vm.DeleteSymbol(sword));

        Assert.Empty(vm.SymbolBrowser.Rows);
        Assert.Single(FrameOf(vm).Placements!);
        Assert.Null(SymbolRegistry.Resolve(sword.Id));
    }

    // ---- editing a row ------------------------------------------------------------------

    [AvaloniaFact]
    public void RenamingARowRenamesTheSymbol()
    {
        var vm = WithProject();
        var sword = Add(vm, "Sword", SymbolKind.Prop);

        vm.SymbolBrowser.Rows[0].Name = "Broadsword";

        Assert.Equal("Broadsword", sword.Name);
    }

    [AvaloniaFact]
    public void RenamingDoesNotCountAsEditingTheDrawing()
    {
        // Or "the sword changed since you placed this" fires because somebody
        // fixed a typo.
        var vm = WithProject();
        var sword = Add(vm, "Sword", SymbolKind.Prop);
        var version = sword.Version;

        vm.SymbolBrowser.Rows[0].Name = "Broadsword";

        Assert.Equal(version, sword.Version);
    }

    [AvaloniaFact]
    public void TagsEditAsOneLine()
    {
        var vm = WithProject();
        var sword = Add(vm, "Sword", SymbolKind.Prop, "knight");

        vm.SymbolBrowser.Rows[0].Tags = "knight, act two,  sharp ";

        Assert.Equal(["knight", "act two", "sharp"], sword.Tags);
    }

    private static byte[] Pixels(MainViewModel vm)
    {
        using var bmp = FrameRasterizer.Materialize(
            FrameOf(vm), vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        return bmp.GetPixelSpan().ToArray();
    }
}
