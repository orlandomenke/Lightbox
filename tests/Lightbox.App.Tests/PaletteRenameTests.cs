using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// Renaming a palette or folder in the palette docker, typed through the real
/// window.
/// </summary>
/// <remarks>
/// The view-model tests set <see cref="PaletteNode.Name"/> in one assignment,
/// which is not what a keyboard does: a keyboard writes one letter at a time,
/// and the box wrote each one straight through. For a project palette every
/// letter was a project edit, the edit reloaded the docker, and the box was
/// gone after the first letter — while every test that assigned a whole name
/// at once stayed green.
/// </remarks>
public sealed class PaletteRenameTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-palrename-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private (MainWindow Window, MainViewModel Vm, Project Project) Open()
    {
        var window = new MainWindow { Width = 1400, Height = 900 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        var project = ProjectIo.Create("Knight", _root);
        vm.ProjectDocker.Project = project;
        vm.Workspace.SetVisible(Lightbox.App.Docking.DockPanelId.Palette, true);
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Palette);
        Pump();
        return (window, vm, project);
    }

    private static PaletteNode Node(MainViewModel vm, Func<PaletteNode, bool> match) =>
        All(vm.PaletteDocker.Tree).First(match);

    private static IEnumerable<PaletteNode> All(IEnumerable<PaletteNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in All(node.Children)) yield return child;
        }
    }

    /// <summary>The rename box on screen for this row, if the tree has realised one.</summary>
    private static TextBox? BoxOf(MainWindow window, PaletteNode node) =>
        window.FindControl<TreeView>("PaletteTree")!
            .GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(b => ReferenceEquals(b.DataContext, node));

    private static TextBox StartRenaming(MainWindow window, MainViewModel vm, PaletteNode node)
    {

        // What the row menu's Rename does.
        node.IsRenaming = true;
        Pump();
        var box = BoxOf(window, node);
        Assert.NotNull(box);
        Assert.True(box!.IsEffectivelyVisible, "Rename must put a text box on the row");
        return box;
    }

    private static void Type(MainWindow window, string text)
    {
        foreach (var c in text)
        {
            window.KeyTextInput(c.ToString());
            Pump();
        }
    }

    [AvaloniaFact]
    public void AProjectPaletteTakesAWholeNameTypedALetterAtATime()
    {
        var (window, vm, project) = Open();
        var palette = new Palette { Name = "Palette" };
        project.Palettes.Add(palette);
        vm.PaletteDocker.Load(vm.Doc);
        Pump();

        var node = Node(vm, n => n.Palette?.Id == palette.Id);
        var box = StartRenaming(window, vm, node);
        Assert.True(box.IsFocused, "Rename must put the caret in the box — the artist should not have to click it");

        Type(window, "Skin");

        var live = BoxOf(window, Node(vm, n => n.Palette?.Id == palette.Id));
        Assert.True(live is { IsEffectivelyVisible: true, IsFocused: true },
            "the box must survive typing — it collapsed after one letter on a project palette");

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Equal("Skin", project.Palettes.Single(p => p.Id == palette.Id).Name);
        Assert.False(Node(vm, n => n.Palette?.Id == palette.Id).IsRenaming);
    }

    [AvaloniaFact]
    public void ADocumentPaletteTakesAWholeNameTypedALetterAtATime()
    {
        var (window, vm, _) = Open();
        var palette = vm.Doc.Palettes[0];

        var node = Node(vm, n => n.Palette?.Id == palette.Id);
        StartRenaming(window, vm, node);
        Type(window, "Skin");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();

        Assert.Equal("Skin", vm.Doc.Palettes.Single(p => p.Id == palette.Id).Name);
    }

    [AvaloniaFact]
    public void TypingARenameIsOneEditNotOnePerLetter()
    {
        // A name is one decision. Undoing a rename should take the whole name
        // back, not peel it a letter at a time.
        var (window, vm, _) = Open();
        var palette = vm.Doc.Palettes[0];
        var before = palette.Name;

        StartRenaming(window, vm, Node(vm, n => n.Palette?.Id == palette.Id));
        Type(window, "Skin");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Pump();
        Assert.Equal("Skin", vm.Doc.Palettes.Single(p => p.Id == palette.Id).Name);

        vm.UndoCommand.Execute(null);
        Pump();

        Assert.Equal(before, vm.Doc.Palettes.Single(p => p.Id == palette.Id).Name);
    }

    [AvaloniaFact]
    public void EscapeLeavesTheNameAsItWas()
    {
        var (window, vm, project) = Open();
        var palette = new Palette { Name = "Palette" };
        project.Palettes.Add(palette);
        vm.PaletteDocker.Load(vm.Doc);
        Pump();

        StartRenaming(window, vm, Node(vm, n => n.Palette?.Id == palette.Id));
        Type(window, "Skin");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Pump();

        Assert.Equal("Palette", project.Palettes.Single(p => p.Id == palette.Id).Name);
        Assert.False(Node(vm, n => n.Palette?.Id == palette.Id).IsRenaming);
    }

    [AvaloniaFact]
    public void AFolderTheArtistClosedStaysClosedThroughARebuild()
    {
        var (_, vm, project) = Open();
        project.Palettes.Add(new Palette { Name = "Shared" });
        vm.PaletteDocker.Load(vm.Doc);
        Pump();

        Node(vm, n => n.Kind == PaletteNodeKind.Scope && n.Scope == PaletteScope.Project).IsExpanded = false;
        vm.PaletteDocker.Load(vm.Doc);
        Pump();

        Assert.False(Node(vm, n => n.Kind == PaletteNodeKind.Scope && n.Scope == PaletteScope.Project).IsExpanded);
        Assert.True(Node(vm, n => n.Kind == PaletteNodeKind.Scope && n.Scope == PaletteScope.Document).IsExpanded);
    }
}
