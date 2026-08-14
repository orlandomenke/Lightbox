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
/// The hovered scope survives the two things that made every panel binding fall
/// through to the canvas in the real application (B199).
/// </summary>
/// <remarks>
/// <para>
/// <b>Reported from a build, and invisible to every test that existed.</b>
/// "Hovering the timeline and pressing I still picks a colour" — with the map
/// correct, the binding intact, and <c>HoverShortcutScopeTests</c> green. Those
/// tests move the mouse and immediately press a key against a still visual tree,
/// which is the one situation in which the old implementation could not fail.
/// </para>
/// <para>
/// <b>The two weaknesses, both in the same sentence of code.</b> The scope came
/// from a <em>cached</em> element (the source of the last <c>PointerMoved</c>)
/// resolved through <em>derived</em> bookkeeping (<c>Docker.ActiveTab</c>). A
/// cache goes stale when a docker rebuilds the row under a stationary pointer —
/// the timeline does this on every frame change — because the remembered visual
/// is detached and its parent chain no longer reaches a docker. Bookkeeping goes
/// stale if anything writes it without a layout pass. Either one resolves to the
/// canvas, and the canvas is a <em>plausible</em> answer, so nothing looks
/// broken: the key just quietly means the wrong thing.
/// </para>
/// <para>
/// Both tests below fail against the previous implementation and pass against
/// the one that asks the dockers what the pointer is over.
/// </para>
/// </remarks>
public class HoverScopeRobustnessTests(ITestOutputHelper output)
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

    /// <summary>
    /// The bookkeeping says one panel and the control on screen is another. The
    /// pointer is inside the control, so the control wins.
    /// </summary>
    /// <remarks>
    /// Reproduced the report exactly before the fix: scope came back as
    /// <c>Panel/Xsheet</c> — the timeline's tab-group neighbour, which binds no
    /// <c>I</c> — so the key fell through to the general eyedropper.
    /// </remarks>
    [AvaloniaFact]
    public void TheScopeFollowsTheDockerOnScreenRatherThanItsTabBookkeeping()
    {
        var (window, _) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);

        // The timeline shares its slot with X-sheet and Graph editor.
        timeline.ActiveTab = DockPanelId.Xsheet;
        Pump();

        window.MouseMove(Centre(timeline, window));
        Pump();

        output.WriteLine($"PanelId={timeline.PanelId} ActiveTab={timeline.ActiveTab} "
            + $"scope={window.ShortcutScopeForTests}");
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);
    }

    /// <summary>
    /// The pointer has not moved and the docker has rebuilt what is under it.
    /// The remembered element is detached; the pointer is still in the timeline.
    /// </summary>
    [AvaloniaFact]
    public void TheScopeSurvivesTheRememberedElementBeingDetached()
    {
        var (window, vm) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);

        window.MouseMove(Centre(timeline, window));
        Pump();
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);

        // Exactly what a rebuilt row leaves behind: a visual with no parent.
        window.ForgetHoveredElementForTests(new Border());
        Pump();

        output.WriteLine($"after detaching the remembered element: {window.ShortcutScopeForTests}");
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);
    }

    /// <summary>
    /// And the whole point of it, end to end: the key still means what the panel
    /// says, from a state where it used to mean the general thing.
    /// </summary>
    [AvaloniaFact]
    public void IStillInsertsAKeyOverATimelineThatHasRebuiltUnderThePointer()
    {
        var (window, vm) = Open();
        var timeline = DockerFor(window, DockPanelId.Timeline);

        window.MouseMove(Centre(timeline, window));
        Pump();
        window.ForgetHoveredElementForTests(new Border());
        timeline.ActiveTab = DockPanelId.GraphEditor;
        Pump();

        vm.ActiveTool = ToolId.Brush;
        vm.CurrentFrameIndex = 5;
        var before = KeyCount(vm);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.I });
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.I });
        Pump();

        output.WriteLine($"keys {before} -> {KeyCount(vm)}, tool {vm.ActiveTool}");
        Assert.True(KeyCount(vm) > before,
            "I did not insert a keyframe over the timeline — it fell through to the "
            + $"general binding and the tool is now {vm.ActiveTool}, which is the reported fault");
    }

    /// <summary>
    /// The other half, so the fix cannot be "every key is a timeline key": the
    /// canvas still answers when the pointer is genuinely not over a docker.
    /// </summary>
    [AvaloniaFact]
    public void TheCanvasStillAnswersWhenThePointerIsNotOverADocker()
    {
        var (window, vm) = Open();
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

        window.MouseMove(Centre(canvas, window));
        Pump();

        Assert.Equal(ShortcutScope.Canvas, window.ShortcutScopeForTests);

        vm.ActiveTool = ToolId.Brush;
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.I });
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.I });
        Pump();
        Assert.Equal(ToolId.Picker, vm.ActiveTool);
    }

    /// <summary>
    /// A parked panel is in the tree at zero size. Without the visibility test it
    /// could claim a pointer that is nowhere near it, which would be the same
    /// bug pointing the other way.
    /// </summary>
    [AvaloniaFact]
    public void APooledPanelNeverClaimsThePointer()
    {
        var (window, _) = Open();
        var canvas = window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First();

        window.MouseMove(Centre(canvas, window));
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
