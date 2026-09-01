using System.Text.RegularExpressions;

namespace Lightbox.App.Tests;

/// <summary>
/// What the board is willing to accept before a drop happens (B351).
/// </summary>
/// <remarks>
/// The board used to read the drag in <c>OnDragOver</c> and refuse the pointer
/// when it could not already see a picture. A refused drag never becomes a
/// drop, so <c>OnDrop</c> never ran, so nothing was said and nothing reached
/// the diagnostics log — the one case where the format names are the whole
/// diagnosis produced none of them.
///
/// These are source guards for the same reason the rest of this window's are:
/// Avalonia's <c>DragEventArgs</c> cannot be constructed from a test, so the
/// handler cannot be driven. What can be pinned is which questions it asks,
/// and that is exactly where the defect lived.
/// </remarks>
public class BoardDragOverTests
{
    private static string DragOverSource()
    {
        var over = Regex.Match(
            BoardWindowSource(),
            @"private static void OnDragOver\(object\? sender, DragEventArgs e\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(over.Success, "OnDragOver has moved — these guards need to follow it");
        return over.Groups[1].Value;
    }

    [Fact]
    public void ADragCarryingAnythingIsAccepted()
    {
        // Accepting is what lets the drop run, and the drop is the only thing
        // that can say what was wrong or write the format names down.
        var over = DragOverSource();

        Assert.Contains("DragDropEffects.Copy", over);
        Assert.Contains("Formats.Count", over);
    }

    [Fact]
    public void TheDragOverDoesNotReadTheDragToDecide()
    {
        // Two faults, one cause. Reading here made the refusal possible *and*
        // made it expensive: drag-over fires continuously while the pointer
        // moves, and the old test decoded every format and ran SKCodec.Create
        // over every byte member — a codec probe per pointer event, to answer
        // a question the drop is about to ask properly anyway.
        var over = DragOverSource();

        Assert.DoesNotContain("DroppedWebImages", over);
        Assert.DoesNotContain("EmbeddedImageIn", over);
        Assert.DoesNotContain("DroppedFiles", over);
    }

    [Fact]
    public void AndTheDropStillExplainsItself()
    {
        // The other half of the trade: the pointer stops saying no, so the drop
        // has to say why. Both messages and the log line live in OnDrop.
        var drop = Regex.Match(
            BoardWindowSource(),
            @"private async void OnDrop\(object\? sender, DragEventArgs e\)\s*\{(.+?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(drop.Success, "OnDrop has moved — this guard needs to follow it");
        Assert.Contains("DescribeFormats", drop.Groups[1].Value);
        Assert.Contains("had no picture in it", drop.Groups[1].Value);
    }

    private static string BoardWindowSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", "Views", "ReferenceBoardWindow.cs"));
    }
}
