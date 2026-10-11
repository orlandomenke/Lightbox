using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Geometry;

namespace Lightbox.Core.Documents;

/// <summary>
/// A puppet pin: a point dropped on a drawing that the drawing is held by, or
/// — as starch — a patch of it that is kept from bending (Q239,
/// <c>docs/DESIGN-puppet-pins.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A pin is a handle in the rig, beside the bones</b> (Q239, the owner's
/// choice). It lives in <see cref="Armature.Pins"/>, and where it stands on a
/// frame is an ordinary pose key: <see cref="PoseKey.Bones"/> is keyed by id
/// and a pin's id goes in it like a bone's, its <see cref="BonePose.X"/> and
/// <see cref="BonePose.Y"/> the distance from where it was dropped. That is
/// what gives pins easing, held keys, stepping on 2s and the timeline's key
/// editing without a line of new code — and why a pin's id, like a bone's,
/// has to be unique across every rig in the document.
/// </para>
/// <para>
/// <b>Absent unless used, all the way down.</b> A rig with no pins writes no
/// <c>pins</c> key; an ordinary pin writes its id, name and place and nothing
/// else — no kind, no extent, no parent.
/// </para>
/// </remarks>
public sealed class Pin
{
    /// <summary>
    /// Keys a newer build wrote that this one does not know, carried through
    /// untouched so a save here does not drop them (Q230). Null unless a file
    /// supplied some; never edited, so a clone may share it.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public string Id { get; set; } = Ids.NewId("pin");

    /// <summary>What the artist calls it — "Wrist", "Left knee".</summary>
    public string Name { get; set; } = "Pin";

    /// <summary>Where it was dropped on the drawing at rest, in document coordinates.</summary>
    public double X { get; set; }

    /// <inheritdoc cref="X"/>
    public double Y { get; set; }

    /// <summary>
    /// What the pin does, or null for the ordinary one: it holds its point of
    /// the drawing where the pose puts it.
    /// </summary>
    public PinKind? Kind { get; set; }

    /// <summary>How far a starch pin stiffens the drawing around it, in document units. Null on any other pin.</summary>
    public double? Extent { get; set; }

    /// <summary>
    /// The bone this pin rides, or null. A pin on a bone goes where the bone
    /// takes it and is posed from there — what lets pins and bones share a
    /// figure: bones for the limbs, a pin for the cheek.
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// The kind actually in force. Derived; never serialized. Anything that
    /// is not starch is a position pin — including a number a file supplied
    /// that names no kind at all.
    /// </summary>
    [JsonIgnore]
    public PinKind EffectiveKind => Kind == PinKind.Starch ? PinKind.Starch : PinKind.Position;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Pin Clone() => (Pin)MemberwiseClone();
}

/// <summary>
/// Reading pins off the record for the solve: what was dropped where, and
/// where a pose puts each one.
/// </summary>
/// <remarks>
/// Pure functions over the record, deterministic for the reason
/// <see cref="ArmatureOps"/> is: the pose track is replayed on load and read
/// by the inbetweener, and two evaluations of the same pose must agree to
/// the bit.
/// </remarks>
public static class PinOps
{
    /// <summary>The rig's pins as the solve takes them, in the record's order. Empty for a rig with none.</summary>
    public static PinHandle[] HandlesOf(Armature armature)
    {
        if (armature.Pins is not { Count: > 0 } pins) return [];
        var handles = new PinHandle[pins.Count];
        for (var i = 0; i < pins.Count; i++)
            handles[i] = new PinHandle(pins[i].X, pins[i].Y, pins[i].EffectiveKind, pins[i].Extent ?? 0);
        return handles;
    }

    /// <summary>
    /// Where a pose puts each pin, one per pin in the record's order: where
    /// it was dropped, moved by its own key, and carried by its bone if it
    /// rides one. A pin the pose does not mention, on no bone, is at rest —
    /// exactly, so an unposed puppet is the identity to the last bit.
    /// </summary>
    /// <remarks>
    /// A pin's own offset is in its bone's frame, the way a child bone's is
    /// in its parent's: turn the arm and the offset turns with it. A parent
    /// id that names no bone is treated as no parent, like a bone's.
    /// <para>
    /// "At rest" means a null pose, as it does for bones: a pose that is
    /// merely empty still runs inverse kinematics and constraints, so on a
    /// rig that has them a pin riding a driven bone goes where the solver
    /// puts that bone.
    /// </para>
    /// </remarks>
    public static (double X, double Y)[] TargetsAt(Armature armature, IReadOnlyDictionary<string, BonePose>? pose)
    {
        if (armature.Pins is not { Count: > 0 } pins) return [];
        var targets = new (double X, double Y)[pins.Count];
        Dictionary<string, RigidDelta>? deltas = null;
        for (var i = 0; i < pins.Count; i++)
        {
            var pin = pins[i];
            var (x, y) = (pin.X, pin.Y);
            // A file can say "id": null; a dictionary cannot be asked for it.
            if (pose is not null && pin.Id is { } id && pose.TryGetValue(id, out var own))
            {
                if (double.IsFinite(own.X)) x += own.X;
                if (double.IsFinite(own.Y)) y += own.Y;
            }
            if (pose is not null && pin.ParentId is { Length: > 0 } parent && armature.BoneById(parent) is not null)
            {
                deltas ??= Skinning.Deltas(armature, pose);
                if (deltas.TryGetValue(parent, out var delta)) (x, y) = delta.Apply(x, y);
            }
            targets[i] = (x, y);
        }
        return targets;
    }
}
