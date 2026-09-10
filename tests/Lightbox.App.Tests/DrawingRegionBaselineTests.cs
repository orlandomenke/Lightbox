using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What one pointer event asks the canvas to repaint, recorded before symmetry
/// exists — measured in pixels, not milliseconds.
/// </summary>
/// <remarks>
/// <para>
/// The companion to <c>DrawingCostBaselineTests</c> in the Raster suite, on the
/// path the artist actually feels: pointer event through the view model to the
/// published clip. It asserts on <c>LastPublishClip</c> rather than on
/// wall-clock, which is the instrument that file's own doc comment recommends —
/// <em>"what the artist feels as a stutter is this rect growing, so tests
/// assert on it rather than on wall-clock, which is unusable on a shared
/// runner."</em>
/// </para>
/// <para>
/// <b>Why this is the test symmetry is most likely to break.</b> Symmetry
/// stamps a mark more than once and the copies land in different places — for a
/// radial order of six, spread right around the centre of the canvas. The
/// obvious implementation publishes one rectangle covering all of them, and for
/// any order above two that rectangle is very nearly the whole page: every
/// pointer event then repaints the canvas, which is invariant 6 and charter O3
/// broken, and the exact shape G7 exists to catch. The cheapest wrong answer is
/// worse still — a null clip, which this pipeline already means as <em>publish
/// the whole canvas</em>.
/// </para>
/// <para>
/// So when symmetry lands, one gesture must publish N dab-sized regions and not
/// one rectangle enclosing them. These assertions, unchanged, are what say so
/// for the un-mirrored case that has to keep costing what it costs today.
/// </para>
/// <para>
/// In the <c>BrushState</c> collection because it pins brush parameters, and
/// those live in a process-wide store — the reason <c>LivePreviewPixelTests</c>
/// is there too.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class DrawingRegionBaselineTests(ITestOutputHelper output) : BrushStateIsolated
{
    // Deliberately not 4K: nothing here needs the pixels, and large App-suite
    // renders have taken a runner down before. A dab-sized region is a smaller
    // fraction of a bigger page, so this is the harder page to pass on, not the
    // easier one.
    private const int W = 1280;
    private const int H = 720;

    private static MainViewModel Vm(double brushSize = 24)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("baseline", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#204080";
        vm.BrushSize = brushSize;
        vm.BrushHardness = 0.9;
        vm.BrushOpacity = 0.9;
        vm.BrushFlow = 0.8;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        return vm;
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>The clip a pointer event published, or null for the whole canvas.</summary>
    private static SKRectI? ClipOf(MainViewModel vm) => vm.LastPublishClip;

    private static long AreaOf(SKRectI? clip) =>
        clip is { } r ? (long)r.Width * r.Height : (long)W * H;

    // ---- one event ------------------------------------------------------

    /// <summary>
    /// Every event of an ordinary stroke publishes a dab-sized clip, and never
    /// the whole canvas.
    /// </summary>
    [AvaloniaFact]
    public void EveryPointerEventPublishesADabSizedClip()
    {
        var vm = Vm();
        vm.BeginStroke(300, 300, 1);
        Pump();

        var areas = new List<long>();
        var nulls = 0;
        for (var i = 1; i <= 12; i++)
        {
            vm.MoveStroke(300 + i * 20, 300 + i * 5, 0.9);
            Pump();
            if (ClipOf(vm) is null) nulls++;
            areas.Add(AreaOf(ClipOf(vm)));
        }

        vm.EndStroke();
        Pump();

        var page = (long)W * H;
        var worst = areas.Max();
        output.WriteLine(
            $"clip areas {string.Join(", ", areas)} px² | worst {worst} px² "
            + $"({worst / (double)page:P2} of a {W}×{H} page) | whole-canvas publishes: {nulls}");

        Assert.Equal(0, nulls);
        Assert.True(
            worst < page / 20,
            $"a pointer event published {worst} px², {worst / (double)page:P1} of the page — "
            + "the repaint is the canvas, not the mark");
    }

    /// <summary>
    /// Two marks at opposite ends of the page are two small clips, and their
    /// union is very much larger than either.
    /// </summary>
    /// <remarks>
    /// The union trap on the real pipeline, and the arithmetic recorded before
    /// there is any symmetry code to argue about. One gesture under a two-axis
    /// mirror produces exactly this pair of marks; a single enclosing rectangle
    /// is the whole width of the document.
    /// </remarks>
    [AvaloniaFact]
    public void TwoDistantMarksAreTwoSmallClipsNotOneLargeOne()
    {
        var vm = Vm();

        vm.BeginStroke(120, 360, 1);
        Pump();
        vm.MoveStroke(150, 360, 0.9);
        Pump();
        var left = ClipOf(vm);
        vm.EndStroke();
        Pump();

        vm.BeginStroke(1130, 360, 1);
        Pump();
        vm.MoveStroke(1160, 360, 0.9);
        Pump();
        var right = ClipOf(vm);
        vm.EndStroke();
        Pump();

        Assert.NotNull(left);
        Assert.NotNull(right);

        var separate = AreaOf(left) + AreaOf(right);
        var union = SKRectI.Union(left!.Value, right!.Value);
        var unionArea = (long)union.Width * union.Height;
        var page = (long)W * H;

        output.WriteLine(
            $"left {AreaOf(left)} px², right {AreaOf(right)} px², together {separate} px² "
            + $"({separate / (double)page:P2} of the page); one enclosing rect {unionArea} px² "
            + $"({unionArea / (double)page:P1}) — {unionArea / (double)separate:0.0}× the paint");

        Assert.True(
            unionArea > separate * 5,
            "these two marks are not far enough apart for this test to mean anything");
        Assert.True(
            separate < page / 20,
            $"two dab-sized clips came to {separate} px², {separate / (double)page:P1} of the page");
    }
}
