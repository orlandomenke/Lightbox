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
        // The two texture-only brushes: no simulation, just the rim and grain
        // effects. Kept because they are cheap and predictable, and because
        // every existing document that references them must keep rendering
        // exactly as it does now.
        new()
        {
            Id = "builtin-watercolor",
            Name = "Watercolor (flat)",
            Settings = new BrushSettings
            {
                Size = 22, Hardness = 0.4, Opacity = 0.65, Flow = 0.5, Spacing = 0.1,
                WetEdge = 0.7, Granulation = 0.35, PressureFlowGamma = 0.8,
            },
        },
        new()
        {
            Id = "builtin-gouache",
            Name = "Gouache (flat)",
            Settings = new BrushSettings
            {
                Size = 18, Hardness = 0.75, Opacity = 0.95, Flow = 0.9, Spacing = 0.12,
                WetEdge = 0.15, Granulation = 0.25,
            },
        },

        // ---- simulated media ------------------------------------------------
        //
        // These run the fluid lattice over the stroke region at commit. The
        // numbers are the physical character of each medium: what separates
        // watercolour from ink is not the colour but the wetness, the
        // viscosity and how readily the paper takes the pigment.

        new()
        {
            Id = "builtin-watercolor-wet",
            Name = "Watercolor",
            Settings = new BrushSettings
            {
                Size = 42, Hardness = 0.25, Opacity = 0.55, Flow = 0.45, Spacing = 0.08,
                PressureFlowGamma = 0.8,
                Medium = new MediumSettings
                {
                    Kind = MediumKind.Watercolour,
                    // Very wet and very mobile: pigment travels, pools at the
                    // boundary, and settles into the tooth as it dries.
                    Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 16,
                    Absorbency = 0.35, EdgePull = 0.7,
                    PigmentDensity = 0.5, Granularity = 0.6, Hiding = 0.05,
                    Paper = PaperKind.ColdPress, PaperScale = 14, PaperInfluence = 0.7,
                    // A light touch is mostly water: paler, and it blooms.
                    PressureWater = 0.8, Rewetting = 0.6,
                },
            },
        },
        new()
        {
            Id = "builtin-gouache-body",
            Name = "Gouache",
            Settings = new BrushSettings
            {
                Size = 26, Hardness = 0.7, Opacity = 0.95, Flow = 0.85, Spacing = 0.1,
                Medium = new MediumSettings
                {
                    Kind = MediumKind.Gouache,
                    // Barely flows and hides what is underneath — the opposite
                    // of watercolour in every term that matters.
                    Wetness = 0.3, Viscosity = 0.75, Drag = 0.7, FlowSteps = 6,
                    Absorbency = 0.8, EdgePull = 0.15,
                    PigmentDensity = 0.9, Granularity = 0.15, Hiding = 0.9,
                    Paper = PaperKind.ColdPress, PaperScale = 10, PaperInfluence = 0.35,
                    Body = 0.35, Relief = 0.2, PaintLoad = 0.85,
                    // Body colour: pressure decides how much it picks up.
                    PressureWater = 0.15, PressureMix = 0.8, Pickup = 0.25, Rewetting = 0.35,
                },
            },
        },
        new()
        {
            Id = "builtin-oil",
            Name = "Oil",
            Settings = new BrushSettings
            {
                Size = 34, Hardness = 0.6, Opacity = 1, Flow = 0.9, Spacing = 0.06,
                Medium = new MediumSettings
                {
                    Kind = MediumKind.Oil,
                    // Thick, slow, and it drags what is already there. Running
                    // out of paint mid-stroke is the point, not a defect.
                    Wetness = 0.2, Viscosity = 0.9, Drag = 0.85, FlowSteps = 4,
                    Absorbency = 0.9, EdgePull = 0.05,
                    PigmentDensity = 1, Granularity = 0.1, Hiding = 0.95,
                    Paper = PaperKind.Canvas, PaperScale = 8, PaperInfluence = 0.6,
                    Body = 0.8, Relief = 0.6, BristleDrag = 0.5,
                    PaintLoad = 0.6, Pickup = 0.4,
                    // Barely engages the canvas under a light touch, drags it
                    // thoroughly under a firm one.
                    PressureWater = 0.05, PressureMix = 0.9, Rewetting = 0.55,
                },
            },
        },
        new()
        {
            Id = "builtin-ink-wash",
            Name = "Ink wash",
            Settings = new BrushSettings
            {
                Size = 30, Hardness = 0.35, Opacity = 0.8, Flow = 0.6, Spacing = 0.07,
                PressureSizeGamma = 1.4,
                Medium = new MediumSettings
                {
                    Kind = MediumKind.Ink,
                    // Extremely fluid and strongly absorbed: it bleeds along
                    // the fibres rather than pooling, and hardly granulates.
                    Wetness = 0.9, Viscosity = 0.05, Drag = 0.15, FlowSteps = 18,
                    Absorbency = 0.75, EdgePull = 0.3,
                    PigmentDensity = 0.8, Granularity = 0.05, Hiding = 0.4,
                    Paper = PaperKind.Smooth, PaperScale = 6, PaperInfluence = 0.25,
                    PressureWater = 0.5, Rewetting = 0.4,
                },
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

        // Stabilizer (input smoothing) — an app preference, not per-document.
        public string? SmoothingMode { get; set; }
        public int? SmoothingWindow { get; set; }
        public double? SmoothingStrength { get; set; }
        public double? LazyRadius { get; set; }
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
