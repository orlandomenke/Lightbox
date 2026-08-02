using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The palette hierarchy's rules. Every way a tree goes wrong is shape rather
/// than pixels — a folder inside itself, an orphan pointing at a folder that
/// has been deleted, a delete that takes the colours with it — so none of it
/// needs a window.
/// </summary>
public class PaletteTreeTests
{
    private static PaletteFolder Folder(string name, string? parent = null) =>
        new() { Name = name, ParentId = parent };

    // ---- what is inside what --------------------------------------------------

    [Fact]
    public void AFolderHoldsFoldersAndPalettesAndTheTopLevelIsJustNull()
    {
        var characters = Folder("Characters");
        var knight = Folder("Knight", characters.Id);
        var folders = new List<PaletteFolder> { characters, knight };

        var loose = new Palette { Name = "Scratch" };
        var armour = new Palette { Name = "Armour", FolderId = knight.Id };
        var palettes = new List<Palette> { loose, armour };

        Assert.Equal([characters], PaletteTree.FoldersIn(folders, null));
        Assert.Equal([knight], PaletteTree.FoldersIn(folders, characters.Id));
        Assert.Equal([loose], PaletteTree.PalettesIn(palettes, null));
        Assert.Equal([armour], PaletteTree.PalettesIn(palettes, knight.Id));
        Assert.Empty(PaletteTree.PalettesIn(palettes, characters.Id));
    }

    [Fact]
    public void AFolderCanBeEmptyAndStaysThere()
    {
        // The whole reason folders are records rather than a path string on the
        // palette. Someone files before they have anything to file.
        var folders = new List<PaletteFolder> { Folder("Backgrounds") };

        Assert.Single(PaletteTree.FoldersIn(folders, null));
        Assert.Empty(PaletteTree.PalettesIn([], folders[0].Id));
    }

    [Fact]
    public void ThePathReadsFromTheTopDown()
    {
        var characters = Folder("Characters");
        var knight = Folder("Knight", characters.Id);
        var armour = Folder("Armour", knight.Id);
        var folders = new List<PaletteFolder> { characters, knight, armour };

        Assert.Equal("Characters / Knight / Armour", PaletteTree.PathOf(folders, armour.Id));
        Assert.Equal("", PaletteTree.PathOf(folders, null));
    }

    // ---- moving ---------------------------------------------------------------

    [Fact]
    public void AFolderCanBeMovedUnderAnother()
    {
        var characters = Folder("Characters");
        var knight = Folder("Knight");
        var folders = new List<PaletteFolder> { characters, knight };

        Assert.True(PaletteTree.Reparent(folders, knight.Id, characters.Id));

        Assert.Equal(characters.Id, knight.ParentId);
    }

    [Fact]
    public void AFolderCannotBeDroppedIntoItsOwnDescendant()
    {
        // The move that detaches a branch: the pair still reference each other,
        // so nothing reads as corrupt, and both simply stop appearing anywhere.
        var characters = Folder("Characters");
        var knight = Folder("Knight", characters.Id);
        var armour = Folder("Armour", knight.Id);
        var folders = new List<PaletteFolder> { characters, knight, armour };

        Assert.False(PaletteTree.Reparent(folders, characters.Id, armour.Id));
        Assert.False(PaletteTree.Reparent(folders, characters.Id, characters.Id));

        Assert.Null(characters.ParentId);
        Assert.Equal(characters.Id, knight.ParentId);
    }

    [Fact]
    public void MovingToAFolderThatIsNotThereChangesNothing()
    {
        var knight = Folder("Knight");
        var folders = new List<PaletteFolder> { knight };

        Assert.False(PaletteTree.Reparent(folders, knight.Id, "pf_nowhere"));

        Assert.Null(knight.ParentId);
    }

    [Fact]
    public void MovingToTheTopLevelIsAlwaysAllowed()
    {
        var characters = Folder("Characters");
        var knight = Folder("Knight", characters.Id);
        var folders = new List<PaletteFolder> { characters, knight };

        Assert.True(PaletteTree.Reparent(folders, knight.Id, null));

        Assert.Null(knight.ParentId);
    }

    // ---- deleting -------------------------------------------------------------

    [Fact]
    public void DeletingAFolderKeepsTheColoursAndLiftsThemOneLevel()
    {
        // Tidying up must not be the fastest way to destroy an afternoon's
        // work — the strokes that reference these swatches would go too.
        var characters = Folder("Characters");
        var knight = Folder("Knight", characters.Id);
        var folders = new List<PaletteFolder> { characters, knight };
        var armour = new Palette { Name = "Armour", FolderId = knight.Id };
        var palettes = new List<Palette> { armour };

        PaletteTree.RemoveFolder(folders, palettes, knight.Id);

        Assert.Contains(armour, palettes);
        Assert.Equal(characters.Id, armour.FolderId);
        Assert.Equal([characters], folders);
    }

    [Fact]
    public void DeletingAFolderTakesTheFoldersBeneathItToo()
    {
        var characters = Folder("Characters");
        var knight = Folder("Knight", characters.Id);
        var armour = Folder("Armour", knight.Id);
        var folders = new List<PaletteFolder> { characters, knight, armour };
        var metal = new Palette { Name = "Metal", FolderId = armour.Id };
        var palettes = new List<Palette> { metal };

        PaletteTree.RemoveFolder(folders, palettes, characters.Id);

        Assert.Empty(folders);
        // Everything comes back to the top level, which is where the deleted
        // folder was.
        Assert.Null(metal.FolderId);
        Assert.Contains(metal, palettes);
    }

    [Fact]
    public void TheSubtreeFindsAGrandchildListedBeforeItsParent()
    {
        // The list is in no particular order — a single pass misses this one.
        var root = Folder("Root");
        var child = Folder("Child", root.Id);
        var grandchild = Folder("Grandchild", child.Id);
        var folders = new List<PaletteFolder> { grandchild, child, root };

        Assert.Equal(3, PaletteTree.Subtree(folders, root.Id).Count);
    }

    // ---- surviving a broken file ----------------------------------------------

    [Fact]
    public void APaletteFiledUnderAFolderThatIsGoneComesBackToTheTopLevel()
    {
        // Otherwise it exists, resolves, colours the art, and cannot be found
        // in the panel.
        var folders = new List<PaletteFolder>();
        var orphan = new Palette { Name = "Armour", FolderId = "pf_missing" };
        var palettes = new List<Palette> { orphan };

        PaletteTree.Prune(folders, palettes);

        Assert.Null(orphan.FolderId);
    }

    [Fact]
    public void ACycleInTheFileIsBrokenRatherThanLoopedOver()
    {
        var a = Folder("A");
        var b = Folder("B", a.Id);
        a.ParentId = b.Id;
        var folders = new List<PaletteFolder> { a, b };

        PaletteTree.Prune(folders, []);

        // Both are reachable from the top again.
        var roots = PaletteTree.FoldersIn(folders, null);
        Assert.NotEmpty(roots);
        Assert.All(folders, f => Assert.False(
            f.ParentId is { } p && PaletteTree.IsSelfOrDescendant(folders, p, f.Id)));
    }

    // ---- absence --------------------------------------------------------------

    [Fact]
    public void ADocumentWithNoFoldersWritesNoFolderKeys()
    {
        // Optional means absent, not empty. The same rule the camera follows.
        var doc = new Doc();
        doc.Palettes.Add(new Palette { Name = "Scratch" });

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("paletteFolders", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("folderId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheHierarchySurvivesASaveAndAReload()
    {
        var doc = new Doc();
        var characters = Folder("Characters");
        var knight = Folder("Knight", characters.Id);
        doc.PaletteFolders = [characters, knight];
        doc.Palettes.Add(new Palette { Name = "Armour", FolderId = knight.Id });

        var back = DocJson.Deserialize(DocJson.Serialize(doc));

        Assert.Equal(2, back.PaletteFolders!.Count);
        Assert.Equal(characters.Id, back.PaletteFolders.Single(f => f.Name == "Knight").ParentId);
        Assert.Equal(knight.Id, back.Palettes[0].FolderId);
    }
}
