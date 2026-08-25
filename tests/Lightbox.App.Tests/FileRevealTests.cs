using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Which command each desktop needs to show a path in its file manager.
///
/// Worth testing precisely because getting it wrong is silent: Explorer given
/// the wrong argument opens Documents and reports nothing, so a broken "show
/// in file manager" looks like a working one that chose the wrong folder.
/// </summary>
public class FileRevealTests
{
    [Fact]
    public void WindowsSelectsAFileInsideItsFolder()
    {
        var command = FileReveal.RevealCommand(Desktop.Windows, @"C:\art\Knight.lbproj\walk.json", false);

        Assert.Equal("explorer.exe", command.File);
        // One argument, comma included. Split into two and Explorer silently
        // ignores both and opens Documents.
        Assert.Equal([@"/select,C:\art\Knight.lbproj\walk.json"], command.Args);
    }

    [Fact]
    public void WindowsOpensAFolderRatherThanSelectingIt()
    {
        var command = FileReveal.RevealCommand(Desktop.Windows, @"C:\art\Knight.lbproj", true);

        Assert.Equal([@"C:\art\Knight.lbproj"], command.Args);
    }

    [Fact]
    public void MacRevealsWithDashR()
    {
        Assert.Equal(
            ["-R", "/art/Knight.lbproj/walk.json"],
            FileReveal.RevealCommand(Desktop.MacOs, "/art/Knight.lbproj/walk.json", false).Args);
        Assert.Equal(
            ["/art/Knight.lbproj"],
            FileReveal.RevealCommand(Desktop.MacOs, "/art/Knight.lbproj", true).Args);
    }

    [Fact]
    public void LinuxOpensTheContainingFolderBecauseThereIsNoPortableSelect()
    {
        // Pretending otherwise would mean shipping a Nautilus-only feature and
        // calling it Linux support.
        var command = FileReveal.RevealCommand(Desktop.Linux, "/art/Knight.lbproj/walk.json", false);

        Assert.Equal("xdg-open", command.File);
        Assert.Equal(["/art/Knight.lbproj"], command.Args);
    }

    [Fact]
    public void OpeningHandsThePathToTheDesktop()
    {
        Assert.Equal("open", FileReveal.OpenCommand(Desktop.MacOs, "/a/b.json").File);
        Assert.Equal("xdg-open", FileReveal.OpenCommand(Desktop.Linux, "/a/b.json").File);
        // Windows has no launcher executable — the path itself is the command,
        // resolved by the shell to whatever is registered for the extension.
        Assert.Equal(@"C:\a\b.json", FileReveal.OpenCommand(Desktop.Windows, @"C:\a\b.json").File);
    }

    [Fact]
    public void APathWithNoParentIsItsOwnFolder()
    {
        Assert.Equal("/", FileReveal.Directory("/"));
    }

    /// <summary>
    /// The parent follows the desktop that was asked for, not the machine asking.
    /// </summary>
    /// <remarks>
    /// Every case here has to give the same answer on the Linux runner and on a
    /// Windows developer's box. Reading the parent with
    /// <c>Path.GetDirectoryName</c> meant it did not:
    /// <see cref="LinuxOpensTheContainingFolderBecauseThereIsNoPortableSelect"/>
    /// asked for a Linux reveal, got <c>\art\Knight.lbproj</c> back on Windows,
    /// and so could only ever pass on one platform.
    /// </remarks>
    [Fact]
    public void TheFolderIsReadTheWayTheAskedForDesktopReadsPaths()
    {
        Assert.Equal("/art/Knight.lbproj",
            FileReveal.Directory(Desktop.Linux, "/art/Knight.lbproj/walk.json"));
        Assert.Equal("/art/Knight.lbproj",
            FileReveal.Directory(Desktop.MacOs, "/art/Knight.lbproj/walk.json"));
        Assert.Equal(@"C:\art\Knight.lbproj",
            FileReveal.Directory(Desktop.Windows, @"C:\art\Knight.lbproj\walk.json"));

        // Windows accepts either separator, so both have to be found.
        Assert.Equal("C:/art", FileReveal.Directory(Desktop.Windows, "C:/art/walk.json"));

        // A backslash is a legal character in a Unix filename rather than a
        // separator, so this is one name with no parent — not a folder called
        // "a". Getting this wrong would send xdg-open somewhere that is not
        // there.
        Assert.Equal(@"a\b.json", FileReveal.Directory(Desktop.Linux, @"a\b.json"));

        // Roots keep their separator: without it the answer stops naming a
        // place, and bare "C:" names the drive's current directory, which is
        // somewhere else entirely.
        Assert.Equal("/", FileReveal.Directory(Desktop.Linux, "/walk.json"));
        Assert.Equal(@"C:\", FileReveal.Directory(Desktop.Windows, @"C:\walk.json"));

        // No separator at all: nothing to take a parent from.
        Assert.Equal("walk.json", FileReveal.Directory(Desktop.Linux, "walk.json"));
    }

    [Fact]
    public void NothingIsRevealedForAPathThatIsNotThere()
    {
        Assert.False(FileReveal.Reveal(""));
        Assert.False(FileReveal.Reveal(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}", "x.json")));
        Assert.False(FileReveal.Open(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json")));
    }
}
