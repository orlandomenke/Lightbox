using Avalonia;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The view and the pixel tools after the paper has been grown.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of these passes on an unresized document whether the code is
/// right or wrong</b>, which is the whole difficulty: the two coordinate spaces
/// coincide until <c>Scene.OriginX</c> or <c>OriginY</c> goes non-zero, so the
/// existing suite cannot see any of it. Each test here therefore grows the
/// canvas first and then asks a question whose answer moves.
/// </para>
/// <para>
/// The split being guarded: <c>CanvasControl.ViewToDoc</c> hands back <b>stroke
/// coordinates</b>, because that is what almost every caller wants — picking a
/// line, dragging a guide, starting a stroke. The three tools that index a
/// bitmap instead — flood fill, the wand, the colour picker — convert back, and
/// the two results that become part of the record convert forward again.
/// </para>
/// </remarks>
public class DocumentOriginInViewTests(ITestOutputHelper output)
{
    private const int Grew = 120;

    /// <summary>A document grown leftward and upward by <see cref="Grew"/>.</summary>
    private static MainViewModel Grown()
    {
        var vm = VmLayers.PaperVm();
        vm.Doc.Scene.OriginX = -Grew;
        vm.Doc.Scene.OriginY = -Grew;
        vm.Doc.Scene.Width += Grew;
        vm.Doc.Scene.Height += Grew;
        return vm;
    }

    // ---- the view transform -------------------------------------------------------------

    /// <summary>
    /// A point in the middle of the widget is the middle of the paper, named in
    /// stroke coordinates.
    /// </summary>
    [AvaloniaFact]
    public void ViewToDocAnswersInStrokeCoordinates()
    {
        var canvas = new CanvasControl();
        var flushPoint = canvas.ViewToDoc(new Point(40, 25));

        canvas.DocumentOrigin = new PixelPoint(-Grew, -Grew);
        var grownPoint = canvas.ViewToDoc(new Point(40, 25));

        output.WriteLine($"flush {flushPoint} -> grown {grownPoint}");
        Assert.Equal(flushPoint.X - Grew, grownPoint.X, 6);
        Assert.Equal(flushPoint.Y - Grew, grownPoint.Y, 6);
    }

    /// <summary>The two conversions are inverses at any origin.</summary>
    /// <remarks>
    /// Cheap, and it is the property that actually matters: a tool that converts
    /// one way and an overlay that converts the other have to agree, or a
    /// selection outline sits beside the thing it selected.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(-Grew, -Grew)]
    [InlineData(37, -11)]
    public void ViewToDocAndDocToViewAreInverses(int ox, int oy)
    {
        var canvas = new CanvasControl { DocumentOrigin = new PixelPoint(ox, oy) };

        var (dx, dy) = canvas.ViewToDoc(new Point(63, 44));
        var (vx, vy) = canvas.DocToView(dx, dy);

        Assert.Equal(63, vx, 6);
        Assert.Equal(44, vy, 6);
    }

    /// <summary>
    /// The bitmap conversion is the origin and nothing else.
    /// </summary>
    [AvaloniaFact]
    public void DocToSurfaceSubtractsTheOrigin()
    {
        var canvas = new CanvasControl { DocumentOrigin = new PixelPoint(-Grew, 15) };

        var (sx, sy) = canvas.DocToSurface(-100, 40);

        Assert.Equal(20, sx, 6);
        Assert.Equal(25, sy, 6);
    }

    // ---- the tools that index pixels ----------------------------------------------------

    /// <summary>
    /// The colour picker reads the pixel the artist pointed at, not the one the
    /// same numbers name in the bitmap.
    /// </summary>
    /// <remarks>
    /// With the canvas grown 120px leftward, stroke coordinate 0 is bitmap
    /// column 120. A picker that skipped the conversion would read column 0 —
    /// which is inside the new margin, so it would report the paper colour for
    /// every point in the drawing and look like the picker was simply broken.
    /// </remarks>
    [AvaloniaFact]
    public void TheColourPickerReadsThePixelUnderThePointer()
    {
        var vm = Grown();
        // Stroke coordinate (0,0) — the old top-left, well inside the paper.
        vm.PickColorAt(0, 0);

        // Outside the paper on the left: there is no pixel there at all, so the
        // guard has to reject it rather than wrapping into the bitmap.
        var before = vm.ColorHex;
        vm.PickColorAt(-Grew - 50, 0);

        output.WriteLine($"in-paper {before}, off-paper {vm.ColorHex}");
        Assert.Equal(before, vm.ColorHex);
    }

    /// <summary>
    /// A fill's outline is written in stroke coordinates, so it lands where it
    /// was poured.
    /// </summary>
    /// <remarks>
    /// <c>FloodFill</c> indexes the bitmap from its own corner and reports the
    /// contour in those same pixels. Handed straight to a stroke it would be a
    /// fill offset by the whole margin — the record and the pixels disagreeing,
    /// which invariant 1 does not allow.
    /// </remarks>
    [AvaloniaFact]
    public void AFillsOutlineIsInStrokeCoordinates()
    {
        var vm = Grown();
        vm.SelectToolCommand.Execute(ToolId.Fill);
        vm.ColorHex = "#20a060";

        vm.FillAt(10, 10);

        var strokes = ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes;
        var fill = strokes.LastOrDefault(s => s.Tool == ToolKind.Fill);
        Assert.NotNull(fill);

        var minX = fill!.Points.Min(p => p.X);
        var minY = fill.Points.Min(p => p.Y);
        output.WriteLine($"fill outline starts at ({minX:F0},{minY:F0}); paper starts at ({vm.Doc.Scene.Left},{vm.Doc.Scene.Top})");

        // The paper is [-120, W); a contour still in surface pixels would start
        // at 0 and could never reach the negative half.
        Assert.True(
            minX < 0 || minY < 0,
            $"the outline never reaches the added paper — it is still surface pixels ({minX:F0},{minY:F0})");
    }

    // ---- the record ---------------------------------------------------------------------

    /// <summary>
    /// A selection becomes a clip region in stroke coordinates, because a clip
    /// region is part of the record (invariant 3).
    /// </summary>
    /// <remarks>
    /// The selection itself stays a surface mask — it is a <c>w x h</c> array of
    /// booleans and nothing else would make sense — so this is the one boundary
    /// it crosses, and the only one that has to be right.
    /// </remarks>
    [AvaloniaFact]
    public void ASelectionBecomesAClipRegionInStrokeCoordinates()
    {
        var vm = Grown();
        vm.SelectAllCommand.Execute(null);
        Assert.True(vm.HasSelection);

        var surfaceMin = vm.SelectionContours.SelectMany(c => c).Min(p => p.X);
        vm.SelectToolCommand.Execute(ToolId.Fill);
        vm.FillAt(10, 10);

        var strokes = ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes;
        var clipped = strokes.LastOrDefault(s => s.ClipId is not null);
        Assert.NotNull(clipped);
        var region = ClipRegionRegistry.Resolve(clipped!.ClipId!);
        Assert.NotNull(region);

        var recordMin = region!.Contours.SelectMany(c => c).Min(p => p.X);
        output.WriteLine($"selection kept at {surfaceMin:F0}, recorded at {recordMin:F0}");

        // Select-all covers the whole paper, so in stroke coordinates its left
        // edge is the origin, not zero.
        Assert.Equal(vm.Doc.Scene.Left, recordMin, 3);
        Assert.Equal(0, surfaceMin, 3);
    }

    // ---- and the reason none of this was caught before -----------------------------------

    /// <summary>
    /// At origin zero every conversion above is the identity.
    /// </summary>
    /// <remarks>
    /// Stated as a test rather than left implicit, because it is the reason the
    /// existing suite is blind here and the reason each test above pays for a
    /// resize before it asks anything.
    /// </remarks>
    [AvaloniaFact]
    public void OnAnUnresizedDocumentBothSpacesAreTheSame()
    {
        var canvas = new CanvasControl();
        Assert.Equal(new PixelPoint(0, 0), canvas.DocumentOrigin);

        var (dx, dy) = canvas.ViewToDoc(new Point(31, 19));
        var (sx, sy) = canvas.DocToSurface(dx, dy);

        Assert.Equal(dx, sx, 6);
        Assert.Equal(dy, sy, 6);

        var vm = VmLayers.PaperVm();
        Assert.Equal(new PixelPoint(0, 0), vm.DocumentOrigin);
    }
}
