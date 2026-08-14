using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The reference board's record, its tidy-up, and where it is kept (Q87).
/// </summary>
public class ReferenceBoardTests
{
    private static BoardTile Tile(double width, double height) =>
        new() { ViewId = Ids.NewId("view"), Width = width, Height = height };

    // ---- z-order ------------------------------------------------------------------

    [Fact]
    public void BringingATileToTheFrontPutsItLastInTheList()
    {
        var board = new ReferenceBoard();
        var back = Tile(100, 100);
        var middle = Tile(100, 100);
        var front = Tile(100, 100);
        board.Tiles.AddRange([back, middle, front]);

        Assert.True(board.BringToFront(back));

        // The list order is the z-order: last is in front, and nothing else moved
        // relative to anything else.
        Assert.Equal([middle, front, back], board.Tiles);
        Assert.False(board.BringToFront(back));
    }

    [Fact]
    public void SendingATileToTheBackPutsItFirst()
    {
        var board = new ReferenceBoard();
        var back = Tile(100, 100);
        var front = Tile(100, 100);
        board.Tiles.AddRange([back, front]);

        Assert.True(board.SendToBack(front));

        Assert.Equal([front, back], board.Tiles);
        Assert.False(board.SendToBack(front));
    }

    [Fact]
    public void ZOrderSurvivesASaveAndReload()
    {
        var board = new ReferenceBoard();
        board.Tiles.AddRange([Tile(100, 100), Tile(100, 100), Tile(100, 100)]);
        board.BringToFront(board.Tiles[0]);
        var order = board.Tiles.Select(t => t.Id).ToList();

        var json = JsonSerializer.Serialize(board, DocJson.Options);
        var reloaded = JsonSerializer.Deserialize<ReferenceBoard>(json, DocJson.Options)!;

        // The whole reason there is no Z key: two tiles cannot claim the same
        // number, and the order round-trips exactly.
        Assert.Equal(order, reloaded.Tiles.Select(t => t.Id));
    }

    // ---- the tidy-up ----------------------------------------------------------------

    [Fact]
    public void ArrangingFitsEveryTileInsideTheView()
    {
        var tiles = new List<BoardTile>
        {
            Tile(1920, 1080), Tile(400, 900), Tile(600, 600), Tile(300, 200), Tile(1000, 250),
        };

        BoardLayout.Arrange(tiles, 1200, 800);

        foreach (var tile in tiles)
        {
            Assert.True(tile.X >= 0 && tile.Y >= 0, $"{tile.X},{tile.Y} is off the top-left");
            Assert.True(tile.X + tile.Width <= 1200.001, $"{tile.X + tile.Width} runs past the right edge");
            Assert.True(tile.Y + tile.Height <= 800.001, $"{tile.Y + tile.Height} runs past the bottom");
        }
    }

    [Fact]
    public void ArrangingKeepsEachTilesAspect()
    {
        var tiles = new List<BoardTile> { Tile(1920, 1080), Tile(400, 900), Tile(600, 600) };
        var before = tiles.Select(t => t.Aspect).ToList();

        BoardLayout.Arrange(tiles, 900, 700);

        for (var i = 0; i < tiles.Count; i++)
        {
            // Stretching a reference to fill its cell would misrepresent the
            // drawing, which is the one thing reference must not do.
            Assert.Equal(before[i], tiles[i].Aspect, 3);
        }
    }

    [Fact]
    public void ArrangingIsDeterministicAndDoesNotReorder()
    {
        var first = new List<BoardTile> { Tile(800, 600), Tile(300, 900), Tile(500, 500), Tile(120, 90) };
        var ids = first.Select(t => t.Id).ToList();
        var second = first.Select(t => t.Clone()).ToList();

        BoardLayout.Arrange(first, 1000, 700);
        BoardLayout.Arrange(second, 1000, 700);

        Assert.Equal(ids, first.Select(t => t.Id));
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].X, second[i].X, 6);
            Assert.Equal(first[i].Y, second[i].Y, 6);
            Assert.Equal(first[i].Width, second[i].Width, 6);
        }
    }

    [Fact]
    public void ArrangingTwiceDoesNotShrinkTheWall()
    {
        // The arranger reads each tile's current size to preserve aspect, so a
        // rounding-down pass would compound: press tidy five times and the
        // reference would be postage stamps.
        var tiles = new List<BoardTile> { Tile(1920, 1080), Tile(600, 900) };
        BoardLayout.Arrange(tiles, 1000, 700);
        var once = tiles.Select(t => t.Width).ToList();

        BoardLayout.Arrange(tiles, 1000, 700);

        for (var i = 0; i < tiles.Count; i++) Assert.Equal(once[i], tiles[i].Width, 6);
    }

    [Fact]
    public void ASmallPictureIsNotBlownUpToFillItsCell()
    {
        var tiles = new List<BoardTile> { Tile(60, 40) };

        BoardLayout.Arrange(tiles, 1200, 800);

        Assert.Equal(60, tiles[0].Width, 6);
        Assert.Equal(40, tiles[0].Height, 6);
    }

    [Fact]
    public void ANewTileLandsBelowEverythingAlreadyUp()
    {
        var tiles = new List<BoardTile> { Tile(200, 200), Tile(200, 200) };
        BoardLayout.Arrange(tiles, 800, 600);
        var bottom = tiles.Max(t => t.Y + t.Height);

        var (x, y) = BoardLayout.NextFreeSpot(tiles);

        Assert.True(y >= bottom, "a new tile must not land on top of one already up");
        Assert.Equal(BoardLayout.Gap, x);
    }

    // ---- where it is kept ------------------------------------------------------------

    private static (Project Project, string Root) TempProject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-board-{Guid.NewGuid():N}.lbproj");
        Directory.CreateDirectory(root);
        return (new Project(new ProjectManifest { Name = "Board" }, root), root);
    }

    [Fact]
    public void ABoardIsWrittenAndReadBackForItsScope()
    {
        var (project, root) = TempProject();
        try
        {
            var folder = ProjectFolders.Add(project.Manifest, "Knight", null);
            var board = new ReferenceBoard();
            board.Tiles.Add(new BoardTile { Name = "Knight · front", ViewId = "v1", X = 12, Y = 34 });

            ProjectBoards.Save(project, folder, board);
            var reloaded = ProjectBoards.Load(project, folder);

            var tile = Assert.Single(reloaded.Tiles);
            Assert.Equal(12, tile.X);
            Assert.Equal(34, tile.Y);
            // A different scope has its own wall, not this one.
            Assert.Empty(ProjectBoards.Load(project, null).Tiles);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AScopeWithNoBoardHasNoFile()
    {
        var (project, root) = TempProject();
        try
        {
            // Absent until used, and absent again once cleared: an emptied wall
            // must not leave a file claiming there is one.
            Assert.Empty(ProjectBoards.Load(project, null).Tiles);
            Assert.False(Directory.Exists(Path.Combine(root, ProjectBoards.RootDir)));

            var board = new ReferenceBoard();
            board.Tiles.Add(new BoardTile { ViewId = "v1" });
            ProjectBoards.Save(project, null, board);
            Assert.True(File.Exists(Path.Combine(root, ProjectBoards.PathFor(null))));

            board.Tiles.Clear();
            ProjectBoards.Save(project, null, board);
            Assert.False(File.Exists(Path.Combine(root, ProjectBoards.PathFor(null))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EveryDocumentUnderASubjectSeesTheSameWall()
    {
        var (project, root) = TempProject();
        try
        {
            var knight = ProjectFolders.Add(project.Manifest, "Knight", null);
            var walk = ProjectFolders.Add(project.Manifest, "Locomotion", knight);
            var run = ProjectFolders.Add(project.Manifest, "Combat", knight);
            var a = new DocumentRef { Id = "a", Name = "walk", Path = "x", FolderId = walk.Id };
            var b = new DocumentRef { Id = "b", Name = "swing", Path = "y", FolderId = run.Id };

            // The scope is the top of the subtree — the same rule a sheet made
            // from either document is filed under, so the wall and the sheets on
            // it agree about who they belong to.
            Assert.Equal(knight.Id, ProjectBoards.ScopeOf(project.Manifest, a)?.Id);
            Assert.Equal(knight.Id, ProjectBoards.ScopeOf(project.Manifest, b)?.Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnImportedImageIsCopiedIntoTheProject()
    {
        var (project, root) = TempProject();
        var source = Path.Combine(Path.GetTempPath(), $"lightbox-ref-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        try
        {
            var relative = ProjectBoards.ImportImage(project, source);

            Assert.NotNull(relative);
            Assert.StartsWith(ProjectBoards.ImagesDir + "/", relative);
            // Copied, not linked: deleting the original leaves the board intact,
            // which is the failure a path into a downloads folder guarantees.
            File.Delete(source);
            var tile = new BoardTile { Path = relative };
            Assert.True(File.Exists(ProjectBoards.ResolveImage(project, tile)!));
        }
        finally
        {
            if (File.Exists(source)) File.Delete(source);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TwoImportsOfTheSameNameDoNotOverwriteEachOther()
    {
        var (project, root) = TempProject();
        try
        {
            var first = ProjectBoards.ImportImage(project, [1, 2, 3], "pose.png");
            var second = ProjectBoards.ImportImage(project, [9, 9, 9], "pose.png");

            Assert.NotEqual(first, second);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(root, first!)));
            Assert.Equal([9, 9, 9], File.ReadAllBytes(Path.Combine(root, second!)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SomethingThatIsNotAnImageIsRefused()
    {
        var (project, root) = TempProject();
        var source = Path.Combine(Path.GetTempPath(), $"lightbox-ref-{Guid.NewGuid():N}.txt");
        File.WriteAllText(source, "notes about the pose");
        try
        {
            Assert.Null(ProjectBoards.ImportImage(project, source));
        }
        finally
        {
            File.Delete(source);
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- the loose-document fallback ---------------------------------------------------

    [Fact]
    public void ADocumentWithNoBoardWritesNoKey()
    {
        var json = DocJson.Serialize(new Doc());

        Assert.DoesNotContain("\"referenceBoard\"", json);
    }

    [Fact]
    public void ALooseDocumentsBoardTravelsInsideIt()
    {
        var doc = new Doc { ReferenceBoard = new ReferenceBoard() };
        doc.ReferenceBoard.Tiles.Add(new BoardTile { Name = "pose", Png = "abcd", X = 5, Y = 6 });

        var reloaded = DocJson.Deserialize(DocJson.Serialize(doc));

        var tile = Assert.Single(reloaded.ReferenceBoard!.Tiles);
        Assert.Equal("abcd", tile.Png);
        Assert.Equal(5, tile.X);
        // A clone is a copy, not a shared wall.
        var clone = doc.Clone();
        clone.ReferenceBoard!.Tiles[0].X = 99;
        Assert.Equal(5, doc.ReferenceBoard.Tiles[0].X);
    }

    [Fact]
    public void ATilesDerivedAnswersAreNotSerialized()
    {
        var json = JsonSerializer.Serialize(new BoardTile { ViewId = "v1" }, DocJson.Options);

        // The convenience getters beside the nullable sources are properties, so
        // without [JsonIgnore] every tile would carry four keys nobody wrote.
        Assert.DoesNotContain("\"isView\"", json);
        Assert.DoesNotContain("\"isFile\"", json);
        Assert.DoesNotContain("\"isEmbedded\"", json);
        Assert.DoesNotContain("\"aspect\"", json);
        // And an unused source writes nothing either.
        Assert.DoesNotContain("\"png\"", json);
        Assert.DoesNotContain("\"path\"", json);
        Assert.DoesNotContain("\"origin\"", json);
    }
}
