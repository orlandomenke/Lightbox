using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B343 — a fill tucked under the line work patches the drawing it changed
/// instead of throwing the whole drawing away.
/// </summary>
/// <remarks>
/// <para>
/// Filling <em>above</em> the lines stamps one stroke onto the cached bitmap
/// and costs nothing. Filling <em>below</em> them — which is the default, and
/// what an artist doing flat colour under ink does every time — dropped the
/// frame's cached render, so the next publish re-rasterized every stroke on the
/// drawing: <b>61.8 ms against 5.6</b> for the same fill placed the other way
/// up, measured at 1920×1080.
/// </para>
/// <para>
/// <b>The tempting shortcut is wrong and this is why.</b> A fill below the
/// lines cannot simply be drawn <em>under</em> the cached bitmap, because that
/// bitmap is already flattened: an earlier fill or gradient on the same drawing
/// would end up on top of the new one, which is the opposite of what the record
/// says. <c>UnderLineWorkIndex</c> puts the new fill after the last stroke that
/// would swallow it, not at the bottom, and only a replay in record order
/// honours that.
/// </para>
/// <para>
/// So it uses B327's region patch, built for undo and already carrying the
/// argument: clear a rectangle, replay the strokes that reach it in record
/// order. The one thing that had to change is <b>when</b> — it used to run
/// beside the append, before the stroke was in the record, where a region
/// repaint would have rebuilt the rectangle from a drawing with no fill in it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class FillBelowRepaintsItsRegionTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A closed box of hard black line work on a bare layer.</summary>
    private static MainViewModel Boxed()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 10;
        vm.BrushHardness = 1;
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
        Line(vm, 100, 100, 500, 100);
        Line(vm, 500, 100, 500, 400);
        Line(vm, 500, 400, 100, 400);
        Line(vm, 100, 400, 100, 100);
        vm.ActiveTool = ToolId.Fill;
        return vm;
    }

    private static void Line(MainViewModel vm, double x0, double y0, double x1, double y1)
    {
        vm.BeginStroke(x0, y0, 1);
        vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
        vm.MoveStroke(x1, y1, 1);
        vm.EndStroke();
    }

    /// <summary>What the canvas shows.</summary>
    private static SKBitmap Shown(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        return SKBitmap.FromImage(latest!.Image);
    }

    /// <summary>
    /// The drawing replayed from the record, start to finish — the answer a
    /// dropped cache would have rebuilt, and therefore the thing the patch has
    /// to agree with.
    /// </summary>
    private static SKBitmap Rebuilt(MainViewModel vm) =>
        FrameRasterizer.Materialize(vm.PaintedCel(), vm.Doc.Scene.Width, vm.Doc.Scene.Height);

    private static (long Differing, int Worst, long Inked) Compare(SKBitmap a, SKBitmap b)
    {
        long differing = 0, inked = 0;
        var worst = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                if (p.Alpha > 8 || q.Alpha > 8) inked++;
                var d = Math.Max(
                    Math.Abs(p.Alpha - q.Alpha),
                    Math.Max(
                        Math.Abs(p.Red - q.Red),
                        Math.Max(Math.Abs(p.Green - q.Green), Math.Abs(p.Blue - q.Blue))));
                if (d > 0) differing++;
                if (d > worst) worst = d;
            }
        }
        return (differing, worst, inked);
    }

    // ---- correctness ---------------------------------------------------------

    /// <summary>
    /// The patched drawing is the drawing, pixel for pixel.
    /// </summary>
    /// <remarks>
    /// Bit-identity rather than a tolerance, and it is available here for the
    /// reason B327 gives: <c>Hash01</c> seeds every dab dynamic from the
    /// IEEE-754 bits of its position, so a stroke replayed under a clip lands
    /// the same dabs it landed the first time. A patch that is merely close
    /// would mean the clip had reached the geometry.
    /// </remarks>
    [AvaloniaFact]
    public void APatchedFillIsWhatAFullRebuildWouldHaveDrawn()
    {
        var vm = Boxed();
        Assert.True(vm.FillBelowLines, "the default is what this is about");
        vm.ColorHex = "#cc2222";
        vm.FillAt(300, 250);

        using var shown = Shown(vm);
        using var rebuilt = Rebuilt(vm);
        var (differing, worst, inked) = Compare(shown, rebuilt);
        output.WriteLine($"inked {inked}, differing {differing}, worst {worst}");
        Assert.Equal(0, differing);
    }

    /// <summary>
    /// The lines stay on top of the colour — the whole point of filling below.
    /// </summary>
    [AvaloniaFact]
    public void TheLineWorkStaysOverTheFill()
    {
        var vm = Boxed();
        vm.ColorHex = "#cc2222";
        vm.FillAt(300, 250);

        using var shown = Shown(vm);
        var onTheLine = shown.GetPixel(300, 100);
        var inside = shown.GetPixel(300, 250);
        output.WriteLine($"on the line {onTheLine}, inside {inside}");
        Assert.True(onTheLine.Red < 60 && onTheLine.Green < 60, "the fill covered the line");
        Assert.True(inside.Red > 150 && inside.Green < 100, "the inside is not the fill colour");
    }

    /// <summary>
    /// A second fill does not slide underneath the first one.
    /// </summary>
    /// <remarks>
    /// <b>The case that rules out drawing the fill under the cached bitmap.</b>
    /// Both fills are "below the line work", but the second must sit on top of
    /// the first — <c>UnderLineWorkIndex</c> goes under the lines and no
    /// further back than the last stroke that would swallow it. A patch that
    /// replays in record order gets this for nothing; the shortcut would put the
    /// older colour in front and read to the artist as "the fill did nothing".
    /// </remarks>
    [AvaloniaFact]
    public void ASecondFillSitsOnTopOfTheFirst()
    {
        var vm = Boxed();
        vm.ColorHex = "#2222cc";
        vm.FillAt(300, 250);
        vm.ColorHex = "#cc2222";
        vm.FillAt(300, 250);

        using var shown = Shown(vm);
        using var rebuilt = Rebuilt(vm);
        var inside = shown.GetPixel(300, 250);
        output.WriteLine($"inside after two fills {inside}");
        Assert.Equal(0, Compare(shown, rebuilt).Differing);
        Assert.True(inside.Red > inside.Blue, "the older fill came out in front of the newer one");
    }

    /// <summary>Undo still takes the fill back off.</summary>
    [AvaloniaFact]
    public void UndoStillPutsTheDrawingBack()
    {
        var vm = Boxed();
        using var before = Shown(vm);
        vm.ColorHex = "#cc2222";
        vm.FillAt(300, 250);
        vm.UndoCommand.Execute(null);

        using var after = Shown(vm);
        var (differing, worst, _) = Compare(before, after);
        output.WriteLine($"after undo: differing {differing}, worst {worst}");
        Assert.Equal(0, differing);
    }

    // ---- and it took the patch, not the drop --------------------------------

    /// <summary>
    /// Which path ran, asked of the counters rather than of a clock.
    /// </summary>
    /// <remarks>
    /// The form B327's counters were added for, and the right one here: what
    /// this change is about is <em>whether the drawing was thrown away</em>, and
    /// a millisecond assertion measures the machine it ran on. A fill below the
    /// line work must resolve to one region repaint and no drops at all.
    /// </remarks>
    [AvaloniaFact]
    public void FillingUnderTheLineWorkPatchesInsteadOfDroppingTheDrawing()
    {
        var vm = Boxed();
        _ = Shown(vm);
        var repaintsBefore = vm.FrameRegionRepaints;
        var dropsBefore = vm.FrameRenderDrops;

        vm.ColorHex = "#cc2222";
        vm.FillAt(300, 250);

        output.WriteLine(
            $"repaints {repaintsBefore} → {vm.FrameRegionRepaints}, "
            + $"drops {dropsBefore} → {vm.FrameRenderDrops}");
        Assert.Equal(repaintsBefore + 1, vm.FrameRegionRepaints);
        Assert.Equal(dropsBefore, vm.FrameRenderDrops);
    }

    /// <summary>
    /// A drawing the patch cannot rebuild is still dropped, not patched badly.
    /// </summary>
    /// <remarks>
    /// <c>FrameRasterizer.CanRepaintRegion</c> refuses a frame carrying imported
    /// pixels, because clearing a rectangle would destroy part of them and only
    /// a crop would put them back. The refusal is the safe direction — a
    /// re-render costs time, a wrong patch leaves ink the document does not
    /// describe — and this is here so that a change making the patch more
    /// eager has to come past it.
    /// </remarks>
    [AvaloniaFact]
    public void ADrawingWithImportedPixelsFallsBackToTheRebuild()
    {
        var vm = Boxed();
        _ = Shown(vm);
        // A baseline: pixels under the strokes that no replay can reconstruct.
        using (var pixels = new SKBitmap(vm.Doc.Scene.Width, vm.Doc.Scene.Height))
        {
            pixels.Erase(new SKColor(0x22, 0x88, 0x22, 0xFF));
            vm.PaintedCel().PngBase64 = PngCodec.Encode(pixels);
        }
        Assert.False(FrameRasterizer.CanRepaintRegion(vm.PaintedCel()));
        var dropsBefore = vm.FrameRenderDrops;

        vm.ColorHex = "#cc2222";
        vm.FillAt(300, 250);

        output.WriteLine($"drops {dropsBefore} → {vm.FrameRenderDrops}");
        Assert.Equal(dropsBefore + 1, vm.FrameRenderDrops);
    }
}
