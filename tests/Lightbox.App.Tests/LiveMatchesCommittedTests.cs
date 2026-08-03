using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The promise the whole brush engine rests on: <b>a stroke looks while you draw it
/// exactly the way it will look when you let go.</b>
/// </summary>
/// <remarks>
/// <para>
/// This is not a new claim — <c>LivePreviewPixelTests</c> already checks it for one case
/// (a self-crossing at half opacity). What it did not check is the case an artist
/// actually hits, which is that <b>a real drag arrives as dozens of separate pointer
/// events</b>, and the live path stamped each batch as if it were a stroke of its own.
/// Every reported symptom — the mark shrinking on release, airbrush and the media losing
/// opacity, the paint load never depleting until commit, the tip texture jumping — is
/// that one fact seen from a different brush.
/// </para>
/// <para>
/// So these tests feed points <b>one event at a time</b>, which is the part that was
/// missing. A two-call stroke restarts the walk once and hides the defect; a
/// forty-call stroke restarts it thirty-nine times and cannot.
/// </para>
/// <para>
/// Numbers are printed rather than only asserted, and side-by-side images are written
/// when <c>LIGHTBOX_BRUSH_IMAGES</c> names a directory — because "the assertion passed"
/// is not the same as "an artist would accept the mark", and this is the class where
/// that difference has cost the most time.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LiveMatchesCommittedTests : BrushStateIsolated
{
    private readonly ITestOutputHelper _out;

    public LiveMatchesCommittedTests(ITestOutputHelper output) => _out = output;

    private const int Width = 420;
    private const int Height = 220;

    private static MainViewModel Vm() => new(null) { SmoothStrokes = false, ColorHex = "#101010" };

    /// <summary>
    /// One drag, fed as <paramref name="events"/> separate pointer events along a path.
    /// </summary>
    /// <returns>The live pixels at the end of the drag, and the committed pixels.</returns>
    private static (SKBitmap Live, SKBitmap Committed) DragAndRelease(
        MainViewModel vm, IReadOnlyList<(double X, double Y, double P)> path)
    {
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(path[0].X, path[0].Y, path[0].P);
        for (var i = 1; i < path.Count; i++)
        {
            // One event per point, which is what a real drag does. Batching them hides
            // exactly the defect these tests exist for.
            vm.MoveStroke(path[i].X, path[i].Y, path[i].P);
        }

        // Let every queued pass land, so the preview is settled rather than caught
        // part-drawn — the artist sees the settled state before they lift.
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var live = SKBitmap.FromImage(latest!.Image);

        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var committed = SKBitmap.FromImage(latest!.Image);
        return (live!, committed!);
    }

    /// <summary>A gentle S, which is what a hand draws and what exercises heading.</summary>
    private static List<(double, double, double)> Curve(int points = 40, double pressure = 1)
    {
        var path = new List<(double, double, double)>();
        for (var i = 0; i < points; i++)
        {
            var t = i / (double)(points - 1);
            var x = 40 + t * 340;
            var y = 110 + Math.Sin(t * Math.PI * 1.5) * 55;
            path.Add((x, y, pressure));
        }
        return path;
    }

    // ---- the measurements ----------------------------------------------------------

    private readonly record struct Difference(
        double MeanAbsolute, double WorstAbsolute, double InkRatio, int LiveInk, int CommittedInk);

    /// <summary>
    /// How far apart two renders are, in terms an artist would recognise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ink coverage is measured as well as the per-pixel difference, and that is the
    /// point.</b> A mean difference tells you the two images are not identical; the ratio
    /// of inked pixels tells you <em>which way</em>, which is the whole of "the stroke
    /// shrank" versus "the stroke darkened". Reporting only the first is how a
    /// discrepancy gets waved through as antialiasing.
    /// </para>
    /// <para>
    /// Both are computed over the union of the two marks rather than the whole canvas —
    /// on a 420×220 canvas an empty border would divide any real difference down to
    /// nothing.
    /// </para>
    /// </remarks>
    private static Difference Compare(SKBitmap live, SKBitmap committed)
    {
        long sum = 0, counted = 0;
        var worst = 0;
        var liveInk = 0;
        var committedInk = 0;

        for (var y = 0; y < live.Height; y++)
        {
            for (var x = 0; x < live.Width; x++)
            {
                var a = live.GetPixel(x, y);
                var b = committed.GetPixel(x, y);

                // The canvas is opaque white under the mark, so "ink" is darkness.
                var aInk = 255 - a.Red;
                var bInk = 255 - b.Red;
                if (aInk > 8) liveInk++;
                if (bInk > 8) committedInk++;
                if (aInk <= 2 && bInk <= 2) continue;

                var d = Math.Abs(aInk - bInk);
                sum += d;
                counted++;
                if (d > worst) worst = d;
            }
        }

        return new Difference(
            counted == 0 ? 0 : sum / (double)counted,
            worst,
            committedInk == 0 ? 0 : liveInk / (double)committedInk,
            liveInk,
            committedInk);
    }

    private void Report(string label, Difference d)
    {
        _out.WriteLine(
            $"{label,-22} mean {d.MeanAbsolute,6:F2}/255  worst {d.WorstAbsolute,3}  "
            + $"ink live {d.LiveInk,6} vs committed {d.CommittedInk,6}  ratio {d.InkRatio,5:F3}");
    }

    /// <summary>
    /// Write live | committed | difference side by side, when asked for.
    /// </summary>
    /// <remarks>
    /// Off unless <c>LIGHTBOX_BRUSH_IMAGES</c> is set, so an ordinary test run writes
    /// nothing. The difference panel is amplified 4× — the failures this hunts are
    /// visible to an artist and near-invisible at 1×, which is precisely why they
    /// survived a passing test suite for so long.
    /// </remarks>
    private static void WriteComparison(string name, SKBitmap live, SKBitmap committed)
    {
        var dir = Environment.GetEnvironmentVariable("LIGHTBOX_BRUSH_IMAGES");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        const int gap = 8;
        var w = live.Width;
        var sheet = new SKBitmap(w * 3 + gap * 2, live.Height);
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(new SKColor(0x20, 0x20, 0x24));
            canvas.DrawBitmap(live, 0, 0);
            canvas.DrawBitmap(committed, w + gap, 0);

            for (var y = 0; y < live.Height; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var d = Math.Abs((255 - live.GetPixel(x, y).Red) - (255 - committed.GetPixel(x, y).Red));
                    var v = (byte)Math.Min(255, d * 4);
                    // Red where live is heavier, blue where the commit is — so the
                    // direction of the error reads at a glance.
                    var heavier = live.GetPixel(x, y).Red < committed.GetPixel(x, y).Red;
                    sheet.SetPixel(
                        (w + gap) * 2 + x, y,
                        heavier ? new SKColor(v, 0, 0, 255) : new SKColor(0, 0, v, 255));
                }
            }
        }

        using var image = SKImage.FromBitmap(sheet);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(dir, name + ".png"));
        data.SaveTo(file);
        sheet.Dispose();
    }

    private Difference Measure(string label, MainViewModel vm, int points = 40)
    {
        vm.NewDocument(new NewDocumentSettings(label, Width, Height, 12, 72, "#ffffff", false));
        var (live, committed) = DragAndRelease(vm, Curve(points));
        try
        {
            var d = Compare(live, committed);
            Report(label, d);
            WriteComparison(label, live, committed);
            return d;
        }
        finally
        {
            live.Dispose();
            committed.Dispose();
        }
    }

    private static void Apply(MainViewModel vm, string presetName)
    {
        var preset = BuiltInPresets.Create().First(p => p.Name == presetName);
        vm.ApplyPreset(preset);
    }

    // ---- the reported brushes, one test each -----------------------------------------

    [AvaloniaFact]
    public void AHardRoundInkStrokeIsTheSameLiveAndCommitted()
    {
        // The user's own baseline: "the hard round ink brush has it the least, but still
        // faintly noticeable". Faint is still wrong, and it is the easiest case — no
        // medium, no texture, no jitter — so anything here is pure walk divergence.
        var vm = Vm();
        Apply(vm, "Ink");

        var d = Measure("ink", vm);

        Assert.True(d.MeanAbsolute < 2.0, $"ink drifted by {d.MeanAbsolute:F2}/255 on release");
        Assert.InRange(d.InkRatio, 0.99, 1.01);
    }

    [AvaloniaFact]
    public void AnAirbrushKeepsItsOpacityWhenThePenLifts()
    {
        // Reported as losing opacity on release. An airbrush is low flow and heavily
        // overlapped, so a duplicated dab per pointer event shows up as darkness far
        // more than it does on ink.
        var vm = Vm();
        Apply(vm, "Airbrush");

        var d = Measure("airbrush", vm);

        Assert.True(d.MeanAbsolute < 3.0, $"airbrush drifted by {d.MeanAbsolute:F2}/255 on release");
        Assert.InRange(d.InkRatio, 0.98, 1.02);
    }

    [AvaloniaFact]
    public void TheLiveStrokeDoesNotShrinkOnRelease()
    {
        // "All around the stroke slightly decreases from the stroke held down." Measured
        // as coverage rather than as a mean, because that is the claim.
        var vm = Vm();
        Apply(vm, "Soft round");

        var d = Measure("soft-round", vm);

        Assert.True(
            d.InkRatio is > 0.985 and < 1.015,
            $"live covered {d.LiveInk} px and the commit {d.CommittedInk} — ratio {d.InkRatio:F3}");
    }

    [AvaloniaFact]
    public void AScatteredJitteredBrushDoesNotReshuffleOnRelease()
    {
        // Stands in for the imported .abr brushes: heavy overlap, scatter and jitter. If
        // the live walk puts dabs anywhere other than where the commit will, every
        // Hash01-seeded dynamic is seeded differently and the texture changes on release.
        var vm = Vm();
        Apply(vm, "Ink");
        vm.BrushScatter = 0.35;
        vm.BrushSizeJitter = 0.5;
        vm.BrushFlow = 0.25;
        vm.BrushSize = 30;

        var d = Measure("scatter-jitter", vm);

        Assert.True(d.MeanAbsolute < 4.0, $"the scattered mark drifted by {d.MeanAbsolute:F2}/255");
        Assert.True(d.WorstAbsolute < 140, $"one pixel moved by {d.WorstAbsolute}/255 — dabs landed elsewhere");
    }

    [AvaloniaTheory]
    [InlineData("Oil")]
    [InlineData("Ink wash")]
    [InlineData("Watercolor")]
    [InlineData("Gouache")]
    public void ASimulatedMediumLooksTheSameLiveAndCommitted(string preset)
    {
        // The worst of the reported cases. Oil especially: the artist tried to fill an
        // area and "only a single stroke survived", because the paint load depletes on
        // commit and never depleted live.
        var vm = Vm();
        Apply(vm, preset);

        var d = Measure(preset.Replace(' ', '-').ToLowerInvariant(), vm);

        Assert.True(d.MeanAbsolute < 6.0, $"{preset} drifted by {d.MeanAbsolute:F2}/255 on release");
        Assert.InRange(d.InkRatio, 0.96, 1.04);
    }

    [AvaloniaFact]
    public void PaintLoadDepletesWhileDrawingAndNotOnlyOnRelease()
    {
        // The mechanism behind the Oil report, isolated: with a load below 1 the mark has
        // to fade along its length *while being drawn*. Measured as a ratio between two
        // places on the same stroke, which is the one measurement the saturation trap
        // cannot fake — and printed both ways round.
        var vm = Vm();
        Apply(vm, "Ink");
        vm.BrushSize = 20;
        vm.MediumPaintLoad = 0.2;
        vm.NewDocument(new NewDocumentSettings("load", Width, Height, 12, 72, "#ffffff", false));

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // A straight line so "along the stroke" is unambiguous.
        vm.BeginStroke(40, 110, 1);
        for (var x = 50; x <= 380; x += 10) vm.MoveStroke(x, 110, 1);
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var live = SKBitmap.FromImage(latest!.Image);
        var startLive = 255 - live!.GetPixel(60, 110).Red;
        var endLive = 255 - live.GetPixel(360, 110).Red;

        vm.EndStroke();
        for (var i = 0; i < 8; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var committed = SKBitmap.FromImage(latest!.Image);
        var startDone = 255 - committed!.GetPixel(60, 110).Red;
        var endDone = 255 - committed.GetPixel(360, 110).Red;

        _out.WriteLine($"live  start {startLive,3} end {endLive,3}");
        _out.WriteLine($"final start {startDone,3} end {endDone,3}");

        // The commit depletes — that is the feature, and the control for this test.
        Assert.True(
            endDone < startDone * 0.75,
            $"the committed stroke did not deplete: {startDone} → {endDone}");

        // And the live preview has to do the same, to within a hair.
        Assert.True(
            endLive < startLive * 0.75,
            $"the live stroke did not deplete: {startLive} → {endLive} — the load resets every event");
        Assert.True(
            Math.Abs(endLive - endDone) < 20,
            $"the far end of the stroke read {endLive} live and {endDone} committed");
    }

    [AvaloniaFact]
    public void ALongStrokeDoesNotDivergeMoreThanAShortOne()
    {
        // The tell that the error accumulates per pointer event rather than being a
        // one-off boundary effect: if it does, a long stroke is worse than a short one,
        // which is exactly what "ink wash grows the longer the stroke" describes.
        var shortVm = Vm();
        Apply(shortVm, "Airbrush");
        var brief = Measure("airbrush-short", shortVm, points: 6);

        var longVm = Vm();
        Apply(longVm, "Airbrush");
        var lengthy = Measure("airbrush-long", longVm, points: 80);

        Assert.True(
            lengthy.MeanAbsolute < Math.Max(1.0, brief.MeanAbsolute * 2),
            $"6 events drifted {brief.MeanAbsolute:F2} and 80 events drifted "
            + $"{lengthy.MeanAbsolute:F2} — the error is per event, so it accumulates");
    }

    [AvaloniaTheory]
    [InlineData(40)]
    [InlineData(200)]
    [InlineData(400)]
    public void SamplingDensityDoesNotChangeWhetherTheMarkMatches(int points)
    {
        // Kept as a theory because sampling density is what localised this bug: below
        // Densify's 2 px chord the incremental render was already exact, and above it the
        // newest span was provisional. A single-density test would have found the symptom
        // and missed the mechanism.
        var vm = Vm();
        Apply(vm, "Ink");
        vm.BrushScatter = 0.35;
        vm.BrushSizeJitter = 0.5;
        vm.BrushFlow = 0.25;
        vm.BrushSize = 30;

        var d = Measure($"density-{points}", vm, points);

        Assert.True(d.MeanAbsolute < 1.0, $"at {points} points the mark drifted {d.MeanAbsolute:F2}/255");
        Assert.InRange(d.InkRatio, 0.995, 1.005);
    }
}
