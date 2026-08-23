using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.Views;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The diagnostics are reachable from the menu bar, and the switch that opens
/// a console survives the restart it exists for.
/// </summary>
/// <remarks>
/// Whether a console window actually appears is Win32 and unverifiable here;
/// <c>MANUAL_TESTING.md</c> owns that. What these close is everything up to it:
/// the menu exists, the setting persists, and it writes nothing when nobody has
/// touched it.
/// </remarks>
[Collection("BrushState")]
public class DiagnosticsMenuTests : BrushStateIsolated
{
    [Fact]
    public void TheConsoleSwitchIsOffUntilSomebodyAsks()
    {
        Assert.False(new AppSettings().ShowDiagnosticsConsole);
    }

    [Fact]
    public void TheSwitchIsListedInTheSettingsFileLikeEveryOtherPreference()
    {
        // Worth stating why this is NOT the "optional means absent" case.
        // That rule is about the document record, where a default written per
        // stroke is a key on every stroke of every drawing and bakes a default
        // into art. A user settings file is the opposite: it is the one place
        // a preference is meant to be visible and hand-editable, and every
        // preference here already serializes. Singling this one out would make
        // the file inconsistent, not tidier.
        Assert.Contains("\"ShowDiagnosticsConsole\"", new AppSettings().Serialize());
    }

    [Fact]
    public void TheSwitchSurvivesTheRestartItExistsFor()
    {
        // The one job this setting has is "turn it on, restart, reproduce". A
        // switch that forgot itself between runs would be no use for it.
        var settings = new AppSettings { ShowDiagnosticsConsole = true };
        var restored = AppSettings.Deserialize(settings.Serialize());
        Assert.True(restored.ShowDiagnosticsConsole);
    }

    [AvaloniaFact]
    public void HelpOffersTheFolderTheConsoleAndTheBuild()
    {
        var window = new MainWindow();
        var menu = window.GetLogicalDescendants().OfType<Menu>().FirstOrDefault();
        Assert.NotNull(menu);

        var help = menu!.Items.OfType<MenuItem>()
            .FirstOrDefault(m => m.Header as string == "_Help");
        Assert.NotNull(help);

        var headers = help!.Items.OfType<MenuItem>()
            .Select(m => m.Header as string ?? "")
            .ToList();

        Assert.Contains(headers, h => h.Contains("diagnostics folder"));
        Assert.Contains(headers, h => h.Contains("console"));
    }

    [Fact]
    public void TheBuildLabelNamesTheCommitRatherThanJustAVersion()
    {
        // A version number alone cannot be exact enough: an untagged bundle and
        // the release it precedes can share one, and a version can be re-cut.
        // The SDK appends +<sha> to the informational version, and that is the
        // half that makes a bug report answerable.
        Assert.Matches(new Regex(@"\+[0-9a-f]{7,}"), DiagnosticLog.Build);
    }
}
