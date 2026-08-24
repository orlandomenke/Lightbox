using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The ring composes a window onto the document when the artist is zoomed in
/// (B291), and these are the pixels that says nothing moved.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the guard <c>ComposePlanTests</c> handed over.</b> That file used
/// to assert the incremental surface stayed document-sized, on the grounds that
/// "a viewport-sized one would place every dab in the wrong place". The origin
/// is what makes that false — and "the origin is right" is a claim about pixels,
/// not about arithmetic, so it belongs here.
/// </para>
/// <para>
/// Everything is compared against the <b>same strokes composed the old way</b>,
/// with a viewport covering the whole document, rather than against recorded
/// values. A fingerprint would only say the render moved; this says whether the
/// window and the whole document agree, which is the actual invariant.
/// </para>
/// </remarks>
public class WindowedRingPixelTests(ITestOutputHelper output)
{
    private const int DocW = 900, DocH = 600;

    /// <summary>The window an artist zoomed in has: well inside the document.</summary>
    private static SKRectI Window => SKRectI.Create(300, 200, 300, 200);

    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null) { SmoothStrokes = false, ColorHex = "#101010" };
        vm.NewDocument(new NewDocumentSettings(
            "probe", DocW, DocH, 12, 72, Scene.DefaultBackgroundColor, false));
        vm.CanvasQuality = CanvasQuality.Display;
        vm.SetDisplayScale(1.0);
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        return vm;
    }

    /// <summary>
    /// Draw a few dabs and return the last published frame, with the document
    /// rectangle it covers.
    /// </summary>
    /// <summary>
    /// Draw a stroke and capture the frame <b>while the pen is still down</b>,
    /// with the document rectangle it covers.
    /// </summary>
    /// <remarks>
    /// <b>Mid-stroke on purpose, and the first cut of this file got it wrong.</b>
    /// The publish after <c>EndStroke</c> is whole-canvas, which takes the
    /// deferred <c>ViewportCulled</c> route — a different compositor that has
    /// always been culled and does not use the ring's origin at all. Capturing
    /// there made every test here pass with the origin deleted. Only the
    /// incremental publishes during a drag go through the windowed ring.
    /// </remarks>
    private static (SKBitmap Pixels, SKRectI Covers) DrawLive(SKRectI viewport, int events)
    {
        var vm = Vm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.SetViewport(viewport);

        // Inside the window, so both framings can see it.
        vm.BeginStroke(340, 240, 1);
        for (var i = 1; i <= events; i++)
        {
            vm.MoveStroke(340 + i * (180 / events), 240 + i * (120 / events), 1);
        }
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var image = latest!.Materialise(null);
        var bitmap = SKBitmap.FromImage(image);
        Assert.NotNull(bitmap);
        var covers = latest.DocViewport ?? SKRectI.Create(0, 0, DocW, DocH);
        vm.EndStroke();
        return (bitmap!, covers);
    }

    /// <summary>Alpha-ish ink at a DOCUMENT point, whatever the image covers.</summary>
    private static int InkAt(SKBitmap pixels, SKRectI covers, int docX, int docY)
    {
        var x = docX - covers.Left;
        var y = docY - covers.Top;
        if (x < 0 || y < 0 || x >= pixels.Width || y >= pixels.Height) return -1;
        return 255 - pixels.GetPixel(x, y).Red;
    }

    [AvaloniaFact]
    public void AWindowedRingPutsTheMarkWhereTheWholeDocumentDoes()
    {
        var (windowed, wCovers) = DrawLive(Window, events: 12);
        var (whole, dCovers) = DrawLive(SKRectI.Create(0, 0, DocW, DocH), events: 12);
        using var _1 = windowed;
        using var _2 = whole;

        output.WriteLine($"windowed image {windowed.Width}x{windowed.Height} covers {wCovers}");
        output.WriteLine($"whole image    {whole.Width}x{whole.Height} covers {dCovers}");

        // The window really is smaller, or this test is comparing a thing to itself.
        Assert.True(
            (long)windowed.Width * windowed.Height < (long)whole.Width * whole.Height,
            "the windowed publish was not actually windowed");

        // Every document point inside the window has to agree, on and off the mark.
        var compared = 0;
        var onMark = 0;
        for (var y = Window.Top + 2; y < Window.Bottom - 2; y += 7)
        for (var x = Window.Left + 2; x < Window.Right - 2; x += 7)
        {
            int a = InkAt(windowed, wCovers, x, y), b = InkAt(whole, dCovers, x, y);
            Assert.True(a >= 0 && b >= 0, $"({x},{y}) fell outside an image");
            Assert.True(
                Math.Abs(a - b) <= 2,
                $"({x},{y}): windowed reads {a} ink, whole document reads {b}");
            compared++;
            if (b > 8) onMark++;
        }
        output.WriteLine($"{compared} document points agree, {onMark} of them on the mark");
        Assert.True(onMark > 40, $"only {onMark} sampled points landed on the mark — nothing is being compared");
    }

    [AvaloniaFact]
    public void PanningReframesRatherThanPatchingTheOldWindow()
    {
        // The stale-pixel hazard: a buffer's dirty regions were recorded against
        // the window it held, so a publish under a new origin cannot patch it.
        // Without a re-frame the second stroke would land shifted by the pan, or
        // the first would still be showing at its old offset.
        var vm = Vm();
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.SetViewport(Window);
        vm.BeginStroke(340, 240, 1);
        for (var i = 1; i <= 8; i++) vm.MoveStroke(340 + i * 15, 240 + i * 10, 1);
        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // Pan: same surface size, different origin — the case Reset keeps its
        // buffers for and marks NeedsFull.
        var moved = SKRectI.Create(Window.Left + 120, Window.Top + 80, Window.Width, Window.Height);
        vm.SetViewport(moved);
        vm.BeginStroke(480, 300, 1);
        for (var i = 1; i <= 8; i++) vm.MoveStroke(480 + i * 10, 300 + i * 8, 1);
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        using var afterImage = latest!.Materialise(null);
        using var after = SKBitmap.FromImage(afterImage);
        Assert.NotNull(after);
        var covers = latest.DocViewport ?? SKRectI.Create(0, 0, DocW, DocH);
        vm.EndStroke();
        output.WriteLine($"after the pan, image {after!.Width}x{after.Height} covers {covers}");
        Assert.Equal(moved, covers);

        // The same two strokes, drawn from scratch at the panned framing.
        var reference = Vm();
        RenderSnapshot? refLatest = null;
        reference.SnapshotChanged += s => refLatest = s;
        reference.SetViewport(moved);
        reference.BeginStroke(340, 240, 1);
        for (var i = 1; i <= 8; i++) reference.MoveStroke(340 + i * 15, 240 + i * 10, 1);
        reference.EndStroke();
        reference.BeginStroke(480, 300, 1);
        for (var i = 1; i <= 8; i++) reference.MoveStroke(480 + i * 10, 300 + i * 8, 1);
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(refLatest);
        using var expectedImage = refLatest!.Materialise(null);
        using var expected = SKBitmap.FromImage(expectedImage);
        Assert.NotNull(expected);
        var refCovers = refLatest.DocViewport ?? SKRectI.Create(0, 0, DocW, DocH);
        reference.EndStroke();

        int worst = 0, compared = 0, onMark = 0;
        var at = (0, 0);
        for (var y = moved.Top + 2; y < moved.Bottom - 2; y += 5)
        for (var x = moved.Left + 2; x < moved.Right - 2; x += 5)
        {
            int a = InkAt(after, covers, x, y), b = InkAt(expected!, refCovers, x, y);
            if (a < 0 || b < 0) continue;
            compared++;
            if (b > 8) onMark++;
            if (Math.Abs(a - b) > worst) { worst = Math.Abs(a - b); at = (x, y); }
        }
        output.WriteLine($"{compared} points compared, {onMark} on the marks; worst {worst} ink at {at}");
        Assert.True(onMark > 20, $"only {onMark} points landed on ink — nothing is being compared");
        Assert.True(worst <= 2, $"the panned frame differs by {worst} ink at {at} — a stale window");
    }

    /// <summary>
    /// Re-framing the ring is a full repaint, not a patch.
    /// </summary>
    /// <remarks>
    /// <b>Asserted on the ring directly, because the pipeline cannot reach it.</b>
    /// Every stroke ends with a whole-canvas publish that marks all three buffers
    /// NeedsFull, so by the time an artist has panned, a full repaint was going to
    /// happen regardless — which is the same reason the <c>InvalidateAll</c> beside
    /// the culled route is documented as unproven defence. Driving <c>ComposeRing</c>
    /// itself is the only way to see the origin actually being load-bearing: patch
    /// under one origin, then publish a dirty region under another, and the clip
    /// handed to the paint callback has to be null.
    /// </remarks>
    [Fact]
    public void ChangingTheRingsOriginForcesAFullRepaint()
    {
        using var ring = new ComposeRing();
        var info = new SKImageInfo(300, 200, SKColorType.Rgba8888, SKAlphaType.Premul);
        var dab = SKRectI.Create(340, 240, 20, 20);
        var clips = new List<SKRectI?>();

        void Publish(SKRectI? dirty, SKPointI origin) =>
            ring.Publish(info, dirty, (_, clip) => clips.Add(clip), 1.0, null, origin).Dispose();

        var first = new SKPointI(300, 200);
        Publish(null, first);   // whole-canvas: establishes the framing

        // Warm the ring. A buffer that has never held pixels repaints in full
        // whatever it is asked for, so the first few patches after a whole-canvas
        // publish are full ones by design — there are three buffers to fill.
        for (var i = 0; i < 5; i++) Publish(dab, first);
        var warm = clips.Count - 1;
        output.WriteLine($"clips: {string.Join(", ", clips.Select(c => c?.ToString() ?? "null (full)"))}");
        Assert.NotNull(clips[warm]);   // patching, now that the buffers hold pixels

        Publish(dab, new SKPointI(420, 280)); // the same patch after a pan
        output.WriteLine($"after the pan: {clips[^1]?.ToString() ?? "null (full)"}");
        Assert.Null(clips[^1]);        // re-framed, so nothing in them can be patched

        // And it goes back to patching once the new framing is established.
        for (var i = 0; i < 5; i++) Publish(dab, new SKPointI(420, 280));
        Assert.NotNull(clips[^1]);
    }

    [AvaloniaFact]
    public void AWindowedRingPatchesRatherThanRepaintingEverything()
    {
        // The point of doing this in the ring at all: the surface shrinks to the
        // window AND the publish still honours a dirty region. If it had to fill
        // the whole window every event this would be B121's 109x all over again.
        var plan = ComposePlan.For(
            DocW, DocH, null, Window, SKRectI.Create(340, 240, 20, 20), tileNative: false, 1.0);

        output.WriteLine($"route {plan.Route}, surface {plan.Info.Width}x{plan.Info.Height}, origin {plan.Origin}");
        Assert.Equal(ComposeRoute.Ring, plan.Route);
        Assert.Equal(Window.Width, plan.Info.Width);
        // CullRect is what forces a fresh fill-everything surface; the ring must
        // not have one.
        Assert.Null(plan.CullRect);
    }
}
