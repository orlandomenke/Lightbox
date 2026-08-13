namespace Lightbox.Core.Documents;

/// <summary>
/// Rescales the two things in a document that are pixels rather than
/// instructions: a frame's imported baseline image and a smudge stroke's
/// baked sample.
/// </summary>
/// <remarks>
/// Declared here and implemented in <c>Lightbox.Raster</c> because
/// <c>Lightbox.Core</c> carries no rendering. It exists so that
/// <see cref="ImageResize"/> can be honest about the parts of the record it
/// cannot scale by arithmetic, rather than leaving them at the old size and
/// producing a document that renders wrong in two specific places.
/// </remarks>
public interface IPixelResampler
{
    /// <summary>
    /// Resample a base64 PNG by the given factors, or return null if it
    /// cannot be decoded — in which case the caller drops it rather than
    /// keeping pixels at the wrong scale.
    /// </summary>
    string? Resample(string pngBase64, double scaleX, double scaleY);
}

/// <summary>
/// Scaling the artwork itself: every coordinate, every brush size, and the
/// paper along with them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the operation that is allowed to change the grain, and
/// <see cref="CanvasResize"/> is the one that is not.</b> The line between
/// them is worth stating because it looks arbitrary and is not: every dab
/// dynamic is seeded from the bits of a dab's position through
/// <c>Hash01</c>, so multiplying coordinates re-rolls scatter, size, flow,
/// roundness, rotation and the three colour jitters everywhere. When the
/// artist rescales the art, that is a change to the art and the mark is
/// allowed to come back different — Q26's finding, that the grain belongs to
/// the canvas. When the artist only changes the size of the paper, nothing
/// about the drawing changed and re-rolling it would be pure loss.
/// </para>
/// <para>
/// <b>Invariant 7 is not what this breaks.</b> That invariant governs
/// <em>rendering</em> the same document larger — "render bigger by scaling
/// the surface, never the geometry" — and the reason is that a 2× render must
/// be a sharper picture of the same mark. An authored rescale is a different
/// request: the artist is asking for a different document, and keeping one
/// document space is what makes a 10 px brush still mean 10 px afterwards. A
/// stored scale factor applied at render time was the alternative, and it was
/// rejected because it makes authoring space and document space diverge
/// permanently for every path that reads pixels.
/// </para>
/// <para>
/// <b>Every coordinate in the record belongs here.</b> Same rule and same
/// failure mode as <see cref="Stroke.Clone"/>: a field added to the document
/// and not added here does not fail a build or a test, it goes quiet and
/// leaves one thing in the wrong place. The list is exhaustive rather than
/// "the ones that matter", and <c>ImageResizeTests</c> asserts against the
/// model's own property list so that adding a coordinate without handling it
/// here is caught.
/// </para>
/// </remarks>
public static class ImageResize
{
    /// <summary>
    /// A non-uniform rescale cannot honestly scale a brush, because a dab has
    /// one diameter and no axes. The geometric mean is the compromise: an
    /// unlinked resize moves the geometry exactly and the marks by the average
    /// of the two factors, so a 2×1 rescale gives correctly-placed strokes
    /// drawn about 1.41× wider rather than strokes that miss their own
    /// outline. Uniform rescales — the default, and what the dialog links by
    /// default — are unaffected, since the mean of two equal factors is the
    /// factor.
    /// </summary>
    private static double MarkScale(double scaleX, double scaleY) =>
        Math.Sqrt(Math.Abs(scaleX) * Math.Abs(scaleY));

    /// <summary>
    /// The scale factors that take a scene to a target size. Public so the
    /// dialog can show them and the preview can use them without editing.
    /// </summary>
    public static (double X, double Y) FactorsFor(Scene scene, int newWidth, int newHeight) =>
        (newWidth / (double)scene.Width, newHeight / (double)scene.Height);

    /// <summary>
    /// Scale the whole document to <paramref name="newWidth"/> ×
    /// <paramref name="newHeight"/>, optionally setting the document's
    /// resolution at the same time.
    /// </summary>
    /// <param name="ppi">
    /// The new pixels-per-inch, or null to leave it alone. Setting it never
    /// resamples anything by itself — it is metadata travelling beside the
    /// pixel size, not a second way of asking for a resize.
    /// </param>
    /// <returns>False, changing nothing, if the request is not a size or asks
    /// for exactly what the document already is.</returns>
    public static bool Apply(
        Doc doc, int newWidth, int newHeight, IPixelResampler resampler, int? ppi = null)
    {
        var scene = doc.Scene;
        if (newWidth < 1 || newHeight < 1) return false;

        var sizeChanged = newWidth != scene.Width || newHeight != scene.Height;
        var ppiChanged = ppi is { } p && p != scene.Ppi && p > 0;
        if (!sizeChanged && !ppiChanged) return false;

        if (ppi is { } newPpi && newPpi > 0) scene.Ppi = newPpi;
        if (!sizeChanged) return true;

        var (sx, sy) = FactorsFor(scene, newWidth, newHeight);
        var mark = MarkScale(sx, sy);

        // The paper. The origin scales with everything else so that a document
        // that was grown leftward keeps its drawing in the same place relative
        // to the paper after a rescale.
        scene.Width = newWidth;
        scene.Height = newHeight;
        scene.OriginX = Scaled(scene.OriginX, sx);
        scene.OriginY = Scaled(scene.OriginY, sy);

        foreach (var layer in scene.Layers)
            foreach (var cel in layer.Cels)
                if (cel.Frame is { } frame)
                    ScaleFrame(frame, sx, sy, mark, resampler);

        foreach (var region in doc.ClipRegions.Values)
        {
            foreach (var contour in region.Contours) ScalePoints(contour, sx, sy);
            // Feather is a radius in pixels, so it follows the marks rather
            // than either axis.
            region.Feather *= mark;
        }

        if (scene.Guides is { } guides)
            foreach (var guide in guides)
            {
                guide.X *= sx;
                guide.Y *= sy;
                // Grid spacing is a distance, not a position. Angle is not a
                // length and does not scale — though a non-uniform rescale
                // does shear an angled guide off its old line, which is a
                // known and accepted limit of unlinking the axes.
                guide.Spacing *= mark;
            }

        if (scene.Camera is { } camera)
            foreach (var key in camera.Keys)
            {
                key.X *= sx;
                key.Y *= sy;
                // Zoom is a ratio against the scene, and the scene scaled with
                // it — so the shot frames the same part of the drawing. Output
                // size is the deliverable's resolution and is nothing to do
                // with the document's.
            }

        if (scene.Pivot is { } pivot)
        {
            pivot.X *= sx;
            pivot.Y *= sy;
        }

        if (doc.Armature is { } armature)
            foreach (var bone in armature.Bones)
            {
                bone.X *= sx;
                bone.Y *= sy;
                // Length is a distance along the bone, so it follows the
                // marks. Rotation is not a length and does not scale — a
                // non-uniform rescale shears a rotated chain off its old
                // line, the same accepted limit an angled guide has. A
                // uniform rescale (the linked default) is exact, because
                // uniform scale commutes with rotation.
                bone.Length *= mark;
            }

        if (scene.PoseTrack is { } poses)
            foreach (var key in poses.Keys)
                foreach (var pose in key.Bones.Values)
                {
                    // A pose's translation is a distance in its parent's
                    // frame; its rotation, like the bone's, is not a length.
                    pose.X *= sx;
                    pose.Y *= sy;
                }

        if (scene.References is { } references)
            foreach (var strip in references)
            {
                // Where the reference sits over the drawing, and how big it is
                // drawn — the cells are coordinates inside the source sheet
                // and must not move.
                strip.OffsetX *= sx;
                strip.OffsetY *= sy;
                strip.Scale *= mark;
            }

        return true;
    }

    private static void ScaleFrame(
        Frame frame, double sx, double sy, double mark, IPixelResampler resampler)
    {
        foreach (var stroke in frame.Strokes)
        {
            ScalePoints(stroke.Points, sx, sy);
            if (stroke.Holes is { } holes)
                foreach (var hole in holes) ScalePoints(hole, sx, sy);

            // Size and TextureScale are the only two brush settings in
            // document pixels. Spacing, smudge length and radius, minimum
            // diameter and every jitter are fractions of one of them and
            // therefore already scale.
            stroke.Brush.Size *= mark;
            stroke.Brush.TextureScale *= mark;

            if (stroke.Baked is { PngBase64.Length: > 0 } baked)
            {
                var resampled = resampler.Resample(baked.PngBase64, sx, sy);
                if (resampled is null) stroke.Baked = null;
                else
                {
                    baked.PngBase64 = resampled;
                    baked.X = (int)Math.Round(baked.X * sx);
                    baked.Y = (int)Math.Round(baked.Y * sy);
                }
            }
        }

        if (frame.PngBase64 is { Length: > 0 } baseline)
            frame.PngBase64 = resampler.Resample(baseline, sx, sy);

        if (frame.Placements is { } placements)
            foreach (var placement in placements)
            {
                placement.X *= sx;
                placement.Y *= sy;
                // A symbol keeps its own coordinate space — it is shared, and
                // rescaling this document must not reach into it. The
                // placement's scale is where the change belongs.
                placement.ScaleX *= sx;
                placement.ScaleY *= sy;
            }

        if (frame.Anchors is { } anchors)
            foreach (var id in anchors.Keys.ToList())
                anchors[id] = new AnchorPoint(anchors[id].X * sx, anchors[id].Y * sy);

        if (frame.Shapes is { } shapes)
            foreach (var id in shapes.Keys.ToList())
            {
                var box = shapes[id];
                shapes[id] = box with { X = box.X * sx, Y = box.Y * sy, W = box.W * sx, H = box.H * sy };
            }
    }

    private static void ScalePoints(List<StrokePoint> points, double sx, double sy)
    {
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            // Pressure is not a coordinate.
            points[i] = p with { X = p.X * sx, Y = p.Y * sy };
        }
    }

    /// <summary>
    /// Scale a nullable origin and keep it null when it lands on zero, so a
    /// rescale never introduces the key that <see cref="Scene.OriginX"/> is
    /// nullable to keep out.
    /// </summary>
    private static int? Scaled(int? origin, double scale)
    {
        if (origin is not { } value) return null;
        var scaled = (int)Math.Round(value * scale);
        return scaled == 0 ? null : scaled;
    }
}
