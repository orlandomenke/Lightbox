using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The shipped bundle is an executable, three native libraries and one folder —
/// and the documented path to the server matches where CI actually puts it.
/// </summary>
/// <remarks>
/// <para>
/// B116. A self-contained publish flattens the whole runtime into the output
/// root, which put <b>266 files</b> in front of the executable. Single-file
/// publishing collapses the managed half into <c>Lightbox.App.exe</c>; the three
/// native libraries stay loose on purpose, and the MCP server moves into
/// <c>mcp/</c> because it can no longer share a runtime that lives inside
/// another executable.
/// </para>
/// <para>
/// <b>What these close.</b> The declaration — that the projects, the workflow,
/// the README and the manual-testing checklist all still agree about where
/// things go. They say nothing about whether the binary starts on Windows;
/// nothing on a Linux runner can. <c>MANUAL_TESTING.md</c> owns that, including
/// the one failure that is silent rather than loud: a native library that does
/// not resolve makes Avalonia fall back to software rendering, which reads as
/// "the new build feels slow" rather than as a packaging fault.
/// </para>
/// <para>
/// The README assertion is the one that earns its keep. B32's own closing note
/// records that a wrong Claude Desktop <c>command</c> fails silently — the
/// server never starts and the tools are simply absent — and that path has now
/// moved twice. Drift between the README and the workflow is a red build here
/// rather than a missing feature there.
/// </para>
/// </remarks>
public class PublishLayoutTests(ITestOutputHelper output)
{
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

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    /// <summary>
    /// The workflow with its comments stripped — what the runner acts on.
    /// </summary>
    /// <remarks>
    /// Only for absence checks, and for reading the publish lines: the comment
    /// above those steps explains the folder the server moved out of, so a raw
    /// search for the old path finds the explanation and reports it as the
    /// thing being explained.
    /// </remarks>
    private static string WorkflowDirectives() =>
        string.Join("\n", Read(".github", "workflows", "build.yml")
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line =>
            {
                var hash = line.IndexOf('#');
                return hash < 0 ? line : line[..hash];
            }));

    [Fact]
    public void TheWindowsBundleIsPublishedAsASingleFile()
    {
        var app = Read("src", "Lightbox.App", "Lightbox.App.csproj");
        Assert.Contains("<PublishSingleFile>true</PublishSingleFile>", app);

        // Conditioned on the RID, not set outright. Without the condition every
        // RID-less publish fails, and this repository builds and tests without
        // one constantly.
        Assert.Matches(
            new Regex(@"Condition\s*=\s*""'\$\(RuntimeIdentifier\)'\s*!=\s*''""", RegexOptions.None),
            app);
    }

    [Fact]
    public void NativeLibrariesAreNotSelfExtracted()
    {
        // The ANGLE guard, and the reason it is written down rather than left
        // to the default. .NET 10 stopped unconditionally adding a single-file
        // app's directory to the native search path, and Avalonia loads ANGLE
        // via a raw LoadLibrary that never used it. Bundling the natives for
        // self-extraction is where those meet, and the failure mode is not a
        // crash — it is a silent fall back to software rendering.
        var app = Read("src", "Lightbox.App", "Lightbox.App.csproj");
        Assert.Contains(
            "<IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>",
            app);
        Assert.DoesNotContain(
            "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>",
            app);
    }

    [Fact]
    public void NativeDebugSymbolsAreNotShipped()
    {
        // Measured: 105 MB of a 216 MB bundle was libSkiaSharp.pdb and
        // libHarfBuzzSharp.pdb. DebugType=none does not cover them — they are
        // native runtime assets from the packages, not this project's symbols.
        var app = Read("src", "Lightbox.App", "Lightbox.App.csproj");
        Assert.Contains("RemoveNativeSymbolsFromPublish", app);
        Assert.Contains("'%(Extension)' == '.pdb'", app);
    }

    [Fact]
    public void CrashReportsCanNameTheLineTheyCameFrom()
    {
        // The bundle shipped with DebugType=none, so a crash report named methods
        // and nothing else — half of what makes one actionable. `embedded` puts
        // the debug information inside the assembly rather than in a .pdb beside
        // it, which is what lets it survive single-file publishing without adding
        // a file to a root that is deliberately four. Measured: +0.4 MB on the
        // executable, nothing once zipped.
        var directives = WorkflowDirectives();
        Assert.Contains("-p:DebugType=embedded", directives);

        // `portable` would also give line numbers and would drop loose .pdb files
        // into the bundle root, undoing B116. Named so the cheaper-looking option
        // is refused on purpose rather than by luck.
        Assert.DoesNotContain("-p:DebugType=none", directives);
        Assert.DoesNotContain("-p:DebugType=portable", directives);
    }

    [Fact]
    public void TheServerPublishesIntoItsOwnFolderAndTheAppDoesNot()
    {
        var directives = WorkflowDirectives();
        Assert.Contains("-o publish/win-x64/mcp", directives);

        // The app's own step must still land in the root. A regex rather than a
        // Contains, because "publish/win-x64" is a prefix of the server's path.
        Assert.Matches(new Regex(@"-o publish/win-x64\s*$", RegexOptions.Multiline), directives);
    }

    [Fact]
    public void TheDocumentedServerPathMatchesWhereItIsPublished()
    {
        // The check with the most value per line: the Claude Desktop command is
        // a string in a document, it has moved twice, and getting it wrong
        // produces silence rather than an error.
        var readme = Read("README.md");
        Assert.Contains(@"mcp\\Lightbox.Mcp.exe", readme);
        output.WriteLine("README documents the server inside mcp\\");

        Assert.Contains("-o publish/win-x64/mcp", WorkflowDirectives());
    }

    [Fact]
    public void TheManualTestingChecklistNamesTheFolderTheServerIsIn()
    {
        // So the checklist cannot be worked through against a stale line.
        var checklist = Read("MANUAL_TESTING.md");
        Assert.Contains(@"mcp\", checklist);
        Assert.Contains("B116", checklist);
    }
}
