using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Docking;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Putting a palette's swatches in the order the artist wants, by dragging one
/// between two others — and seeing where it will land before letting go.
/// </summary>
/// <remarks>
/// The layers panel shows a drop as a line on the edge the row will land
/// against; a swatch grid does the same with a line beside the swatch. The
/// line is what makes the drop predictable, so it is tested as pixels on
/// screen, not only as a flag on the row.
/// </remarks>
[Collection("BrushState")]
public sealed class SwatchOrderTests(ITestOutputHelper output) : BrushStateIsolated, IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-swatchorder-{Guid.NewGuid():N}.lbproj");

    public new void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        base.Dispose();
    }

    private static readonly string[] Abcd = ["A", "B", "C", "D"];

    /// <summary>A document palette of four swatches named A to D.</summary>
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.Doc.Palettes.Clear();
        vm.Doc.Palettes.Add(Four());
        vm.PaletteDocker.Load(vm.Doc);
        return vm;
    }

    private static Palette Four() => new()
    {
        Name = "Four",
        Swatches =
        [
            new Swatch { Name = "A", Color = "#ff0000" },
            new Swatch { Name = "B", Color = "#00ff00" },
            new Swatch { Name = "C", Color = "#0000ff" },
            new Swatch { Name = "D", Color = "#ffff00" },
        ],
    };

    private static SwatchRow Row(MainViewModel vm, string name) =>
        vm.PaletteDocker.Swatches.Single(s => s.Name == name);

    private static string[] Order(Palette palette) => palette.Swatches.Select(s => s.Name!).ToArray();

    private static string[] Shown(MainViewModel vm) => vm.PaletteDocker.Swatches.Select(s => s.Name!).ToArray();

    // ---- the move -------------------------------------------------------------

    [AvaloniaFact]
    public void DroppingASwatchBeforeAnotherPutsItThere()
    {
        var vm = Vm();
        var ids = vm.Doc.Palettes[0].Swatches.ToDictionary(s => s.Name!, s => s.Id);

        Assert.True(vm.PaletteDocker.ReorderSwatch(Row(vm, "D"), Row(vm, "B"), SwatchDropSide.Before));

        Assert.Equal(["A", "D", "B", "C"], Order(vm.Doc.Palettes[0]));
        Assert.Equal(["A", "D", "B", "C"], Shown(vm));
        // A reorder is not a copy: every stroke painted with D still follows it.
        Assert.All(vm.Doc.Palettes[0].Swatches, s => Assert.Equal(ids[s.Name!], s.Id));
    }

    [AvaloniaFact]
    public void DroppingAfterTheLastSwatchPutsItAtTheEnd()
    {
        var vm = Vm();

        Assert.True(vm.PaletteDocker.ReorderSwatch(Row(vm, "A"), Row(vm, "D"), SwatchDropSide.After));

        Assert.Equal(["B", "C", "D", "A"], Order(vm.Doc.Palettes[0]));
    }

    [AvaloniaFact]
    public void DroppingOnTheGapsEitherSideOfItselfChangesNothing()
    {
        // B before C and B after A are where B already is. A drop that moves
        // nothing must not leave an undo step that undoes nothing.
        var vm = Vm();
        var depth = vm.UndoDepth;

        Assert.False(vm.PaletteDocker.ReorderSwatch(Row(vm, "B"), Row(vm, "C"), SwatchDropSide.Before));
        Assert.False(vm.PaletteDocker.ReorderSwatch(Row(vm, "B"), Row(vm, "A"), SwatchDropSide.After));
        Assert.False(vm.PaletteDocker.ReorderSwatch(Row(vm, "B"), Row(vm, "B"), SwatchDropSide.Before));

        Assert.Equal(Abcd, Order(vm.Doc.Palettes[0]));
        Assert.Equal(depth, vm.UndoDepth);
    }

    [AvaloniaFact]
    public void AReorderIsOneUndoStep()
    {
        var vm = Vm();
        vm.PaletteDocker.ReorderSwatch(Row(vm, "A"), Row(vm, "C"), SwatchDropSide.After);
        Assert.Equal(["B", "C", "A", "D"], Order(vm.Doc.Palettes[0]));

        vm.UndoCommand.Execute(null);

        Assert.Equal(Abcd, Order(vm.Doc.Palettes[0]));
        Assert.Equal(Abcd, Shown(vm));
    }

    [AvaloniaFact]
    public void TheSelectedSwatchStaysSelectedThroughTheMove()
    {
        var vm = Vm();
        vm.PaletteDocker.SelectedSwatch = Row(vm, "D");

        vm.PaletteDocker.ReorderSwatch(Row(vm, "D"), Row(vm, "A"), SwatchDropSide.Before);

        Assert.Equal(["D", "A", "B", "C"], Shown(vm));
        Assert.Equal("D", vm.PaletteDocker.SelectedSwatch?.Name);
    }

    [AvaloniaFact]
    public void AProjectPaletteIsReorderedInTheProject()
    {
        var vm = VmLayers.PaperVm();
        vm.Doc.Palettes.Clear();
        var project = ProjectIo.Create("Knight", _root);
        vm.ProjectDocker.Project = project;
        var palette = Four();
        project.Palettes.Add(palette);
        vm.PaletteDocker.Load(vm.Doc);
        vm.PaletteDocker.SelectedPalette = palette;

        Assert.True(vm.PaletteDocker.ReorderSwatch(Row(vm, "C"), Row(vm, "A"), SwatchDropSide.Before));

        Assert.Equal(["C", "A", "B", "D"], Order(project.Palettes.Single()));
        Assert.Equal(["C", "A", "B", "D"], Shown(vm));
    }

    // ---- where it will land ---------------------------------------------------

    [Theory]
    [InlineData(0, SwatchDropSide.Before)]
    [InlineData(12, SwatchDropSide.Before)]
    [InlineData(14, SwatchDropSide.After)]
    [InlineData(26, SwatchDropSide.After)]
    public void TheHalfOfTheSwatchUnderThePointerSaysWhichSide(double x, SwatchDropSide side) =>
        Assert.Equal(side, PaletteDockerViewModel.SideOf(x, 26));

    [AvaloniaFact]
    public void OnlyTheSwatchUnderThePointerShowsALine()
    {
        var vm = Vm();

        vm.PaletteDocker.ShowSwatchDrop(Row(vm, "B"), SwatchDropSide.Before);
        vm.PaletteDocker.ShowSwatchDrop(Row(vm, "C"), SwatchDropSide.After);

        Assert.Equal(SwatchDropSide.None, Row(vm, "B").DropSide);
        Assert.True(Row(vm, "C").DropAfter);
        Assert.False(Row(vm, "C").DropBefore);

        vm.PaletteDocker.ShowSwatchDrop(null, SwatchDropSide.None);
        Assert.All(vm.PaletteDocker.Swatches, s => Assert.Equal(SwatchDropSide.None, s.DropSide));
    }

    [AvaloniaFact]
    public void ADropClearsTheLine()
    {
        var vm = Vm();
        Row(vm, "B").DropSide = SwatchDropSide.Before;
        Row(vm, "C").DropSide = SwatchDropSide.After;

        vm.PaletteDocker.ReorderSwatch(Row(vm, "D"), Row(vm, "B"), SwatchDropSide.Before);

        Assert.All(vm.PaletteDocker.Swatches, s => Assert.Equal(SwatchDropSide.None, s.DropSide));
    }

    [AvaloniaFact]
    public void TheLineIsStyledOntoTheSideItWillLand()
    {
        // What the swatch's style resolves to, through the real window. This
        // host draws nothing (headless drawing), so the pixels are checked in
        // the gallery's palette-drop scene; what is pinned here is that the
        // hint reaches a style at all, on the right side, in the drop accent.
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        var vm = (MainViewModel)window.DataContext!;
        vm.Doc.Palettes.Clear();
        vm.Doc.Palettes.Add(Four());
        vm.PaletteDocker.Load(vm.Doc);
        vm.Workspace.SetVisible(DockPanelId.Palette, true);
        vm.Workspace.Activate(DockPanelId.Palette);
        Pump();

        BoxShadow Shadow(SwatchDropSide side)
        {
            vm.PaletteDocker.ShowSwatchDrop(Row(vm, "B"), side);
            Pump();
            var shadows = SwatchBox(window, "B").BoxShadow;
            output.WriteLine($"{side}: {shadows}");
            return shadows.Count == 0 ? default : shadows[0];
        }

        Assert.Equal(default, Shadow(SwatchDropSide.None));
        var before = Shadow(SwatchDropSide.Before);
        var after = Shadow(SwatchDropSide.After);
        Assert.True(before.OffsetX < 0, "a drop before B is a line on its left");
        Assert.True(after.OffsetX > 0, "a drop after B is a line on its right");
        Assert.All(new[] { before, after }, s => Assert.Equal(Color.Parse("#FF6A3D"), s.Color));
        Assert.Equal(default, Shadow(SwatchDropSide.None));
    }

    // ---- what the pointer is over, through the window's own hit test ----------

    private static (MainWindow Window, MainViewModel Vm) Window(int swatches = 4)
    {
        // Tall: at 900 the docker's swatch half is squeezed under the status
        // bar, and the hit test lands on that instead.
        var window = new MainWindow { Width = 1400, Height = 1500 };
        window.Show();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Swatches", 400, 300, 12, 72, "#ffffff", false));
        vm.Doc.Palettes.Clear();
        var palette = Four();
        for (var i = 4; i < swatches; i++) palette.Swatches.Add(new Swatch { Name = $"S{i}", Color = "#808080" });
        vm.Doc.Palettes.Add(palette);
        vm.PaletteDocker.Load(vm.Doc);
        vm.Workspace.SetVisible(DockPanelId.Palette, true);
        vm.Workspace.Activate(DockPanelId.Palette);
        Pump();
        return (window, vm);
    }

    private static ListBoxItem ItemOf(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => i.DataContext is SwatchRow row && row.Name == name);

    /// <summary>Where a drop at this point of an item's own box would land.</summary>
    private static (string? Target, SwatchDropSide Side) DropAt(MainWindow window, MainViewModel vm, Visual over, Point local)
    {
        var point = over.TranslatePoint(local, window)!.Value;
        var hit = window.InputHitTest(point) as Visual;
        var (target, side) = MainWindow.SwatchDropAt(
            hit, v => window.TranslatePoint(point, v)!.Value, vm.PaletteDocker.Swatches);
        return (target?.Name, side);
    }

    [AvaloniaFact]
    public void ADropInTheGapBetweenTwoSwatchesLandsInThatGap()
    {
        // The gap is each item's 2px padding. It is where a hand aims, and it
        // is not the swatch: a walk that only recognised the swatch sent every
        // drop there to the end of the palette.
        var (window, vm) = Window();
        var c = ItemOf(window, "C");
        var w = c.Bounds.Width;
        var h = c.Bounds.Height;

        Assert.Equal(("C", SwatchDropSide.Before), DropAt(window, vm, c, new Point(0.5, h / 2)));
        Assert.Equal(("C", SwatchDropSide.Before), DropAt(window, vm, c, new Point(1.5, h / 2)));
        Assert.Equal(("C", SwatchDropSide.After), DropAt(window, vm, c, new Point(w - 0.5, h / 2)));
        Assert.Equal(("C", SwatchDropSide.Before), DropAt(window, vm, c, new Point(w / 2 - 1, 0.5)));
    }

    [AvaloniaFact]
    public void ADropOverASwatchTakesTheHalfUnderThePointer()
    {
        var (window, vm) = Window();
        var b = ItemOf(window, "B");

        Assert.Equal(("B", SwatchDropSide.Before), DropAt(window, vm, b, new Point(6, 12)));
        Assert.Equal(("B", SwatchDropSide.After), DropAt(window, vm, b, new Point(b.Bounds.Width - 6, 12)));
    }

    [AvaloniaFact]
    public void ADropPastTheEndOfARowLandsAfterThatRowsLastSwatch()
    {
        // Enough swatches to wrap: the space after a full row's last swatch is
        // beside that swatch, not at the end of the palette.
        var (window, vm) = Window(swatches: 40);
        var items = window.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.DataContext is SwatchRow).ToList();
        var firstRowY = items[0].Bounds.Y;
        var lastOfRow = items.Last(i => Math.Abs(i.Bounds.Y - firstRowY) < 0.5);
        var name = ((SwatchRow)lastOfRow.DataContext!).Name;
        Assert.NotEqual("S39", name);
        var list = lastOfRow.FindAncestorOfType<ListBox>()!;
        var gapStart = lastOfRow.TranslatePoint(new Point(lastOfRow.Bounds.Width + 1, lastOfRow.Bounds.Height / 2), list)!.Value;
        output.WriteLine($"row ends after {name} at x={gapStart.X} of list {list.Bounds.Width}");
        if (gapStart.X >= list.Bounds.Width - 1) return; // the row filled the width exactly: no space to test

        Assert.Equal((name, SwatchDropSide.After), DropAt(window, vm, list, gapStart));
    }

    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static Border SwatchBox(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Border>()
            .First(b => b.DataContext is SwatchRow row && row.Name == name && b.Classes.Contains("swatch"));
}
