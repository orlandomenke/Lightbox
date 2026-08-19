using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.App.Services;

/// <summary>
/// The artist's own library of effects, beside their brushes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shaped after <see cref="PresetStore"/> rather than invented</b>: one JSON
/// file next to <c>brushes.json</c>, camel-cased, and a corrupt one loads as
/// empty rather than blocking anything. A library that can stop an artist
/// working is worse than no library.
/// </para>
/// <para>
/// <b>Global only, for now, and that is a smaller claim than it looks.</b>
/// Symbols have both a project scope and a global one because a project has to
/// re-render on another machine with no library installed — and a preset has no
/// such duty, because using one <em>copies</em> its parameters into the
/// document. Nothing renders from here. A project-scoped shelf is worth having
/// so a show can carry its own fire, and it is a separate change with nothing
/// in this one to undo.
/// </para>
/// </remarks>
public static class EffectPresetStore
{
    public static string StorePath =>
        Path.Combine(Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "effects.json");

    public sealed class State
    {
        public List<EffectPreset> Presets { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
            // A corrupt library must never block making an effect by hand.
            return new State();
        }
    }

    public static void Save(State state, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            path ??= StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, Json));
        }
        catch
        {
            // Same reasoning as the load: failing to write the library is a lost
            // preset, and taking the application down with it loses the work
            // that was about to be saved.
        }
    }
}
