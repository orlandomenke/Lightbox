using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.Raster.Text;

/// <summary>
/// Glyph outlines, flattened to the contours the document records.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is where type stops being type.</b> Everything above it knows about
/// families and sizes; everything below it sees a closed polygon exactly like
/// the one the fill tool traces. The flattening happens once, when the artist
/// commits the text, and the result is what the file carries — so the tolerance
/// below is not a rendering quality setting that can be turned up later, it is
/// baked into the drawing. That is deliberate: a curve resolution that varied
/// with a preference would make the same document a different picture on two
/// machines, which is invariant 2 seen from the geometry side.
/// </para>
/// <para>
/// <b>Deterministic by construction.</b> Segment counts come from the control
/// points and the tolerance through a closed formula — no adaptive recursion
/// with a floating-point stopping test, whose depth can differ by one on a
/// different rounding mode. Same font bytes, same size, same points, every time.
/// </para>
/// </remarks>
public static class GlyphOutline
{
    /// <summary>
    /// The greatest distance, in document pixels, a flattened segment may sit
    /// from the curve it replaces.
    /// </summary>
    /// <remarks>
    /// A fifth of a pixel. Below what anti-aliasing can express at 1× and still
    /// coarse enough that a 48px cap-height letter comes out in tens of points
    /// rather than hundreds — which matters because these points are written to
    /// the file, once per glyph, and a page of type would otherwise be the
    /// largest thing in the document. Text is routinely rendered at 2× or 4× for
    /// print-scale output (invariant 7 scales the surface, not the geometry), so
    /// this is the number that decides whether an enlarged title shows facets:
    /// at 4× a fifth of a pixel becomes four fifths, which is still under one.
    /// </remarks>
    public const double Tolerance = 0.2;

    /// <summary>Segments a single curve may be broken into, however tight it is.</summary>
    /// <remarks>
    /// A ceiling rather than a target. A glyph drawn at 2000px would ask for
    /// hundreds of segments per curve and gain nothing an artist can see, and
    /// the cost lands in the saved file rather than in a frame budget where it
    /// would at least be noticed.
    /// </remarks>
    private const int MaxSegments = 64;

    /// <summary>
    /// The closed contours of a path, in document coordinates, offset by
    /// <paramref name="dx"/> and <paramref name="dy"/>.
    /// </summary>
    /// <remarks>
    /// Contours are not closed by repeating the first point: a filled contour is
    /// closed by <c>BrushEngine.PathFromContours</c>, which calls
    /// <c>Close()</c> on each one. That is the opposite of
    /// <c>ShapeBuilder.Outline</c>, which does repeat — because a shape is
    /// stamped along its points and a fill is not.
    /// </remarks>
    public static List<List<StrokePoint>> Contours(SKPath path, double dx, double dy)
    {
        var contours = new List<List<StrokePoint>>();
        List<StrokePoint>? current = null;
        var at = new SKPoint();

        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];

        while (true)
        {
            var verb = iterator.Next(points);
            if (verb == SKPathVerb.Done) break;

            switch (verb)
            {
                case SKPathVerb.Move:
                    Finish();
                    current = [Point(points[0])];
                    at = points[0];
                    break;

                case SKPathVerb.Line:
                    Add(points[1]);
                    break;

                case SKPathVerb.Quad:
                    Quad(points[0], points[1], points[2]);
                    break;

                case SKPathVerb.Conic:
                    // Rare from a font — conics come from circles and from
                    // OpenType variable interpolation — and converted rather
                    // than approximated, so the weight is honoured. pow2: 3 is
                    // eight quads, which is exact enough that the flattening
                    // below is what decides the error.
                    var quads = SKPath.ConvertConicToQuads(
                        points[0], points[1], points[2], iterator.ConicWeight(), 3);
                    for (var i = 0; i + 2 < quads.Length; i += 2)
                    {
                        Quad(quads[i], quads[i + 1], quads[i + 2]);
                    }
                    break;

                case SKPathVerb.Cubic:
                    Cubic(points[0], points[1], points[2], points[3]);
                    break;

                case SKPathVerb.Close:
                    Finish();
                    break;
            }
        }

        Finish();
        return contours;

        StrokePoint Point(SKPoint p) => new(p.X + dx, p.Y + dy, 1);

        void Add(SKPoint p)
        {
            current?.Add(Point(p));
            at = p;
        }

        void Finish()
        {
            // Two points enclose no area, and the fill path skips them anyway —
            // dropping them here keeps a degenerate contour out of the file.
            if (current is { Count: >= 3 }) contours.Add(current);
            current = null;
        }

        void Quad(SKPoint p0, SKPoint p1, SKPoint p2)
        {
            var n = QuadSegments(p0, p1, p2);
            for (var i = 1; i <= n; i++)
            {
                var t = (double)i / n;
                var u = 1 - t;
                Add(new SKPoint(
                    (float)(u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X),
                    (float)(u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y)));
            }
            at = p2;
        }

        void Cubic(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3)
        {
            var n = CubicSegments(p0, p1, p2, p3);
            for (var i = 1; i <= n; i++)
            {
                var t = (double)i / n;
                var u = 1 - t;
                Add(new SKPoint(
                    (float)(u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X),
                    (float)(u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y)));
            }
            at = p3;
        }
    }

    /// <summary>
    /// Segments a quadratic needs to stay within <see cref="Tolerance"/>.
    /// </summary>
    /// <remarks>
    /// Uniform subdivision of a quadratic into <c>n</c> pieces leaves at most
    /// <c>|p0 - 2p1 + p2| / (8n²)</c> between the curve and the chords, so
    /// inverting that for the tolerance gives the count directly. No recursion,
    /// no stopping test, no dependence on evaluation order.
    /// </remarks>
    private static int QuadSegments(SKPoint p0, SKPoint p1, SKPoint p2)
    {
        var dx = p0.X - 2 * p1.X + p2.X;
        var dy = p0.Y - 2 * p1.Y + p2.Y;
        return Segments(Math.Sqrt(dx * dx + dy * dy) / (8 * Tolerance));
    }

    /// <summary>
    /// Segments a cubic needs, by the same argument with the cubic's own
    /// bound: <c>3·max(|p0-2p1+p2|, |p1-2p2+p3|) / (4n²)</c>.
    /// </summary>
    private static int CubicSegments(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3)
    {
        var ax = p0.X - 2 * p1.X + p2.X;
        var ay = p0.Y - 2 * p1.Y + p2.Y;
        var bx = p1.X - 2 * p2.X + p3.X;
        var by = p1.Y - 2 * p2.Y + p3.Y;
        var d = Math.Max(Math.Sqrt(ax * ax + ay * ay), Math.Sqrt(bx * bx + by * by));
        return Segments(3 * d / (4 * Tolerance));
    }

    private static int Segments(double squared) =>
        squared <= 1 ? 1 : Math.Min(MaxSegments, (int)Math.Ceiling(Math.Sqrt(squared)));
}
