using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The resize dialog's anchor grid, pressed the way an artist presses it.
/// </summary>
/// <remarks>
/// Real pointer input on a shown window rather than <c>SetAnchor</c>, because
/// the view-model tests cannot tell a grid that ignores clicks from one that
/// works. Written while chasing a report that the anchor did nothing — which
/// turned out to be the render (see
/// <c>GrownPaperCanvasTests.ADrawingOnAnotherLayerMovesWithThePaper</c>), so
/// this is the half that rules the grid out.
/// </remarks>
public class ResizeAnchorGridTests(ITestOutputHelper output)
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    [AvaloniaTheory]
    [InlineData(ResizeAnchor.TopLeft)]
    [InlineData(ResizeAnchor.BottomRight)]
    [InlineData(ResizeAnchor.Right)]
    public void ClickingACellOfTheGridSetsTheAnchor(ResizeAnchor wanted)
    {
        var doc = DocumentFactory.CreateDoc(400, 300, 1);
        var dialog = new ResizeDialog(doc.Scene, ResizeMode.Canvas);
        dialog.Show();
        Pump();

        var grid = dialog.FindControl<UniformGrid>("AnchorGrid")!;
        var cell = (Control)grid.Children[(int)wanted];
        var at = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), dialog)!.Value;
        output.WriteLine($"cell {wanted}: bounds {cell.Bounds}, click at {at}");

        dialog.MouseDown(at, MouseButton.Left); Pump();
        dialog.MouseUp(at, MouseButton.Left); Pump();

        output.WriteLine($"anchor now {dialog.Choice.Anchor}");
        Assert.Equal(wanted, dialog.Choice.Anchor);
        dialog.Close();
    }
    /// <summary>
    /// The preview is what makes the anchor legible before the button, so a
    /// click on the grid has to move it — not only the view model's field.
    /// Read off the control the window shows, because a preview bound to a
    /// stale value would pass every view-model test.
    /// </summary>
    [AvaloniaFact]
    public void ClickingACellMovesThePreview()
    {
        var doc = DocumentFactory.CreateDoc(400, 300, 1);
        var dialog = new ResizeDialog(doc.Scene, ResizeMode.Canvas);
        dialog.Show();
        Pump();
        dialog.Choice.Width = 600;
        dialog.Choice.Height = 400;
        Pump();

        var preview = dialog.FindControl<ResizeOutlinePreview>("OutlinePreview")!;
        Assert.True(preview.IsEffectivelyVisible);
        Assert.True(preview.Bounds.Width > 0 && preview.Bounds.Height > 0, $"preview bounds {preview.Bounds}");

        Click(dialog, ResizeAnchor.TopLeft);
        var topLeft = preview.Outline!;
        output.WriteLine($"top-left: old {topLeft.Old} new {topLeft.New}");
        Assert.Equal(topLeft.Old.Left, topLeft.New.Left, 6);
        Assert.Equal(topLeft.Old.Top, topLeft.New.Top, 6);

        Click(dialog, ResizeAnchor.BottomRight);
        var bottomRight = preview.Outline!;
        output.WriteLine($"bottom-right: old {bottomRight.Old} new {bottomRight.New}");
        Assert.Equal(bottomRight.Old.Right, bottomRight.New.Right, 6);
        Assert.Equal(bottomRight.Old.Bottom, bottomRight.New.Bottom, 6);
        Assert.NotEqual(topLeft.Old, bottomRight.Old);
        dialog.Close();
    }

    [AvaloniaFact]
    public void AnImageResizeHasNoOutline()
    {
        // Rescaling artwork has no corner to hold still, so nothing to draw.
        var doc = DocumentFactory.CreateDoc(400, 300, 1);
        var dialog = new ResizeDialog(doc.Scene, ResizeMode.Image);
        dialog.Show();
        Pump();
        Assert.False(dialog.FindControl<ResizeOutlinePreview>("OutlinePreview")!.IsEffectivelyVisible);
        dialog.Close();
    }

    private static void Click(ResizeDialog dialog, ResizeAnchor anchor)
    {
        var grid = dialog.FindControl<UniformGrid>("AnchorGrid")!;
        var cell = (Control)grid.Children[(int)anchor];
        var at = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), dialog)!.Value;
        dialog.MouseDown(at, MouseButton.Left); Pump();
        dialog.MouseUp(at, MouseButton.Left); Pump();
    }
}
