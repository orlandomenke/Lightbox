using System.Globalization;

namespace Lightbox.Import.Unity;

/// <summary>A reference to one sprite: the texture's guid and the sprite's id inside it.</summary>
public readonly record struct UnitySpriteRef(string Guid, long FileId)
{
    /// <summary>The id Unity gives the only sprite of a Single-mode texture.</summary>
    public const long SingleSpriteFileId = 21300000;

    public override string ToString() => $"{Guid}:{FileId.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>A sprite swap at a time in seconds. A null sprite is a blank frame.</summary>
public sealed record UnitySpriteKey(double Time, UnitySpriteRef? Sprite);

/// <summary>
/// The sprite keys driving one renderer. <see cref="Path"/> is the renderer's
/// path below the animated object; empty means the object itself.
/// </summary>
/// <see cref="ClassId"/> tells two components on one path apart — a sprite
/// renderer (212) and a sprite mask on the same object are two tracks.
public sealed record UnitySpriteTrack(string Path, IReadOnlyList<UnitySpriteKey> Keys, int ClassId = 212);

/// <summary>A sprite-swap clip as read from an <c>.anim</c> file.</summary>
/// <param name="Skipped">
/// What the clip animates besides sprites — transform curves, float curves,
/// events — named so the artist knows it exists. Lightbox never writes the
/// <c>.anim</c>, so all of it survives untouched.
/// </param>
public sealed record UnityClip(
    string Name,
    double SampleRate,
    bool Loop,
    double StartTime,
    double StopTime,
    IReadOnlyList<UnitySpriteTrack> Tracks,
    IReadOnlyList<string> Skipped);

/// <summary>
/// Reads the sprite-swap part of a Unity <c>AnimationClip</c> (class 74).
/// </summary>
public static class UnityAnimReader
{
    /// <summary>Keys on one track above this are refused (S1).</summary>
    public const int MaxKeysPerTrack = 100_000;

    /// <summary>Renderer tracks above this are refused (S1).</summary>
    public const int MaxTracks = 256;

    private const int AnimationClipClassId = 74;

    /// <summary>
    /// The clip in <paramref name="text"/>. A clip with no sprite curves comes back
    /// with no tracks rather than being refused: it is an animation, just not one
    /// Lightbox can draw.
    /// </summary>
    /// <exception cref="FormatException">Naming <paramref name="label"/>.</exception>
    public static UnityClip Read(string text, string label)
    {
        var docs = UnityYaml.Parse(text, label);
        var clip = docs.FirstOrDefault(d => d.ClassId == AnimationClipClassId || d.Root["AnimationClip"] is not null)
            ?.Root["AnimationClip"]
            ?? throw new FormatException($"{label}: not an animation clip.");

        var name = clip["m_Name"]?.Text ?? "";
        if (clip["m_SampleRate"] is not { } rateNode || !rateNode.TryDouble(out var rate) || rate <= 0 || rate > 1000)
            throw new FormatException($"{label}: m_SampleRate is missing or not a frame rate.");

        var settings = clip["m_AnimationClipSettings"];
        var loop = settings?["m_LoopTime"]?.Text == "1";
        var start = ReadTime(settings?["m_StartTime"], label, "m_StartTime") ?? 0;
        var stop = ReadTime(settings?["m_StopTime"], label, "m_StopTime") ?? 0;

        var tracks = new List<UnitySpriteTrack>();
        var skipped = new List<string>();

        if (clip["m_PPtrCurves"] is { Kind: UnityYamlNode.NodeKind.Sequence } curves)
        {
            foreach (var curve in curves.Items)
            {
                var attribute = curve["attribute"]?.Text ?? "";
                var path = curve["path"]?.Text ?? "";
                if (attribute != "m_Sprite")
                {
                    skipped.Add($"object curve '{attribute}' on '{PathLabel(path)}'");
                    continue;
                }
                if (tracks.Count >= MaxTracks)
                    throw new FormatException($"{label}: more than {MaxTracks} sprite tracks.");
                var classId = curve["classID"] is { } cn && cn.TryLong(out var cv) ? (int)cv : 212;
                var keys = ReadKeys(curve["curve"], label, path, skipped);
                if (keys.Count == 0)
                {
                    skipped.Add($"a sprite curve on '{PathLabel(path)}' with no keys");
                    continue;
                }
                tracks.Add(new UnitySpriteTrack(path, keys, classId));
            }
        }

        NoteCurves(clip, "m_PositionCurves", "position", skipped);
        NoteCurves(clip, "m_RotationCurves", "rotation", skipped);
        NoteCurves(clip, "m_CompressedRotationCurves", "compressed rotation", skipped);
        NoteCurves(clip, "m_EulerCurves", "rotation", skipped);
        NoteCurves(clip, "m_ScaleCurves", "scale", skipped);
        NoteCurves(clip, "m_FloatCurves", "float", skipped);
        if (clip["m_Events"] is { Kind: UnityYamlNode.NodeKind.Sequence, Items.Count: > 0 } events)
            skipped.Add($"{events.Items.Count} animation event(s)");

        return new UnityClip(name, rate, loop, start, stop,
            tracks, skipped);
    }

    private static string PathLabel(string path) => path.Length == 0 ? "(root)" : path;

    private static double? ReadTime(UnityYamlNode? node, string label, string key)
    {
        if (node is null || node.IsEmpty) return null;
        if (!node.TryDouble(out var t) || t < 0 || t > 1e6)
            throw new FormatException($"{label}: {key} is not a time.");
        return t;
    }

    private static List<UnitySpriteKey> ReadKeys(UnityYamlNode? curve, string label, string path, List<string> skipped)
    {
        var keys = new List<UnitySpriteKey>();
        if (curve is not { Kind: UnityYamlNode.NodeKind.Sequence }) return keys;
        if (curve.Items.Count > MaxKeysPerTrack)
            throw new FormatException($"{label}: more than {MaxKeysPerTrack} keys on '{PathLabel(path)}'.");

        foreach (var key in curve.Items)
        {
            if (key["time"] is not { } tn || !tn.TryDouble(out var time) || time < 0 || time > 1e6)
                throw new FormatException($"{label}: a key on '{PathLabel(path)}' has no usable time.");
            var sprite = ReadRef(key["value"]);
            // fileID 0 is Unity's own "no sprite"; anything else unreadable is a
            // broken reference, and a blank frame from one has to say so.
            if (sprite is null && key["value"]?["fileID"] is { } f && f.Text is not ("" or "0"))
                skipped.Add(FormattableString.Invariant($"a key at {time:0.####}s on '{PathLabel(path)}' points at a sprite that cannot be read; it shows as a blank frame"));
            keys.Add(new UnitySpriteKey(time, sprite));
        }
        // Stable: two keys at one time keep their file order, and the later wins.
        return keys.OrderBy(k => k.Time).ToList();
    }

    /// <summary>
    /// <c>{fileID: n, guid: g, type: 3}</c> as a sprite reference, or null for an
    /// empty one (<c>fileID: 0</c>, or no guid).
    /// </summary>
    public static UnitySpriteRef? ReadRef(UnityYamlNode? value)
    {
        if (value is null) return null;
        var guid = value["guid"]?.Text ?? "";
        if (value["fileID"] is not { } fid || !fid.TryLong(out var fileId) || fileId == 0) return null;
        if (!IsGuid(guid)) return null;
        return new UnitySpriteRef(guid.ToLowerInvariant(), fileId);
    }

    internal static bool IsGuid(string s) =>
        s.Length == 32 && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void NoteCurves(UnityYamlNode clip, string key, string what, List<string> skipped)
    {
        if (clip[key] is { Kind: UnityYamlNode.NodeKind.Sequence, Items.Count: > 0 } c)
            skipped.Add($"{c.Items.Count} {what} curve(s)");
    }
}

/// <summary>One drawing held for <see cref="Length"/> frames, from <see cref="Start"/>.</summary>
public sealed record UnityExposure(int Start, int Length, UnitySpriteRef? Sprite);

/// <summary>A track's keys as exposures, and every key the frame grid moved.</summary>
public sealed record UnityExposureSheet(IReadOnlyList<UnityExposure> Exposures, IReadOnlyList<string> Notes)
{
    public int FrameCount => Exposures.Count == 0 ? 0 : Exposures[^1].Start + Exposures[^1].Length;
}

/// <summary>Turns a clip's timed keys into frame exposures on its sample-rate grid.</summary>
public static class UnityClipTiming
{
    private const double OnGrid = 1e-3;

    /// <summary>
    /// The exposures of <paramref name="track"/>. A key holds until the next one;
    /// the last holds until the clip's stop time, and for at least a frame.
    /// </summary>
    /// <remarks>
    /// A key off the grid is rounded to the nearest frame and named in the notes.
    /// Two keys landing on one frame keep the later one — it is the one Unity
    /// shows for that frame — and the earlier is named as dropped.
    /// </remarks>
    public static UnityExposureSheet Exposures(UnitySpriteTrack track, double sampleRate, double stopTime)
    {
        var notes = new List<string>();
        var placed = new List<(int Frame, UnitySpriteRef? Sprite)>();

        foreach (var key in track.Keys)
        {
            var exact = key.Time * sampleRate;
            var frame = (int)Math.Round(exact, MidpointRounding.AwayFromZero);
            if (Math.Abs(exact - frame) > OnGrid)
                notes.Add(FormattableString.Invariant($"key at {key.Time:0.####}s is between frames; placed on frame {frame + 1}"));
            if (placed.Count > 0 && placed[^1].Frame == frame)
            {
                notes.Add(FormattableString.Invariant($"two keys on frame {frame + 1}; the later one is kept"));
                placed[^1] = (frame, key.Sprite);
                continue;
            }
            placed.Add((frame, key.Sprite));
        }

        var exposures = new List<UnityExposure>(placed.Count);
        var stopFrame = (int)Math.Round(stopTime * sampleRate, MidpointRounding.AwayFromZero);
        if (placed.Count > 0 && stopFrame <= placed[^1].Frame)
            notes.Add(FormattableString.Invariant($"the clip stops before its last key; that key is held for one frame"));
        for (var k = 0; k < placed.Count; k++)
        {
            var end = k + 1 < placed.Count ? placed[k + 1].Frame : Math.Max(stopFrame, placed[k].Frame + 1);
            exposures.Add(new UnityExposure(placed[k].Frame, end - placed[k].Frame, placed[k].Sprite));
        }
        return new UnityExposureSheet(exposures, notes);
    }
}
