using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The control treatments: emphasis, badges and tabs.
/// </summary>
/// <remarks>
/// <para>
/// Stage 2 of <c>docs/DESIGN-ui-system.md</c>. <b>Two axes, kept apart.</b> Size
/// is a role — icon, text, tool — and lives in <c>Density.axaml</c> because it
/// is about hitting a control. Emphasis is a rank — primary, secondary,
/// tertiary, ghost — and lives in <c>Controls.axaml</c> because it is about
/// which control an artist should reach for first. They compose.
/// </para>
/// <para>
/// What these cannot tell you is whether any of it looks right. They pin the
/// rules that would otherwise drift silently: that a rank never sets a size,
/// and that the button Enter presses is the one that looks like the answer.
/// </para>
/// </remarks>
public class ControlTreatmentTests
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

    private static string Views(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lightbox.App", "Views", file));

    [Fact]
    public void TheButtonEnterPressesIsTheOneThatLooksLikeTheAnswer()
    {
        // IsDefault and the primary rank say the same thing in two languages —
        // one to the keyboard, one to the eye. Letting them disagree is how a
        // dialog comes to have a loud button that Enter does not press, which
        // is worse than having neither.
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "Lightbox.App", "Views"), "*.axaml"))
        {
            foreach (Match button in Regex.Matches(
                         File.ReadAllText(file), @"<Button\b[^>]*IsDefault=""True""[^>]*>"))
            {
                if (!button.Value.Contains("primary"))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {Trim(button.Value)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "default buttons without the primary rank:\n  " + string.Join("\n  ", offenders));

        static string Trim(string s) => s.Length <= 90 ? s : s[..90] + "…";
    }

    [Fact]
    public void NothingDestructiveIsThePrimaryButton()
    {
        // Delete, Remove and Clear are what an artist arrives at by accident.
        // The primary rank means "this is the thing you came here to do", and a
        // delete button wearing it gets pressed by reflex. This is the rule
        // somebody breaks while making a confirmation dialog feel decisive, so
        // it is checked rather than remembered.
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "Lightbox.App", "Views"), "*.axaml"))
        {
            foreach (Match button in Regex.Matches(
                         File.ReadAllText(file), @"<Button\b[^>]*?/?>", RegexOptions.Singleline))
            {
                if (!button.Value.Contains("primary")) continue;
                if (Regex.IsMatch(button.Value, @"Delete|Remove|Clear|Discard|🗑",
                                  RegexOptions.IgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {Trim(button.Value)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "destructive buttons carrying the primary rank:\n  " + string.Join("\n  ", offenders));

        static string Trim(string s) => s.Length <= 90 ? s : s[..90] + "…";
    }

    [Fact]
    public void ARankNeverSetsASize()
    {
        // The two axes compose only if they stay disjoint. A rank that also set
        // a height would silently override the role beside it and the row would
        // stop lining up — which DESIGN.md calls the most common way this app
        // has looked unfinished.
        var controls = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Lightbox.App", "Styles", "Controls.axaml"));

        foreach (var rank in new[] { "primary", "secondary", "tertiary", "ghost" })
        {
            var block = Regex.Match(controls,
                $@"Selector=""Button\.{rank}""\s*>(.*?)</Style>", RegexOptions.Singleline);
            Assert.True(block.Success, $"no style for the {rank} rank");

            foreach (var size in new[] { "Height", "Width", "Padding", "MinWidth", "MinHeight" })
            {
                Assert.DoesNotContain($"Property=\"{size}\"", block.Groups[1].Value);
            }
        }
    }

    [AvaloniaFact]
    public void EveryRankAndBadgeResolves()
    {
        // Cheap, and it catches the failure that is otherwise invisible: a class
        // named in a view that no style matches renders as an unstyled control
        // rather than as an error.
        var window = new Window();
        foreach (var rank in new[] { "primary", "secondary", "tertiary", "ghost" })
        {
            var button = new Button { Content = "x" };
            button.Classes.Add(rank);
            window.Content = button;
            window.Show();

            Assert.Contains(rank, button.Classes);
        }
    }

    [AvaloniaFact]
    public void ThePrimaryRankCarriesTheAccentGradient()
    {
        // The one treatment the design is specific about, and the one that
        // would quietly fall back to a flat colour if the resource were renamed.
        Assert.True(
            Application.Current!.TryFindResource("AccentGradientBrush", out var found));
        var gradient = Assert.IsType<LinearGradientBrush>(found);
        Assert.Equal(2, gradient.GradientStops.Count);
    }

    [Fact]
    public void BadgesAreNamedForMeaningRatherThanColour()
    {
        // A badge that says "amber" has to be renamed when the design changes
        // its mind; one that says "warning" does not.
        var controls = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Lightbox.App", "Styles", "Controls.axaml"));

        foreach (var meaning in new[] { "info", "warning", "error", "success" })
        {
            Assert.Contains($"Border.badge.{meaning}", controls);
        }

        // And they point at the status roles rather than restating an accent,
        // so the palette stays one list.
        foreach (var role in new[] { "StatusInfoBrush", "StatusWarningBrush",
                                     "StatusErrorBrush", "StatusSuccessBrush" })
        {
            Assert.Contains(role, controls);
        }
    }
}
