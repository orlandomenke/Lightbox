using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B400: a selected row has to be seen. It was drawn fainter than a hovered one
/// in both themes (Dark-lit 4% under a 6.5% hover, Studio grey 18% under 28%),
/// so Ctrl/Shift+click looked like it picked one row while the model held five.
/// </summary>
/// <remarks>
/// The owner's answer: a bar on the left edge plus a fill between hover and
/// active. Hover &lt; selected &lt; active is the order asserted, because each is a
/// statement — "under the pointer", "in the selection", "where the brush lands" —
/// and a weaker statement must not out-draw a stronger one.
/// </remarks>
[Collection("BrushState")]
public sealed class SelectedRowVisibilityTests : BrushStateIsolated
{
    private static double Strength(IBrush? brush) =>
        brush is ISolidColorBrush s ? s.Opacity * s.Color.A / 255.0 : 0;

    /// <summary>The bar: an inset shadow along the left edge.</summary>
    private static bool HasBar(BoxShadows shadows)
    {
        for (var i = 0; i < shadows.Count; i++)
        {
            if (shadows[i].IsInset && shadows[i].OffsetX > 0) return true;
        }
        return false;
    }

    private static double Resource(Control on, string key)
    {
        Assert.True(on.TryFindResource(key, on.ActualThemeVariant, out var value), $"{key} not found");
        return Strength(Assert.IsAssignableFrom<IBrush>(value));
    }

    [AvaloniaTheory]
    [InlineData("Dark-lit")]
    [InlineData("Studio grey")]
    public void ASelectedRowIsDrawnAboveHoverAndBelowActive_WithABar(string theme)
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            vm.ThemeChoice = theme;
            while (vm.Doc.Scene.Layers.Count < 4) vm.AddPaintedLayerCommand.Execute(null);
            var rows = vm.LayerRows.Where(r => !r.Layer.IsBackground).ToList();
            vm.SelectLayer(rows[0], toggle: false, range: false);
            vm.SelectLayer(rows[1], toggle: true, range: false);
            vm.SelectLayer(rows[2], toggle: true, range: false);
            Dispatcher.UIThread.RunJobs();

            var borders = window.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("layerRow")).ToList();
            var selected = borders.First(b => b.Classes.Contains("selected") && !b.Classes.Contains("active"));
            var active = borders.First(b => b.Classes.Contains("active"));

            var hover = Resource(window, "HoverTintBrush");
            Assert.True(Strength(selected.Background) > hover,
                $"{theme}: selected {Strength(selected.Background):0.###} is not above hover {hover:0.###}");
            Assert.True(Strength(selected.Background) < Strength(active.Background),
                $"{theme}: selected {Strength(selected.Background):0.###} is not below active {Strength(active.Background):0.###}");
            Assert.True(HasBar(selected.BoxShadow));
            Assert.True(HasBar(active.BoxShadow));

            var plain = borders.FirstOrDefault(b => !b.Classes.Contains("selected"));
            Assert.NotNull(plain);
            Assert.False(HasBar(plain!.BoxShadow));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Dark-lit")]
    [InlineData("Studio grey")]
    public void APickedFolderIsDrawnLikeASelectedLayer(string theme)
    {
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            vm.ThemeChoice = theme;
            while (vm.Doc.Scene.Layers.Count < 3) vm.AddPaintedLayerCommand.Execute(null);
            vm.GroupLayersCommand.Execute(null); // picks the new folder
            Dispatcher.UIThread.RunJobs();

            var header = window.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("groupRow") && b.Classes.Contains("selected"));
            Assert.True(Strength(header.Background) > Resource(window, "HoverTintBrush"));
            Assert.True(HasBar(header.BoxShadow));
        }
        finally
        {
            window.Close();
        }
    }
}
