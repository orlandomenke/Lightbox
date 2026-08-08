using System.Text.RegularExpressions;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// One scale, in one place, and the doc that describes it tells the truth.
/// </summary>
/// <remarks>
/// <para>
/// Stage 3 of <c>docs/DESIGN-ui-system.md</c>. Before this there were four
/// sources for a size and they disagreed: <c>DESIGN.md</c>'s table,
/// <c>Density.axaml</c>, per-view overrides in <c>MainWindow.axaml</c> that beat
/// both, and two constants in C#. <b>The overrides won</b>, so the scale
/// described sizes no control actually had — <c>--tool</c> was written 30 and
/// rendered 34, the icon tile was written 24 and rendered 26.
/// </para>
/// <para>
/// Nothing failed, because every source was internally consistent. That is the
/// same shape as the colour system's two halves, and the fix is the same shape
/// too: <see cref="PaletteTests.NoViewInventsItsOwnChromeColour"/> does exactly
/// this for colour and has already caught two real defects.
/// </para>
/// </remarks>
public class DensityScaleTests
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

    private static string Density() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "Lightbox.App", "Styles", "Density.axaml"));

    private static string Design() => File.ReadAllText(Path.Combine(
        RepoRoot(), ".claude", "quality", "DESIGN.md"));

    /// <summary>The value a selector's block sets for a property, or null.</summary>
    private static string? Setter(string styles, string selector, string property)
    {
        var block = Regex.Match(styles,
            $@"Selector=""{Regex.Escape(selector)}""\s*>(.*?)</Style>", RegexOptions.Singleline);
        if (!block.Success) return null;
        var setter = Regex.Match(block.Groups[1].Value,
            $@"Property=""{property}""\s+Value=""([^""]+)""");
        return setter.Success ? setter.Groups[1].Value : null;
    }

    [Fact]
    public void TheScaleTheDocDescribesIsTheScaleTheCodeApplies()
    {
        // Each row: the token DESIGN.md names, the selector that implements it,
        // and the property that carries the number. If the table and the styles
        // ever disagree again, this says which token and both values.
        (string Token, string Selector, string Property)[] scale =
        [
            ("--row", "Button", "MinHeight"),
            ("--tile", "Button.icon", "Height"),
            ("--bar", "controls|OverflowBar", "Height"),
            ("--tool", "ToggleButton.tool", "Height"),
            ("--field", "NumericUpDown.value", "Width"),
        ];

        var doc = Design();
        var styles = Density();

        foreach (var (token, selector, property) in scale)
        {
            var row = Regex.Match(doc, $@"^\|\s*`{Regex.Escape(token)}`\s*\|\s*(\d+)\s*\|",
                RegexOptions.Multiline);
            Assert.True(row.Success, $"DESIGN.md's size table no longer lists {token}");

            var applied = Setter(styles, selector, property);
            Assert.True(applied is not null,
                $"Density.axaml no longer sets {property} for {selector}, which is what {token} means");

            Assert.True(row.Groups[1].Value == applied,
                $"{token}: DESIGN.md says {row.Groups[1].Value}, Density.axaml applies {applied}");
        }
    }

    [Fact]
    public void NoViewRedeclaresASizeTheScaleOwns()
    {
        // The failure that actually happened, and the only one of these three
        // that is invisible from inside either file: a view restating a size for
        // a class the scale owns does not conflict with anything — it just wins,
        // quietly, in that window only. `MainWindow.axaml` held `Button.icon` at
        // 26 against the scale's 24 for months.
        //
        // Scoped to the classes the scale exists to make uniform. A one-off
        // control setting its own height is a different argument and is allowed
        // with a comment; a *class* being redefined is never right, because the
        // class is the promise that they all match.
        string[] owned = ["icon", "text", "tool", "value", "param"];
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "Lightbox.App"), "*.axaml",
                     SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "Density.axaml") continue;   // where the scale is declared

            var text = File.ReadAllText(file);
            foreach (Match style in Regex.Matches(text,
                         @"<Style\s+Selector=""([^""]+)""\s*>(.*?)</Style>", RegexOptions.Singleline))
            {
                var selector = style.Groups[1].Value;
                // The owned class must be on the selector's TARGET — the last
                // simple selector — not merely on an ancestor. `Button.icon`
                // re-declaring a size is the drift this guards against;
                // `ToggleButton.tool Path` sizing the glyph INSIDE a tool
                // button is sizing content, and content is not the box.
                var target = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
                if (!owned.Any(c => target.Contains($".{c}"))) continue;

                foreach (var size in new[]
                         { "Height", "Width", "MinHeight", "MinWidth", "MaxWidth", "Padding" })
                {
                    if (Regex.IsMatch(style.Groups[2].Value, $@"Property=""{size}"""))
                    {
                        offenders.Add($"{name}: {selector} sets {size}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "views re-declaring a size the scale owns — move it into Density.axaml:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheOverlayTileAgreesWithTheScale()
    {
        // The one number that genuinely has to live twice: the bars lay
        // themselves out in C# and cannot read a style. `OverlayConverters`
        // documents itself as the duplicate; this is the check that keeps the
        // duplicate honest rather than trusting the comment.
        var constant = Regex.Match(
            File.ReadAllText(Path.Combine(
                RepoRoot(), "src", "Lightbox.App", "Docking", "OverlayConverters.cs")),
            @"OverlayTile\s*=\s*(\d+)");
        Assert.True(constant.Success, "OverlayConverters no longer declares OverlayTile");

        var tile = Setter(Density(), "Button.icon", "Height");
        Assert.Equal(tile, constant.Groups[1].Value);
    }
}
