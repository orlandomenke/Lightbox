using System.Reflection;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Settings the engine does not read must not be offered, and must not ship set.
/// </summary>
/// <remarks>
/// <para>
/// B23. Four medium settings were once measured at <b>0 of 41 600 pixels different</b>
/// between all-zero and all-one. Two of them — <c>Body</c> and <c>Relief</c> — were then
/// implemented. The other two are the directional advection loop that
/// <c>DESIGN-fluid-media.md</c> sequences last behind two open questions, so they are still
/// read by nothing, and until they are the honest thing is to not offer them: charter O7,
/// a setting that does nothing is worse than an absent one, because it teaches an artist
/// that the panel lies.
/// </para>
/// <para>
/// <b>The list below is meant to shrink.</b> Both assertions run in each direction, so
/// implementing one of these and forgetting this file fails the test with instructions —
/// the same self-clearing shape as <c>KnownFaint</c> in the picker tests and the shortcut
/// exemptions, both of which have already caught a real mistake.
/// </para>
/// </remarks>
public class UnreadSettingsTests(ITestOutputHelper output)
{
    /// <summary>
    /// Medium settings that exist on the record and reach no pixel, with why.
    /// </summary>
    /// <remarks>
    /// Kept on the record rather than deleted so a document written while they were offered
    /// still round-trips. The record says the same thing at each declaration; this is the
    /// enforcement.
    /// </remarks>
    private static readonly Dictionary<string, string> Unread = new()
    {
        ["BristleDrag"] = "advection loop, DESIGN-fluid-media.md piece (3) — the look is "
            + "reachable now via TipShape.Bristle with AngleFollowsDirection",
        ["Pickup"] = "advection loop, DESIGN-fluid-media.md piece (3) — no cheap substitute",
    };

    /// <summary>Every property an artist can reach on the medium page, by name.</summary>
    private static HashSet<string> MediumViewModelProperties() =>
        typeof(MainViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n.StartsWith("Medium", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void ASettingTheEngineIgnoresIsNotOfferedToTheArtist()
    {
        // A slider bound to one of these moved, wrote to the record, marked the brush
        // modified, and changed no pixel. Reflection rather than a XAML scan because the
        // binding cannot exist without the property, and the property is the thing a future
        // panel would reach for.
        var offered = MediumViewModelProperties();

        var wrong = Unread.Keys
            .Where(name => offered.Contains("Medium" + name))
            .Select(name => $"Medium{name} ({Unread[name]})")
            .ToList();

        Assert.True(
            wrong.Count == 0,
            "these settings are exposed and the engine reads none of them: " + string.Join("; ", wrong));
    }

    [Fact]
    public void NoShippedBrushSetsASettingTheEngineIgnores()
    {
        // The half that survives hiding the control. The Oil preset shipped
        // `BristleDrag = 0.5, Pickup = 0.4` and Watercolor shipped `Pickup = 0.25`, so every
        // stroke made with them serialised two keys promising behaviour that does not exist —
        // in the document, where it outlives the panel it came from.
        var lying = new List<string>();

        foreach (var preset in BuiltInPresets.Create())
        {
            var medium = preset.Settings.Medium;
            foreach (var (name, why) in Unread)
            {
                var value = (double)typeof(MediumSettings).GetProperty(name)!.GetValue(medium)!;
                if (value != 0) lying.Add($"{preset.Name}.{name} = {value} ({why})");
            }
        }

        output.WriteLine(lying.Count == 0
            ? "no shipped preset sets an unread medium setting"
            : string.Join("\n", lying));
        Assert.True(lying.Count == 0, "shipped presets set settings nothing reads: " + string.Join("; ", lying));
    }

    [Fact]
    public void TheUnreadListDoesNotOutliveTheSettingsOnIt()
    {
        // Stops this file becoming the stale thing it guards against. If a name here stops
        // being a real property — deleted, renamed, or the whole idea abandoned — the
        // exemption is describing nothing and should go.
        var missing = Unread.Keys
            .Where(name => typeof(MediumSettings).GetProperty(name) is null)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "these are no longer settings at all — take them out of Unread: " + string.Join(", ", missing));
    }

    [Fact]
    public void ASettingThatWasImplementedIsNotStillListedAsUnread()
    {
        // The direction that matters when somebody does the work. Body and Relief were on
        // this list and came off it when Impasto.Shade landed; the next pair should come off
        // the same way, and this is what says so rather than leaving a stale exemption
        // quietly suppressing a control that now works.
        var implemented = new[] { "Body", "Relief", "PaintLoad", "Wetness", "Viscosity" };

        var stale = implemented.Where(Unread.ContainsKey).ToList();

        Assert.True(
            stale.Count == 0,
            "these are implemented and must not be listed as unread: " + string.Join(", ", stale));
    }
}
