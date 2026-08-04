using System.Text.RegularExpressions;
using Avalonia.Input;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

/// <summary>
/// Every keystroke the app responds to has to be in the registry the Configure window
/// reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>The failure this catches is invisible to every other kind of test.</b> A gesture
/// written straight onto a menu item works perfectly: the key does the thing, nothing is
/// red, and the feature is done by any reasonable measure. What it is not is
/// <em>configurable</em> — it cannot be found in the shortcut editor, cannot be searched
/// for, cannot be rebound, and an artist who remaps everything else finds one key that
/// ignores them.
/// </para>
/// <para>
/// It is also how a gesture goes missing entirely. <c>Ctrl+S</c> was wired to the Save
/// menu item and never registered, so nothing ever asked the obvious next question —
/// which is that <c>Ctrl+Shift+S</c> had no binding at all. That was reported as "Save As
/// does not open", and it is really "Save As was never a shortcut".
/// </para>
/// <para>
/// <b>The exemption list is empty, and that is the state to keep it in.</b> It existed for
/// exactly as long as it took SAVE1 to register Save and add Save as; it stays here because
/// the alternative to an empty list is a filter, and a filter hides what an empty list
/// advertises. Both checks below still run against it, so an entry added later has to earn
/// its place and cannot outlive its reason.
/// </para>
/// </remarks>
public class ShortcutRegistrationTests
{
    /// <summary>Gestures declared in XAML that are not yet in the registry.</summary>
    /// <remarks>
    /// Shrink this list, never grow it. Each entry is a key an artist cannot rebind.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownUnregistered = new();

    private static string ViewsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "Lightbox.App", "Views");
    }

    /// <summary>Every <c>InputGesture</c> / <c>HotKey</c> written into the XAML.</summary>
    private static List<(string Gesture, string File)> DeclaredInXaml()
    {
        var found = new List<(string, string)>();
        foreach (var file in Directory.GetFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (Match match in Regex.Matches(
                         File.ReadAllText(file), @"(?:InputGesture|HotKey)=""([^""]+)"""))
            {
                found.Add((match.Groups[1].Value, Path.GetFileName(file)));
            }
        }
        return found;
    }

    [Fact]
    public void EveryGestureInTheXamlIsAlsoInTheShortcutRegistry()
    {
        var map = new ShortcutMap();
        var registered = map.Definitions
            .Where(d => d.Current is not null)
            .Select(d => d.Current!.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unregistered = DeclaredInXaml()
            .Where(g => !registered.Contains(g.Gesture))
            .Where(g => !KnownUnregistered.ContainsKey(g.Gesture))
            .ToList();

        Assert.True(
            unregistered.Count == 0,
            "these gestures are wired straight into the XAML and are not in ShortcutMap, so "
            + "they cannot be seen or rebound in Configure: "
            + string.Join(", ", unregistered.Select(g => $"{g.Gesture} ({g.File})")));
    }

    [Fact]
    public void TheExemptionListDescribesGesturesThatAreActuallyThere()
    {
        // An exemption for a gesture nobody declares any more is a line that will outlive
        // its reason and quietly permit a future collision with the same keys.
        var declared = DeclaredInXaml().Select(g => g.Gesture).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (gesture, why) in KnownUnregistered)
        {
            Assert.True(
                declared.Contains(gesture),
                $"{gesture} is exempted as \"{why}\" but no XAML declares it — delete the exemption");
        }
    }

    [Fact]
    public void AnExemptedGestureStopsBeingExemptOnceItIsRegistered()
    {
        // The list is debt, so it has to be self-clearing: registering one properly and
        // leaving it here would keep the guard blind to that key forever.
        var map = new ShortcutMap();
        var registered = map.Definitions
            .Where(d => d.Current is not null)
            .Select(d => d.Current!.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var redundant = KnownUnregistered.Keys.Where(registered.Contains).ToList();

        Assert.True(
            redundant.Count == 0,
            $"now registered, so remove from the exemption list: {string.Join(", ", redundant)}");
    }
}
