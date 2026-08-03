using System.Text.Json;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster.Tips;

namespace Lightbox.App.Services;

/// <summary>
/// The brush tip library: the tips an artist has made or scanned, kept where
/// their scope says they belong.
/// </summary>
/// <remarks>
/// <para>
/// Three scopes, and the split is the one palettes already settled. <b>User</b>
/// tips live beside the other app settings and are there with no project open,
/// because someone who opens Lightbox to draw one picture should still have
/// their brushes. <b>Project</b> tips live in the project and are shared by
/// every document under it, because a tip is part of how a project looks —
/// the same argument that put palettes and the remembered brush there.
/// <b>Document</b> tips are <see cref="Doc.BrushTips"/>, which already exists
/// and stays what it is: the flattening target, so an exported file renders
/// with no library present.
/// </para>
/// <para>
/// Only the third of those is in the document. The other two are a source of
/// tips to <em>choose</em> from; the moment one is painted with, its raster is
/// copied into the document under the same id, and from then on the drawing
/// does not care whether the library still exists. That is invariant 1, and it
/// is why a tip carries its pixels rather than a path.
/// </para>
/// </remarks>
public static class TipStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Where the user library lives, beside brushes.json.</summary>
    public static string StorePath =>
        Path.Combine(Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "tips.json");

    public sealed class State
    {
        public List<BrushTip> Tips { get; set; } = [];
    }

    public static State Load(string? path = null)
    {
        try
        {
            path ??= StorePath;
            if (!File.Exists(path)) return new State();
            return JsonSerializer.Deserialize<State>(File.ReadAllText(path), Json) ?? new State();
        }
        catch
        {
            return new State(); // a corrupt library must never block painting
        }
    }

    public static void Save(State state, string? path = null)
    {
        try
        {
            path ??= StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, Json));
        }
        catch
        {
            // best effort — losing the library must never crash the app
        }
    }

    /// <summary>
    /// Everything visible right now: the project's tips first when there is a
    /// project, then the user's, then the built-in catalogue.
    /// </summary>
    /// <remarks>
    /// Project first because it is the more specific scope, and de-duplicated
    /// by id so a tip promoted from user to project does not appear twice. The
    /// built-ins come last because they are always there — an artist's own work
    /// should never be pushed down the list by shapes that shipped with the app.
    /// </remarks>
    public static List<BrushTip> Available(Project? project, State? user = null, bool includeBuiltIn = true)
    {
        var all = new List<BrushTip>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tip in project?.Manifest.Tips ?? [])
        {
            if (seen.Add(tip.Id)) all.Add(tip);
        }
        foreach (var tip in (user ?? Load()).Tips)
        {
            if (seen.Add(tip.Id)) all.Add(tip);
        }
        if (includeBuiltIn)
        {
            foreach (var tip in TipCatalogue.All)
            {
                if (seen.Add(tip.Id)) all.Add(tip);
            }
        }

        return all;
    }

    /// <summary>
    /// Copy a library tip's raster into the document, so the drawing keeps
    /// rendering when the library is gone. Idempotent.
    /// </summary>
    public static void AdoptInto(Doc doc, BrushTip tip)
    {
        if (string.IsNullOrEmpty(tip.Png)) return;
        doc.BrushTips[tip.Id] = tip.Png;
    }
}
