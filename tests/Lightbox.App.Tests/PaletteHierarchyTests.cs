using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;

namespace Lightbox.App.Tests;

/// <summary>
/// The palette hierarchy as the docker presents it: folders, what goes in
/// them, and the two scopes — the document's palettes and the project's — that
/// must never be filed into each other.
/// </summary>
/// <remarks>
/// The rules of the tree itself live in <c>PaletteTreeTests</c> over in Core.
/// This is about the docker: which rows appear, what a move does to the model,
/// and whether the two stores are kept apart.
/// </remarks>
public class PaletteHierarchyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"lightbox-pal-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static MainViewModel Vm()
    {
        var vm = new MainViewModel(null);
        // The starting black-and-white palette would be an extra row in every
        // assertion about which rows are where.
        vm.Doc.Palettes.Clear();
        vm.PaletteDocker.Load(vm.Doc);
        return vm;
    }

    private static PaletteNode Node(MainViewModel vm, string name) =>
        All(vm.PaletteDocker.Tree).First(n => n.Name == name);

    private static IEnumerable<PaletteNode> All(IEnumerable<PaletteNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in All(node.Children)) yield return child;
        }
    }

    private static PaletteNode AddFolder(MainViewModel vm, string name)
    {
        vm.PaletteDocker.AddFolderCommand.Execute(null);
        var node = vm.PaletteDocker.SelectedNode!;
        node.Name = name;
        return node;
    }

    private static PaletteNode AddPalette(MainViewModel vm, string name)
    {
        vm.PaletteDocker.AddPaletteCommand.Execute(null);
        var node = All(vm.PaletteDocker.Tree).First(n => n.Palette?.Id == vm.PaletteDocker.SelectedPalette!.Id);
        node.Name = name;
        return node;
    }

    // ---- absence --------------------------------------------------------------

    [AvaloniaFact]
    public void ADocumentWithNoFoldersHasNoFolderMachinery()
    {
        // Optional means absent. A single-file document with three palettes
        // must not carry a filing system it never asked for, and must not show
        // a "Document" heading when there is nothing to tell it apart from.
        var vm = Vm();
        AddPalette(vm, "Scratch");

        Assert.Null(vm.Doc.PaletteFolders);
        Assert.All(vm.PaletteDocker.Tree, n => Assert.NotEqual(PaletteNodeKind.Scope, n.Kind));
        Assert.Single(vm.PaletteDocker.Tree);
    }

    [AvaloniaFact]
    public void DeletingTheLastFolderPutsTheDocumentBackToHavingNone()
    {
        var vm = Vm();
        var folder = AddFolder(vm, "Characters");
        Assert.NotNull(vm.Doc.PaletteFolders);

        vm.PaletteDocker.SelectedNode = folder;
        vm.PaletteDocker.RemovePaletteCommand.Execute(null);

        Assert.Null(vm.Doc.PaletteFolders);
    }

    // ---- folders --------------------------------------------------------------

    [AvaloniaFact]
    public void ANewFolderLandsInsideWhateverIsSelected()
    {
        // Where the artist was looking, not the bottom of the panel.
        var vm = Vm();
        AddFolder(vm, "Characters");
        var knight = AddFolder(vm, "Knight");

        // Re-read: a structural change rebuilds the rows, so the node object
        // from before the second folder was made is a stale view.
        var characters = Node(vm, "Characters");
        Assert.Equal(characters.Folder!.Id, knight.Folder!.ParentId);
        Assert.Contains(characters.Children, n => n.Name == "Knight");
    }

    [AvaloniaFact]
    public void ANewPaletteLandsInTheSelectedFolder()
    {
        var vm = Vm();
        var characters = AddFolder(vm, "Characters");
        var armour = AddPalette(vm, "Armour");

        Assert.Equal(characters.Folder!.Id, armour.Palette!.FolderId);
    }

    [AvaloniaFact]
    public void AnEmptyFolderStaysInTheTree()
    {
        // The reason folders are records rather than a path on the palette:
        // people file before they have anything to file.
        var vm = Vm();
        AddFolder(vm, "Backgrounds");

        vm.PaletteDocker.Load(vm.Doc);

        Assert.Contains(All(vm.PaletteDocker.Tree), n => n.Name == "Backgrounds" && n.IsFolder);
    }

    [AvaloniaFact]
    public void RenamingARowRenamesTheModel()
    {
        var vm = Vm();
        var folder = AddFolder(vm, "Characters");
        var palette = AddPalette(vm, "Armour");

        Assert.Equal("Characters", folder.Folder!.Name);
        Assert.Equal("Armour", palette.Palette!.Name);

        // And a blank name snaps back rather than leaving a row you cannot read.
        folder.Name = "   ";
        Assert.Equal("Characters", folder.Folder.Name);
    }

    // ---- moving ---------------------------------------------------------------

    [AvaloniaFact]
    public void DraggingAPaletteOntoAFolderFilesIt()
    {
        var vm = Vm();
        var characters = AddFolder(vm, "Characters");
        vm.PaletteDocker.SelectedNode = null;
        var armour = AddPalette(vm, "Armour");
        Assert.Null(armour.Palette!.FolderId);

        Assert.True(vm.PaletteDocker.Drop(armour, characters));

        Assert.Equal(characters.Folder!.Id, armour.Palette.FolderId);
    }

    [AvaloniaFact]
    public void DroppingOntoAPaletteMeansBesideIt()
    {
        var vm = Vm();
        var characters = AddFolder(vm, "Characters");
        var armour = AddPalette(vm, "Armour");
        vm.PaletteDocker.SelectedNode = null;
        var cloth = AddPalette(vm, "Cloth");

        Assert.True(vm.PaletteDocker.Drop(cloth, armour));

        Assert.Equal(characters.Folder!.Id, cloth.Palette!.FolderId);
    }

    [AvaloniaFact]
    public void AFolderCannotBeDroppedIntoItself()
    {
        var vm = Vm();
        var characters = AddFolder(vm, "Characters");
        var knight = AddFolder(vm, "Knight");

        Assert.False(vm.PaletteDocker.Drop(characters, knight));
        Assert.False(vm.PaletteDocker.Drop(characters, characters));

        Assert.Null(characters.Folder!.ParentId);
        Assert.Equal(characters.Folder.Id, knight.Folder!.ParentId);
    }

    [AvaloniaFact]
    public void AssignToOffersTheTopLevelAndEveryFolderByItsPath()
    {
        var vm = Vm();
        AddFolder(vm, "Characters");
        AddFolder(vm, "Knight");

        var labels = vm.PaletteDocker.AssignTargets.Select(t => t.Label).ToList();

        Assert.Contains("(top level)", labels);
        Assert.Contains("Characters", labels);
        // The path, not the bare name — it is what tells two folders with the
        // same name apart, which a nested menu cannot.
        Assert.Contains("Characters / Knight", labels);
    }

    [AvaloniaFact]
    public void AssignAndDragAgreeBecauseTheyAreTheSameCall()
    {
        var vm = Vm();
        var characters = AddFolder(vm, "Characters");
        vm.PaletteDocker.SelectedNode = null;
        var armour = AddPalette(vm, "Armour");

        var target = vm.PaletteDocker.AssignTargets.First(t => t.FolderId == characters.Folder!.Id);
        Assert.True(vm.PaletteDocker.Assign(armour, target));
        Assert.Equal(characters.Folder!.Id, armour.Palette!.FolderId);

        var top = vm.PaletteDocker.AssignTargets.First(t => t.FolderId is null);
        Assert.True(vm.PaletteDocker.Assign(armour, top));
        Assert.Null(armour.Palette.FolderId);
    }

    [AvaloniaFact]
    public void DeletingAFolderKeepsThePalettesInIt()
    {
        // Tidying up must not be the fastest way to lose an afternoon's work.
        var vm = Vm();
        var characters = AddFolder(vm, "Characters");
        var armour = AddPalette(vm, "Armour");

        vm.PaletteDocker.SelectedNode = characters;
        vm.PaletteDocker.RemovePaletteCommand.Execute(null);

        Assert.Contains(vm.Doc.Palettes, p => p.Id == armour.Palette!.Id);
        Assert.Null(vm.Doc.Palettes.Single(p => p.Id == armour.Palette!.Id).FolderId);
    }

    // ---- the project scope ----------------------------------------------------

    private MainViewModel WithProject(out Project project)
    {
        var vm = Vm();
        project = ProjectIo.Create("Knight", _root);
        vm.ProjectDocker.Project = project;
        vm.PaletteDocker.Load(vm.Doc);
        return vm;
    }

    [AvaloniaFact]
    public void WithAProjectOpenBothScopesGetAHeading()
    {
        var vm = WithProject(out var project);
        project.Palettes.Add(new Palette { Name = "Shared" });
        vm.PaletteDocker.Load(vm.Doc);

        Assert.Equal(2, vm.PaletteDocker.Tree.Count);
        Assert.All(vm.PaletteDocker.Tree, n => Assert.Equal(PaletteNodeKind.Scope, n.Kind));
        Assert.Equal(["Document", "Project"], vm.PaletteDocker.Tree.Select(n => n.Name));
        Assert.Contains(vm.PaletteDocker.Tree[1].Children, n => n.Name == "Shared");
    }

    [AvaloniaFact]
    public void TheProjectOpensWithItsHierarchyAlreadyInPlace()
    {
        // Saved with the project and read back with it, so a project someone
        // filed last week opens filed.
        var vm = WithProject(out var project);
        var characters = new PaletteFolder { Name = "Characters" };
        project.PaletteFolders.Add(characters);
        project.Palettes.Add(new Palette { Name = "Shared", FolderId = characters.Id });
        ProjectIo.Save(project);

        var reopened = ProjectIo.Load(_root);
        vm.ProjectDocker.Project = reopened;
        vm.PaletteDocker.Load(vm.Doc);

        var folder = Node(vm, "Characters");
        Assert.True(folder.IsFolder);
        Assert.Equal(PaletteScope.Project, folder.Scope);
        Assert.Contains(folder.Children, n => n.Name == "Shared");
    }

    [AvaloniaFact]
    public void NothingCrossesBetweenTheDocumentAndTheProject()
    {
        // Not a filing move: a change of ownership that would re-point, or
        // orphan, every stroke referencing the palette.
        var vm = WithProject(out var project);
        var projectFolder = new PaletteFolder { Name = "Shared colours" };
        project.PaletteFolders.Add(projectFolder);
        AddPalette(vm, "Armour");
        vm.PaletteDocker.Load(vm.Doc);

        var armour = Node(vm, "Armour");
        var folder = Node(vm, "Shared colours");
        Assert.Equal(PaletteScope.Document, armour.Scope);
        Assert.Equal(PaletteScope.Project, folder.Scope);

        Assert.False(vm.PaletteDocker.Drop(armour, folder));
        Assert.Null(armour.Palette!.FolderId);
        // And the menu does not offer it either.
        Assert.DoesNotContain(
            vm.PaletteDocker.AssignTargets.Where(t => t.Scope == PaletteScope.Project),
            t => vm.PaletteDocker.CanAssign(armour, t));
    }

    [AvaloniaFact]
    public void AFolderMadeWithAProjectRowSelectedBelongsToTheProject()
    {
        var vm = WithProject(out var project);
        project.Palettes.Add(new Palette { Name = "Shared" });
        vm.PaletteDocker.Load(vm.Doc);

        vm.PaletteDocker.SelectedNode = Node(vm, "Shared");
        vm.PaletteDocker.AddFolderCommand.Execute(null);

        Assert.Single(project.PaletteFolders);
        Assert.Null(vm.Doc.PaletteFolders);
    }

    [AvaloniaFact]
    public void ASwatchAddedToAProjectPaletteLandsInTheProject()
    {
        // The docker's swatch buttons used to reach only into the document, so
        // with a project palette selected they did nothing at all.
        var vm = WithProject(out var project);
        var shared = new Palette { Name = "Shared" };
        project.Palettes.Add(shared);
        vm.PaletteDocker.Load(vm.Doc);
        vm.PaletteDocker.SelectedPalette = shared;

        vm.ColorHex = "#3070b0";
        vm.PaletteDocker.AddSwatchCommand.Execute(null);

        Assert.Equal("#3070b0", Assert.Single(shared.Swatches).Color);
    }

    // ---- the panel's shape (B174) ----------------------------------------------
    // Asserted against the XAML text, the way IconSetTests walks the views:
    // the defect was two attributes — a hard-coded Height in an Auto row, and a
    // transparent splitter — and a view-model test cannot see either.

    private static string PaletteBlock()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", "Views", "MainWindow.axaml"));

        // The hierarchy grid: from the tree's name to the end of its grid.
        var start = xaml.IndexOf("x:Name=\"PaletteTree\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the palette tree is no longer in MainWindow.axaml — move these assertions with it");
        var gridStart = xaml.LastIndexOf("<Grid", start, StringComparison.Ordinal);
        var gridEnd = xaml.IndexOf("</Grid>", start, StringComparison.Ordinal);
        return xaml[gridStart..(gridEnd + "</Grid>".Length)];
    }

    /// <summary>
    /// <b>B174.</b> The tree sat in an <c>Auto</c> row with <c>Height="128"</c>
    /// hard-coded on the control, so the row measured to 128 whatever the
    /// splitter under it was dragged to — a splitter in the XAML that could not
    /// work. The row must be a sized row the splitter can write, and the
    /// control must not carry a height of its own for the row to un-do.
    /// </summary>
    [AvaloniaFact]
    public void TheHierarchyCanBeResizedAgainstTheSwatches()
    {
        var block = PaletteBlock();

        var tree = TreeTag(block);
        Assert.DoesNotContain("Height=\"128\"", tree);

        // A pixel row with a floor, not Auto: Auto re-measures to content and
        // the splitter's write is thrown away.
        Assert.Contains("<RowDefinition Height=\"128\"", block);
        Assert.Contains("MinHeight", block);
        Assert.Contains("GridSplitter", block);
    }

    /// <summary>The TreeView's opening tag: from &lt;TreeView to its drop attribute.</summary>
    private static string TreeTag(string block)
    {
        var start = block.IndexOf("<TreeView", StringComparison.Ordinal);
        var end = block.IndexOf("DragDrop.AllowDrop", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the palette TreeView's tag has changed shape");
        return block[start..end];
    }

    /// <summary>A folder tree deeper than the pane scrolls rather than clipping.</summary>
    [AvaloniaFact]
    public void TheHierarchyScrollsRatherThanClippingItsDeepestFolder()
    {
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", TreeTag(PaletteBlock()));
    }

    /// <summary>
    /// The splitter is the seam between the two panes, and a transparent one
    /// leaves them reading as one flat field. No Background override: the
    /// window's GridSplitter style draws the same scored line every other
    /// splitter wears.
    /// </summary>
    [AvaloniaFact]
    public void TheHierarchyAndTheSwatchesReadAsTwoPanes()
    {
        var block = PaletteBlock();
        var splitter = block.Substring(
            block.IndexOf("<GridSplitter", StringComparison.Ordinal),
            block.IndexOf("ResizeDirection=\"Rows\"", StringComparison.Ordinal)
                - block.IndexOf("<GridSplitter", StringComparison.Ordinal));
        Assert.DoesNotContain("Background=\"Transparent\"", splitter);
    }
}
