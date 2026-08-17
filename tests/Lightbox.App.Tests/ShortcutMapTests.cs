using Avalonia.Input;
using Lightbox.App.Services;

namespace Lightbox.App.Tests;

public class ShortcutMapTests
{
    private static string TempStore() =>
        Path.Combine(Path.GetTempPath(), $"lightbox-shortcuts-{Guid.NewGuid():N}.json");

    [Fact]
    public void Defaults_CoverTheCoreCommands_WithoutDuplicates()
    {
        var map = new ShortcutMap();
        Assert.Contains(map.Definitions, d => d.Id == "tool.brush" && d.Category == "Tools");
        Assert.Contains(map.Definitions, d => d.Id == "timeline.playPause" && d.Category == "Timeline");
        Assert.Contains(map.Definitions, d => d.Id == "docker.deleteLayer" && d.Category == "Dockers");

        // no two defaults may collide within overlapping contexts (the same
        // gesture in disjoint areas — canvas I vs timeline I — is fine)
        foreach (var def in map.Definitions)
        {
            if (def.Current is null) continue;
            Assert.Null(map.ConflictWith(def.Id, def.Current));
        }
    }

    /// <summary>
    /// Redo answers to both keys an artist may arrive with: Ctrl+Y and
    /// Ctrl+Shift+Z. The second is its own definition rather than a special
    /// case in the key handler, so the Configure window can see and rebind it —
    /// which is the whole reason the registry exists.
    /// </summary>
    [Fact]
    public void Redo_HasBothOfItsKeys()
    {
        var map = new ShortcutMap();

        var main = map.Definitions.First(d => d.Id == "canvas.redo");
        var alt = map.Definitions.First(d => d.Id == "canvas.redoAlt");

        Assert.Equal(new KeyGesture(Key.Y, KeyModifiers.Control), main.Current);
        Assert.Equal(new KeyGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift), alt.Current);

        // And the shifted undo is not read as a conflict with undo itself.
        Assert.Null(map.ConflictWith("canvas.redoAlt", alt.Current!));
    }

    [Fact]
    public void ConflictDetection_FindsTheOtherCommand()
    {
        var map = new ShortcutMap();
        var conflict = map.ConflictWith("tool.brush", new KeyGesture(Key.E));
        Assert.NotNull(conflict);
        Assert.Equal("tool.eraser", conflict!.Id);

        Assert.Null(map.ConflictWith("tool.brush", new KeyGesture(Key.B))); // own binding is not a conflict
        Assert.Null(map.ConflictWith("tool.brush", new KeyGesture(Key.Q))); // unused key
    }

    [Fact]
    public void AssignWithUnbind_StealsTheGesture()
    {
        var map = new ShortcutMap { StorePathOverride = TempStore() };
        map.Assign("tool.brush", new KeyGesture(Key.E), unbindConflicts: true);

        Assert.Equal(Key.E, map.Find("tool.brush")!.Current!.Key);
        Assert.Null(map.Find("tool.eraser")!.Current); // lost its shortcut, as warned
    }

    [Fact]
    public void Overrides_PersistAndReload()
    {
        var store = TempStore();
        var map = new ShortcutMap { StorePathOverride = store };
        map.Assign("tool.fill", new KeyGesture(Key.G, KeyModifiers.Control));
        map.Assign("canvas.mirror", null); // explicitly unbound

        var reloaded = new ShortcutMap { StorePathOverride = store };
        reloaded.Load();
        Assert.Equal("Ctrl+G", reloaded.Find("tool.fill")!.Current!.ToString());
        Assert.Null(reloaded.Find("canvas.mirror")!.Current);
        Assert.Equal(Key.B, reloaded.Find("tool.brush")!.Current!.Key); // untouched default

        reloaded.ResetToDefaults();
        Assert.Equal(Key.F, reloaded.Find("tool.fill")!.Current!.Key);
    }

    [Fact]
    public void CorruptStore_FallsBackToDefaults()
    {
        var store = TempStore();
        File.WriteAllText(store, "{ not json ");
        var map = new ShortcutMap { StorePathOverride = store };
        map.Load();
        Assert.Equal(Key.B, map.Find("tool.brush")!.Current!.Key);
    }
}
