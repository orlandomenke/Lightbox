using System.Text.Json.Serialization;
using Lightbox.Core.Documents;

namespace Lightbox.Core.Effects;

/// <summary>
/// One effect in a stack: a kind resolved through the registry at render
/// time, and its parameters — each an <see cref="EffectParam"/>, the keyed
/// scalar the wind elements already share (Q122), so a camera move, a gust
/// and an animated blur are described in one vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Kind"/> is a string id</b> (<c>"blur.gaussian"</c>,
/// <c>"grade.levels"</c>), resolved through the Raster registry. An unknown
/// kind — a document from a newer build — is preserved on save and rendered
/// as identity, flagged in the UI, never dropped. Forward compatibility is
/// what a string buys over an enum; the brush tip registry made this trade
/// the same way.
/// </para>
/// <para>
/// <b><see cref="Disabled"/> rather than an <c>Enabled</c> defaulting to
/// true</b>, deviating from the design sketch for the serialization rule the
/// sketch itself cites: a plain <c>"enabled": true</c> on every use of every
/// document is the medium-block mistake again. Null — and absent — is the
/// ordinary, running state.
/// </para>
/// </remarks>
public sealed class EffectUse
{
    public string Id { get; set; } = Ids.NewId("fx");

    public string Kind { get; set; } = "";

    /// <summary>Switched off without losing its settings. Null — and absent — while it runs.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Whether this use currently applies. Derived; never serialized.</summary>
    [JsonIgnore] public bool Applies => Disabled != true;

    public Dictionary<string, EffectParam> Params { get; set; } = [];

    /// <summary>
    /// The value of <paramref name="key"/> at <paramref name="frame"/>, or
    /// <paramref name="fallback"/> when this use never authored it — which is
    /// how a document from an older build reads under a definition that grew
    /// a parameter.
    /// </summary>
    public double At(string key, int frame, double fallback) =>
        Params.TryGetValue(key, out var param) ? param.At(frame) : fallback;

    /// <summary>A copy holding no reference in common with this one.</summary>
    public EffectUse Clone()
    {
        var copy = (EffectUse)MemberwiseClone();
        copy.Params = Params.ToDictionary(p => p.Key, p => p.Value.Clone());
        return copy;
    }
}

/// <summary>
/// An ordered chain of effects — the degenerate node graph, one image in and
/// one out per link, pure and deterministic, which is what lets the future
/// node system subsume it instead of replacing it (DESIGN-effects.md).
/// </summary>
public sealed class EffectStack
{
    public List<EffectUse> Uses { get; set; } = [];

    /// <summary>Whether anything in the stack currently applies. Derived; never serialized.</summary>
    [JsonIgnore] public bool AppliesAnything => Uses.Exists(u => u.Applies);

    /// <summary>A copy holding no reference in common with this one.</summary>
    public EffectStack Clone()
    {
        var copy = (EffectStack)MemberwiseClone();
        copy.Uses = Uses.Select(u => u.Clone()).ToList();
        return copy;
    }
}
