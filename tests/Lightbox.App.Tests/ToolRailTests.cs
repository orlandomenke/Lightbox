using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// The tool rail is legible: every tool has a button, and no two buttons wear
/// the same glyph.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both halves shipped broken once.</b> The Bone tool was registered
/// everywhere a tool can be registered — <c>ToolId</c>, the shortcut map, the
/// cursor map, <c>SyncCanvasToolMode</c>, a toolbar button with the right
/// binding and command — and the owner still could not find it in the running
/// application, because its <c>Path</c> pointed at <c>IconMove</c>: a
/// placeholder that was never replaced. The rail is icon-only until it is
/// dragged past 150px (<c>ReflowToolRail</c>), so what an artist actually saw
/// was two identical four-way arrows side by side.
/// </para>
/// <para>
/// That is B58's lesson one turn of the screw further on. B58 was <em>shipped
/// green and invisible</em> — nothing drew the overlay at all — and the answer
/// was a painter with pixel tests. This was <em>shipped green and
/// indistinguishable</em>, which no amount of testing the feature can catch,
/// because the feature worked perfectly; only the picture of it was wrong. So
/// the guard belongs here, on the rail as a whole, rather than on any one
/// tool.
/// </para>
/// <para>
/// <b>Read off the XAML as text, deliberately.</b> The alternative is
/// instantiating the window and walking the visual tree, which needs a
/// compositor lease a headless run does not get — the very hole B58 fell
/// through. The markup is the source of truth for what an artist sees, and it
/// can be read without a window.
/// </para>
/// </remarks>
public class ToolRailTests
{
    private static string Markup() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src/Lightbox.App/Views/MainWindow.axaml"));

    /// <summary>
    /// Each tool button's id and the glyph it draws, in rail order.
    /// </summary>
    /// <remarks>
    /// Split on <c>Classes="tool"</c> so each segment is exactly one button's
    /// markup, then take the <em>first</em> glyph in it — a variant-bearing
    /// tool (Shape, Select) declares its own glyph before its hold-to-open
    /// flyout, so the first is always the button's own. A glyph is either a
    /// static icon or a binding for the tools whose picture follows their
    /// variant; both are identifiers and both must be unique.
    /// </remarks>
    private static List<(string Tool, string Glyph)> Buttons()
    {
        var segments = Markup().Split("Classes=\"tool\"").Skip(1);
        var found = new List<(string, string)>();
        foreach (var segment in segments)
        {
            var tool = Regex.Match(segment, @"\{x:Static vm:ToolId\.(\w+)\}");
            if (!tool.Success) continue;
            var glyph = Regex.Match(segment, @"Data=""\{(?:StaticResource|Binding) (\w+)\}""");
            found.Add((tool.Groups[1].Value, glyph.Success ? glyph.Groups[1].Value : "(none)"));
        }
        return found;
    }

    [Fact]
    public void EveryToolHasAButtonInTheRail()
    {
        var buttoned = Buttons().Select(b => b.Tool).ToHashSet(StringComparer.Ordinal);
        var missing = Enum.GetNames<ToolId>().Where(t => !buttoned.Contains(t)).ToList();

        // A tool reachable only by shortcut is a tool nobody finds. The rail
        // is the door; the key is the shortcut for people who already know.
        Assert.True(
            missing.Count == 0,
            $"no toolbar button for: {string.Join(", ", missing)} — a tool an artist "
            + "cannot see is a tool that does not exist to them.");
    }

    [Fact]
    public void NoTwoToolsWearTheSameGlyph()
    {
        var duplicates = Buttons()
            .GroupBy(b => b.Glyph, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} on {string.Join(" and ", g.Select(b => b.Tool))}")
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            $"tools sharing a glyph: {string.Join("; ", duplicates)} — the rail is "
            + "icon-only until it is dragged wide, so a shared icon makes one of "
            + "them unfindable.");
    }

    [Fact]
    public void EveryButtonActuallyDrawsSomething()
    {
        var blank = Buttons().Where(b => b.Glyph == "(none)").Select(b => b.Tool).ToList();
        Assert.True(blank.Count == 0, $"no glyph at all on: {string.Join(", ", blank)}");
    }

    [Fact]
    public void EveryIconAToolAsksForActuallyExists()
    {
        var defined = Regex.Matches(IconMarkup(), @"x:Key=""(Icon\w+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // A mistyped key resolves to nothing and draws nothing — the same
        // invisible-tool outcome as a shared glyph, reached another way.
        var dangling = Buttons()
            .Where(b => b.Glyph.StartsWith("Icon", StringComparison.Ordinal) && !defined.Contains(b.Glyph))
            .Select(b => $"{b.Tool} wants {b.Glyph}")
            .ToList();
        Assert.True(dangling.Count == 0, $"undefined icon resource: {string.Join(", ", dangling)}");
    }

    [AvaloniaFact]
    public void EveryToolIconIsGeometryThatDrawsSomething()
    {
        // Through Avalonia's own parser, because that is what the application
        // uses: a malformed path passes every text-level check above and then
        // draws an empty tile at runtime.
        foreach (Match m in Regex.Matches(IconMarkup(), @"x:Key=""(Icon\w+)"">([^<]+)<"))
        {
            var (key, data) = (m.Groups[1].Value, m.Groups[2].Value);
            var bounds = StreamGeometry.Parse(data).Bounds;
            Assert.True(
                bounds.Width > 1 && bounds.Height > 1,
                $"{key} parses but covers nothing: {bounds}");
            Assert.True(
                bounds.Right <= 24.5 && bounds.Bottom <= 24.5 && bounds.X >= -0.5 && bounds.Y >= -0.5,
                $"{key} escapes the 24x24 box every other icon is drawn in: {bounds}");
        }
    }

    private static string IconMarkup() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src/Lightbox.App/Styles/Icons.axaml"));

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
