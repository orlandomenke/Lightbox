using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

[Collection("BrushState")]
public class TmpFillShiftTest(ITestOutputHelper output) : BrushStateIsolated
{
    [AvaloniaFact]
    public void ShiftClickFloodsADifferentRegionThanThePreviewShowed()
    {
        var vm = new MainViewModel(null)
        {
            SmoothStrokes = false,
            ColorHex = "#000000",
            BrushSize = 12,
            BrushHardness = 1,
            BrushOpacity = 1,
            BrushFlow = 1,
        };
        vm.NewDocument(new NewDocumentSettings("Fill", 300, 300, 12, 72, "#ffffff", false));

        // Layer 0 (background): a bounded box of content.
        vm.BeginStroke(50, 50, 1);
        vm.MoveStroke(250, 50, 1);
        vm.MoveStroke(250, 250, 1);
        vm.MoveStroke(50, 250, 1);
        vm.MoveStroke(50, 50, 1);
        vm.EndStroke();

        // Layer 1 (active, on top): totally blank.
        vm.AddPaintedLayerCommand.Execute(null);
        Assert.True(vm.SmartFill, "test assumes the default SmartFill=true");

        vm.ActiveTool = ToolId.Fill;
        IReadOnlyList<List<StrokePoint>>? previewed = null;
        vm.FillPreviewChanged += (contours, _, _) => previewed = contours;

        // Hover with no Shift: the preview never sees a modifier at all.
        vm.UpdatePointerContext(150, 150, KeyModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotNull(previewed);
        var previewArea = SignedArea(previewed![0]);
        output.WriteLine($"previewed outer points {previewed[0].Count}, area {previewArea:F0}");

        // Shift+click at the same spot: invertSmart=true, so the fill samples
        // the active (blank) layer instead of the smart composite.
        vm.ColorHex = "#ff0000";
        vm.FillAt(150, 150, invertSmart: true);
        var fill = Assert.Single(vm.PaintedCel().Strokes, s => s.Tool == ToolKind.Fill);
        var committedArea = SignedArea(fill.Points);
        output.WriteLine($"committed outer points {fill.Points.Count}, area {committedArea:F0}");

        // If the preview were honest about what Shift+click does, the two
        // regions would agree. They do not: the preview showed the bounded
        // background box: the click, unseen by the preview, flooded the
        // whole blank active layer instead.
        Assert.NotEqual(previewed[0].Count, fill.Points.Count);
    }

    private static double SignedArea(List<StrokePoint> pts)
    {
        double a = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            var q = pts[(i + 1) % pts.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return System.Math.Abs(a) / 2;
    }
}
