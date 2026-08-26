using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// B320: moving ink must not un-rub what an eraser took off it.
/// </summary>
/// <remarks>
/// <para>
/// Reported as <em>"moving ink reveals erased lines"</em>. An erasure is a
/// record entry with its own geometry, and the region filter used to judge the
/// two halves of one rub by different rules — ink by its <b>surviving</b>
/// points, erasures by their <b>raw</b> ones — so the ink and the eraser that
/// carved it could disagree about whether they were selected. When the ink
/// went and the eraser stayed, the ink arrived at its destination un-carved
/// and the paint came back.
/// </para>
/// <para>
/// Presence/absence measurements, not magnitudes, so the saturation trap does
/// not apply — but both numbers are printed and both directions asserted
/// anyway, because "the gap is empty" also passes on a build that drew no ink
/// at all.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TransformCarriesTheCarvingTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// A hard line across the middle with a bite taken out of it at x=300.
    /// </summary>
    private static MainViewModel Carved()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 24;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.BeginStroke(100, 200, 1);
        vm.MoveStroke(300, 200, 1);
        vm.MoveStroke(500, 200, 1);
        vm.EndStroke();

        vm.ActiveTool = ToolId.Eraser;
        vm.BrushSize = 40;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BeginStroke(300, 150, 1);
        vm.MoveStroke(300, 250, 1);
        vm.EndStroke();
        return vm;
    }

    private static Frame ActiveFrame(MainViewModel vm) =>
        (Frame)vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!;

    private static List<StrokePoint> Box(double x0, double y0, double x1, double y1) =>
        [new(x0, y0, 1), new(x1, y0, 1), new(x1, y1, 1), new(x0, y1, 1)];

    private static byte AlphaAt(MainViewModel vm, int x, int y)
    {
        using var bmp = FrameRasterizer.Rasterize(
            ActiveFrame(vm).Strokes, vm.Doc.Scene.Width, vm.Doc.Scene.Height);
        return bmp.GetPixel(x, y).Alpha;
    }

    /// <summary>The drawing starts out carved — otherwise nothing below means anything.</summary>
    [AvaloniaFact]
    public void TheBiteIsThereToBeginWith()
    {
        var vm = Carved();
        var gap = AlphaAt(vm, 300, 200);
        var ink = AlphaAt(vm, 150, 200);
        output.WriteLine($"gap {gap}, ink {ink}");
        Assert.Equal(0, gap);
        Assert.True(ink > 200, $"the line itself is missing (alpha {ink})");
    }

    /// <summary>
    /// Select the carved stretch and move it: the bite goes with it.
    /// </summary>
    /// <remarks>
    /// The eraser sits wholly inside this marquee and the ink crosses it, so
    /// the two are caught by different arms of the same rule — which is
    /// exactly the arrangement that used to let them come apart.
    /// </remarks>
    [AvaloniaFact]
    public void TheCarvingTravelsWithTheInkItCarved()
    {
        var vm = Carved();
        vm.ApplySelectionShape(Box(50, 120, 400, 280), false, false);
        Assert.True(vm.BeginTransform(), $"refused with: {vm.AiStatus}");

        vm.CommitTransformAffine(225, 200, 1, 1, 0, 0, 200); // straight down 200

        var movedGap = AlphaAt(vm, 300, 400);   // where the bite landed
        var movedInk = AlphaAt(vm, 150, 400);   // moved ink away from the bite
        output.WriteLine($"moved: gap {movedGap}, ink {movedInk}");

        Assert.True(movedInk > 200, $"the ink did not arrive (alpha {movedInk})");
        Assert.Equal(0, movedGap);
    }

    /// <summary>
    /// A band that catches the ink and misses both ends of the eraser.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B320 in its purest form.</b> The marquee is a horizontal band across
    /// the middle: every one of the line's recorded points sits in it, and
    /// <em>neither</em> end of the vertical eraser does — its two points are
    /// above and below the band, and only the stretch between them passes
    /// through. Judging strokes by their vertices alone, the ink was selected
    /// and the eraser was not, so the ink travelled and its carving stayed
    /// behind: the paint the artist had rubbed off arrived at the destination
    /// intact.
    /// </para>
    /// <para>
    /// The fix has two halves and this needs both. Reading the mark rather
    /// than the vertices is what catches the eraser at all; splitting a
    /// crossing stroke is what lets the caught part of it travel while the
    /// rest stays holding down what it erased where it was.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ABandAcrossTheMiddleTakesTheCarvingWithIt()
    {
        var vm = Carved();
        vm.ApplySelectionShape(Box(50, 180, 550, 220), false, false);
        Assert.True(vm.BeginTransform(), $"refused with: {vm.AiStatus}");

        vm.CommitTransformAffine(300, 200, 1, 1, 0, 0, 200); // straight down 200

        var movedGap = AlphaAt(vm, 300, 400);   // the bite, at its new home
        var movedInk = AlphaAt(vm, 150, 400);   // moved ink either side of it
        output.WriteLine($"band moved: gap {movedGap}, ink {movedInk}");

        Assert.True(movedInk > 200, $"the ink did not arrive (alpha {movedInk})");
        Assert.Equal(0, movedGap);
    }

    /// <summary>
    /// And the part left behind keeps its own bite.
    /// </summary>
    /// <remarks>
    /// The other half of the same promise, and the one the old stay-copy
    /// machinery existed to keep. It is not special-cased any more: a crossing
    /// erasure is split like anything else, so the part outside the marquee is
    /// still sitting exactly where it was, on the ink it rubbed out there.
    /// </remarks>
    [AvaloniaFact]
    public void TheInkLeftBehindKeepsItsOwnEdge()
    {
        var vm = Carved();
        // A marquee that cuts the eraser as well as the ink: its left half
        // travels, its right half stays.
        vm.ApplySelectionShape(Box(50, 120, 300, 280), false, false);
        Assert.True(vm.BeginTransform(), $"refused with: {vm.AiStatus}");

        vm.CommitTransformAffine(175, 200, 1, 1, 0, 0, 200);

        var stayedInk = AlphaAt(vm, 450, 200);  // right of the marquee, untouched
        var movedInk = AlphaAt(vm, 150, 400);   // left of it, moved down
        var vacated = AlphaAt(vm, 150, 200);    // where the moved ink came from
        output.WriteLine($"stayed {stayedInk}, moved {movedInk}, vacated {vacated}");

        Assert.True(stayedInk > 200, $"ink outside the marquee was disturbed (alpha {stayedInk})");
        Assert.True(movedInk > 200, $"the selected ink did not arrive (alpha {movedInk})");
        Assert.Equal(0, vacated);
    }
}
