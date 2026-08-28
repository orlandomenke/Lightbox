using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Dragging pictures onto the reference board, more than once (B282).
/// </summary>
/// <remarks>
/// Reported as *"I am only able to drag an image once from a browser"*. Nothing
/// in the board latched — the fault was in what a drop was willing to accept:
/// a browser that has already cached a picture offers it as a **file** on the
/// next drag, that file is a temporary one whose name says nothing, and the
/// drop threw it away on the name and then refused the URL half because a file
/// format was present. Both halves of that are pinned here.
/// </remarks>
[Collection("BrushState")]
public class BoardWebDropTests : BrushStateIsolated
{
    private static byte[] Png(SKColor colour, int w = 20, int h = 12)
    {
        using var bmp = new SKBitmap(w, h);
        bmp.Erase(colour);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A picture on disk under a name that says nothing — a browser's cache file.</summary>
    private static string NamelessTempPicture(SKColor colour)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-drag-{Guid.NewGuid():N}");
        File.WriteAllBytes(path, Png(colour));
        return path;
    }

    [AvaloniaFact]
    public void OneWebDropAfterAnotherBothLand()
    {
        var vm = VmLayers.PaperVm();
        var board = new ReferenceBoardViewModel(vm);

        var first = board.AddImageBytes("one", Png(SKColors.Red), (100, 100));
        var second = board.AddImageBytes("two", Png(SKColors.Blue), (300, 100));
        var third = board.AddImageBytes("three", Png(SKColors.Green), (500, 100));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(3, board.Tiles.Count);
    }

    [AvaloniaFact]
    public void APictureWhoseNameSaysNothingIsStillAPicture()
    {
        // What a browser hands over the second time: real PNG bytes in a file
        // with no extension at all. Judged by what decodes, not by what it is
        // called.
        var vm = VmLayers.PaperVm();
        var board = new ReferenceBoardViewModel(vm);
        var path = NamelessTempPicture(SKColors.Orange);
        try
        {
            var tile = board.AddImageFile(path, (50, 50));

            Assert.NotNull(tile);
            Assert.Single(board.Tiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void AFileThatIsNotAPictureIsStillRefused()
    {
        // The other half of dropping the name check: content has to be the test,
        // so something that is not a picture must not become a tile.
        var vm = VmLayers.PaperVm();
        var board = new ReferenceBoardViewModel(vm);
        var path = Path.Combine(Path.GetTempPath(), $"lightbox-drag-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "not a picture, whatever the name claims");
        try
        {
            Assert.Null(board.AddImageFile(path, (50, 50)));
            Assert.Empty(board.Tiles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ANamelessPictureIsCopiedIntoTheProjectRatherThanEmbedded()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-b280-{Guid.NewGuid():N}.lbproj");
        Directory.CreateDirectory(root);
        var path = NamelessTempPicture(SKColors.Purple);
        try
        {
            var vm = VmLayers.PaperVm();
            var project = new Project(new ProjectManifest { Name = "Knight" }, root);
            ProjectIo.Save(project);
            vm.ProjectDocker.Adopt(project);
            var board = new ReferenceBoardViewModel(vm);

            var tile = board.AddImageFile(path, (50, 50))!;

            // Q87's promise holds even when the name is useless: the picture is
            // copied in and pointed at, not embedded in the board file.
            Assert.NotNull(tile.Path);
            Assert.Null(tile.Png);
            Assert.True(File.Exists(Path.Combine(root, tile.Path!)));
            Assert.EndsWith(".png", tile.Path);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- the two refusals, guarded where they live ---------------------------------

    /// <summary>
    /// The board window's source. Asserted against directly, in the idiom
    /// <c>SaveSuggestsANameTests</c> uses for the save dialog: the behaviour is
    /// in a drag payload this environment cannot produce, and a stub that
    /// returns whatever it was handed would prove only that the stub works.
    /// Weaker than driving it, far stronger than <c>evidence: manual</c>.
    /// </summary>
    private static string BoardWindowSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", "Views", "ReferenceBoardWindow.cs"));
    }

    [Fact]
    public void ADroppedFileIsNotJudgedByItsName()
    {
        var files = Regex.Match(
            BoardWindowSource(), @"List<string> DroppedFiles\(DragEventArgs e\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(files.Success, "DroppedFiles has moved — this guard needs to follow it");
        // The extension filter is what threw away a browser's nameless cache
        // file, and what decides now is whether the bytes decode.
        Assert.DoesNotContain("ImageExtensions", files.Groups[1].Value);
    }

    [Fact]
    public void CarryingAFileDoesNotRuleOutTheWebHalfOfADrag()
    {
        var web = Regex.Match(
            BoardWindowSource(), @"IReadOnlyList<Uri> DroppedWebImages\(DragEventArgs e\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(web.Success, "DroppedWebImages has moved — this guard needs to follow it");
        // `data.Contains(DataFormat.File)` here refused the whole drop whenever a
        // browser advertised both, which is what a second drag of the same
        // picture looks like.
        Assert.DoesNotContain("Contains(DataFormat.File)", web.Groups[1].Value);
    }

    [Fact]
    public void ADropThatPinsNothingSaysSo_OnTheBoardItself()
    {
        var source = BoardWindowSource();
        var drop = Regex.Match(
            source, @"private async void OnDrop\(object\? sender, DragEventArgs e\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(drop.Success, "OnDrop has moved — this guard needs to follow it");
        // A drop that silently does nothing is indistinguishable from a feature
        // that does not work, which is exactly how this was reported — twice:
        // B282 for saying nothing at all, B285 for saying it only on the main
        // window's status line, which the artist at the board cannot see.
        Assert.Contains("Say(", drop.Groups[1].Value);

        var say = Regex.Match(
            source, @"private void Say\(string message\)\s*\{(.+?)\n    \}", RegexOptions.Singleline);
        Assert.True(say.Success, "Say has moved — this guard needs to follow it");
        Assert.Contains("_status.Text", say.Groups[1].Value);
        Assert.Contains("AiStatus", say.Groups[1].Value);
    }

    [Fact]
    public void ADropResolvesAPageToTheImageItNames()
    {
        var drop = DropSource();

        // FetchAsync hands back whatever the site sends. FetchFirstImageAsync is
        // the one that reads a fetched *page* for the image it names (B285),
        // which is what a drag off Pinterest and its kin carries — and that
        // tries every address the drag holds as a picture before reading any of
        // them as a page (B344), so the picture beats the page it sat on.
        Assert.Contains("FetchFirstImageAsync", drop);
    }

    [Fact]
    public void APictureFoundByReadingAPageIsAnnouncedAsAGuess()
    {
        // A page’s og:image is that site’s answer to “what is this page
        // about”. On a site whose pages all share one social card it is the
        // site’s own logo, and pinning that silently is indistinguishable from
        // pinning the right picture (B344). The board says which it was.
        var drop = DropSource();

        Assert.Contains("NamedByAPage", drop);
        Assert.Contains("that page names", drop);
    }

    /// <summary>The body of the board’s drop handler, for the guards that read it.</summary>
    private static string DropSource()
    {
        var drop = Regex.Match(
            BoardWindowSource(), @"private async void OnDrop\(object\? sender, DragEventArgs e\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(drop.Success, "OnDrop has moved — these guards need to follow it");
        return drop.Groups[1].Value;
    }
}
