using System.Text.RegularExpressions;

namespace Lightbox.App.Tests;

/// <summary>
/// A drop reads what the drag carried <em>before</em> it awaits anything (B352).
/// </summary>
/// <remarks>
/// <para>
/// The platform releases a drag's data object when the drop handler returns,
/// and an <c>async</c> handler returns at its first <c>await</c>. Every read
/// after that point is a read of a released object: it throws, the throw is
/// caught, and the answer comes back <em>empty rather than wrong</em>. That is
/// what hid it for so long.
/// </para>
/// <para>
/// Found in a real log, from a real refused drop: <c>carried DragContext
/// (unreadable), DragImageBits (unreadable), chromium/x-renderer-taint
/// (unreadable), Chromium Web Custom MIME Data Format (unreadable)</c> — every
/// format unreadable, which in <c>DescribeFormats</c> is the branch where
/// <c>TryGetRaw</c> threw. The one line that exists to diagnose a refused drop
/// could only report that it had been asked too late.
/// </para>
/// <para>
/// Source guards, because the fault is <em>where</em> a call sits relative to
/// an await. No headless test can observe it: the release is the operating
/// system's, and Avalonia's headless platform has no drag data object to
/// release. Position is the defect, so position is what is pinned.
/// </para>
/// </remarks>
public class DropReadsBeforeAwaitingTests
{
    /// <summary>The reads that must not appear after an await, and what each one loses.</summary>
    private static readonly (string Call, string Loses)[] LiveReads =
    [
        ("EmbeddedImageIn", "the picture the drag was carrying itself"),
        ("DescribeFormats", "the format names, which are the whole diagnosis of a refused drop"),
    ];

    [Theory]
    [InlineData("Views/ReferenceBoardWindow.cs", @"private async void OnDrop\(object\? sender, DragEventArgs e\)")]
    [InlineData("Views/MainWindow.Palette.cs", @"private async Task ImportWebImage\(")]
    public void NoDragReadSitsAfterAnAwait(string file, string signature)
    {
        var method = MethodBody(file, signature);
        var firstAwait = method.IndexOf("await ", StringComparison.Ordinal);
        Assert.True(firstAwait >= 0, $"{file}: this guard is only meaningful for an async method");

        var afterTheAwait = method[firstAwait..];
        foreach (var (call, loses) in LiveReads)
        {
            Assert.False(
                afterTheAwait.Contains(call, StringComparison.Ordinal),
                $"{file}: {call} runs after an await, so it reads a released drag object "
                    + $"and loses {loses}");
        }
    }

    [Fact]
    public void TheBoardTakesThePayloadUpFrontAndUsesWhatItTook()
    {
        // Reading early is only a fix if the values read are the ones used
        // further down, so both halves are pinned.
        var drop = MethodBody(
            "Views/ReferenceBoardWindow.cs",
            @"private async void OnDrop\(object\? sender, DragEventArgs e\)");

        var captured = drop.IndexOf("EmbeddedImageIn", StringComparison.Ordinal);
        var firstAwait = drop.IndexOf("await ", StringComparison.Ordinal);

        Assert.True(captured >= 0, "the drag's own picture must still be read somewhere");
        Assert.True(firstAwait >= 0);
        Assert.True(captured < firstAwait, "the drag's own picture must be taken before the fetch");
        Assert.Contains("carriedPicture", drop);
        Assert.Contains("carriedFormats", drop);
    }

    private static string MethodBody(string relativePath, string signature)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Lightbox.App", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var m = Regex.Match(source, signature + @"[^{]*\{(.+?)\n    \}", RegexOptions.Singleline);
        Assert.True(m.Success, $"{relativePath}: {signature} has moved — this guard needs to follow it");
        return WithoutComments(m.Groups[1].Value);
    }

    /// <summary>The method body with its comments taken out.</summary>
    /// <remarks>
    /// Not fussiness. This guard asks <em>where a call sits relative to an
    /// await</em>, and the comment explaining that rule necessarily contains
    /// both the word "await" and the names of the calls it is about — so
    /// reading the prose made the guard fail on the very code that satisfies
    /// it. Found by writing it that way first and watching it go red. A
    /// <c>//</c> inside a string literal would fool this, there is none in
    /// either method, and the alternative is a C# parser for a positional check.
    /// </remarks>
    private static string WithoutComments(string body) =>
        Regex.Replace(body, @"//[^\n]*", string.Empty);
}
