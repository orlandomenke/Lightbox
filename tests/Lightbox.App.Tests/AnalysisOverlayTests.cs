using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The trail's analysers end to end (Q133): the painter puts ghost targets and
/// the fitted arc where the arithmetic says, the readout speaks in the flyout,
/// the nudge moves the drawing and undoes exactly, and every switch is
/// registered where the Configure window can see it — B58's three assertions,
/// asked of each new surface from day one.
/// </summary>
[Collection("BrushState")]
public sealed class AnalysisOverlayTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 200, H = 160;

    private static (int Painted, SKBitmap Image) PaintOf(TrailOverlay? overlay, float scale = 1)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            MotionTrailPainter.Paint(canvas, overlay, scale);
            canvas.Flush();
        }
        var painted = 0;
        for (var y = 0; y < H; y++)
        for (var x = 0; x < W; x++)
        {
            if (bmp.GetPixel(x, y).Alpha > 8) painted++;
        }
        return (painted, bmp);
    }

    [Fact]
    public void AMissedTargetPaintsAGhostAndATether()
    {
        // One drawing at (60, 80) whose target is (120, 80): the ghost ring
        // sits at the target, the tether crosses the midpoint between them.
        var overlay = new TrailOverlay(
            Points: null,
            SpacingTargets:
            [
                new SpacingTarget(1, "B", 60, 80, 120, 80, 60, Misses: true),
            ],
            Jump: null);

        var (painted, bmp) = PaintOf(overlay);
        output.WriteLine($"painted {painted} px");

        Assert.True(painted > 20, $"the ghost painted almost nothing: {painted} px");
        Assert.True(bmp.GetPixel(90, 80).Alpha > 8, "no tether between the drawing and its target");
        var ring = false;
        for (var y = 76; y <= 84 && !ring; y++)
        for (var x = 116; x <= 124 && !ring; x++)
        {
            if (bmp.GetPixel(x, y).Alpha > 8) ring = true;
        }
        Assert.True(ring, "no ghost tick at the target");
    }

    [Fact]
    public void ADrawingOnItsSpacingPaintsNoGhost()
    {
        // Ghosting every tick would read as the trail doubled; a drawing
        // already where it belongs stays clean.
        var overlay = new TrailOverlay(
            Points: null,
            SpacingTargets:
            [
                new SpacingTarget(1, "B", 60, 80, 60.2, 80, 0.2, Misses: false),
            ],
            Jump: null);

        Assert.Equal(0, PaintOf(overlay).Painted);
    }

    [Fact]
    public void TheJumpArcPaintsAndTheOffenderIsRinged()
    {
        var curve = Enumerable.Range(0, 41)
            .Select(k => new ArcPoint(20 + 4 * k, 140 - 0.06 * (4 * k) * (160 - 4 * k)))
            .ToList();
        var overlay = new TrailOverlay(
            Points: null,
            SpacingTargets: null,
            Jump: new JumpArcFit(
                curve,
                [new ArcDeviation(2, "C", 100, 40, 100, 25.6, 14.4, OffArc: true)],
                Ballistic: true,
                Tolerance: 5));

        var (painted, bmp) = PaintOf(overlay);
        output.WriteLine($"painted {painted} px");

        Assert.True(painted > 50, $"the arc painted almost nothing: {painted} px");
        // The offender's ring: some ink on the circle around (100, 40).
        var ring = false;
        for (var y = 32; y <= 48 && !ring; y++)
        for (var x = 92; x <= 108 && !ring; x++)
        {
            if (bmp.GetPixel(x, y).Alpha > 8) ring = true;
        }
        Assert.True(ring, "the drawing off the arc is not ringed");
    }

    [AvaloniaFact]
    public void TheNudgeMovesTheDrawingAndUndoRestoresTheExactBits()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Nudge", W, H, 12, 72, "#ffffff", false));
        vm.ColorHex = "#000000";
        vm.BrushSize = 6;

        // Three drawings whose ink centres at x = 20, 50, 120 — the middle
        // one 15 px short of the even spacing (70) along a straight path.
        vm.BeginStroke(10, 40, 1);
        vm.MoveStroke(30, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(110, 40, 1);
        vm.MoveStroke(130, 60, 1);
        vm.EndStroke();

        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        layer.Cels[1].Frame!.Role = FrameRole.Inbetween;
        var middle = layer.Cels[1].Frame!;
        var before = middle.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList();

        vm.CurrentFrameIndex = 1;
        vm.NudgeToSpacing();
        output.WriteLine(vm.AiStatus);

        Assert.Equal(before[0].X + 20, middle.Strokes[0].Points[0].X, 6);
        Assert.Equal(before[0].Y, middle.Strokes[0].Points[0].Y, 6);

        // Undo restores the exact coordinates — the bits, not "minus the
        // offset", because every dab dynamic is seeded from them.
        vm.UndoCommand.Execute(null);
        var restored = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[1].Frame!;
        Assert.Equal(before, restored.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList());

        // And redo/undo again round-trips — the revert hands back copies, so
        // one cycle cannot corrupt the next.
        vm.RedoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        var again = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[1].Frame!;
        Assert.Equal(before, again.Strokes[0].Points.Select(p => (p.X, p.Y)).ToList());
    }

    [AvaloniaFact]
    public void ANudgeRefusesADrawingWithAPixelBaseline()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Nudge", W, H, 12, 72, "#ffffff", false));
        vm.ColorHex = "#000000";
        vm.BeginStroke(10, 40, 1);
        vm.MoveStroke(30, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(110, 40, 1);
        vm.MoveStroke(130, 60, 1);
        vm.EndStroke();

        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        layer.Cels[1].Frame!.Role = FrameRole.Inbetween;
        layer.Cels[1].Frame!.PngBase64 = "iVBORw0KGgo=";
        var before = layer.Cels[1].Frame!.Strokes[0].Points[0].X;

        vm.CurrentFrameIndex = 1;
        vm.NudgeToSpacing();
        output.WriteLine(vm.AiStatus);

        Assert.Equal(before, layer.Cels[1].Frame!.Strokes[0].Points[0].X);
        Assert.Contains("baseline", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TheReadoutSpeaksWhenAnAnalyserIsOnAndTheTrailCarriesTheGhosts()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Readout", W, H, 12, 72, "#ffffff", false));
        vm.ColorHex = "#000000";
        vm.BeginStroke(10, 40, 1);
        vm.MoveStroke(30, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(40, 40, 1);
        vm.MoveStroke(60, 60, 1);
        vm.EndStroke();
        vm.AddFrameCommand.Execute(null);
        vm.BeginStroke(110, 40, 1);
        vm.MoveStroke(130, 60, 1);
        vm.EndStroke();
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[1].Frame!.Role = FrameRole.Inbetween;
        // On the inbetween: the run the analysers read is the playhead's, and
        // standing on the closing extreme reads the (empty) run after it.
        vm.CurrentFrameIndex = 1;

        TrailOverlay? latest = null;
        vm.MotionTrailChanged += o => latest = o;

        Assert.Equal("", vm.AnalysisReadout);
        vm.MotionTrail = true;
        Assert.Equal("", vm.AnalysisReadout);   // trail on, analysers off: quiet

        vm.SpacingGhosts = true;
        output.WriteLine(vm.AnalysisReadout);
        Assert.Contains("Spacing", vm.AnalysisReadout);
        Assert.True(vm.HasAnalysisReadout);
        Assert.NotNull(latest?.SpacingTargets);
        Assert.Contains(latest!.SpacingTargets!, t => t.Misses);

        // Off with the trail: the analysers annotate its ticks, so switching
        // the trail off silences them too.
        vm.MotionTrail = false;
        Assert.Equal("", vm.AnalysisReadout);
        Assert.Null(latest);
    }

    [AvaloniaFact]
    public void EverySwitchIsRegisteredSoItCanBeFoundAndRebound()
    {
        var map = new Services.ShortcutMap { StorePathOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"analysis-shortcuts-{Guid.NewGuid():N}.json") };
        Assert.Contains(map.Definitions, d => d.Id == "canvas.spacingGhosts");
        Assert.Contains(map.Definitions, d => d.Id == "canvas.jumpArc");
        Assert.Contains(map.Definitions, d => d.Id == "canvas.walkReport");
        Assert.Contains(map.Definitions, d => d.Id == "timeline.nudgeToSpacing");
    }
}
