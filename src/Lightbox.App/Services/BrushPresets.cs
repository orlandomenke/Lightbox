using System.Text.Json;
using Lightbox.Core.Documents;

namespace Lightbox.App.Services;

/// <summary>A named, reusable brush configuration (Krita-style preset).</summary>
public sealed class BrushPreset
{
    public string Id { get; set; } = Ids.NewId("preset");

    public string Name { get; set; } = "Brush";

    public ToolKind Tool { get; set; } = ToolKind.Brush;

    public BrushSettings Settings { get; set; } = new();

    /// <summary>Custom tip carried by the preset (alpha-mask PNG, base64); copied into a document on first use.</summary>
    public string? TipPng { get; set; }

    public override string ToString() => Name;
}

/// <summary>The built-in presets. Users add their own on top (persisted).</summary>
public static class BuiltInPresets
{
    public static List<BrushPreset> Create() =>
    [
        new()
        {
            Id = "builtin-pencil",
            Name = "Pencil",
            Settings = new BrushSettings
            {
                Size = 3, Hardness = 0.9, Opacity = 1, Flow = 0.85, Spacing = 0.12,
                Granulation = 0.15, PressureFlowGamma = 1,
            },
        },
        new()
        {
            Id = "builtin-ink",
            Name = "Ink",
            Settings = new BrushSettings
            {
                Size = 5, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.1, PressureSizeGamma = 1.4,
            },
        },
        new()
        {
            Id = "builtin-soft-round",
            Name = "Soft round",
            Settings = new BrushSettings { Size = 12, Hardness = 0.35, Opacity = 1, Flow = 1, Spacing = 0.15 },
        },
        new()
        {
            Id = "builtin-airbrush",
            Name = "Airbrush",
            Settings = new BrushSettings
            {
                Size = 30, Hardness = 0.05, Opacity = 1, Flow = 0.25, Spacing = 0.08, PressureFlowGamma = 1,
            },
        },
        new()
        {
            Id = "builtin-watercolor",
            Name = "Watercolor",
            Settings = new BrushSettings
            {
                Size = 22, Hardness = 0.4, Opacity = 0.65, Flow = 0.5, Spacing = 0.1,
                WetEdge = 0.7, Granulation = 0.35, PressureFlowGamma = 0.8,
            },
        },
        new()
        {
            Id = "builtin-gouache",
            Name = "Gouache",
            Settings = new BrushSettings
            {
                Size = 18, Hardness = 0.75, Opacity = 0.95, Flow = 0.9, Spacing = 0.12,
                WetEdge = 0.15, Granulation = 0.25,
            },
        },
        new()
        {
            Id = "builtin-smudge",
            Name = "Smudge",
            Settings = new BrushSettings { Size = 20, Hardness = 0.5, Flow = 0.6, Spacing = 0.1, Kind = BrushKind.Smudge },
        },
        new()
        {
            Id = "builtin-blur",
            Name = "Blur",
            Settings = new BrushSettings { Size = 24, Flow = 0.7, Spacing = 0.12, Kind = BrushKind.Blur },
        },
    ];
}

/// <summary>
/// Persists user presets and the last-configured brush/eraser to
/// brushes.json next to the app settings — pressing B always returns to the
/// brush exactly as last tweaked, across sessions.
/// </summary>
public static class PresetStore
{
    public static string StorePath =>
        Path.Combine(Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "brushes.json");

    public sealed class State
    {
        public List<BrushPreset> UserPresets { get; set; } = [];
        public string? LastBrushPresetId { get; set; }
        public BrushSettings? LastBrush { get; set; }
        public BrushSettings? LastEraser { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
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
            return new State(); // a corrupt store must never block painting
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
            // best effort — losing brush persistence must never crash the app
        }
    }
}
