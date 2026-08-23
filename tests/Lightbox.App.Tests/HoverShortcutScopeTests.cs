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
/// The pointer decides what a key does — driven through the real window rather
/// than asked of the map.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these exist beside <c>ContextShortcutTests</c>.</b> Those call
/// <see cref="ShortcutMap.IdFor(KeyEventArgs, ShortcutScope)"/> directly, so
/// they prove the resolver and say nothing about whether a scope ever reaches
/// it. Every test of this feature lived in that gap, and so did a hole big
/// enough to drive a panel through: a docker torn out into its own window
/// answered <em>no shortcuts at all</em> — not its own, not the general ones —
/// because <c>FloatingPanelWindow</c> wired no key handling and the main window
/// never saw the press. The resolver was correct, tested, and unreachable from
/// half the places a docker can be.
/// </para>
/// <para>
/// <b>Reading a scope off a side effect is how a probe lies.</b> Writing these,
/// "insert a key at the playhead" reported the shortcut dead three times over —
/// the playhead was parked on a frame that was already a key, so the command ran
/// and changed nothing. <see cref="Verdict"/> moves to a fresh frame every time
/// for that reason, and <c>ShortcutScopeForTests</c> exists so the scope can be
/// asserted directly rather than inferred.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class HoverShortcutScopeTests(ITestOutputHelper output) : BrushStateIsolated
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

    /// <summary>A whole press: down and up, because a momentary key latches on the release.</summary>
    private static void Press(Interactive from, Key key)
    {
        from.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        from.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
        Pump();
    }

    private static Point Centre(Visual item, Visual root) =>
        item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;

    private static Docker DockerFor(MainWindow window, DockPanelId id) =>
        window.GetVisualDescendants().OfType<Docker>().First(d => d.PanelId == id);

    private static void Hover(MainWindow window, Visual over)
    {
        window.MouseMove(Centre(over, window));
        Pump();
    }

    /// <summary>What <c>I</c> meant: the timeline's insert, the general eyedropper, or nothing.</summary>
    private enum WhatIMeant { Nothing, Eyedropper, InsertedAKey }

    private int _probeFrame = 3;

    private WhatIMeant Verdict(Interactive from, MainViewModel vm)
    {
        vm.ActiveTool = ToolId.Brush;
        // A frame with no key on it, and a different one each time: re-keying a
        // frame that is already a key changes nothing and reads as "dead".
        vm.CurrentFrameIndex = _probeFrame += 2;
        var keys = KeyCount(vm);
        Press(from, Key.I);
        if (KeyCount(vm) > keys) return WhatIMeant.InsertedAKey;
        return vm.ActiveTool == ToolId.Picker ? WhatIMeant.Eyedropper : WhatIMeant.Nothing;
    }

    private static int KeyCount(MainViewModel vm)
    {
        var n = 0;
        foreach (var layer in vm.Doc.Scene.Layers)
            foreach (var cel in layer.Cels)
                if (cel.Frame is { Role: FrameRole.Key }) n++;
        return n;
    }

    // ---- docked ------------------------------------------------------------

    /// <summary>The pair the whole scheme is named for, end to end.</summary>
    [AvaloniaFact]
    public void IInsertsAKeyOverTheTimelineAndReachesForTheEyedropperOverTheCanvas()
    {
        var (window, vm) = Open();

        Hover(window, DockerFor(window, DockPanelId.Timeline));
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);
        var overTimeline = Verdict(window, vm);

        Hover(window, window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First());
        Assert.Equal(ShortcutScope.Canvas, window.ShortcutScopeForTests);
        var overCanvas = Verdict(window, vm);

        output.WriteLine($"over the timeline: {overTimeline}; over the canvas: {overCanvas}");
        Assert.Equal(WhatIMeant.InsertedAKey, overTimeline);
        Assert.Equal(WhatIMeant.Eyedropper, overCanvas);
    }

    /// <summary>
    /// The half an artist notices: a docker with no <c>I</c> of its own does not
    /// swallow the key, it passes it on.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(DockPanelId.Layers)]
    [InlineData(DockPanelId.Color)]
    [InlineData(DockPanelId.Sheets)]
    public void ADockerThatClaimsNoKeyStillGetsTheGeneralOne(DockPanelId panel)
    {
        var (window, vm) = Open();

        Hover(window, DockerFor(window, panel));
        Assert.Equal(ShortcutScope.In(panel), window.ShortcutScopeForTests);

        Assert.Equal(WhatIMeant.Eyedropper, Verdict(window, vm));
    }

    /// <summary>
    /// The scope follows the pointer while the docker redraws under it. The
    /// timeline rebuilds its rows constantly — every frame change, every layer —
    /// and a scope that only survived a still tree would work once and then
    /// silently revert to the canvas.
    /// </summary>
    [AvaloniaFact]
    public void TheScopeSurvivesTheDockerRebuildingUnderAStationaryPointer()
    {
        var (window, vm) = Open();
        Hover(window, DockerFor(window, DockPanelId.Timeline));

        Assert.Equal(WhatIMeant.InsertedAKey, Verdict(window, vm));

        // Grow the timeline, which rebuilds every track row, and do not touch
        // the mouse.
        vm.CurrentFrameIndex = 30;
        vm.InsertKeyframeAtPlayhead();
        Pump();

        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), window.ShortcutScopeForTests);
        Assert.Equal(WhatIMeant.InsertedAKey, Verdict(window, vm));
    }

    // ---- torn off ----------------------------------------------------------

    /// <summary>
    /// <b>The gap this branch closed.</b> A floating timeline answered nothing
    /// at all — <c>I</c> neither inserted nor picked.
    /// </summary>
    [AvaloniaFact]
    public void ATornOffPanelStillAnswersItsOwnShortcuts()
    {
        var (window, vm) = Open();
        vm.Workspace.Float(DockPanelId.Timeline, 100, 100, 600, 300);
        Pump();

        var floating = window.FloatingWindowsForTests.Single(w => w.PanelId == DockPanelId.Timeline);
        Assert.Equal(ShortcutScope.In(DockPanelId.Timeline), floating.Scope);

        Assert.Equal(WhatIMeant.InsertedAKey, Verdict(floating, vm));
    }

    /// <summary>
    /// And the general ones, which is the half that made the hole obvious: a
    /// torn-off panel could not even reach the brush.
    /// </summary>
    [AvaloniaFact]
    public void ATornOffPanelStillAnswersTheGeneralShortcuts()
    {
        var (window, vm) = Open();
        vm.Workspace.Float(DockPanelId.Layers, 120, 120, 320, 400);
        Pump();

        var floating = window.FloatingWindowsForTests.Single(w => w.PanelId == DockPanelId.Layers);

        // The Layers docker claims no I, so the general binding answers — the
        // same thing it does when the panel is docked.
        Assert.Equal(WhatIMeant.Eyedropper, Verdict(floating, vm));

        // And a plain tool key, from a tool it would have to move away from.
        vm.ActiveTool = ToolId.Eraser;
        Press(floating, Key.B);
        Assert.Equal(ToolId.Brush, vm.ActiveTool);
    }

    /// <summary>
    /// A panel's own binding goes with it, rather than being answered by
    /// whatever the main window's pointer is resting on. The pointer never
    /// enters the floating window here, which is the case that would fail if the
    /// scope were read from the main window's hover.
    /// </summary>
    [AvaloniaFact]
    public void AKeyInATornOffPanelBelongsToThatPanelWhereverTheMainPointerIs()
    {
        var (window, vm) = Open();
        vm.Workspace.Float(DockPanelId.Timeline, 100, 100, 600, 300);
        Pump();

        // Park the main window's pointer firmly on the canvas.
        Hover(window, window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First());
        Assert.Equal(ShortcutScope.Canvas, window.ShortcutScopeForTests);

        var floating = window.FloatingWindowsForTests.Single(w => w.PanelId == DockPanelId.Timeline);
        Assert.Equal(WhatIMeant.InsertedAKey, Verdict(floating, vm));
    }

    /// <summary>
    /// Docking a panel again hands the keys back to the pointer. The floating
    /// window's handlers must not outlive it — a stale subscription would keep
    /// answering for a panel that is back in the strip.
    /// </summary>
    [AvaloniaFact]
    public void APanelDockedAgainGoesBackToFollowingThePointer()
    {
        var (window, vm) = Open();
        vm.Workspace.Float(DockPanelId.Timeline, 100, 100, 600, 300);
        Pump();
        vm.Workspace.Dock(DockPanelId.Timeline, DockSide.Bottom, 0);
        Pump();

        Assert.Empty(window.FloatingWindowsForTests);

        Hover(window, DockerFor(window, DockPanelId.Timeline));
        Assert.Equal(WhatIMeant.InsertedAKey, Verdict(window, vm));

        Hover(window, window.GetVisualDescendants().OfType<Lightbox.App.Rendering.CanvasControl>().First());
        Assert.Equal(WhatIMeant.Eyedropper, Verdict(window, vm));
    }
}
