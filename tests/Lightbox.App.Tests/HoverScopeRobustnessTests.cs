using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The hovered scope survives the states that made every panel binding fall
/// through to the canvas in the running application (B199).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported from a build, twice, against two different implementations</b> —
/// which is what earns this file its length. "Hovering the timeline and pressing
/// I still picks a colour", with the map correct, the binding intact, and the
/// end-to-end tests green both times. Each implementation read a signal that is
/// live and correct in a headless test and dead on the artist's machine:
/// </para>
/// <para>
/// <b>Round one: the cached element.</b> The source of the last
/// <c>PointerMoved</c> is detached the moment a docker rebuilds its rows — the
/// timeline does on every frame change — and a detached visual's parent chain
/// reaches no docker.
/// </para>
/// <para>
/// <b>Round two: the live <c>IsPointerOver</c> flags.</b> A pen leaves proximity
/// a centimetre off the tablet, between pointing at a panel and pressing the key
/// with the other hand. The leave clears every live flag — and the same event
/// has been observed leaving a docker's flag stale-<i>true</i> on X11, so the
/// flags are unreliable in both directions once the pointer is gone. A mouse
/// never leaves proximity, which is why no mouse-driven test, headless or real,
/// ever caught it.
/// </para>
/// <para>
/// The mechanism that survives both is the one these tests pin: the panel is an
/// <b>id resolved at move time</b>, overwritten by the next move, and kept on
/// <c>PointerExited</c> — with the live flags consulted only while the pointer
/// is known present.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class HoverScopeRobustnessTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static (MainWindow Window, MainViewModel Vm) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Untitled-1", 960, 540, 12, 72, "#ffffff", false));
        Pump();
        return (window, vm);
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static Point Centre(Visual item, Visual root) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;

    private static Docker DockerFor(MainWindow window, DockPanelId id) =>
        window.GetVisualDescendants().OfType<Docker>().First(d => d.PanelId == id);

    private static Lightbox.App.Rendering.CanvasControl CanvasOf(MainWindow window) =>
        window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

    // ---- the pen (round two) --------------------------------------------------

    /// <summary>
    /// Point the pen at the timeline, lift it off the tablet, press I with the
    /// other hand. The proximity leave kills every live pointer signal; the key
    /// must still belong to the timeline, because that is where the artist
    /// pointed and nothing since says otherwise.
    /// </summary>
    [AvaloniaFact]
    public void LiftingThePenOverTheTimelineKeepsTheTimeline()
    {
        var (window, _) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);

        window.MouseMove(Centre(timeline, window));
        Pump();
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);

        window.SimulateProximityLossForTests();
        Pump();

        output.WriteLine($"after proximity loss: {window.ShortcutScopeForTests}");
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);
    }

    /// <summary>
    /// The same lift, end to end: I still inserts a key rather than reaching
    /// for the eyedropper — the reported fault, both times it was reported.
    /// </summary>
    [AvaloniaFact]
    public void IStillInsertsAKeyAfterThePenLifts()
    {
        var (window, vm) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);

        window.MouseMove(Centre(timeline, window));
        Pump();
        window.SimulateProximityLossForTests();
        Pump();

        vm.ActiveTool = ToolId.Brush;
        vm.CurrentFrameIndex = 5;
        var before = KeyCount(vm);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.I });
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.I });
        Pump();

        output.WriteLine($"keys {before} -> {KeyCount(vm)}, tool {vm.ActiveTool}");
        Assert.True(KeyCount(vm) > before,
            "I did not insert a keyframe after the pen lifted over the timeline — it "
            + $"fell through to the general binding and the tool is now {vm.ActiveTool}, "
            + "which is the reported fault");
    }

    /// <summary>
    /// Lifting the pen over the CANVAS must not hand the canvas's keys to a
    /// panel: the memory is of the last place pointed, and that was no panel.
    /// </summary>
    [AvaloniaFact]
    public void LiftingThePenOverTheCanvasKeepsTheCanvas()
    {
        var (window, _) = Open();

        window.MouseMove(Centre(DockerFor(window, DockPanelId.Timeline), window));
        Pump();
        window.MouseMove(Centre(CanvasOf(window), window));
        Pump();
        window.SimulateProximityLossForTests();
        Pump();

        Assert.Equal(ShortcutScope.Canvas, window.ShortcutScopeForTests);
    }

    // ---- the rebuild (round one) ----------------------------------------------

    /// <summary>
    /// The timeline rebuilds its rows under a stationary pointer on every frame
    /// change. Nothing the resolver keeps may die with the old rows — which is
    /// why it keeps an id, not an element.
    /// </summary>
    [AvaloniaFact]
    public void TheScopeSurvivesTheRowsRebuildingUnderThePointer()
    {
        var (window, vm) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);

        window.MouseMove(Centre(timeline, window));
        Pump();
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);

        // Frame changes rebuild the timeline's rows; the pointer does not move.
        for (var f = 0; f < 4; f++)
        {
            vm.CurrentFrameIndex = f;
            Pump();
        }

        // And the harshest form: the live flags gone too, mid-rebuild.
        window.SimulateProximityLossForTests();
        Pump();

        output.WriteLine($"after rebuilds and proximity loss: {window.ShortcutScopeForTests}");
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);
    }

    // ---- the fix must not over-reach -------------------------------------------

    /// <summary>
    /// The canvas still answers when the pointer is genuinely over it — the fix
    /// cannot be "every key is a panel key".
    /// </summary>
    [AvaloniaFact]
    public void TheCanvasStillAnswersWhenThePointerIsNotOverADocker()
    {
        var (window, vm) = Open();

        window.MouseMove(Centre(CanvasOf(window), window));
        Pump();

        Assert.Equal(ShortcutScope.Canvas, window.ShortcutScopeForTests);

        vm.ActiveTool = ToolId.Brush;
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.I });
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.I });
        Pump();
        Assert.Equal(ToolId.Picker, vm.ActiveTool);
    }

    /// <summary>
    /// Moving from the timeline to the canvas RELEASES the timeline — the
    /// memory is one move deep, which is what separates "the pen went away"
    /// from "the artist pointed somewhere else".
    /// </summary>
    [AvaloniaFact]
    public void MovingToTheCanvasReleasesTheRememberedPanel()
    {
        var (window, _) = Open();

        window.MouseMove(Centre(DockerFor(window, DockPanelId.Timeline), window));
        Pump();
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);

        window.MouseMove(Centre(CanvasOf(window), window));
        Pump();
        Assert.Equal(ShortcutScope.Canvas, window.ShortcutScopeForTests);
    }

    /// <summary>
    /// A parked panel is in the tree at zero size. It must never claim the
    /// pointer — the same bug pointing the other way.
    /// </summary>
    [AvaloniaFact]
    public void APooledPanelNeverClaimsThePointer()
    {
        var (window, _) = Open();

        window.MouseMove(Centre(CanvasOf(window), window));
        Pump();

        var parked = window.GetVisualDescendants().OfType<Docker>()
            .Where(d => d.Bounds.Width < 1).Select(d => d.PanelId).ToList();
        output.WriteLine($"parked: {string.Join(", ", parked)}");
        Assert.NotEmpty(parked);
        Assert.Equal(ShortcutScope.Canvas, window.ShortcutScopeForTests);
    }

    private static int KeyCount(MainViewModel vm)
    {
        var n = 0;
        foreach (var layer in vm.Doc.Scene.Layers)
            foreach (var cel in layer.Cels)
                if (cel.Frame is { Role: FrameRole.Key }) n++;
        return n;
    }
}
