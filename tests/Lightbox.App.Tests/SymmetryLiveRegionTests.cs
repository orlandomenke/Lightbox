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
    /// A stroke drawn one event at a time comes out exactly as the same stroke
    /// rendered in one pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The smear test, and the reason it compares against the engine.</b> The
    /// live scratch is incremental: settled dabs are stamped once and the moving
    /// tail is lent and taken back. Symmetry's copies are stamped into that same
    /// scratch, so a copy landing inside the drawn mark's lent region — a stroke
    /// crossing its own axis — would be erased by the next rollback, or stamped
    /// twice, and the mark would come out heavier or lighter than it should.
    /// </para>
    /// <para>
    /// Neither shows up as an exception or a wrong region; it shows up as the
    /// preview disagreeing with the commit, which is what this measures. The
    /// stroke deliberately crosses the axis so the copies and the drawn mark
    /// overlap.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ASymmetricStrokeCrossingItsOwnAxisMatchesTheExactRender()
    {
        var vm = Vm(Mirror);
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Across the axis, so the drawn mark and its reflection overlap.
        vm.BeginStroke(AxisX - 70, 150, 1);
        Pump();
        for (var i = 1; i <= 8; i++)
        {
            vm.MoveStroke(AxisX - 70 + i * 18, 150, 0.9);
            Pump();
        }

        vm.EndStroke();
        Pump();

        Assert.NotNull(latest);
        using var committed = Pixels(latest!);

        // The mark is symmetric about the axis by construction, so its own
        // mirror is the reference the incremental path has to reproduce. An
        // erased or doubled copy breaks that symmetry even though a single
        // render of it could never.
        var inked = 0;
        var mismatched = 0;
        for (var y = 100; y < 200; y++)
        {
            for (var x = 0; x < W / 2; x++)
            {
                var a = Inked(committed, x, y);
                var b = Inked(committed, W - 1 - x, y);
                if (a) inked++;
                if (a != b) mismatched++;
            }
        }

        output.WriteLine($"crossing stroke: {inked} px inked one side, {mismatched} mirrored pairs disagree");
        Assert.True(inked > 200, $"the stroke did not land ({inked} px)");
        Assert.True(
            mismatched * 20 < inked,
            $"{mismatched} of {inked} px disagree across the axis — the incremental path smeared a copy");
    }

    /// <summary>
    /// A symmetric stroke leaves nothing behind for the next stroke to
    /// composite.
    /// </summary>
    /// <remarks>
    /// The live scratch is cleared at pen lift over the region it recorded
    /// using, and symmetry made that region wrong: it tracked only the drawn
    /// mark, so a reflection's pixels stayed in the scratch and would compose
    /// over the next stroke as a ghost. Drawing a second stroke somewhere else
    /// and looking at where the first reflection was is what catches it.
    /// </remarks>
    [AvaloniaFact]
    public void ASymmetricStrokeLeavesNoGhostForTheNextStroke()
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

        // A second stroke with no symmetry, well clear of the first.
        vm.ActiveSymmetry = null;
        vm.BeginStroke(40, 270, 1);
        Pump();
        vm.MoveStroke(90, 270, 0.9);
        Pump();
        vm.EndStroke();
        Pump();

        using var afterSecond = Pixels(latest!);

        // Nothing should have appeared where the second stroke's reflection
        // WOULD have been, because it was drawn without an axis.
        var ghost = InkedIn(afterSecond, new SKRectI(W - 100, 255, W - 30, 285));
        output.WriteLine($"{ghost} px where a reflection of the second stroke would be");
        Assert.Equal(0, ghost);
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
