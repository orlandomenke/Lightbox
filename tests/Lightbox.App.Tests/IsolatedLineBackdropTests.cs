using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// While a line is open for reshaping, the art under it is held, so letting
/// go of a point redraws the line and what is over it — not the drawing.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was left of B448, measured in the lab on 2026-10-12.</b> A line
/// across a busy drawing: release to screen about 200 ms on the owner-shaped
/// document, the same on main and with B450, because the rectangle a long line
/// can change covers most of the strokes in the drawing and every one of them
/// was replayed. Nothing in the engine can make that small. What can is not
/// replaying them: the strokes under the open line do not change while it is
/// open, so their picture is rendered once and put back under each edit.
/// </para>
/// <para>
/// <b>Pixels first, as everywhere on this path.</b> The held picture is a
/// cache of a state the record already describes; if a patched drawing ever
/// differed from a render of the record by one level, this would be a second
/// source of truth rather than a faster route to the first. Every test here
/// that takes the fast path compares against a whole render, byte for byte.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class IsolatedLineBackdropTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Plain()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BrushHardness = 0.8;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushScatter = 0;
        vm.BrushSizeJitter = 0;
        vm.BrushFlowJitter = 0;
        vm.BrushRotationJitter = 0;
        vm.BrushRoundnessJitter = 0;
        vm.BrushColorJitter = 0;
        vm.BrushGranulation = 0;
        vm.BrushWetEdge = 0;
        return vm;
    }

    private static void Sweep(MainViewModel vm, double y0, double y1, int wobble)
    {
        var w = vm.Doc.Scene.Width;
        vm.BeginStroke(20, y0, 0.6);
        for (var k = 1; k <= 30; k++)
        {
            var t = k / 30.0;
            vm.MoveStroke(20 + t * (w - 40), y0 + (y1 - y0) * t + Math.Sin(t * 9 + wobble) * 12, 0.4 + 0.5 * Math.Sin(t * Math.PI));
        }
        vm.EndStroke();
    }

    /// <summary>A pen line right across the page, through everything.</summary>
    private static string PenLineAcross(MainViewModel vm)
    {
        var w = vm.Doc.Scene.Width;
        var h = vm.Doc.Scene.Height;
        vm.ActiveTool = ToolId.Pen;
        vm.ColorHex = "#2244cc";
        foreach (var (x, y) in new[] { (30.0, h * 0.5), (w * 0.5, h * 0.45), (w - 30.0, h * 0.5) })
        {
            vm.PenPress(x, y, tolerance: 6);
            vm.PenRelease();
        }
        vm.FinishPen();
        return vm.PaintedCel().Strokes[^1].Id;
    }

    /// <summary>
    /// A busy drawing — ten sweeps across the page — a pen line across all of
    /// it, and optionally more strokes over the line.
    /// </summary>
    private static (MainViewModel Vm, string LineId) Busy(bool strokesOver)
    {
        var vm = Plain();
        var h = vm.Doc.Scene.Height;
        vm.ActiveTool = ToolId.Brush;
        vm.BrushSize = 12;
        for (var i = 0; i < 10; i++)
        {
            vm.ColorHex = (i % 3) switch { 0 => "#203040", 1 => "#a03020", _ => "#207040" };
            Sweep(vm, 30 + i * 22, h - 30 - i * 20, i);
        }

        var lineId = PenLineAcross(vm);

        if (strokesOver)
        {
            vm.ActiveTool = ToolId.Brush;
            vm.ColorHex = "#cc2222";
            Sweep(vm, 60, h - 60, 3);
            vm.ActiveTool = ToolId.Eraser;
            Sweep(vm, h - 80, 80, 5);
            vm.ActiveTool = ToolId.Brush;
            vm.ColorHex = "#404040";
            Sweep(vm, h * 0.5, h * 0.52, 1);
        }

        vm.ActiveTool = ToolId.DirectSelect;
        return (vm, lineId);
    }

    private static void Open(MainViewModel vm, string lineId)
    {
        // The drawing has to be in the cache for anything to patch it.
        _ = vm.FrameCache.Get(vm.PaintedCel(), vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        Assert.True(vm.BeginPathEdit(lineId));
        Assert.True(vm.LineBackdropBuild is not null, "opening a line starts holding the art under it");
        Assert.True(vm.LineBackdropBuild!.Wait(TimeSpan.FromSeconds(30)));
    }

    /// <summary>Pull the middle point of the line down by sixty pixels, and commit.</summary>
    private static void Reshape(MainViewModel vm, double dy = 60)
    {
        var node = vm.PathEdit!.Path.Nodes[1];
        var grabbed = vm.GrabPathPart(node.X, node.Y, tolerance: 6);
        Assert.True(grabbed.IsHit);
        vm.DragPathPart(grabbed, node.X, node.Y + dy, 0, dy);
        Assert.True(vm.CommitPathEdit());
    }

    private static SKBitmap Rebuilt(MainViewModel vm) =>
        FrameRasterizer.Materialize(vm.PaintedCel(), vm.Doc.Scene.Width, vm.Doc.Scene.Height);

    private static SKBitmap Cached(MainViewModel vm) =>
        vm.FrameCache.Get(vm.PaintedCel(), vm.Doc.Scene.Width, vm.Doc.Scene.Height).Copy();

    private static long Differing(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        long n = 0;
        for (var i = 0; i < sa.Length; i++) if (sa[i] != sb[i]) n++;
        return n;
    }

    /// <summary>
    /// The case the lab could not move: a line across a busy drawing. With the
    /// art under it held, letting go stamps the line and nothing else.
    /// </summary>
    [AvaloniaFact]
    public void AReshapeRedrawsOnlyTheLineItHolds()
    {
        var (vm, lineId) = Busy(strokesOver: false);

        var before = BrushEngine.DabsStamped;
        using (Rebuilt(vm)) { }
        var forWholeDrawing = BrushEngine.DabsStamped - before;

        Open(vm, lineId);
        var held = vm.LineBackdropRepaints;
        var drops = vm.FrameRenderDrops;
        before = BrushEngine.DabsStamped;
        Reshape(vm);
        var forTheReshape = BrushEngine.DabsStamped - before;

        using var cached = Cached(vm);
        using var rebuilt = Rebuilt(vm);
        var differing = Differing(cached, rebuilt);
        output.WriteLine(
            $"dabs: whole drawing {forWholeDrawing:N0}, the reshape {forTheReshape:N0} "
            + $"({(double)forTheReshape / forWholeDrawing:P0}); held repaints {held} → {vm.LineBackdropRepaints}; "
            + $"drops {drops} → {vm.FrameRenderDrops}; differing {differing}");
        Assert.Equal(held + 1, vm.LineBackdropRepaints);
        Assert.Equal(drops, vm.FrameRenderDrops);
        Assert.Equal(0, differing);
        // The dab count is printed and not asserted here: it is the whole
        // application's, and a commit also redraws the drawing's thumbnail and
        // warms its tiles. What the repaint itself stamps is held where it can
        // be counted alone — RegionRepaintOverHeldArtTests, in the raster suite.
    }

    /// <summary>
    /// With strokes over the line — paint, an eraser, more paint — they are
    /// replayed over it in order, and the result is still the record's.
    /// </summary>
    [AvaloniaFact]
    public void WhatIsOverTheLineIsReplayedOverItExactly()
    {
        var (vm, lineId) = Busy(strokesOver: true);
        Open(vm, lineId);
        var held = vm.LineBackdropRepaints;

        Reshape(vm);
        using (var cached = Cached(vm))
        using (var rebuilt = Rebuilt(vm))
        {
            var differing = Differing(cached, rebuilt);
            output.WriteLine($"first reshape: held repaints {held} → {vm.LineBackdropRepaints}, differing {differing}");
            Assert.Equal(held + 1, vm.LineBackdropRepaints);
            Assert.Equal(0, differing);
        }

        // And again, from where the first one left it: the held art is the
        // same art, because nothing under the line has changed.
        Reshape(vm, dy: -90);
        using (var cached = Cached(vm))
        using (var rebuilt = Rebuilt(vm))
        {
            var differing = Differing(cached, rebuilt);
            output.WriteLine($"second reshape: held repaints → {vm.LineBackdropRepaints}, differing {differing}");
            Assert.Equal(held + 2, vm.LineBackdropRepaints);
            Assert.Equal(0, differing);
        }
    }

    /// <summary>
    /// An edit that is not this line's reshape lets the held art go: it may
    /// have changed what is under the line, and a stale picture put back under
    /// an edit is wrong ink on the page.
    /// </summary>
    [AvaloniaFact]
    public void AnyOtherEditLetsTheHeldArtGo()
    {
        var (vm, lineId) = Busy(strokesOver: false);
        Open(vm, lineId);
        Assert.True(vm.HasLineBackdrop);

        var letGo = vm.LineBackdropsLetGo;

        // Somebody else's edit, to a stroke under the line: recolour the first sweep.
        var frame = vm.PaintedCel();
        var first = frame.Strokes[0];
        var was = first.Color;
        vm.PanelEditor.PerformDelta(
            apply: _ => first.Color = "#ffcc00",
            revert: _ => first.Color = was,
            affectedFrameId: frame.Id);
        Assert.True(vm.LineBackdropsLetGo == letGo + 1, "the held art outlived an edit it could not vouch for");

        // The reshape after it is still right — by the slower road or by art
        // held afresh, whichever is there.
        vm.FrameCache.Invalidate(frame.Id);
        _ = vm.FrameCache.Get(frame, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        vm.LineBackdropBuild?.Wait(TimeSpan.FromSeconds(30));
        Reshape(vm);
        using var cached = Cached(vm);
        using var rebuilt = Rebuilt(vm);
        var differing = Differing(cached, rebuilt);
        output.WriteLine($"after a foreign edit: differing {differing}");
        Assert.Equal(0, differing);
    }

    /// <summary>
    /// A repaint that is not an edit lets the held art go too.
    /// </summary>
    /// <remarks>
    /// The adversary's refutation of the first cut. Switching a palette
    /// variant, dragging a swatch's colour and editing a gradient all change
    /// what the strokes under the line look like <em>without an edit</em> —
    /// the record of a stroke does not change, what its swatch resolves to
    /// does — so the change listener never ran, the held art kept the old
    /// colours, and the next reshape copied them back into the drawing. What
    /// they do all go through is the funnel that drops a drawing's pixels,
    /// and that is where the held art is let go now.
    /// </remarks>
    [AvaloniaFact]
    public void ARepaintThatIsNotAnEditLetsTheHeldArtGoToo()
    {
        var (vm, lineId) = Busy(strokesOver: false);
        Open(vm, lineId);
        var frame = vm.PaintedCel();

        // What a variant switch does to a drawing, by its own entry point: the
        // first sweep now resolves to another colour, and every drawing is
        // told to repaint. No edit is made.
        frame.Strokes[0].Color = "#ffcc00";
        vm.OnVariantSwitched();
        _ = vm.FrameCache.Get(frame, vm.Doc.Scene.Width, vm.Doc.Scene.Height);

        Reshape(vm);

        using var cached = Cached(vm);
        using var rebuilt = Rebuilt(vm);
        var differing = Differing(cached, rebuilt);
        output.WriteLine($"after a variant switch: differing {differing}");
        Assert.Equal(0, differing);
    }

    /// <summary>The held art is the session's: leaving the line lets it go.</summary>
    [AvaloniaFact]
    public void LeavingTheLineLetsTheHeldArtGo()
    {
        var (vm, lineId) = Busy(strokesOver: false);
        Open(vm, lineId);
        Assert.True(vm.HasLineBackdrop);

        vm.EndPathEdit();
        Assert.False(vm.HasLineBackdrop);
        Assert.Null(vm.LineBackdropBuild);
    }

    /// <summary>
    /// A reshape that arrives before the art is ready takes the road it always
    /// took, and is right.
    /// </summary>
    [AvaloniaFact]
    public void AReshapeBeforeTheArtIsReadyIsStillRight()
    {
        var (vm, lineId) = Busy(strokesOver: true);
        _ = vm.FrameCache.Get(vm.PaintedCel(), vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        Assert.True(vm.BeginPathEdit(lineId));
        vm.ForgetLineBackdrop();
        var held = vm.LineBackdropRepaints;

        Reshape(vm);

        using var cached = Cached(vm);
        using var rebuilt = Rebuilt(vm);
        var differing = Differing(cached, rebuilt);
        output.WriteLine($"no held art: held repaints {held} → {vm.LineBackdropRepaints}, differing {differing}");
        Assert.Equal(held, vm.LineBackdropRepaints);
        Assert.Equal(0, differing);
    }
}
