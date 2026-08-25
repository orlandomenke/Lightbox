using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>
/// What detection read off a layer: the frames where a footfall begins, and
/// every frame whose drawing stands on the ground line.
/// </summary>
public sealed record ContactReading(
    IReadOnlyList<int> Starts,
    IReadOnlyList<int> PlantedFrames);

/// <summary>
/// Reads contact frames off a layer's ink (Q135): the ground line is the
/// artist's own "ground" guide when one is placed (any angle — Q137), else
/// the lowest ink the sequence ever reaches; a drawing whose feet sit within
/// the band of it is planted, and a footfall starts where a planted drawing
/// follows an airborne one.
/// </summary>
/// <remarks>
/// <para>
/// The band rule is <see cref="WalkCycleAnalyser"/>'s, shared through
/// <see cref="Planted"/> so "what counts as standing on the ground" is one
/// answer wherever it is asked. The difference is the wrap: the walk analyser
/// reads a cycle, where the last drawing hands off to the first; detection
/// reads a shot, where frame 0 being planted IS a footfall and nothing wraps.
/// </para>
/// <para>
/// Detection only reads — writing the marker is the caller's editor step, so
/// it is undoable and never runs without being asked (Q135's rule: a
/// continuous detector writes to the record unasked and fights the contacts
/// an artist marks by hand).
/// </para>
/// </remarks>
public static class ContactFrames
{
    /// <summary>The label the detect command writes, and the one the jump arc trims by.</summary>
    public const string MarkerLabel = "contact";

    /// <summary>The walk analyser's honesty floor, for the same reason.</summary>
    public const int MinDrawings = WalkCycleAnalyser.MinDrawings;

    /// <summary>
    /// One drawing read against the ground: where its feet are (the ink
    /// nearest the ground — elevation up-positive, so feet live near the
    /// minimum), where that foot sits along the ground, where the subject is,
    /// and how big the drawing is. The shape the walk analyser and detection
    /// share, so they can never disagree about what a drawing says.
    /// </summary>
    public readonly record struct FootRead(
        int Index,
        double FootElev, double FootAlong,
        double SubjElev, double SubjAlong,
        double W, double H);

    /// <summary>Every drawing with ink in [first..last], read against the ground.</summary>
    public static List<FootRead> ReadFeet(
        Scene scene, Layer layer, GroundFrame ground, int first, int last)
    {
        var reads = new List<FootRead>();
        for (var i = first; i <= last && i < layer.Cels.Count; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not { } frame) continue;
            if (MotionTrail.InkBounds(frame) is not { } b) continue;

            // The foot is the ink nearest the ground, wherever the ground
            // slopes — a per-point walk, because a bounds corner is only the
            // right answer when the ground is axis-aligned.
            var footElev = double.MaxValue;
            var footAlong = 0.0;
            foreach (var stroke in frame.Strokes)
            {
                if (stroke.Tool is ToolKind.Eraser or ToolKind.ClearRegion) continue;
                foreach (var p in stroke.Points)
                {
                    var e = ground.Elevation(p.X, p.Y);
                    if (e < footElev)
                    {
                        footElev = e;
                        footAlong = ground.Along(p.X, p.Y);
                    }
                }
            }
            if (footElev == double.MaxValue) continue;

            var subject = MotionTrail.Locate(scene, frame, b)!.Value;
            reads.Add(new FootRead(
                i, footElev, footAlong,
                ground.Elevation(subject.X, subject.Y), ground.Along(subject.X, subject.Y),
                b.MaxX - b.MinX, b.MaxY - b.MinY));
        }
        return reads;
    }

    /// <summary>
    /// Read the layer. Null when fewer than <see cref="MinDrawings"/> drawings
    /// have ink — a ground line needs a sequence to be read from.
    /// </summary>
    public static ContactReading? Detect(Scene scene, Layer layer)
    {
        var ground = Ground.Resolve(scene);
        var reads = ReadFeet(scene, layer, ground, 0, layer.Cels.Count - 1);
        if (reads.Count < MinDrawings) return null;

        var scale = reads.Average(d => d.H);
        if (scale <= 0) scale = reads.Average(d => d.W);
        if (scale <= 0) return null;

        var planted = Planted(reads.Select(d => d.FootElev).ToList(), scale);
        var starts = new List<int>();
        var plantedFrames = new List<int>();
        for (var j = 0; j < reads.Count; j++)
        {
            if (!planted[j]) continue;
            plantedFrames.Add(reads[j].Index);
            if (j == 0 || !planted[j - 1]) starts.Add(reads[j].Index);
        }
        return new ContactReading(starts, plantedFrames);
    }

    /// <summary>
    /// Which drawings stand on the ground line — the one band rule, shared
    /// with the walk analyser. Elevations are up-positive, so the ground is
    /// the minimum the feet ever reach.
    /// </summary>
    public static bool[] Planted(IReadOnlyList<double> footElevations, double scale)
    {
        var ground = footElevations.Min();
        var planted = new bool[footElevations.Count];
        for (var j = 0; j < footElevations.Count; j++)
        {
            planted[j] = footElevations[j] <= ground + WalkCycleAnalyser.ContactBand * scale;
        }
        return planted;
    }

    /// <summary>The frames within a range that carry a contact marker, in order.</summary>
    public static IReadOnlyList<int> MarkedIn(Scene scene, int first, int last) =>
        scene.Markers
            .Where(m => m.Frame >= first && m.Frame <= last
                        && string.Equals(m.Label?.Trim(), MarkerLabel, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Frame)
            .OrderBy(f => f)
            .ToList();
}
