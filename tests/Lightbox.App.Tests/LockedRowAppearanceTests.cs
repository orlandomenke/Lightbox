using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Xunit;
using Path = Avalonia.Controls.Shapes.Path;

namespace Lightbox.App.Tests;

/// <summary>
/// A locked row says "locked" with its padlock and its name, not with an
/// outline round the row.
/// </summary>
/// <remarks>
/// <para>
/// The outline was cyan, one pixel, round the whole row — and a ring round a
/// row is what "this is the one you are on" looks like everywhere else, so a
/// locked layer read as the active one. The owner's report, 2026-10-09.
/// </para>
/// <para>
/// What replaces it: the padlock is shut on every row that refuses edits —
/// including one that is only locked by the folder it is in, whose padlock
/// used to stay open — and the row's name is drawn in the quieter text colour.
/// Nothing is drawn round the row.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LockedRowAppearanceTests : BrushStateIsolated
{
    private static LayerRow Row(MainViewModel vm, string name) =>
        vm.LayerRows.Single(r => r.Layer.Name == name);

    private static GroupRow HeaderOf(MainViewModel vm, string folder) =>
        vm.LayerPanelItems.OfType<GroupRow>().Single(g => g.Group.Name == folder);

    /// <summary>Bottom to top: paper, a, [Outer: b, c, [Inner: d, e]], f.</summary>
    private static MainViewModel Nested(MainViewModel? into = null)
    {
        var vm = into ?? VmLayers.PaperVm();
        while (vm.Doc.Scene.Layers.Count < 7) vm.AddPaintedLayerCommand.Execute(null);
        string[] names = ["a", "b", "c", "d", "e", "f"];
        for (var i = 1; i < 7; i++) vm.LayerRows.Single(r => r.SceneIndex == i).Name = names[i - 1];
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
    public void ALockedFolderReachesEveryRowBeneathIt_FoldersIncluded()
    {
        var vm = Nested();

        HeaderOf(vm, "Outer").Locked = true;

        foreach (var name in new[] { "b", "c", "d", "e" })
        {
            Assert.True(Row(vm, name).LockedByFolder, $"{name} is not marked locked by its folder");
            Assert.False(Row(vm, name).Locked, $"{name}'s own lock was written");
        }
        Assert.True(HeaderOf(vm, "Inner").LockedByFolder);
        Assert.True(HeaderOf(vm, "Inner").EditsBlocked);
        Assert.False(HeaderOf(vm, "Outer").LockedByFolder);
        Assert.True(HeaderOf(vm, "Outer").EditsBlocked);
        Assert.False(Row(vm, "a").LockedByFolder);
        Assert.False(Row(vm, "f").EditsBlocked);

        HeaderOf(vm, "Outer").Locked = false;
        Assert.False(Row(vm, "d").LockedByFolder);
        Assert.False(HeaderOf(vm, "Inner").EditsBlocked);
    }

    [AvaloniaTheory]
    [InlineData("Dark-lit")]
    [InlineData("Studio grey")]
    public void ALockedRowHasNoOutline_AShutPadlock_AndAQuietName(string theme)
    {
        var window = new Views.MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var vm = (MainViewModel)window.DataContext!;
        try
        {
            vm.ThemeChoice = theme;
            Nested(vm);
            Row(vm, "f").Locked = true;            // locked in its own right
            HeaderOf(vm, "Outer").Locked = true;   // and b–e through their folder
            vm.SelectLayer(Row(vm, "a"), toggle: false, range: false);
            Dispatcher.UIThread.RunJobs();

            var list = window.GetVisualDescendants().OfType<ItemsControl>().Single(c => c.Name == "LayerList");
            Assert.True(window.TryFindResource("IconLockClosed", window.ActualThemeVariant, out var shut));
            Assert.True(window.TryFindResource("IconLockOpen", window.ActualThemeVariant, out var open));
            Assert.True(window.TryFindResource("TextSecondaryBrush", window.ActualThemeVariant, out var quiet));
            var quietColor = ((ISolidColorBrush)quiet!).Color;

            (Border Row, ToggleButton Lock, TextBlock Name) Drawn(object item, string name)
            {
                var row = list.ContainerFromItem(item)!.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("layerRow") || b.Classes.Contains("groupRow"));
                var padlock = row.GetVisualDescendants().OfType<ToggleButton>().First(t => t.Classes.Contains("lock"));
                var label = row.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name);
                return (row, padlock, label);
            }

            var locked = new (object Item, string Name, bool Own)[]
            {
                (Row(vm, "f"), "f", true),
                (Row(vm, "b"), "b", false),
                (Row(vm, "d"), "d", false),
                (HeaderOf(vm, "Outer"), "Outer", true),
                (HeaderOf(vm, "Inner"), "Inner", false),
            };
            foreach (var (item, name, own) in locked)
            {
                var (row, padlock, label) = Drawn(item, name);
                Assert.True(row.BorderBrush is null || row.BorderThickness == new Thickness(0)
                            || name is "Outer" or "Inner",
                    $"{theme}: {name} is outlined ({row.BorderThickness})");
                // A folder header keeps its colour bar on the left edge and nothing else.
                Assert.Equal(0, row.BorderThickness.Top);
                Assert.Equal(0, row.BorderThickness.Right);
                Assert.Equal(0, row.BorderThickness.Bottom);
                Assert.Same(shut, padlock.GetVisualDescendants().OfType<Path>().Single().Data);
                Assert.Equal(own, padlock.IsChecked);
                Assert.Equal(quietColor, ((ISolidColorBrush)label.Foreground!).Color);
            }

            var (freeRow, freeLock, freeName) = Drawn(Row(vm, "a"), "a");
            Assert.Equal(new Thickness(0), freeRow.BorderThickness);
            Assert.Same(open, freeLock.GetVisualDescendants().OfType<Path>().Single().Data);
            Assert.NotEqual(quietColor, ((ISolidColorBrush)freeName.Foreground!).Color);
        }
        finally
        {
            window.Close();
        }
    }
}
