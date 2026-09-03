using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests.Inbetween;

/// <summary>
/// The verifier is tested against <b>known-bad</b> frames, not known-good
/// ones: hand-built candidates that fail each check exactly once, so a check
/// that can never fire is caught. That is the <c>BadStrokes</c> off-canvas
/// lesson — a check that cannot fire is worse than no check
/// (<c>docs/DESIGN-ai-correctness.md</c>).
/// </summary>
/// <remarks>
/// Everything here runs with <see cref="Easing.Linear"/> so the expected
/// positions are arithmetic a reader can do in the margin. The keys are two
/// labelled strokes — an "arm" swinging down 80px and a static "post" — plus
/// whatever a scenario adds.
/// </remarks>
public class InbetweenVerifierTests(ITestOutputHelper output)
{
    private static Stroke Line(string? label, double x1, double y1, double x2, double y2) => new()
    {
        Label = label,
        Points = [new(x1, y1, 0.6), new(x2, y2, 0.6)],
        Brush = new BrushSettings { Size = 4 },
    };

    /// <summary>The arm swings from y=40 to y=120; the post never moves.</summary>
    private static List<Stroke> KeyA() => [Line("arm", 40, 40, 120, 40), Line("post", 160, 40, 160, 120)];

    private static List<Stroke> KeyB() => [Line("arm", 40, 120, 120, 120), Line("post", 160, 40, 160, 120)];

    private static InbetweenRunJudgement Verify(params CandidateInbetween[] run) =>
        InbetweenVerifier.Verify(KeyA(), KeyB(), run, Easing.Linear);

    private static CandidateInbetween Deterministic(double t) =>
        new(t, Inbetweener.Inbetween(KeyA(), KeyB(), t, Easing.Linear));

    private static Stroke Translated(Stroke s, double dx, double dy)
    {
        var copy = s.Clone();
        copy.Points = s.Points.Select(p => new StrokePoint(p.X + dx, p.Y + dy, p.Pressure)).ToList();
        return copy;
    }

    // ---- the answer that must always pass ---------------------------------

    [Fact]
    public void TheDeterministicAnswerPassesEveryCheck()
    {
        var judged = Verify(Deterministic(0.25), Deterministic(0.5), Deterministic(0.75));

        foreach (var f in judged.Frames) output.WriteLine($"t={f.T}: {f.Refusal ?? "accepted"}");
        Assert.True(judged.AllAccepted);
    }

    [Fact]
    public void TooCloseToTheDeterministicAnswerIsANoteNeverAVeto()
    {
        // Q33: distance from the deterministic answer is evidence about cost,
        // never about correctness. The model redrawing the free answer is
        // reported — the artist paid for nothing — and never refused.
        var judged = Verify(Deterministic(0.5));

        Assert.True(judged.AllAccepted);
        Assert.Contains(judged.Frames[0].Notes, n => n.Contains("added nothing"));
    }

    // ---- one refusal per check, so every check is proven able to fire -----

    [Fact]
    public void ATimingOutsideTheKeysIsRefused()
    {
        var strokes = Inbetweener.Inbetween(KeyA(), KeyB(), 0.5, Easing.Linear);
        var judged = Verify(new CandidateInbetween(1.2, strokes));

        Assert.Contains("timing", judged.Frames[0].Refusal);
    }

    [Fact]
    public void AFrameWhereNothingWouldMarkIsRefused()
    {
        var judged = Verify(new CandidateInbetween(0.5, []));

        Assert.Contains("nothing was drawn", judged.Frames[0].Refusal);
    }

    [Fact]
    public void ACopiedKeyIsRefused()
    {
        // The failure a small model actually produces: well-formed, plausible,
        // and not an inbetween — key A handed back with the t it was asked for.
        var judged = Verify(new CandidateInbetween(0.5, KeyA()));

        output.WriteLine(judged.Frames[0].Refusal!);
        Assert.Contains("“arm” did not stay between the keys", judged.Frames[0].Refusal);
    }

    [Fact]
    public void ADroppedStrokeIsRefused()
    {
        // Both keys draw the post; a frame without it loses ink the artist
        // drew. "Drops strokes" is the measured weakness of small models.
        var strokes = Inbetweener.Inbetween(KeyA(), KeyB(), 0.5, Easing.Linear)
            .Where(s => s.Label != "post")
            .ToList();
        var judged = Verify(new CandidateInbetween(0.5, strokes));

        Assert.Contains("“post” went missing", judged.Frames[0].Refusal);
    }

    [Fact]
    public void NewInkExplainedByNothingIsRefused()
    {
        // A stroke in empty space: nothing vacated it, it trails nothing, and
        // it is nowhere near the drawing. This is the one tier that refuses.
        var strokes = Inbetweener.Inbetween(KeyA(), KeyB(), 0.5, Easing.Linear);
        strokes.Add(Line(null, 10, 190, 30, 190));
        var judged = Verify(new CandidateInbetween(0.5, strokes));

        output.WriteLine(judged.Frames[0].Refusal!);
        Assert.Contains("explained by nothing", judged.Frames[0].Refusal);
    }

    [Fact]
    public void AVolumeCollapseIsRefused()
    {
        // Volume is conserved as area, not length: this square shrinks to an
        // eighth of its area mid-motion, which no squash explains.
        var judged = InbetweenVerifier.Verify(
            [ClosedBox("box", 40, 40, 40)],
            [ClosedBox("box", 100, 40, 40)],
            [new CandidateInbetween(0.5, [ClosedBox("box", 82.5, 52.5, 15)])],
            Easing.Linear);

        output.WriteLine(judged.Frames[0].Refusal!);
        Assert.Contains("volume", judged.Frames[0].Refusal);
    }

    [Fact]
    public void ASquashThatWidensConservesVolumeAndPasses()
    {
        // Squash and stretch are the principle, not the error: 40×40 becomes
        // 57×28 — same area, different shape — and must pass.
        var candidate = ClosedRect("box", 90 - 28.5, 60 - 14, 57, 28);
        var judged = InbetweenVerifier.Verify(
            [ClosedBox("box", 40, 40, 40)],
            [ClosedBox("box", 100, 40, 40)],
            [new CandidateInbetween(0.5, [candidate])],
            Easing.Linear);

        output.WriteLine(judged.Frames[0].Refusal ?? "accepted");
        Assert.True(judged.AllAccepted);
    }

    // ---- new ink the feature exists to allow -------------------------------

    [Fact]
    public void RevealedInkBehindTheMoverIsLicensed()
    {
        // An occluder slides right off a body line; the candidate extends the
        // body into the vacated region, joining the visible end — disocclusion
        // with continuation, the tier the design doc calls the valuable one.
        List<Stroke> a = [Line("occluder", 60, 40, 60, 120), Line("body", 40, 80, 55, 80)];
        List<Stroke> b = [Line("occluder", 140, 40, 140, 120), Line("body", 40, 80, 55, 80)];

        var candidate = new List<Stroke>
        {
            Line("occluder", 100, 40, 100, 120),
            Line("body", 40, 80, 55, 80),
            Line(null, 55, 80, 64, 80), // revealed: continues the body where the occluder was
        };
        var judged = InbetweenVerifier.Verify(a, b, [new CandidateInbetween(0.5, candidate)], Easing.Linear);

        output.WriteLine(judged.Frames[0].Refusal ?? string.Join(" | ", judged.Frames[0].Notes));
        Assert.True(judged.AllAccepted);
        Assert.Contains(judged.Frames[0].Notes, n => n.Contains("revealed"));
    }

    [Fact]
    public void RevealedInkThatContinuesNothingIsRefused()
    {
        // Same vacated region, but the ink floats — it joins no stroke, so it
        // is "drew something in the hole", not "drew the body behind the arm".
        List<Stroke> a = [Line("occluder", 60, 40, 60, 120), Line("body", 40, 80, 55, 80)];
        List<Stroke> b = [Line("occluder", 140, 40, 140, 120), Line("body", 40, 80, 55, 80)];

        var candidate = new List<Stroke>
        {
            Line("occluder", 100, 40, 100, 120),
            Line("body", 40, 80, 55, 80),
            Line(null, 55, 55, 64, 55), // in the vacated region, touching nothing
        };
        var judged = InbetweenVerifier.Verify(a, b, [new CandidateInbetween(0.5, candidate)], Easing.Linear);

        output.WriteLine(judged.Frames[0].Refusal ?? "accepted");
        Assert.False(judged.AllAccepted);
        Assert.Contains("explained by nothing", judged.Frames[0].Refusal);
    }

    [Fact]
    public void DragBehindTheMotionIsLicensed()
    {
        // Follow-through trails its mover: the arm swings down, the ink hangs
        // behind (above) it. Deviation pointing backwards along the travel.
        var strokes = Inbetweener.Inbetween(KeyA(), KeyB(), 0.5, Easing.Linear);
        strokes.Add(Line(null, 70, 60, 90, 60));
        var judged = Verify(new CandidateInbetween(0.5, strokes));

        output.WriteLine(judged.Frames[0].Refusal ?? string.Join(" | ", judged.Frames[0].Notes));
        Assert.True(judged.AllAccepted);
        Assert.Contains(judged.Frames[0].Notes, n => n.Contains("drag"));
    }

    [Fact]
    public void AThreeStrokeTailTrailingTheMotionIsLicensed()
    {
        // The design doc's flagship case: fur, cloth, tails following the
        // motion — drawn as a chain, where only the base touches the body.
        // The first shipped cut refused everything past a stub, because it
        // measured the ink's centroid instead of its nearest point and judged
        // each segment against the mover instead of the chain it hangs off.
        // This test is the art-director veto that rewrote the licensing.
        var strokes = Inbetweener.Inbetween(KeyA(), KeyB(), 0.5, Easing.Linear);
        strokes.Add(Line(null, 80, 80, 80, 60));  // base: hangs on the arm
        strokes.Add(Line(null, 80, 60, 80, 40));  // mid: hangs on the base
        strokes.Add(Line(null, 80, 40, 80, 25));  // tip: hangs on the mid

        var judged = Verify(new CandidateInbetween(0.5, strokes));

        foreach (var f in judged.Frames) output.WriteLine(f.Refusal ?? string.Join(" | ", f.Notes));
        Assert.True(judged.AllAccepted);
    }

    [Fact]
    public void ALongStrandHangingOffTheDrawingIsLicensed()
    {
        // A 60px hair strand attached to a static line. Under the centroid
        // measurement its own length was what failed — a 20px strand passed
        // and this one did not, with the dial changing nothing. Proximity is
        // the ink's nearest point, so attachment licenses it at any length.
        var strokes = Inbetweener.Inbetween(KeyA(), KeyB(), 0.5, Easing.Linear);
        strokes.Add(Line(null, 160, 120, 160, 180)); // pendant from the post's end

        var judged = Verify(new CandidateInbetween(0.5, strokes));

        output.WriteLine(judged.Frames[0].Refusal ?? string.Join(" | ", judged.Frames[0].Notes));
        Assert.True(judged.AllAccepted);
    }

    [Fact]
    public void InkLeadingTheMotionIsNotDrag()
    {
        // Fur that leads the jump is not follow-through, it is a different
        // drawing. The same offset in front of the travel licenses nothing.
        var strokes = Inbetweener.Inbetween(KeyA(), KeyB(), 0.5, Easing.Linear);
        strokes.Add(Line(null, 70, 105, 90, 105));
        var judged = Verify(new CandidateInbetween(0.5, strokes));

        output.WriteLine(judged.Frames[0].Refusal ?? "accepted");
        Assert.Contains("explained by nothing", judged.Frames[0].Refusal);
    }

    // ---- the run, not the frame --------------------------------------------

    [Fact]
    public void PerFrameJitterIsRefusedAsIncoherent()
    {
        // Each frame is individually between the keys; the middle one is
        // shoved sideways against its neighbours. No single-frame check can
        // see this — it is exactly the noise that reads as boiling at 12fps.
        var wobbled = Deterministic(0.5).Strokes
            .Select(s => s.Label == "arm" ? Translated(s, 18, 0) : s)
            .ToList();
        var judged = Verify(
            Deterministic(0.25),
            new CandidateInbetween(0.5, wobbled),
            Deterministic(0.75));

        output.WriteLine(judged.Frames[1].Refusal ?? "accepted");
        Assert.True(judged.Frames[0].Accepted);
        Assert.Contains("jitters", judged.Frames[1].Refusal);
        Assert.True(judged.Frames[2].Accepted);
    }

    [Fact]
    public void ASmoothArcAcrossTheRunPasses()
    {
        // The same magnitude of deviation, drifting smoothly — an arc the keys
        // never stated, which is interpretation working as intended.
        CandidateInbetween Bowed(double t, double dx) => new(t,
            Deterministic(t).Strokes.Select(s => s.Label == "arm" ? Translated(s, dx, 0) : s).ToList());

        var judged = Verify(Bowed(0.25, 8), Bowed(0.5, 12), Bowed(0.75, 8));

        foreach (var f in judged.Frames) output.WriteLine($"t={f.T}: {f.Refusal ?? "accepted"}");
        Assert.True(judged.AllAccepted);
    }

    /// <summary>
    /// A pendulum swung to the wrong side of its own pivot is refused (B360).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The keys are the committed <c>arc</c> golden pair, on purpose.</b>
    /// This is not a constructed edge case — it is the exact drawing a boxes-only
    /// reading produced during the Q180 experiment, and every arm of that
    /// experiment scored it <c>Arc: clean (1/1)</c>. A rod pivots at
    /// <c>(128,128)</c> and reaches up-left in one key, up-right in the other;
    /// both keys are entirely above the pivot, and the mirrored answer hangs
    /// below it.
    /// </para>
    /// <para>
    /// <b>Why it used to pass is the whole lesson: the centroid is not a
    /// drawing.</b> Betweenness was the distance between two centres of mass,
    /// and reflecting a rod through its midpoint moves its centroid by exactly
    /// as much as the correct answer does — 21.6 px in both directions. The old
    /// check could not have caught this at any threshold, because the two
    /// numbers were equal.
    /// </para>
    /// </remarks>
    [Fact]
    public void AMirroredArcIsRefusedBecauseItIsNotBetweenTheKeys()
    {
        var keyA = new List<Stroke> { Rod((128, 128), (40, 60)) };
        var keyB = new List<Stroke> { Rod((128, 128), (216, 60)) };

        InbetweenJudgement Judge(Stroke answer) =>
            InbetweenVerifier.Verify(
                keyA, keyB, [new CandidateInbetween(0.5, [answer])], Easing.Linear).Frames[0];

        // The rod straight up: the arc the keys imply, and the answer to keep.
        var correct = Judge(Rod((128, 128), (128, 16.8)));
        // The same rod reflected through the interpolated position: pivot
        // displaced 68px, tip 111px the wrong way, below a swing that never
        // goes below its pivot.
        var mirrored = Judge(Rod((128, 60), (128, 171.2)));

        output.WriteLine($"correct : {correct.Refusal ?? "accepted"}");
        output.WriteLine($"mirrored: {mirrored.Refusal ?? "accepted"}");

        Assert.True(correct.Accepted, $"the real arc must survive: {correct.Refusal}");
        Assert.False(mirrored.Accepted, "a rod hanging below a pivot both keys stay above is not between them");
        Assert.Equal(InbetweenFault.NotBetween, mirrored.Fault);
    }

    /// <summary>
    /// The chord answer still passes, so the fix refuses wrongness rather than
    /// interpretation.
    /// </summary>
    /// <remarks>
    /// The verifier's stated latitude is to reject "not between the keys at
    /// all" and never "not where I would have put it" (Q33). A straight
    /// interpolation is the most defensible answer there is, so if tightening
    /// betweenness had made *it* fail, the fix would have been a regression
    /// wearing a bug fix's clothes.
    /// </remarks>
    [Fact]
    public void TheChordAnswerIsStillAccepted_TighteningRefusedWrongnessNotInterpretation()
    {
        var keyA = new List<Stroke> { Rod((128, 128), (40, 60)) };
        var keyB = new List<Stroke> { Rod((128, 128), (216, 60)) };
        var judged = InbetweenVerifier.Verify(
            keyA, keyB,
            [new CandidateInbetween(0.5, [Rod((128, 128), (128, 60))])], Easing.Linear).Frames[0];

        output.WriteLine(judged.Refusal ?? $"accepted; notes: {string.Join("; ", judged.Notes)}");
        Assert.True(judged.Accepted);
        Assert.True(judged.MatchesDeterministic, "the chord is the deterministic answer and should say so");
    }

    /// <summary>
    /// A stroke recorded end-to-start is the same mark, and is not refused for
    /// it.
    /// </summary>
    /// <remarks>
    /// The cost of comparing along a stroke rather than between two centroids
    /// is that point order suddenly matters — so <c>ShapeDeviation</c> measures
    /// both traversals and keeps the better. Without that, this correct answer
    /// would be refused for a property no artist can see, which would be a
    /// worse bug than B360.
    /// </remarks>
    [Fact]
    public void AnAnswerDrawnEndToStartIsStillTheSameDrawing()
    {
        var keyA = new List<Stroke> { Rod((128, 128), (40, 60)) };
        var keyB = new List<Stroke> { Rod((128, 128), (216, 60)) };
        var judged = InbetweenVerifier.Verify(
            keyA, keyB,
            [new CandidateInbetween(0.5, [Rod((128, 16.8), (128, 128))])], Easing.Linear).Frames[0];

        output.WriteLine(judged.Refusal ?? "accepted");
        Assert.True(judged.Accepted, $"a reversed but identical stroke must pass: {judged.Refusal}");
    }

    // ---- helpers ------------------------------------------------------------

    private static Stroke Rod((double X, double Y) a, (double X, double Y) b) => new()
    {
        Label = "rod",
        Points = [new(a.X, a.Y, 0.6), new(b.X, b.Y, 0.6)],
        Brush = new BrushSettings { Size = 4 },
    };

    private static Stroke ClosedBox(string label, double x, double y, double side) =>
        ClosedRect(label, x, y, side, side);

    private static Stroke ClosedRect(string label, double x, double y, double w, double h) => new()
    {
        Label = label,
        Points =
        [
            new(x, y, 0.6), new(x + w, y, 0.6), new(x + w, y + h, 0.6),
            new(x, y + h, 0.6), new(x, y, 0.6),
        ],
        Brush = new BrushSettings { Size = 4 },
    };
}
