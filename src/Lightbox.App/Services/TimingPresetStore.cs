using System.Text.Json;
using Lightbox.Core.Timeline;

namespace Lightbox.App.Services;

/// <summary>
/// The timing patterns an artist has saved, beside their brushes and shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// Per application rather than per document, and that is the one place a timing
/// preset differs from everything else Q11 touches. A pattern is <em>how this
/// animator times things</em> — it belongs to them, not to one walk cycle, and a
/// studio's slow-in is the same slow-in in every scene. Invariant 4 is not in
/// play: a preset never reaches a pixel. It performs an edit, once, and what it
/// leaves behind is an ordinary exposure sheet in the document. Deleting a preset
/// cannot change a drawing.
/// </para>
/// <para>
/// <see cref="TimingPreset.BuiltIns"/> are not stored. Writing them out would
/// freeze them into every artist's settings file the first time they opened the
/// app, so a later correction to "slow in" would reach nobody.
/// </para>
/// </remarks>
public static class TimingPresetStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Test seam. Null means the real file beside the other settings.</summary>
    public static string? PathOverride { get; set; }

    public static string Path =>
        PathOverride ?? System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "timing.json");

    /// <summary>One saved pattern, as the file holds it.</summary>
    /// <remarks>
    /// The holds are stored as the text an artist typed rather than as an array,
    /// so the file stays readable and hand-editable — <c>"1, 1, 2, 3, 4"</c> says
    /// what it is where <c>[1,1,2,3,4]</c> only nearly does.
    /// </remarks>
    public sealed class Entry
    {
        public string Name { get; set; } = "";

        public string Pattern { get; set; } = "";
    }

    public sealed class State
    {
        public List<Entry> Presets { get; set; } = [];
    }

    /// <summary>The artist's own patterns. Built-ins are not in here.</summary>
    public static List<TimingPreset> Load()
    {
        var presets = new List<TimingPreset>();
        try
        {
            if (!File.Exists(Path)) return presets;
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(Path), Json);
            foreach (var entry in state?.Presets ?? [])
            {
                // A malformed entry is skipped rather than failing the load: one
                // bad hand-edit must not cost an artist the rest of their
                // patterns.
                if (TimingPreset.TryParse(entry.Name, entry.Pattern, out var preset)) presets.Add(preset);
            }
        }
        catch
        {
            // A corrupt or unreadable file leaves the built-ins, which is a
            // working app rather than a dialog on startup.
        }
        return presets;
    }

    public static void Save(IEnumerable<TimingPreset> presets)
    {
        try
        {
            var state = new State
            {
                Presets = presets.Select(p => new Entry { Name = p.Name, Pattern = p.Pattern }).ToList(),
            };
            if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } dir) Directory.CreateDirectory(dir);
            // Write then move, so a crash mid-save cannot leave a half-written
            // file where the patterns were.
            var temp = Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, Path, overwrite: true);
        }
        catch
        {
            // Failing to save a preset must not take the drawing down with it.
        }
    }
}
