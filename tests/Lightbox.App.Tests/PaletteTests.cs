using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The colour system is one file, and the chrome names roles rather than hexes.
/// </summary>
/// <remarks>
/// <para>
/// Stage 1 of <c>docs/DESIGN-ui-system.md</c>. Before it there were 120 colour
/// literals across 60 distinct values and six resource references, which made
/// "change the application's colour" a hundred edits with no way to check they
/// agreed.
/// </para>
/// <para>
/// <b>The guard is the point.</b> A token layer with nothing defending it lasts
/// until the first person in a hurry, and the failure is invisible — one panel a
/// shade off from the one beside it reads as a rendering quirk, not as a literal
/// somebody typed. So the interesting test here is not that the tokens exist; it
/// is that <c>App.axaml</c> has stopped using raw values.
/// </para>
/// </remarks>
public class PaletteTests
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
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lightbox.App", Path.Combine(parts)));

    [AvaloniaFact]
    public void EveryTokenTheDesignNamesResolves()
    {
        // Named for the role, so the assertion reads as the design does.
        string[] roles =
        [
            "BackgroundPrimaryBrush", "BackgroundSecondaryBrush",
            "SurfacePanelBrush", "SurfaceElevatedBrush",
            "TextPrimaryBrush", "TextSecondaryBrush",
            "BorderBrush", "BorderStrongBrush",
            "AccentCoralBrush", "AccentMagentaBrush", "AccentVioletBrush",
            "AccentCyanBrush", "AccentLimeBrush", "AccentAmberBrush",
            "AccentGradientBrush",
            "StatusInfoBrush", "StatusWarningBrush", "StatusErrorBrush", "StatusSuccessBrush",
            "CanvasPaperBrush",
        ];

        foreach (var role in roles)
        {
            Assert.True(
                Application.Current!.TryFindResource(role, out var found),
                $"{role} does not resolve — the palette is not merged, or the key was renamed");
            Assert.NotNull(found);
        }
    }

    [AvaloniaFact]
    public void TheCoreSurfacesAreTheColoursTheDesignGave()
    {
        // Pinned to the mockup's values. A retune is a deliberate edit to the
        // palette and to this list, not a drift somebody notices months later.
        (string Role, string Hex)[] core =
        [
            ("BackgroundPrimaryBrush", "#FF0B0D12"),
            ("BackgroundSecondaryBrush", "#FF13161D"),
            ("SurfacePanelBrush", "#FF1A1E27"),
            ("SurfaceElevatedBrush", "#FF222634"),
            ("TextPrimaryBrush", "#FFE6E8F0"),
            ("TextSecondaryBrush", "#FF9AA1B2"),
        ];

        foreach (var (role, hex) in core)
        {
            Application.Current!.TryFindResource(role, out var found);
            var brush = Assert.IsType<SolidColorBrush>(found);
            Assert.Equal(hex, brush.Color.ToString(), ignoreCase: true);
        }
    }

    [AvaloniaFact]
    public void TheThemeAgreesWithThePalette()
    {
        // **The half of the colour system that was missing for a week.**
        // Tokenising the views only reaches surfaces somebody aimed at a token.
        // Every stock control — toggle buttons, slider thumbs, checkboxes,
        // radios, focus rings, list selection — paints from the *theme's*
        // palette, and Fluent's accent is Windows blue. Nothing was red; the
        // app simply wore two colour systems, and the opacity slider wore both
        // at once: our coral track, Fluent's #0078D7 thumb.
        //
        // Asserted through the theme's own resource rather than by reading
        // App.axaml, because what matters is the colour a control resolves at
        // runtime, not the text somebody typed.
        Assert.True(Application.Current!.TryFindResource(
            "SystemAccentColor", Avalonia.Styling.ThemeVariant.Dark, out var accent));
        var violet = Assert.IsType<Color>(accent);

        Application.Current!.TryFindResource("AccentVioletBrush", out var role);
        Assert.Equal(((SolidColorBrush)role!).Color, violet);
    }

    [Fact]
    public void TheThemePaletteIsWrittenInHexOnPurpose()
    {
        // ColorPaletteResources is built before the merged dictionaries it
        // would look into, so {StaticResource} there cannot resolve — the two
        // literals in App.axaml are unavoidable rather than sloppy. This is the
        // check the reference would have been, so the comment saying so is not
        // the only thing keeping them in step.
        var app = Read("App.axaml");
        var palette = Read("Styles", "Palette.axaml");

        foreach (var (property, token) in new[]
                 {
                     ("Accent", "AccentViolet"),
                     ("RegionColor", "SurfaceElevated"),
                 })
        {
            var used = Regex.Match(app, $@"{property}=""(#[0-9a-fA-F]{{6,8}})""");
            Assert.True(used.Success, $"the theme no longer sets {property}");

            var defined = Regex.Match(palette, $@"x:Key=""{token}"">(#[0-9a-fA-F]{{6,8}})<");
            Assert.True(defined.Success, $"{token} is no longer in the palette");

            Assert.Equal(defined.Groups[1].Value, used.Groups[1].Value, ignoreCase: true);
        }
    }

    [Fact]
    public void NoViewInventsItsOwnChromeColour()
    {
        // Every view — a literal in one panel is how two panels come to be
        // *nearly* the same colour, which nobody can see deliberately and
        // everybody can see accidentally.
        //
        // Matches colour ASSIGNMENTS, not anything hex-shaped. The first version
        // matched any #rrggbb and reported a tooltip reading "Hex colour, e.g.
        // #1a1a1a" and a comment about transcribing #c04a2f — prose, both of
        // them. A guard that cries wolf gets an exception added to shut it up,
        // and then it is guarding nothing.
        var assignment = new Regex(
            @"(?:Background|Foreground|BorderBrush|Fill|Stroke|Color|Value)=""(#[0-9a-fA-F]{6,8})""");

        string[] allowed =
        [
            "#00000001",  // the drag grip's fill: a hit target, not a colour
            "#FF7A00",    // the splash placeholder, defined in App.axaml;
                          // branding is deferred entirely until the vector
                          // tooling exists, so this is the one colour the
                          // design deliberately has no opinion about yet
        ];

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "Lightbox.App"), "*.axaml",
                     SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "Palette.axaml") continue;      // where colour is defined
            if (name is "SplashWindow.axaml") continue; // the placeholder; branding is deferred

            var text = File.ReadAllText(file);

            // The theme's own palette is the one place a role CANNOT be named:
            // a ColorPaletteResources is built before the merged dictionaries
            // it would look into, so {StaticResource} there does not resolve.
            //
            // Cut out by element rather than by allowing its values, because an
            // allowed hex is an exception that outlives its reason — it keeps
            // passing after the colour it excused has moved. The element is
            // still guarded: TheThemePaletteIsWrittenInHexOnPurpose asserts
            // those literals equal the tokens they stand in for.
            text = Regex.Replace(text, @"<ColorPaletteResources\b[^>]*/?>", "");

            // Layer folder colours are DOCUMENT DATA — an artist picks one and
            // the file stores it. Re-mapping them to accents would change what
            // is already saved in people's work, which is not a re-skin.
            //
            // Derived from the file's own Tag values rather than listed here,
            // because each swatch writes its colour twice: once as the Tag that
            // is stored, once on the icon that previews it. Listing them would
            // mean editing this test to add a colour to a menu.
            var documentData = Regex.Matches(text, @"Tag=""(#[0-9a-fA-F]{6,8})""")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (Match m in assignment.Matches(text))
            {
                var value = m.Groups[1].Value;
                if (allowed.Contains(value)) continue;
                if (documentData.Contains(value)) continue;
                offenders.Add($"{name}: {value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "views naming raw colours instead of roles:\n  " + string.Join("\n  ", offenders));
    }
}
