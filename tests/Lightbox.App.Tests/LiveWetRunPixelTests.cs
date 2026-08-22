using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// A wash laid in several strokes is simulated as one wash, and the artist has
/// to be shown that while the pen is down.
/// </summary>
/// <remarks>
/// <para>
/// The preview is an overlay drawn <em>over</em> the layer, and a fused wash is
/// lighter than the same strokes dried one at a time — so nothing drawn on top
/// can take paint away. Without lifting the open run off the layer first, the
/// mark diverged from its own commit by 4.2/255 on average and <b>123 at
/// worst</b>: paint the fourth band of a wash, lift the pen, watch the whole
/// wash go pale. That is the one thing a painting tool cannot do.
/// </para>
/// <para>
/// So these are convergence tests like <see cref="LiveMediumPixelTests"/>, on
/// the case that has strokes before it in the record.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LiveWetRunPixelTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 240, H = 160;

    private static MainViewModel Vm(int window)
    {
        var vm = new MainViewModel(null);
        // Synchronous, so pumping the dispatcher is enough for the medium pass
        // to have landed — see LiveMediumPixelTests for the argument.
        vm.LivePostRunner = work => { work(); return Task.CompletedTask; };
        vm.NewDocument(new NewDocumentSettings("Wash", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#2050b0";
        vm.BrushSize = 28;
        vm.BrushHardness = 0.7;
        vm.BrushOpacity = 1;
        // Pale, so the wash is not clipped: at full flow overlapping strokes
        // saturate and every difference between the two paths hides under it.
        vm.BrushFlow = 0.2;
        vm.BrushScatter = 0;
        vm.BrushMedium = MediumKind.Watercolour;
        vm.MediumRewetting = 0.7;
        vm.MediumWetStrokes = window;
        return vm;
    }

    private static SKBitmap Pixels(RenderSnapshot s)
    {
        var bmp = SKBitmap.FromImage(s.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    private static (double Mean, int Peak) Divergence(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        long total = 0;
        var peak = 0;
        for (var y = 0; y < a.Height; y++)
        for (var x = 0; x < a.Width; x++)
        {
            var ia = y * pa.RowBytes + x * 4;
            var ib = y * pb.RowBytes + x * 4;
            for (var c = 0; c < 4; c++)
            {
                var d = Math.Abs(sa[ia + c] - sb[ib + c]);
                total += d;
                peak = Math.Max(peak, d);
            }
        }
        return (total / (double)(a.Width * a.Height * 4), peak);
    }

    private static void Band(MainViewModel vm, double y, double x0 = 40, double x1 = 200)
    {
        vm.BeginStroke(x0, y, 0.9);
        for (var x = x0 + 10; x <= x1; x += 10) vm.MoveStroke(x, y, 0.9);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Draw one more band, watching it, and return the last live frame and the commit.</summary>
    private static (SKBitmap Live, SKBitmap Committed) WatchBand(
        MainViewModel vm, double y, double x0 = 40, double x1 = 200)
    {
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(x0, y, 0.9);
        for (var x = x0 + 10; x <= x1; x += 10)
        {
            vm.MoveStroke(x, y, 0.9);
            Dispatcher.UIThread.RunJobs();
        }
        // Settle: the pass is throttled and posted at background priority.
        for (var i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        var live = Pixels(latest!);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        return (live, Pixels(latest!));
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(4)]
    public void AWashLooksTheSameLiveAsCommitted(int window)
    {
        var vm = Vm(window);
        Band(vm, 60);
        Band(vm, 72);

        var (live, committed) = WatchBand(vm, 84);
        using (live)
        using (committed)
        {
            var (mean, peak) = Divergence(live, committed);
            output.WriteLine($"window {window}: {mean:F2}/255 mean, {peak} peak");
            Assert.True(
                mean < 0.1 && peak < 8,
                $"window {window}: the third band of a wash differs live from committed by "
                + $"{mean:F2}/255 on average and {peak} at worst — the wash will jump when the pen lifts");
        }
    }

    [AvaloniaFact]
    public void AStrokeThatOnlyReachesTheWashPartWayThroughStillMatches()
    {
        // Whether a mark is sharing a wash is not knowable when the pen goes
        // down — its reach grows as it is drawn. This one starts clear of the
        // first band and runs into it, so the run is joined mid-drag, with the
        // live dabs already stamped and the wrong way round in the scratch.
        var vm = Vm(4);
        Band(vm, 60, x0: 120, x1: 200);

        var (live, committed) = WatchBand(vm, 66, x0: 20, x1: 190);
        using (live)
        using (committed)
        {
            var (mean, peak) = Divergence(live, committed);
            output.WriteLine($"joined mid-drag: {mean:F2}/255 mean, {peak} peak");
            Assert.True(mean < 0.1 && peak < 8, $"{mean:F2}/255 mean, {peak} peak");
        }
    }

    [AvaloniaFact]
    public void JoiningAWashDoesNotLeaveDabsInTheSharedScratch()
    {
        // Joining a run rebuilds the live scratch, and the scratch is pooled
        // across strokes: `ClearScratch` wipes exactly the region it was told
        // about, so a rebuild that forgot what this stroke had already stamped
        // would leave those dabs behind for the next stroke to composite a
        // second time. That is B39's failure reached from the other direction,
        // and it shows up as paint appearing where nothing was drawn: without
        // the live mark's own region kept in `ScratchUsed`, this reads 890 px
        // of stale wash, 65 levels at worst.
        var vm = Vm(4);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // A band that starts clear of the first and runs into it, so the run is
        // joined mid-drag with dabs already in the scratch.
        Band(vm, 60, x0: 120, x1: 200);
        Band(vm, 66, x0: 20, x1: 190);
        Dispatcher.UIThread.RunJobs();
        using var settled = Pixels(latest!);

        // Now one mark far away, in a dry brush, and watch it live.
        vm.MediumWetStrokes = 0;
        vm.BrushMedium = MediumKind.None;
        vm.BeginStroke(40, 140, 0.9);
        vm.MoveStroke(60, 140, 0.9);
        for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
        using var during = Pixels(latest!);

        // Everything above the new mark's neighbourhood has to be untouched.
        var strayed = 0;
        var worst = 0;
        for (var y = 0; y < 110; y++)
        for (var x = 0; x < W; x++)
        {
            var a = settled.GetPixel(x, y);
            var b = during.GetPixel(x, y);
            var d = Math.Max(
                Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)),
                Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
            if (d <= 2) continue;
            strayed++;
            worst = Math.Max(worst, d);
        }
        output.WriteLine($"{strayed} px changed away from the new mark, worst {worst}");
        Assert.True(strayed == 0, $"{strayed} px of stale scratch reappeared, worst {worst}");
    }

    [AvaloniaFact]
    public void AbandoningTheStrokeLeavesTheWashAsItWas()
    {
        // The preview takes the open run off the layer. A stroke that never
        // commits — a click with no drag, a tab switch, playback starting — has
        // to put it back, or the artist's wash loses its last strokes.
        var vm = Vm(4);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        Band(vm, 60);
        Band(vm, 72);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        using var before = Pixels(latest!);

        // A stroke that joins the wash, then playback, which cancels it.
        vm.BeginStroke(40, 78, 0.9);
        for (var x = 50; x <= 200; x += 10)
        {
            vm.MoveStroke(x, 78, 0.9);
            Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
        vm.PlayCommand.Execute(null);
        vm.PauseCommand.Execute(null);
        vm.PublishSnapshot();
        for (var i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
        using var after = Pixels(latest!);

        var (mean, peak) = Divergence(before, after);
        output.WriteLine($"after an abandoned stroke: {mean:F2}/255 mean, {peak} peak");
        Assert.True(mean < 0.1 && peak < 8, $"the wash changed: {mean:F2}/255 mean, {peak} peak");
    }

    [AvaloniaFact]
    public void AWashIsFlatterWithTheWindowOnThanWithout()
    {
        // The point of the whole thing, through the app rather than the
        // rasteriser: six bands, and the seams between them.
        var flat = Ribbing(6);
        var ribbed = Ribbing(0);
        output.WriteLine($"ribbing: window off {ribbed:P1}, window on {flat:P1}");
        Assert.True(flat < ribbed * 0.6, $"window on gave {flat:P1} against {ribbed:P1} off");

        static double Ribbing(int window)
        {
            var vm = Vm(window);
            RenderSnapshot? latest = null;
            vm.SnapshotChanged += s => latest = s;
            for (var i = 0; i < 6; i++) Band(vm, 50 + i * 10);
            Dispatcher.UIThread.RunJobs();
            using var painted = Pixels(latest!);

            // Rows through the middle of the sweep, each covered by two bands
            // or more, as a fraction of their own mean — see WetRunTests for
            // why a row profile and not a box metric.
            var rows = new List<double>();
            for (var y = 56; y < 104; y++)
            {
                double sum = 0;
                for (var x = 70; x < 170; x++) sum += painted.GetPixel(x, y).Blue;
                rows.Add(sum / 100);
            }
            var mean = rows.Average();
            return Math.Sqrt(rows.Sum(r => (r - mean) * (r - mean)) / rows.Count) / mean;
        }
    }
}
