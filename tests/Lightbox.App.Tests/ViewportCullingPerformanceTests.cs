using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Painting must cost the dirty region, not the visible viewport — including
/// when the compositor is culling to that viewport.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gap this file closes.</b> Every test in
/// <c>LargeCanvasPerformanceTests</c> paints on a fitted view and never calls
/// <see cref="MainViewModel.SetViewport"/>. B82 then made the culled path the
/// one an artist takes the moment they zoom in past fit — the ordinary posture
/// for painting detail — and that path repainted the whole visible rectangle on
/// every pointer event. Measured at <b>0.26 ms → 76 ms</b>, roughly 290×, and
/// every budget in the suite stayed green because none of them set a viewport.
/// </para>
/// <para>
/// So these tests assert the property directly rather than a wall-clock budget:
/// <b>the published clip must stay proportional to the stroke, not to the
/// viewport</b>. A clip assertion cannot flake on a loaded CI box the way a
/// millisecond budget can, and it fails for the reason the artist reported.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class ViewportCullingPerformanceTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel PinnedVm() => new(null)
    {
        SmoothStrokes = false,
        ColorHex = "#000000",
        BrushSize = 20,
        BrushHardness = 1,
        BrushOpacity = 1,
        BrushFlow = 1,
        BrushWetEdge = 0,
        BrushGranulation = 0,
        BrushScatter = 0,
    };

    /// <summary>
    /// A pointer event inside a culled viewport must repaint the dab, not the
    /// viewport. This is the regression itself.
    /// </summary>
    [AvaloniaFact]
    public void APointerEventInACulledViewportRepaintsTheDabNotTheViewport()
    {
        var vm = PinnedVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Zoomed in on a 1920x1080 document: a 600x400 window of it is visible.
        var window = new SKRectI(400, 300, 1000, 700);
        vm.SetViewport(window);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Settle: the first publish after a viewport change is legitimately full.
        vm.BeginStroke(600, 450, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Now the steady state an artist is in: one small move inside the window.
        vm.BeginStroke(700, 500, 1);
        vm.MoveStroke(716, 500, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var clip = vm.LastPublishClip;
        output.WriteLine($"viewport {window} ({(long)window.Width * window.Height} px)");
        output.WriteLine($"published clip {(clip is null ? "WHOLE CANVAS" : clip.ToString())}");

        Assert.NotNull(latest);
        Assert.NotNull(clip);

        var clipArea = (long)clip!.Value.Width * clip.Value.Height;
        var viewportArea = (long)window.Width * window.Height;
        var share = 100.0 * clipArea / viewportArea;
        output.WriteLine($"clip is {share:F1}% of the viewport — a 20px brush moving 16px should be a few per cent");

        // A 20 px brush moving 16 px cannot legitimately need a quarter of a
        // 600x400 window. Generous, so it fails on the defect and not on a
        // reasonable change to dab padding.
        Assert.True(share < 25.0,
            $"a pointer event repainted {share:F1}% of the visible viewport "
            + $"({clip.Value.Width}x{clip.Value.Height} of {window.Width}x{window.Height}) — "
            + "the culled path is ignoring the dirty region, so painting costs "
            + "the viewport instead of the stroke");
    }

    /// <summary>
    /// The same stroke must cost the same clip whether or not the compositor is
    /// culling — culling is a rendering decision and may not change the amount
    /// of work a dab implies.
    /// </summary>
    [AvaloniaFact]
    public void CullingDoesNotEnlargeTheClipAStrokeImplies()
    {
        static SKRectI? ClipFor(SKRectI? viewport)
        {
            var vm = PinnedVm();
            vm.SetViewport(viewport);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            vm.BeginStroke(600, 450, 1);
            vm.EndStroke();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            vm.BeginStroke(700, 500, 1);
            vm.MoveStroke(716, 500, 1);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return vm.LastPublishClip;
        }

        var fitted = ClipFor(null);
        var culled = ClipFor(new SKRectI(400, 300, 1000, 700));

        output.WriteLine($"fitted clip {(fitted is null ? "WHOLE CANVAS" : fitted.ToString())}");
        output.WriteLine($"culled clip {(culled is null ? "WHOLE CANVAS" : culled.ToString())}");

        Assert.NotNull(fitted);
        Assert.NotNull(culled);

        var fittedArea = (long)fitted!.Value.Width * fitted.Value.Height;
        var culledArea = (long)culled!.Value.Width * culled.Value.Height;
        output.WriteLine($"fitted {fittedArea} px, culled {culledArea} px, ratio {culledArea / (double)fittedArea:F1}x");

        Assert.True(culledArea <= fittedArea * 2,
            $"culling made the same dab repaint {culledArea / (double)fittedArea:F1}x more pixels");
    }

    /// <summary>
    /// The other half of B121: gating culling on a whole-canvas publish must not
    /// switch it off altogether. A frame change repaints everything anyway, and
    /// that is precisely where composing the viewport instead of the document
    /// pays — it is what B29 is waiting on.
    /// </summary>
    [AvaloniaFact]
    public void AWholeCanvasPublishStillComposesOnlyTheViewport()
    {
        var vm = PinnedVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        var window = new SKRectI(400, 300, 1000, 700);
        vm.SetViewport(window);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        output.WriteLine($"document {latest!.DocWidth}x{latest.DocHeight}");
        output.WriteLine($"published image {latest.Image.Width}x{latest.Image.Height} covering {latest.DocViewport}");

        // The window reaches past the document, so what comes back is the
        // intersection — that clamp is the point of ClampToDocument, not a
        // discrepancy. Stating it here rather than the raw window, because an
        // earlier version of this test asserted the raw one and was simply wrong.
        var expected = SKRectI.Intersect(window, new SKRectI(0, 0, latest.DocWidth, latest.DocHeight));
        output.WriteLine($"expected the clamped window {expected}");

        Assert.Equal(expected, latest.DocViewport);
        Assert.True(latest.Image.Width < latest.DocWidth,
            "a whole-canvas publish with a small viewport still composed the whole document — "
            + "B121's gate went too far and switched B82 off");
    }

    /// <summary>
    /// The hazard B121's fix introduces, guarded rather than reasoned about.
    /// </summary>
    /// <remarks>
    /// Gating culling on a whole-canvas publish means some publishes go around
    /// <c>ComposeRing</c> entirely, leaving its buffers holding an older frame.
    /// The next incremental publish repaints only a dab into one of those
    /// buffers — so if the ring were not told, everything outside the dab would
    /// still be the <em>previous frame's</em> art. That is a stale-pixel bug,
    /// which is wrong quietly.
    /// <para>
    /// <b>What this test does and does not prove.</b> It pins the behaviour — no
    /// ghost of the previous frame — and it would catch a real regression in it.
    /// It does <em>not</em> isolate the <c>ComposeRing.InvalidateAll()</c> call in
    /// the culled branch: it passes with that line deleted. The trace says why —
    /// every <c>EndStroke</c> publishes whole-canvas and marks the other two
    /// buffers <c>NeedsFull</c>, so the rotation after a culled publish lands on a
    /// buffer that repaints in full regardless. Two earlier attempts at this test
    /// are written down rather than deleted, because each was wrong in a way worth
    /// knowing: changing only the viewport moves no pixels in the record, and
    /// leaving a ring buffer never-painted hides the hazard behind a lucky full
    /// repaint.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AnIncrementalPublishAfterACulledOneDoesNotShowThePreviousFrame()
    {
        var vm = PinnedVm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Frame 1 carries a mark on the left. Fitted view, so through the ring.
        vm.BeginStroke(200, 300, 1);
        vm.MoveStroke(300, 300, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Cycle every buffer in ComposeRing so none is left never-painted.
        // Without this the test cannot see the bug: PickBuffer rotates, so the
        // publish after the culled one lands on a virgin buffer, repaints in full
        // and looks correct by luck. A real session has all three warm.
        for (var i = 0; i < 3; i++)
        {
            vm.BeginStroke(200, 330 + i * 20, 1);
            vm.MoveStroke(300, 330 + i * 20, 1);
            vm.EndStroke();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        // Zoom in: from here on, a whole-canvas publish takes the culled path.
        var window = new SKRectI(100, 200, 900, 500);
        vm.SetViewport(window);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // A new, EMPTY frame. This is the whole-canvas publish that goes around
        // the ring while the document's content changes underneath it.
        vm.AddFrameCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Now an incremental publish on the new frame, far from frame 1's mark.
        vm.BeginStroke(700, 400, 1);
        vm.MoveStroke(760, 400, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        var snap = latest!;
        using var bmp = SKBitmap.FromImage(snap.Image);
        Assert.NotNull(bmp);

        var originX = snap.DocViewport?.Left ?? 0;
        var originY = snap.DocViewport?.Top ?? 0;
        var sx = bmp!.Width / (double)(snap.DocViewport?.Width ?? snap.DocWidth);
        var sy = bmp.Height / (double)(snap.DocViewport?.Height ?? snap.DocHeight);

        bool InkAt(double docX, double docY)
        {
            var px = (int)Math.Round((docX - originX) * sx);
            var py = (int)Math.Round((docY - originY) * sy);
            if (px < 0 || py < 0 || px >= bmp.Width || py >= bmp.Height) return false;
            return bmp.GetPixel(px, py).Red < 128;
        }

        var newMark = InkAt(730, 400);
        var ghostOfFrameOne = InkAt(250, 300);
        output.WriteLine($"image {bmp.Width}x{bmp.Height} covering {snap.DocViewport}");
        output.WriteLine($"this frame's mark present: {newMark}   frame 1's mark bleeding through: {ghostOfFrameOne}");

        // Checking the new mark too matters — a blank image has no ghost either,
        // and a probe in this repository once reported full coverage on one.
        Assert.True(newMark, "the mark just painted is missing, so this test proves nothing about the ghost");
        Assert.False(ghostOfFrameOne,
            "the previous frame's art is still on screen: an incremental publish repainted a "
            + "dab into a ComposeRing buffer that had gone stale behind a culled publish");
    }
}
