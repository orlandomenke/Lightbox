using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;

namespace Lightbox.Ai.Golden;

/// <summary>What a golden pair is asking of a model.</summary>
/// <remarks>
/// A category exists so a profile can say <b>which questions it did not ask</b>.
/// A row that reports "not measured" is a known gap; a row silently missing is
/// an unknown one, and the difference is the whole reason this is an enum
/// rather than a free-text tag on each pair.
/// </remarks>
public enum GoldenCategory
{
    /// <summary>Can it place a drawing between two keys at all.</summary>
    Swing,

    /// <summary>Does it arc, or interpolate along the chord like the free engine.</summary>
    Arc,

    /// <summary>Does it keep correspondence when the drawing turns rather than travels.</summary>
    Rotation,

    /// <summary>Does it invent something sane where a part moves off what it covered.</summary>
    Occlusion,

    /// <summary>How many strokes before it stops working — the number nobody measures.</summary>
    StrokeLadder,

    /// <summary>
    /// Complex organic subjects with hand-drawn known-good answers — a
    /// quadruped's gait, a figure turning.
    /// </summary>
    /// <remarks>
    /// <b>Ships empty, on purpose, and says so in every profile.</b> Q32 and
    /// Q33 together say the deterministic engine is weakest exactly on complex
    /// organic subjects, so a set of boxes and swinging arms would certify a
    /// model on the cases where the reference is already reliable and say
    /// nothing about the cases that actually worry anyone. The answers for
    /// these have to be *drawn*, not computed, which is why nothing here can be
    /// generated — a machine-made answer would re-introduce the exact bias the
    /// category exists to remove.
    /// </remarks>
    Organic,
}

/// <summary>
/// One keyframe pair the set puts to a model, and — where a person drew one —
/// the answer it should have given.
/// </summary>
/// <param name="Probes">
/// One sentence on what this pair is for, written for the profile output rather
/// than for the code. It is what makes a low score actionable instead of merely
/// bad news.
/// </param>
/// <param name="KnownGood">
/// A hand-drawn answer at the pair's single <c>t</c>, or null. Null is the
/// normal case and costs nothing: the verifier scores against the keys, not
/// against a reference, so betweenness, dropped strokes, licensed ink and
/// coherence are all measurable without one. A reference buys the one thing the
/// verifier cannot judge — whether the answer is *good* rather than merely
/// defensible.
/// </param>
public sealed record GoldenPair(
    string Id,
    GoldenCategory Category,
    string Probes,
    InbetweenRequest Request,
    IReadOnlyList<Stroke>? KnownGood = null);

/// <summary>
/// The committed set of keyframe pairs a model is graded on (Q34: it ships, so
/// an artist can grade their own model rather than discovering over an
/// afternoon that it cannot do the job).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every pair here is constructed, and that is a stated limit rather than a
/// shortcut.</b> Constructed geometry answers most of the profile honestly —
/// schema adherence, label retention, betweenness, correspondence and the
/// stroke ladder need no reference answer, because the verifier scores against
/// the keys. What it cannot answer is whether a model draws a *good* inbetween
/// of something organic, and <see cref="GoldenCategory.Organic"/> is where that
/// goes when somebody draws it.
/// </para>
/// <para>
/// <b>The set is a published claim.</b> Q34's obligation: it is committed,
/// reviewed, and changing it changes what Lightbox says about somebody's model.
/// Adding a pair that a good model fails is a bug in the pair until proven
/// otherwise, which is what <c>TheFreeEngineClearsEveryConstructedPair</c> is
/// for — the deterministic engine is a known quantity, so the set is checked
/// against it rather than the other way round.
/// </para>
/// </remarks>
public static class GoldenSet
{
    private const int Canvas = 256;
    private static readonly SceneInfo Scene = new(Canvas, Canvas, 12);

    /// <summary>
    /// The default run: one pair per question and a ladder capped low.
    /// </summary>
    /// <remarks>
    /// Following the brush rule — expensive is available, deliberate, and never
    /// the default. Somebody trying a local model out should not spend the cost
    /// of <see cref="Full"/> to find out it cannot parse a schema. The
    /// degradation number is coarser here and <see cref="CapabilityProfile"/>
    /// says which run produced it.
    /// </remarks>
    public static IReadOnlyList<GoldenPair> Short() => [.. Core(), .. Ladder([2, 8, 24])];

    /// <summary>
    /// Every pair, and a ladder long enough to find where most models actually
    /// break. Opt-in: the top rung alone is a ~100-stroke payload.
    /// </summary>
    public static IReadOnlyList<GoldenPair> Full() => [.. Core(), .. Ladder([2, 8, 16, 32, 64, 100])];

    private static IReadOnlyList<GoldenPair> Core() =>
    [
        new("swing", GoldenCategory.Swing,
            "An arm swinging top to bottom — the least a model can be asked for.",
            Request(
                [Line("arm", 40, 40, 216, 40)],
                [Line("arm", 40, 216, 216, 216)])),

        new("arc", GoldenCategory.Arc,
            "A pendulum whose ends are level: the honest answer dips, a chord interpolation does not.",
            Request(
                [Line("pendulum", 128, 128, 40, 60)],
                [Line("pendulum", 128, 128, 216, 60)])),

        new("rotation", GoldenCategory.Rotation,
            "A cross turning a quarter turn — travels nowhere, so only correspondence can carry it.",
            Request(
                [Line("bar-a", 60, 128, 196, 128), Line("bar-b", 128, 60, 128, 196)],
                [Line("bar-a", 128, 60, 128, 196), Line("bar-b", 196, 128, 60, 128)])),

        // Key A draws the torso in two pieces because the arm covers the middle
        // of it; by key B the arm has swung clear and the torso is whole. The
        // ink in the middle therefore exists in B and in neither piece of A,
        // which is what makes this a reveal rather than a move.
        new("occlusion", GoldenCategory.Occlusion,
            "An arm swings off the torso it covered, vacating the region the hidden middle sits in.",
            Request(
            [
                Line("torso-upper", 128, 60, 128, 100),
                Line("torso-lower", 128, 170, 128, 196),
                Line("arm", 128, 100, 128, 170),
            ],
            [
                Line("torso", 128, 60, 128, 196),
                Line("arm", 128, 100, 200, 150),
            ])),
    ];

    /// <summary>
    /// The same trivial motion at growing stroke counts. Everything except the
    /// count is held still on purpose: the rung that fails then names a
    /// *capacity*, not a difficulty, which is what makes the number usable as
    /// "send it fewer than this".
    /// </summary>
    private static IEnumerable<GoldenPair> Ladder(IReadOnlyList<int> counts) =>
        counts.Select(n => new GoldenPair(
            $"ladder-{n}",
            GoldenCategory.StrokeLadder,
            $"{n} strokes moving together — capacity, not difficulty.",
            Request(Comb(n, 40), Comb(n, 120))));

    /// <summary>A row of labelled vertical strokes, all shifted down together.</summary>
    private static List<Stroke> Comb(int count, double top)
    {
        var strokes = new List<Stroke>(count);
        // Spread across the canvas whatever the count, so a long ladder does not
        // also become a test of drawing things very close together.
        var step = (Canvas - 40.0) / Math.Max(1, count);
        for (var i = 0; i < count; i++)
        {
            var x = 20 + step * (i + 0.5);
            strokes.Add(Line($"tooth-{i}", x, top, x, top + 60));
        }
        return strokes;
    }

    private static Stroke Line(string label, double x1, double y1, double x2, double y2) => new()
    {
        Label = label,
        Points = [new(x1, y1, 0.6), new(x2, y2, 0.6)],
        Brush = new BrushSettings { Size = 4 },
    };

    /// <summary>
    /// One <c>t</c> per pair, at the midpoint, and Linear.
    /// </summary>
    /// <remarks>
    /// A profile is about capability rather than timing, and asking for several
    /// <c>t</c>s would multiply the cost of every rung of the ladder for an
    /// answer the midpoint already gives. Linear because an eased midpoint is
    /// still the midpoint, and the pairs should not depend on which easing the
    /// artist last left in the bar.
    /// </remarks>
    private static InbetweenRequest Request(IReadOnlyList<Stroke> a, IReadOnlyList<Stroke> b) =>
        new(Scene, a, b, [0.5], Easing.Linear);
}
