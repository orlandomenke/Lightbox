using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Q216 — onion ghosts that follow a whole-layer transform must not cost one
/// resample each per pointer event.
/// </summary>
/// <remarks>
/// <para>
/// Whole-layer drags draw each ghost whose drawing is in the session through
/// the preview matrix. Drawn one by one that is a document-sized resample per
/// ghost per event: at 960x540 with 24 drawings, Before=After=2, a publish
/// went from about 4.9 ms (ghosts static) to about 10.6 ms, and the cost was
/// linear in depth (about 2 ms a ghost, against about 0.55 ms when static).
/// The fix composites the ghosts once per session and draws that sheet through
/// the matrix, so depth stops being a per-event cost.
/// </para>
/// <para>
/// <b>A ratio, of minimums, steady-state events only.</b> The first event of a
/// session pays the sheet build and is reported separately. The bar is the
/// whole-layer publish with onion Before=After=2 over the same drag with
/// onion off, set by breaking it: the per-ghost resample (commit 429b3a9a)
/// read 4.1-4.3x and the sheet reads 1.7-1.9x, so the limit sits between them
/// (2.8) with about 1.5x of room each way.
/// </para>
/// <para>
/// <b>Not asserted: the first event.</b> It builds the sheet — about 10 ms at
/// depth 2 and 14.5 ms at depth 4 on 960x540, once per drag — and is printed
/// so a change to it is visible.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class GhostsFollowingADragCostTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Sheet(bool onion)
    {
        var vm = new MainViewModel(null);
        vm.NewDocument(new NewDocumentSettings("probe", 960, 540, 12, 72, "#ffffff", true));
        vm.SmoothStrokes = false;
        vm.ColorHex = "#000000";
        vm.BrushSize = 12;
        var rnd = new Random(7);
        for (var f = 0; f < 24; f++)
        {
            if (f > 0) vm.AddFrameCommand.Execute(null);
            vm.CurrentFrameIndex = f;
            for (var i = 0; i < 8; i++)
            {
                double x = 150 + (rnd.NextDouble() * 450), y = 100 + (rnd.NextDouble() * 300);
                vm.BeginStroke(x, y, 1);
                for (var k = 0; k < 6; k++)
                {
                    x += (rnd.NextDouble() * 20) - 10;
                    y += (rnd.NextDouble() * 20) - 10;
                    vm.MoveStroke(x, y, 1);
                }
                vm.EndStroke();
            }
        }
        vm.CurrentFrameIndex = 12;
        vm.OnionSkin = onion;
        if (onion) { vm.OnionBefore = 2; vm.OnionAfter = 2; }
        vm.TransformScope = TransformScope.ActiveLayerAllFrames;
        return vm;
    }

    private static void Publish(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        void Capture(RenderSnapshot s) => latest = s;
        vm.SnapshotChanged += Capture;
        vm.PublishSnapshot();
        vm.SnapshotChanged -= Capture;
        SKBitmap.FromImage(latest!.Image).Dispose();
    }

    private static SKMatrix Turn(double degrees)
    {
        var m = SKMatrix.CreateTranslation(-480, -270);
        m = m.PostConcat(SKMatrix.CreateRotation((float)(degrees * Math.PI / 180)));
        return m.PostConcat(SKMatrix.CreateTranslation(480, 270));
    }

    private static (double First, double Steady) Drag(bool onion)
    {
        double first = double.MaxValue, steady = double.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var vm = Sheet(onion);
            Publish(vm);
            Publish(vm);
            Assert.True(vm.BeginTransform(), vm.AiStatus);
            for (var i = 1; i <= 12; i++)
            {
                vm.PreviewTransform(Turn(i * 0.5));
                var sw = Stopwatch.StartNew();
                Publish(vm);
                var ms = sw.Elapsed.TotalMilliseconds;
                if (i == 1) first = Math.Min(first, ms);
                else steady = Math.Min(steady, ms);
            }
        }
        return (first, steady);
    }

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void FollowingGhostsDoNotCostAResamplePerGhostPerEvent()
    {
        var (_, off) = Drag(false);
        var (first, on) = Drag(true);
        output.WriteLine($"whole-layer drag publish: onion off {off:F2} ms, onion 2/2 {on:F2} ms ({on / off:F2}x); first event with onion {first:F2} ms");
        Assert.True(on < off * 2.8,
            $"a whole-layer drag publish cost {on:F2} ms with onion 2/2 against {off:F2} ms with it off ({on / off:F1}x) — ghosts are being resampled one by one");
    }
}
