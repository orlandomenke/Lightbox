using Avalonia;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.Testing;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The cursor must land where the mark lands, at every zoom, pan and rotation,
/// and whether or not the compositor culled its work to the viewport.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this file exists.</b> <c>CanvasViewTests</c> already covers the view
/// transform, and every one of its cases publishes a snapshot with
/// <c>DocViewport = null</c>. B82 made the view model report a viewport on
/// <em>every</em> document, so the whole suite stayed green while the pointer
/// mapping was wrong in the running application — the tests were pinning the
/// one case the app had stopped taking.
/// </para>
/// <para>
/// So every test here publishes a snapshot <b>with</b> a viewport, wired the way
/// <c>MainWindow</c> wires it: the canvas reports its visible rectangle, and the
/// next publish hands that rectangle straight back. The invariant is the one an
/// artist can see — the document point under the pointer must not depend on
/// whether the compositor culled.
/// </para>
/// </remarks>
public class CursorAlignmentTests(ITestOutputHelper output)
{
    private const int DocW = 960;
    private const int DocH = 540;

    /// <summary>
    /// A canvas plus the viewport it last reported — the pair `MainWindow` keeps
    /// by subscribing `ViewportChanged` to `MainViewModel.SetViewport`.
    /// </summary>
    private sealed class Rig
    {
        public CanvasControl Canvas { get; } = new();
        public SKRectI? Reported;
        public bool Culled;

        public Rig(bool culled)
        {
            Culled = culled;
            Canvas.ViewportChanged += vp => Reported = vp;
            Canvas.Measure(new Size(800, 600));
            Canvas.Arrange(new Rect(0, 0, 800, 600));
            Publish();
        }

        /// <summary>
        /// Publish a frame the way <c>MainViewModel.PublishSnapshot</c> does: the
        /// reported document size is the document's, the image is whatever the
        /// compositor produced, and the viewport rides along when culling.
        /// </summary>
        public void Publish()
        {
            var vp = Culled ? Reported : null;
            var w = vp is { } v ? Math.Clamp(v.Width, 1, 8192) : DocW;
            var h = vp is { } v2 ? Math.Clamp(v2.Height, 1, 8192) : DocH;
            using var surface = SKSurface.Create(new SKImageInfo(w, h));
            Canvas.UpdateSnapshot(new RenderSnapshot(surface.Snapshot(), DocW, DocH, ++_seq, vp));
        }

        public void Pan(double x, double y)
        {
            var f = Canvas.Framing;
            Canvas.Framing = f with { PanX = f.PanX + x, PanY = f.PanY + y };
        }

        public void Frame(double rotationDeg, bool mirrored)
        {
            Canvas.Framing = Canvas.Framing with { RotationDeg = rotationDeg, Mirrored = mirrored };
        }

        private long _seq;
    }

    /// <summary>
    /// The heart of it: culling is a rendering decision, so it must not move a
    /// single document coordinate. Same gesture, both paths, same answer.
    /// </summary>
    /// <remarks>
    /// <b>Rotation and mirror are in the matrix on purpose, and were the gap in
    /// the first version of this file.</b> The offending term lived in
    /// <c>ViewMatrix</c>'s <em>final translation</em>, which is the one factor
    /// rotation reorders — so a viewport term that happened to look right at
    /// zoom and pan can still be wrong the moment the canvas is turned. A theory
    /// that only sweeps zoom and pan would pass on that, which is the same shape
    /// of hole that let the original bug ship.
    /// </remarks>
    [AvaloniaTheory]
    // zoom,  panX, panY, rotationDeg, mirrored
    [InlineData(1.0, 0, 0, 0, false)]
    [InlineData(2.0, 0, 0, 0, false)]
    [InlineData(0.5, 0, 0, 0, false)]
    [InlineData(1.0, 120, -80, 0, false)]
    [InlineData(3.0, 200, 150, 0, false)]
    [InlineData(0.25, -300, 90, 0, false)]
    // …and the same again turned and flipped, where a translation-order error hides.
    [InlineData(1.0, 0, 0, 90, false)]
    [InlineData(1.0, 0, 0, 37, false)]
    [InlineData(2.0, 140, -60, 17, false)]
    [InlineData(0.5, -220, 110, -45, false)]
    [InlineData(1.0, 0, 0, 0, true)]
    [InlineData(2.5, 90, 70, 0, true)]
    [InlineData(1.5, -110, 40, 23, true)]
    public void CullingNeverMovesTheDocumentPointUnderThePointer(
        double zoom, double panX, double panY, double rotationDeg, bool mirrored)
    {
        var probes = new[]
        {
            new Point(400, 300),  // the centre
            new Point(0, 0),      // and the corners, where an offset shows worst
            new Point(799, 0),
            new Point(799, 599),
            new Point(0, 599),
            new Point(137, 421),
        };

        var plain = new Rig(culled: false);
        var culled = new Rig(culled: true);

        foreach (var rig in new[] { plain, culled })
        {
            rig.Canvas.ZoomAt(new Point(400, 300), zoom);
            rig.Pan(panX, panY);
            rig.Frame(rotationDeg, mirrored);
            rig.Publish();   // the app republishes after every view change
        }

        var worst = 0.0;
        foreach (var p in probes)
        {
            var (ax, ay) = plain.Canvas.ViewToDoc(p);
            var (bx, by) = culled.Canvas.ViewToDoc(p);
            var err = Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
            if (err > worst) worst = err;
            output.WriteLine(
                $"view {p.X,4},{p.Y,4}  plain {ax,9:F2},{ay,9:F2}   culled {bx,9:F2},{by,9:F2}   off {err:F2}");

            // And the other direction, because ViewToDoc agreeing does not prove
            // DocToView does — they are separate call sites on the same matrix.
            var (fax, fay) = plain.Canvas.DocToView(ax, ay);
            var (fbx, fby) = culled.Canvas.DocToView(ax, ay);
            worst = Math.Max(worst, Math.Max(Math.Abs(fax - fbx), Math.Abs(fay - fby)));
        }

        output.WriteLine($"viewport reported: {culled.Reported}");
        output.WriteLine($"worst offset: {worst:F2} document units");
        Assert.True(worst < 0.5,
            $"culling moved the pointer by up to {worst:F2} document units — "
            + "the cursor no longer sits on the mark it makes");
    }

    /// <summary>
    /// A round trip through the transform is the property a stroke depends on:
    /// the point the pointer reports is the point the mark is stamped at, so if
    /// doc→view→doc drifts, the line leaves the cursor.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(2.5)]
    [InlineData(0.3)]
    public void AViewportCulledCanvasRoundTripsADocumentPoint(double zoom)
    {
        var rig = new Rig(culled: true);
        rig.Canvas.ZoomAt(new Point(400, 300), zoom);
        rig.Publish();

        foreach (var (dx, dy) in new[] { (0.0, 0.0), (480.0, 270.0), (959.0, 539.0), (12.0, 500.0) })
        {
            var (vx, vy) = rig.Canvas.DocToView(dx, dy);
            var (rx, ry) = rig.Canvas.ViewToDoc(new Point(vx, vy));
            output.WriteLine($"doc {dx,7:F1},{dy,7:F1} → view {vx,8:F2},{vy,8:F2} → doc {rx,7:F2},{ry,7:F2}");
            Assert.Equal(dx, rx, 3);
            Assert.Equal(dy, ry, 3);
        }
    }

    /// <summary>
    /// End to end, and the only test here an artist would recognise: put the
    /// pointer somewhere, paint, and check the ink came out under the pointer.
    /// </summary>
    /// <remarks>
    /// Everything above measures the transform. This measures the <em>whole</em>
    /// path — pointer → <see cref="CanvasControl.ViewToDoc"/> → the real brush
    /// engine → the published frame — with the canvas and the view model wired
    /// to each other exactly as <c>MainWindow</c> wires them, so viewport
    /// culling is live while the stroke is made. It is the check that would have
    /// caught this from the beginning.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void InkLandsUnderThePointer_WithCullingLive(double zoom)
    {
        var vm = new ViewModels.MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#000000",
            BrushSize = 24,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
            BrushWetEdge = 0,
            BrushGranulation = 0,
            BrushScatter = 0,
        };

        var canvas = new CanvasControl();
        RenderSnapshot? latest = null;
        // The two lines MainWindow.axaml.cs has: the canvas reports what it can
        // see, and the view model publishes for it.
        canvas.ViewportChanged += vp => vm.SetViewport(vp);
        vm.SnapshotChanged += s => latest = s;

        canvas.Measure(new Size(800, 600));
        canvas.Arrange(new Rect(0, 0, 800, 600));
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        canvas.UpdateSnapshot(latest!);

        canvas.ZoomAt(new Point(400, 300), zoom);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (latest is not null) canvas.UpdateSnapshot(latest);

        // Aim at a point deliberately off-centre, so an offset cannot hide.
        var aim = new Point(300, 380);
        var (docX, docY) = canvas.ViewToDoc(aim);

        vm.BeginStroke(docX, docY, 1);
        vm.MoveStroke(docX, docY, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.NotNull(latest);
        var snap = latest!;
        // B125 stage 3b: the culled route hands over a description of the
        // composite rather than the pixels, so it is performed here — on the
        // CPU, since a test has no lease to take a GRContext from.
        using var composed = snap.Materialise(null);
        using var bmp = SKBitmap.FromImage(composed);
        Assert.NotNull(bmp);

        // Where did the ink actually land? Centroid of everything dark.
        double sumX = 0, sumY = 0;
        var n = 0;
        for (var y = 0; y < bmp!.Height; y++)
        for (var x = 0; x < bmp.Width; x++)
        {
            if (bmp.GetPixel(x, y).Red >= 100) continue;
            sumX += x; sumY += y; n++;
        }

        output.WriteLine($"zoom {zoom}  viewport {snap.DocViewport}  image {bmp.Width}×{bmp.Height}  dark px {n}");
        Assert.True(n > 0, "no ink in the published frame at all — the dab never landed");

        // Image pixels back into document space. A culled image covers its
        // viewport rectangle rather than the whole document, which is the one
        // thing the reader has to account for.
        var originX = snap.DocViewport?.Left ?? 0;
        var originY = snap.DocViewport?.Top ?? 0;
        var scaleX = (snap.DocViewport?.Width ?? snap.DocWidth) / (double)bmp.Width;
        var scaleY = (snap.DocViewport?.Height ?? snap.DocHeight) / (double)bmp.Height;
        var inkX = originX + sumX / n * scaleX;
        var inkY = originY + sumY / n * scaleY;

        var offset = Math.Sqrt((inkX - docX) * (inkX - docX) + (inkY - docY) * (inkY - docY));
        output.WriteLine($"pointer at doc {docX:F2},{docY:F2}   ink at doc {inkX:F2},{inkY:F2}   offset {offset:F2}");

        // One document pixel of slack for the centroid of a round dab.
        Assert.True(offset < 2.0,
            $"the mark landed {offset:F2} document units from the pointer that made it");
    }

    /// <summary>
    /// "Zooming displaces the layer": the art a culled compose produces must sit
    /// exactly where the uncalled one put it.
    /// </summary>
    /// <remarks>
    /// Asserted on the pixels and also written as a contact sheet, because this
    /// is the half of the bug a person sees rather than measures. Set
    /// <c>LIGHTBOX_VISUALS</c> to a directory to get
    /// <c>cull-vs-full-compose.png</c>; the difference panel is flat mid-grey
    /// when the two agree.
    /// </remarks>
    [AvaloniaFact]
    public void ACulledComposePutsTheArtWhereAFullComposeDoes()
    {
        var vm = new ViewModels.MainViewModel(null)
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

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // A shape with corners, so a shift of even a pixel is legible.
        vm.BeginStroke(300, 180, 1);
        vm.MoveStroke(660, 180, 1);
        vm.MoveStroke(660, 360, 1);
        vm.MoveStroke(300, 360, 1);
        vm.MoveStroke(300, 180, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(latest);

        // No viewport: the whole document, the path the app has always taken.
        vm.SetViewport(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var fullImage = latest!.Materialise(null);
        using var full = SKBitmap.FromImage(fullImage);
        var fullCovers = latest!.DocViewport;

        // A viewport strictly inside the document, so culling actually engages.
        var window = new SKRectI(200, 120, 760, 420);
        vm.SetViewport(window);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var culledImage = latest!.Materialise(null);
        using var culled = SKBitmap.FromImage(culledImage);
        var culledCovers = latest!.DocViewport;

        output.WriteLine($"full   image {full!.Width}×{full.Height}  covers {fullCovers}");
        output.WriteLine($"culled image {culled!.Width}×{culled.Height}  covers {culledCovers}");

        Assert.Null(fullCovers);
        Assert.Equal(window, culledCovers);
        Assert.True(culled.Width < full.Width, "culling did not actually shrink the surface");

        // The same document region out of each, so they can be compared directly.
        using var fullCrop = VisualSheet.Zoom(full, window, 1);
        using var culledCrop = VisualSheet.Zoom(culled, new SKRectI(0, 0, culled.Width, culled.Height), 1);

        VisualSheet.WriteComparison(
            "cull-vs-full-compose",
            "B82 — the same square, composed full-document and culled to {200,120,760,420}. "
            + "Flat grey difference means culling moved nothing.",
            "full compose, cropped to the window", VisualSheet.OverPaper(fullCrop),
            "culled compose", VisualSheet.OverPaper(culledCrop));

        // And the measurement the sheet is beside: same ink, same places.
        var w = Math.Min(fullCrop.Width, culledCrop.Width);
        var h = Math.Min(fullCrop.Height, culledCrop.Height);
        long differing = 0;
        var worst = 0;
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var d = Math.Abs(fullCrop.GetPixel(x, y).Red - culledCrop.GetPixel(x, y).Red);
            if (d <= 2) continue;
            differing++;
            if (d > worst) worst = d;
        }

        var share = 100.0 * differing / (w * h);
        output.WriteLine($"compared {w}×{h}: {differing} pixels differ by more than 2 ({share:F3}%), worst {worst}");
        Assert.True(share < 0.5,
            $"culling moved the art: {share:F2}% of pixels differ, worst by {worst}/255");
    }

    /// <summary>
    /// Zooming must not walk the document under the pointer. This is the
    /// reported symptom — "zooming in and out displaces the layer" — and it is
    /// exactly what a feedback loop between the matrix and the viewport derived
    /// from it produces: each publish shifts the matrix that produced it.
    /// </summary>
    [AvaloniaFact]
    public void RepeatedZoomingDoesNotDriftTheAnchor()
    {
        var rig = new Rig(culled: true);
        var anchor = new Point(400, 300);
        var (startX, startY) = rig.Canvas.ViewToDoc(anchor);
        output.WriteLine($"start   {startX:F3}, {startY:F3}");

        // In and back out, ten times, republishing each step the way the app does.
        for (var i = 0; i < 10; i++)
        {
            rig.Canvas.ZoomAt(anchor, 1.15);
            rig.Publish();
            rig.Canvas.ZoomAt(anchor, 1 / 1.15);
            rig.Publish();
        }

        var (endX, endY) = rig.Canvas.ViewToDoc(anchor);
        var drift = Math.Max(Math.Abs(endX - startX), Math.Abs(endY - startY));
        output.WriteLine($"end     {endX:F3}, {endY:F3}   drift {drift:F3}");
        Assert.True(drift < 0.5,
            $"the point under the pointer walked {drift:F2} document units over ten zoom round trips");
    }
}
