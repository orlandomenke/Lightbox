using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Q216's ramp: a ramped whole-layer drag must not pay one resample per onion
/// ghost per event — the nearest ghost each side follows live and the rest are
/// one sheet made at the last pause (the owner's "near live, rest on pause").
/// </summary>
/// <remarks>
/// Ratio of minimums, steady events, ramp over plain whole-layer drag, onion 4/4,
/// the larger of unsettled and settled ramp. Set by breaking it: 6567692a (every
/// ramped ghost resampled per event) read 4.05-4.36x and fails; f8da8816 reads
/// 1.68-1.89x. Limit 3.0, about 1.6x of room on one side and 1.35x on the other.
/// The first event of a drag pays the sheet build and is printed, not asserted.
/// </remarks>
[Collection("BrushState")]
public sealed class RampedDragCostTests(ITestOutputHelper output) : BrushStateIsolated
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

    private static (double First, double Steady) Drag(int depth, bool ramp, bool settle = false)
    {
        double first = double.MaxValue, steady = double.MaxValue;
        for (var round = 0; round < 3; round++)
        {
            var vm = Sheet(depth > 0);
            if (depth > 0) { vm.OnionBefore = depth; vm.OnionAfter = depth; }
            Publish(vm);
            Publish(vm);
            Assert.True(vm.BeginLayerTransform(), vm.AiStatus);
            if (ramp) { vm.RampOverFrames = true; Assert.True(vm.RampOverFrames, vm.AiStatus); }
            if (ramp && settle) { vm.SetTransformBox(new AffineParts(480, 270, 1, 1, 3 * Math.PI / 180, 0, 0)); vm.PreviewTransform(Turn(3)); vm.SettleRamp(); }
            for (var i = 1; i <= 12; i++)
            {
                var degrees = i * 0.5;
                if (ramp) vm.SetTransformBox(new AffineParts(480, 270, 1, 1, degrees * Math.PI / 180, 0, 0));
                vm.PreviewTransform(Turn(degrees));
                var sw = Stopwatch.StartNew();
                Publish(vm);
                var ms = sw.Elapsed.TotalMilliseconds;
                if (i == 1) first = Math.Min(first, ms); else steady = Math.Min(steady, ms);
            }
        }
        return (first, steady);
    }

    /// <summary>Set by breaking it — see the remarks on the class.</summary>
    private const double Limit = 3.0;

    [AvaloniaFact]
    [Trait("Category", "Performance")]
    public void ARampedDragCostsAboutWhatAPlainOneDoesWhateverTheOnionDepth()
    {
        // Depth 4 only: it is where the per-ghost resample and the sheet differ
        // most (4.05-4.36x against 1.68-1.89x); at depth 2 the broken version
        // read 2.0x, too close to discriminate. The other depths were measured
        // once when the limit was set and are not worth a CI runner's minutes.
        const int depth = 4;
        var (_, plain) = Drag(depth, false);
        var (first, ramp) = Drag(depth, true);
        var (firstSettled, rampSettled) = Drag(depth, true, settle: true);
        output.WriteLine($"onion {depth}/{depth}: plain {plain:F2} ms; ramp {ramp:F2} ({ramp / plain:F2}x, first {first:F2}); " +
                         $"ramp settled {rampSettled:F2} ({rampSettled / plain:F2}x, first {firstSettled:F2})");
        var worst = Math.Max(ramp, rampSettled);
        Assert.True(worst < plain * Limit,
            $"ramped publish {worst:F2} ms against {plain:F2} ms plain at onion {depth}/{depth} ({worst / plain:F1}x) — ramped ghosts are being resampled per event");
    }
}
