using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>
    /// What the artist filed this brush under. Absent until they tag one.
    /// </summary>
    /// <remarks>
    /// Free text rather than a fixed vocabulary. The categories an artist wants
    /// are the ones their work has — "inking", "roughs", "that client", "hair"
    /// — and no list written here would contain them. The picker collects
    /// whatever tags exist and offers those, so the vocabulary grows out of use.
    /// </remarks>
    public List<string>? Tags { get; set; }

    /// <summary>Tags, lower-cased and trimmed, for matching. Never null.</summary>
    [JsonIgnore]
    public IEnumerable<string> NormalisedTags =>
        Tags?.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0) ?? [];

    /// <summary>
    /// A preset that ships with the app. Its id is fixed, which is what lets a
    /// user preset shadow it — see <see cref="BuiltInPresets.Merge"/>.
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn => Id.StartsWith("builtin-", StringComparison.Ordinal);

    /// <summary>
    /// What this preset costs to draw with. Derived from its settings, never
    /// stored — see <see cref="BrushCostOf"/> for why a written-down answer
    /// would go wrong the first time somebody edited the brush.
    /// </summary>
    [JsonIgnore]
    public BrushCost Cost => BrushCostOf.Settings(Settings);

    /// <summary>Whether the picker should badge this one.</summary>
    [JsonIgnore]
    public bool IsExpressive => Cost == BrushCost.Expressive;

    /// <summary>The badge glyph, or empty for an ordinary brush.</summary>
    /// <remarks>
    /// A glyph rather than a colour on its own: a dot that only differs by
    /// hue is invisible to a good share of people, and the picker is how you
    /// choose a brush. The textured tier (B177) is the same glyph hollowed:
    /// related cost, lighter weight, still legible without colour.
    /// </remarks>
    [JsonIgnore]
    public string CostBadge => Cost switch
    {
        BrushCost.Expressive => "◈",
        BrushCost.Textured => "◇",
        _ => "",
    };

    /// <summary>Why it is badged, phrased for a tooltip.</summary>
    [JsonIgnore]
    public string CostTip => Cost switch
    {
        BrushCost.Expressive =>
            $"Expressive — {BrushCostOf.Why(Settings)}. Slower on a large canvas; worth it when the mark matters.",
        BrushCost.Textured =>
            $"Textured — {BrushCostOf.Why(Settings)}. Drawing stays light; the pen-lift pays for the finish.",
        _ => "Stamps dabs and stops. Predictable cost at any canvas size.",
    };

    public override string ToString() => Name;
}

/// <summary>
/// Comparing a working brush against the preset it came from.
/// </summary>
/// <remarks>
/// <b>By serializing, not by a hand-written comparer.</b> A comparer over forty
/// properties is a list somebody has to remember to extend, and the failure
/// mode when they forget is silent: the indicator says "unchanged" for a brush
/// that is not. That is the same bug <see cref="BrushSettings.Clone"/> can
/// have, and the same reason to avoid the shape. The JSON is already the
/// canonical form of a brush, it already covers every field, and a new setting
/// joins it for free.
/// </remarks>
public static class BrushComparison
{
    private static readonly JsonSerializerOptions Canonical = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Do these two brushes paint the same mark?
    /// </summary>
    /// <remarks>
    /// Anti-aliasing and the smudge sample source are excluded, and they have
    /// to be: both live in Configure rather than in a brush, and the preset
    /// apply path deliberately carries them across unchanged. Comparing them
    /// would light the "modified" dot the moment somebody turned anti-aliasing
    /// off, on a brush they had not touched.
    /// </remarks>
    public static bool SameMark(BrushSettings a, BrushSettings b) =>
        Canonicalise(a) == Canonicalise(b);

    /// <summary>
    /// A short string that changes whenever the mark would.
    /// </summary>
    /// <remarks>
    /// For caching a rendered preview of the brush. Deriving it from the same canonical
    /// form <see cref="SameMark"/> uses is the point: a key that missed a field would show
    /// the artist a picture of a brush they had already changed, and a hand-written key is
    /// exactly the list somebody forgets to extend.
    /// </remarks>
    public static string Fingerprint(BrushSettings settings) =>
        Canonicalise(settings).GetHashCode().ToString("x8");

    private static string Canonicalise(BrushSettings settings)
    {
        var copy = settings.Clone();
        copy.AntiAlias = true;
        copy.SampleSource = SampleSource.ThisLayer;
        return JsonSerializer.Serialize(copy, Canonical);
    }
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

        new()
        {
            Id = "builtin-oil-flat",
            Name = "Oil (flat)",
            // Oil's character without oil's price: a broad, opaque, slightly
            // broken mark from spacing and jitter alone. Every simulated
            // medium wants one of these, because a medium with no affordable
            // version leaves an artist who cannot run it with nothing — which
            // is how an expressive feature turns into a trap. Watercolour,
            // gouache and ink all had a counterpart; oil, the most expensive
            // of the four, did not.
            Settings = new BrushSettings
            {
                Size = 34, Hardness = 0.6, Opacity = 1, Flow = 0.92, Spacing = 0.06,
                // Standing in for bristle drag and paint running out: a little
                // size and flow variation along the stroke, both seeded from
                // position so the mark is the same on every re-render.
                SizeJitter = 0.12, FlowJitter = 0.18, MinimumDiameter = 0.75,
                TextureSurface = PaperKind.Canvas,
                AngleFollowsDirection = true, Roundness = 0.85,
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
                // B36 — a wash edge is not directional, so this gets an
                // irregular tip and a little jitter rather than a heading.
                // Both are seeded from dab position via Hash01, so the mark is
                // varied and still reproducible (invariant 2).
                TipId = "tip-builtin-wet-edge",
                SizeJitter = 0.15, RoundnessJitter = 0.2,
                Medium = new MediumSettings
                {
                    Kind = MediumKind.Watercolour,
                    // Very wet and very mobile: pigment travels, pools at the
                    // boundary, and settles into the tooth as it dries.
                    Wetness = 0.85, Viscosity = 0.1, Drag = 0.25, FlowSteps = 16,
                    // B35 — EdgePull 0.7 left the centre at 3/255 alpha: a
                    // white line down the middle of every stroke. The rim is a
                    // wash that pooled and dried at its boundary, not something
                    // every mark does. Measured centre/flank alpha: 0.05 at
                    // 0.70, 0.42 at 0.20, 1.74 with no pull at all.
                    Absorbency = 0.35, EdgePull = 0.06,
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
                // B36 — a loaded chisel brush, turned to the stroke.
                TipId = "tip-builtin-paintbrush",
                AngleFollowsDirection = true, SizeJitter = 0.1,
                Medium = new MediumSettings
                {
                    Kind = MediumKind.Gouache,
                    // Barely flows and hides what is underneath — the opposite
                    // of watercolour in every term that matters.
                    Wetness = 0.3, Viscosity = 0.75, Drag = 0.7, FlowSteps = 6,
                    Absorbency = 0.8, EdgePull = 0.05,
                    PigmentDensity = 0.9, Granularity = 0.15, Hiding = 0.9,
                    Paper = PaperKind.ColdPress, PaperScale = 10, PaperInfluence = 0.35,
                    Body = 0.35, Relief = 0.2, PaintLoad = 0.85,
                    // Body colour: pressure decides how much it picks up.
                    PressureWater = 0.15, PressureMix = 0.8, Rewetting = 0.35,
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
                // B36 — DESIGN-fluid-media.md names this pairing as the
                // cheapest directional-drag answer in the engine, and B23 says
                // it is what an artist wanting dragged bristles actually needs.
                TipId = "tip-builtin-bristle",
                AngleFollowsDirection = true, SizeJitter = 0.08, RotationJitter = 0.04,
                Medium = new MediumSettings
                {
                    Kind = MediumKind.Oil,
                    // Thick, slow, and it drags what is already there. Running
                    // out of paint mid-stroke is the point, not a defect.
                    Wetness = 0.2, Viscosity = 0.9, Drag = 0.85, FlowSteps = 4,
                    Absorbency = 0.9, EdgePull = 0.02,
                    PigmentDensity = 1, Granularity = 0.1, Hiding = 0.95,
                    Paper = PaperKind.Canvas, PaperScale = 8, PaperInfluence = 0.6,
                    Body = 0.8, Relief = 0.6, PaintLoad = 0.6,
                    // No BristleDrag or Pickup: the engine reads neither, so setting
                    // them wrote two keys on every oil stroke promising behaviour that
                    // does not exist. The dragged-bristle *look* comes from the tip
                    // above instead. B23.
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
                    // B275 — 0.3 drew a pale line down the spine of every
                    // stroke, which is the one thing an ink wash never does.
                    // The budget cap in FluidLattice took the dip from 71/255
                    // to 20; the rest is that ink runs 18 flow steps, so what
                    // survives the cap still accumulates. Measured centreline
                    // dip against the flank at this stroke width: 20 levels at
                    // 0.3, 8 at 0.15, **4 at 0.1** — below what the eye finds
                    // on a wash. Nothing is lost by turning it down, because
                    // the rim it was buying does not form on a stroke or on a
                    // wash at all (B274, re-measured).
                    Absorbency = 0.75, EdgePull = 0.1,
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
            // Flow 0.08, not 0.6. An effect brush's flow is how hard each
            // dab pulls, and dabs overlap ~10 deep at this spacing — so a
            // flow an artist can steer is an order of magnitude below what
            // reads as "a bit of smudge" on a single dab. See the note in
            // CLAUDE.md on measuring below saturation.
            Settings = new BrushSettings { Size = 20, Hardness = 0.5, Flow = 0.08, Spacing = 0.1, Kind = BrushKind.Smudge },
        },
        new()
        {
            Id = "builtin-blender",
            Name = "Blender",
            Settings = new BrushSettings
            {
                Size = 28, Hardness = 0.3, Flow = 0.06, Spacing = 0.06,
                Kind = BrushKind.Smudge,
                // Dulling with no colour of its own: it dissolves detail
                // rather than smearing it, which is what makes a blender read
                // as a finger or a stump instead of a loaded brush.
                SmudgeMode = SmudgeMode.Dulling,
                SmudgeLength = 0.35, SmudgeRadius = 0.7, ColorRate = 0,
            },
        },
        new()
        {
            Id = "builtin-blur",
            Name = "Blur",
            // Flow is the blur sigma: sigma = Flow * Size / 4, so 0.35 on a 24 px
            // brush is a 2 px softening. It was 0.1 — a 0.6 px sigma, which is
            // no visible blur at all — on the reasoning that the blur is
            // "applied at every dab" and would stack. It does not: StampBlur
            // snapshots the layer once before the stroke and draws that same
            // snapshot per dab with SKBlendMode.Src (the B37 fix), so every dab
            // replaces its area with a single pass and the sigma is exactly what
            // one dab gives. See B49.
            Settings = new BrushSettings { Size = 24, Flow = 0.35, Spacing = 0.12, Kind = BrushKind.Blur },
        },
    ];

    /// <summary>
    /// The list an artist actually sees: the built-ins, plus the artist's own,
    /// with a user preset that carries a built-in's id <em>replacing</em> it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shadowing rule is what makes "overwrite" work on a shipped brush.
    /// Tweaking Pencil and keeping the change is the most ordinary thing
    /// anybody does with a preset list, and the alternatives are both bad: an
    /// app that refuses leaves the artist maintaining "Pencil 2", and one that
    /// edits the built-in in place has no way back to what shipped.
    /// </para>
    /// <para>
    /// A shadow is an ordinary user preset that happens to reuse the id, so it
    /// persists, exports and deletes like any other — and deleting it uncovers
    /// the original, which is exactly what "revert" should mean.
    /// </para>
    /// </remarks>
    public static List<BrushPreset> Merge(IEnumerable<BrushPreset> userPresets)
    {
        var user = userPresets.ToList();
        var shadowed = user.Where(p => p.IsBuiltIn).ToDictionary(p => p.Id, StringComparer.Ordinal);

        var merged = Create()
            .Select(builtIn => shadowed.GetValueOrDefault(builtIn.Id, builtIn))
            .ToList();
        merged.AddRange(user.Where(p => !p.IsBuiltIn));
        return merged;
    }
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

    /// <summary>
    /// How many times the store has been written this process.
    /// </summary>
    /// <remarks>
    /// A test seam, and a narrow one on purpose. The thing worth guarding is that a bulk
    /// operation saves <em>once</em>: removing an imported collection of fifty-six used to
    /// rewrite the whole file per brush, each write larger than the last, and nothing about
    /// the result distinguished that from doing it properly. Counting writes is the only
    /// honest way to assert it — a timing test would be measuring the disk, and a file watcher
    /// coalesces events and answers too late.
    /// </remarks>
    internal static int Writes { get; private set; }

    public static void Save(State state, string? path = null)
    {
        Writes++;
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
