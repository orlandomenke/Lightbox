using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// B342 — what the bucket's hover preview costs, from the artist's side.
/// </summary>
/// <remarks>
/// <para>
/// Reported as "hovering the canvas previews the fill colour, and this takes
/// quite a while". It did: on a 1920×1080 page the trace behind the ghost
/// outline took <b>170–200 ms</b> whatever it was tracing, because everything
/// under it was proportional to the canvas rather than to the region — see
/// <c>FillCostFollowsTheRegionTests</c> for the half of the fix that lives in
/// <c>FloodFill</c>.
/// </para>
/// <para>
/// This is the same claim made where the artist meets it: move the pointer
/// into a new region and the outline follows without a wait. It asserts a
/// ratio rather than milliseconds — the trace against an ordinary publish of
/// the same document, both measured in the same run so contention cancels.
/// </para>
/// <para>
/// <b>That a small region costs less than a large one is pinned in
/// <c>FillCostFollowsTheRegionTests</c>, not here.</b> It was written here
/// first and could not be measured honestly: a hover that stays inside the
/// region already traced takes the containment shortcut and does no work at
/// all, so the minimum across a few moves is whichever move happened to be a
/// shortcut — 0.1 ms, and nothing to compare. The same claim made against
/// <c>FloodFill</c> directly has no dispatcher and no shortcut in it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class FillPreviewCostTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>A page ruled into cells, each one its own flood region.</summary>
    private static MainViewModel Ruled(int cells)
    {
        var vm = new MainViewModel(null);
        // Half size on purpose — see B341's guards and the runner they tipped
        // over. Everything asserted here is a ratio.
        vm.NewDocument(new NewDocumentSettings("probe", 960, 540, 12, 72, "#ffffff", true));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 8;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        void Line(double x0, double y0, double x1, double y1)
        {
            vm.BeginStroke(x0, y0, 1);
            vm.MoveStroke((x0 + x1) / 2, (y0 + y1) / 2, 1);
            vm.MoveStroke(x1, y1, 1);
            vm.EndStroke();
        }
        for (var i = 0; i <= cells; i++)
        {
            var t = i / (double)cells;
            Line((t * 940) + 10, 10, (t * 940) + 10, 530);
            Line(10, (t * 520) + 10, 950, (t * 520) + 10);
        }
        vm.ActiveTool = ToolId.Fill;
        return vm;
    }

    private static void Flush() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static double Ms(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>The centre of cell (col, row) on a 24×24 ruling.</summary>
    private static (double X, double Y) Cell(int col, int row) =>
        (10 + ((col + 0.5) * 940 / 24), 10 + ((row + 0.5) * 520 / 24));

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void MovingIntoANewRegionDoesNotStall()
    {
        var vm = Ruled(24);
        vm.PublishSnapshot();
        // Warm: the first trace of a session pays for the composite's caches.
        vm.UpdatePointerContext(Cell(1, 1).X, Cell(1, 1).Y, KeyModifiers.None);
        Flush();

        var publish = double.MaxValue;
        var hover = double.MaxValue;
        for (var i = 2; i < 8; i++)
        {
            var (x, y) = Cell(i, i);
            hover = Math.Min(hover, Ms(() =>
            {
                vm.UpdatePointerContext(x, y, KeyModifiers.None);
                Flush();
            }));
            publish = Math.Min(publish, Ms(() => vm.PublishSnapshot()));
        }
        output.WriteLine(
            $"hover into a new region {hover:F1} ms against a publish of the same page "
            + $"{publish:F1} ms — {hover / publish:F1}×");

        // Set by breaking it, at the size this runs: the old code reads 56.2×
        // and the new one 1.9×. A trace that costs about what showing the page
        // costs is a trace the artist cannot feel.
        Assert.True(
            hover < publish * 4,
            $"the hover trace cost {hover:F1} ms against {publish:F1} ms to publish the page");
    }

    // ---- the preview has to answer to the options ---------------------------

    /// <summary>
    /// Changing what a click would take re-traces what the pointer is over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Found while measuring, and it is the preview's own promise broken: the
    /// traced region was forgotten only when the <em>document</em> changed, so
    /// an artist who nudged the tolerance, the gap or the overfill — or turned
    /// smart fill off — went on being shown the region the old settings would
    /// have taken, until they happened to move the pointer across a boundary.
    /// A preview that does not answer to the control beside it is worse than
    /// none, because it is believed.
    /// </para>
    /// <para>
    /// <b>What is asserted is that it re-traced, not that the answer moved.</b>
    /// The first version of this checked the outline for a change and failed on
    /// three settings out of four — correctly, as it turns out: on hard black
    /// lines over white, raising the tolerance to 200 still leaves black 255
    /// away from the seed, gap closing hands back at the edges what it took at
    /// the barriers, and smart fill has one drawing layer to composite. The
    /// region really is the same, and a test that demanded otherwise was
    /// testing the fixture rather than the fix. Overfill is kept as the one
    /// case where the shape must visibly move, so this cannot pass by never
    /// tracing anything at all.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("tolerance")]
    [InlineData("gap")]
    [InlineData("grow")]
    [InlineData("smart")]
    public void ChangingAFillOptionRetracesThePreview(string option)
    {
        var vm = Ruled(6);
        vm.PublishSnapshot();
        IReadOnlyList<List<StrokePoint>>? shown = null;
        vm.FillPreviewChanged += (contours, _, _) => shown = contours;

        vm.UpdatePointerContext(100, 60, KeyModifiers.None);
        Flush();
        Assert.NotNull(shown);
        var before = Outline(shown!);

        // Nothing has changed, so nothing is re-traced: the containment
        // shortcut answers from the region it already has. This is the half
        // that makes the assertion below mean something.
        shown = null;
        vm.UpdatePointerContext(101, 61, KeyModifiers.None);
        Flush();
        Assert.Null(shown);

        switch (option)
        {
            case "tolerance": vm.FillTolerance = 200; break;
            case "gap": vm.FillGapPx = 40; break;    // the slider's far end
            case "grow": vm.FillGrowPx = 20; break;
            default: vm.SmartFill = false; break;
        }
        Flush();

        Assert.NotNull(shown);
        var after = Outline(shown!);
        output.WriteLine($"{option}: outline box {before} → {after}");
        if (option == "grow") Assert.NotEqual(before, after);
    }

    /// <summary>The outer contour's box, as a string — a cheap shape fingerprint.</summary>
    private static string Outline(IReadOnlyList<List<StrokePoint>> contours)
    {
        var outer = contours[0];
        return $"[{outer.Min(p => p.X):F0},{outer.Min(p => p.Y):F0}"
            + $"..{outer.Max(p => p.X):F0},{outer.Max(p => p.Y):F0}]";
    }
}
