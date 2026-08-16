using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Geometry;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Lightbox.App.Tests;

/// <summary>
/// The icon set, walked from both ends.
/// </summary>
/// <remarks>
/// <para>
/// An unregistered icon fails quietly in two directions, and this file closes
/// both. A name in <see cref="IconSet"/> with no geometry behind it shows a
/// blank button; a geometry referenced from XAML that nobody registered cannot
/// be found when the set is redrawn, which is the whole reason the roadmap
/// wanted "one place that names every icon" before it wanted better artwork.
/// </para>
/// <para>
/// The third test is the one with a history. Four tool buttons drew Unicode
/// characters from the system font instead of icons — including U+1FA84, an
/// emoji, which rendered in full colour beside monoline glyphs where the font
/// had it and as a blank box where it did not.
/// </para>
/// </remarks>
public class IconSetTests(ITestOutputHelper output)
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

    private static string AppDir(params string[] parts) =>
        Path.Combine([RepoRoot(), "src", "Lightbox.App", .. parts]);

    // ---- the registry resolves ---------------------------------------------------------

    [AvaloniaFact]
    public void EveryIconNameResolvesToAGeometry()
    {
        var missing = IconSet.All.Where(n => IconSet.Resolve(n) is null).ToList();

        output.WriteLine($"{IconSet.All.Count} icons, {missing.Count} unresolved");
        Assert.Empty(missing);
    }

    [Fact]
    public void TheRegistryHasNoDuplicates()
    {
        // A name listed twice makes the count above lie about the coverage.
        Assert.Equal(IconSet.All.Count, IconSet.All.Distinct().Count());
    }

    /// <summary>
    /// Every geometry defined in the dictionary is named in the registry.
    /// </summary>
    /// <remarks>
    /// The direction that catches an icon drawn and then forgotten: it exists,
    /// it renders, and nothing enumerating the set — a redraw, a picker, this
    /// file — knows it is there.
    /// </remarks>
    [Fact]
    public void EveryDrawnIconIsNamedInTheRegistry()
    {
        var xaml = File.ReadAllText(AppDir("Styles", "Icons.axaml"));
        var drawn = Regex.Matches(xaml, @"x:Key=""(Icon[A-Za-z]+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

        output.WriteLine($"{drawn.Count} drawn: {string.Join(", ", drawn.Except(IconSet.All))} unregistered");
        Assert.NotEmpty(drawn);
        Assert.Empty(drawn.Except(IconSet.All));
    }

    /// <summary>Every icon a view asks for by name has actually been drawn.</summary>
    [Fact]
    public void EveryIconReferencedByAViewIsInTheRegistry()
    {
        var referenced = new SortedSet<string>();
        foreach (var dir in new[] { AppDir("Views"), AppDir("Styles") })
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.axaml", SearchOption.AllDirectories))
            {
                foreach (Match m in Regex.Matches(
                             File.ReadAllText(file), @"StaticResource (Icon[A-Za-z]+)"))
                {
                    referenced.Add(m.Groups[1].Value);
                }
            }
        }

        output.WriteLine($"{referenced.Count} referenced by views");
        Assert.NotEmpty(referenced);
        Assert.Empty(referenced.Except(IconSet.All));
    }

    // ---- no tool button falls back to a system font --------------------------------------

    /// <summary>
    /// No tool button draws a character instead of an icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The regression guard, and it is written against the <em>shape</em> of the
    /// mistake rather than against the eleven characters that were there: any
    /// non-ASCII literal inside the tool rail is a glyph borrowed from whatever
    /// font the platform resolved, at whatever weight and baseline that font
    /// has. It cannot take its colour from a style, cannot dim with the button,
    /// and cannot be redrawn with the rest of the set.
    /// </para>
    /// <para>
    /// Scoped to the tool rail rather than the whole file, because a bullet or
    /// an arrow in a label elsewhere is typography rather than an icon —
    /// widening this to every <c>TextBlock</c> would make it fail for reasons
    /// that are not this rule.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoToolButtonDrawsAGlyphFromASystemFont()
    {
        var xaml = File.ReadAllText(AppDir("Views", "MainWindow.axaml"));
        var start = xaml.IndexOf("x:Name=\"ToolButtons\"", StringComparison.Ordinal);
        Assert.True(start > 0, "could not find the tool rail");
        var end = xaml.IndexOf("</WrapPanel>", start, StringComparison.Ordinal);
        Assert.True(end > start, "could not find the end of the tool rail");
        var rail = xaml[start..end];

        var offenders = Regex.Matches(rail, @"Text=""([^""]*)""")
            .Select(m => m.Groups[1].Value)
            .Where(t => t.Any(c => c > 127))
            .ToList();

        output.WriteLine(
            offenders.Count == 0
                ? "tool rail is all geometry"
                : $"glyphs still in the rail: {string.Join(" ", offenders)}");
        Assert.Empty(offenders);
    }

    /// <summary>
    /// No button anywhere wears a bare character where an icon belongs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tool-rail guard above, widened to the whole application once the
    /// set grew big enough to cover it: any Button, ToggleButton or
    /// RadioButton whose literal Content is entirely non-ASCII is a glyph
    /// borrowed from the system font, with everything that entails — no
    /// styleable stroke, no disabled dimming, per-platform metrics, and the
    /// 26px tile that DESIGN.md records as waiting on exactly this.
    /// </para>
    /// <para>
    /// A label with words in it passes ("＋ Swatch", "✦ AI Inbetween"): text
    /// is typography and keeps its prefix. Bound Content is invisible to this
    /// scan; the stateful pairs it cannot see (play/pause, collapse chevrons)
    /// are dual <c>Path</c>s toggled by IsVisible instead, so there is nothing
    /// left for a binding to hand a character to.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoButtonAnywhereWearsAGlyphInsteadOfAnIcon()
    {
        var offenders = new List<string>();
        var files = new[] { AppDir("Views"), AppDir("Styles") }
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.axaml", SearchOption.AllDirectories))
            .Append(AppDir("App.axaml"));

        foreach (var file in files)
        {
            var xaml = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(
                         xaml,
                         @"<(Button|ToggleButton|RadioButton|DropDownButton)\b[^>]*?Content=""([^""]+)""",
                         RegexOptions.Singleline))
            {
                var content = m.Groups[2].Value;
                if (content.StartsWith("{", StringComparison.Ordinal)) continue; // bound, not literal
                var visible = content.Replace(" ", "");
                if (visible.Length > 0 && visible.All(c => c > 127))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {content}");
                }
            }
        }

        output.WriteLine(
            offenders.Count == 0
                ? "every icon button is geometry"
                : $"glyph buttons remain: {string.Join(", ", offenders)}");
        Assert.Empty(offenders);
    }

    /// <summary>
    /// The docker's float button actually renders its drawn icon.
    /// </summary>
    /// <remarks>
    /// The one conversion with wiring subtle enough to break silently: a style
    /// cannot hand the same Path instance to every docker, so the drawing
    /// arrives via a ContentTemplate over a placeholder Content. If a later
    /// edit drops the placeholder or the template, the button goes blank and
    /// nothing else fails — this is the something else.
    /// </remarks>
    [AvaloniaFact]
    public void TheDockerFloatButtonRendersADrawnIcon()
    {
        var docker = new Docker { PanelId = DockPanelId.Layers, Title = "x" };
        var window = new Window { Content = docker };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var floatButton = docker.GetVisualDescendants().OfType<Button>()
            .First(b => b.Name == "PART_Float");
        var drawing = floatButton.GetVisualDescendants().OfType<ShapePath>().FirstOrDefault();

        Assert.NotNull(drawing);
        Assert.NotNull(drawing!.Data);
        window.Close();
    }

    /// <summary>
    /// Every tool in the rail resolves an icon, including each variant of the
    /// two tools whose button changes with their setting.
    /// </summary>
    [AvaloniaFact]
    public void EveryToolbarButtonResolvesAnIcon()
    {
        var unresolved = new List<string>();

        foreach (var tool in Enum.GetValues<ToolId>())
        {
            // The rail's own list, by name: a tool with no button needs no icon.
            var name = tool switch
            {
                ToolId.Brush => IconSet.Brush,
                ToolId.Eraser => IconSet.Eraser,
                ToolId.Fill => IconSet.Fill,
                ToolId.Picker => IconSet.Picker,
                ToolId.Gradient => IconSet.Gradient,
                ToolId.Move => IconSet.Move,
                ToolId.Pen => IconSet.Pen,
                ToolId.Width => IconSet.Width,
                ToolId.Arrow => IconSet.Arrow,
                ToolId.DirectSelect => IconSet.Points,
                ToolId.Shape => IconSet.ShapeRect,
                ToolId.Select => IconSet.SelectLasso,
                _ => null,
            };
            if (name is null) continue;
            if (IconSet.Resolve(name) is null) unresolved.Add($"{tool} -> {name}");
        }

        foreach (var shape in Enum.GetValues<ShapeKind>())
        {
            if (IconSet.Resolve(IconSet.ForShape(shape)) is null) unresolved.Add($"shape {shape}");
        }

        foreach (var variant in Enum.GetValues<SelectVariant>())
        {
            if (IconSet.Resolve(IconSet.ForSelect(variant)) is null) unresolved.Add($"select {variant}");
        }

        output.WriteLine(
            unresolved.Count == 0 ? "every tool and variant has an icon" : string.Join(", ", unresolved));
        Assert.Empty(unresolved);
    }

    /// <summary>
    /// Each shape and each select variant gets its <em>own</em> icon.
    /// </summary>
    /// <remarks>
    /// A mapping that resolved every variant to the same glyph would pass the
    /// test above and leave the button saying nothing about which variant is
    /// loaded — which is the only reason that button carries an icon at all.
    /// </remarks>
    [Fact]
    public void EachVariantIsToldApartFromTheOthers()
    {
        var shapes = Enum.GetValues<ShapeKind>().Select(IconSet.ForShape).ToList();
        Assert.Equal(shapes.Count, shapes.Distinct().Count());

        var variants = Enum.GetValues<SelectVariant>().Select(IconSet.ForSelect).ToList();
        Assert.Equal(variants.Count, variants.Distinct().Count());
    }

    /// <summary>
    /// A selection variant never reuses the shape tool's outline.
    /// </summary>
    /// <remarks>
    /// The two sets overlap in subject — box, ellipse, polygon — and mean
    /// different things: a solid outline is a shape the tool will draw, a dashed
    /// one is a region it will select. Sharing the geometry to save four paths
    /// would make the tool rail ambiguous in exactly the place two tools sit
    /// next to each other.
    /// </remarks>
    [Fact]
    public void ASelectionVariantIsNeverTheShapeToolsOutline()
    {
        var shapes = Enum.GetValues<ShapeKind>().Select(IconSet.ForShape).ToHashSet();
        var variants = Enum.GetValues<SelectVariant>().Select(IconSet.ForSelect).ToHashSet();

        Assert.Empty(shapes.Intersect(variants));
    }

    /// <summary>The set is monoline geometry, not filled shapes.</summary>
    /// <remarks>
    /// Every path is authored on a 24x24 grid and opens with the two moves that
    /// pin those bounds, so a stretched icon keeps its stroke width instead of
    /// going bold at one size and thin at another. A new icon that skips the
    /// prefix renders at the wrong weight next to its neighbours, which is the
    /// sizing question the roadmap says the old pile of assets could not answer.
    /// </remarks>
    [Fact]
    public void EveryIconIsAuthoredOnTheSameGrid()
    {
        var xaml = File.ReadAllText(AppDir("Styles", "Icons.axaml"));
        var offenders = Regex.Matches(xaml, @"x:Key=""(Icon[A-Za-z]+)"">([^<]*)<")
            .Where(m => !m.Groups[2].Value.StartsWith("M0,0 M24,24 ", StringComparison.Ordinal))
            .Select(m => m.Groups[1].Value)
            .ToList();

        output.WriteLine(
            offenders.Count == 0 ? "all on the 24x24 grid" : string.Join(", ", offenders));
        Assert.Empty(offenders);
    }
}
