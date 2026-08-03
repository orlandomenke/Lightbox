using Lightbox.Core.Timeline;

namespace Lightbox.Core.Documents;

/// <summary>
/// Declaring collision shapes, and sizing them across a range of drawings.
/// </summary>
/// <remarks>
/// <para>
/// P5c, and deliberately the same six operations as <see cref="Anchors"/> — a
/// hitbox and a socket are the same kind of authored, per-drawing, non-pixel data
/// and only differ in what they carry. Keeping the shapes identical means the
/// canvas overlay that eventually places one places the other, and the exporter
/// resolves both the same way through holds.
/// </para>
/// <para>
/// The operations are here rather than on the model so they can be wrapped in one
/// undo step by the editor and tested without one.
/// </para>
/// </remarks>
public static class CollisionShapes
{
    /// <summary>Declare a named shape on the document. Returns the new one.</summary>
    public static CollisionShape Declare(Scene scene, string name, ShapeRole role = ShapeRole.Hurtbox)
    {
        var shape = new CollisionShape
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Body" : name.Trim(),
            Role = role,
        };
        (scene.Shapes ??= []).Add(shape);
        return shape;
    }

    /// <summary>
    /// Remove a shape and every rectangle of it.
    /// </summary>
    /// <remarks>
    /// Deleting the declaration has to clear the rectangles too, or the drawings
    /// keep entries nothing can name — invisible, exported, and impossible to
    /// find. Back to null when the last one goes, so removing them all leaves the
    /// document as it was before any existed.
    /// </remarks>
    public static bool Remove(Scene scene, string shapeId)
    {
        if (scene.Shapes?.RemoveAll(s => s.Id == shapeId) is not > 0) return false;
        if (scene.Shapes.Count == 0) scene.Shapes = null;

        foreach (var layer in scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame?.Shapes is not { } shapes) continue;
                shapes.Remove(shapeId);
                if (shapes.Count == 0) cel.Frame.Shapes = null;
            }
        }
        return true;
    }

    /// <summary>A shape's rectangle on one drawing, or null if it is not placed there.</summary>
    public static ShapeBox? At(Frame frame, string shapeId) =>
        frame.Shapes is { } shapes && shapes.TryGetValue(shapeId, out var box) ? box : null;

    /// <summary>
    /// Put the same rectangle on every drawing a cel range exposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Works on <b>drawings</b> rather than cels, like
    /// <see cref="Anchors.SetAcross"/>: a range on 2s holds each drawing twice, and
    /// setting the same rectangle twice on one drawing is the same edit done twice.
    /// </para>
    /// <para>
    /// One rectangle for the whole range rather than an interpolation, and here
    /// that is the *common* case rather than a simplification: a physics body is
    /// meant to be the same box for the whole clip, and a hurtbox that tracks the
    /// drawing is authored frame by frame anyway.
    /// </para>
    /// </remarks>
    /// <returns>Drawings changed.</returns>
    public static int SetAcross(Layer layer, int start, int count, string shapeId, ShapeBox box)
    {
        if (layer.Cels.Count == 0 || count < 1) return 0;
        start = Math.Clamp(start, 0, layer.Cels.Count - 1);
        var end = Math.Min(layer.Cels.Count, start + count);

        var touched = new HashSet<string>(StringComparer.Ordinal);
        var changed = 0;
        for (var i = start; i < end; i++)
        {
            // Through the exposure, so a hold sizes the drawing it is holding
            // rather than nothing at all.
            if (ExposureSheet.ExposedFrame(layer, i) is not { } frame) continue;
            if (!touched.Add(frame.Id)) continue;

            (frame.Shapes ??= [])[shapeId] = box;
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Clear a shape from the drawings a range exposes.
    /// </summary>
    /// <remarks>
    /// The other half of how a hitbox becomes active for three frames only: place
    /// it across the contact frames, clear it across the rest. Absence is the
    /// off state, so there is no flag that can disagree with the rectangle.
    /// </remarks>
    public static int ClearAcross(Layer layer, int start, int count, string shapeId)
    {
        if (layer.Cels.Count == 0 || count < 1) return 0;
        start = Math.Clamp(start, 0, layer.Cels.Count - 1);
        var end = Math.Min(layer.Cels.Count, start + count);

        var touched = new HashSet<string>(StringComparer.Ordinal);
        var changed = 0;
        for (var i = start; i < end; i++)
        {
            if (ExposureSheet.ExposedFrame(layer, i) is not { Shapes: { } shapes } frame) continue;
            if (!touched.Add(frame.Id)) continue;
            if (!shapes.Remove(shapeId)) continue;

            // Back to null when the last one goes, so a drawing that ends up with
            // no shapes writes no key.
            if (shapes.Count == 0) frame.Shapes = null;
            changed++;
        }
        return changed;
    }

    /// <summary>
    /// Every shape rectangle that applies at a timeline index, resolved through holds.
    /// </summary>
    /// <remarks>
    /// What the exporter asks for, once per exported frame. Resolving through the
    /// exposure matters for the same reason it does for anchors: on 2s the second
    /// cel is a hold, and a hurtbox that vanished on every other frame would make
    /// the character invulnerable half the time — a gameplay bug with no visible
    /// cause in the drawing.
    /// </remarks>
    public static Dictionary<string, ShapeBox> ResolvedAt(Scene scene, int index)
    {
        var found = new Dictionary<string, ShapeBox>(StringComparer.Ordinal);
        if (scene.Shapes is null) return found;

        // Bottom to top, so a shape placed on an upper layer wins — the same
        // precedence the layer stack already means everywhere else.
        foreach (var layer in scene.Layers)
        {
            if (ExposureSheet.ExposedFrame(layer, index)?.Shapes is not { } shapes) continue;
            foreach (var (id, box) in shapes) found[id] = box;
        }
        return found;
    }
}
