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

    [Fact]
    public void TheSharedChromeNamesRolesRatherThanColours()
    {
        // App.axaml carries the docker and overlay templates, so every panel in
        // the application inherits whatever it says. It is the one file where a
        // stray literal is worst, which is why it is the one held to this.
        var app = Read("App.axaml");

        var literals = Regex.Matches(app, @"#[0-9a-fA-F]{6,8}\b")
            .Select(m => m.Value)
            .Where(v => v is not "#00000001")   // the grip's hit target, not a colour
            .Where(v => v is not "#FF7A00")     // the splash placeholder; branding is deferred
            .ToList();

        Assert.True(
            literals.Count == 0,
            $"raw colours left in the shared chrome: {string.Join(", ", literals)}");
    }
}
