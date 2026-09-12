using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// B374: a press on a symbol tile has to reach the handler that starts the drag.
/// </summary>
/// <remarks>
/// <para>
/// The manual has promised drag-to-place since the feature landed. It never
/// worked: <c>OnSymbolTilePressed</c> was registered in XAML as a plain
/// bubbling handler on the <c>SymbolTiles</c> ListBox, and a ListBox's item
/// container marks the press handled on the way back up, so the handler was
/// skipped every time.
/// </para>
/// <para>
/// <b>This covers the press only.</b> Headless Avalonia registers no
/// <c>IPlatformDragSource</c>, so <c>DoDragDropAsync</c> never completes and no
/// test here can assert that a drag carries or drops. The receiver half is
/// covered separately, and the end-to-end gesture needs the real app.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SymbolTileDragTests(ITestOutputHelper output) : BrushStateIsolated
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-tiledrag-{Guid.NewGuid():N}.lbproj");

    public override void Dispose()
    {
        SymbolRegistry.Clear();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static Stroke Bar(double x, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(x, y, 1), new StrokePoint(x + 40, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static Symbol Prop(string name) => new()
    {
        Name = name,
        PivotX = 0,
        PivotY = 0,
        Layers = Symbol.Flat(name, [new Frame { Strokes = [Bar(0, 0)] }]),
    };

    // ---- 4. the drag that does not work --------------------------------------------

    private static object? Peek(object o, string field) =>
        o.GetType().GetField(field,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(o);

    /// <summary>
    /// Pressing a symbol tile must arm the drag gesture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this asserts is that the press arms the gesture.</b>
    /// <c>OnSymbolTilePressed</c> used to be registered in XAML as a plain
    /// bubbling handler on the SymbolTiles ListBox, and the press is already
    /// marked handled by the item container before it bubbles that far — so it
    /// never ran: <c>_tilePress</c> stayed null, the move and release handlers
    /// were never subscribed, and <c>DoDragDropAsync</c> was never reached. It
    /// tunnels now. Note the press still arrives at the ListBox handled, which
    /// is exactly why a bubbling registration cannot work here.
    /// </para>
    /// <para>
    /// <b>The first version of this test passed while asserting the opposite.</b>
    /// It raised a synthetic <c>PointerPressedEventArgs</c> on the container with
    /// <c>RaiseEvent</c>, which does not engage the container's selection
    /// handler, so nothing marked the event handled and the press looked clean.
    /// Its own output said <c>selection after the press: none</c>, which was the
    /// tell. Real input is the only thing that answers this question.
    /// </para>
    /// <para>
    /// <b>Why the control is rehosted.</b> In the docked window the docker gives
    /// the panel a 38 px viewport and clips the grid, so no point in the window
    /// hit-tests to a tile — and <c>TranslatePoint</c> still returns a
    /// plausible-looking coordinate, so a press aimed that way lands elsewhere
    /// and looks exactly like a swallowed press. Detaching the real ListBox into
    /// a plain window is what makes the gesture reachable headless.
    /// </para>
    /// <para>
    /// This covers the press only. Headless Avalonia registers no
    /// <c>IPlatformDragSource</c>, so <c>DoDragDropAsync</c> never completes and
    /// no test here can assert that a drag carries or drops. The end-to-end
    /// gesture needs a check in the real app.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void PressingASymbolTileArmsTheDragGesture()
    {
        var window = new MainWindow { Width = 1600, Height = 1000 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;

        var project = ProjectIo.Create("Knight", _root);
        foreach (var n in new[] { "sword", "shield" })
        {
            var s = Prop(n);
            project.Symbols[s.Id] = s;
        }
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        vm.SymbolBrowser.Refresh();
        Pump();

        foreach (var id in Enum.GetValues<Lightbox.App.Docking.DockPanelId>())
        {
            if (id != Lightbox.App.Docking.DockPanelId.Symbols) vm.Workspace.SetVisible(id, false);
        }
        vm.Workspace.SetVisible(Lightbox.App.Docking.DockPanelId.Symbols, true);
        Pump();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump();

        var tiles = window.GetVisualDescendants().OfType<ListBox>()
            .FirstOrDefault(l => l.Name == "SymbolTiles");
        Assert.NotNull(tiles);

        // The real control, its real template and its real XAML registration,
        // moved somewhere headless layout will give it room.
        switch (tiles!.Parent)
        {
            case ContentControl cc: cc.Content = null; break;
            case Panel p: p.Children.Remove(tiles); break;
            case Decorator d: d.Child = null; break;
            default: Assert.Fail($"cannot detach from {tiles.Parent?.GetType().Name ?? "null"}"); break;
        }
        Pump();
        tiles.Width = 400;
        tiles.Height = 400;
        // Detaching cost it the inherited DataContext its ItemsSource binds to.
        tiles.DataContext = vm;

        var host = new Window { Width = 600, Height = 600, Content = tiles };
        host.Show();

        // **Laid out explicitly, and waited for.** Relying on the render timer
        // alone made this pass alone and fail inside a full-suite run: under
        // load the containers were not realized by the time the sweep ran, the
        // sweep found nothing, and a press that lands nowhere is identical to
        // one that was swallowed — the exact confusion this test exists to
        // resolve. Measure/Arrange forces layout rather than awaiting it.
        Point? found = null;
        Control? hit = null;
        for (var attempt = 0; attempt < 20 && found is null; attempt++)
        {
            host.Measure(new Size(600, 600));
            host.Arrange(new Rect(0, 0, 600, 600));
            Pump();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Pump();

            for (var y = 0d; y < 600 && found is null; y += 2)
            {
                for (var x = 0d; x < 600; x += 2)
                {
                    if (host.InputHitTest(new Point(x, y)) is not Control c) continue;
                    if (c.DataContext is not SymbolRow) continue;
                    found = new Point(x, y);
                    hit = c;
                    break;
                }
            }
        }

        output.WriteLine($"a point that hits a tile: {found?.ToString() ?? "NONE"} "
            + $"({hit?.GetType().Name} / {(hit?.DataContext as SymbolRow)?.Name})");
        Assert.NotEmpty(tiles.GetVisualDescendants().OfType<ListBoxItem>().ToList());
        Assert.NotNull(found);

        var handledOnArrival = false;
        var leftButton = false;
        object? sourceDc = null;
        tiles.AddHandler(InputElement.PointerPressedEvent,
            (object? _, PointerPressedEventArgs e) =>
            {
                handledOnArrival = e.Handled;
                leftButton = e.GetCurrentPoint(tiles).Properties.IsLeftButtonPressed;
                sourceDc = (e.Source as Control)?.DataContext;
            },
            RoutingStrategies.Bubble, handledEventsToo: true);

        host.MouseDown(found!.Value, MouseButton.Left);
        Pump();

        output.WriteLine($"selection after the press: {vm.SymbolBrowser.Selected?.Name ?? "none"}");
        output.WriteLine($"handled by the time it reached the ListBox: {handledOnArrival}");
        output.WriteLine("both of OnSymbolTilePressed's guards would have passed: "
            + $"left button = {leftButton}, e.Source DataContext = {sourceDc?.GetType().Name ?? "null"}");
        output.WriteLine($"_tilePress:    {Peek(window, "_tilePress") ?? "null"}");
        output.WriteLine($"_tileSymbolId: {Peek(window, "_tileSymbolId") ?? "null"}");

        // The press did reach the tile: the selection moved.
        Assert.Equal(
            (hit!.DataContext as SymbolRow)!.Name,
            vm.SymbolBrowser.Selected?.Name);

        Assert.NotNull(Peek(window, "_tilePress"));
        Assert.NotNull(Peek(window, "_tileSymbolId"));
    }

    private static int PlacementCount(MainViewModel vm) =>
        vm.Doc.Scene.Layers
            .SelectMany(l => l.Cels)
            .Select(c => c.Frame)
            .OfType<Frame>()
            .Sum(f => f.Placements?.Count ?? 0);

    /// <summary>
    /// The other half: the canvas takes a dropped symbol and places it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This half was never broken, and it is worth a guard anyway — B374 was one
    /// fault in a two-part gesture, and without this there is nothing saying
    /// which part works.
    /// </para>
    /// <para>
    /// <b>It raises the drag events directly on the canvas</b>, because headless
    /// Avalonia registers no <c>IPlatformDragSource</c> and an OS-mediated drag
    /// cannot be driven here at all. So this proves the handlers, the format
    /// round-trip and the placement — not that a real drag session delivers the
    /// payload intact. That last step needs the real app.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheCanvasTakesADroppedSymbolAndPlacesIt()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;

        var project = ProjectIo.Create("Knight", _root);
        var sword = Prop("sword");
        project.Symbols[sword.Id] = sword;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        vm.SymbolBrowser.Refresh();
        Pump();
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;

        var canvas = window.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.Name == "Canvas");
        Assert.NotNull(canvas);
        Assert.True(DragDrop.GetAllowDrop(canvas!),
            "the canvas does not accept drops at all, so nothing can be dragged onto it");

        // The same in-process format the tile drag puts on the wire. Constructed
        // fresh here on purpose: if DataFormat stopped comparing by name, the
        // sender and the receiver would silently stop agreeing.
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(
            DataFormat.CreateInProcessFormat<string>("lightbox-symbol"), sword.Id));

        var over = new DragEventArgs(
            DragDrop.DragOverEvent, transfer, canvas!, new Point(40, 40), KeyModifiers.None);
        canvas!.RaiseEvent(over);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);

        var before = PlacementCount(vm);
        var drop = new DragEventArgs(
            DragDrop.DropEvent, transfer, canvas!, new Point(40, 40), KeyModifiers.None);
        canvas.RaiseEvent(drop);
        Pump();

        output.WriteLine($"placements {before} -> {PlacementCount(vm)}, status: {vm.AiStatus}");
        Assert.Equal(before + 1, PlacementCount(vm));
    }
}
