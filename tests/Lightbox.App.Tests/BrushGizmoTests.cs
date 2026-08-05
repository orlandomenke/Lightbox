using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The ring on the canvas, and whether it is telling the truth about the brush.
/// </summary>
/// <remarks>
/// <para>
/// B72 and B74, which are the same promise from two directions: the gizmo answers
/// "how big is my brush and what shape is it", and it was wrong about the size
/// until the pointer moved and wrong about the shape always.
/// </para>
/// <para>
/// <b>What these tests can and cannot reach, stated once so nobody mistakes the
/// coverage.</b> The ring is drawn inside the canvas's render op, on the render
/// thread — it is not in the published <c>RenderSnapshot</c>, so
/// <c>LivePreviewPixelTests</c>' approach cannot see it. The suite runs on
/// Avalonia's headless <em>software</em> drawing, where a rendered frame cannot be
/// captured at all, and switching that on for 1370 App tests to reach one overlay
/// would be a large risk bought for a small assertion.
/// </para>
/// <para>
/// So the fix is guarded where each half is exact: the <b>shape</b> in
/// <c>Lightbox.Raster.Tests.BrushTipOutlineTests</c>, which traces real tips and
/// checks the silhouette and its aspect; and the <b>wiring</b> here — that the
/// values come from the brush the engine stamps from, that a change to them
/// repaints, and that the XAML actually binds them. What is left unasserted is
/// Avalonia's own <c>AffectsRender</c> implementation, and that the ring looks
/// right, which is a <c>MANUAL_TESTING.md</c> check.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BrushGizmoTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Ready()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false };
        Dispatcher.UIThread.RunJobs();
        return vm;
    }

    private static string MainWindowXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(
            Path.Combine(dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));
    }

    /// <summary>
    /// Changing the brush size changes the ring, and does so without waiting for a
    /// pointer move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B72.</b> <c>BrushCursorSize</c> is read inside <c>Render</c> and was a
    /// plain <c>StyledProperty</c> with no <c>AffectsRender</c>, so nothing
    /// invalidated when it changed: the ring kept its old size until the pointer
    /// moved and invalidated for its own reasons. The view model was right and the
    /// binding was right — the canvas was simply never asked to draw again.
    /// </para>
    /// <para>
    /// Two halves, because either alone passes on the bug. The value has to change
    /// and announce itself (or the binding never delivers a new one), <b>and</b> the
    /// property has to be registered for repaint (or the new value is never drawn).
    /// The registration is asserted through <c>CanvasControl.RepaintOnChange</c>,
    /// which is the list handed to <c>AffectsRender</c> — so this guards the thing
    /// that was actually missing, not Avalonia's plumbing beneath it.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TheGizmoFollowsTheBrushSizeWithoutAPointerMove()
    {
        Assert.Contains(CanvasControl.BrushCursorSizeProperty, CanvasControl.RepaintOnChange);

        var vm = Ready();
        vm.BrushSize = 12;
        Dispatcher.UIThread.RunJobs();
        var small = vm.BrushCursorDiameter;

        var announced = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.BrushCursorDiameter)) announced++;
        };

        vm.BrushSize = 96;
        Dispatcher.UIThread.RunJobs();
        var large = vm.BrushCursorDiameter;

        output.WriteLine($"12 px brush → ring {small:F1}; 96 px brush → ring {large:F1}; {announced} notifications");
        Assert.True(large > small * 4, $"the ring reported {large:F1} for a 96 px brush against {small:F1} for 12 px");
        Assert.True(announced > 0, "changing the brush size never announced a new ring diameter, so no binding fires");
    }

    /// <summary>
    /// The ring outlines the tip rather than drawing a circle over it.
    /// </summary>
    /// <remarks>
    /// <b>B74.</b> The cursor drew two concentric circles whatever the brush was —
    /// its own comment said "today the brush footprint is always a circle", which
    /// had not been true for a long time. A tip id is passed rather than a shape, so
    /// the outline is traced from the same bitmap <c>BrushEngine</c> stamps and the
    /// preview cannot drift from the mark; <c>BrushTipOutlineTests</c> is where the
    /// silhouette itself is checked.
    /// </remarks>
    [AvaloniaFact]
    public void TheGizmoOutlinesTheTipRatherThanACircle()
    {
        Assert.Contains(CanvasControl.BrushCursorTipIdProperty, CanvasControl.RepaintOnChange);
        Assert.Contains(CanvasControl.BrushCursorRoundnessProperty, CanvasControl.RepaintOnChange);
        Assert.Contains(CanvasControl.BrushCursorAngleProperty, CanvasControl.RepaintOnChange);

        var vm = Ready();

        // No tip is null rather than a stand-in: the canvas draws the ellipse the
        // round dab genuinely is, which a substitute circle would have hidden.
        vm.CurrentToolSettingsForTest.TipId = null;
        vm.CurrentToolSettingsForTest.Roundness = 1;
        vm.CurrentToolSettingsForTest.TipRotationDeg = 0;
        Assert.Null(vm.BrushCursorTipId);
        Assert.Equal(1, vm.BrushCursorRoundness);
        Assert.Equal(0, vm.BrushCursorAngle);

        // And it reads the brush the engine stamps from, so a chisel is previewed
        // as a chisel at its own angle and flatness.
        vm.CurrentToolSettingsForTest.TipId = "tip_chisel";
        vm.CurrentToolSettingsForTest.Roundness = 0.25;
        vm.CurrentToolSettingsForTest.TipRotationDeg = 35;
        Assert.Equal("tip_chisel", vm.BrushCursorTipId);
        Assert.Equal(0.25, vm.BrushCursorRoundness);
        Assert.Equal(35, vm.BrushCursorAngle);

        output.WriteLine($"tip {vm.BrushCursorTipId}, roundness {vm.BrushCursorRoundness}, angle {vm.BrushCursorAngle}");

        // Roundness is clamped away from zero, because a flatness of nothing is a
        // ring of nothing and the artist would read it as the cursor being gone.
        vm.CurrentToolSettingsForTest.Roundness = 0;
        Assert.True(vm.BrushCursorRoundness > 0, "a roundness of zero collapsed the ring to invisible");
    }

    /// <summary>
    /// The eraser gets its own ring, because it has its own settings.
    /// </summary>
    /// <remarks>
    /// <c>CurrentToolSettings</c> switches between the brush's and the eraser's
    /// records, so every one of these properties has to follow it. A gizmo that kept
    /// showing the brush's tip while the eraser was selected would be the same class
    /// of lie in a place nobody would look for it.
    /// </remarks>
    [AvaloniaFact]
    public void TheEraserGetsItsOwnRing()
    {
        var vm = Ready();
        vm.IsEraser = false;
        vm.CurrentToolSettingsForTest.TipId = "tip_brush";
        vm.IsEraser = true;
        vm.CurrentToolSettingsForTest.TipId = "tip_eraser";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("tip_eraser", vm.BrushCursorTipId);
        vm.IsEraser = false;
        Assert.Equal("tip_brush", vm.BrushCursorTipId);
    }

    /// <summary>
    /// The canvas is actually bound to all four, which no unit test can see.
    /// </summary>
    /// <remarks>
    /// The same technique <c>ShortcutRegistrationTests</c> uses on gestures, and for
    /// the same reason: a property with a correct value, a correct default and no
    /// binding behaves exactly like the bug. B72 was itself one link of this kind
    /// being absent.
    /// </remarks>
    [AvaloniaFact]
    public void TheCanvasIsBoundToEveryPartOfTheRing()
    {
        var xaml = MainWindowXaml();
        foreach (var binding in new[]
        {
            "BrushCursorSize=\"{Binding BrushCursorDiameter}\"",
            "BrushCursorTipId=\"{Binding BrushCursorTipId}\"",
            "BrushCursorRoundness=\"{Binding BrushCursorRoundness}\"",
            "BrushCursorAngle=\"{Binding BrushCursorAngle}\"",
        })
        {
            Assert.Contains(binding, xaml);
        }
    }

    /// <summary>
    /// A brush change re-reads every part of the ring, not only its size.
    /// </summary>
    /// <remarks>
    /// The shape properties are computed from <c>CurrentToolSettings</c>, so they
    /// need to be in the list a brush change announces. Leaving them out is the
    /// registry-shaped miss <c>CLAUDE.md</c> keeps warning about: the values would be
    /// right whenever anything else happened to refresh them, and stale the rest of
    /// the time.
    /// </remarks>
    [AvaloniaFact]
    public void ABrushChangeAnnouncesTheRingsShapeAndNotOnlyItsSize()
    {
        var vm = Ready();
        var seen = new HashSet<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name) seen.Add(name);
        };

        // Switching the eraser on runs the same wholesale re-announcement that
        // loading a preset does, and is reachable from outside the view model.
        vm.IsEraser = true;
        Dispatcher.UIThread.RunJobs();

        output.WriteLine($"{seen.Count} properties announced");
        Assert.Contains(nameof(vm.BrushCursorDiameter), seen);
        Assert.Contains(nameof(vm.BrushCursorTipId), seen);
        Assert.Contains(nameof(vm.BrushCursorRoundness), seen);
        Assert.Contains(nameof(vm.BrushCursorAngle), seen);
    }
}
