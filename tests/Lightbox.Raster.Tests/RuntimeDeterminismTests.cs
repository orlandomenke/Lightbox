using System.Security.Cryptography;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

/// <summary>
/// A fingerprint of the render path that survives leaving the process, so a
/// runtime, JIT or native-library change that alters pixels is caught instead
/// of being discovered a month later on somebody else's machine.
/// </summary>
/// <remarks>
/// <para>
/// The suite already proves the engine is a function — nothing here consults a
/// clock or an RNG, and <c>EffectBrushes_AreDeterministic_AcrossRerenders</c>,
/// <c>FillStroke_SurvivesDocumentSerialization_PixelForPixel</c> and
/// <c>OutputScaleTests</c> all pin that exactly. What none of them can prove is
/// that it is the same function it was on another runtime, because both sides
/// of every one of those comparisons is computed in the same process. A
/// migration that changed the render by one bit would pass all of them.
/// </para>
/// <para>
/// The instrument that catches that is a stored value, and the thing worth
/// storing is <em>rendered output</em> rather than <c>Hash01</c>. All three
/// <c>Hash01</c> copies are FNV-then-avalanche over <c>uint</c>/<c>int</c>
/// seeded through <c>BitConverter.SingleToInt32Bits</c> — integer arithmetic
/// with wrapping semantics the language specifies, so a runtime cannot change
/// what they return and a pinned-constant test over them would guard the thing
/// least at risk. What <em>is</em> at risk is the floating-point arithmetic
/// around them: scatter turns a hash into an angle and moves the dab by
/// <c>Math.Cos</c>/<c>Math.Sin</c>, and transcendental results are not
/// guaranteed bit-identical across runtime versions. A one-ULP shift moves a
/// dab a fraction of a pixel, which changes an antialiased edge, which changes
/// the pixels — and looks like nothing until two machines disagree.
/// </para>
/// <para>
/// The three scenarios are chosen to localise a failure rather than merely
/// report one. <c>jitter</c> is the only one that reaches the transcendental
/// path; <c>soft</c> isolates the coverage-and-blend arithmetic with every
/// stochastic control off; <c>hard-aa</c> isolates the antialiased edge. Jitter
/// alone moving points at the hash-fed float math. All three moving together
/// points at Skia or the blend path, which is a native-library question rather
/// than a runtime one.
/// </para>
/// <para>
/// Stroke geometry is literal coordinates on purpose. Generating the path with
/// <c>Math.Sin</c>, as <c>OutputScaleTests</c> does, would fold the stability
/// of the input into a measurement that is supposed to be about the render.
/// </para>
/// <para>
/// See <c>docs/DESIGN-net10-upgrade.md</c>. The ordering constraint there is
/// the whole point of this file: the baseline has to be recorded on the old
/// runtime <em>before</em> the target framework moves. Recorded afterwards it
/// captures the new runtime's output as the reference and destroys the only
/// evidence that would have shown a change.
/// </para>
/// </remarks>
[Collection("Registries")]
public class RuntimeDeterminismTests(ITestOutputHelper output)
{
    private const int W = 200, H = 140;

    /// <summary>
    /// The fingerprint recorded on a known-good runtime, or empty when none has
    /// been recorded yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recorded on .NET 8.0.29 / SkiaSharp 3.119.4, linux-x64, before any
    /// TargetFramework moves.</b> That is what the stored value is evidence
    /// <em>about</em> — the numbers mean nothing without the runtime they came
    /// from, since a mismatch is only diagnosable against a named starting
    /// point. Debug and Release produce the same three hashes, checked when
    /// this was taken, so the guard does not depend on configuration.
    /// </para>
    /// <para>
    /// Empty was the shipped state until now, and deliberately: a value
    /// invented by anything other than a real run on the runtime being left
    /// behind is worse than no value, because it would fail for a reason nobody
    /// could act on. While empty,
    /// <see cref="TheFingerprint_MatchesTheRecordedBaseline"/> logged what it
    /// computed and passed — an instrument rather than a guard. Filling it in is
    /// step 2 of the order in <c>docs/DESIGN-net10-upgrade.md</c>, and it only
    /// counts because it happened before the TFM bump in step 4. B55.
    /// </para>
    /// <para>
    /// A mismatch after an <em>intentional</em> engine change is expected and
    /// the entry is re-recorded — with the pixel change reviewed on its own
    /// merits first, because "re-record the baseline" is also exactly what
    /// somebody would do to make a real regression go quiet. Which scenarios
    /// moved is the diagnosis: see the class remarks.
    /// </para>
    /// <para>
    /// <b><c>hard-aa</c> was re-recorded on 2026-08-23</b>, when hard-edged
    /// brushes stopped accumulating coverage per dab and started drawing their
    /// mark as one silhouette (<c>BrushEngine.DrawsAsOneSilhouette</c>). The
    /// pixels genuinely changed and were meant to: overlapping antialiased dabs
    /// had been saturating the mark's own rim, which cost a size-5 Ink stroke
    /// 17.7% of its width at the worst sub-pixel position. Q156 records the
    /// decision to change art already drawn rather than gate the fix.
    /// </para>
    /// <para>
    /// <b><c>jitter</c> and <c>soft</c> were re-recorded on 2026-08-24</b>, when
    /// a soft brush's mark stopped saturating past its own footprint (Q157,
    /// <c>BrushEngine.NeedsFootprintCap</c>). Both carry hardness below 1 —
    /// 0.8 and 0.25 — so both are capped; <c>hard-aa</c> is hardness 1.0, takes
    /// the silhouette route instead, and came back byte-identical to the value
    /// recorded below it. Which scenarios moved is again exactly the set the
    /// change is allowed to reach.
    /// </para>
    /// <para>
    /// <b>One thing this costs, worth knowing before the next failure.</b> With
    /// <c>jitter</c> re-recorded, its value no longer dates from .NET 8: it is a
    /// .NET 10 measurement of a changed engine. The runtime-migration evidence
    /// the class was built for now rests on <c>hard-aa</c>, which is still the
    /// original figure and still the only scenario reaching the antialiased edge
    /// arithmetic. <c>jitter</c> remains the only one reaching
    /// <c>Math.Cos</c>/<c>Math.Sin</c>, so it keeps its diagnostic job for any
    /// future move — it just no longer carries a pre-migration baseline.
    /// </para>
    /// <para>
    /// <b>The other two hashes did not move, and that is the evidence rather
    /// than a convenience.</b> This is precisely the localisation the class
    /// remarks describe: <c>soft</c> is hardness 0.25 and <c>jitter</c> carries
    /// scatter and six jitters, so both are disqualified from the silhouette and
    /// both came back byte-identical. Had either shifted, the change would have
    /// reached further than its own predicate allows and the diff would have
    /// been wrong.
    /// </para>
    /// <para>
    /// <b><c>soft</c> was re-recorded on 2026-08-24</b>, when the dab walk
    /// started subdividing a spacing interval that is too coarse to resolve the
    /// dab's own soft band (B301, <c>BrushEngine.SubdividesForFidelity</c>). It
    /// is the only one of the three that could move: at spacing 0.1 against a
    /// 0.09 target it walks two dabs where it walked one, each thinned to
    /// compensate. <c>jitter</c> is at spacing 0.2 and would subdivide harder
    /// still, and did not move — scatter and its six jitters all seed from the
    /// dab position, which disqualifies it — and <c>hard-aa</c> keeps taking the
    /// silhouette route. Both came back byte-identical to the values recorded
    /// beneath, which is once more exactly the set the change is allowed to
    /// reach.
    /// </para>
    /// </remarks>
    private const string Baseline = """
        jitter=7CB9FDADF17861527AB11094451A34CA00CA025C80C302CD47472D8043507A09
        soft=5654BE3493C388CE816199F745E9C56AC8D466FC4024DD0BD6155C3F4DA7749D
        hard-aa=8AAFCED84264B48B00A4488C3F0CB1B9A48DBD9B6D625DDE356DDD243EFCF3B6
        """;

    /// <summary>Everything stochastic on at once — the only scenario that reaches
    /// <c>Math.Cos</c>/<c>Math.Sin</c> through scatter.</summary>
    private static BrushSettings Jittery() => new()
    {
        Size = 22,
        Hardness = 0.8,
        Opacity = 1,
        Flow = 0.9,
        Spacing = 0.2,
        AntiAlias = true,
        Scatter = 0.35,
        SizeJitter = 0.5,
        MinimumDiameter = 0.3,
        FlowJitter = 0.4,
        RoundnessJitter = 0.3,
        RotationJitter = 0.5,
        ColorJitter = 0.4,
        SecondaryColor = "#c04020",
        HueJitter = 0.3,
        SaturationJitter = 0.3,
        BrightnessJitter = 0.3,
    };

    /// <summary>A soft edge with nothing stochastic — coverage and blend only.</summary>
    private static BrushSettings Soft() => new()
    {
        Size = 26,
        Hardness = 0.25,
        Opacity = 1,
        Flow = 0.8,
        Spacing = 0.1,
        AntiAlias = true,
    };

    /// <summary>A hard edge with antialiasing — the edge arithmetic on its own.</summary>
    private static BrushSettings HardAntiAliased() => new()
    {
        Size = 18,
        Hardness = 1.0,
        Opacity = 1,
        Flow = 1.0,
        Spacing = 0.08,
        AntiAlias = true,
    };

    /// <summary>
    /// A fixed polyline — literal coordinates, so the input is exactly
    /// reproducible and only the render is under measurement.
    /// </summary>
    private static List<StrokePoint> Path() =>
    [
        new(25, 70, 0.35),
        new(48, 43, 0.60),
        new(71, 96, 0.85),
        new(94, 38, 1.00),
        new(117, 88, 0.75),
        new(140, 51, 0.55),
        new(163, 79, 0.40),
        new(175, 66, 0.30),
    ];

    private static string Fingerprint(BrushSettings brush)
    {
        var stroke = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = "#2050b0",
            Points = Path(),
            Brush = brush,
        };
        using var bitmap = FrameRasterizer.Rasterize([stroke], W, H, 1.0);
        return Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
    }

    /// <summary>One line per scenario, so a diff names which one moved.</summary>
    private static string Fingerprints() =>
        string.Join(
            "\n",
            $"jitter={Fingerprint(Jittery())}",
            $"soft={Fingerprint(Soft())}",
            $"hard-aa={Fingerprint(HardAntiAliased())}");

    /// <summary>
    /// The harness itself is sound: two fresh renders of the same input agree.
    /// Without this a broken <see cref="Fingerprint"/> — an empty bitmap, a
    /// stroke that never lands — would make the baseline test vacuous rather
    /// than failing, and a fingerprint of nothing is stable too.
    /// </summary>
    [Fact]
    public void TheFingerprint_IsStableWithinARun()
    {
        var first = Fingerprints();
        var second = Fingerprints();

        output.WriteLine(first);
        Assert.Equal(first, second);

        // A hash of an all-transparent canvas is perfectly reproducible and
        // measures nothing, so assert the strokes actually put ink down.
        Assert.All(
            first.Split('\n'),
            line => Assert.NotEqual(EmptyCanvasFingerprint(), line.Split('=')[1]));
    }

    /// <summary>
    /// The baseline is still recorded, because the cheapest way to silence a
    /// real pixel regression is to empty it — at which point
    /// <see cref="TheFingerprint_MatchesTheRecordedBaseline"/> goes back to
    /// logging and passing, and the silence looks exactly like health.
    /// </summary>
    /// <remarks>
    /// Emptying it is never needed to re-record either: the fingerprint test
    /// writes what it computed <em>before</em> it asserts, so a failing run
    /// already prints the block to paste.
    /// </remarks>
    [Fact]
    public void TheBaseline_IsStillRecorded()
    {
        Assert.False(
            string.IsNullOrWhiteSpace(Baseline),
            "the recorded baseline has been emptied — that turns the fingerprint test back "
            + "into an instrument that cannot fail. Re-record it from the block the "
            + "fingerprint test logs rather than blanking it.");
    }

    /// <summary>
    /// The stored value from the runtime this was recorded on. Inert until
    /// <see cref="Baseline"/> is filled in — see its remarks for why that was
    /// the shipped state and how it was recorded.
    /// </summary>
    [Fact]
    public void TheFingerprint_MatchesTheRecordedBaseline()
    {
        var actual = Fingerprints();
        output.WriteLine(actual);

        if (string.IsNullOrWhiteSpace(Baseline))
        {
            output.WriteLine(
                "\nNo baseline recorded — this test is an instrument, not a guard, "
                + "until the block above is pasted into the Baseline constant. "
                + "Record it BEFORE changing any TargetFramework: see the order in "
                + "docs/DESIGN-net10-upgrade.md.");
            return;
        }

        Assert.Equal(Baseline.Trim().ReplaceLineEndings("\n"), actual);
    }

    private static string EmptyCanvasFingerprint()
    {
        using var bitmap = FrameRasterizer.Rasterize([], W, H, 1.0);
        return Convert.ToHexString(SHA256.HashData(bitmap.GetPixelSpan()));
    }
}
