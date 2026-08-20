using Lightbox.Core.Documents;

namespace Lightbox.Core.Timeline;

/// <summary>Which of the analyser's checks a finding came from.</summary>
public enum WalkCheck
{
    Loop,
    Contacts,
    Bob,

    /// <summary>The planted foot moved when it should have held (Q137).</summary>
    Slide,

    /// <summary>The range reads as depth motion, which the flat checks cannot judge (Q137).</summary>
    Depth,
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
/// <param name="RangeName">The tag the range came from, or null for the whole layer.</param>
public sealed record WalkCycleReport(
    int Drawings,
    IReadOnlyList<int> ContactFrames,
    IReadOnlyList<WalkFinding> Findings,
    string? RangeName = null);

/// <summary>
/// Reads a walk back to the artist: loop closure, contact evenness, bob
/// symmetry (Q134) — and, since Q137, on shot terms as well as asset terms.
/// The range is the tag under the playhead when one covers it (else the whole
/// sheet); the ground is the artist's own "ground" line guide at any angle
/// (else the canvas-horizontal lowest ink); the cycle checks run only when
/// the range is meant to repeat — the tag's own <c>Loop</c> flag, else
/// standing in place; and a travelling walk gains the check that matters
/// there instead: the planted foot must hold its place along the ground.
/// </summary>
/// <remarks>
/// <para>
/// Everything is read off the record — the subject through
/// <see cref="MotionTrail.Locate"/> (authored pivot, else ink-bounds centre)
/// and the feet as the ink nearest the ground. Tolerances are fractions of
/// the drawings' mean ink height, so the same walk reads the same at any
/// canvas size, with named constants rather than magic below.
/// </para>
/// <para>
/// The checks are advisory prose and deliberately so: the bob check assumes a
/// gait that rises between contacts, and a deliberate shuffle will trip it.
/// That cost was accepted in Q134 — a readout can be ignored, and the walks
/// that wobble by accident vastly outnumber the shuffles drawn on purpose.
/// </para>
/// <para>
/// <b>Always world-space, camera or no camera (Q137).</b> Whether a foot
/// slides, whether contacts land evenly and whether a cycle closes are
/// physical facts a camera move cannot change; judging them through a pan
/// would flag correct animation.
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

    /// <summary>How far a planted foot may drift along the ground, as a share of ink height.</summary>
    public const double SlideBand = 0.06;

    /// <summary>A range whose subject travels less than this share of ink height stands in place.</summary>
    public const double InPlaceBand = 0.5;

    /// <summary>Past this ratio between the biggest and smallest drawing, the range reads as depth motion.</summary>
    public const double DepthBand = 1.25;

    /// <summary>
    /// Read the walk under the playhead. Null when fewer than
    /// <see cref="MinDrawings"/> drawings in range have ink — there is no
    /// cycle to judge.
    /// </summary>
    public static WalkCycleReport? Analyse(Scene scene, Layer layer, int index)
    {
        // The tag under the playhead is the range (Q137) — the same object an
        // engine clip is, and in a shot the way to say "frames 30–70 are the
        // walk". Whole sheet when nothing covers the playhead, as always.
        var tag = scene.Tags?.FirstOrDefault(t => index >= t.Start && index <= t.End);
        var first = Math.Max(0, tag?.Start ?? 0);
        var last = Math.Min(layer.Cels.Count - 1, tag?.End ?? layer.Cels.Count - 1);

        var ground = Ground.Resolve(scene);
        var d = ContactFrames.ReadFeet(scene, layer, ground, first, last);
        if (d.Count < MinDrawings) return null;

        var scale = d.Average(f => f.H);
        if (scale <= 0) scale = d.Average(f => f.W);
        if (scale <= 0) return null;

        var findings = new List<WalkFinding>();

        // Depth first, and alone: a character moving toward or away from
        // camera changes size, and every flat check below would misread that
        // as error. Saying "I cannot judge this" is the honest report (Q137).
        var sizes = d.Select(f => Math.Sqrt(f.W * f.W + f.H * f.H)).ToList();
        if (sizes.Min() > 0 && sizes.Max() / sizes.Min() > DepthBand)
        {
            findings.Add(new WalkFinding(WalkCheck.Depth, d[0].Index,
                $"The drawing changes size across this range ({sizes.Min():0.#} px to {sizes.Max():0.#} px) — " +
                "this reads as depth motion, which the flat checks cannot judge."));
            return new WalkCycleReport(d.Count, [], findings, tag?.Name);
        }

        // In place, or travelling? The tag's own Loop flag answers when a tag
        // is present — authored beats derived — else standing still does.
        var travel = d.Max(f => f.SubjAlong) - d.Min(f => f.SubjAlong);
        var inPlace = travel < InPlaceBand * scale;
        var loops = tag?.Loop ?? inPlace;

        if (loops) CheckLoop(d, scale, inPlace, findings);
        var contacts = ReadContacts(d, scale, loops, last - first + 1, findings);
        CheckBob(d, contacts, scale, loops, findings);
        CheckSlide(d, scale, inPlace, findings);

        return new WalkCycleReport(d.Count, contacts, findings, tag?.Name);
    }

    /// <summary>
    /// A cycle repeats, so the step from its last drawing back to its first
    /// must read like any other step — the seam must not be the jump.
    /// </summary>
    private static void CheckLoop(
        List<ContactFrames.FootRead> d, double scale, bool inPlace, List<WalkFinding> findings)
    {
        var floor = LoopSlack * scale;
        var last = d[^1];

        Judge(f => f.SubjElev, "vertically", "the loop will pop");
        Judge(f => f.H, "in height", "the drawing breathes across the loop");
        Judge(f => f.W, "in width", "the drawing breathes across the loop");

        // Horizontal closure only means something on an in-place cycle; a walk
        // that crosses the canvas is supposed to end somewhere else.
        if (inPlace)
        {
            Judge(f => f.SubjAlong, "along the ground", "an in-place walk ends somewhere it never was");
        }

        void Judge(Func<ContactFrames.FootRead, double> read, string axis, string cost)
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
    /// Contacts read as runs of drawings whose feet sit on the ground line;
    /// each run is one footfall, and the footfalls should land as evenly as
    /// the walk is timed.
    /// </summary>
    private static IReadOnlyList<int> ReadContacts(
        List<ContactFrames.FootRead> d, double scale, bool loops, int period,
        List<WalkFinding> findings)
    {
        var planted = ContactFrames.Planted(d.Select(f => f.FootElev).ToList(), scale);

        // A footfall starts where a planted drawing follows an airborne one.
        // A looping range wraps — a contact spanning the seam is one footfall —
        // and a shot does not: its first planted drawing IS a footfall.
        var starts = new List<int>();
        for (var j = 0; j < d.Count; j++)
        {
            if (!planted[j]) continue;
            var prev = j > 0 ? planted[j - 1] : (loops && planted[^1]);
            if (!prev) starts.Add(j);
        }

        if (starts.Count < 2)
        {
            findings.Add(new WalkFinding(WalkCheck.Contacts, d[0].Index,
                "Fewer than two contacts read from the lowest ink — the feet never both land, so stride and bob cannot be judged."));
            return starts.Select(j => d[j].Index).ToList();
        }

        // Intervals between footfalls, in frames — wrapping through the
        // range's period only when the range loops.
        var intervals = new List<double>(starts.Count);
        for (var k = 0; k < starts.Count; k++)
        {
            if (k + 1 < starts.Count)
            {
                intervals.Add(d[starts[k + 1]].Index - d[starts[k]].Index);
            }
            else if (loops)
            {
                intervals.Add(d[starts[0]].Index + period - d[starts[k]].Index);
            }
        }
        if (intervals.Count >= 2 && intervals.Max() - intervals.Min() > StrideSlack)
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
        List<ContactFrames.FootRead> d, IReadOnlyList<int> contactFrames, double scale,
        bool loops, List<WalkFinding> findings)
    {
        var wholeAmplitude = d.Max(f => f.SubjElev) - d.Min(f => f.SubjElev);
        if (wholeAmplitude < BobFloor * scale)
        {
            findings.Add(new WalkFinding(WalkCheck.Bob, d[0].Index,
                "The subject does not bob at all — a dead-level walk reads as floating."));
            return;
        }
        if (contactFrames.Count < 2) return;

        // One stride's drawings: from a footfall up to the next — wrapping
        // through the seam ONLY when the range loops. A shot's last footfall
        // opens no stride, and folding it back to frame 0 was the adversary's
        // find: a clean travelling walk read as limping against a stride
        // nobody drew.
        var starts = contactFrames.Select(f => d.FindIndex(p => p.Index == f)).ToList();
        var amplitudes = new List<(int Frame, double Amp)>(starts.Count);
        for (var k = 0; k < starts.Count; k++)
        {
            var from = starts[k];
            int to;
            if (k + 1 < starts.Count) to = starts[k + 1];
            else if (loops) to = starts[0] + d.Count;
            else break;
            double min = double.MaxValue, max = double.MinValue;
            for (var j = from; j <= to; j++)
            {
                var e = d[j % d.Count].SubjElev;
                min = Math.Min(min, e);
                max = Math.Max(max, e);
            }
            amplitudes.Add((d[from].Index, max - min));
        }
        if (amplitudes.Count < 2) return;

        var biggest = amplitudes.MaxBy(a => a.Amp);
        var smallest = amplitudes.MinBy(a => a.Amp);
        if (biggest.Amp > BobRatio * smallest.Amp + BobFloor * scale)
        {
            findings.Add(new WalkFinding(WalkCheck.Bob, smallest.Frame,
                $"The bob is uneven between steps: the stride at frame {biggest.Frame + 1} rises " +
                $"{biggest.Amp:0.#} px against {smallest.Amp:0.#} px at frame {smallest.Frame + 1} — the walk will limp."));
        }
    }

    /// <summary>
    /// The planted foot's law (Q137): in a travelling walk it holds still in
    /// the world; in an in-place cycle the tread carries it back at a constant
    /// rate. Either way, the drawing where it breaks is the slide every
    /// audience notices and no asset-lens check could see.
    /// </summary>
    private static void CheckSlide(
        List<ContactFrames.FootRead> d, double scale, bool inPlace, List<WalkFinding> findings)
    {
        var planted = ContactFrames.Planted(d.Select(f => f.FootElev).ToList(), scale);
        var band = SlideBand * scale;

        var j = 0;
        while (j < d.Count)
        {
            if (!planted[j]) { j++; continue; }
            var from = j;
            while (j < d.Count && planted[j]) j++;
            var group = d[from..j];
            if (group.Count < 2) continue;

            if (!inPlace)
            {
                // Travelling: the foot is nailed to the world for the whole
                // contact. Judged against the group's first placement.
                var anchor = group[0].FootAlong;
                var worst = group.MaxBy(f => Math.Abs(f.FootAlong - anchor));
                var slid = Math.Abs(worst.FootAlong - anchor);
                if (slid > band)
                {
                    findings.Add(new WalkFinding(WalkCheck.Slide, worst.Index,
                        $"The planted foot slides {slid:0.#} px along the ground during the contact at " +
                        $"frame {group[0].Index + 1} — worst at frame {worst.Index + 1}."));
                }
            }
            else if (group.Count >= 3)
            {
                // In place: the tread carries the foot at a constant rate, so
                // the contact's positions must sit on a line in time. Two
                // drawings fit any line; the first honest judgement needs three.
                double n = group.Count, st = 0, sa = 0, sta = 0, stt = 0;
                foreach (var f in group)
                {
                    st += f.Index; sa += f.FootAlong;
                    sta += (double)f.Index * f.FootAlong; stt += (double)f.Index * f.Index;
                }
                var det = n * stt - st * st;
                if (Math.Abs(det) < 1e-9) continue;
                var rate = (n * sta - st * sa) / det;
                var b = (sa - rate * st) / n;
                var worst = group.MaxBy(f => Math.Abs(f.FootAlong - (rate * f.Index + b)));
                var off = Math.Abs(worst.FootAlong - (rate * worst.Index + b));
                if (off > band)
                {
                    findings.Add(new WalkFinding(WalkCheck.Slide, worst.Index,
                        $"The tread is uneven under the planted foot: frame {worst.Index + 1} sits " +
                        $"{off:0.#} px off the contact's own constant rate."));
                }
            }
        }
    }
}
