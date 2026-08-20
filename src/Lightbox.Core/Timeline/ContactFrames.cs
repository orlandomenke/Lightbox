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
/// lowest ink the sequence ever reaches, a drawing whose lowest ink sits
/// within the walk analyser's band of it is planted, and a footfall starts
/// where a planted drawing follows an airborne one.
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
    /// Read the layer. Null when fewer than <see cref="MinDrawings"/> drawings
    /// have ink — a ground line needs a sequence to be read from.
    /// </summary>
    public static ContactReading? Detect(Layer layer)
    {
        var drawings = new List<(int Index, double Bottom, double W, double H)>();
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not { } frame) continue;
            if (MotionTrail.InkBounds(frame) is not { } b) continue;
            drawings.Add((i, b.MaxY, b.MaxX - b.MinX, b.MaxY - b.MinY));
        }
        if (drawings.Count < MinDrawings) return null;

        var scale = drawings.Average(d => d.H);
        if (scale <= 0) scale = drawings.Average(d => d.W);
        if (scale <= 0) return null;

        var planted = Planted(drawings.Select(d => d.Bottom).ToList(), scale);
        var starts = new List<int>();
        var plantedFrames = new List<int>();
        for (var j = 0; j < drawings.Count; j++)
        {
            if (!planted[j]) continue;
            plantedFrames.Add(drawings[j].Index);
            if (j == 0 || !planted[j - 1]) starts.Add(drawings[j].Index);
        }
        return new ContactReading(starts, plantedFrames);
    }

    /// <summary>
    /// Which drawings stand on the ground line — the one band rule, shared
    /// with the walk analyser.
    /// </summary>
    public static bool[] Planted(IReadOnlyList<double> bottoms, double scale)
    {
        var ground = bottoms.Max();
        var planted = new bool[bottoms.Count];
        for (var j = 0; j < bottoms.Count; j++)
        {
            planted[j] = bottoms[j] >= ground - WalkCycleAnalyser.ContactBand * scale;
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
