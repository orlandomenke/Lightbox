using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// The reference board must be reachable without a reference sheet already
/// existing. Its only entry point used to be an icon on a view's row in the
/// Reference sheets docker — a button that exists only once a sheet with a
/// view does, which is to say it was invisible exactly when the wall was
/// empty and an artist went looking for somewhere to drop a reference.
/// </summary>
public class ReferenceBoardReachabilityTests
{
    private static string MainWindowMarkup() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src/Lightbox.App/Views/MainWindow.axaml"));

    [Fact]
    public void TheViewMenuOpensTheBoard()
    {
        // The markup is the registry here: a menu item is not reachable from
        // any model, so the promise is held where it is made.
        var markup = MainWindowMarkup();
        var menu = markup[..markup.IndexOf("controls:Docker", StringComparison.Ordinal)];
        Assert.Contains("OnOpenReferenceBoard", menu);
    }

    [Fact]
    public void TheSheetsDockerOpensTheBoardBeforeAnySheetExists()
    {
        // The docker's top bar exists with an empty document; the per-view row
        // does not. Both carry the button, and this guards the one that is
        // always on screen.
        var markup = MainWindowMarkup();
        var docker = markup[markup.IndexOf("x:Name=\"SheetsPanel\"", StringComparison.Ordinal)..];
        var topBar = docker[..docker.IndexOf("ItemsControl", StringComparison.Ordinal)];
        Assert.Contains("OnOpenReferenceBoard", topBar);
    }

    [Fact]
    public void OpeningTheBoardIsABindableShortcut()
    {
        // In the map so it can be seen, searched and rebound — the registry
        // rule. Resolved by the main window, unlike the board's own three,
        // because it is pressed before the board exists.
        var map = new ShortcutMap();
        var binding = map.Find("reference.board");
        Assert.NotNull(binding);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
