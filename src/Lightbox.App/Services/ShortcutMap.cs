using System.Text.Json;
using Avalonia.Input;

namespace Lightbox.App.Services;

/// <summary>Where a shortcut is active: everywhere, or only while the pointer is over that area.</summary>
public enum ShortcutContext
{
    Global,
    Canvas,
    Timeline,
    LayersDocker,
}

/// <summary>One rebindable command: what it's called, where it lives, what triggers it.</summary>
public sealed class ShortcutDefinition(string id, string name, string category, KeyGesture? @default, ShortcutContext context = ShortcutContext.Global)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>Grouping in the editor: Tools, Canvas, Timeline, Dockers.</summary>
    public string Category { get; } = category;

    /// <summary>Where this binding fires — the same key can mean different things per area.</summary>
    public ShortcutContext Context { get; } = context;

    public KeyGesture? Default { get; } = @default;

    /// <summary>The active binding (null = unbound).</summary>
    public KeyGesture? Current { get; internal set; } = @default;

    public string GestureText => Current?.ToString() ?? "—";
}

/// <summary>
/// The application's rebindable shortcuts: defaults, user overrides
/// (persisted to shortcuts.json), reverse lookup for dispatch, and conflict
/// detection for the editor.
/// </summary>
public sealed class ShortcutMap
{
    public static string StorePath =>
        Path.Combine(Path.GetDirectoryName(Lightbox.Ai.ApiKeyProvider.SettingsPath)!, "shortcuts.json");

    /// <summary>Test seam.</summary>
    public string? StorePathOverride { get; init; }

    private readonly List<ShortcutDefinition> _definitions;

    public IReadOnlyList<ShortcutDefinition> Definitions => _definitions;

    public ShortcutMap()
    {
        static KeyGesture G(Key key, KeyModifiers modifiers = KeyModifiers.None) => new(key, modifiers);
        _definitions =
        [
            new("tool.brush", "Brush", "Tools", G(Key.B)),
            new("tool.eraser", "Eraser", "Tools", G(Key.E)),
            new("tool.fill", "Fill", "Tools", G(Key.F)),
            new("tool.gradient", "Gradient", "Tools", G(Key.G)),
            new("tool.select", "Select / next variant", "Tools", G(Key.S)),
            new("select.all", "Select all", "Tools", G(Key.A, KeyModifiers.Control)),
            new("select.none", "Deselect", "Tools", G(Key.D, KeyModifiers.Control)),
            new("select.invert", "Invert selection", "Tools", G(Key.I, KeyModifiers.Control | KeyModifiers.Shift)),
            new("select.cancel", "Cancel polygon", "Tools", G(Key.Escape)),

            new("canvas.undo", "Undo", "Canvas", G(Key.Z, KeyModifiers.Control)),
            new("canvas.redo", "Redo", "Canvas", G(Key.Y, KeyModifiers.Control)),
            new("canvas.transform", "Transform (move/scale/rotate/perspective)", "Tools", G(Key.T, KeyModifiers.Control)),
            new("canvas.mirror", "Mirror view", "Canvas", G(Key.M)),
            new("canvas.resetView", "Reset view", "Canvas", G(Key.D0)),

            new("timeline.playPause", "Play / pause", "Timeline", G(Key.Space)),
            new("timeline.prevFrame", "Previous frame (scrub)", "Timeline", G(Key.Left), ShortcutContext.Timeline),
            new("timeline.nextFrame", "Next frame (scrub)", "Timeline", G(Key.Right), ShortcutContext.Timeline),
            new("timeline.prevKey", "Flip to previous key", "Timeline", G(Key.D1)),
            new("timeline.nextKey", "Flip to next key", "Timeline", G(Key.D2)),
            new("timeline.copyCel", "Copy cel", "Timeline", G(Key.C, KeyModifiers.Control)),
            new("timeline.cutCel", "Cut cel", "Timeline", G(Key.X, KeyModifiers.Control)),
            new("timeline.pasteCel", "Paste cel", "Timeline", G(Key.V, KeyModifiers.Control)),

            new("docker.deleteLayer", "Delete layer", "Dockers", G(Key.Delete), ShortcutContext.LayersDocker),
            new("docker.clearLayer", "Blank layer content", "Dockers", G(Key.Back), ShortcutContext.LayersDocker),

            // Context twins: the same key does area-appropriate things.
            new("canvas.pickColor", "Color picker tool (canvas)", "Tools", G(Key.I), ShortcutContext.Canvas),
            new("timeline.insertKey", "Insert keyframe at playhead (timeline)", "Timeline", G(Key.I), ShortcutContext.Timeline),
            new("canvas.nudgeLeft", "Nudge selection left", "Canvas", G(Key.Left), ShortcutContext.Canvas),
            new("canvas.nudgeRight", "Nudge selection right", "Canvas", G(Key.Right), ShortcutContext.Canvas),
            new("canvas.nudgeUp", "Nudge selection up", "Canvas", G(Key.Up), ShortcutContext.Canvas),
            new("canvas.nudgeDown", "Nudge selection down", "Canvas", G(Key.Down), ShortcutContext.Canvas),
            new("docker.layerAbove", "Select the layer above", "Dockers", G(Key.Up), ShortcutContext.LayersDocker),
            new("docker.layerBelow", "Select the layer below", "Dockers", G(Key.Down), ShortcutContext.LayersDocker),
        ];
    }

    /// <summary>
    /// The command a key event triggers in the given context, or null. A
    /// context-specific binding beats a global one for the same keys.
    /// </summary>
    public string? IdFor(KeyEventArgs e, ShortcutContext context = ShortcutContext.Global)
    {
        ShortcutDefinition? global = null;
        foreach (var d in _definitions)
        {
            if (d.Current is not { } g || g.Key != e.Key || g.KeyModifiers != e.KeyModifiers) continue;
            if (d.Context == context) return d.Id;
            if (d.Context == ShortcutContext.Global) global ??= d;
        }
        return global?.Id;
    }

    public ShortcutDefinition? Find(string id) => _definitions.FirstOrDefault(d => d.Id == id);

    /// <summary>
    /// The OTHER command already bound to this gesture whose context overlaps
    /// (same area, or either is global) — bindings in disjoint areas coexist.
    /// </summary>
    public ShortcutDefinition? ConflictWith(string id, KeyGesture gesture)
    {
        if (Find(id) is not { } self) return null;
        return _definitions.FirstOrDefault(d =>
            d.Id != id
            && d.Current is { } g && g.Key == gesture.Key && g.KeyModifiers == gesture.KeyModifiers
            && (d.Context == self.Context
                || d.Context == ShortcutContext.Global
                || self.Context == ShortcutContext.Global));
    }

    /// <summary>Bind a gesture (stealing it from a conflicting command must be the CALLER's explicit choice).</summary>
    public void Assign(string id, KeyGesture? gesture, bool unbindConflicts = false)
    {
        if (Find(id) is not { } def) return;
        if (gesture is not null && unbindConflicts && ConflictWith(id, gesture) is { } other)
        {
            other.Current = null;
        }
        def.Current = gesture;
        Save();
    }

    public void ResetToDefaults()
    {
        foreach (var def in _definitions) def.Current = def.Default;
        Save();
    }

    public void Load()
    {
        try
        {
            var path = StorePathOverride ?? StorePath;
            if (!File.Exists(path)) return;
            var overrides = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (overrides is null) return;
            foreach (var (id, text) in overrides)
            {
                if (Find(id) is not { } def) continue;
                def.Current = string.IsNullOrWhiteSpace(text) ? null : TryParse(text);
            }
        }
        catch
        {
            // a corrupt store must never block input — defaults stay active
        }
    }

    private void Save()
    {
        try
        {
            var path = StorePathOverride ?? StorePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var overrides = _definitions
                .Where(d => !Equals(d.Current?.ToString(), d.Default?.ToString()))
                .ToDictionary(d => d.Id, d => d.Current?.ToString() ?? "");
            File.WriteAllText(path, JsonSerializer.Serialize(overrides, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // best-effort persistence
        }
    }

    private static KeyGesture? TryParse(string text)
    {
        try
        {
            return KeyGesture.Parse(text);
        }
        catch
        {
            return null;
        }
    }
}
