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

    /// <summary>More parts than this is no subject an animator would chain out by hand.</summary>
    public const int MaxParts = 24;

    /// <summary>More joints than this in one part is no chain anyone would drag.</summary>
    public const int MaxJoints = 16;

    /// <summary>A part's or a joint's name, at most: a word or two.</summary>
    public const int MaxName = 40;

    /// <summary>Further from the origin than this is no place on any canvas.</summary>
    private const double MaxCoordinate = 1e7;

    /// <summary>
    /// What of a pose can be trusted, from a file or a model, or null when
    /// nothing can (Q274 review). A null where a list or a joint should be is
    /// nothing there. A joint without a usable place ends its chain there —
    /// joining the joints either side would make one long straight bone, a
    /// wrong guide that looks right — and the first run of two or more joints
    /// is the part. A part needs a name, and only the first part with a name
    /// keeps it, because a name is what says a part is the same part in both
    /// keys. Counts and names are capped.
    /// </summary>
    public static PoseGuide? Readable(PoseGuide? guide)
    {
        if (guide?.Parts is null) return null;
        var parts = new List<PosePart>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in guide.Parts)
        {
            if (part?.Joints is null) continue;
            var name = Named(part.Name);
            if (name.Length == 0 || names.Contains(name)) continue;
            var run = new List<PoseJoint>();
            foreach (var joint in part.Joints)
            {
                if (joint is not null && Usable(joint.A) && Usable(joint.B))
                {
                    if (run.Count < MaxJoints) run.Add(new PoseJoint { Unknown = joint.Unknown, Name = Named(joint.Name), A = joint.A, B = joint.B });
                    continue;
                }
                if (run.Count >= 2) break;
                run.Clear();
            }
            if (run.Count < 2) continue;
            names.Add(name);
            parts.Add(new PosePart { Unknown = part.Unknown, Name = name, Joints = run });
            if (parts.Count == MaxParts) break;
        }
        return parts.Count > 0 ? new PoseGuide { Unknown = guide.Unknown, Parts = parts } : null;
    }

    private static string Named(string? name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.Length <= MaxName ? trimmed : trimmed[..MaxName];
    }

    private static bool Usable(PosePoint p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) <= MaxCoordinate && Math.Abs(p.Y) <= MaxCoordinate;
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
