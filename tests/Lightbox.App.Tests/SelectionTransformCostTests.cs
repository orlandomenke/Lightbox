using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B341 — a transform with a selection up must not stop dead on the first
/// handle you move, or again when you press Enter.
/// </summary>
/// <remarks>
/// <para>
/// Reported as two symptoms — "the initial movement has a significant delay"
/// and "committing the transform also takes a significant amount of time" —
/// and they were one piece of work paid twice. Everything a region transform
/// needs beyond the ordinary one is built lazily on the first pointer event
/// of the drag, and built again at the commit; on 1920×1080 that was
/// <b>346–389 ms</b> the first time and <b>346–392 ms</b> the second, against
/// 12–14 ms for every other event of the same drag.
/// </para>
/// <para>
/// <b>These assert ratios and print absolutes.</b> A wall-clock threshold in
/// milliseconds means one thing on the owner's machine and another on a
/// contended runner, and this suite runs beside three others. The ratio is
/// what the artist actually experiences — the first frame of a drag against
/// the rest of the same drag.
/// </para>
/// <para>
/// <b>The bar is set by breaking it, at the size these actually run.</b>
/// Measured at 960×540 with the complement rebuilt the old way: <b>17.2×</b> on
/// the first drag event and <b>21.2×</b> on the commit, against <b>6.8×</b> and
/// <b>7.1×</b> fixed. Twelve sits between them with room on both sides — the
/// fixed build clears it by nearly two and the broken one misses by nearly two,
/// and neither margin is a rounding error on a busy box. That revert restores
/// only the largest of B341's three fixes, so a wholly reverted build reads
/// higher still; at full size it read 30.5× and 31.6×.
/// </para>
/// <para>
/// Minima, not means, for the denominator: a mean carries whatever else the
/// machine was doing, and the question here is what the work costs when it is
/// allowed to run.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SelectionTransformCostTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>
    /// A document with sixty short marks scattered over it, at half the size
    /// the fault was measured at.
    /// </summary>
    /// <remarks>
    /// <b>960×540, and the reason is a runner that died rather than a test that
    /// failed.</b> These were written at 1920×1080 — the size the owner works
    /// at, and the size every number in B341's ledger entry was taken at. The
    /// pull request's own CI passed; the merge commit's run on <c>main</c>
    /// reported <c>The runner has received a shutdown signal</c> and
    /// <c>MSBUILD error MSB4166: Child node exited prematurely</c> partway
    /// through the App suite, with Core, Ai and Raster already green. That is
    /// an out-of-memory wearing an infrastructure failure's clothes, and this
    /// suite had just gained six page-sized documents publishing nine 8 MB
    /// snapshots each.
    /// <para>
    /// Quartering the area costs the assertions nothing, because what they
    /// assert is a <b>ratio</b> — the first event of a drag against the rest of
    /// the same drag — and both sides scale with the canvas together. The
    /// absolute milliseconds move and were never the claim; they live in the
    /// ledger entry, taken at full size, where a number that cannot flake
    /// belongs.
    /// </para>
    /// </remarks>
    private static MainViewModel Sketch(int strokes = 60)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("probe", 960, 540, 12, 72, "#ffffff", true));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        var rnd = new Random(7);
        for (var i = 0; i < strokes; i++)
        {
            double x = 150 + (rnd.NextDouble() * 450), y = 100 + (rnd.NextDouble() * 300);
            vm.BeginStroke(x, y, 1);
            for (var k = 0; k < 6; k++)
            {
                x += (rnd.NextDouble() * 20) - 10;
                y += (rnd.NextDouble() * 20) - 10;
                vm.MoveStroke(x, y, 1);
            }
            vm.EndStroke();
        }
        return vm;
    }

    private static void Marquee(MainViewModel vm) =>
        vm.ApplySelectionShape(
            [new(150, 100, 1), new(600, 100, 1), new(600, 400, 1), new(150, 400, 1)],
            false, false);

    private static void Publish(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        SKBitmap.FromImage(latest!.Image).Dispose();
    }

    private static SKMatrix Turn(double degrees)
    {
        const float px = 380, py = 250;
        var m = SKMatrix.CreateTranslation(-px, -py);
        m = m.PostConcat(SKMatrix.CreateRotation((float)(degrees * Math.PI / 180)));
        return m.PostConcat(SKMatrix.CreateTranslation(px, py));
    }

    private static double Ms(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        return sw.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// Three drags, and the best of each figure.
    /// </summary>
    /// <remarks>
    /// The minimum, not the mean, and of both halves of the ratio — this suite
    /// runs beside three others, and a mean carries whatever else the box was
    /// doing at the time. Best against best is the ratio the work has when it
    /// is allowed to run, which is the thing being asserted; a single drag
    /// caught one contended moment and reported it as a regression.
    /// </remarks>
    private (double First, double Later, double Commit) Drag()
    {
        double first = double.MaxValue, later = double.MaxValue, commit = double.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var (f, l, c) = OneDrag();
            first = Math.Min(first, f);
            later = Math.Min(later, l);
            commit = Math.Min(commit, c);
        }
        return (first, later, commit);
    }

    private static (double First, double Later, double Commit) OneDrag()
    {
        var vm = Sketch();
        Publish(vm);                     // warm, the way a canvas being drawn on is warm
        Marquee(vm);
        Assert.True(vm.BeginTransform(), vm.AiStatus);

        vm.PreviewTransform(Turn(1));
        var first = Ms(() => Publish(vm));
        var later = new List<double>();
        for (var i = 2; i <= 8; i++)
        {
            vm.PreviewTransform(Turn(i));
            later.Add(Ms(() => Publish(vm)));
        }
        var commit = Ms(() => vm.CommitTransformAffine(380, 250, 1, 1, 8 * Math.PI / 180, 0, 0));
        return (first, later.Min(), commit);
    }

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void TheFirstDragOfASelectionTransformIsNotAStall()
    {
        var (first, later, _) = Drag();
        output.WriteLine($"first drag event {first:F1} ms, later events {later:F1} ms — {first / later:F1}×");
        Assert.True(
            first < later * 12,
            $"the first event of the drag cost {first:F1} ms against {later:F1} ms for the rest of it");
    }

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void CommittingASelectionTransformIsNotAStall()
    {
        var (_, later, commit) = Drag();
        output.WriteLine($"commit {commit:F1} ms, drag events {later:F1} ms — {commit / later:F1}×");
        Assert.True(
            commit < later * 12,
            $"the commit cost {commit:F1} ms against {later:F1} ms for an event of the drag it ended");
    }
}
