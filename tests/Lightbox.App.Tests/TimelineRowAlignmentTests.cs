using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The timeline is one list drawn in two halves, and they have to line up.
/// </summary>
/// <remarks>
/// <para>
/// A timeline row is a layer name on the left and a strip of cels on the right,
/// laid out by two different templates that happen to agree on a height. When
/// they stop agreeing, the name for row three sits beside the cels for row two
/// and a half — which reads as the timeline being wrong about the drawing rather
/// than as a layout defect, and is the most expensive kind of confusion this
/// panel can cause.
/// </para>
/// <para>
/// Written while retuning the docker density, because that change removed the
/// vertical padding from <c>Border.layerRow</c> — a class the timeline's layer
/// column also uses. It happened to be safe: the row is sized by
/// <c>TimelineRowHeight</c> and the padding was inside it. It was safe by
/// <em>accident</em>, nothing said so, and the next person shortening a row has
/// no way to know they got away with it.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class TimelineRowAlignmentTests : BrushStateIsolated
{
    [AvaloniaFact]
    public void ALayersNameAndItsCelsShareOneRow()
    {
        var window = new MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;

        // The grid lives on the X-sheet tab now; the track view is in front
        // by default and has no cels to measure.
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The name column and the cel strip, found by the height they are both
        // bound to rather than by name — the point is that they agree, so
        // asserting on the shared source would prove nothing.
        var cels = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("cel") && b.Bounds.Height > 0)
            .ToList();
        Assert.NotEmpty(cels);

        var names = window.GetVisualDescendants().OfType<Panel>()
            .Where(p => p.Bounds is { Width: 88, Height: > 0 })
            .ToList();
        Assert.NotEmpty(names);

        var celHeights = cels.Select(c => c.Bounds.Height).Distinct().ToList();
        var nameHeights = names.Select(n => n.Bounds.Height).Distinct().ToList();

        Assert.True(celHeights.Count == 1,
            $"cels disagree on row height: {string.Join(", ", celHeights)}");
        Assert.True(nameHeights.Count == 1,
            $"layer names disagree on row height: {string.Join(", ", nameHeights)}");
        Assert.True(celHeights[0] == nameHeights[0],
            $"a layer's name is {nameHeights[0]} tall and its cels are {celHeights[0]} — "
            + "the two halves of the timeline have drifted apart");

        // And the row is the height the view model says, so shrinking the scale
        // moves both halves rather than one.
        Assert.Equal(vm.TimelineRowHeight, celHeights[0]);
    }
}
