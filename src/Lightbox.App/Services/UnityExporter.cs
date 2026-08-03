using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;
using Lightbox.Core.Export;

namespace Lightbox.App.Services;

/// <param name="WorldHeightUnits">
/// How many Unity world units tall the canvas should be. Decides
/// pixels-per-unit, and it is taken rather than assumed because how many pixels
/// make a unit is a project-wide decision.
/// </param>
public sealed record UnityExportOptions(
    double WorldHeightUnits = 1.0,
    bool WriteImporter = true)
{
    /// <summary>Sheet options passed through to the sprite-sheet exporter.</summary>
    public SpriteSheetOptions Sheet { get; init; } = new();
}

public sealed record UnityExportResult(
    string SheetPath,
    string MetadataPath,
    string? ImporterPath,
    int SpriteCount,
    int ClipCount);

/// <summary>
/// Export for Unity: the atlas, the sidecar, and a Unity-side importer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lightbox never writes or edits a Unity <c>.meta</c> file.</b> Unity owns
/// those: they carry GUIDs, they are version-specific YAML, and Unity rewrites
/// them. Hand-writing one is how asset importers corrupt projects. So the split is
/// deliberate — Lightbox writes files, and a small editor script on Unity's side
/// does the Unity-side work through <c>TextureImporter</c> the way Unity intends.
/// </para>
/// <para>
/// The arithmetic that would bite is done <em>here</em> rather than in the shipped
/// script, and the answers go in a <c>unity</c> block in the sidecar. A pivot
/// converted wrongly looks like an animation problem and gets debugged as one, so
/// it belongs where <c>UnityConvertTests</c> can hold worked examples against it.
/// The script reads numbers; it does not compute them.
/// </para>
/// <para>
/// The block is <b>additive and optional</b>: the generic sidecar stays generic, so
/// Godot and Unreal read the same file without a Unity-shaped field in it, and a
/// document exported without this class produces exactly the bytes it did before.
/// </para>
/// </remarks>
public static class UnityExporter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The Unity-facing half of the sidecar.</summary>
    private sealed class UnityBlock
    {
        [JsonPropertyName("pixelsPerUnit")] public double PixelsPerUnit { get; set; }

        [JsonPropertyName("secondsPerFrame")] public double SecondsPerFrame { get; set; }

        [JsonPropertyName("sprites")] public List<UnitySprite> Sprites { get; set; } = [];

        [JsonPropertyName("clips")] public List<UnityClip>? Clips { get; set; }
    }

    private sealed class UnitySprite
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        [JsonPropertyName("rect")] public int[] Rect { get; set; } = [];

        /// <summary>Normalised, bottom-left, Y up, within this sprite's own rect.</summary>
        [JsonPropertyName("pivot")] public double[]? Pivot { get; set; }

        /// <summary>Named anchors, converted the same way as the pivot.</summary>
        [JsonPropertyName("anchors")] public Dictionary<string, double[]>? Anchors { get; set; }
    }

    private sealed class UnityClip
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        [JsonPropertyName("from")] public int From { get; set; }

        [JsonPropertyName("to")] public int To { get; set; }

        [JsonPropertyName("loop")] public bool Loop { get; set; }

        [JsonPropertyName("direction")] public string Direction { get; set; } = "forward";

        /// <summary>Events inside this clip, at seconds from the clip's start.</summary>
        [JsonPropertyName("events")] public List<UnityEvent>? Events { get; set; }
    }

    private sealed class UnityEvent
    {
        [JsonPropertyName("function")] public string Function { get; set; } = "";

        [JsonPropertyName("time")] public double Time { get; set; }
    }

    /// <summary>
    /// Write the atlas, the sidecar with its Unity block, and the importer.
    /// </summary>
    /// <remarks>
    /// The sheet is produced by <see cref="SpriteSheetExporter"/> unchanged — this
    /// adds a block to the sidecar it wrote rather than rendering anything itself,
    /// so the two exports cannot disagree about where a sprite is.
    /// </remarks>
    public static UnityExportResult Export(Doc doc, string sheetPath, UnityExportOptions? options = null)
    {
        var opts = options ?? new UnityExportOptions();
        var sheet = SpriteSheetExporter.Export(doc, sheetPath, opts.Sheet);
        var scene = doc.Scene;

        // Read back what the exporter wrote rather than recomputing it. Two
        // computations of the same rect are two chances to disagree, and the
        // disagreement would be invisible until something rendered wrongly.
        using var written = JsonDocument.Parse(File.ReadAllText(sheet.MetadataPath));
        var root = written.RootElement;

        var block = new UnityBlock
        {
            PixelsPerUnit = UnityConvert.PixelsPerUnit(scene.Height, opts.WorldHeightUnits),
            SecondsPerFrame = UnityConvert.SecondsPerFrame(scene.Fps),
        };

        var frames = root.GetProperty("frames").EnumerateArray().ToList();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var rect = frame.GetProperty("frame");
            var source = frame.GetProperty("spriteSourceSize");
            var cellLeft = source.GetProperty("x").GetInt32();
            var cellTop = source.GetProperty("y").GetInt32();
            var w = rect.GetProperty("w").GetInt32();
            var h = rect.GetProperty("h").GetInt32();

            var sprite = new UnitySprite
            {
                Name = $"{Path.GetFileNameWithoutExtension(sheetPath)}_{i}",
                Rect =
                [
                    rect.GetProperty("x").GetInt32(),
                    rect.GetProperty("y").GetInt32(),
                    w,
                    h,
                ],
            };

            if (scene.Pivot is { } pivot)
            {
                var (px, py) = UnityConvert.Pivot(pivot.X, pivot.Y, cellLeft, cellTop, w, h);
                sprite.Pivot = [px, py];
            }

            var resolved = Anchors.ResolvedAt(scene, i);
            if (scene.Anchors is { Count: > 0 } declared && resolved.Count > 0)
            {
                var anchors = new Dictionary<string, double[]>(StringComparer.Ordinal);
                foreach (var anchor in declared)
                {
                    if (!resolved.TryGetValue(anchor.Id, out var point)) continue;
                    var (ax, ay) = UnityConvert.Pivot(point.X, point.Y, cellLeft, cellTop, w, h);
                    var name = string.IsNullOrWhiteSpace(anchor.Name) ? anchor.Id : anchor.Name.Trim();
                    anchors.TryAdd(name, [ax, ay]);
                }
                if (anchors.Count > 0) sprite.Anchors = anchors;
            }

            block.Sprites.Add(sprite);
        }

        block.Clips = ClipsFor(scene);

        // Re-serialize the whole document with the block appended. Writing it as a
        // property on the object we parsed keeps every key the generic exporter
        // produced, so nothing that reads the sidecar today stops working.
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject()) merged[property.Name] = property.Value;
        merged["unity"] = JsonSerializer.SerializeToElement(block, Json);
        File.WriteAllText(sheet.MetadataPath, JsonSerializer.Serialize(merged, Json));

        string? importerPath = null;
        if (opts.WriteImporter)
        {
            importerPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(sheetPath))!, "LightboxSheetImporter.cs");
            // Only if it is not already there: it is source we ship, and somebody
            // may well have edited it to fit their project. Overwriting an artist's
            // edit on every export would be the worst kind of helpful.
            if (!File.Exists(importerPath)) File.WriteAllText(importerPath, ImporterSource);
        }

        return new UnityExportResult(
            sheet.SheetPath, sheet.MetadataPath, importerPath, block.Sprites.Count, block.Clips?.Count ?? 0);
    }

    /// <summary>
    /// Tags as clips, with each clip's events timed from its own start.
    /// </summary>
    /// <remarks>
    /// Unity's <c>AnimationEvent.time</c> is seconds from the clip's start, not
    /// from the sheet's — so an event on frame 9 of a clip that begins at frame 8 is
    /// at one frame, not nine. Getting that wrong puts every event in a
    /// later clip out by the clip's offset, which reads as "events fire early" and
    /// is a horrible thing to chase.
    /// </remarks>
    private static List<UnityClip>? ClipsFor(Scene scene)
    {
        if (scene.Tags is not { Count: > 0 } tags) return null;
        var last = Math.Max(0, Math.Max(1, scene.FrameCount) - 1);
        var perFrame = UnityConvert.SecondsPerFrame(scene.Fps);

        var clips = new List<UnityClip>();
        foreach (var tag in tags)
        {
            var from = Math.Clamp(Math.Min(tag.Start, tag.End), 0, last);
            var to = Math.Clamp(Math.Max(tag.Start, tag.End), 0, last);
            if (Math.Min(tag.Start, tag.End) > last) continue;

            var events = scene.Markers
                .Where(m => m.ExportsAsEvent && m.Frame >= from && m.Frame <= to)
                .OrderBy(m => m.Frame)
                .Select(m => new UnityEvent
                {
                    Function = string.IsNullOrWhiteSpace(m.Label) ? "OnEvent" : m.Label.Trim(),
                    Time = (m.Frame - from) * perFrame,
                })
                .ToList();

            clips.Add(new UnityClip
            {
                Name = string.IsNullOrWhiteSpace(tag.Name) ? tag.Id : tag.Name.Trim(),
                From = from,
                To = to,
                Loop = tag.Loop,
                Direction = tag.Direction switch
                {
                    TagDirection.Reverse => "reverse",
                    TagDirection.PingPong => "pingpong",
                    _ => "forward",
                },
                Events = events.Count > 0 ? events : null,
            });
        }
        return clips.Count > 0 ? clips : null;
    }

    /// <summary>
    /// The Unity-side importer, shipped as source.
    /// </summary>
    /// <remarks>
    /// Source rather than a compiled package, so it has no version coupling to
    /// Unity and can be read and edited by whoever has to live with it. It
    /// references <c>UnityEditor</c>, so it cannot be compiled or tested here —
    /// which is exactly why every number it needs is computed on this side and
    /// tested here.
    /// </remarks>
    internal const string ImporterSource = """
        // Lightbox → Unity sheet importer.
        //
        // Drop this anywhere under Assets/. It reads the .json Lightbox wrote beside
        // a sprite sheet and does the Unity-side work: slices the sprites, sets each
        // pivot, and builds an AnimationClip per tag with the right frame durations
        // and any animation events.
        //
        // It computes nothing. Every pivot is already normalised, bottom-left, Y up,
        // and inside its own sprite rect, and every time is already in seconds —
        // because a coordinate bug here looks like an animation bug and gets
        // debugged as one. See the "unity" block in the json.
        //
        // It never writes a .meta file. Unity owns those.
        #if UNITY_EDITOR
        using System.Collections.Generic;
        using System.IO;
        using UnityEditor;
        using UnityEngine;

        public static class LightboxSheetImporter
        {
            [MenuItem("Assets/Lightbox/Import selected sheet")]
            public static void ImportSelected()
            {
                foreach (var guid in Selection.assetGUIDs)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetExtension(path).ToLowerInvariant() == ".png") Import(path);
                }
            }

            public static void Import(string texturePath)
            {
                var jsonPath = Path.ChangeExtension(texturePath, ".json");
                if (!File.Exists(jsonPath))
                {
                    Debug.LogWarning($"Lightbox: no sidecar beside {texturePath}");
                    return;
                }

                var sheet = JsonUtility.FromJson<Sheet>(File.ReadAllText(jsonPath));
                if (sheet?.unity == null)
                {
                    Debug.LogWarning($"Lightbox: {jsonPath} has no unity block — export again for Unity.");
                    return;
                }

                var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = sheet.unity.pixelsPerUnit;
                // Point art stays point art. An artist who wants filtering can set it
                // afterwards; guessing Bilinear silently softens pixel work.
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;

                var height = 0;
                {
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    if (texture != null) height = texture.height;
                }

                var metas = new List<SpriteMetaData>();
                foreach (var sprite in sheet.unity.sprites)
                {
                    // The rect's y is measured from the top in the sidecar and from the
                    // bottom in Unity. This is the one flip the script does, and it is
                    // here rather than in the file because it needs the texture height,
                    // which only Unity knows.
                    var y = height - sprite.rect[1] - sprite.rect[3];
                    metas.Add(new SpriteMetaData
                    {
                        name = sprite.name,
                        rect = new Rect(sprite.rect[0], y, sprite.rect[2], sprite.rect[3]),
                        alignment = (int)SpriteAlignment.Custom,
                        pivot = sprite.pivot != null && sprite.pivot.Length == 2
                            ? new Vector2(sprite.pivot[0], sprite.pivot[1])
                            : new Vector2(0.5f, 0.5f),
                    });
                }

                importer.spritesheet = metas.ToArray();
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                BuildClips(texturePath, sheet);
                Debug.Log($"Lightbox: imported {metas.Count} sprites from {Path.GetFileName(texturePath)}");
            }

            private static void BuildClips(string texturePath, Sheet sheet)
            {
                if (sheet.unity.clips == null || sheet.unity.clips.Length == 0) return;

                var sprites = new List<Sprite>();
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(texturePath))
                {
                    if (asset is Sprite s) sprites.Add(s);
                }
                sprites.Sort((a, b) => a.name.CompareTo(b.name));
                if (sprites.Count == 0) return;

                var dir = Path.GetDirectoryName(texturePath);
                foreach (var clip in sheet.unity.clips)
                {
                    var animation = new AnimationClip { frameRate = 1f / sheet.unity.secondsPerFrame };

                    var binding = new EditorCurveBinding
                    {
                        type = typeof(SpriteRenderer),
                        path = "",
                        propertyName = "m_Sprite",
                    };

                    var keys = new List<ObjectReferenceKeyframe>();
                    for (var i = clip.from; i <= clip.to && i < sprites.Count; i++)
                    {
                        keys.Add(new ObjectReferenceKeyframe
                        {
                            // Seconds from the clip's start, not the sheet's.
                            time = (i - clip.from) * sheet.unity.secondsPerFrame,
                            value = sprites[i],
                        });
                    }
                    if (keys.Count == 0) continue;

                    AnimationUtility.SetObjectReferenceCurve(animation, binding, keys.ToArray());

                    var settings = AnimationUtility.GetAnimationClipSettings(animation);
                    settings.loopTime = clip.loop;
                    AnimationUtility.SetAnimationClipSettings(animation, settings);

                    if (clip.events != null && clip.events.Length > 0)
                    {
                        var events = new List<AnimationEvent>();
                        foreach (var e in clip.events)
                        {
                            events.Add(new AnimationEvent { functionName = e.function, time = e.time });
                        }
                        AnimationUtility.SetAnimationEvents(animation, events.ToArray());
                    }

                    var clipPath = Path.Combine(dir, clip.name + ".anim");
                    AssetDatabase.CreateAsset(animation, AssetDatabase.GenerateUniqueAssetPath(clipPath));
                }
                AssetDatabase.SaveAssets();
            }

            // JsonUtility needs plain serializable classes and reads only the fields
            // it knows, so the rest of the sidecar is ignored rather than fought.
            [System.Serializable] private class Sheet { public UnityBlock unity; }

            [System.Serializable]
            private class UnityBlock
            {
                public float pixelsPerUnit;
                public float secondsPerFrame;
                public UnitySprite[] sprites;
                public UnityClip[] clips;
            }

            [System.Serializable]
            private class UnitySprite
            {
                public string name;
                public int[] rect;
                public float[] pivot;
            }

            [System.Serializable]
            private class UnityClip
            {
                public string name;
                public int from;
                public int to;
                public bool loop;
                public string direction;
                public UnityEventEntry[] events;
            }

            [System.Serializable]
            private class UnityEventEntry
            {
                public string function;
                public float time;
            }
        }
        #endif
        """;
}
