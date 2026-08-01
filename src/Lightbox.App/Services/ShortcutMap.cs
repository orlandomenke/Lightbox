using System.Text.Json;
using Avalonia.Input;

namespace Lightbox.App.Services;

/// <summary>One rebindable command: what it's called, where it lives, what triggers it.</summary>
public sealed class ShortcutDefinition(string id, string name, string category, KeyGesture? @default)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>Grouping in the editor: Tools, Canvas, Timeline, Dockers.</summary>
    public string Category { get; } = category;

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
            new("tool.select", "Select / next variant", "Tools", G(Key.S)),
            new("select.all", "Select all", "Tools", G(Key.A, KeyModifiers.Control)),
            new("select.none", "Deselect", "Tools", G(Key.D, KeyModifiers.Control)),
            new("select.invert", "Invert selection", "Tools", G(Key.I, KeyModifiers.Control | KeyModifiers.Shift)),
            new("select.cancel", "Cancel polygon", "Tools", G(Key.Escape)),

            new("canvas.undo", "Undo", "Canvas", G(Key.Z, KeyModifiers.Control)),
            new("canvas.redo", "Redo", "Canvas", G(Key.Y, KeyModifiers.Control)),
            new("canvas.mirror", "Mirror view", "Canvas", G(Key.M)),
            new("canvas.resetView", "Reset view", "Canvas", G(Key.D0)),

            new("timeline.playPause", "Play / pause", "Timeline", G(Key.Space)),
            new("timeline.prevFrame", "Previous frame", "Timeline", G(Key.Left)),
            new("timeline.nextFrame", "Next frame", "Timeline", G(Key.Right)),
            new("timeline.prevKey", "Flip to previous key", "Timeline", G(Key.D1)),
            new("timeline.nextKey", "Flip to next key", "Timeline", G(Key.D2)),
            new("timeline.copyCel", "Copy cel", "Timeline", G(Key.C, KeyModifiers.Control)),
            new("timeline.cutCel", "Cut cel", "Timeline", G(Key.X, KeyModifiers.Control)),
            new("timeline.pasteCel", "Paste cel", "Timeline", G(Key.V, KeyModifiers.Control)),

            new("docker.deleteLayer", "Delete layer (pointer in Layers docker)", "Dockers", G(Key.Delete)),
            new("docker.clearLayer", "Blank layer content (pointer in Layers docker)", "Dockers", G(Key.Back)),
        ];
    }

    /// <summary>The command a key event triggers, or null.</summary>
    public string? IdFor(KeyEventArgs e) =>
        _definitions.FirstOrDefault(d => d.Current is { } g && g.Key == e.Key && g.KeyModifiers == e.KeyModifiers)?.Id;

    public ShortcutDefinition? Find(string id) => _definitions.FirstOrDefault(d => d.Id == id);

    /// <summary>The OTHER command already bound to this gesture, if any.</summary>
    public ShortcutDefinition? ConflictWith(string id, KeyGesture gesture) =>
        _definitions.FirstOrDefault(d =>
            d.Id != id && d.Current is { } g && g.Key == gesture.Key && g.KeyModifiers == gesture.KeyModifiers);

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
