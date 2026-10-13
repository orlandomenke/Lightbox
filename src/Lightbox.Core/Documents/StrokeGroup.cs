using System.Text.Json;
using System.Text.Json.Serialization;
using Lightbox.Core.Effects;

namespace Lightbox.Core.Documents;

/// <summary>
/// What a <see cref="ToolKind.Group"/> stroke holds: strokes of its own, and
/// how their result lands on the drawing they are part of.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a layer's drawing, kept as strokes inside another drawing.</b>
/// Merging a layer down used to flatten a drawing to pixels whenever something
/// about the upper layer read what was under it or was applied when the layers
/// were combined — an eraser, a blend mode, a mask. Every one of those is
/// computed from the two drawings alone, so the computation can be kept and run
/// when the drawing is rendered instead of once when it was merged. That is
/// this record: the upper drawing's strokes, and the layer attributes the merge
/// would otherwise have had to bake. <c>docs/DESIGN-stroke-groups.md</c> has
/// the reasoning and the table of what a merge writes.
/// </para>
/// <para>
/// <b>Everything but the strokes is absent unless it says something.</b> Null
/// blend is Normal, null opacity is 1, null clip is unclipped, and none of them
/// is written — the camera's rule, and the reason they are nullable rather than
/// defaulted: the serializer only omits nulls.
/// </para>
/// </remarks>
public sealed class StrokeGroup
{
    /// <summary>
    /// Keys a newer build wrote that this one does not know, carried through
    /// untouched so a save here does not drop them (Q230).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The group's own record, in order. May contain groups.</summary>
    public List<Stroke> Strokes { get; set; } = [];

    /// <summary>
    /// Pixels under the group's strokes, as bare base64 of a PNG — present only
    /// when the drawing it came from already had a baseline (imported pixels).
    /// A merge never creates one.
    /// </summary>
    public string? PngBase64 { get; set; }

    /// <summary>How the group's result lands on what is under it. Null is Normal.</summary>
    public LayerBlendMode? Blend { get; set; }

    /// <summary>0..1. Null is fully opaque.</summary>
    public double? Opacity { get; set; }

    /// <summary>
    /// Land only where there is already paint under the group — a layer that
    /// was clipped to the one it merged into. Null is unclipped.
    /// </summary>
    public bool? Clip { get; set; }

    /// <summary>A mask carved out of (or, inverted, into) the group's result.</summary>
    public LayerMask? Mask { get; set; }

    /// <summary>The effect stack the layer had, applied to the group's result per frame.</summary>
    public EffectStack? Effects { get; set; }

    /// <summary>A copy that shares nothing: children, mask and effects are all copied.</summary>
    /// <param name="newIds">
    /// Fresh ids for the children, as <see cref="Stroke.Clone"/> — true for a
    /// duplicated drawing, false for an undo snapshot, where ids key caches
    /// that must still mean the same strokes.
    /// </param>
    public StrokeGroup Clone(bool newIds = true)
    {
        var copy = (StrokeGroup)MemberwiseClone();
        copy.Strokes = Strokes.Select(s => s.Clone(newIds)).ToList();
        copy.Mask = Mask?.Clone(newIds ? null : f => f.Clone());
        copy.Effects = Effects?.Clone();
        return copy;
    }
}

/// <summary>Questions about groups asked of a stroke list from more than one place.</summary>
public static class StrokeGroups
{
    /// <summary>
    /// Every mark in the list, looking through groups: the strokes that are not
    /// groups, in the order they are drawn. A group is not itself a mark.
    /// </summary>
    public static IEnumerable<Stroke> Flatten(IEnumerable<Stroke> strokes)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Group is { } group)
            {
                foreach (var inner in Flatten(group.Strokes)) yield return inner;
            }
            else
            {
                yield return stroke;
            }
        }
    }

    /// <summary>Does the list hold a group anywhere, at any depth — including inside a group's mask?</summary>
    public static bool Any(IEnumerable<Stroke> strokes) =>
        strokes.Any(s => s.Group is not null);

    /// <summary>
    /// Hand every piece of stored pixels inside the list's groups — a group's
    /// baseline, its mask's — to <paramref name="resample"/>, which rewrites
    /// the holder's <see cref="Frame.PngBase64"/> in place.
    /// </summary>
    /// <remarks>
    /// A whole-drawing transform moves strokes in Core and resamples the
    /// drawing's baseline where Skia is. A group's pixels are the same kind of
    /// thing one level down, and the caller already has a routine that takes a
    /// frame — so each is lent a frame to be resampled in, rather than every
    /// caller learning a second shape.
    /// </remarks>
    public static void ResamplePixels(IEnumerable<Stroke> strokes, Action<Frame> resample)
    {
        foreach (var stroke in strokes)
        {
            if (stroke.Group is not { } group) continue;
            if (!string.IsNullOrEmpty(group.PngBase64))
            {
                var holder = new Frame { PngBase64 = group.PngBase64 };
                resample(holder);
                group.PngBase64 = holder.PngBase64;
            }
            ResamplePixels(group.Strokes, resample);
            if (group.Mask is { } mask)
            {
                if (!string.IsNullOrEmpty(mask.Frame.PngBase64)) resample(mask.Frame);
                ResamplePixels(mask.Frame.Strokes, resample);
            }
        }
    }

    /// <summary>Does this drawing hold a group anywhere?</summary>
    public static bool Any(Frame? frame) => frame is not null && Any(frame.Strokes);

    /// <summary>
    /// Does the document hold a group anywhere a drawing can be — a layer's
    /// cels, a layer's mask? Decides the version a document is written with:
    /// only a document that contains one needs a build that can read one.
    /// </summary>
    public static bool Any(Doc doc) =>
        Any(doc.Scene.Layers)
        || (doc.Symbols?.Values.Any(s => Any(s.Layers)) ?? false)
        || doc.ReferenceSheets.Any(sheet => sheet.Views.Any(view => Any(view.Layers)));

    /// <summary>A layer holds drawings in its cels and in its mask.</summary>
    private static bool Any(IEnumerable<Layer> layers) =>
        layers.Any(layer => layer.Cels.Any(c => Any(c.Frame)) || Any(layer.Mask?.Frame));
}
