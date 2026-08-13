using System.Text.RegularExpressions;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B78: adding a reference sheet asks for a name once, not twice.
/// </summary>
/// <remarks>
/// <para>
/// The fix is that <c>SaveDocumentAsAsync</c> takes a suggested name and the
/// reference-sheet path passes the sheet's, so the file picker opens already
/// filled in. Neither prompt is removed — they ask different questions, <em>what
/// is this called</em> and <em>where does it go</em> — but the second one no
/// longer asks the artist to type an answer they have already given.
/// </para>
/// <para>
/// <b>Asserted against the source rather than through the window</b>, in the
/// idiom <c>ControlTreatmentTests</c> already uses. The behaviour lives in a
/// file dialog, which this environment cannot drive: a headless test would have
/// to stub the picker, and a stub that returns whatever it was handed proves
/// only that the stub works. Reading the call is a weaker guard than driving it
/// and a much stronger one than <c>evidence: manual</c>, which is what this
/// entry carried while the code was already fixed.
/// </para>
/// </remarks>
public class SaveSuggestsANameTests(ITestOutputHelper output)
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
    /// The whole of the <c>MainWindow</c> partial class, not one file of it. The view
    /// was split into fifteen partials when it hit 5,544 lines, and a test that reads
    /// only <c>MainWindow.axaml.cs</c> silently stops guarding anything the moment the
    /// member it greps for moves to a sibling.
    /// </summary>
    private static string MainWindow() =>
        string.Concat(Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot(), "src", "Lightbox.App", "Views"), "MainWindow*.cs")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(File.ReadAllText));

    /// <summary>The save path can be told what to call the file.</summary>
    [Fact]
    public void SaveCanBeGivenASuggestedName()
    {
        var source = MainWindow();

        var signature = Regex.Match(
            source, @"Task SaveDocumentAsAsync\(([^)]*)\)");

        output.WriteLine($"SaveDocumentAsAsync({signature.Groups[1].Value})");
        Assert.True(signature.Success, "SaveDocumentAsAsync is gone or renamed");
        Assert.Contains("suggestedName", signature.Groups[1].Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Adding a reference sheet passes the sheet's name through, so the picker
    /// does not ask for it again.
    /// </summary>
    /// <remarks>
    /// The parameter existing is not the fix — a suggested name nobody supplies
    /// leaves the artist typing it twice exactly as before, which is the bug.
    /// This is the half that has to keep holding.
    /// </remarks>
    [Fact]
    public void AddingAReferenceSheetPassesTheNameToTheSavePicker()
    {
        var source = MainWindow();

        var passed = Regex.IsMatch(source, @"SaveDocumentAsAsync\(\s*sheet\.Name\s*\)");

        output.WriteLine(passed
            ? "the picker opens on the sheet's own name"
            : "SaveDocumentAsAsync is called with no name on the reference-sheet path");
        Assert.True(
            passed,
            "adding a reference sheet no longer suggests the sheet's name — B78 has reopened");
    }
}
