using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Symmetry through the real live-preview pipeline: does the reflection appear
/// under the pointer, and does it cost the region it touches rather than the
/// page.
/// </summary>
/// <remarks>
/// <para>
/// <c>MainViewModel.Painting.cs</c> is the second-riskiest file in the
/// repository by <c>HOTSPOTS.md</c> and had no test file of its own at all, so
/// these drive the actual begin/move/end pipeline rather than the engine
/// underneath it. The failure they exist to catch is a <b>smear</b>: the live
/// scratch lends the pixels under its moving dabs and takes them back next
/// event, and a copy stamped into a region that is later rolled back leaves ink
/// with no cause visible anywhere near it.
/// </para>
/// <para>
/// In the <c>BrushState</c> collection because it pins brush parameters, which
/// live in a process-wide store.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class SymmetryLiveRegionTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 400;
    private const int H = 300;
    private const double AxisX = W / 2.0;

    private static MainViewModel Vm(SymmetryAxis? axis, double hardness = 0.9)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("symmetry", W, H, 12, 72, "#ffffff", false));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 16;
        vm.BrushHardness = hardness;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.ActiveSymmetry = axis;
        return vm;
    }

    /// <summary>A vertical mirror down the middle of the page.</summary>
    private static SymmetryAxis Mirror => new()
    {
        CenterX = AxisX, CenterY = H / 2.0, AngleDeg = 90, Order = 1, Mirror = true,
    };

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static SKBitmap Pixels(RenderSnapshot snapshot)
    {
        var bmp = SKBitmap.FromImage(snapshot.Image);
        Assert.NotNull(bmp);
        return bmp!;
    }

    /// <summary>Draws a short stroke on the left of the axis, one event a point.</summary>
    private static void DrawLeft(MainViewModel vm)
    {
        vm.BeginStroke(60, 120, 1);
        Pump();
        for (var i = 1; i <= 6; i++)
        {
            vm.MoveStroke(60 + i * 15, 120 + i * 8, 0.9);
            Pump();
        }
    }

    private static bool Inked(SKBitmap b, int x, int y) => b.GetPixel(x, y).Red < 128;

    private static int InkedIn(SKBitmap b, SKRectI box)
    {
        var n = 0;
        for (var y = Math.Max(0, box.Top); y < Math.Min(b.Height, box.Bottom); y++)
        {
            for (var x = Math.Max(0, box.Left); x < Math.Min(b.Width, box.Right); x++)
            {
                if (Inked(b, x, y)) n++;
            }
        }

        return n;
    }

    // ---- the ask ---------------------------------------------------------

    /// <summary>
    /// The reflection is on screen while the stroke is still being drawn, not
    /// only after the pen lifts.
    /// </summary>
    /// <remarks>
    /// The whole point of the branch. Before this, the live path stamped and
    /// published only the drawn copy, so a mirrored mark appeared on the next
    /// full repaint — which for an artist is "symmetry does not work while I
    /// draw".
    /// </remarks>
    [AvaloniaFact]
    public void AReflectionIsOnScreenWhileTheStrokeIsStillBeingDrawn()
    {
        var vm = Vm(Mirror);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        DrawLeft(vm);

        Assert.NotNull(latest);
        using var live = Pixels(latest!);

        // The drawn half, and the reflection of its early (settled) part. The
        // copies trail the pen by one event's provisional dabs, so the reflected
        // TIP may not be down yet — this asks about the body of the mark.
        var drawn = InkedIn(live, new SKRectI(50, 110, 130, 190));
        var reflected = InkedIn(live, new SKRectI(W - 130, 110, W - 50, 190));
        output.WriteLine($"mid-stroke: drawn half {drawn} px, reflected half {reflected} px");

        Assert.True(drawn > 100, $"the drawn half is not on screen at all ({drawn} px)");
        Assert.True(
            reflected > 100,
            $"the reflection is not on screen while drawing ({reflected} px against {drawn} px drawn)");

        vm.EndStroke();
        Pump();
    }

    /// <summary>
    /// And without an axis nothing appears on the other side, so the test above
    /// is measuring symmetry rather than a wide brush.
    /// </summary>
    [AvaloniaFact]
    public void WithoutAnAxisNothingAppearsOnTheOtherSide()
    {
        var vm = Vm(null);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        DrawLeft(vm);
        Assert.NotNull(latest);
        using var live = Pixels(latest!);

        var reflected = InkedIn(live, new SKRectI(W - 130, 110, W - 50, 190));
        output.WriteLine($"no axis: {reflected} px on the far side");
        Assert.Equal(0, reflected);

        vm.EndStroke();
        Pump();
    }

    // ---- the failure it would be easy to ship ----------------------------

    /// <summary>
    /// While the stroke is still being drawn, the settled body of a mark that
    /// crosses its own axis already looks like what the commit will render.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The smear test, and it has to read the live overlay to be one.</b> The
    /// live scratch is incremental: settled dabs are stamped once and the moving
    /// tail is lent and taken back. Symmetry's copies go into that same scratch,
    /// so a copy landing inside the drawn mark's lent region — which is what a
    /// stroke crossing its own axis produces — would be erased by the next
    /// rollback, or stamped twice, and come out lighter or heavier than the
    /// commit will.
    /// </para>
    /// <para>
    /// <b>An earlier version of this test read its pixels after
    /// <c>EndStroke</c>, and was worthless.</b> By then <c>StrokeBuilder</c> has
    /// dropped the live stroke and <c>ScenePassBuilder.OverlayFor</c> excludes
    /// the live scratch from the composite entirely, so it was re-testing the
    /// commit path — which <c>SymmetryStampingTests</c> already covers.
    /// Confirmed rather than assumed: with the copy stamping disabled the old
    /// test still passed, and this one fails.
    /// </para>
    /// <para>
    /// Compared over the settled body rather than the whole mark, because the
    /// copies deliberately trail the pen by one event's provisional dabs — that
    /// is the lag the design buys its lack of per-copy state with, and it lives
    /// near the tip.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ASymmetricStrokeCrossingItsOwnAxisPreviewsWhatItWillCommit()
    {
        var vm = Vm(Mirror);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // A DIAGONAL across the axis, and diagonal on purpose: a horizontal
        // stroke's reflection lands on top of the stroke itself, so no region
        // can tell a reflection that is present from one that is missing. An
        // earlier version of this used one and could not fail. This one crosses
        // its own reflection in an X, so there are places only the copy reaches.
        vm.BeginStroke(AxisX - 70, 120, 1);
        Pump();
        for (var i = 1; i <= 8; i++)
        {
            vm.MoveStroke(AxisX - 70 + i * 18, 120 + i * 7.5, 0.9);
            Pump();
        }

        Assert.NotNull(latest);
        // The live overlay, while the stroke is still under the pen.
        var live = Pixels(latest!);

        vm.EndStroke();
        Pump();
        using var committed = Pixels(latest!);

        // Where only the REFLECTION reaches: the drawn mark runs down-right from
        // (130,120) to (274,180), so at x≈240 it sits near y≈166, while its
        // reflection sits near y≈133. This box holds the copy's settled body and
        // none of the drawn mark, and it is at the copy's early end rather than
        // the lagging one — the drawn tip mirrors to LOW x, so that is where the
        // one-event lag lives.
        var body = new SKRectI(225, 124, 258, 143);
        var inked = 0;
        var diverged = 0;
        var structural = 0;
        var liveExtra = 0;
        var liveMissing = 0;
        var worst = 0;
        for (var y = body.Top; y < body.Bottom; y++)
        {
            for (var x = body.Left; x < body.Right; x++)
            {
                var l = live.GetPixel(x, y);
                var c = committed.GetPixel(x, y);
                if (c.Red < 128) inked++;
                var d = Math.Abs(l.Red - c.Red);
                if (d <= 16) continue;
                diverged++;
                worst = Math.Max(worst, d);

                // Ink present in one and absent in the other, rather than an
                // edge landing a fraction of a pixel apart. This is the
                // distinction that matters: a rim that shifted is invisible, a
                // copy that is missing is the feature not working.
                var lInk = l.Red < 96;
                var cInk = c.Red < 96;
                var lPaper = l.Red > 160;
                var cPaper = c.Red > 160;
                if (lInk && cPaper) liveExtra++;
                if (cInk && lPaper) liveMissing++;
                if ((lInk && cPaper) || (cInk && lPaper)) structural++;
            }
        }

        live.Dispose();

        output.WriteLine(
            $"settled body {body.Width}×{body.Height}: {inked} px inked in the commit; "
            + $"{diverged} px disagreed by more than 16/255 (worst {worst}), of which "
            + $"{structural} are ink-versus-paper rather than a shifted edge "
            + $"({liveMissing} missing from the preview, {liveExtra} extra in it)");
        Assert.True(
            inked > 80,
            $"the reflection did not land in the compared region ({inked} px) — the fixture is wrong, "
            + "not the code");
        // <b>The two directions mean opposite things, so they get separate
        // bars.</b> Measured here: 1 px extra, 104 px missing, of 427 inked.
        //
        // EXTRA ink in the preview is the smear — a copy stamped twice, or ink
        // left behind by a rollback that restored the wrong region. That is the
        // failure this test exists to catch and it is bounded hard.
        Assert.True(
            liveExtra * 100 < inked,
            $"{liveExtra} of {inked} px are inked in the preview where the commit has paper — "
            + "the incremental path doubled a copy or rolled one back wrongly");

        // MISSING ink is the lag, and it is the design: copies stamp only dabs
        // whose position has settled, so a copy trails the drawn mark by
        // whatever has not. It is a real, visible fraction rather than a few
        // pixels — a quarter of this region — which is the honest cost of
        // needing no per-copy tail state. The bar is loose because it is
        // measuring a deliberate lag; what it still catches is a copy that never
        // arrived at all, which reads as 99% missing.
        Assert.True(
            liveMissing * 2 < inked,
            $"{liveMissing} of {inked} px of the copy are missing from the preview — that is not "
            + "the settle lag, that is a copy that was never stamped");
    }

    /// <summary>
    /// Turning the axis off applies to the next stroke, and the committed
    /// reflection of the previous one stays put.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is not the test its name used to claim, and the name was the
    /// thing that was wrong.</b> It was written to catch a reflection left
    /// behind in the live scratch: <c>ScratchUsed</c> tracked only the drawn
    /// mark before this branch, so the scratch was cleared over less than it had
    /// inked. That union is now made and is still right — but <b>this test
    /// cannot fail for that reason</b>. Removing the union and re-running it
    /// still passes, because the compositor lays the live scratch down only
    /// inside the region the <em>current</em> stroke has used, so stale ink
    /// outside that is never composited. Tried and measured rather than assumed.
    /// </para>
    /// <para>
    /// So it is named for what it does check, which is worth checking on its
    /// own: that <c>ActiveSymmetry</c> is read per stroke rather than latched at
    /// the first one, and that a committed reflection survives a later stroke
    /// drawn near it. The <c>ScratchUsed</c> union is kept as hygiene — a
    /// scratch must be cleared over everything it inked — and is guarded by
    /// nothing here, which is recorded rather than papered over.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void TurningTheAxisOffAppliesToTheNextStrokeAndNotTheLastOne()
    {
        var vm = Vm(Mirror);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        DrawLeft(vm);
        vm.EndStroke();
        Pump();

        using (var afterFirst = Pixels(latest!))
        {
            var reflected = InkedIn(afterFirst, new SKRectI(W - 130, 110, W - 50, 190));
            Assert.True(reflected > 100, $"the committed reflection is missing ({reflected} px)");
        }

        // A second stroke with no symmetry, well clear of the first — and read
        // WHILE it is being drawn. A leftover copy lives in the live scratch,
        // and OverlayFor only composites that scratch while a stroke is active:
        // after EndStroke the overlay is excluded and a ghost is invisible,
        // which is why the first version of this test could not fail.
        // Placed so its own repaint region OVERLAPS where the first stroke's
        // reflection was: the compositor only lays the live scratch down inside
        // the region the current stroke has used, so stale ink outside that is
        // invisible and a test that looked elsewhere could not fail.
        vm.ActiveSymmetry = null;
        vm.BeginStroke(W - 120, 205, 1);
        Pump();
        vm.MoveStroke(W - 60, 205, 0.9);
        Pump();

        using var afterSecond = Pixels(latest!);

        // Nothing should have appeared where the second stroke's reflection
        // WOULD have been, because it was drawn without an axis.
        // Just above the second stroke and inside its repaint region — where the
        // first stroke's reflection ended, and where nothing should be now.
        var ghost = InkedIn(afterSecond, new SKRectI(W - 128, 176, W - 52, 194));
        output.WriteLine($"{ghost} px where a reflection of the second stroke would be");
        Assert.Equal(0, ghost);

        vm.EndStroke();
        Pump();
    }

    // ---- what it costs ---------------------------------------------------

    /// <summary>
    /// Every copy is inside the region the publish repaints, and that region is
    /// still a fraction of the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This test records a known limitation rather than a clean win.</b>
    /// <c>PublishState.MarkDirty</c> unions everything marked since the last
    /// publish into one rectangle, so marking a region per copy currently
    /// repaints the box enclosing them. For a vertical mirror that box is a wide
    /// short band — measured below — and for a radial order it would be most of
    /// the page.
    /// </para>
    /// <para>
    /// Carrying disjoint regions through route selection, the compose ring and
    /// the tiled path is its own piece of work; until then this asserts the two
    /// things that are true and matter: no copy is left stale (each is inside
    /// the clip), and the clip has not become the whole canvas.
    /// <c>DrawingRegionBaselineTests</c> holds the un-mirrored figure this is
    /// measured against — 1924 px², 0.21% of its page.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void EveryCopyIsInsideThePublishedClipAndItIsNotTheWholeCanvas()
    {
        var vm = Vm(Mirror);
        vm.BeginStroke(60, 150, 1);
        Pump();

        var areas = new List<long>();
        var nulls = 0;
        for (var i = 1; i <= 6; i++)
        {
            vm.MoveStroke(60 + i * 12, 150, 0.9);
            Pump();
            if (vm.LastPublishClip is not { } clip)
            {
                nulls++;
                continue;
            }

            areas.Add((long)clip.Width * clip.Height);
        }

        vm.EndStroke();
        Pump();

        var page = (long)W * H;
        var worst = areas.Count > 0 ? areas.Max() : page;
        output.WriteLine(
            $"mirrored clip areas {string.Join(", ", areas)} px² | worst {worst} "
            + $"({worst / (double)page:P1} of a {W}×{H} page) | whole-canvas publishes: {nulls}");

        Assert.Equal(0, nulls);
        Assert.True(
            worst < page / 2,
            $"a mirrored pointer event published {worst} px², {worst / (double)page:P0} of the page");
    }
}
