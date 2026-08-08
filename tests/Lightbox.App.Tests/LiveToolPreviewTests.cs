using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Brushwork must look, while you are making it, the way it will look when you
/// let go. Anything that only resolves on pen-up is guesswork — the rule the
/// wet-media work established, applied to the tools that still broke it.
/// </summary>
[Collection("BrushState")]
public class LiveToolPreviewTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Painted()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 40;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.AntiAliasing = false;

        // A hard black bar to smear or blur.
        vm.BeginStroke(60, 100, 1);
        vm.MoveStroke(300, 100, 1);
        vm.EndStroke();
        return vm;
    }

    /// <summary>Select a built-in preset by name — how the brush kind is chosen in the app.</summary>
    private static void Pick(MainViewModel vm, string preset)
    {
        vm.SelectedBrushPreset = vm.BrushPresetChoices.First(p => p.Name == preset);
        vm.BrushSize = 40;
        vm.BrushHardness = 1;
        vm.BrushFlow = 1;
        vm.BrushSpacing = 0.08;
    }

    private static SKColor PixelAt(RenderSnapshot snapshot, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        return bmp.GetPixel(x, y);
    }

    /// <summary>B4 — the smear has to appear during the drag, not on release.</summary>
    [AvaloniaFact]
    public void SmudgeShowsMidDrag()
    {
        var vm = Painted();
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.9;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        using (var before = SKBitmap.FromImage(latest!.Image))
        {
            // Empty just under the bar, which is where the smear will drag ink.
            Assert.Equal(0, before.GetPixel(180, 150).Alpha);
        }

        // Drag downward out of the bar and STOP — no EndStroke.
        vm.BeginStroke(180, 100, 1);
        vm.MoveStroke(180, 125, 1);
        vm.MoveStroke(180, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // flush the coalesced publish

        using var during = SKBitmap.FromImage(latest!.Image);
        Assert.True(during!.GetPixel(180, 150).Alpha > 0,
            "the smudge did not reach the canvas until the pen lifted");
    }

    /// <summary>B4 — and what it showed is what the commit produces.</summary>
    [AvaloniaFact]
    public void TheSmudgePreviewMatchesTheCommit()
    {
        var vm = Painted();
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.9;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(180, 100, 1);
        vm.MoveStroke(180, 125, 1);
        vm.MoveStroke(180, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var preview = PixelAt(latest!, 180, 140);

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var committed = PixelAt(latest!, 180, 140);

        // Both inked, and close enough that the artist was not being shown a
        // different mark from the one they were making.
        Assert.True(preview.Alpha > 0 && committed.Alpha > 0);
        Assert.InRange(Math.Abs(preview.Alpha - committed.Alpha), 0, 12);
    }

    /// <summary>B4 — blur is the same class of tool and the same failure.</summary>
    [AvaloniaFact]
    public void BlurShowsMidDrag()
    {
        var vm = Painted();
        Pick(vm, "Blur");

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        // The bar's edge is hard: one row is opaque, the row below is empty.
        using (var before = SKBitmap.FromImage(latest!.Image))
        {
            Assert.Equal(0, before.GetPixel(180, 128).Alpha);
        }

        vm.BeginStroke(180, 118, 1);
        vm.MoveStroke(182, 120, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var during = SKBitmap.FromImage(latest!.Image);
        Assert.True(during!.GetPixel(180, 128).Alpha > 0,
            "blur did not soften the edge until the pen lifted");
    }

    /// <summary>
    /// A smudge abandoned mid-drag must leave nothing behind.
    ///
    /// Showing the composite instead of the layer is a new way to be wrong:
    /// a stale copy left on screen after the stroke went away would look like
    /// paint the record does not contain. Playback is the real path that
    /// abandons a stroke, so it is what this drives.
    /// </summary>
    [AvaloniaFact]
    public void AnAbandonedSmudgeLeavesNoTrace()
    {
        var vm = Painted();
        var strokes = ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count;
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.9;

        vm.BeginStroke(180, 100, 1);
        vm.MoveStroke(180, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.PlayCommand.Execute(null);  // abandons the stroke
        vm.PauseCommand.Execute(null);

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();

        Assert.Equal(strokes, ((Frame)vm.PaintLayer().Cels[0].Frame!).Strokes.Count);
        Assert.Equal(0, PixelAt(latest!, 180, 150).Alpha);
    }

    // ---- B33 · the live blur covered more ground than the commit ---------------

    /// <summary>Pixels with any ink in them.</summary>
    /// <remarks>
    /// Coordinate-free on purpose. Two earlier probes measured the wrong thing
    /// — one walked sideways along the opaque bar and measured the bar, the
    /// other dragged along the bar's middle where blurring uniform black is a
    /// no-op by construction. Both passed against the unfixed code. Counting
    /// covered pixels asks the artist's question directly: how much ground does
    /// the mark cover?
    /// </remarks>
    private static int InkedPixels(RenderSnapshot snapshot)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        var n = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).Alpha > 8) n++;
        return n;
    }

    /// <summary>
    /// A long blur drag must not cover visibly more ground on screen than the
    /// mark that commits.
    /// </summary>
    /// <remarks>
    /// The artist's report was that the affected area is bigger than the brush
    /// while dragging. It was: the live composite was handed to the engine as
    /// both the surface to write and the pixels to read, so each pointer event
    /// blurred the previous event's blur. The exact render gives every dab of a
    /// stroke the same pre-stroke pixels — one pass per stroke, not one per
    /// event — and N passes of sigma s reach like sigma*sqrt(N).
    ///
    /// Measured over 60 events on a 40 px brush at flow 0.6: the preview
    /// covered **672 px more** than the commit before the pristine-base fix,
    /// **88 px more** after it, and **-2 px** — noise — once B37 stopped the
    /// blur compositing a second copy of its snapshot over the first. Both
    /// were the same accumulation seen from different ends.
    ///
    /// The threshold is 20 rather than 200 for that reason. It was loosened to
    /// 200 to accommodate a residual that has since turned out not to exist,
    /// and a threshold with 200 px of slack in it would not notice either bug
    /// coming back.
    ///
    /// Note the drag runs along the bar's EDGE. Along its middle there is no
    /// gradient and blur is a no-op, which is how two earlier versions of this
    /// test managed to pass against the bug.
    /// </remarks>
    [AvaloniaFact]
    public void ALiveBlurDoesNotCoverMoreGroundThanTheMarkThatCommits()
    {
        var vm = Painted();
        Pick(vm, "Blur");
        vm.BrushFlow = 0.6;   // a wide sigma, so any extra pass would be unmistakable

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginStroke(120, 118, 1);
        for (var x = 122; x <= 240; x += 2) vm.MoveStroke(x, 118, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var preview = InkedPixels(latest!);

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var committed = InkedPixels(latest!);

        // Both numbers, always. An assertion that passes tells you nothing
        // about how close it came.
        Assert.True(
            preview - committed <= 20,
            $"the live blur covered {preview} px against the commit's {committed} "
            + $"({preview - committed} more) — the preview is applying the blur more times "
            + "than the commit does");
    }

    /// <summary>
    /// The pulling brushes ship with a low flow, because flow on those is how hard each dab
    /// drags and the dabs overlap ten deep.
    /// </summary>
    /// <remarks>
    /// Guarded rather than merely set: a default nudged back up would be invisible in every
    /// other test, and it is the one number that decides whether these tools are steerable
    /// at all.
    /// <para>
    /// <b>Blur is deliberately not in this list any more</b> — see the test below. It was,
    /// and that is how it came to ship at a flow that made it do nothing.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ThePullingBrushesShipWithAFlowAnArtistCanSteer()
    {
        var vm = VmLayers.BareVm();
        foreach (var name in new[] { "Smudge", "Blender" })
        {
            var preset = vm.BrushPresetChoices.First(p => p.Name == name);
            Assert.True(
                preset.Settings.Flow <= 0.1,
                $"{name} ships with flow {preset.Settings.Flow:F2}; a pulling brush needs 0.1 or less");
            Assert.True(preset.Settings.Flow > 0, $"{name} ships with no flow at all");
        }
    }

    /// <summary>
    /// Blur's flow is a radius, not a pull, and it does not compound — so the bound runs the
    /// other way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// B49. This test used to hold Blur to the same ceiling as smudge and the blender, on the
    /// reasoning that flow on an effect brush compounds across overlapping dabs.
    /// <b>Blur is the one that does not.</b> <c>StampBlur</c> snapshots the layer once before
    /// the stroke and draws that same snapshot per dab with <c>SKBlendMode.Src</c> — the B37
    /// fix, deliberately — so every dab replaces its circle with a single pass and the
    /// softness an artist gets is the softness of one dab, however long the stroke.
    /// </para>
    /// <para>
    /// Stated as a sigma rather than a flow, because sigma is what a person sees: the flow
    /// number means nothing without the size beside it, and a bound on the flow alone would
    /// pass on a 4 px brush that softens by a tenth of a pixel.
    /// </para>
    /// </remarks>
    // ---- B39 · effect brushes on a partially-transparent wash -------------

    /// <summary>
    /// A soft, partially-transparent wash — not a hard opaque bar. Every
    /// discriminating case for B39 needs this: an opaque source makes opaque
    /// output correct, so <see cref="Painted"/>'s hard bar cannot show the
    /// failure the report described, and neither can a test built on it —
    /// which is exactly what every pre-existing App-level effect-brush test
    /// was built on.
    /// </summary>
    private static MainViewModel WashPainted(out byte peakAlpha)
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 200;
        vm.BrushHardness = 0.3;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 0.24;
        vm.BrushWetEdge = 0;
        vm.BrushGranulation = 0;
        vm.BrushScatter = 0;
        vm.AntiAliasing = true;

        // One soft dab: a radial wash with a gradient edge, not a bar.
        vm.BeginStroke(300, 270, 1);
        vm.EndStroke();

        RenderSnapshot? snap = null;
        vm.SnapshotChanged += s => snap = s;
        vm.PublishSnapshot();
        using var bmp = SKBitmap.FromImage(snap!.Image);
        peakAlpha = PeakAlpha(bmp);
        return vm;
    }

    private static byte PeakAlpha(SKBitmap bmp)
    {
        byte peak = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var a = bmp.GetPixel(x, y).Alpha;
                if (a > peak) peak = a;
            }
        return peak;
    }

    private static (byte Peak, int Opaque) Survey(SKBitmap bmp)
    {
        byte peak = 0;
        var opaque = 0;
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
            {
                var a = bmp.GetPixel(x, y).Alpha;
                if (a > peak) peak = a;
                if (a > 250) opaque++;
            }
        return (peak, opaque);
    }

    /// <summary>
    /// B39: dragging an effect brush across a soft wash must never produce a
    /// pixel more opaque than the wash itself — a smudge or blur moves
    /// colour, it does not manufacture alpha. Driven through the real
    /// live-preview pipeline (<c>_liveComposite</c>,
    /// <c>FrameRasterizer.AppendDraft</c>, the publish path) rather than
    /// <c>BrushEngine.StampStroke</c> directly: <c>EffectArtefactTests</c>
    /// already proved the engine clean in isolation at this exact
    /// configuration, so this is the layer above it — the one B39's own notes
    /// point at next — and not a restatement of that coverage.
    /// </summary>
    [AvaloniaFact]
    public void AnEffectBrushMidDrag_CannotExceedTheWashItStartedFrom()
    {
        var vm = WashPainted(out var peakAlpha);
        Assert.True(peakAlpha is > 20 and < 200, $"the wash fixture itself is not soft ({peakAlpha})");
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.6;
        vm.BrushSmudgeRadius = 0.6;

        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // Diagonally across the wash's edge and out into bare canvas — the
        // arrangement EffectArtefactTests found the bug in, driven as a real
        // drag: many pointer events, none of them ending the stroke.
        vm.BeginStroke(260, 230, 1);
        for (var i = 1; i <= 40; i++) vm.MoveStroke(260 + i * 3.0, 230 + i * 2.0, 0.8);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var during = SKBitmap.FromImage(latest!.Image);
        var (peak, opaque) = Survey(during);
        output.WriteLine($"mid-drag: peak {peak} (wash {peakAlpha}), {opaque} px opaque");
        Assert.True(opaque == 0,
            $"the live smudge left {opaque} opaque pixels mid-drag on a wash whose peak is {peakAlpha}");
        Assert.True(peak <= peakAlpha + 4,
            $"the live smudge reached alpha {peak} from a wash of {peakAlpha}");

        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        using var committed = SKBitmap.FromImage(latest!.Image);
        var (peakCommitted, opaqueCommitted) = Survey(committed);
        output.WriteLine($"committed: peak {peakCommitted} (wash {peakAlpha}), {opaqueCommitted} px opaque");
        Assert.True(opaqueCommitted == 0,
            $"the committed smudge left {opaqueCommitted} opaque pixels — the artefact survived release");
        Assert.True(peakCommitted <= peakAlpha + 4,
            $"the committed smudge reached alpha {peakCommitted} from a wash of {peakAlpha}");
    }

    /// <summary>
    /// B39 follow-up, found by an adversarial review of the fix above rather
    /// than by a report: abandoning a Blur/Smudge stroke without
    /// <see cref="MainViewModel.EndStroke"/> — starting playback mid-drag, or
    /// switching documents mid-drag — used to leave <c>_liveComposite</c>
    /// non-null forever, because only <c>BeginStroke</c> and
    /// <c>EndStroke</c> cleared it and <c>_strokeBuilder.Cancel()</c> knows
    /// nothing about it. After the fix above, a stale non-null
    /// <c>_liveComposite</c> silently suppresses the overlay for every
    /// ordinary stroke, gradient or shape drag that follows — on any
    /// document — until ink happens to reset it. `ClearLiveEffectState`
    /// closes that: every place that abandons a stroke calls it.
    /// </summary>
    [AvaloniaFact]
    public void AbandoningAnEffectBrushDoesNotBlockTheNextOrdinaryStroke()
    {
        var vm = Painted();
        Pick(vm, "Smudge");
        vm.BrushSmudgeLength = 0.9;

        vm.BeginStroke(180, 100, 1);
        vm.MoveStroke(180, 150, 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.PlayCommand.Execute(null);  // abandons the smudge via StartPlayback -> Cancel()
        vm.PauseCommand.Execute(null);

        // Not another brush stroke: BeginStroke unconditionally clears
        // _liveComposite itself (it has to, to start its own preview), which
        // would mask the gap regardless of whether Cancel() was fixed. The
        // gradient tool never touches _liveComposite, so it is the one that
        // actually exercises whether abandoning the smudge cleaned up after
        // itself.
        vm.ActiveTool = ToolId.Gradient;
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        vm.BeginGradient(400, 300);
        vm.MoveGradient(460, 300);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        using var during = SKBitmap.FromImage(latest!.Image);
        Assert.True(during.GetPixel(430, 300).Alpha > 0,
            "a gradient drag after an abandoned smudge did not show live — _liveComposite was left stale");
    }

    [AvaloniaFact]
    public void BlurShipsWithARadiusYouCanActuallySee()
    {
        var vm = VmLayers.BareVm();
        var blur = vm.BrushPresetChoices.First(p => p.Name == "Blur").Settings;

        // The engine's own formula — StampBlur: sigma = Flow × Size / 4.
        var sigma = blur.Flow * blur.Size / 4;
        output.WriteLine($"Blur ships at flow {blur.Flow:F2} on {blur.Size} px — sigma {sigma:F2} px");

        Assert.True(
            sigma >= 1.5,
            $"Blur's sigma is {sigma:F2} px, which is not a visible softening — it shipped at "
            + "0.6 px once and the brush appeared to do nothing at all");
        Assert.True(
            sigma <= 4,
            $"Blur's sigma is {sigma:F2} px — a single dab that wipes out that much detail is "
            + "a smear rather than a softening, and it costs a full-layer blur per dab");
    }
}
