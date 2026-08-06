using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Projects;

namespace Lightbox.App.Services;

/// <summary>
/// The export settings an artist has saved, beside their brushes and timing patterns.
/// </summary>
/// <remarks>
/// <para>
/// Per application rather than per document, for the same reason
/// <see cref="TimingPresetStore"/> is: these are <em>how this studio ships</em>, not
/// a property of one walk cycle, and a project's atlas settings are the same in
/// every animation under it. Invariant 4 is not in play — an export preset never
/// reaches a pixel in a document. It decides what a file on disk looks like, and
/// deleting one cannot change a drawing.
/// </para>
/// <para>
/// <see cref="ExportPreset.BuiltIns"/> are not written out. Freezing them into every
/// artist's settings file the first time they opened the app would mean a later
/// correction to "Character sprites" reached nobody.
/// </para>
/// </remarks>
public static class ExportPresetStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Test seam. Null means the real file beside the other settings.</summary>
    public static string? PathOverride { get; set; }

    public static string Path =>
        PathOverride ?? System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "export.json");

    private sealed class State
    {
        public List<ExportPreset> Presets { get; set; } = [];

        /// <summary>The preset used last, by name, so the next export starts there.</summary>
        public string? Last { get; set; }
    }

    /// <summary>The artist's own presets. Built-ins are not in here.</summary>
    public static List<ExportPreset> Load()
    {
        try
        {
            if (!File.Exists(Path)) return [];
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(Path), Json);
            // A nameless entry cannot be picked from a list, so it is dropped rather
            // than shown as a blank row.
            return (state?.Presets ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        }
        catch
        {
            // A corrupt or hand-mangled file leaves the built-ins, which is a working
            // app rather than a dialog on startup.
            return [];
        }
    }

    /// <summary>The name of the preset used last, or null.</summary>
    public static string? LoadLastUsed()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            return JsonSerializer.Deserialize<State>(File.ReadAllText(Path), Json)?.Last;
        }
        catch
        {
            return null;
        }
    }

    public static void Save(IEnumerable<ExportPreset> presets, string? lastUsed = null)
    {
        try
        {
            var state = new State
            {
                Presets = presets.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList(),
                Last = lastUsed,
            };
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            // Write then move, so a crash mid-save cannot leave a half-written file
            // where the presets were.
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, Path, overwrite: true);
        }
        catch
        {
            // Failing to save a preset must not take the export down with it.
        }
    }
}
