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
    /// The id of the brush this one paints with while Alt is held, or null for
    /// what Alt has always done: erase with this brush (Q255).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A wash names a water brush here and a blot on <see cref="SecondAlternate"/>:
    /// paint, then rework the edge, without putting the brush down. On the
    /// preset and not in <see cref="Settings"/>, because it is how the artist
    /// reaches another brush and not something a stroke is made of - a stroke
    /// drawn through an alternate records that other brush's settings, like any
    /// stroke, and nothing about how it was reached.
    /// </para>
    /// <para>
    /// An id that no longer names a brush is not an error: the key falls back to
    /// what it did before. Absent from the file unless set.
    /// </para>
    /// <para>
    /// Null on a brush that shadows a shipped one means "as shipped", so a
    /// Watercolor an artist tagged or retuned before alternates existed gets
    /// its blot without doing anything; the empty string is how such a brush
    /// says "none after all" (<see cref="BuiltInPresets.ShippedAlternate"/>).
    /// </para>
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Alternate { get; set; }

    /// <summary>
    /// The same for Ctrl+Alt. Null leaves Ctrl+Alt to the eyedropper, which is
    /// what Ctrl at pen-down is.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecondAlternate { get; set; }

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
            $"Textured — {BrushCostOf.Why(Settings)}. Finished under the pen; a little more work per stroke than a plain brush.",
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
            // Graphite caught on the paper's tooth: a light touch marks the
            // peaks, a hard press fills the valleys (TexturePressure).
            // docs/DESIGN-pencil-tooth.md has why.
            //
            // Retuned 2026-10-10 on the owner's verdict that it was "too dark,
            // too consistent" to be graphite. Three things, judged on a sheet:
            // graphite is never black (Opacity 0.8), it builds with passes and
            // answers pressure across its whole range rather than saturating
            // at a medium press (Flow 0.5, gamma 1.2), and even a firm stroke
            // leaves the deepest valleys of the sheet (TexturePressure 0.7 at
            // Depth 0.9). And the sheet is a scanned one - Plain, a little
            // finer than life - whose tooth is fibre, not noise.
            Settings = new BrushSettings
            {
                Size = 4, Hardness = 0.7, Opacity = 0.8, Flow = 0.5, Spacing = 0.1,
                PressureFlowGamma = 1.2,
                TextureSurface = PaperKind.Plain, TextureScale = 5, TextureDepth = 0.9,
                TexturePressure = 0.7,
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
            // Rework without putting the brush down (Q255): Alt rewets with
            // Water, Ctrl+Alt lifts with the Blot.
            Alternate = "builtin-water",
            SecondAlternate = "builtin-blot",
            // A wash, without the fluid simulation it used to run (Q240, Q244):
            // a crisp-edged, thin layer of paint whose pigment gathers at the
            // rim, on a sheet with visible fibre, that picks up what it is laid
            // over and runs out along the stroke. Every part of it is finished
            // under the pen - nothing changes when the pen lifts.
            //
            // Chosen from a sheet beside the simulated brush it replaces. Round
            // and unjittered on purpose: the rim is measured from the edge of
            // everything the stroke covered, so a tip that varies in size puts
            // faint bumps on that edge and the rim breaks into dashes on them.
            Settings = new BrushSettings
            {
                Size = 42, Hardness = 0.8, Opacity = 0.8, Flow = 0.35, Spacing = 0.08,
                PressureFlowGamma = 0.8,
                WetEdge = 0.7,
                TextureSurface = PaperKind.Fibre, TextureScale = 8, TextureDepth = 0.35,
                // Q232: a wash picks up what it is laid over.
                Mixing = new ColourMixing { Amount = 0.6, Length = 0.3, Reach = 0.9 },
                // Paint that runs out along the stroke (B434); no medium is
                // simulated - the load is the one thing of it this keeps.
                Medium = new MediumSettings { PaintLoad = 0.6 },
            },
        },
        new()
        {
            Id = "builtin-gouache-body",
            Name = "Gouache",
            // Opaque body colour, without the simulation it used to run (Q240,
            // Q251): it goes solid at a light press, carries a little of the
            // paper's tone inside it, and breaks into streaks as the brush
            // empties. Chosen from a sheet beside the simulated brush.
            Settings = new BrushSettings
            {
                Size = 26, Hardness = 0.7, Opacity = 0.95, Flow = 0.85, Spacing = 0.1,
                // B36 - a loaded chisel brush, turned to the stroke.
                TipId = "tip-builtin-paintbrush",
                AngleFollowsDirection = true, SizeJitter = 0.1,
                Bristles = 0.5,
                // Plain paper: filled by a press of a quarter, and thin enough
                // that a little of it settles.
                TextureSurface = PaperKind.Plain, TextureScale = 8, TextureDepth = 0.8,
                TexturePressure = 1, TextureFill = 0.25, TextureSettle = 0.15,
                // Q232: body colour covers more than it mixes, and what it picks
                // up does not travel far.
                Mixing = new ColourMixing { Amount = 0.8, Length = 0.15, Reach = 0.9 },
                Medium = new MediumSettings { PaintLoad = 0.63 },
            },
        },
        new()
        {
            Id = "builtin-oil",
            Name = "Oil",
            // Thick paint on canvas, without the simulation it used to run (Q240,
            // Q246): pressure pushes it into the weave until the mark is solid,
            // and the canvas shows only where the touch is light or the brush is
            // running dry - as streaks, because each hair lifts on its own. The
            // owner's verdict on the first try, which held the paint to the
            // weave at every pressure: "it looks like a stamp, or that it only
            // skims the paper surface instead of flowing into the crevices with
            // pressure." Chosen from sheets beside the simulated brush.
            Settings = new BrushSettings
            {
                Size = 34, Hardness = 0.6, Opacity = 1, Flow = 0.9, Spacing = 0.06,
                TipId = "tip-builtin-bristle",
                AngleFollowsDirection = true, SizeJitter = 0.08, RotationJitter = 0.04,
                // The tip's hairs: uneven enough that the thin ones lift early.
                Bristles = 0.8,
                // Linen, pressed into: a tooth that a press of a third fills.
                TextureSurface = PaperKind.Linen, TextureScale = 6, TextureDepth = 0.9,
                TexturePressure = 1, TextureFill = 0.35,
                // Q232: oil drags what is already there a long way and keeps
                // half of its own paint.
                Mixing = new ColourMixing { Amount = 0.7, Length = 0.25, Reach = 0.9 },
                // No medium is simulated. What is kept of it is the paint on
                // the brush, which runs out along the stroke (B434).
                Medium = new MediumSettings { PaintLoad = 0.55 },
            },
        },
        new()
        {
            Id = "builtin-ink-wash",
            Name = "Ink wash",
            // Thin ink on paper, without the simulation it used to run (Q240,
            // Q251): it covers the sheet and settles into it, so the paper
            // shows as tone inside the mark, and it gathers at the rim as a
            // wash does. Chosen from a sheet beside the simulated brush.
            Alternate = "builtin-water", // Q255: see Watercolor
            SecondAlternate = "builtin-blot",
            Settings = new BrushSettings
            {
                Size = 30, Hardness = 0.75, Opacity = 0.8, Flow = 0.4, Spacing = 0.07,
                PressureSizeGamma = 1.4,
                WetEdge = 0.5,
                TextureSurface = PaperKind.Plain, TextureScale = 8, TextureSettle = 0.4,
                // Q232: a wash over a wash bleeds into it.
                Mixing = new ColourMixing { Amount = 0.6, Length = 0.2, Reach = 0.9 },
                Medium = new MediumSettings { PaintLoad = 0.7 },
            },
        },
        new()
        {
            Id = "builtin-blot",
            Name = "Blot",
            // A paper towel (Q255, from how the owner works a wash): dab an
            // edge and it lifts, more the harder it is pressed, never all of
            // it. An eraser whose stroke opacity is the most one dab can take,
            // with flow on pressure and a sheet's texture in what is left.
            // Chosen from a sheet: firm edge, 80%, the textured one.
            Tool = ToolKind.Eraser,
            Settings = new BrushSettings
            {
                Size = 44, Hardness = 0.6, Opacity = 0.8, Flow = 0.6, Spacing = 0.1,
                PressureFlowGamma = 1.2,
                TextureSurface = PaperKind.Fibre, TextureScale = 6, TextureDepth = 0.6,
            },
        },
        new()
        {
            Id = "builtin-water",
            Name = "Water",
            // Clean water on dry paint (Q255): what it passes over bleeds out
            // into the wet area and thins, in uneven pools, and the pigment the
            // water pushed gathers where the water stopped. It lays no paint of
            // its own. Pressure sets how wet; size, how far paint can travel.
            // Chosen by the owner from a sheet rendered by the engine (cell 2).
            Settings = new BrushSettings
            {
                // Spacing a quarter: the wet area is the most of soft discs, and denser
                // dabs only cost time - each pen event re-renders what they cover.
                Size = 60, Hardness = 0.65, Flow = 1, Spacing = 0.25, Kind = BrushKind.Blur,
                PressureFlowGamma = 0.6,
                TextureSurface = PaperKind.Fibre, TextureScale = 8, TextureSettle = 0.4,
                Wet = new WetSettings { Spread = 0.6, Thin = 0.2, Cloud = 0.9, Gather = 0.5 },
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
            // Length 0.75 rather than the record's 0.5, and the number is measured (B92):
            // a smear copies pixels, and copying does not get the trail the old
            // colour-carrying smudge got for free from a sampler that reached back
            // into the mark. At 0.75 a size-20 smear drags about as far as it did.
            Settings = new BrushSettings
            {
                Size = 20, Hardness = 0.5, Flow = 0.08, Spacing = 0.1, Kind = BrushKind.Smudge,
                SmudgeLength = 0.75,
            },
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
    private static readonly Lazy<Dictionary<string, (string? First, string? Second)>> Alternates = new(() =>
        Create().Where(p => p.Alternate is not null || p.SecondAlternate is not null)
            .ToDictionary(p => p.Id, p => (p.Alternate, p.SecondAlternate)));

    /// <summary>The alternate a shipped brush names for a key, or null: what a shadow of it inherits.</summary>
    public static string? ShippedAlternate(string presetId, BrushAlternate alternate) =>
        !Alternates.Value.TryGetValue(presetId, out var named) ? null
        : alternate == BrushAlternate.First ? named.First
        : alternate == BrushAlternate.Second ? named.Second
        : null;

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

        /// <summary>
        /// Presets that have been nudged and not written back, by id (B71).
        /// Null — and therefore absent from the file — when nothing is.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore(
            Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, BrushSettings>? Tweaks { get; set; }

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

    /// <summary>One preset as the store writes it, for the test that a key is absent unless used.</summary>
    internal static string SerializeForTest(BrushPreset preset) => JsonSerializer.Serialize(preset, Json);

    internal static BrushPreset DeserializeForTest(string json) => JsonSerializer.Deserialize<BrushPreset>(json, Json)!;

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
