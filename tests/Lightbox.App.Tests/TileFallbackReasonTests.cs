using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// When playback does not get the tile path, the application can say why.
/// </summary>
/// <remarks>
/// <para>
/// <b>B144's tile path is worth 145 → 14 ms a frame at 1080p, and falling back
/// was silent.</b> So a document that stutters because it carries a camera and
/// a document that stutters because the machine is slow produced the same
/// evidence — none — and the owner's first report of "still stuttering at
/// FullHD" could not be attributed without guessing.
/// </para>
/// <para>
/// These drive the <em>real</em> compose path rather than calling
/// <see cref="TileFallback.Reason"/> directly. A tally that agrees with a pure
/// function proves the function; what needed proving is that the compositor
/// asks it, and asks it about the frame it actually drew.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TileFallbackReasonTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Playing()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;

        // Without a viewport the gate is never taken and every assertion here
        // would pass against NoViewport — the arrangement lesson
        // PlaybackThroughTilesTests already records.
        vm.SetViewport(SKRectI.Create(0, 0, 960, 540));

        vm.BeginStroke(100, 100, 1);
        vm.MoveStroke(300, 200, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return vm;
    }

    /// <summary>Play one step and publish, which is what fills the tally.</summary>
    private static void PlayOneFrame(MainViewModel vm)
    {
        vm.ReportTileFallbacks.Reset();
        vm.TogglePlaybackCommand.Execute(null);
        vm.StepPlayback();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.TogglePlaybackCommand.Execute(null);
    }

    private void Report(MainViewModel vm)
    {
        var t = vm.ReportTileFallbacks;
        output.WriteLine($"offered {t.Considered}, tiled {t.Tiled}, dominant {t.Dominant}");
        foreach (var (reason, count) in t.NonZero())
        {
            output.WriteLine($"  {reason} x{count} — {TileFallback.Explain(reason)}");
        }
    }

    [AvaloniaFact]
    public void AnOrdinaryDrawingTilesAndSaysSo()
    {
        var vm = Playing();
        PlayOneFrame(vm);
        Report(vm);

        // Not vacuous: something has to have been offered for "no fallbacks" to
        // mean anything. A gate that was never reached also reports zero.
        Assert.True(vm.ReportTileFallbacks.Considered > 0, "the tile gate was never reached");
        Assert.True(vm.ReportTileFallbacks.Tiled > 0, "nothing was drawn from tiles");
        Assert.Equal(TileFallbackReason.None, vm.ReportTileFallbacks.Dominant);
        Assert.Equal(0, vm.ReportTileFallbacks.FellBack);
    }

    [AvaloniaFact]
    public void ACameraIsNamedAsTheReason()
    {
        var vm = Playing();
        vm.Doc.Scene.Camera = new Camera();
        PlayOneFrame(vm);
        Report(vm);

        Assert.True(vm.ReportTileFallbacks.CountOf(TileFallbackReason.Camera) > 0);
        Assert.Equal(TileFallbackReason.Camera, vm.ReportTileFallbacks.Dominant);
        Assert.Equal(0, vm.ReportTileFallbacks.Tiled);
    }

    [AvaloniaFact]
    public void BaselinePixelsAreNamedAsTheReason()
    {
        var vm = Playing();
        // A one-pixel PNG is a baseline as far as the gate is concerned, and
        // encoding a real one keeps this honest about the property being read.
        using var bmp = new SKBitmap(1, 1);
        bmp.SetPixel(0, 0, SKColors.Red);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        vm.PaintedCel().PngBase64 = Convert.ToBase64String(data.ToArray());

        PlayOneFrame(vm);
        Report(vm);

        Assert.True(vm.ReportTileFallbacks.CountOf(TileFallbackReason.Baseline) > 0);
    }

    [AvaloniaFact]
    public void ASmudgeStrokeIsNamedAsTheReason()
    {
        var vm = Playing();
        vm.ApplyPreset(vm.BrushPresetChoices.First(p => p.Id == "builtin-smudge"));
        vm.BeginStroke(120, 120, 1);
        vm.MoveStroke(260, 180, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        PlayOneFrame(vm);
        Report(vm);

        Assert.True(
            vm.ReportTileFallbacks.CountOf(TileFallbackReason.EffectStroke) > 0,
            "a committed smudge did not register as the reason tiles were refused");
    }

    /// <summary>
    /// The reason and the decision cannot disagree, because they are one call.
    /// </summary>
    /// <remarks>
    /// The shape this replaced — a <c>bool</c> gate beside a separate "why" for
    /// reporting — passes every test on the day it is written and drifts later,
    /// at which point the report names a reason the compositor never acted on.
    /// This asserts the property directly so the two cannot be split again
    /// without something going red.
    /// </remarks>
    [AvaloniaFact]
    public void EveryFrameOfferedIsEitherTiledOrHasAReason()
    {
        var vm = Playing();
        vm.Doc.Scene.Camera = new Camera();
        PlayOneFrame(vm);

        var t = vm.ReportTileFallbacks;
        var accounted = t.Tiled;
        foreach (var (_, count) in t.NonZero()) accounted += count;

        output.WriteLine($"offered {t.Considered}, accounted {accounted}");
        Assert.Equal(t.Considered, accounted);
    }

    [AvaloniaFact]
    public void APausedCanvasOffersNothingToTheTilePath()
    {
        // Rest is canonical: the still picture never goes through tiles, so a
        // report written without playing must not claim the path was fine.
        var vm = Playing();
        vm.ReportTileFallbacks.Reset();
        vm.PublishSnapshot();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, vm.ReportTileFallbacks.Considered);
    }
}
