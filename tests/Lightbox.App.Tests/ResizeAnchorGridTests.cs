using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
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
}
