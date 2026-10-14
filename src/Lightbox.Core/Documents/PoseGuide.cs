using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>A point of a pose, in the document's stroke coordinates.</summary>
public readonly record struct PosePoint(double X, double Y);

/// <summary>
/// The pose of the subject in two keys (Q274): each part it is made of — a
/// leg, a tail, a lamp's arm — as a chain of joints, with where each joint is
/// in key A and in key B. Kept on key A for the gap to the next key, the way a
/// timing chart and line matches are; null, and no key in the file, until a
/// pose is read.
/// </summary>
/// <remarks>
/// A guide, never a drawing: it is shown over the inbetweens for the artist to
/// draw against and is in no render and no export. Claude reads it; the artist
/// corrects it by dragging a joint.
/// </remarks>
public sealed class PoseGuide
{
    /// <summary>Keys a newer build wrote that this one does not know (Q230).</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public List<PosePart> Parts { get; set; } = [];

    public PoseGuide Clone() => new() { Unknown = Unknown, Parts = [.. Parts.Select(p => p.Clone())] };
}

/// <summary>One part of the subject: a name, and its joints in order along it, the root first.</summary>
public sealed class PosePart
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public string Name { get; set; } = "";

    public List<PoseJoint> Joints { get; set; } = [];

    public PosePart Clone() => new() { Unknown = Unknown, Name = Name, Joints = [.. Joints.Select(j => j.Clone())] };
}

/// <summary>A joint of a part, where it is in key A and in key B.</summary>
public sealed class PoseJoint
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public string Name { get; set; } = "";

    public PosePoint A { get; set; }

    public PosePoint B { get; set; }

    public PoseJoint Clone() => new() { Unknown = Unknown, Name = Name, A = A, B = B };
}
