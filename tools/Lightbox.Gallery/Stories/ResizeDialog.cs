using Avalonia;
using Avalonia.Controls;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.Gallery.Stories;

public static partial class Catalog
{
    /// <summary>
    /// The resize dialog's content, lifted out of the real window so the
    /// outline preview is judged in place, beside the anchor grid it answers.
    /// </summary>
    private static Story ResizeDialogStory() => new(
        "Resize dialog · outline preview",
        "Resize canvas with the outline preview the owner chose over a thumbnail or arrows in the grid: the current paper dashed, the "
        + "new paper solid, the part a crop cuts away dimmed. Placed by CanvasResize.OriginShift, the call the edit makes, so the "
        + "picture and the result cannot disagree. A 400 × 300 document throughout; Image mode has no anchor and no preview.",
        [
            new("Canvas · unchanged", () => Resize(ResizeMode.Canvas, 400, 300, ResizeAnchor.Center)),
            new("Grow · top-left", () => Resize(ResizeMode.Canvas, 600, 400, ResizeAnchor.TopLeft)),
            new("Grow · bottom-right", () => Resize(ResizeMode.Canvas, 600, 400, ResizeAnchor.BottomRight)),
            new("Grow · right only", () => Resize(ResizeMode.Canvas, 700, 300, ResizeAnchor.Left)),
            new("Crop · centre", () => Resize(ResizeMode.Canvas, 200, 150, ResizeAnchor.Center)),
            new("Wider and shorter · centre", () => Resize(ResizeMode.Canvas, 500, 200, ResizeAnchor.Center)),
            new("Portrait from landscape · top", () => Resize(ResizeMode.Canvas, 300, 400, ResizeAnchor.Top)),
            new("Image mode · no preview", () => Resize(ResizeMode.Image, 800, 600, ResizeAnchor.Center)),
        ],
        Columns: 2);

    private static Control Resize(ResizeMode mode, int width, int height, ResizeAnchor anchor)
    {
        var scene = DocumentFactory.CreateDoc(400, 300, 1).Scene;
        var dialog = new Lightbox.App.Views.ResizeDialog(scene, mode);
        var vm = dialog.Choice;
        vm.Anchor = anchor;
        vm.Width = width;
        vm.Height = height;

        // Eight dialogs share one gallery page, and a radio group spans its
        // root: give each state its own group, then check the anchor it shows.
        if (dialog.FindControl<Avalonia.Controls.Primitives.UniformGrid>("AnchorGrid") is { } grid)
        {
            var group = $"anchor-{mode}-{width}x{height}-{anchor}";
            foreach (var cell in grid.Children.OfType<RadioButton>()) cell.GroupName = group;
            ((RadioButton)grid.Children[(int)anchor]).IsChecked = true;
        }

        var content = (Control)dialog.Content!;
        dialog.Content = null;
        return new Border
        {
            Background = Brush("SurfaceElevatedBrush"),
            Width = dialog.Width,
            DataContext = vm,
            Child = content,
        };
    }
}
