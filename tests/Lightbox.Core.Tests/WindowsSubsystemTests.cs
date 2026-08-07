using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The app is a GUI program and the two tools are console programs, and each
/// of those is a decision rather than a default.
/// </summary>
/// <remarks>
/// <para>
/// B115. <c>OutputType</c> decides which subsystem the Windows apphost is built
/// for, and the shipped app had <c>Exe</c> — console. Windows gives a
/// console-subsystem process a console if it has none, which is the terminal
/// window that opened beside Lightbox and took it down with it when closed.
/// </para>
/// <para>
/// <b>What these close and what they do not.</b> They assert the declaration:
/// that the project still says <c>WinExe</c>, that the two genuinely
/// console-facing programs still say <c>Exe</c>, and that the traces still have
/// a way to reach a terminal. Whether a console window actually appears is a
/// Win32 fact that no test on a Linux runner can observe, so
/// <c>MANUAL_TESTING.md</c> owns that half. Asserting the declaration is worth
/// doing anyway: the regression is a one-word edit, and it is invisible in
/// review until somebody runs the bundle on Windows.
/// </para>
/// </remarks>
public class WindowsSubsystemTests(ITestOutputHelper output)
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

    /// <summary>
    /// Read the declared OutputType, parsed rather than matched.
    /// </summary>
    /// <remarks>
    /// XML rather than a regex because the comment above the property names
    /// both values to explain the choice between them, and a text search for
    /// the wrong one would find the sentence ruling it out.
    /// </remarks>
    private static string OutputTypeOf(params string[] projectPath)
    {
        var full = Path.Combine(RepoRoot(), Path.Combine(projectPath));
        Assert.True(File.Exists(full), $"no project at {full}");
        var declared = XDocument.Load(full)
            .Descendants("OutputType")
            .Select(e => e.Value.Trim())
            .ToList();
        Assert.True(declared.Count == 1, $"{full} declares {declared.Count} OutputType values");
        return declared[0];
    }

    [Fact]
    public void TheAppShipsAsAGuiProgramSoNoConsoleWindowOpensBesideIt()
    {
        var declared = OutputTypeOf("src", "Lightbox.App", "Lightbox.App.csproj");
        output.WriteLine($"Lightbox.App OutputType = {declared}");
        Assert.Equal("WinExe", declared);
    }

    [Fact]
    public void TheStdioProgramsStayConsolePrograms()
    {
        // The MCP server talks to Claude Desktop over stdin/stdout and the
        // bench harness is run from a prompt. Both want the console the app
        // does not, so this is the other half of the same decision.
        var mcp = OutputTypeOf("src", "Lightbox.Mcp", "Lightbox.Mcp.csproj");
        var bench = OutputTypeOf("tools", "Lightbox.Bench", "Lightbox.Bench.csproj");
        output.WriteLine($"Lightbox.Mcp = {mcp}, Lightbox.Bench = {bench}");
        Assert.Equal("Exe", mcp);
        Assert.Equal("Exe", bench);
    }

    [Fact]
    public void TheOptInTracesStillHaveSomewhereToPrint()
    {
        // A GUI-subsystem process starts with no console, so Console.Error goes
        // nowhere. Both trace variables are useless without something arranging
        // one, and the failure is silence rather than an error — which reads as
        // the tracing being broken rather than as there being nothing to print.
        var program = File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Lightbox.App", "Program.cs"));
        Assert.Contains("LIGHTBOX_TRACE", program);
        Assert.Contains("LIGHTBOX_PERFTRACE", program);
        Assert.Contains("DiagnosticsConsole.Open", program);
    }

    [Fact]
    public void AskingForAConsoleWorksEvenWhenThereIsNoTerminalToAttachTo()
    {
        // The whole point, and the thing an AttachConsole-only version got
        // wrong: attaching needs a parent console, and the ordinary way to
        // start Lightbox is to double-click it, where there is none. Without
        // AllocConsole "give me a console" is unanswerable for exactly the
        // launch an artist uses.
        var console = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Lightbox.App", "Services", "DiagnosticsConsole.cs"));

        Assert.Contains("AttachConsole", console);
        Assert.Contains("AllocConsole", console);
        // And the streams are pointed at the console device explicitly, so it
        // does not matter what the process inherited — the question B115 could
        // not settle from Linux stops mattering rather than being answered.
        Assert.Contains("CONOUT$", console);
    }

}
