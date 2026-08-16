using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The bucket and the wand preview their region before the click. Both were
/// click-commit with no feedback at all — the only tools left whose result
/// could not be seen before it happened. The preview is traced by the same
/// functions the click runs, so it cannot drift from what the click does;
/// these tests pin that equivalence and the cost rules around it.
/// </summary>
[Collection("BrushState")]
public class FillHoverPreviewTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel VmWithShape()
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
        // A closed box around (150,100): inside and outside are different
        // flood regions, which is what the containment shortcut needs.
        vm.BeginStroke(100, 50, 1);
        vm.MoveStroke(200, 50, 1);
        vm.MoveStroke(200, 150, 1);
        vm.MoveStroke(100, 150, 1);
        vm.MoveStroke(100, 50, 1);
        vm.EndStroke();
        return vm;
    }

    private static void Flush() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    [AvaloniaFact]
    public void TheHoverTracesExactlyWhatTheClickWouldFill()
    {
        var vm = VmWithShape();
        vm.ActiveTool = ToolId.Fill;
        IReadOnlyList<List<StrokePoint>>? previewed = null;
        vm.FillPreviewChanged += (contours, _, _) => previewed = contours;

        vm.UpdatePointerContext(150, 100, KeyModifiers.None);
        Flush();

        Assert.NotNull(previewed);
        output.WriteLine($"preview: {previewed!.Count} contours, outer {previewed[0].Count} points");

        vm.ColorHex = "#ff0000";
        vm.FillAt(150, 100);
        // FillBelowLines tucks the fill under the line work, so find it by kind.
        var fill = Assert.Single(vm.PaintedCel().Strokes, s => s.Tool == ToolKind.Fill);

        // The committed outline is the previewed outline, point for point —
        // one function behind both, so this can only fail if that splits.
        Assert.Equal(previewed[0].Count, fill.Points.Count);
        for (var i = 0; i < fill.Points.Count; i++)
        {
            Assert.Equal(previewed[0][i].X, fill.Points[i].X, 6);
            Assert.Equal(previewed[0][i].Y, fill.Points[i].Y, 6);
        }
    }

    [AvaloniaFact]
    public void AHoverInsideTheTracedRegionDoesNotRetrace()
    {
        var vm = VmWithShape();
        vm.ActiveTool = ToolId.Fill;
        var events = 0;
        vm.FillPreviewChanged += (_, _, _) => events++;

        vm.UpdatePointerContext(150, 100, KeyModifiers.None);
        Flush();
        var afterFirst = events;

        // Same connected region: the flood from any seed inside it is the
        // same region, so no new trace and no new push.
        vm.UpdatePointerContext(160, 110, KeyModifiers.None);
        vm.UpdatePointerContext(140, 90, KeyModifiers.None);
        Flush();

        output.WriteLine($"events after first {afterFirst}, after moves inside {events}");
        Assert.Equal(afterFirst, events);

        // Crossing the boundary retraces.
        vm.UpdatePointerContext(30, 30, KeyModifiers.None);
        Flush();
        Assert.True(events > afterFirst, "leaving the region did not retrace");
    }

    [AvaloniaFact]
    public void TheWandPreviewsAndTheBrushDoesNot()
    {
        var vm = VmWithShape();
        IReadOnlyList<List<StrokePoint>>? previewed = null;
        var wandFlag = false;
        vm.FillPreviewChanged += (contours, wand, _) => (previewed, wandFlag) = (contours, wand);

        vm.ActiveTool = ToolId.Select;
        vm.ActiveSelectVariant = SelectVariant.Wand;
        vm.UpdatePointerContext(150, 100, KeyModifiers.None);
        Flush();
        Assert.NotNull(previewed);
        Assert.True(wandFlag, "a wand trace was not flagged as the wand's");

        // Back to the brush: the preview clears rather than lingering.
        vm.ActiveTool = ToolId.Brush;
        Flush();
        Assert.Null(previewed);
    }

    [AvaloniaFact]
    public void AHoverNeverKeysAHeldCel()
    {
        // The click's sample target may key a held cel; the hover's must
        // not — a preview that edits the timeline is not a preview.
        var vm = VmWithShape();
        vm.ActiveTool = ToolId.Fill;
        var celsBefore = vm.PaintedCel();

        vm.UpdatePointerContext(150, 100, KeyModifiers.None);
        Flush();

        Assert.Same(celsBefore, vm.PaintedCel());
        Assert.Single(vm.PaintedCel().Strokes); // still just the box
    }
}
