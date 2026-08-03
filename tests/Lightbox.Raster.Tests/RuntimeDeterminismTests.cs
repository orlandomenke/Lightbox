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
    /// Empty is the shipped state and it is deliberate: a value invented by
    /// anything other than a real run on the runtime being left behind is worse
    /// than no value, because it would fail for a reason nobody could act on.
    /// While it is empty <see cref="TheFingerprint_MatchesTheRecordedBaseline"/>
    /// logs what it computed and passes — it is an instrument, not yet a guard.
    /// </para>
    /// <para>
    /// To record it: run this class on the current runtime, take the block the
    /// test logs, and paste it here. That is step 2 of the order in
    /// <c>docs/DESIGN-net10-upgrade.md</c> and it only counts if it happens
    /// before the TFM bump in step 4.
    /// </para>
    /// <para>
    /// A mismatch after an <em>intentional</em> engine change is expected and
    /// the entry is re-recorded — with the pixel change reviewed on its own
    /// merits first, because "re-record the baseline" is also exactly what
    /// somebody would do to make a real regression go quiet.
    /// </para>
    /// </remarks>
    private const string Baseline = "";

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
    /// The stored value from the runtime this was recorded on. Inert until
    /// <see cref="Baseline"/> is filled in — see its remarks for why that is
    /// the shipped state and how to record it.
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
