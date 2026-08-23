using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Export;

/// <summary>
/// The rig export's own JSON schema — phase 6 of <c>docs/DESIGN-bones.md</c>,
/// under the owner's decision: our schema is the source of truth, and the
/// engine converters (Godot, DragonBones) are views of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two animation blocks, and both are the point.</b> <c>Keys</c> is the
/// authored record — the sparse pose keys, verbatim — because a source of
/// truth that dropped authorship would be a render, not a source.
/// <c>Baked</c> is the per-frame <em>solved</em> result: IK, constraints,
/// splines, correctives' drivers and the jiggle springs all folded in, as
/// bone-local transforms. Engines consume the baked block — no engine can be
/// asked to replay Lightbox's solvers — and determinism is what makes baking
/// honest: the same document bakes the same frames forever.
/// </para>
/// <para>
/// <b>Bones are named, and names are made unique here.</b> Engines address
/// bones by name; the record addresses them by id. A rig with two bones both
/// called <c>bone.3</c> exports as <c>bone.3</c> and <c>bone.3#2</c>,
/// deterministically (authoring order), rather than exporting a file where
/// half an animation lands on the wrong bone.
/// </para>
/// <para>
/// Never Spine's format, never Live2D's — the licensing wall in
/// <c>docs/DESIGN-bones.md</c>. DragonBones (BSD) and our own schema are the
/// interchange surfaces.
/// </para>
/// </remarks>
public static class RigExport
{
    /// <summary>The schema version engines can gate on.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Whether this document has anything to export.</summary>
    public static bool HasRig(Doc doc) => doc.Armature is { Bones.Count: > 0 };

    /// <summary>
    /// Build the export model: the skeleton, the authored keys, and the baked
    /// per-frame animation over the scene's timeline.
    /// </summary>
    public static RigDocument Build(Doc doc)
    {
        if (doc.Armature is not { Bones.Count: > 0 } armature)
            throw new InvalidOperationException("The document has no rig to export.");

        var names = UniqueNames(armature);
        var bones = armature.Bones.Select(b => new RigBone(
            names[b.Id],
            b.ParentId is { } p && names.TryGetValue(p, out var parent) ? parent : null,
            b.X,
            b.Y,
            b.RotationDeg,
            b.Length,
            b.Jiggle is { } j ? new RigJiggle(j.Stiffness, j.Damping) : null)).ToList();

        var keys = ArmatureOps.Ordered(doc.Scene.PoseTrack)
            .Select(k => new RigKey(
                k.Frame,
                k.Ease.ToString(),
                k.Bones
                    .Where(e => names.ContainsKey(e.Key))
                    .ToDictionary(e => names[e.Key], e => new RigPose(
                        e.Value.RotationDeg, e.Value.X, e.Value.Y))))
            .ToList();

        var baked = BakeFrames(doc, armature, names);

        // The step travels with the authored keys — a re-import loses nothing
        // — while the baked frames already carry its effect, sampled and held.
        var step = doc.Scene.PoseTrack?.Step is { } s and > 1 ? s : (int?)null;

        return new RigDocument(SchemaVersion, doc.Scene.Fps, doc.Scene.FrameCount, step, bones, keys, baked);
    }

    /// <summary>Serialize the export model to the schema's JSON.</summary>
    public static string ToJson(RigDocument rig) => JsonSerializer.Serialize(rig, Json);

    /// <summary>Build and serialize in one step.</summary>
    public static string ExportJson(Doc doc) => ToJson(Build(doc));

    /// <summary>
    /// The solved animation, one entry per timeline frame: each bone's
    /// <em>local</em> transform (relative to its parent) with every solver in
    /// force — the same arithmetic the canvas renders, so the engine plays
    /// exactly what the artist saw.
    /// </summary>
    private static List<RigFrame> BakeFrames(
        Doc doc, Armature armature, IReadOnlyDictionary<string, string> names)
    {
        var frames = new List<RigFrame>(doc.Scene.FrameCount);
        for (var f = 0; f < doc.Scene.FrameCount; f++)
        {
            var pose = ArmatureOps.EffectivePoseAt(armature, doc.Scene.PoseTrack, f);
            var placements = ArmatureOps.Solve(armature, pose);
            var locals = new Dictionary<string, RigPose>(armature.Bones.Count);
            foreach (var bone in armature.Bones)
            {
                var world = placements[bone.Id];
                var parent = bone.ParentId is { } p && placements.TryGetValue(p, out var pw)
                    ? pw
                    : new BonePlacement(0, 0, 0);
                // World back into the parent's frame — the inverse of the
                // solve's own composition, so a chain of these replays the
                // solved placements exactly.
                var rad = -parent.RotationDeg * Math.PI / 180.0;
                var dx = world.X - parent.X;
                var dy = world.Y - parent.Y;
                locals[names[bone.Id]] = new RigPose(
                    world.RotationDeg - parent.RotationDeg,
                    Math.Cos(rad) * dx - Math.Sin(rad) * dy,
                    Math.Sin(rad) * dx + Math.Cos(rad) * dy);
            }
            frames.Add(new RigFrame(f, locals));
        }
        return frames;
    }

    /// <summary>
    /// A deterministic unique display name per bone id: the name itself, or
    /// <c>name#2</c>, <c>name#3</c>… in authoring order for duplicates.
    /// </summary>
    public static Dictionary<string, string> UniqueNames(Armature armature)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(armature.Bones.Count);
        foreach (var bone in armature.Bones)
        {
            var name = bone.Name;
            for (var i = 2; !taken.Add(name); i++) name = $"{bone.Name}#{i}";
            result[bone.Id] = name;
        }
        return result;
    }
}

/// <summary>The exported rig: skeleton, authored keys, baked frames.</summary>
public sealed record RigDocument(
    [property: JsonPropertyName("lightboxRig")] int Schema,
    int Fps,
    int FrameCount,
    int? Step,
    IReadOnlyList<RigBone> Bones,
    IReadOnlyList<RigKey> Keys,
    IReadOnlyList<RigFrame> Baked);

/// <summary>One bone at rest, in its parent's frame.</summary>
public sealed record RigBone(
    string Name,
    string? Parent,
    double X,
    double Y,
    double RotationDeg,
    double Length,
    RigJiggle? Jiggle);

/// <summary>Secondary-motion settings, carried so a re-import loses nothing.</summary>
public sealed record RigJiggle(double Stiffness, double Damping);

/// <summary>One authored pose key: sparse departures from rest, by bone name.</summary>
public sealed record RigKey(
    int Frame,
    string Ease,
    IReadOnlyDictionary<string, RigPose> Bones);

/// <summary>A departure from rest (keys) or a solved local transform (baked frames).</summary>
public sealed record RigPose(double RotationDeg, double X, double Y);

/// <summary>One baked frame: every bone's solved local transform.</summary>
public sealed record RigFrame(
    int Frame,
    IReadOnlyDictionary<string, RigPose> Bones);
