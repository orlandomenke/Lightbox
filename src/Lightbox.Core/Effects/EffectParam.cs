using Lightbox.Core.Inbetween;

namespace Lightbox.Core.Effects;

/// <summary>One value at one frame, and how it eases into the next.</summary>
public sealed class EffectKey
{
    public int Frame { get; set; }

    public double Value { get; set; }

    /// <summary>
    /// How this key eases into the NEXT one — the same easing vocabulary the
    /// inbetweener and the camera already use, so a camera move, a drawing
    /// inbetween and a gust of wind are described one way.
    /// </summary>
    public Easing Ease { get; set; } = Easing.EaseInOut;

    public EffectKey Clone() => (EffectKey)MemberwiseClone();
}

/// <summary>
/// A number that may be a constant or may change over time.
/// </summary>
/// <remarks>
/// <para>
/// <b>The vocabulary <c>docs/DESIGN-effects.md</c> specified, built here because
/// wind is its first user</b> (Q122). That design's argument is the reason this
/// is shared rather than a keyed number local to effects elements: a second
/// keying vocabulary means a second timeline editor, "which is how a node system
/// ships twice". Being the first user, this branch shapes it — so it is
/// deliberately the smallest thing that serves a keyed scalar and nothing more.
/// </para>
/// <para>
/// <b>Absent unless animated.</b> <see cref="Keys"/> is null on a constant, so an
/// unkeyed parameter serializes as <c>{"value": 4}</c> and nothing else. A
/// document that never keys anything is byte-identical to one from before this
/// existed.
/// </para>
/// <para>
/// <b>Holds outside its authored range</b>, the same rule <c>CameraOps.At</c>
/// follows: before the first key it reads the first key, after the last it reads
/// the last. An effect does not snap back to a default because the timeline ran
/// past where somebody stopped keying.
/// </para>
/// </remarks>
public sealed class EffectParam
{
    public EffectParam() { }

    public EffectParam(double value) => Value = value;

    /// <summary>The value when nothing is keyed, and the value the first key replaces.</summary>
    public double Value { get; set; }

    /// <summary>Keys in frame order, or null for a constant.</summary>
    public List<EffectKey>? Keys { get; set; }

    /// <summary>Whether this parameter changes over time. Derived; never serialized.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsAnimated => Keys is { Count: > 0 };

    /// <summary>
    /// What this parameter is worth on a frame.
    /// </summary>
    /// <remarks>
    /// Keys are read in list order and the first bracketing pair wins, so an
    /// out-of-order list resolves predictably rather than throwing — a record
    /// arriving from an agent or a hand edit should not be able to make a
    /// document unopenable.
    /// </remarks>
    public double At(int frame)
    {
        if (Keys is not { Count: > 0 } keys) return Value;
        if (keys.Count == 1) return keys[0].Value;

        EffectKey? before = null;
        EffectKey? after = null;

        foreach (var key in keys)
        {
            if (key.Frame <= frame && (before is null || key.Frame >= before.Frame)) before = key;
            if (key.Frame >= frame && (after is null || key.Frame <= after.Frame)) after = key;
        }

        if (before is null) return after!.Value;
        if (after is null || after.Frame == before.Frame) return before.Value;

        var t = (frame - before.Frame) / (double)(after.Frame - before.Frame);
        return before.Value + (after.Value - before.Value) * EasingOps.Ease(t, before.Ease);
    }

    public EffectParam Clone() => new()
    {
        Value = Value,
        Keys = Keys?.Select(k => k.Clone()).ToList(),
    };
}
