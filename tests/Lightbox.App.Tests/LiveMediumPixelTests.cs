using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Wet media and paper effects have to be visible while drawing.
///
/// They were not: the live preview stamped raw dabs and only the commit ran
/// the medium, so a watercolour stroke was flat colour under the pen and
/// became simulated paint the instant it lifted. That is the one thing a
/// painting tool cannot do — the mark you are judging has to be the mark you
/// are making.
///
/// These effects are stroke-global (the wet edge comes from the whole
/// silhouette, the fluid flows across the whole wet area), so they cannot be
/// applied per segment. The preview instead re-renders the stroke so far
/// through the real pipeline, throttled by its own cost. What is asserted
/// here is therefore convergence, not per-event exactness.
/// </summary>
[Collection("BrushState")]
public class LiveMediumPixelTests : BrushStateIsolated
{
    private const int W = 240, H = 160;

    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        // Synchronous, so a pumped dispatcher is enough for the pass to have
        // landed: these tests pin the PIXELS of the pass, and the worker
        // choreography that B189 moved it onto has its own suite
        // (LivePostAsyncTests).
        vm.LivePostRunner = work => { work(); return Task.CompletedTask; };
        vm.NewDocument(new NewDocumentSettings("Wet", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#2050b0";
        vm.BrushSize = 28;
        vm.BrushHardness = 0.7;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 0.9;
        vm.BrushScatter = 0;
        return vm;
    }

    private static SKBitmap Pixels(RenderSnapshot s)
    {
        var bmp = SKBitmap.FromImage(s.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    /// <summary>Draw a stroke, returning the last mid-stroke frame and the committed one.</summary>
    private static (SKBitmap Live, SKBitmap Committed) Draw(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(40, 80, 0.9);
        for (var x = 50; x <= 200; x += 10)
        {
            vm.MoveStroke(x, 80, 0.9);
            Dispatcher.UIThread.RunJobs();
        }
        // Settle: the post-process is throttled and posted at background
        // priority, so give the queue a chance to land the final pass — which
        // is the convergence the design promises.
        for (var i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        var live = Pixels(latest!);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        return (live, Pixels(latest!));
    }

    /// <summary>Largest per-channel difference anywhere, 0–255.</summary>
    private static int Peak(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        var peak = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var ia = y * pa.RowBytes + x * 4;
                var ib = y * pb.RowBytes + x * 4;
                for (var c = 0; c < 4; c++) peak = Math.Max(peak, Math.Abs(sa[ia + c] - sb[ib + c]));
            }
        }
        return peak;
    }

    /// <summary>Mean absolute per-channel difference, 0–255.</summary>
    private static double Difference(SKBitmap a, SKBitmap b)
    {
        using var pa = a.PeekPixels();
        using var pb = b.PeekPixels();
        var sa = pa.GetPixelSpan();
        var sb = pb.GetPixelSpan();
        long total = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var ia = y * pa.RowBytes + x * 4;
                var ib = y * pb.RowBytes + x * 4;
                for (var c = 0; c < 4; c++) total += Math.Abs(sa[ia + c] - sb[ib + c]);
            }
        }
        return total / (double)(a.Width * a.Height * 4);
    }

    [AvaloniaTheory]
    [InlineData(MediumKind.Watercolour)]
    [InlineData(MediumKind.Gouache)]
    [InlineData(MediumKind.Oil)]
    [InlineData(MediumKind.Ink)]
    public void AMediumStrokeLooksTheSameLiveAsCommitted(MediumKind kind)
    {
        var vm = Vm();
        vm.BrushMedium = kind;

        var (live, committed) = Draw(vm);
        using (live)
        using (committed)
        {
            var diff = Difference(live, committed);
            Assert.True(diff < 1, $"{kind}: live differs from committed by {diff:0.00}/255 on average");
        }
    }

    /// <summary>
    /// The parity above, but over paint that is already down — which is where
    /// a wet medium interacts with the layer rather than with blank paper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The theory above draws on an empty layer, so nothing the medium does
    /// with `existing` is exercised: re-wetting has nothing to re-wet and
    /// physical mixing has nothing to mix with. That left the parity guard
    /// blind to exactly the case where the two paths can disagree, because the
    /// commit works into the layer while the preview is an overlay drawn over
    /// it.
    /// </para>
    /// <para>
    /// Found by building lift-and-erase — making re-wetting move the paint it
    /// picks up instead of copying it. That is a commit-path change by nature
    /// (the layer has to lose what the stroke took) and the preview cannot do it
    /// from an overlay, so live and committed came apart by exactly the lifted
    /// paint while every existing test stayed green. Measured: this build sits
    /// at <b>0.01/255 mean and 1 at worst</b>, and the lifting prototype took it
    /// to <b>0.47 and 32</b> — a crossing visibly lightening the moment the pen
    /// lifts. The thresholds leave that an order of magnitude of room and still
    /// fail on it.
    /// </para>
    /// <para>
    /// Whatever eventually fuses a wash across strokes has the same shape, so
    /// the guard belongs here before the work rather than after it. The live path
    /// already has the answer if it is needed: blur and smudge read the layer
    /// back, so they paint into a copy of it instead of an overlay, and a medium
    /// that moves paint belongs in that class too.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(MediumKind.Watercolour)]
    [InlineData(MediumKind.Ink)]
    public void AMediumStrokeCrossingWetPaintLooksTheSameLiveAsCommitted(MediumKind kind)
    {
        var vm = Vm();
        vm.BrushMedium = kind;
        // Rewetting is what makes a stroke interact with the paint it crosses,
        // and it defaults to 0 — so without this the test draws over the layer
        // without touching it and proves nothing.
        vm.MediumRewetting = 0.7;

        // Lay paint down and commit it, so the second stroke has something to
        // interact with rather than blank paper.
        vm.BeginStroke(30, 60, 0.9);
        for (var x = 40; x <= 210; x += 10) vm.MoveStroke(x, 60, 0.9);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        vm.BeginStroke(30, 95, 0.9);
        for (var x = 40; x <= 210; x += 10) vm.MoveStroke(x, 95, 0.9);
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        // Now cross both of them.
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.BeginStroke(120, 30, 0.9);
        for (var y = 40; y <= 130; y += 10)
        {
            vm.MoveStroke(120, y, 0.9);
            Dispatcher.UIThread.RunJobs();
        }
        for (var i = 0; i < 8; i++) Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        using var live = Pixels(latest!);

        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);
        using var committed = Pixels(latest!);

        var diff = Difference(live, committed);
        var peak = Peak(live, committed);
        Assert.True(
            diff < 0.1 && peak < 8,
            $"{kind}: a stroke crossing wet paint differs live from committed by {diff:0.00}/255 "
            + $"on average and {peak} at worst — the mark will jump when the pen lifts");
    }

    [AvaloniaFact]
    public void AWetEdgeIsVisibleBeforeThePenLifts()
    {
        var vm = Vm();
        vm.BrushWetEdge = 0.9;

        var (live, committed) = Draw(vm);
        using (live)
        using (committed)
        {
            Assert.True(Difference(live, committed) < 1,
                "the wet edge was not on screen until the stroke committed");
        }
    }

    [AvaloniaFact]
    public void PaperTextureIsVisibleBeforeThePenLifts()
    {
        var vm = Vm();
        vm.BrushTextureSurface = nameof(PaperKind.ColdPress);
        vm.BrushTextureDepth = 0.7;

        var (live, committed) = Draw(vm);
        using (live)
        using (committed)
        {
            Assert.True(Difference(live, committed) < 1,
                "the paper texture was not on screen until the stroke committed");
        }
    }

    [AvaloniaFact]
    public void AMediumStrokeIsNotJustFlatDabs()
    {
        // Guards the guard: if the medium quietly stopped rendering at all,
        // live and committed would agree perfectly and every test above would
        // pass. A simulated stroke must differ from plain dabs.
        var vm = Vm();
        var (plainLive, _) = Draw(vm);

        var wet = Vm();
        wet.BrushMedium = MediumKind.Watercolour;
        var (wetLive, _) = Draw(wet);

        using (plainLive)
        using (wetLive)
        {
            // Mean over the whole canvas understates it — the stroke covers a
            // fraction of the frame — so this asks whether any pixel differs
            // substantially, which is what "you can see the medium" means.
            Assert.True(Peak(plainLive, wetLive) > 24,
                $"the watercolour preview differs from plain dabs by at most {Peak(plainLive, wetLive)}");
        }
    }

    [AvaloniaFact]
    public void APlainBrushDoesNotPayForThePostProcess()
    {
        // No medium, no wet edge, no texture, no granulation: the throttled
        // re-render must never run, or every ordinary stroke pays for a
        // feature it is not using.
        var vm = Vm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(40, 80, 1);
        for (var x = 50; x <= 200; x += 10)
        {
            vm.MoveStroke(x, 80, 1);
            Dispatcher.UIThread.RunJobs();
        }
        vm.EndStroke();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var bmp = Pixels(latest!);
        Assert.Equal(SKColors.White, bmp.GetPixel(10, 10));
        Assert.NotEqual(SKColors.White, bmp.GetPixel(120, 80));
    }
}
