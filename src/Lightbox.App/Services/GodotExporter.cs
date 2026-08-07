using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;

namespace Lightbox.App.Services;

/// <param name="WriteImporter">Write the Godot-side importer script beside the sheet.</param>
public sealed record GodotExportOptions(bool WriteImporter = true)
{
    public SpriteSheetOptions Sheet { get; init; } = new();
}

public sealed record GodotExportResult(
    string SheetPath,
    string MetadataPath,
    string? ImporterPath,
    int SpriteCount,
    int ClipCount,
    SpriteSheetResult? Sheet = null);

/// <summary>
/// Export for Godot: the atlas, the sidecar, and a Godot-side importer in GDScript.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lightbox does not write <c>.tres</c> files, and that is a correction to an
/// earlier plan in this repository.</b> The roadmap argued Godot was "the one engine
/// whose asset format we can legitimately write, because <c>.tres</c> is plain text".
/// That reasoning does not hold: <b>a format being text is not the same as knowing
/// it.</b> The exact serialisation of a <c>SpriteFrames</c> resource — how the
/// animations array is written, how a <c>StringName</c> is quoted, what
/// <c>load_steps</c> must be, whether a <c>uid://</c> is required — could not be
/// verified here, and hand-writing a format from partial knowledge is precisely what
/// produced the Unity importer that silently sliced nothing.
/// </para>
/// <para>
/// So this follows the Unity pattern instead, and follows it further: <b>we write
/// files and data; the engine's own API builds the asset.</b> The shipped GDScript
/// calls <c>AtlasTexture</c>, <c>SpriteFrames</c> and <c>ResourceSaver</c>, so the
/// <c>.tres</c> that lands is one Godot wrote — the format is Godot's problem, and it
/// stays correct across versions for free.
/// </para>
/// <para>
/// <b>The sidecar needs almost no Godot block</b>, unlike Unity's. Everything the
/// script wants — regions, durations, fps, tags — is already in the generic file.
/// The one exception is the pivot, because Godot expresses it as a
/// <c>Sprite2D.offset</c> from the region's centre rather than as a normalised point,
/// and that conversion belongs on this side where it can be tested.
/// </para>
/// <para>
/// It never touches <c>project.godot</c> or the <c>.godot/</c> cache. Godot owns those
/// the way Unity owns <c>.meta</c>, and the rule has a test rather than a comment.
/// </para>
/// </remarks>
public static class GodotExporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The Godot-facing half of the sidecar: only what the generic file lacks.</summary>
    private sealed class GodotBlock
    {
        /// <summary>
        /// Per-sprite <c>Sprite2D.offset</c>, in the frame order of the generic sidecar.
        /// </summary>
        [JsonPropertyName("spriteOffsets")] public List<double[]>? SpriteOffsets { get; set; }

        /// <summary>
        /// Per-frame duration as Godot's relative multiplier, not milliseconds.
        /// </summary>
        /// <remarks>
        /// Converted here because the unit is the trap: Godot's frame duration scales
        /// the animation's speed, so passing milliseconds runs the animation thousands of
        /// times too slowly and reads as a hang.
        /// </remarks>
        [JsonPropertyName("frameDurations")] public List<double> FrameDurations { get; set; } = [];
    }

    public static GodotExportResult Export(Doc doc, string sheetPath, GodotExportOptions? options = null)
    {
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        var opts = options ?? new GodotExportOptions();
        var sheet = SpriteSheetExporter.Export(doc, sheetPath, opts.Sheet);

        // Read back what the exporter wrote rather than recomputing it — one source for
        // where a sprite is, the same rule the Unity export follows.
        using var written = JsonDocument.Parse(File.ReadAllText(sheet.MetadataPath));
        var root = written.RootElement;
        var frames = root.GetProperty("frames").EnumerateArray().ToList();

        var block = new GodotBlock();
        var offsets = new List<double[]>();
        var scene = doc.Scene;

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var rect = frame.GetProperty("frame");
            var source = frame.GetProperty("spriteSourceSize");
            var w = rect.GetProperty("w").GetInt32();
            var h = rect.GetProperty("h").GetInt32();

            block.FrameDurations.Add(GodotConvert.FrameDuration(
                frame.GetProperty("duration").GetInt32(), scene.Fps));

            if (scene.Pivot is { } pivot)
            {
                var (ox, oy) = GodotConvert.SpriteOffset(
                    pivot.X, pivot.Y,
                    source.GetProperty("x").GetInt32(), source.GetProperty("y").GetInt32(), w, h);
                offsets.Add([ox, oy]);
            }
        }
        if (offsets.Count > 0) block.SpriteOffsets = offsets;

        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject()) merged[property.Name] = property.Value;
        merged["godot"] = JsonSerializer.SerializeToElement(block, Json);
        File.WriteAllText(sheet.MetadataPath, JsonSerializer.Serialize(merged, Json));

        string? importerPath = null;
        if (opts.WriteImporter)
        {
            importerPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(sheetPath))!, "lightbox_import.gd");
            // Only if absent: it is source we ship and somebody may have adjusted it.
            if (!File.Exists(importerPath)) File.WriteAllText(importerPath, ImporterSource);
        }

        var clips = root.GetProperty("meta").TryGetProperty("frameTags", out var tags)
            ? tags.GetArrayLength()
            : 0;

        return new GodotExportResult(
            sheet.SheetPath, sheet.MetadataPath, importerPath, frames.Count, clips, sheet);
    }

    /// <summary>
    /// The Godot-side importer, shipped as GDScript.
    /// </summary>
    /// <remarks>
    /// An <c>EditorScript</c> rather than a plugin: a plugin needs a
    /// <c>plugin.cfg</c>, a directory layout and enabling in project settings, and all
    /// three are things we would be putting into somebody's project uninvited. An
    /// editor script is one file you open and run.
    /// </remarks>
    internal const string ImporterSource = """
        # Lightbox → Godot sheet importer.
        #
        # Open this in the script editor and press Ctrl+Shift+X (File ▸ Run). It finds
        # every Lightbox sidecar under res:// and builds a SpriteFrames resource beside
        # each sheet.
        #
        # Why a script rather than a .tres written by Lightbox: the resource is built by
        # Godot's own API here, so its serialised format is Godot's business and stays
        # correct across versions. Lightbox writes a PNG and a JSON; nothing else.
        #
        # It never touches project.godot or the .godot/ cache. Godot owns those.
        #
        # Godot 4. SpriteFrames.add_frame took no duration before 4.0, so the frame
        # timing below would be silently dropped on 3.x.
        @tool
        extends EditorScript

        func _run() -> void:
            var sheets := _find_sidecars("res://")
            if sheets.is_empty():
                print("Lightbox: no sidecars found under res://")
                return
            for path in sheets:
                _import(path)

        func _find_sidecars(root: String) -> Array[String]:
            var found: Array[String] = []
            var dir := DirAccess.open(root)
            if dir == null:
                return found
            dir.list_dir_begin()
            var name := dir.get_next()
            while name != "":
                var full := root.path_join(name)
                if dir.current_is_dir():
                    # Skip Godot's own cache and any hidden folder.
                    if not name.begins_with("."):
                        found.append_array(_find_sidecars(full))
                elif name.ends_with(".json") and _is_lightbox(full):
                    found.append(full)
                name = dir.get_next()
            dir.list_dir_end()
            return found

        func _is_lightbox(path: String) -> bool:
            # A "godot" block plus our app name, so this cannot mistake an unrelated
            # JSON in somebody's project for a sheet to import.
            var text := FileAccess.get_file_as_string(path)
            if text.is_empty():
                return false
            var data: Variant = JSON.parse_string(text)
            return (data is Dictionary
                and data.has("godot")
                and data.get("meta", {}).get("app", "") == "Lightbox")

        func _import(json_path: String) -> void:
            var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(json_path))
            var meta: Dictionary = data.get("meta", {})
            var frames: Array = data.get("frames", [])
            var block: Dictionary = data.get("godot", {})

            var image_name: String = meta.get("image", "")
            var texture_path := json_path.get_base_dir().path_join(image_name)
            var texture: Texture2D = load(texture_path)
            if texture == null:
                push_warning("Lightbox: could not load %s" % texture_path)
                return

            # One AtlasTexture per frame, in sidecar order — which is the order the
            # tags' from/to indices refer to.
            var atlases: Array[AtlasTexture] = []
            for frame in frames:
                var rect: Dictionary = frame.get("frame", {})
                var atlas := AtlasTexture.new()
                atlas.atlas = texture
                atlas.region = Rect2(rect.get("x", 0), rect.get("y", 0), rect.get("w", 0), rect.get("h", 0))
                # Filtering off the edge of a region is what makes neighbouring sprites
                # bleed into each other on an atlas.
                atlas.filter_clip = true
                atlases.append(atlas)

            var durations: Array = block.get("frameDurations", [])
            var fps: float = float(meta.get("fps", 12))

            var sprite_frames := SpriteFrames.new()
            # The default animation exists in a new SpriteFrames; drop it unless a tag
            # happens to be called "default", or you get an empty animation in the list.
            var tags: Array = meta.get("frameTags", [])
            if tags.is_empty():
                _add_animation(sprite_frames, "default", atlases, durations, 0, atlases.size() - 1, fps, true)
            else:
                sprite_frames.remove_animation("default")
                var used: Dictionary = {}
                for tag in tags:
                    var name: String = str(tag.get("name", "animation"))
                    if name.is_empty():
                        name = "animation"
                    # A duplicate tag name would silently replace the earlier animation.
                    if used.has(name):
                        name = "%s_%d" % [name, used[name]]
                    used[name] = used.get(name, 0) + 1
                    _add_animation(
                        sprite_frames, name, atlases, durations,
                        int(tag.get("from", 0)), int(tag.get("to", 0)),
                        fps, bool(tag.get("loop", true)))

            var out_path := json_path.get_basename() + "_frames.tres"
            var err := ResourceSaver.save(sprite_frames, out_path)
            if err != OK:
                push_warning("Lightbox: could not save %s (error %d)" % [out_path, err])
                return

            print("Lightbox: %d sprite(s), %d animation(s) → %s"
                % [atlases.size(), sprite_frames.get_animation_names().size(), out_path])

        func _add_animation(
            sprite_frames: SpriteFrames, name: String, atlases: Array[AtlasTexture],
            durations: Array, from_index: int, to_index: int, fps: float, loop: bool) -> void:
            if not sprite_frames.has_animation(name):
                sprite_frames.add_animation(name)
            # Speed is the animation's own rate; each frame's duration is a MULTIPLIER of
            # it, not a time. Lightbox has already converted, so this passes the number
            # through rather than computing one.
            sprite_frames.set_animation_speed(name, fps)
            sprite_frames.set_animation_loop(name, loop)
            for i in range(from_index, min(to_index + 1, atlases.size())):
                var duration: float = 1.0
                if i < durations.size():
                    duration = float(durations[i])
                sprite_frames.add_frame(name, atlases[i], duration)
        """;
}
