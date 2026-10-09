using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Lightbox.App.ViewModels;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A folder's colour washes its whole row and, more faintly, the rows of the
/// layers inside it; a subfolder shows its parent's colour until it is given
/// one of its own (Q226).
/// </summary>
/// <remarks>
/// It used to be a four-pixel bar at the left edge of the header and nothing
/// else, so in a deep stack the colour said which header was which and nothing
/// about which layers belonged to it.
/// </remarks>
[Collection("BrushState")]
public class FolderColourRowTests : BrushStateIsolated
{
    private const string Red = "#c25050";
    private const string Green = "#4a9a5e";

    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerPanelItems.OfType<LayerRow>().Single(r => r.Layer.Name == name);

    private static GroupRow HeaderOf(MainViewModel vm, string folder) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single(g => g.Group.Name == folder);

    private static Color? Tint(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

    /// <summary>Bottom to top: paper, a, [Outer: b, c, [Inner: d, e]], f.</summary>
    private static MainViewModel Nested()
    {
        var vm = VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 7) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c", "d", "e", "f"];
        for (var i = 1; i < 7; i++) vm.Doc.Scene.Layers[i].Name = names[i - 1];
        vm.SelectLayer(Row(vm, "b"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "c"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
        vm.SelectLayer(Row(vm, "d"), toggle: false, range: false);
        vm.SelectLayer(Row(vm, "e"), toggle: true, range: false);
        vm.GroupLayersCommand.Execute(null);
        var headers = vm.LayerPanelItems.OfType<GroupRow>().ToList();
        var inner = headers.Single(h => h.Group.Id == Row(vm, "d").Layer.GroupId);
        var outer = headers.Single(h => h != inner);
        outer.Name = "Outer";
        inner.Name = "Inner";
        vm.DropOnLayerPanel(inner, outer, LayerDropHint.Into);
        Assert.Equal(outer.Group.Id, inner.Group.ParentId);
        return vm;
    }

    [AvaloniaFact]
    public void ASubfolderShowsItsParentsColour_UntilItIsGivenOne()
    {
        var vm = Nested();

        HeaderOf(vm, "Outer").PickColor(Red);
        Assert.Equal(Red, HeaderOf(vm, "Inner").Color);
        Assert.False(HeaderOf(vm, "Inner").HasOwnColor);
        Assert.Null(HeaderOf(vm, "Inner").Group.Color);

        HeaderOf(vm, "Inner").PickColor(Green);
        Assert.Equal(Green, HeaderOf(vm, "Inner").Color);
        Assert.Equal(Red, HeaderOf(vm, "Outer").Color);

        HeaderOf(vm, "Inner").PickColor(null); // "Same as parent"
        Assert.Equal(Red, HeaderOf(vm, "Inner").Color);
    }

    /// <summary>Picking the colour a subfolder already inherits still pins it.</summary>
    [AvaloniaFact]
    public void PickingTheInheritedColourMakesItTheSubfoldersOwn()
    {
        var vm = Nested();
        HeaderOf(vm, "Outer").PickColor(Red);

        HeaderOf(vm, "Inner").PickColor(Red);
        HeaderOf(vm, "Outer").PickColor(Green);

        Assert.Equal(Red, HeaderOf(vm, "Inner").Color);
    }

    [AvaloniaFact]
    public void TheColourWashesTheHeaderRow_AndMoreFaintlyTheLayersInside()
    {
        var vm = Nested();
        HeaderOf(vm, "Outer").PickColor(Red);
        var red = Color.Parse(Red);

        var header = Tint(HeaderOf(vm, "Outer").TintBrush);
        var member = Tint(Row(vm, "b").TintBrush);
        var deep = Tint(Row(vm, "d").TintBrush);
        Assert.NotNull(header);
        Assert.NotNull(member);
        Assert.NotNull(deep);
        foreach (var tint in new[] { header.Value, member.Value, deep.Value })
        {
            Assert.Equal((red.R, red.G, red.B), (tint.R, tint.G, tint.B));
        }
        // A wash, not a fill: the row's own active and selected fills are
        // translucent white and have to read through it.
        Assert.InRange(header.Value.A, 1, 96);
        Assert.True(member.Value.A < header.Value.A, $"layer {member.Value.A} vs header {header.Value.A}");
        Assert.Equal(member.Value.A, deep.Value.A);
    }

    [AvaloniaFact]
    public void ALooseLayerIsNotTinted()
    {
        var vm = Nested();
        HeaderOf(vm, "Outer").PickColor(Red);

        Assert.Null(Row(vm, "a").TintBrush);
        Assert.Null(Row(vm, "f").TintBrush);
    }

    /// <summary>
    /// The wash as drawn, not as offered: a brush on the row model that no
    /// style binds is a colour nobody sees, and a binding that fails is silent.
    /// </summary>
    /// <remarks>
    /// Read off the row's container, under the row — and the row itself must
    /// keep the fill its state gives it, because a wash that replaced the
    /// active fill would be B400 again with a colour on it.
    /// </remarks>
    [AvaloniaFact]
    public void TheWashIsPaintedUnderTheRow_AndTheActiveFillStaysOnTop()
    {
        var window = new Views.MainWindow();
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            while (vm.Doc.Scene.Layers.Count < 3) vm.AddPaintedLayerCommand.Execute(null);
            vm.GroupLayersCommand.Execute(null); // the active layer, into a new folder
            var header = vm.LayerPanelItems.OfType<GroupRow>().Single();
            header.PickColor(Red);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var red = Color.Parse(Red);

            var list = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                .OfType<Avalonia.Controls.ItemsControl>().Single(c => c.Name == "LayerList");
            Color? Painted(object item) =>
                Tint((list.ContainerFromItem(item) as Avalonia.Controls.Presenters.ContentPresenter)?.Background);

            var member = vm.LayerPanelItems.OfType<LayerRow>().Single(r => r.Layer.GroupId == header.Group.Id);
            var loose = vm.LayerPanelItems.OfType<LayerRow>().First(r => r.Layer.GroupId is null);
            Assert.Equal(Color.FromArgb(GroupRow.HeaderWashAlpha, red.R, red.G, red.B), Painted(header));
            Assert.Equal(Color.FromArgb(LayerRow.MemberWashAlpha, red.R, red.G, red.B), Painted(member));
            Assert.Null(Painted(loose));

            var active = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(list)
                .OfType<Avalonia.Controls.Border>().Single(b => b.Classes.Contains("layerRow") && b.Classes.Contains("active"));
            Assert.Same(member, active.DataContext);
            Assert.True(window.TryFindResource("ActiveFillBrush", window.ActualThemeVariant, out var fill));
            Assert.Equal(Tint(fill as IBrush), Tint(active.Background));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void UndoTakesTheWashBackOffEveryRow()
    {
        var vm = Nested();
        var before = Tint(Row(vm, "d").TintBrush);

        HeaderOf(vm, "Outer").PickColor(Red);
        Assert.NotEqual(before, Tint(Row(vm, "d").TintBrush));
        vm.UndoCommand.Execute(null);

        Assert.Equal(before, Tint(Row(vm, "d").TintBrush));
        Assert.Null(HeaderOf(vm, "Outer").Group.Color);
    }
}
