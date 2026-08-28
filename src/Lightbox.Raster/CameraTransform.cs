using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// Turns a camera framing into the matrix a composite is drawn under, and maps
/// document regions into the device pixels that matrix puts them in.
///
/// Both halves live together on purpose. The compositor uses the mapping to
/// decide what to repaint and <see cref="ComposeRing"/> uses it to decide what
/// to copy; if those two ever disagreed about where a document rect lands, the
/// ring would copy the wrong pixels and the error would only show under a
/// rotating camera.
/// </summary>
public static class CameraTransform
{
    /// <summary>
    /// Document coordinates to device pixels: put the framed point at the
    /// centre of the output, apply the zoom and roll, then the render scale
    /// the surface is actually allocated at.
    /// </summary>
    public static SKMatrix Matrix(CameraFraming framing, int outputWidth, int outputHeight, double renderScale)
    {
        var m = SKMatrix.CreateTranslation(-(float)framing.X, -(float)framing.Y);
        m = m.PostConcat(SKMatrix.CreateScale((float)framing.Zoom, (float)framing.Zoom));
        if (framing.RotationDeg != 0)
        {
            m = m.PostConcat(SKMatrix.CreateRotationDegrees((float)framing.RotationDeg));
        }
        m = m.PostConcat(SKMatrix.CreateTranslation(outputWidth / 2f, outputHeight / 2f));
        if (renderScale != 1.0)
        {
            m = m.PostConcat(SKMatrix.CreateScale((float)renderScale, (float)renderScale));
        }
        return m;
    }

    /// <summary>
    /// The camera frame's four corners back in document coordinates, clockwise
    /// from the top left. This is what the canvas overlay draws: the artist
    /// works in the world, and the frame shows what the shot will keep.
    /// Returns an empty array if the matrix is degenerate.
    /// </summary>
    public static SKPoint[] FrameCorners(CameraFraming framing, int outputWidth, int outputHeight)
    {
        var m = Matrix(framing, outputWidth, outputHeight, 1.0);
        if (!m.TryInvert(out var toDoc)) return [];
        return toDoc.MapPoints(
        [
            new SKPoint(0, 0),
            new SKPoint(outputWidth, 0),
            new SKPoint(outputWidth, outputHeight),
            new SKPoint(0, outputHeight),
        ]);
    }

    /// <summary>
    /// The device rectangle a document rectangle occupies.
    ///
    /// Without a camera this is the uniform-scale mapping the compositor has
    /// always used, kept exactly as it was — the whole point of the camera
    /// being optional is that a document without one composites the way it did
    /// before the feature landed. With one, the rect is mapped through the
    /// matrix and its bounding box taken, which under a roll repaints a little
    /// more than strictly necessary and is the honest price of a rotated dirty
    /// region.
    /// </summary>
    /// <param name="origin">
    /// The document point the surface's (0,0) holds. Zero whenever the surface
    /// spans the whole document, which is every camera route and every
    /// uncropped one — so the arithmetic below is unchanged for them.
    /// </param>
    public static SKRect DeviceBounds(
        SKRectI doc, double scale, SKMatrix? transform, SKPointI origin = default)
    {
        if (transform is not { } m)
        {
            return SKRect.Create(
                (float)Math.Floor((doc.Left - origin.X) * scale),
                (float)Math.Floor((doc.Top - origin.Y) * scale),
                (float)Math.Ceiling(doc.Width * scale) + 1,
                (float)Math.Ceiling(doc.Height * scale) + 1);
        }

        var mapped = m.MapRect(new SKRect(doc.Left, doc.Top, doc.Right, doc.Bottom));
        mapped.Inflate(1, 1); // the same slack the unrotated path leaves for antialiasing

        // Round outward to whole pixels. A rotated rect almost never lands on
        // pixel boundaries, and an un-rounded clip rounds INWARD — so two
        // document regions that abut can both exclude the pixel between them
        // and leave it stale. It showed up as two wrong pixels on a 30-degree
        // roll, which is exactly the kind of thing that reads as dirt on the
        // frame rather than as a bug.
        return new SKRect(
            (float)Math.Floor(mapped.Left),
            (float)Math.Floor(mapped.Top),
            (float)Math.Ceiling(mapped.Right),
            (float)Math.Ceiling(mapped.Bottom));
    }
}
