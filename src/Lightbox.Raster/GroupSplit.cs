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
    /// <summary>
    /// Why <paramref name="target"/> cannot be split out of <paramref name="layer"/>,
    /// in words for the status line — or null when it can. Asked before the
    /// split rather than discovered inside it, so a refusal records no undo step.
    /// </summary>
    public static string? WhyNot(Scene scene, Layer layer, Stroke target)
    {
        if (target.Group is not { } chosen) return "That is not a merged group.";
        if (!scene.Layers.Contains(layer)) return "That layer is not in this scene.";
        // The merged layer's own drawing, wrapped for its mask or effects so
        // they kept applying to it alone. What was merged into it lies on top
        // of it, so split above it would be painted in the wrong order.
        if (chosen.From == layer.Id)
        {
            return $"That group is “{layer.Name}”'s own drawing, kept with its mask or effects — there is no other layer to give it back to.";
        }
        var found = Found(layer, target);
        if (!found.Any(f => ReferenceEquals(f.Frame.Strokes[f.Position], target)))
        {
            return "That group is not the topmost one from its merge in this drawing, or not at the top of the drawing at all. Split the one above it first.";
        }
        var groups = found.Select(f => f.Frame.Strokes[f.Position].Group!).ToList();
        var first = groups[0];
        if (groups.Any(g => g.Blend != first.Blend || g.Opacity != first.Opacity || g.Clip != first.Clip))
        {
            return "Its drawings land with different blend modes, opacities or clips, so no one layer can take them all back.";
        }
        return null;
    }

    /// <summary>
    /// Every cel's group from the same merge as <paramref name="target"/>, at
    /// the top of its drawing — the last one, if a drawing holds two. A group
    /// with no record of its layer (written before groups kept one) is a merge
    /// of one: nothing says any other group came with it.
    /// </summary>
    private static List<(int Cel, Frame Frame, int Position)> Found(Layer layer, Stroke target)
    {
        var from = target.Group!.From;
        var found = new List<(int Cel, Frame Frame, int Position)>();
        for (var cel = 0; cel < layer.Cels.Count; cel++)
        {
            if (layer.Cels[cel].Frame is not { } frame) continue;
            for (var i = frame.Strokes.Count - 1; i >= 0; i--)
            {
                var entry = frame.Strokes[i];
                if (entry.Group is not { } group) continue;
                if (from is null ? !ReferenceEquals(entry, target) : group.From != from) continue;
                found.Add((cel, frame, i));
                break;
            }
        }
        return found;
    }

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
        if (WhyNot(scene, layer, target) is not null) return null;
        var chosen = target.Group!;
        var at = scene.Layers.IndexOf(layer);
        var info = new SKImageInfo(scene.Width, scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var found = Found(layer, target);
        var groups = found.Select(f => f.Frame.Strokes[f.Position].Group!).ToList();
        var first = groups[0];
        // The mask and the stack go on the layer when every copy agrees, and
        // stay with each drawing when one was moved or changed on its own.
        var liftMask = groups.All(g => ContentEquality.Same(g.Mask, first.Mask));
        var liftEffects = groups.All(g => ContentEquality.Same(g.Effects, first.Effects));

        var split = new Layer
        {
            Name = first.Name ?? "Split group",
            Kind = layer.Kind,
            // In the merged layer's folder: inserted inside its span and
            // belonging to none, it would escape the folder's blend, opacity,
            // mask and visibility (the adversary).
            GroupId = layer.GroupId,
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
            // What the drawing said about itself goes with it: the model's
            // marking above all, or its output would pass as hand-drawn (A6).
            // The merge kept one marking for the pair; split, both keep it,
            // which errs toward saying so.
            var drawing = new Frame
            {
                Role = frame.Role,
                Ai = frame.Ai,
                Chart = frame.Chart is null ? null : [.. frame.Chart],
            };
            var keepWrapped = !liftMask && group.Mask is not null
                || !liftEffects && group.Effects is not null
                || entry.ClipId is not null
                // A newer build's keys on the group may say how it lands, and
                // unwrapping would drop them (Q230): it stays whole.
                || entry.Unknown is not null || group.Unknown is not null;
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
