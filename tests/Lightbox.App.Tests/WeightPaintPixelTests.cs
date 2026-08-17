using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B247, measured in pixels: a weight stroke recolors influence and must not
/// move ink. The deformation the new weights describe appears when the brush
/// is disarmed — never mid-session, never on the commit itself.
/// </summary>
/// <remarks>
/// In the <c>BrushState</c> collection because it draws with pinned brush
/// parameters through the real stroke pipeline, and those live in a
/// process-wide store.
/// </remarks>
[Collection("BrushState")]
public class WeightPaintPixelTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static byte[] PixelBytes(RenderSnapshot snapshot)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        Assert.NotNull(bmp);
        return bmp!.Bytes;
    }

    private static int DifferingBytes(byte[] a, byte[] b)
    {
        Assert.Equal(a.Length, b.Length);
        var n = 0;
        for (var i = 0; i < a.Length; i++)
            if (a[i] != b[i]) n++;
        return n;
    }

    [AvaloniaFact]
    public void AWeightStrokeCommitLeavesTheInkAloneUntilTheBrushIsDisarmed()
    {
        var vm = new MainViewModel(null)
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
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;

        // A horizontal line, then a rig: bone A carries the stroke, bone B —
        // far away and unposed — is what the weights get painted toward.
        vm.BeginStroke(100, 150, 1);
        vm.MoveStroke(160, 150, 1);
        vm.EndStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var atRest = PixelBytes(latest!);

        vm.ArmatureEditMode = true;
        vm.CreateBoneFromDrag(100, 150, 160, 150);
        var carrier = vm.SelectedBoneId!;
        vm.SelectedBoneId = null;
        vm.CreateBoneFromDrag(300, 60, 340, 60);
        var painted = vm.SelectedBoneId!;

        var frame = ExposureSheet.ExposedFrame(
            vm.Doc.Scene.Layers.First(l => !l.IsBackground), vm.CurrentFrameIndex)!;
        var stroke = frame.Strokes.Single();
        vm.Selection.SelectStroke(stroke.Id);
        vm.SelectedBoneId = carrier;
        vm.AssignSelectedStrokesToBone();

        // Pose the carrier straight down: the ink swings vertical.
        vm.PosingMode = true;
        vm.PoseBoneTo(carrier, 100, 250);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var posed = PixelBytes(latest!);
        Assert.True(DifferingBytes(atRest, posed) > 0,
            "the pose moved no ink — the rig is not deforming and the rest of this test would be vacuous");

        vm.WeightPainting = true;
        vm.SelectedBoneId = painted;
        vm.MirrorWeights = false;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var beforePaint = PixelBytes(latest!);

        // Paint the unposed bone up where the artist SEES the stroke's second
        // point — its posed position — and commit the gesture.
        vm.BeginWeightStroke(100, 210, 1);
        vm.WeightDab(100, 210, 1);
        vm.WeightDab(100, 210, 1);
        vm.EndWeightStroke();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var afterCommit = PixelBytes(latest!);

        var binding = stroke.Weights!.FirstOrDefault(b => b.BoneId == painted);
        Assert.NotNull(binding);
        Assert.True(binding!.WeightAt(1) > 0.3,
            $"the dab did not take: weight {binding.WeightAt(1):F3}");

        var duringSession = DifferingBytes(beforePaint, afterCommit);
        output.WriteLine($"pose moved {DifferingBytes(atRest, posed)} bytes; " +
                         $"commit moved {duringSession}; ");
        Assert.True(duringSession == 0,
            $"committing the weight stroke moved {duringSession} bytes of ink while the brush was still armed");

        // Disarming is where the new weights take visible effect.
        vm.IsBindMode = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var afterDisarm = PixelBytes(latest!);
        var onDisarm = DifferingBytes(beforePaint, afterDisarm);
        output.WriteLine($"disarm moved {onDisarm} bytes");
        Assert.True(onDisarm > 0,
            "disarming the brush applied no deformation — the deferred invalidation never landed");
    }
}
