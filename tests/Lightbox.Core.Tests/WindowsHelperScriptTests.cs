using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The Windows helper scripts are pure ASCII, so no reader can turn their prose
/// into syntax.
/// </summary>
/// <remarks>
/// <para>
/// B194. <c>get-build.ps1</c> shipped with em dashes in its comments and in two
/// <c>Write-Host</c> strings, in a UTF-8 file with no byte-order mark. Windows
/// PowerShell 5.1 -- the one the script says in its own comments it has to run
/// under, because it is the one already on the machine -- reads a script with no
/// BOM in the machine's ANSI code page rather than as UTF-8. On a Western
/// Windows install that is Windows-1252, where the three bytes of an em dash
/// arrive as <c>a EUR "</c> and the last of them is U+201D, a curly closing
/// double quote. <b>PowerShell honours curly quotes as string delimiters</b>, so
/// the string ended at the dash, the rest of that line and everything up to the
/// next quote became one long string -- taking an <c>else {</c> with it -- and
/// the script failed to parse with <c>Unexpected token '}'</c> pointing at a
/// brace eight lines past the end of the block it was closing.
/// </para>
/// <para>
/// <b>Nothing that runs here can see it.</b> The file is valid UTF-8 and parses
/// clean under PowerShell 7 on Linux, which is the only PowerShell CI could
/// reach; the defect exists only in the misreading, on the machine the script is
/// written for. Reproduced by decoding the bytes as Windows-1252 and parsing
/// that, which returns the owner's error at the owner's line number.
/// </para>
/// <para>
/// <b>ASCII rather than a BOM</b>, though a BOM also fixes it. These scripts get
/// copied by hand onto a Windows box -- the report came from a copy sitting in a
/// <c>Builds\</c> folder, not from the repository -- and a BOM does not survive
/// being pasted into a new file, while ASCII survives every encoding anything
/// will read it in. The rule is also checkable in one line, which a rule about
/// invisible leading bytes is not.
/// </para>
/// </remarks>
public class WindowsHelperScriptTests(ITestOutputHelper output)
{
    private static readonly string[] WindowsScriptExtensions = [".ps1", ".psm1", ".bat", ".cmd"];

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

    /// <summary>
    /// Every script in the tree a Windows shell will run, build output excluded.
    /// </summary>
    private static List<string> WindowsScripts()
    {
        var root = RepoRoot();
        var found = new List<string>();
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                var leaf = Path.GetFileName(child);
                if (leaf is ".git" or "bin" or "obj" or "node_modules")
                {
                    continue;
                }

                pending.Push(child);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (WindowsScriptExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(file);
                }
            }
        }

        return found;
    }

    [Fact]
    public void EveryWindowsHelperScriptIsAsciiSoWindowsPowerShellCannotMisreadIt()
    {
        foreach (var script in WindowsScripts())
        {
            var relative = Path.GetRelativePath(RepoRoot(), script);
            var line = 1;
            var column = 1;

            foreach (var character in File.ReadAllText(script))
            {
                if (character == '\n')
                {
                    line++;
                    column = 1;
                    continue;
                }

                Assert.True(
                    character < 128,
                    $"{relative}:{line}:{column} holds '{character}' (U+{(int)character:X4}). "
                    + "Windows PowerShell 5.1 reads a script with no byte-order mark in the "
                    + "machine's ANSI code page, and a character that decodes there as a curly "
                    + "quote ends a string in the middle of a sentence -- which is B194, a "
                    + "parse error nothing on a Linux runner can reproduce. Use ASCII: "
                    + "-- for an em dash, -> for an arrow.");

                column++;
            }

            output.WriteLine($"{relative}: ASCII");
        }
    }

    /// <summary>
    /// The walker above is only worth its assertion if it finds the files the
    /// bug was in, so it cannot pass by finding nothing.
    /// </summary>
    [Fact]
    public void TheGuardCoversTheScriptsTheBugWasIn()
    {
        var root = RepoRoot();
        var found = WindowsScripts().Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();

        Assert.Contains("scripts/get-build.ps1", found);
        Assert.Contains("scripts/watch-builds.ps1", found);
        output.WriteLine($"{found.Count} Windows scripts guarded: {string.Join(", ", found)}");
    }
}
