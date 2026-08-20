namespace Lightbox.Core.Documents;

/// <summary>
/// Move a whole drawing by a document-space offset: every stroke, every hole,
/// every placed anchor, symbol and shape box. The spacing assistant's nudge is
/// the caller — re-spacing a drawing means moving the drawing, not one line.
/// </summary>
/// <remarks>
/// <para>
/// The coordinate walk is <see cref="ImageResize"/>'s, restated for a
/// translation: stroke points, holes, bind-pose seed points, the authored
/// path's nodes (handles are offsets from their node, so a translation cannot
/// change the curve's shape), baked-sample offsets, placed anchors, symbol
/// placements and shape boxes. Brush size and texture scale are untouched —
/// nothing changes size under a move.
/// </para>
/// <para>
/// <b>A drawing with a pixel baseline is refused whole.</b> The baseline is a
/// canvas-sized raster with no offset of its own; shifting it means resampling
/// pixels, and moving the strokes while the baseline stays put tears the
/// drawing in two. Refusing is honest where a half-move would look like
/// corruption.
/// </para>
/// <para>
/// Baked sample offsets are integers, so a fractional move would put a bake up
/// to half a pixel out of register with the stroke it froze. Callers that care
/// (the nudge does) round the offset to whole pixels when the frame carries a
/// bake; <see cref="HasBakedRaster"/> is how they ask.
/// </para>
/// <para>
/// <b>A stroke's clip region does not travel</b>, and cannot from here: a
/// <see cref="Stroke.ClipId"/> names a content-hashed entry in
/// <c>Doc.ClipRegions</c>, shareable between strokes and frames, and this
/// method only has the frame. A caller moving a drawing that contains a
/// clip-limited stroke must refuse (the nudge does, via
/// <see cref="HasClippedStrokes"/>) or re-author the clip itself — silently
/// moving the ink out from under its mask is the failure this note exists to
/// prevent.
/// </para>
/// </remarks>
public static class FrameTranslate
{
    /// <summary>Whether any stroke carries a baked raster, so a caller can round the offset to whole pixels first.</summary>
    public static bool HasBakedRaster(Frame frame) =>
        frame.Strokes.Any(s => s.Baked is { PngBase64.Length: > 0 });

    /// <summary>Whether any stroke is clip-limited, so a caller can refuse rather than move ink out from under its mask.</summary>
    public static bool HasClippedStrokes(Frame frame) =>
        frame.Strokes.Any(s => s.ClipId is not null);

    /// <summary>
    /// Translate the drawing in place. False when the frame has a pixel
    /// baseline, in which case nothing is touched.
    /// </summary>
    public static bool Apply(Frame frame, double dx, double dy)
    {
        if (frame.HasBaseline) return false;
        if (dx == 0 && dy == 0) return true;

        foreach (var stroke in frame.Strokes)
        {
            stroke.Points = [.. stroke.Points.Select(p => p with { X = p.X + dx, Y = p.Y + dy })];
            stroke.Holes = stroke.Holes?
                .Select(hole => hole.Select(p => p with { X = p.X + dx, Y = p.Y + dy }).ToList())
                .ToList();
            if (stroke.RestPoints is { } rest)
                stroke.RestPoints = [.. rest.Select(p => p with { X = p.X + dx, Y = p.Y + dy })];
            if (stroke.Path is { } path)
            {
                for (var i = 0; i < path.Nodes.Count; i++)
                {
                    var n = path.Nodes[i];
                    path.Nodes[i] = n with { X = n.X + dx, Y = n.Y + dy };
                }
            }
            if (stroke.Baked is { PngBase64.Length: > 0 } baked)
            {
                baked.X = (int)Math.Round(baked.X + dx);
                baked.Y = (int)Math.Round(baked.Y + dy);
            }
        }

        if (frame.Placements is { } placements)
            foreach (var placement in placements)
            {
                placement.X += dx;
                placement.Y += dy;
            }

        if (frame.Anchors is { } anchors)
            foreach (var id in anchors.Keys.ToList())
                anchors[id] = new AnchorPoint(anchors[id].X + dx, anchors[id].Y + dy);

        if (frame.Shapes is { } shapes)
            foreach (var id in shapes.Keys.ToList())
            {
                var box = shapes[id];
                shapes[id] = box with { X = box.X + dx, Y = box.Y + dy };
            }

        return true;
    }
}
