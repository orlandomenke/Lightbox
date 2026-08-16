using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Core.Export;

/// <summary>
/// The DragonBones converter: our rig schema rendered as a DragonBones 5.5
/// skeleton file (<c>*_ske.json</c>) — the BSD-licensed interchange format
/// chosen for engine reach (owner's decision, <c>docs/DESIGN-bones.md</c>;
/// never Spine's format, never Live2D's).
/// </summary>
/// <remarks>
/// <para>
/// <b>A view of <see cref="RigDocument"/>, never of the document.</b> Every
/// converter reads the schema, which is what "our JSON is the source of
/// truth" means in practice: a second consumer proves the schema carries
/// enough, and a schema bug is fixed once rather than per engine.
/// </para>
/// <para>
/// <b>The animation is the baked block, one key per frame.</b> DragonBones
/// expresses bone animation as offsets from the bone's rest transform, in the
/// parent's space — the same space our baked locals are in, so the conversion
/// is a subtraction. No engine is asked to replay IK, constraints, splines or
/// the jiggle springs; they are already in the samples, which is the only way
/// the engine plays exactly what the artist saw.
/// </para>
/// <para>
/// <b>Skeleton and animation only — no slots, skins or displays.</b> Pixels
/// travel through the sprite-sheet path that already exists; this file is the
/// motion. The written subset is deliberately the small, stable core of the
/// format (bones, transforms, translate/rotate frames), because the design
/// doc's own warning is that DragonBones importers vary in quality — the less
/// surface written, the less surface to disagree about.
/// </para>
/// </remarks>
public static class DragonBonesConvert
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The skeleton file's content for a rig.</summary>
    /// <param name="name">The armature's name — the document's, usually.</param>
    public static string Convert(RigDocument rig, string name)
    {
        var rest = rig.Bones.ToDictionary(b => b.Name, b => b);

        var bones = rig.Bones.Select(b => new DbBone(
            b.Name,
            b.Parent,
            b.Length,
            new DbTransform(b.X, b.Y, b.RotationDeg, b.RotationDeg))).ToList();

        // One timeline per bone, one key per frame, each held for one tick —
        // sampled motion rather than sparse keys, because the samples carry
        // the solvers and the springs that DragonBones cannot run.
        var timelines = new List<DbBoneTimeline>();
        foreach (var bone in rig.Bones)
        {
            var translate = new List<DbTranslateFrame>();
            var rotate = new List<DbRotateFrame>();
            foreach (var frame in rig.Baked)
            {
                if (!frame.Bones.TryGetValue(bone.Name, out var local)) continue;
                var r = rest[bone.Name];
                translate.Add(new DbTranslateFrame(1, 0, local.X - r.X, local.Y - r.Y));
                rotate.Add(new DbRotateFrame(1, 0, Normalized(local.RotationDeg - r.RotationDeg)));
            }
            if (translate.Count == 0) continue;
            timelines.Add(new DbBoneTimeline(bone.Name, translate, rotate));
        }

        var root = new DbRoot(
            rig.Fps,
            name,
            "5.5",
            "5.5",
            [
                new DbArmature(
                    "Armature",
                    name,
                    rig.Fps,
                    bones,
                    [
                        new DbAnimation("animation", Math.Max(1, rig.FrameCount), 0, timelines),
                    ],
                    [new DbAction("animation")]),
            ]);
        return JsonSerializer.Serialize(root, Json);
    }

    /// <summary>
    /// Degrees folded into (-180, 180]: a solved chain can accumulate turns,
    /// and DragonBones treats a rotate key's value as the short way round.
    /// </summary>
    public static double Normalized(double degrees)
    {
        var d = degrees % 360;
        if (d > 180) d -= 360;
        if (d <= -180) d += 360;
        return d;
    }

    // ---- the written subset of the format, named for the reader ----------------

    private sealed record DbRoot(
        int FrameRate,
        string Name,
        string Version,
        string CompatibleVersion,
        IReadOnlyList<DbArmature> Armature);

    private sealed record DbArmature(
        string Type,
        string Name,
        int FrameRate,
        IReadOnlyList<DbBone> Bone,
        IReadOnlyList<DbAnimation> Animation,
        IReadOnlyList<DbAction> DefaultActions);

    private sealed record DbBone(
        string Name,
        string? Parent,
        double Length,
        DbTransform Transform);

    /// <remarks>DragonBones splits rotation into skX/skY shear angles; a rigid bone keeps them equal.</remarks>
    private sealed record DbTransform(
        double X,
        double Y,
        [property: JsonPropertyName("skX")] double SkX,
        [property: JsonPropertyName("skY")] double SkY);

    private sealed record DbAnimation(
        string Name,
        int Duration,
        int PlayTimes,
        IReadOnlyList<DbBoneTimeline> Bone);

    private sealed record DbBoneTimeline(
        string Name,
        IReadOnlyList<DbTranslateFrame> TranslateFrame,
        IReadOnlyList<DbRotateFrame> RotateFrame);

    private sealed record DbTranslateFrame(int Duration, double TweenEasing, double X, double Y);

    private sealed record DbRotateFrame(int Duration, double TweenEasing, double Rotate);

    private sealed record DbAction([property: JsonPropertyName("gotoAndPlay")] string GotoAndPlay);
}
