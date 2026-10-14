using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Split group: a merge taken back out (Q268). Every cel's group that came from
/// one merged layer goes onto a new layer directly above, carrying what that
/// layer did — and the two layers composite to what the merged drawing showed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole merge, every cel.</b> A merge leaves a group in every cel where
/// the upper layer had a drawing, each remembering the layer it came from
/// (<see cref="StrokeGroup.From"/>). Split takes all of them, so one command
/// undoes one merge without undo — and without losing what was drawn since.
/// </para>
/// <para>
/// <b>Exact, until something was drawn over it.</b> The group's blend, opacity
/// and clip become the new layer's, which is what they were before the merge.
/// Its mask and effects do too when every cel's copy agrees; when a cel's copy
/// was moved on its own, each drawing keeps its own inside a group on the new
/// layer, which is still the same picture. Marks drawn on the merged layer after
/// the merge stay there (the owner's choice): an eraser among them that reached
/// the group is copied onto the new layer so what was rubbed out stays rubbed
/// out; a brush mark that overlapped the group now sits under it, and the
/// result says how many.
/// </para>
/// </remarks>
public static class GroupSplit
{
    /// <summary>What a split made, for the status line and the tests.</summary>
    /// <param name="Layer">The new layer, already in the scene directly above.</param>
    /// <param name="Cels">How many drawings gave a group to it.</param>
    /// <param name="MarksNowUnder">Marks drawn after the merge that overlapped a group and now sit under it.</param>
    public sealed record Result(Layer Layer, int Cels, int MarksNowUnder);

    /// <summary>
    /// Take the merge <paramref name="target"/> belongs to out of
    /// <paramref name="layer"/>, onto a new layer directly above it. Null —
    /// having changed nothing — when <paramref name="target"/> is not a group at
    /// the top of one of the layer's drawings, or the merge's groups disagree
    /// about how they land (a blend, an opacity, a clip), which one layer above
    /// cannot say for all of them.
    /// </summary>
    public static Result? Split(Scene scene, Layer layer, Stroke target)
    {
        if (target.Group is not { } chosen) return null;
        var at = scene.Layers.IndexOf(layer);
        if (at < 0) return null;
        var info = new SKImageInfo(scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        // Every cel's group from the same merge, at the top of its drawing —
        // the last one, if a drawing somehow holds two.
        var found = new List<(int Cel, Frame Frame, int Position)>();
        var targetIsAtTheTop = false;
        for (var cel = 0; cel < layer.Cels.Count; cel++)
        {
            if (layer.Cels[cel].Frame is not { } frame) continue;
            for (var i = frame.Strokes.Count - 1; i >= 0; i--)
            {
                if (frame.Strokes[i].Group is not { } group || group.From != chosen.From) continue;
                found.Add((cel, frame, i));
                targetIsAtTheTop |= ReferenceEquals(frame.Strokes[i], target);
                break;
            }
        }
        if (!targetIsAtTheTop) return null;

        var groups = found.Select(f => f.Frame.Strokes[f.Position].Group!).ToList();
        var first = groups[0];
        if (groups.Any(g => g.Blend != first.Blend || g.Opacity != first.Opacity || g.Clip != first.Clip)) return null;
        // The mask and the stack go on the layer when every copy agrees, and
        // stay with each drawing when one was moved or changed on its own.
        var liftMask = groups.All(g => ContentEquality.Same(g.Mask, first.Mask));
        var liftEffects = groups.All(g => ContentEquality.Same(g.Effects, first.Effects));

        var split = new Layer
        {
            Name = first.Name ?? "Split group",
            Kind = layer.Kind,
            BlendMode = first.Blend ?? LayerBlendMode.Normal,
            Opacity = first.Opacity ?? 1,
            ClipToBelow = first.Clip == true ? true : null,
            Mask = liftMask ? first.Mask?.Clone() : null,
            Effects = liftEffects ? first.Effects?.Clone() : null,
            Cels = [],
        };

        var byCel = found.ToDictionary(f => f.Cel);
        var marksNowUnder = 0;
        for (var cel = 0; cel < layer.Cels.Count; cel++)
        {
            if (layer.Cels[cel].Frame is not { } frame)
            {
                split.Cels.Add(new Cel());   // the merged layer holds here, and so does this one
                continue;
            }
            if (!byCel.TryGetValue(cel, out var hit))
            {
                // The merged drawing changes here and the merged-in layer had
                // nothing: an empty drawing, or the one before would be held.
                split.Cels.Add(new Cel { Frame = new Frame() });
                continue;
            }

            var entry = frame.Strokes[hit.Position];
            var group = entry.Group!;
            var drawing = new Frame { Role = frame.Role };
            var keepWrapped = !liftMask && group.Mask is not null
                || !liftEffects && group.Effects is not null
                || entry.ClipId is not null;
            if (keepWrapped)
            {
                // Its own mask, stack or selection clip stays with it; how it
                // lands is the layer's now, so the group stops saying so.
                var wrapped = entry.Clone(newId: false);
                wrapped.Group!.Blend = null;
                wrapped.Group.Opacity = null;
                wrapped.Group.Clip = null;
                if (liftMask) wrapped.Group.Mask = null;
                if (liftEffects) wrapped.Group.Effects = null;
                drawing.Strokes.Add(wrapped);
            }
            else
            {
                drawing.PngBase64 = group.PngBase64;
                drawing.Strokes.AddRange(group.Strokes);
            }

            // Marks drawn after the merge stay on the merged layer. An eraser
            // that reached the group follows it as well, so what was rubbed out
            // stays rubbed out; anything else that reached it is now under it.
            var reach = StrokeGroupRenderer.Reach(group, info, default);
            for (var later = hit.Position + 1; later < frame.Strokes.Count; later++)
            {
                var mark = frame.Strokes[later];
                if (reach is not { } r || BrushEngine.CommitRegion(mark, info) is not { } m || !r.IntersectsWith(m)) continue;
                if (mark.Tool is ToolKind.Eraser or ToolKind.ClearRegion) drawing.Strokes.Add(mark.Clone());
                else marksNowUnder++;
            }

            frame.Strokes.RemoveAt(hit.Position);
            split.Cels.Add(new Cel { Frame = drawing });
        }

        scene.Layers.Insert(at + 1, split);
        return new Result(split, found.Count, marksNowUnder);
    }
}
