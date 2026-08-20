using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The arc overlay end to end, asked B58's three questions like the trail
/// before it: the painter puts pixels where the arc is, the view model hands
/// the window the overlay and strips the half that is off, and both toggles
/// are registered so they can be found and rebound. Plus the one promise the
/// view model alone can keep: a predicted tick never covers a real drawing.
/// </summary>
[Collection("BrushState")]
public sealed class MotionArcOverlayTests(ITestOutputHelper output) : BrushStateIsolated
{
    private const int W = 200, H = 160;

    private static (int Painted, SKBitmap Image) PaintOf(MotionArcOverlay? arc, float scale = 1)
    {
        var bmp = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            MotionArcPainter.Paint(canvas, arc, scale);
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

    private static int PaintedNear(SKBitmap bmp, double cx, double cy, int radius)
    {
        var painted = 0;
        for (var y = (int)cy - radius; y <= (int)cy + radius; y++)
        for (var x = (int)cx - radius; x <= (int)cx + radius; x++)
        {
            if (x >= 0 && y >= 0 && x < W && y < H && bmp.GetPixel(x, y).Alpha > 8) painted++;
        }
        return painted;
    }

    [Fact]
    public void TheArcPaintsAndThePredictionsAreDashed()
    {
        var overlay = new MotionArcOverlay(
            Path: [new(20, 80), new(100, 80), new(180, 80)],
            Extension: [new(180, 80), new(180, 40)],
            OffArc: [new(100, 120, 100, 80, "F1")],
            Next: new ArcSample(180, 40),
            Current: new ArcSample(60, 80));

        var (painted, bmp) = PaintOf(overlay);
        output.WriteLine($"painted {painted} px");
        Assert.True(painted > 100, $"the overlay painted almost nothing: {painted} px");

        // The arc line runs between its samples…
        Assert.True(bmp.GetPixel(50, 80).Alpha > 8, "no arc line between samples");
        // …the pull-line runs from the off-arc tick to its foot…
        Assert.True(bmp.GetPixel(100, 100).Alpha > 8, "no pull-line from the off-arc tick to its foot");
        // …and the off-arc ring surrounds the tick.
        Assert.True(PaintedNear(bmp, 100, 120, 9) > 8, "no ring around the off-arc tick");

        // The predictions exist but are dashed: ink near each, gaps within.
        Assert.True(PaintedNear(bmp, 180, 40, 6) > 4, "no predicted next tick");
        Assert.True(PaintedNear(bmp, 60, 80, 9) > 4, "no predicted current tick");
        var onExtension = 0;
        var gaps = 0;
        for (var y = 45; y < 76; y++)
        {
            if (bmp.GetPixel(180, y).Alpha > 8) onExtension++;
            else gaps++;
        }
        output.WriteLine($"extension column: {onExtension} inked, {gaps} bare");
        Assert.True(onExtension > 4, "the extension to the prediction is not drawn");
        Assert.True(gaps > 4, "the extension is solid — a suggestion drawn as a fact");

        Assert.Equal(0, PaintOf(null).Painted);
    }

    [AvaloniaFact]
    public void TheViewModelHandsTheWindowTheArcAndStripsWhatIsOff()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Arc", W, H, 12, 72, "#ffffff", false));
        vm.ColorHex = "#000000";
        vm.BrushSize = 6;

        // Four drawings roughly along an arc, one pushed well off it.
        Draw(vm, 30, 100);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 70, 70);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 110, 110);   // the stray: its neighbours arc over it
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 150, 70);

        MotionArcOverlay? arc = null;
        vm.MotionArcChanged += a => arc = a;

        // Switching the arc on switches the trail on: a judgement of ticks
        // nobody can see is a toggle that reads as broken.
        Assert.False(vm.MotionTrail);
        vm.MotionArcs = true;
        Assert.True(vm.MotionTrail, "enabling the arc did not enable the trail it is read off");
        Assert.NotNull(arc);
        Assert.True(arc!.Path.Count >= 2, "no fitted path");
        output.WriteLine($"path {arc.Path.Count} samples, off-arc: {string.Join(", ", arc.OffArc.Select(o => o.FrameId))}");

        // Prediction is off: nothing dashed is handed over.
        Assert.Null(arc.Next);
        Assert.Null(arc.Current);
        Assert.Empty(arc.Extension);

        // The arc off but prediction on: the path is stripped instead.
        vm.MotionArcs = false;
        vm.ArcPrediction = true;
        Assert.NotNull(arc);
        Assert.Empty(arc!.Path);
        Assert.Empty(arc.OffArc);

        // And the trail's master switch clears the arc with the ticks.
        vm.MotionTrail = false;
        Assert.Null(arc);
    }

    [AvaloniaFact]
    public void ARealNextDrawingSuppressesThePrediction()
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false };
        vm.NewDocument(new NewDocumentSettings("Arc", W, H, 12, 72, "#ffffff", false));
        vm.ColorHex = "#000000";
        vm.BrushSize = 6;
        Draw(vm, 30, 100);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 70, 72);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 110, 60);
        vm.AddFrameCommand.Execute(null);
        Draw(vm, 150, 64);

        MotionArcOverlay? arc = null;
        IReadOnlyList<TrailPoint>? trail = null;
        vm.MotionArcChanged += a => arc = a;
        vm.MotionTrailChanged += o => trail = o?.Points;
        vm.ArcPrediction = true;
        vm.MotionTrailAfter = 0;

        // Standing mid-sequence with the window ending before a real drawing:
        // the arc must not draw a suggestion on top of it…
        vm.CurrentFrameIndex = 2;
        Assert.NotNull(arc);
        Assert.Null(arc!.Next);
        // …and the probe that discovered it is not displayed as a tick.
        output.WriteLine($"ticks shown: {string.Join(", ", trail!.Select(p => p.Index))}");
        Assert.DoesNotContain(trail!, p => p.Index > 2);

        // On the last drawing nothing follows, and the prediction appears.
        vm.CurrentFrameIndex = 3;
        Assert.NotNull(arc?.Next);
        output.WriteLine($"predicted next at ({arc!.Next!.Value.X:F1}, {arc.Next.Value.Y:F1})");
        Assert.True(arc.Next.Value.X > 150, "the prediction did not continue past the last drawing");
    }

    [AvaloniaFact]
    public void TheArcTogglesAreRegisteredSoTheyCanBeFoundAndRebound()
    {
        var map = new Services.ShortcutMap { StorePathOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"arc-shortcuts-{Guid.NewGuid():N}.json") };
        Assert.Contains(map.Definitions, d => d.Id == "canvas.motionArc");
        Assert.Contains(map.Definitions, d => d.Id == "canvas.arcPrediction");
    }

    private static void Draw(MainViewModel vm, double x, double y)
    {
        vm.BeginStroke(x - 5, y - 5, 1);
        vm.MoveStroke(x + 5, y + 5, 1);
        vm.EndStroke();
    }
}
