using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>Which of the analyser's three checks a finding came from.</summary>
public enum WalkCheck
{
    Loop,
    Contacts,
    Bob,
}

/// <summary>
/// One thing the cycle does that a walk should not, in the artist's words,
/// anchored to the drawing it reads worst on. Frame numbers in the message are
/// 1-based — the timeline's numbers, not the record's indices.
/// </summary>
public readonly record struct WalkFinding(WalkCheck Check, int Frame, string Message);

/// <summary>
/// What the analyser read: how many drawings it saw, where it read contacts,
/// and everything it would tell an animator. No findings is the good news.
/// </summary>
public sealed record WalkCycleReport(
    int Drawings,
    IReadOnlyList<int> ContactFrames,
    IReadOnlyList<WalkFinding> Findings);

/// <summary>
/// Reads the active layer's sheet as one walk cycle and reports the three
/// things that make a cycle hitch (Q133): a loop that does not close, contacts
/// that land unevenly, and a bob that differs between the steps.
/// </summary>
/// <remarks>
/// <para>
/// Everything is read off the record — the subject through
/// <see cref="MotionTrail.Locate"/> (authored pivot, else ink-bounds centre)
/// and the feet as the lowest ink, <see cref="MotionTrail.InkBounds"/>'s
/// bottom edge. Tolerances are fractions of the drawings' mean ink height, so
/// the same walk reads the same at any canvas size, with named constants
/// rather than magic below.
/// </para>
/// <para>
/// The checks are advisory prose and deliberately so: the bob check assumes a
/// gait that rises between contacts, and a deliberate shuffle will trip it.
/// That cost was accepted in Q133 — a readout can be ignored, and the walks
/// that wobble by accident vastly outnumber the shuffles drawn on purpose.
/// </para>
/// </remarks>
public static class WalkCycleAnalyser
{
    /// <summary>The fewest drawings a cycle can be judged on.</summary>
    public const int MinDrawings = 4;

    /// <summary>
    /// The seam step may be this many times the biggest step inside the cycle
    /// before the loop reads as popping. Not an equality check on purpose: a
    /// correct cycle's last drawing is one step BEFORE the first, so first and
    /// last are supposed to differ — by about as much as any two neighbours do.
    /// </summary>
    public const double SeamRatio = 1.5;

    /// <summary>A seam step under this share of ink height is never worth pointing at.</summary>
    public const double LoopSlack = 0.06;

    /// <summary>How far above the lowest ink of the whole cycle a drawing still counts as planted.</summary>
    public const double ContactBand = 0.04;

    /// <summary>Contact intervals may differ by this many frames before the stride reads uneven.</summary>
    public const double StrideSlack = 1.0;

    /// <summary>One step's bob may be this many times another's before the gait reads lopsided.</summary>
    public const double BobRatio = 2.0;

    /// <summary>Below this share of ink height, the cycle does not bob at all — it floats.</summary>
    public const double BobFloor = 0.01;

    /// <summary>
    /// Read the layer as one cycle. Null when fewer than
    /// <see cref="MinDrawings"/> drawings have ink — there is no cycle to judge.
    /// </summary>
    public static WalkCycleReport? Analyse(Scene scene, Layer layer)
    {
        var drawings = new List<(int Index, double X, double Y, double Bottom, double W, double H)>();
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not { } frame) continue;
            if (MotionTrail.InkBounds(frame) is not { } b) continue;
            // The subject may be an authored pivot; the feet are always ink.
            var subject = MotionTrail.Locate(scene, frame)!.Value;
            drawings.Add((i, subject.X, subject.Y, b.MaxY, b.MaxX - b.MinX, b.MaxY - b.MinY));
        }
        if (drawings.Count < MinDrawings) return null;

        var scale = drawings.Average(d => d.H);
        if (scale <= 0) scale = drawings.Average(d => d.W);
        if (scale <= 0) return null;

        var findings = new List<WalkFinding>();
        CheckLoop(drawings, scale, findings);
        var contacts = ReadContacts(drawings, scale, layer.Cels.Count, findings);
        CheckBob(drawings, contacts, scale, findings);

        return new WalkCycleReport(drawings.Count, contacts, findings);
    }

    /// <summary>
    /// A cycle repeats, so the step from its last drawing back to its first
    /// must read like any other step — the seam must not be the jump.
    /// </summary>
    private static void CheckLoop(
        List<(int Index, double X, double Y, double Bottom, double W, double H)> d,
        double scale, List<WalkFinding> findings)
    {
        var floor = LoopSlack * scale;
        var last = d[^1];

        Judge(p => p.Y, "vertically", "the loop will pop");
        Judge(p => p.H, "in height", "the drawing breathes across the loop");
        Judge(p => p.W, "in width", "the drawing breathes across the loop");

        // Horizontal closure only means something on an in-place cycle; a walk
        // that crosses the canvas is supposed to end somewhere else.
        if (d.Max(p => p.X) - d.Min(p => p.X) < 0.5 * scale)
        {
            Judge(p => p.X, "sideways", "an in-place walk ends somewhere it never was");
        }

        void Judge(Func<(int Index, double X, double Y, double Bottom, double W, double H), double> read,
            string axis, string cost)
        {
            // The seam is judged against the steps AWAY from it — a wrong
            // endpoint drawing corrupts the internal step beside it too, so
            // including the seam's neighbours would let the mistake set its
            // own yardstick and never be caught.
            double biggest = 0;
            for (var j = 2; j < d.Count - 1; j++)
                biggest = Math.Max(biggest, Math.Abs(read(d[j]) - read(d[j - 1])));
            var seam = Math.Abs(read(d[0]) - read(last));
            if (seam > floor && seam > SeamRatio * biggest)
            {
                findings.Add(new WalkFinding(WalkCheck.Loop, last.Index,
                    $"The cycle does not close {axis}: frame {last.Index + 1} back to frame {d[0].Index + 1} " +
                    $"steps {seam:0.#} px where the cycle's own steps stay under {biggest:0.#} px — {cost}."));
            }
        }
    }

    /// <summary>
    /// Contacts read as runs of drawings whose lowest ink sits on the cycle's
    /// ground line; each run is one footfall, and the footfalls should land as
    /// evenly as the walk is timed.
    /// </summary>
    private static IReadOnlyList<int> ReadContacts(
        List<(int Index, double X, double Y, double Bottom, double W, double H)> d,
        double scale, int period, List<WalkFinding> findings)
    {
        var ground = d.Max(p => p.Bottom);
        var planted = d.Select(p => p.Bottom >= ground - ContactBand * scale).ToArray();

        // A footfall starts where a planted drawing follows an airborne one.
        // The cycle wraps: a contact spanning the loop's seam is one footfall,
        // which is why "before the first drawing" means the last one.
        var starts = new List<int>();
        for (var j = 0; j < d.Count; j++)
        {
            var prev = planted[(j + d.Count - 1) % d.Count];
            if (planted[j] && !prev) starts.Add(j);
        }

        // Every drawing planted (starts empty but planted everywhere) is a
        // pose sheet, not a walk; no contact reading either way.
        if (starts.Count < 2)
        {
            findings.Add(new WalkFinding(WalkCheck.Contacts, d[0].Index,
                "Fewer than two contacts read from the lowest ink — the feet never both land, so stride and bob cannot be judged."));
            return starts.Select(j => d[j].Index).ToList();
        }

        // Intervals between footfalls, in frames, wrapping through the
        // cycle's period so the last stride is judged like the others.
        var intervals = new List<double>(starts.Count);
        for (var k = 0; k < starts.Count; k++)
        {
            var here = d[starts[k]].Index;
            var next = k + 1 < starts.Count ? d[starts[k + 1]].Index : d[starts[0]].Index + period;
            intervals.Add(next - here);
        }
        if (intervals.Max() - intervals.Min() > StrideSlack)
        {
            var frames = string.Join(", ", starts.Select(j => d[j].Index + 1));
            findings.Add(new WalkFinding(WalkCheck.Contacts, d[starts[0]].Index,
                $"The contacts land unevenly (frames {frames}): the longest stride is " +
                $"{intervals.Max():0.#} frames against the shortest's {intervals.Min():0.#}."));
        }

        return starts.Select(j => d[j].Index).ToList();
    }

    /// <summary>
    /// Between footfalls the body rises and settles; each step's rise should
    /// match the others, or the walk limps.
    /// </summary>
    private static void CheckBob(
        List<(int Index, double X, double Y, double Bottom, double W, double H)> d,
        IReadOnlyList<int> contactFrames, double scale, List<WalkFinding> findings)
    {
        var wholeAmplitude = d.Max(p => p.Y) - d.Min(p => p.Y);
        if (wholeAmplitude < BobFloor * scale)
        {
            findings.Add(new WalkFinding(WalkCheck.Bob, d[0].Index,
                "The subject does not bob at all — a dead-level walk reads as floating."));
            return;
        }
        if (contactFrames.Count < 2) return;

        // One stride's drawings: from a footfall up to the next, wrapping.
        var starts = contactFrames.Select(f => d.FindIndex(p => p.Index == f)).ToList();
        var amplitudes = new List<(int Frame, double Amp)>(starts.Count);
        for (var k = 0; k < starts.Count; k++)
        {
            var from = starts[k];
            var to = k + 1 < starts.Count ? starts[k + 1] : starts[0] + d.Count;
            double min = double.MaxValue, max = double.MinValue;
            for (var j = from; j <= to; j++)
            {
                var y = d[j % d.Count].Y;
                min = Math.Min(min, y);
                max = Math.Max(max, y);
            }
            amplitudes.Add((d[from].Index, max - min));
        }

        var biggest = amplitudes.MaxBy(a => a.Amp);
        var smallest = amplitudes.MinBy(a => a.Amp);
        if (biggest.Amp > BobRatio * smallest.Amp + BobFloor * scale)
        {
            findings.Add(new WalkFinding(WalkCheck.Bob, smallest.Frame,
                $"The bob is uneven between steps: the stride at frame {biggest.Frame + 1} rises " +
                $"{biggest.Amp:0.#} px against {smallest.Amp:0.#} px at frame {smallest.Frame + 1} — the walk will limp."));
        }
    }
}
