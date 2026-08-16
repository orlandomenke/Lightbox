using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;

namespace Lightbox.App.Services;

/// <param name="WriteImporter">Write the Godot-side importer script beside the sheet.</param>
/// <param name="IncludeRig">
/// Write each rigged document's rig JSON (and its importer) beside the sheet.
/// A document with no armature writes nothing whichever way this is set —
/// optional means absent.
/// </param>
public sealed record GodotExportOptions(bool WriteImporter = true, bool IncludeRig = true)
{
    public SpriteSheetOptions Sheet { get; init; } = new();
}

public sealed record GodotExportResult(
    string SheetPath,
    string MetadataPath,
    string? ImporterPath,
    int SpriteCount,
    int ClipCount,
    SpriteSheetResult? Sheet = null)
{
    /// <summary>One rig JSON per rigged document, empty for unrigged exports.</summary>
    public IReadOnlyList<string> RigPaths { get; init; } = [];

    /// <summary>The rig importer script, when any rig was written.</summary>
    public string? RigImporterPath { get; init; }
}

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
        return Export([doc], sheetPath, options);
    }

    /// <summary>
    /// Several documents into one Godot sheet — each frame's clock and offset
    /// from its own document, by <see cref="SpriteSheetResult.FrameOwners"/>.
    /// </summary>
    public static GodotExportResult Export(
        IReadOnlyList<Doc> docs, string sheetPath, GodotExportOptions? options = null,
        IReadOnlyList<string>? names = null)
    {
        if (docs is not { Count: > 0 }) throw new ArgumentException("An export needs at least one document.", nameof(docs));
        var opts = options ?? new GodotExportOptions();
        var sheet = SpriteSheetExporter.Export(docs, sheetPath, opts.Sheet, names);

        // Read back what the exporter wrote rather than recomputing it — one source for
        // where a sprite is, the same rule the Unity export follows.
        using var written = JsonDocument.Parse(File.ReadAllText(sheet.MetadataPath));
        var root = written.RootElement;
        var frames = root.GetProperty("frames").EnumerateArray().ToList();

        var block = new GodotBlock();
        var offsets = new List<double[]>();
        var owners = sheet.FrameOwners;

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var scene = owners is null ? docs[0].Scene : docs[owners[i].Document].Scene;
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

        // The rig, when there is one: our schema beside the sheet, and a
        // Godot-side importer that builds the Skeleton2D through Godot's own
        // API — the same rule the sheet importer follows, because the .tscn
        // that lands must be one Godot wrote.
        var rigPaths = new List<string>();
        string? rigImporterPath = null;
        if (opts.IncludeRig)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(sheetPath))!;
            var baseName = Path.GetFileNameWithoutExtension(sheetPath);
            for (var i = 0; i < docs.Count; i++)
            {
                if (!Lightbox.Core.Export.RigExport.HasRig(docs[i])) continue;
                var stem = docs.Count == 1
                    ? baseName
                    : Sanitized(names is not null && i < names.Count ? names[i] : $"{baseName}_{i + 1}");
                var rigPath = Path.Combine(directory, stem + "_rig.json");
                File.WriteAllText(rigPath, Lightbox.Core.Export.RigExport.ExportJson(docs[i]));
                rigPaths.Add(rigPath);
            }
            if (rigPaths.Count > 0 && opts.WriteImporter)
            {
                rigImporterPath = Path.Combine(directory, "lightbox_import_rig.gd");
                // Only if absent, the sheet importer's rule: it is source we
                // ship and somebody may have adjusted it.
                if (!File.Exists(rigImporterPath)) File.WriteAllText(rigImporterPath, RigImporterSource);
            }
        }

        return new GodotExportResult(
            sheet.SheetPath, sheet.MetadataPath, importerPath, frames.Count, clips, sheet)
        {
            RigPaths = rigPaths,
            RigImporterPath = rigImporterPath,
        };
    }

    /// <summary>A document name as a file stem: whatever the filesystem would refuse, replaced.</summary>
    private static string Sanitized(string name)
    {
        var stem = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return stem.Length == 0 ? "rig" : stem;
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

    /// <summary>
    /// The Godot-side rig importer, shipped as GDScript for the sheet
    /// importer's reason: the scene that lands is one Godot's own API built,
    /// so its serialised form is Godot's business and stays correct across
    /// versions. Lightbox writes JSON; nothing else.
    /// </summary>
    internal const string RigImporterSource = """
        # Lightbox → Godot rig importer.
        #
        # Open this in the script editor and press Ctrl+Shift+X (File ▸ Run). It finds
        # every *_rig.json under res:// and builds a Skeleton2D scene beside each —
        # the bones as Bone2D children with their rest transforms, and an
        # AnimationPlayer whose "animation" plays the baked frames.
        #
        # The baked frames already carry everything Lightbox solved — IK, constraints,
        # spline chains, the jiggle springs — so nothing here re-implements them: the
        # engine plays exactly what the artist saw.
        #
        # It never touches project.godot or the .godot/ cache. Godot owns those.
        @tool
        extends EditorScript

        func _run() -> void:
            var count := 0
            for path in _find_rigs("res://"):
                _build(path)
                count += 1
            print("Lightbox: built %d rig scene(s)." % count)

        func _find_rigs(root: String) -> PackedStringArray:
            var found := PackedStringArray()
            var dir := DirAccess.open(root)
            if dir == null:
                return found
            dir.list_dir_begin()
            var entry := dir.get_next()
            while entry != "":
                var path := root.path_join(entry)
                if dir.current_is_dir() and not entry.begins_with("."):
                    found.append_array(_find_rigs(path))
                elif entry.ends_with("_rig.json"):
                    found.append(path)
                entry = dir.get_next()
            dir.list_dir_end()
            return found

        func _build(path: String) -> void:
            var text := FileAccess.get_file_as_string(path)
            var rig: Dictionary = JSON.parse_string(text)
            if rig == null or not rig.has("bones"):
                push_warning("Lightbox: %s is not a rig file." % path)
                return

            var skeleton := Skeleton2D.new()
            skeleton.name = "Skeleton2D"
            var nodes := {}
            for bone_data in rig["bones"]:
                var bone := Bone2D.new()
                bone.name = bone_data["name"]
                bone.position = Vector2(bone_data["x"], bone_data["y"])
                bone.rotation = deg_to_rad(bone_data["rotationDeg"])
                bone.set_length(bone_data["length"])
                nodes[bone_data["name"]] = bone
            for bone_data in rig["bones"]:
                var bone: Bone2D = nodes[bone_data["name"]]
                var parent = bone_data.get("parent")
                if parent != null and nodes.has(parent):
                    nodes[parent].add_child(bone)
                else:
                    skeleton.add_child(bone)
            for bone_name in nodes:
                nodes[bone_name].rest = nodes[bone_name].transform

            var animation := Animation.new()
            var fps: int = rig.get("fps", 12)
            var frame_count: int = rig.get("frameCount", 1)
            animation.length = float(frame_count) / float(fps)
            animation.loop_mode = Animation.LOOP_LINEAR
            for bone_name in nodes:
                var bone_path := _path_to(skeleton, nodes[bone_name])
                var rot := animation.add_track(Animation.TYPE_VALUE)
                animation.track_set_path(rot, NodePath(bone_path + ":rotation"))
                var pos := animation.add_track(Animation.TYPE_VALUE)
                animation.track_set_path(pos, NodePath(bone_path + ":position"))
                for frame_data in rig.get("baked", []):
                    var local = frame_data["bones"].get(bone_name)
                    if local == null:
                        continue
                    var at := float(frame_data["frame"]) / float(fps)
                    animation.track_insert_key(rot, at, deg_to_rad(local["rotationDeg"]))
                    animation.track_insert_key(pos, at, Vector2(local["x"], local["y"]))

            var library := AnimationLibrary.new()
            library.add_animation("animation", animation)
            var player := AnimationPlayer.new()
            player.name = "AnimationPlayer"
            skeleton.add_child(player)
            player.add_animation_library("", library)

            _own(skeleton, skeleton)
            var scene := PackedScene.new()
            scene.pack(skeleton)
            var out := path.get_basename() + ".tscn"
            ResourceSaver.save(scene, out)
            print("Lightbox: %s -> %s" % [path, out])

        func _path_to(root: Node, node: Node) -> String:
            return str(root.get_path_to(node))

        func _own(node: Node, root: Node) -> void:
            for child in node.get_children():
                child.owner = root
                _own(child, root)
        """;
}
