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

/// <param name="Sheet">
/// The underlying sheet export, whole. Carried rather than summarised because a
/// caller that wanted the omitted-layer report would otherwise export a second time
/// to get it — which would rewrite the sidecar and strip the Unity block off it.
/// </param>
public sealed record UnityExportResult(
    string SheetPath,
    string MetadataPath,
    string? ImporterPath,
    int SpriteCount,
    int ClipCount,
    SpriteSheetResult? Sheet = null);

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

        /// <summary>Colliders for this sprite, in world units relative to its pivot.</summary>
        [JsonPropertyName("colliders")] public List<UnityCollider>? Colliders { get; set; }
    }

    /// <summary>
    /// One collision rectangle, already in the two numbers a
    /// <c>BoxCollider2D</c> takes.
    /// </summary>
    /// <remarks>
    /// Offset and size rather than a rect, because that is the shape Unity's
    /// inspector and API use — converting a rect on the Unity side would put the
    /// pixels-per-unit division and the Y flip in the one place that cannot be
    /// tested here.
    /// </remarks>
    private sealed class UnityCollider
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";

        /// <summary>"hurtbox", "hitbox" or "physics" — what layer this belongs on.</summary>
        [JsonPropertyName("role")] public string Role { get; set; } = "hurtbox";

        [JsonPropertyName("offset")] public double[] Offset { get; set; } = [];

        [JsonPropertyName("size")] public double[] Size { get; set; } = [];
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
        if (doc == null) throw new ArgumentNullException(nameof(doc));
        return Export([doc], sheetPath, options);
    }

    /// <summary>
    /// Several documents into one Unity atlas — every clock, pivot, anchor and
    /// collider read from the frame's <em>own</em> document, which is what
    /// <see cref="SpriteSheetResult.FrameOwners"/> exists to answer.
    /// </summary>
    public static UnityExportResult Export(
        IReadOnlyList<Doc> docs, string sheetPath, UnityExportOptions? options = null,
        IReadOnlyList<string>? names = null)
    {
        if (docs is not { Count: > 0 }) throw new ArgumentException("An export needs at least one document.", nameof(docs));
        var opts = options ?? new UnityExportOptions();
        var sheet = SpriteSheetExporter.Export(docs, sheetPath, opts.Sheet, names);

        // Read back what the exporter wrote rather than recomputing it. Two
        // computations of the same rect are two chances to disagree, and the
        // disagreement would be invisible until something rendered wrongly.
        using var written = JsonDocument.Parse(File.ReadAllText(sheet.MetadataPath));
        var root = written.RootElement;

        var block = new UnityBlock
        {
            // The tallest canvas decides the world scale — one atlas has one
            // pixels-per-unit, and the largest document is the one that must
            // not come out shrunk.
            PixelsPerUnit = UnityConvert.PixelsPerUnit(
                docs.Max(d => d.Scene.Height), opts.WorldHeightUnits),
            SecondsPerFrame = UnityConvert.SecondsPerFrame(docs[0].Scene.Fps),
        };

        var owners = sheet.FrameOwners;
        var frames = root.GetProperty("frames").EnumerateArray().ToList();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var owner = owners is null ? docs[0].Scene : docs[owners[i].Document].Scene;
            var local = owners?[i].Frame ?? i;
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

            if (owner.Pivot is { } pivot)
            {
                var (px, py) = UnityConvert.Pivot(pivot.X, pivot.Y, cellLeft, cellTop, w, h);
                sprite.Pivot = [px, py];
            }

            var resolved = Anchors.ResolvedAt(owner, local);
            if (owner.Anchors is { Count: > 0 } declared && resolved.Count > 0)
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

            // Where Unity measures a collider offset from. The pivot when there is
            // one; otherwise the cell's centre, because that is Unity's default
            // sprite origin and a collider offset means nothing without knowing
            // which of the two the sprite ended up with.
            var originX = owner.Pivot?.X ?? cellLeft + w / 2.0;
            var originY = owner.Pivot?.Y ?? cellTop + h / 2.0;

            var boxes = CollisionShapes.ResolvedAt(owner, local);
            if (owner.Shapes is { Count: > 0 } shapes && boxes.Count > 0)
            {
                var colliders = new List<UnityCollider>();
                foreach (var shape in shapes)
                {
                    if (!boxes.TryGetValue(shape.Id, out var box)) continue;
                    var (ox, oy, sx, sy) = UnityConvert.Collider(
                        box.X, box.Y, box.W, box.H, originX, originY, block.PixelsPerUnit);
                    colliders.Add(new UnityCollider
                    {
                        Name = string.IsNullOrWhiteSpace(shape.Name) ? shape.Id : shape.Name.Trim(),
                        Role = shape.Role switch
                        {
                            ShapeRole.Hitbox => "hitbox",
                            ShapeRole.Physics => "physics",
                            _ => "hurtbox",
                        },
                        Offset = [ox, oy],
                        Size = [sx, sy],
                    });
                }
                if (colliders.Count > 0) sprite.Colliders = colliders;
            }

            block.Sprites.Add(sprite);
        }

        // Each clip's event clock is its own document's — one sheet can hold
        // a 12 fps cycle and a 24 fps one, and a single number cannot.
        block.Clips = ClipsFor(root, from =>
            UnityConvert.SecondsPerFrame(
                (owners is null ? docs[0].Scene : docs[owners[from].Document].Scene).Fps));

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
            sheet.SheetPath, sheet.MetadataPath, importerPath,
            block.Sprites.Count, block.Clips?.Count ?? 0, sheet);
    }

    /// <summary>
    /// The sidecar's tags as clips, with each clip's events timed from its own
    /// start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>From the sidecar, not from the Scene.</b> It used to read
    /// <c>scene.Tags</c> and clamp them itself — a second implementation of the
    /// clamping <see cref="SpriteSheetExporter"/> already does, and the reason
    /// several documents could not produce a Unity artifact: those tags are the
    /// first document's, numbered in its own frames. The sheet writer merges
    /// every document's tags, shifts them to where their frames landed and adds
    /// one per document; reading that is how Unity gets a clip per cycle for
    /// free, and it is one clamp rather than two that have to agree.
    /// </para>
    /// <para>
    /// Unity's <c>AnimationEvent.time</c> is seconds from the clip's start, not
    /// from the sheet's — so an event on frame 9 of a clip that begins at frame 8
    /// is at one frame, not nine. Getting that wrong puts every event in a later
    /// clip out by the clip's offset, which reads as "events fire early" and is a
    /// horrible thing to chase. The sidecar's event frames are sheet-wide, which
    /// is exactly what this subtraction wants.
    /// </para>
    /// </remarks>
    private static List<UnityClip>? ClipsFor(JsonElement root, Func<int, double> perFrameAt)
    {
        var meta = root.GetProperty("meta");
        if (!meta.TryGetProperty("frameTags", out var tags)) return null;

        var events = meta.TryGetProperty("events", out var declared)
            ? declared.EnumerateArray().ToList()
            : [];

        var clips = new List<UnityClip>();
        foreach (var tag in tags.EnumerateArray())
        {
            var from = tag.GetProperty("from").GetInt32();
            var to = tag.GetProperty("to").GetInt32();
            var perFrame = perFrameAt(from);

            var inside = events
                .Where(e => e.GetProperty("frame").GetInt32() >= from
                            && e.GetProperty("frame").GetInt32() <= to)
                .OrderBy(e => e.GetProperty("frame").GetInt32())
                .Select(e => new UnityEvent
                {
                    Function = e.GetProperty("name").GetString() is { Length: > 0 } name
                        ? name
                        : "OnEvent",
                    Time = (e.GetProperty("frame").GetInt32() - from) * perFrame,
                })
                .ToList();

            clips.Add(new UnityClip
            {
                Name = tag.GetProperty("name").GetString() ?? "clip",
                From = from,
                To = to,
                Loop = !tag.TryGetProperty("loop", out var loop) || loop.GetBoolean(),
                Direction = tag.TryGetProperty("direction", out var d)
                    ? d.GetString() ?? "forward"
                    : "forward",
                Events = inside.Count > 0 ? inside : null,
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
        //
        // Two slicing APIs, chosen by Unity version, and that is not belt-and-braces:
        // TextureImporter.spritesheet STOPPED WORKING in 2021.2 and was removed in
        // 2022.2. Setting it on a modern Unity throws nothing and slices nothing —
        // the import "succeeds" and you get one sprite. Anything from 2021.2 up goes
        // through ISpriteEditorDataProvider, which needs the 2D Sprite package
        // (com.unity.2d.sprite — present in every 2D template).
        #if UNITY_EDITOR
        using System.Collections.Generic;
        using System.IO;
        using UnityEditor;
        using UnityEngine;
        #if UNITY_2021_2_OR_NEWER
        using UnityEditor.U2D.Sprites;
        #endif

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

                // One neutral list, so the version-specific slicing below differs only
                // in which API it hands these to.
                var slices = new List<Slice>();
                foreach (var sprite in sheet.unity.sprites)
                {
                    // The rect's y is measured from the top in the sidecar and from the
                    // bottom in Unity. This is the one flip the script does, and it is
                    // here rather than in the file because it needs the texture height,
                    // which only Unity knows.
                    var y = height - sprite.rect[1] - sprite.rect[3];
                    slices.Add(new Slice
                    {
                        Name = sprite.name,
                        Rect = new Rect(sprite.rect[0], y, sprite.rect[2], sprite.rect[3]),
                        Pivot = sprite.pivot != null && sprite.pivot.Length == 2
                            ? new Vector2(sprite.pivot[0], sprite.pivot[1])
                            : new Vector2(0.5f, 0.5f),
                    });
                }

                ApplySlices(importer, slices);
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                BuildClips(texturePath, sheet);
                Debug.Log($"Lightbox: imported {slices.Count} sprites from {Path.GetFileName(texturePath)}");
            }

            private struct Slice
            {
                public string Name;
                public Rect Rect;
                public Vector2 Pivot;
            }

            // The version split. Everything above and below is shared; only this
            // decides which API receives the rects.
            private static void ApplySlices(TextureImporter importer, List<Slice> slices)
            {
        #if UNITY_2021_2_OR_NEWER
                // 2021.2 and up. TextureImporter.spritesheet is inert here — it fails
                // silently, which is the worst failure mode available, so this path is
                // the one that matters.
                var factories = new SpriteDataProviderFactories();
                factories.Init();
                var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                if (provider == null)
                {
                    Debug.LogError(
                        "Lightbox: no sprite data provider. Install the 2D Sprite package "
                        + "(com.unity.2d.sprite) and import again.");
                    return;
                }
                provider.InitSpriteEditorDataProvider();

                var rects = new List<SpriteRect>();
                foreach (var slice in slices)
                {
                    rects.Add(new SpriteRect
                    {
                        name = slice.Name,
                        // A fresh id per rect. The old SpriteMetaData had no such field;
                        // here it is what Unity keys the sub-asset on, and leaving it
                        // default makes every sprite collide on the same empty id.
                        spriteID = GUID.Generate(),
                        rect = slice.Rect,
                        alignment = SpriteAlignment.Custom,
                        pivot = slice.Pivot,
                    });
                }

                provider.SetSpriteRects(rects.ToArray());
                provider.Apply();
        #else
                var metas = new List<SpriteMetaData>();
                foreach (var slice in slices)
                {
                    metas.Add(new SpriteMetaData
                    {
                        name = slice.Name,
                        rect = slice.Rect,
                        alignment = (int)SpriteAlignment.Custom,
                        pivot = slice.Pivot,
                    });
                }
                importer.spritesheet = metas.ToArray();
        #endif
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

            // Colliders are data, not an import action, and that is a Unity fact
            // rather than a shortcut: a Sprite is an asset and a collider is a
            // component, so there is nothing on the sliced sprite to set. The offsets
            // and sizes are already in world units relative to the sprite's pivot,
            // so applying one is two lines in whatever builds your character:
            //
            //   var box = go.AddComponent<BoxCollider2D>();
            //   box.offset = c.Offset; box.size = c.Size;
            //
            // Read them with CollidersOf below, and filter on role to decide which
            // layer each one belongs on.
            public static Collider2DSpec[] CollidersOf(string texturePath, string spriteName)
            {
                var jsonPath = Path.ChangeExtension(texturePath, ".json");
                if (!File.Exists(jsonPath)) return new Collider2DSpec[0];

                var sheet = JsonUtility.FromJson<Sheet>(File.ReadAllText(jsonPath));
                if (sheet?.unity?.sprites == null) return new Collider2DSpec[0];

                foreach (var sprite in sheet.unity.sprites)
                {
                    if (sprite.name != spriteName || sprite.colliders == null) continue;

                    var specs = new List<Collider2DSpec>();
                    foreach (var c in sprite.colliders)
                    {
                        specs.Add(new Collider2DSpec
                        {
                            Name = c.name,
                            Role = c.role,
                            Offset = new Vector2(c.offset[0], c.offset[1]),
                            Size = new Vector2(c.size[0], c.size[1]),
                        });
                    }
                    return specs.ToArray();
                }
                return new Collider2DSpec[0];
            }

            public struct Collider2DSpec
            {
                public string Name;
                public string Role;
                public Vector2 Offset;
                public Vector2 Size;
            }

            [System.Serializable]
            private class UnitySprite
            {
                public string name;
                public int[] rect;
                public float[] pivot;
                public UnityColliderEntry[] colliders;
            }

            [System.Serializable]
            private class UnityColliderEntry
            {
                public string name;
                public string role;
                public float[] offset;
                public float[] size;
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
