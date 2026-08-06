using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;

namespace Lightbox.App.Services;

/// <param name="WriteImporter">Write the Unreal-side importer script beside the sheet.</param>
public sealed record UnrealExportOptions(bool WriteImporter = true)
{
    public SpriteSheetOptions Sheet { get; init; } = new();

    /// <summary>
    /// How tall the drawing should stand in the world, in <b>metres</b>.
    /// </summary>
    /// <remarks>
    /// Metres, the same as the Unity export takes, so one preset serves both. The
    /// conversion to Unreal's centimetre unit happens in
    /// <see cref="UnrealConvert.PixelsPerUnrealUnit"/> where it is tested, not here.
    /// </remarks>
    public double WorldHeightUnits { get; init; } = 1.0;
}

public sealed record UnrealExportResult(
    string SheetPath,
    string MetadataPath,
    string? ImporterPath,
    int SpriteCount,
    int FlipbookCount,
    SpriteSheetResult? Sheet = null);

/// <summary>
/// Export for Unreal: the atlas, the sidecar, and an Unreal-side importer in Python.
/// </summary>
/// <remarks>
/// <para>
/// <b>Unreal's <c>.uasset</c> is binary and cannot be written from outside the
/// editor</b>, so unlike Unity and Godot there was never a choice here: this is an
/// in-editor script or it is nothing. That turns out to be the same shape the other two
/// arrived at anyway — <b>Lightbox writes files and data; the engine's own API builds
/// the asset</b> — which is the third time that has been the right answer and is now
/// simply how engine export works here.
/// </para>
/// <para>
/// <b>The script treats every property name as fallible, and that is the load-bearing
/// decision.</b> Paper2D's Python surface could be confirmed for the properties this
/// uses, but not all of it, and a <c>set_editor_property</c> against a name that moved
/// between engine versions is exactly the failure that shipped a Unity importer which
/// silently sliced nothing. So every write goes through one helper that records what it
/// could not set and prints the list as an error at the end. An import that half-worked
/// says so.
/// </para>
/// <para>
/// The <c>unreal</c> block carries three things the generic sidecar cannot: the
/// centimetre-based pixels-per-unit, per-frame <c>frame_run</c> counts, and the pivot
/// expressed the way <c>PaperSprite</c> wants it. Everything else — regions, fps, tags
/// — is read from the generic file, because two copies of a rect are two chances to
/// disagree.
/// </para>
/// </remarks>
public static class UnrealExporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The Unreal-facing half of the sidecar: only what the generic file lacks.</summary>
    private sealed class UnrealBlock
    {
        /// <summary>
        /// <c>PaperSprite.pixels_per_unreal_unit</c> — pixels per <b>centimetre</b>.
        /// </summary>
        [JsonPropertyName("pixelsPerUnrealUnit")] public double PixelsPerUnrealUnit { get; set; }

        /// <summary>
        /// Per-frame <c>PaperFlipbookKeyFrame.frame_run</c>: a count of flipbook frames.
        /// </summary>
        [JsonPropertyName("frameRuns")] public List<int> FrameRuns { get; set; } = [];

        /// <summary>
        /// Per-sprite <c>custom_pivot_point</c>, in the frame order of the generic
        /// sidecar. Absent when the document has no pivot.
        /// </summary>
        [JsonPropertyName("pivotPoints")] public List<double[]>? PivotPoints { get; set; }

        /// <summary>The stem every sprite asset is named from.</summary>
        /// <remarks>
        /// Written here rather than derived in the script, and that is the point: the
        /// sanitising rules for an Unreal object name live in
        /// <see cref="UnrealConvert.AssetName"/> where they are tested, instead of being
        /// implemented twice and drifting. The script does no name-cleaning of its own.
        /// </remarks>
        [JsonPropertyName("assetBase")] public string AssetBase { get; set; } = "Sprite";

        /// <summary>
        /// One flipbook name per animation, already sanitised and de-duplicated.
        /// </summary>
        /// <remarks>
        /// One entry even for an untagged sheet, which gets a single flipbook — so the
        /// script iterates a list in both cases rather than branching on the tags and
        /// inventing a name in one of the branches.
        /// </remarks>
        [JsonPropertyName("flipbookNames")] public List<string> FlipbookNames { get; set; } = [];
    }

    public static UnrealExportResult Export(Doc doc, string sheetPath, UnrealExportOptions? options = null) =>
        Export([doc], sheetPath, options);

    /// <summary>
    /// One Unreal artifact from several documents — one flipbook per document.
    /// </summary>
    /// <remarks>
    /// Flipbooks come from the sidecar's <c>frameTags</c>, already merged and
    /// shifted by the sheet writer, so the list needed nothing. Frame runs and
    /// pivot points did: both are per document, and the first document's would
    /// be wrong for every cycle after it.
    /// </remarks>
    public static UnrealExportResult Export(
        IReadOnlyList<Doc> docs, string sheetPath, UnrealExportOptions? options = null)
    {
        if (docs.Count == 0) throw new ArgumentException("An export needs a document.", nameof(docs));
        var opts = options ?? new UnrealExportOptions();
        var sheet = SpriteSheetExporter.Export(docs, sheetPath, opts.Sheet);
        var owners = sheet.FrameOwners;
        // Pixels-per-unit is one number for the whole texture, so it takes the
        // leading document's canvas — the same choice the sidecar's own header
        // makes for fps and pivot.
        var scene = docs[0].Scene;

        // Read back what the exporter wrote rather than recomputing it — one source for
        // where a sprite is, the same rule the Unity and Godot exports follow.
        using var written = JsonDocument.Parse(File.ReadAllText(sheet.MetadataPath));
        var root = written.RootElement;
        var frames = root.GetProperty("frames").EnumerateArray().ToList();

        var block = new UnrealBlock
        {
            PixelsPerUnrealUnit = UnrealConvert.PixelsPerUnrealUnit(scene.Height, opts.WorldHeightUnits),
        };
        var pivots = new List<double[]>();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var owner = docs[i < owners.Count ? owners[i].Document : 0].Scene;
            block.FrameRuns.Add(UnrealConvert.FrameRun(
                frame.GetProperty("duration").GetInt32(), owner.Fps));

            if (owner.Pivot is { } pivot)
            {
                var source = frame.GetProperty("spriteSourceSize");
                var (px, py) = UnrealConvert.PivotPoint(
                    pivot.X, pivot.Y,
                    source.GetProperty("x").GetInt32(), source.GetProperty("y").GetInt32());
                pivots.Add([px, py]);
            }
        }
        if (pivots.Count > 0) block.PivotPoints = pivots;

        block.AssetBase = UnrealConvert.AssetName(
            Path.GetFileNameWithoutExtension(sheet.SheetPath), "LightboxSheet");
        block.FlipbookNames = FlipbookNames(root, block.AssetBase);

        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject()) merged[property.Name] = property.Value;
        merged["unreal"] = JsonSerializer.SerializeToElement(block, Json);
        File.WriteAllText(sheet.MetadataPath, JsonSerializer.Serialize(merged, Json));

        string? importerPath = null;
        if (opts.WriteImporter)
        {
            importerPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(sheetPath))!, "lightbox_import.py");
            // Only if absent: it is source we ship and somebody may have adjusted it.
            if (!File.Exists(importerPath)) File.WriteAllText(importerPath, ImporterSource);
        }

        var flipbooks = root.GetProperty("meta").TryGetProperty("frameTags", out var tags)
            ? Math.Max(1, tags.GetArrayLength())
            : 1;

        return new UnrealExportResult(
            sheet.SheetPath, sheet.MetadataPath, importerPath, frames.Count, flipbooks, sheet);
    }

    /// <summary>
    /// One asset name per flipbook: one per tag, or a single one for an untagged sheet.
    /// </summary>
    /// <remarks>
    /// De-duplicated here rather than left to Unreal, which would append its own number
    /// and produce a name that no longer matches the tag it came from. Two tags called
    /// "run" become <c>Hero_run</c> and <c>Hero_run_1</c>, in tag order.
    /// </remarks>
    private static List<string> FlipbookNames(JsonElement root, string assetBase)
    {
        if (!root.GetProperty("meta").TryGetProperty("frameTags", out var tags)
            || tags.GetArrayLength() == 0)
        {
            return [$"{assetBase}_Flipbook"];
        }

        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var names = new List<string>();

        foreach (var tag in tags.EnumerateArray())
        {
            var raw = tag.TryGetProperty("name", out var n) ? n.GetString() : null;
            var clean = UnrealConvert.AssetName(raw, "Animation");

            if (used.TryGetValue(clean, out var seen)) names.Add($"{assetBase}_{clean}_{seen}");
            else names.Add($"{assetBase}_{clean}");

            used[clean] = seen + 1;
        }

        return names;
    }

    /// <summary>
    /// The Unreal-side importer, shipped as Python.
    /// </summary>
    /// <remarks>
    /// Python rather than a C++ plugin or an editor utility widget: the Python plugin
    /// ships with the editor and needs enabling once, where a C++ module would mean a
    /// build and a widget would mean an asset we put into somebody's project uninvited.
    /// </remarks>
    internal const string ImporterSource = """"
        # Lightbox -> Unreal Paper2D importer.
        #
        # Enable the "Python Editor Script Plugin" and "Paper2D" plugins, then run this
        # from the editor: Tools > Execute Python Script..., or in the Output Log's Cmd
        # box with `py "<path>/lightbox_import.py"`.
        #
        # It finds every Lightbox sidecar under the project's Content folder, imports the
        # sheet as a texture, and builds one PaperSprite per frame and one PaperFlipbook
        # per tag using Unreal's own asset API. Lightbox writes no .uasset — it cannot,
        # they are binary, which is why this script exists at all.
        #
        # WHY EVERY PROPERTY WRITE GOES THROUGH _apply():
        # Paper2D's scripting surface is marked experimental and property names have moved
        # between engine versions. A set_editor_property() against a name that no longer
        # exists is the failure mode that produces an asset which looks imported and is
        # empty. _apply() records anything it could not set and this script fails loudly
        # with the list, rather than leaving you to find it in the sprite editor.
        import json
        import os

        import unreal


        def _apply(obj, name, value, missing):
            """Set an editor property, recording rather than raising if it is not there."""
            try:
                obj.set_editor_property(name, value)
                return True
            except Exception as exc:  # noqa: BLE001 - the whole point is to keep going
                missing.append("%s.%s (%s)" % (type(obj).__name__, name, exc))
                return False


        def _content_path(abs_dir):
            """An absolute directory under Content/ as a /Game/... package path."""
            content = os.path.normpath(unreal.Paths.project_content_dir())
            rel = os.path.relpath(os.path.normpath(abs_dir), content)
            if rel in (".", ""):
                return "/Game"
            return "/Game/" + rel.replace(os.sep, "/")


        def _find_sidecars():
            found = []
            content = unreal.Paths.project_content_dir()
            for root, dirs, files in os.walk(content):
                dirs[:] = [d for d in dirs if not d.startswith(".")]
                for name in files:
                    if not name.endswith(".json"):
                        continue
                    path = os.path.join(root, name)
                    data = _read(path)
                    if data is not None:
                        found.append((path, data))
            return found


        def _read(path):
            # Ours only: an "unreal" block plus our app name, so this cannot mistake an
            # unrelated JSON in somebody's project for a sheet to import.
            try:
                with open(path, "r", encoding="utf-8") as handle:
                    data = json.load(handle)
            except Exception:  # noqa: BLE001
                return None
            if not isinstance(data, dict) or "unreal" not in data:
                return None
            if data.get("meta", {}).get("app", "") != "Lightbox":
                return None
            return data


        def _import_texture(png_path, package_path, missing):
            task = unreal.AssetImportTask()
            _apply(task, "filename", png_path, missing)
            _apply(task, "destination_path", package_path, missing)
            _apply(task, "automated", True, missing)
            _apply(task, "replace_existing", True, missing)
            _apply(task, "save", False, missing)
            unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])

            name = os.path.splitext(os.path.basename(png_path))[0]
            texture = unreal.load_asset("%s/%s" % (package_path, name))
            if texture is None:
                return None

            # Sprite-sheet texture settings. Mips are what make an atlas bleed: a lower
            # mip averages across region boundaries, so a sliver of the next sprite ends
            # up on this one. Nearest filtering keeps pixel art crisp.
            _apply(texture, "mip_gen_settings",
                   unreal.TextureMipGenSettings.TMGS_NO_MIPMAPS, missing)
            _apply(texture, "filter", unreal.TextureFilter.TF_NEAREST, missing)
            _apply(texture, "compression_settings",
                   unreal.TextureCompressionSettings.TC_EDITOR_ICON, missing)
            texture.modify()
            return texture


        def _make_sprite(name, package_path, texture, rect, ppuu, pivot, missing):
            tools = unreal.AssetToolsHelpers.get_asset_tools()
            sprite = tools.create_asset(name, package_path, unreal.PaperSprite,
                                        unreal.PaperSpriteFactory())
            if sprite is None:
                return None

            # The factory exposes no texture, so the region is set afterwards.
            _apply(sprite, "source_texture", texture, missing)
            _apply(sprite, "source_uv",
                   unreal.Vector2D(float(rect.get("x", 0)), float(rect.get("y", 0))), missing)
            _apply(sprite, "source_dimension",
                   unreal.Vector2D(float(rect.get("w", 0)), float(rect.get("h", 0))), missing)
            _apply(sprite, "pixels_per_unreal_unit", float(ppuu), missing)

            if pivot is not None:
                # custom_pivot_point is texture pixels from the sprite rectangle's own
                # top-left, Y down. Lightbox has already converted; this passes it
                # through rather than computing one.
                _apply(sprite, "pivot_mode", unreal.SpritePivotMode.CUSTOM, missing)
                _apply(sprite, "custom_pivot_point",
                       unreal.Vector2D(float(pivot[0]), float(pivot[1])), missing)
                _apply(sprite, "snap_pivot_to_pixel_grid", False, missing)

            return sprite


        def _make_flipbook(name, package_path, sprites, runs, fps, first, last, missing):
            tools = unreal.AssetToolsHelpers.get_asset_tools()
            flipbook = tools.create_asset(name, package_path, unreal.PaperFlipbook,
                                          unreal.PaperFlipbookFactory())
            if flipbook is None:
                return None

            keys = []
            for i in range(first, min(last + 1, len(sprites))):
                if sprites[i] is None:
                    continue
                key = unreal.PaperFlipbookKeyFrame()
                _apply(key, "sprite", sprites[i], missing)
                # frame_run is a COUNT of flipbook frames, not a time. Lightbox has
                # already converted; a zero here would drop the drawing entirely.
                _apply(key, "frame_run", int(runs[i]) if i < len(runs) else 1, missing)
                keys.append(key)

            _apply(flipbook, "frames_per_second", float(fps), missing)
            _apply(flipbook, "key_frames", keys, missing)
            return flipbook


        def _import(json_path, data, missing):
            meta = data.get("meta", {})
            frames = data.get("frames", [])
            block = data.get("unreal", {})

            folder = os.path.dirname(json_path)
            package_path = _content_path(folder)
            png_path = os.path.join(folder, meta.get("image", ""))
            if not os.path.isfile(png_path):
                unreal.log_warning("Lightbox: no sheet beside %s" % json_path)
                return

            texture = _import_texture(png_path, package_path, missing)
            if texture is None:
                unreal.log_warning("Lightbox: could not import %s" % png_path)
                return

            # Asset names come from the sidecar, already sanitised and de-duplicated.
            # Unreal object names reject punctuation an artist will happily put in a
            # sheet name, and those rules are tested on the Lightbox side rather than
            # implemented a second time here where they could drift.
            base = block.get("assetBase", "LightboxSheet")
            names = block.get("flipbookNames", [])
            ppuu = float(block.get("pixelsPerUnrealUnit", 1.0))
            runs = block.get("frameRuns", [])
            pivots = block.get("pivotPoints", None)

            sprites = []
            for i, frame in enumerate(frames):
                pivot = pivots[i] if pivots is not None and i < len(pivots) else None
                sprites.append(_make_sprite(
                    "%s_%03d" % (base, i), package_path, texture,
                    frame.get("frame", {}), ppuu, pivot, missing))

            # One flipbook per tag, or one for the whole sheet when there are none. The
            # sidecar's name list has an entry for both cases, so there is no branch here
            # in which this script has to invent a name.
            tags = meta.get("frameTags", [])
            spans = ([(0, len(sprites) - 1)] if not tags
                     else [(int(t.get("from", 0)), int(t.get("to", 0))) for t in tags])

            made = 0
            for i, span in enumerate(spans):
                name = names[i] if i < len(names) else "%s_Flipbook_%d" % (base, i)
                if _make_flipbook(name, package_path, sprites, runs,
                                  meta.get("fps", 12), span[0], span[1], missing):
                    made += 1

            for asset in sprites:
                if asset is not None:
                    unreal.EditorAssetLibrary.save_loaded_asset(asset)
            unreal.EditorAssetLibrary.save_loaded_asset(texture)

            unreal.log("Lightbox: %d sprite(s), %d flipbook(s) in %s"
                       % (len([s for s in sprites if s]), made, package_path))


        def run():
            sheets = _find_sidecars()
            if not sheets:
                unreal.log_warning("Lightbox: no sidecars found under Content/")
                return

            missing = []
            for path, data in sheets:
                _import(path, data, missing)

            if missing:
                # Loudly, and once, with the whole list. This is the difference between
                # "the import did not work" and three days of wondering why a flipbook
                # plays at the wrong speed.
                unreal.log_error(
                    "Lightbox: %d property write(s) failed - the imported assets are "
                    "incomplete. This usually means a Paper2D property was renamed in "
                    "this engine version:" % len(missing))
                for item in sorted(set(missing)):
                    unreal.log_error("  " + item)


        run()
        """";
}
