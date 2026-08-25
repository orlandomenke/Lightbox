using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// A marquee lands where it was drawn, on a page whose origin is not zero.
/// </summary>
/// <remarks>
/// <para>
/// <b>The selection outline is a surface mask</b> — a <c>w × h</c> array of
/// booleans and its traced contours — which is what
/// <c>DocumentOriginInViewTests.ASelectionBecomesAClipRegionInStrokeCoordinates</c>
/// pins, and why <c>SelectAll</c> correctly writes <c>(0,0)…(W,H)</c>. The one
/// boundary it crosses is into the record, where <c>PrepareClipForSelection</c>
/// adds the origin back.
/// </para>
/// <para>
/// <b>The canvas reports a drawn marquee in <em>document</em> coordinates</b>,
/// though — <c>_dragShape</c> is built from <c>ViewToDoc</c>, and
/// <c>AddPolygonVertex</c> is handed the same. Those went straight into a
/// surface-indexed rasterizer with no conversion, so on a page that had been
/// cropped or grown leftward every hand-drawn selection landed one origin away
/// from the hand that drew it. Select All was unaffected, which is what made
/// this survive: the command everybody tests with is the one that was right.
/// </para>
/// <para>
/// Every assertion here passes either way at origin zero. That is the whole
/// reason it went unnoticed, and the reason each test pays for a crop first.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SelectionOriginTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A view model whose page has been cropped to (100, 60) 400 × 300.</summary>
    private static MainViewModel MovedPage()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        Assert.True(CanvasResize.CropTo(vm.Doc.Scene, 100, 60, 400, 300));
        vm.RefreshDocumentOrigin();
        return vm;
    }

    /// <summary>A rectangular marquee, in the document coordinates the canvas reports.</summary>
    private static void Marquee(MainViewModel vm, double x, double y, double w, double h) =>
        vm.ApplySelectionShape(
        [
            new StrokePoint(x, y, 1), new StrokePoint(x + w, y, 1),
            new StrokePoint(x + w, y + h, 1), new StrokePoint(x, y + h, 1),
        ], add: false, subtract: false);

    private static (double L, double T, double R, double B) SurfaceBounds(MainViewModel vm)
    {
        var pts = vm.SelectionContours.SelectMany(c => c).ToList();
        return (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
    }

    /// <summary>
    /// A marquee drawn at document (200, 150) sits at surface (100, 90) on a
    /// page that starts at (100, 60).
    /// </summary>
    [AvaloniaFact]
    public void ADrawnMarqueeIsStoredInSurfaceCoordinates()
    {
        var vm = MovedPage();

        Marquee(vm, 200, 150, 200, 150);

        var b = SurfaceBounds(vm);
        output.WriteLine($"drawn at document (200, 150); stored at surface ({b.L:F0}, {b.T:F0}) → ({b.R:F0}, {b.B:F0})");
        Assert.True(vm.HasSelection);
        // Traced off a mask, so the outline runs down the middle of the
        // boundary ring — a pixel of slack each way, but not one origin.
        Assert.True(Math.Abs(b.L - 100) <= 2, $"left is {b.L}, expected about 100 (200 − origin 100)");
        Assert.True(Math.Abs(b.T - 90) <= 2, $"top is {b.T}, expected about 90 (150 − origin 60)");
    }

    /// <summary>
    /// The round trip: a marquee drawn at a document rectangle is recorded as a
    /// clip at that same document rectangle.
    /// </summary>
    /// <remarks>
    /// The end-to-end version, and the one an artist would report. The clip is
    /// what actually stops paint, so a marquee stored one origin out paints
    /// through a hole nowhere near the marching ants.
    /// </remarks>
    [AvaloniaFact]
    public void AStrokeIsClippedWhereTheMarqueeWasDrawn()
    {
        var vm = MovedPage();
        Marquee(vm, 200, 150, 200, 150);

        vm.BeginStroke(250, 200, 0.8);
        vm.MoveStroke(350, 250, 0.8);
        vm.EndStroke();

        var stroke = vm.PaintStrokes().LastOrDefault(s => s.ClipId is not null);
        Assert.NotNull(stroke);
        var region = ClipRegionRegistry.Resolve(stroke!.ClipId!);
        Assert.NotNull(region);

        var xs = region!.Contours.SelectMany(c => c).Select(p => p.X).ToList();
        var ys = region.Contours.SelectMany(c => c).Select(p => p.Y).ToList();
        output.WriteLine($"marquee drawn at x 200–400, y 150–300; "
            + $"clip recorded at x {xs.Min():F0}–{xs.Max():F0}, y {ys.Min():F0}–{ys.Max():F0}");
        Assert.True(Math.Abs(xs.Min() - 200) <= 2, $"clip starts at x {xs.Min()}, marquee at 200");
        Assert.True(Math.Abs(ys.Min() - 150) <= 2, $"clip starts at y {ys.Min()}, marquee at 150");
    }

    /// <summary>A polygon selection lands where its vertices were clicked.</summary>
    /// <remarks>
    /// The same entry point one gesture along: <c>AddPolygonVertex</c> is handed
    /// document coordinates too, and its points are also what the in-progress
    /// overlay draws — so a conversion in only one of the two places would fix
    /// the selection and leave the rubber band lying.
    /// </remarks>
    [AvaloniaFact]
    public void APolygonSelectionLandsWhereItsVerticesWereClicked()
    {
        var vm = MovedPage();

        vm.AddPolygonVertex(200, 150);
        vm.AddPolygonVertex(400, 150);
        vm.AddPolygonVertex(400, 300);
        vm.CompletePolygon(add: false, subtract: false);

        var b = SurfaceBounds(vm);
        output.WriteLine($"clicked at document x 200–400; stored at surface x {b.L:F0}–{b.R:F0}");
        Assert.True(vm.HasSelection);
        Assert.True(Math.Abs(b.L - 100) <= 2, $"left is {b.L}, expected about 100 (200 − origin 100)");
    }

    /// <summary>
    /// Select All is unaffected, and stays that way.
    /// </summary>
    /// <remarks>
    /// It writes the surface rectangle directly and always has, which is
    /// correct. Pinned here beside the fix so a later tidy-up that "makes the
    /// two consistent" by moving Select All into document space cannot pass.
    /// </remarks>
    [AvaloniaFact]
    public void SelectAllStillCoversTheSurfaceFromItsOwnCorner()
    {
        var vm = MovedPage();

        vm.SelectAllCommand.Execute(null);

        var b = SurfaceBounds(vm);
        output.WriteLine($"select all → surface ({b.L:F0}, {b.T:F0}) → ({b.R:F0}, {b.B:F0})");
        Assert.Equal(0, b.L);
        Assert.Equal(0, b.T);
        Assert.Equal(400, b.R);
        Assert.Equal(300, b.B);
    }
}
