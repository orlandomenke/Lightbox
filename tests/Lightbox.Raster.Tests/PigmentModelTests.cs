using Lightbox.Raster.Media;
using SkiaSharp;
using Xunit.Abstractions;

namespace Lightbox.Raster.Tests;

[Collection("Performance")]
public class PigmentModelTests(ITestOutputHelper output)
{
    // A real ultramarine and a real cerulean, not the RGB primary. This matters and
    // is not a dodge: (0,0,255) means "reflects no green light whatsoever", and no
    // physical model can conjure green out of a backdrop that sends none back. Real
    // blue paint reflects a useful amount of green, which is exactly why glazing
    // yellow over a blue underpainting is a technique rather than a way to make mud.
    // The pure-primary case is pinned separately in YellowGlaze_OverPureBlue_*.
    private static readonly SKColor Ultramarine = new(40, 70, 180);
    private static readonly SKColor Cerulean = new(30, 90, 150);
    private static readonly SKColor Yellow = new(255, 214, 0);

    private static readonly SKColor[] Backdrops =
    [
        SKColors.Black, SKColors.White, new SKColor(128, 128, 128), Ultramarine,
        Cerulean, new SKColor(255, 0, 128), new SKColor(12, 200, 40), new SKColor(200, 190, 170),
    ];

    private static readonly SKColor[] Pigments =
    [
        SKColors.Black, SKColors.White, new SKColor(5, 5, 5), new SKColor(250, 250, 250),
        new SKColor(128, 128, 128), Yellow, Ultramarine, new SKColor(3, 200, 250),
        new SKColor(200, 40, 40), new SKColor(90, 20, 140),
    ];

    // --- degenerate cases ------------------------------------------------------

    [Fact]
    public void Over_ZeroThickness_ReturnsBackdropBitForBit()
    {
        foreach (var pig in Pigments)
        {
            foreach (var hiding in new[] { 0.0, 0.3, 0.7, 1.0 })
            {
                var p = Pigment.FromColor(pig, hiding);
                foreach (var bd in Backdrops)
                {
                    Assert.Equal(bd, p.Over(bd, 0.0));
                    Assert.Equal(bd, p.Over(bd, -1.0));
                }
            }
        }
    }

    [Fact]
    public void Over_ZeroThickness_ChangesNothingAtAll()
    {
        var p = Pigment.FromColor(Yellow, 0.5);
        var bd = new SKColor(40, 70, 180, 77);
        Assert.Equal(bd, p.Over(bd, 0.0));
    }

    [Fact]
    public void Over_LayingPaintDown_AddsOpacity()
    {
        // Kubelka-Munk answers "what colour is this film over that backing",
        // which presumes a backing. Preserving the backdrop's alpha — as this
        // did — meant a stroke onto an empty layer came back fully computed
        // and fully transparent, i.e. invisible. Paint adds opacity in
        // proportion to how much it hides.
        var p = Pigment.FromColor(Yellow, 0.8);

        // On an empty layer the alpha is the pigment's own hiding power at
        // that thickness — a semi-opaque paint stays semi-opaque. What must
        // not happen is alpha 0, which is what preserving the backdrop's
        // alpha produced.
        var onNothing = p.Over(new SKColor(0, 0, 0, 0), 1.0);
        Assert.True(onNothing.Alpha is > 100 and < 255,
            $"a pigment hiding 0.8 should be clearly visible but not opaque, got alpha {onNothing.Alpha}");

        var hiding = Pigment.FromColor(Yellow, 1.0);
        Assert.True(hiding.Over(new SKColor(0, 0, 0, 0), 1.0).Alpha > 240,
            "a fully hiding pigment on an empty layer should be very nearly opaque");

        var partial = p.Over(new SKColor(40, 70, 180, 77), 1.0);
        Assert.True(partial.Alpha > 77, "paint must not reduce what is already there");

        // A glaze that hides nothing adds nothing.
        var glaze = Pigment.FromColor(Yellow, 0.0);
        Assert.Equal((byte)77, glaze.Over(new SKColor(40, 70, 180, 77), 1.0).Alpha);

        // And it never exceeds opaque.
        Assert.Equal((byte)255, p.Over(new SKColor(40, 70, 180, 255), 1.0).Alpha);
    }

    [Fact]
    public void Over_AGlazeOnBlankCanvas_KeepsItsHue()
    {
        // B273. A transparent glaze is nearly all absorption, so its mass tone
        // is close to black — and the transparent-backdrop path used to back
        // the film with exactly that, turning every watercolour and ink stroke
        // on blank canvas grey. A thin transparent film is lit from behind by
        // the paper, so it must come out near the colour it has over white.
        var glaze = Pigment.FromColor(Ultramarine, 0.05);

        var onNothing = glaze.Over(new SKColor(0, 0, 0, 0), 0.6);
        var onPaper = glaze.Over(SKColors.White, 0.6);

        // The hue survives: distinctly blue, not a grey ramp.
        Assert.True(onNothing.Blue > onNothing.Red + 60,
            $"ultramarine glaze on blank canvas gave ({onNothing.Red},{onNothing.Green},{onNothing.Blue}) — grey, not blue");

        // And it is close to the over-paper colour. Not equal: the backing is
        // weighted by hiding, so a glaze at 0.05 keeps a twentieth of its own
        // mass tone. What must not come back is the near-black it used to be —
        // the mass tone's blue channel is around 29 of 255.
        Assert.True(
            Math.Abs(onNothing.Blue - onPaper.Blue) <= 12
            && Math.Abs(onNothing.Red - onPaper.Red) <= 12,
            $"blank canvas gave ({onNothing.Red},{onNothing.Green},{onNothing.Blue}), "
            + $"paper gave ({onPaper.Red},{onPaper.Green},{onPaper.Blue})");
    }

    [Fact]
    public void Over_AnOpaquePaintsEdgeIsNotBackedByThePaper()
    {
        // The regression the B273 fix nearly shipped, and the reason the
        // backing is weighted by `Hiding` rather than simply being white. A low
        // thickness means one of two different things: a thin transparent film,
        // or a partly covered pixel at the edge of an opaque stroke. For the
        // second, the compositor's own alpha is already what lets the paper
        // through, so backing it white counts the paper twice and the edge
        // dissolves — measured on the presets, a pale yellow gouache band went
        // visibly thin and lost its shape.
        //
        // The thin end is where the two answers separate, so that is where this
        // measures. A pale yellow at hiding 0.9, thickness 0.05: backed white it
        // comes out (254,247,163), which against paper is nothing at all; backed
        // by weight it comes out (233,199,109), which is the paint. Distance to
        // its own mass tone is 62 against 8 — so the threshold below has a wide
        // margin and still fails loudly on the naive version.
        var pale = new SKColor(232, 196, 106);
        var body = Pigment.FromColor(pale, 0.9);
        var blank = new SKColor(0, 0, 0, 0);
        var mass = body.MassTone;

        foreach (var thickness in new[] { 0.05, 0.15, 0.3, 0.6, 1.0 })
        {
            var got = body.Over(blank, thickness);
            var away = Distance(got, mass);
            output.WriteLine($"t={thickness}: {Show(got)} vs mass tone {Show(mass)} — {away} away");

            Assert.True(away <= 20,
                $"an opaque paint at thickness {thickness} read {Show(got)} on blank canvas, "
                + $"{away} from its own colour {Show(mass)} — its edge will dissolve into the paper");
        }
    }

    [Fact]
    public void Over_TheBackingIsIrrelevantOnceAPaintIsFullyHiding()
    {
        // Why the weight is safe to add at all: at hiding 1 the backing term
        // cancels, so `FromCoefficients` and `Mix` — which carry no dial and
        // default to 1 — behave exactly as they did before the field existed.
        var opaque = Pigment.FromColor(Ultramarine, 1.0);
        var onBlank = opaque.Over(new SKColor(0, 0, 0, 0), 1.0);
        var onWhite = opaque.Over(SKColors.White, 1.0);
        var onBlack = opaque.Over(SKColors.Black, 1.0);

        Assert.Equal(onWhite.Red, onBlank.Red);
        Assert.Equal(onWhite.Green, onBlank.Green);
        Assert.Equal(onWhite.Blue, onBlank.Blue);
        Assert.Equal(onBlack.Red, onBlank.Red);

        // And a mixed pigment, which is the path that has no dial to read.
        var mixed = Pigment.Mix(Pigment.FromColor(Yellow, 1.0), opaque, 0.5);
        var mixedBlank = mixed.Over(new SKColor(0, 0, 0, 0), 1.0);
        Assert.True(Distance(mixedBlank, mixed.MassTone) <= 2,
            $"a mixed pigment on blank canvas gave {Show(mixedBlank)} against its mass tone {Show(mixed.MassTone)}");
    }

    [Fact]
    public void Over_FullyHiding_ConvergesToMassToneWhateverIsUnderneath()
    {
        foreach (var pig in Pigments)
        {
            var p = Pigment.FromColor(pig, 1.0);
            foreach (var bd in Backdrops)
            {
                var got = p.Over(bd, 1.0);
                Assert.True(
                    Distance(got, pig) <= 2,
                    $"pigment {Show(pig)} over {Show(bd)} gave {Show(got)}");
            }
        }
    }

    [Fact]
    public void Over_FullyHiding_IsIndependentOfBackdropToTheBit()
    {
        var p = Pigment.FromColor(new SKColor(200, 40, 40), 1.0);
        var reference = p.Over(SKColors.Black, 1.0);
        foreach (var bd in Backdrops) Assert.Equal(reference, p.Over(bd, 1.0));
    }

    [Fact]
    public void Over_NoScattering_IsBeerLambert()
    {
        // S = 0 is a pure absorber: light down through the film, off the backing,
        // back up through the film, so the backdrop is attenuated by exp(-2KX).
        const double k = 1.3;
        const double x = 0.7;
        var p = Pigment.FromCoefficients(k, k, k, 0, 0, 0);

        foreach (var bd in Backdrops)
        {
            var got = p.Over(bd, x);
            foreach (var (g, b) in Channels(got, bd))
            {
                var want = Pigment.FromLinear(Pigment.ToLinear(b) * Math.Exp(-2.0 * k * x));
                Assert.InRange(Math.Abs(g - want), 0, 1);
            }
        }

        // And it really attenuates: a mid grey must come back much darker.
        var grey = p.Over(new SKColor(128, 128, 128), x);
        Assert.InRange(grey.Red, 50, 70);
    }

    [Fact]
    public void Over_NoAbsorption_IsPureScattering()
    {
        // K = 0 collapses Kubelka-Munk to SX/(SX+1) over black, and to a perfect
        // 1.0 over white — a layer that absorbs nothing cannot darken anything.
        foreach (var s in new[] { 0.05, 0.5, 4.0, 60.0 })
        {
            foreach (var x in new[] { 0.25, 1.0 })
            {
                var p = Pigment.FromCoefficients(0, 0, 0, s, s, s);

                var overBlack = p.Over(SKColors.Black, x);
                var want = Pigment.FromLinear(s * x / (s * x + 1.0));
                Assert.Equal(want, overBlack.Red);

                Assert.Equal(SKColors.White, p.Over(SKColors.White, x));
            }
        }
    }

    [Fact]
    public void Over_NoPigmentAtAll_LeavesTheBackdropAlone()
    {
        // K = S = 0 is clear medium. Distinct from thickness 0: there is a layer,
        // it just does nothing, and it must not drift by a rounding step either.
        var p = Pigment.FromCoefficients(0, 0, 0, 0, 0, 0);
        foreach (var bd in Backdrops) Assert.Equal(bd, p.Over(bd, 1.0));
    }

    [Fact]
    public void Over_DenormalThickness_DoesNotProduceGarbage()
    {
        // The X -> 0 limit runs through 1/X; unguarded, a denormal thickness makes
        // that infinite and the reflectance ratio a NaN, which encodes to 0 (black).
        var p = Pigment.FromColor(Yellow, 0.5);
        foreach (var x in new[] { double.Epsilon, 1e-320, 1e-300, 1e-12, 1e-9 })
            Assert.Equal(Ultramarine, p.Over(Ultramarine, x));

        // Just above the cut-off the model is live again but still imperceptible.
        Assert.True(Distance(p.Over(Ultramarine, 1e-7), Ultramarine) <= 1);
    }

    [Fact]
    public void Over_ExtremeInputs_StayInGamut()
    {
        foreach (var pig in Pigments)
        {
            foreach (var hiding in new[] { 0.0, 1e-9, 0.001, 0.5, 1.0 - 1e-9, 1.0, 2.0 })
            {
                var p = Pigment.FromColor(pig, hiding);
                foreach (var bd in Backdrops)
                {
                    foreach (var x in new[] { 1e-6, 0.01, 0.5, 1.0, 5.0 })
                    {
                        var c = p.Over(bd, x);
                        // A byte cannot be out of range; what this catches is NaN,
                        // which casts to 0, so assert against a black-hole result
                        // for pigments and backdrops that cannot both be black.
                        if (pig != SKColors.Black && bd != SKColors.Black && hiding > 0.9)
                            Assert.True(c.Red + c.Green + c.Blue > 0, $"{Show(pig)} h={hiding} on {Show(bd)} x={x}");
                    }
                }
            }
        }
    }

    // --- the headline: a glaze is not a dissolve -------------------------------

    [Theory]
    [InlineData(40, 70, 180)]
    [InlineData(30, 90, 150)]
    [InlineData(20, 40, 120)]
    [InlineData(60, 90, 200)]
    public void YellowGlaze_OverBlue_IsGreenerThanEveryPossibleAlphaBlend(byte r, byte g, byte b)
    {
        var blue = new SKColor(r, g, b);
        var glaze = Pigment.FromColor(Yellow, 0.0).Over(blue, 1.0);
        var (glazeHue, glazeSat, _) = Hsv(glaze);

        Assert.True(IsGreen(glazeHue), $"glaze {Show(glaze)} hue {glazeHue:F1} is not in the green band");
        Assert.True(glazeSat > 0.9, $"glaze {Show(glaze)} saturation {glazeSat:F3}");

        // Greenness measured as a luminance-normalised opponent signal in linear
        // light, G - (R+B)/2 over the total. Hue angle is useless for this
        // comparison because it goes wild near grey, and raw chroma just rewards
        // whichever result is brighter; this asks the only question that matters,
        // "how much of this colour is green".
        //
        // Beating one hand-picked alpha would prove nothing, so sweep the whole
        // range, in sRGB and in linear light, and take alpha's best shot. It loses
        // by a factor of three and a half, because averaging two colours can only
        // walk the straight line between them and that line runs from blue through
        // grey to yellow — it never passes anywhere near green.
        var best = double.MinValue;
        var bestAt = "";
        for (var i = 0; i <= 200; i++)
        {
            var a = i / 200.0;
            foreach (var (kind, blended) in new[]
                     {
                         ("srgb", AlphaOver(Yellow, blue, a)),
                         ("linear", LinearAlphaOver(Yellow, blue, a)),
                     })
            {
                var green = Greenness(blended);
                if (green <= best) continue;
                best = green;
                bestAt = $"{kind} a={a:F3} {Show(blended)}";
            }
        }

        Assert.True(
            Greenness(glaze) > 2.5 * best,
            $"glaze {Show(glaze)} greenness {Greenness(glaze):F4} vs best alpha {best:F4} ({bestAt})");
    }

    [Fact]
    public void YellowGlaze_OverBlue_IsMoreSaturatedThanAlphaBlending()
    {
        var glaze = Pigment.FromColor(Yellow, 0.0).Over(Ultramarine, 1.0);
        var (_, glazeSat, _) = Hsv(glaze);

        // Half-strength source-over is the like-for-like comparison a paint program
        // would otherwise be doing, and it lands on a desaturated khaki.
        var (_, alphaSat, _) = Hsv(AlphaOver(Yellow, Ultramarine, 0.5));
        Assert.True(glazeSat - alphaSat > 0.4, $"glaze {glazeSat:F3} vs alpha {alphaSat:F3}");

        // The glaze also beats every alpha short of simply replacing the backdrop
        // with the source colour, which is not a glaze at all.
        for (var i = 0; i <= 90; i++)
        {
            var (_, sat, _) = Hsv(AlphaOver(Yellow, Ultramarine, i / 100.0));
            Assert.True(glazeSat > sat, $"alpha {i / 100.0:F2} reached saturation {sat:F3}");
        }
    }

    [Fact]
    public void YellowGlaze_OverBlue_DarkensRatherThanAveraging()
    {
        // The tell that this is subtractive: a bright yellow over a blue comes out
        // darker than the blue. Source-over with a lighter source can only lighten.
        var glaze = Pigment.FromColor(Yellow, 0.0).Over(Ultramarine, 1.0);
        Assert.True(Luma(glaze) < Luma(Ultramarine), $"{Show(glaze)} vs {Show(Ultramarine)}");
        Assert.True(Luma(AlphaOver(Yellow, Ultramarine, 0.5)) > Luma(Ultramarine));
    }

    [Fact]
    public void YellowGlaze_OverPureBlue_GoesBlackAndSaysSoHonestly()
    {
        // A mathematically pure blue reflects no green and no red. A glaze — which
        // by definition does not scatter — has nothing to send back, so the result
        // is black. This is the model being right, not the model failing; pin it so
        // nobody "fixes" it into a lie.
        var glaze = Pigment.FromColor(Yellow, 0.0).Over(SKColors.Blue, 1.0);
        Assert.Equal(SKColors.Black, glaze);

        // Give the yellow some scattering and its own colour appears, because the
        // light now turns round inside the yellow layer instead of reaching the blue.
        var bodied = Pigment.FromColor(Yellow, 0.4).Over(SKColors.Blue, 1.0);
        var (hue, sat, val) = Hsv(bodied);
        Assert.InRange(hue, 40, 70);
        Assert.True(sat > 0.9 && val > 0.2, $"{Show(bodied)}");
    }

    // --- thickness and hiding behave like dials --------------------------------

    [Fact]
    public void Over_ThickerGlaze_MovesMonotonicallyAwayFromTheBackdrop()
    {
        var p = Pigment.FromColor(Yellow, 0.2);
        var last = -1.0;
        for (var i = 1; i <= 32; i++)
        {
            var d = Distance(p.Over(Ultramarine, i / 32.0), Ultramarine);
            Assert.True(d >= last, $"thickness {i / 32.0:F3} moved back toward the backdrop");
            last = d;
        }
        Assert.True(last > 40, $"a full-thickness glaze barely moved: {last}");
    }

    [Fact]
    public void Coverage_RisesWithHidingAndWithThickness()
    {
        var red = new SKColor(200, 40, 40);
        var last = -1.0;
        foreach (var h in new[] { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 })
        {
            var cov = Pigment.FromColor(red, h).Coverage(1.0);
            Assert.True(cov > last, $"hiding {h:F1} gave coverage {cov:F4}, not above {last:F4}");
            last = cov;
        }
        Assert.Equal(1.0, last, 6);

        var p = Pigment.FromColor(red, 0.6);
        Assert.Equal(0.0, p.Coverage(0.0));
        Assert.True(p.Coverage(0.25) < p.Coverage(1.0));
    }

    [Fact]
    public void FromColor_HidingDial_ClosesOnTheChosenColourMonotonically()
    {
        // The numbers quoted in FromColor's remarks, pinned. They matter to whoever
        // builds the swatch UI: only hiding 1 hands back exactly what was picked.
        var expected = new[] { (0.85, 45), (0.9, 30), (0.95, 16), (0.99, 4), (1.0, 0) };
        var last = int.MaxValue;
        foreach (var (h, budget) in expected)
        {
            var worst = 0;
            foreach (var pig in Pigments)
            {
                var p = Pigment.FromColor(pig, h);
                foreach (var bd in Backdrops) worst = Math.Max(worst, Distance(p.Over(bd, 1.0), pig));
            }
            Assert.True(worst <= budget, $"hiding {h:F2} drifted {worst} levels, budget {budget}");
            Assert.True(worst <= last, $"hiding {h:F2} drifted more ({worst}) than a lower setting ({last})");
            last = worst;
        }
        Assert.Equal(0, last);
    }

    [Fact]
    public void FromColor_WhiteWithNoHiding_IsInvisible()
    {
        // Not a wart: white pigment absorbs nothing, so with no scattering there is
        // no mechanism by which it can change what is underneath it.
        var p = Pigment.FromColor(SKColors.White, 0.0);
        foreach (var bd in Backdrops) Assert.True(Distance(p.Over(bd, 1.0), bd) <= 1, Show(bd));
    }

    // --- mixing ----------------------------------------------------------------

    [Fact]
    public void Mix_AtTheEnds_ReproducesTheInputsExactly()
    {
        var a = Pigment.FromColor(Yellow, 0.6);
        var b = Pigment.FromColor(Ultramarine, 0.35);
        foreach (var bd in Backdrops)
        {
            foreach (var x in new[] { 0.2, 1.0 })
            {
                Assert.Equal(a.Over(bd, x), Pigment.Mix(a, b, 0.0).Over(bd, x));
                Assert.Equal(b.Over(bd, x), Pigment.Mix(a, b, 1.0).Over(bd, x));
            }
        }
    }

    [Fact]
    public void Mix_IsContinuous()
    {
        var a = Pigment.FromColor(Yellow, 0.6);
        var b = Pigment.FromColor(Ultramarine, 0.6);

        // Continuity is "the jump goes to zero as the step does", not "the jump is
        // under some number I picked". A step discontinuity would hold its size no
        // matter how finely the interval is sampled; this refines by ten and demands
        // the largest jump at least halve each time.
        var coarse = LargestMixJump(a, b, 2000);
        var fine = LargestMixJump(a, b, 20_000);
        var finer = LargestMixJump(a, b, 200_000);
        Assert.True(fine * 2 < coarse, $"jump did not shrink: {coarse} -> {fine}");
        Assert.True(finer * 2 < fine, $"jump did not shrink: {fine} -> {finer}");
        Assert.True(finer <= 6, $"still jumping by {finer} at a step of 5e-6");

        // Away from the ends the curve is gentle enough to sample coarsely. It is
        // steep at the ends because a saturated yellow is an enormously strong
        // absorber of blue, so a trace of it swamps ultramarine's own absorption —
        // real behaviour, but worth knowing about before wiring up smudge.
        var prev = Pigment.Mix(a, b, 0.05).Over(SKColors.White, 1.0);
        for (var i = 1; i <= 2000; i++)
        {
            var cur = Pigment.Mix(a, b, 0.05 + 0.85 * i / 2000.0).Over(SKColors.White, 1.0);
            Assert.True(Distance(cur, prev) <= 2, $"jump of {Distance(cur, prev)} in the calm middle");
            prev = cur;
        }
    }

    private static int LargestMixJump(in Pigment a, in Pigment b, int steps)
    {
        var worst = 0;
        var prev = Pigment.Mix(a, b, 0.0).Over(SKColors.White, 1.0);
        for (var i = 1; i <= steps; i++)
        {
            var cur = Pigment.Mix(a, b, (double)i / steps).Over(SKColors.White, 1.0);
            worst = Math.Max(worst, Distance(cur, prev));
            prev = cur;
        }
        return worst;
    }

    [Fact]
    public void Mix_IsClampedAndSymmetricInItsEndpoints()
    {
        var a = Pigment.FromColor(Yellow, 0.6);
        var b = Pigment.FromColor(Ultramarine, 0.6);
        Assert.Equal(Pigment.Mix(a, b, 0.0).MassTone, Pigment.Mix(a, b, -3.0).MassTone);
        Assert.Equal(Pigment.Mix(a, b, 1.0).MassTone, Pigment.Mix(a, b, 9.0).MassTone);
        Assert.Equal(Pigment.Mix(a, b, 0.25).MassTone, Pigment.Mix(b, a, 0.75).MassTone);
    }

    [Fact]
    public void Mix_YellowAndBlue_MakesGreen()
    {
        // Two-constant mixing is linear in K and in S, which is the whole reason for
        // carrying two constants: mixed paint subtracts, mixed light averages.
        var mixed = Pigment.Mix(
            Pigment.FromColor(Yellow, 0.6),
            Pigment.FromColor(Ultramarine, 0.6),
            0.5).Over(SKColors.White, 1.0);
        var (hue, sat, _) = Hsv(mixed);
        Assert.True(IsGreen(hue), $"mixed paint {Show(mixed)} came out at hue {hue:F1}");
        Assert.True(sat > 0.8, $"mixed paint {Show(mixed)} saturation {sat:F3}");

        // The averaged-light answer for the same two colours is a grey-khaki.
        var (avgHue, _, _) = Hsv(AlphaOver(Yellow, Ultramarine, 0.5));
        Assert.False(IsGreen(avgHue));
    }

    // --- determinism -----------------------------------------------------------

    [Fact]
    public void Over_IsDeterministic()
    {
        // Same inputs, bit-identical outputs — and no dependence on the order the
        // caller happens to walk the region in, which is what would bite a threaded
        // or tiled repaint.
        var p = Pigment.FromColor(new SKColor(200, 40, 40), 0.45);
        var forward = new byte[256 * 3];
        for (var i = 0; i < 256; i++)
        {
            var c = p.Over(new SKColor((byte)i, (byte)(255 - i), (byte)(i * 7 % 256)), 0.01 + i / 300.0);
            forward[i * 3] = c.Red; forward[i * 3 + 1] = c.Green; forward[i * 3 + 2] = c.Blue;
        }
        for (var i = 255; i >= 0; i--)
        {
            var c = Pigment.FromColor(new SKColor(200, 40, 40), 0.45)
                .Over(new SKColor((byte)i, (byte)(255 - i), (byte)(i * 7 % 256)), 0.01 + i / 300.0);
            Assert.Equal(forward[i * 3], c.Red);
            Assert.Equal(forward[i * 3 + 1], c.Green);
            Assert.Equal(forward[i * 3 + 2], c.Blue);
        }
    }

    [Fact]
    public void FromCoefficients_AndFromColor_AgreeWhenTheyDescribeTheSameFilm()
    {
        var a = Pigment.FromColor(Ultramarine, 0.55);
        var b = Pigment.Mix(a, a, 0.5);
        foreach (var bd in Backdrops) Assert.Equal(a.Over(bd, 0.8), b.Over(bd, 0.8));
    }

    // --- linear light ----------------------------------------------------------

    [Fact]
    public void SrgbConversion_RoundTripsEverySingleLevel()
    {
        for (var i = 0; i < 256; i++)
            Assert.Equal((byte)i, Pigment.FromLinear(Pigment.ToLinear((byte)i)));
    }

    [Fact]
    public void SrgbConversion_IsTheRealTransferFunctionNotAGammaGuess()
    {
        Assert.Equal(0.0, Pigment.ToLinear(0));
        Assert.Equal(1.0, Pigment.ToLinear(255));
        // Mid grey is 21.6% of the light, not 50%. Getting this wrong is precisely
        // the mistake that makes naive paint models mix mud.
        Assert.Equal(0.21586, Pigment.ToLinear(128), 5);
        // The toe is linear, not a power curve: 10/255 / 12.92.
        Assert.Equal(10.0 / 255.0 / 12.92, Pigment.ToLinear(10), 12);

        Assert.Equal((byte)0, Pigment.FromLinear(-1.0));
        Assert.Equal((byte)255, Pigment.FromLinear(2.0));
    }

    [Fact]
    public void SrgbConversion_IsMonotonic()
    {
        var prev = -1.0;
        for (var i = 0; i < 256; i++)
        {
            var v = Pigment.ToLinear((byte)i);
            Assert.True(v > prev);
            prev = v;
        }

        byte lastByte = 0;
        for (var i = 0; i <= 4096; i++)
        {
            var enc = Pigment.FromLinear(i / 4096.0);
            Assert.True(enc >= lastByte);
            lastByte = enc;
        }
    }

    [Fact]
    public void Over_WorksInLinearLight_NotOnEncodedValues()
    {
        // A half-covering white over black lands at linear 0.5 -> sRGB 188, not at
        // the encoded midpoint 128. If this ever reads ~128 the model has been
        // quietly moved into gamma space and every mixture is wrong.
        var half = Pigment.FromCoefficients(0, 0, 0, 1, 1, 1).Over(SKColors.Black, 1.0);
        Assert.Equal(Pigment.FromLinear(0.5), half.Red);
        Assert.InRange(half.Red, 186, 190);
    }

    // --- cost ------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Performance")]
    public void Over_CostsUnderAMicrosecondPerPixel()
    {
        // A million calls is roughly a full-screen repaint. The budget is loose on
        // purpose — it catches somebody putting an allocation or a Pow-per-channel
        // detour in the inner loop, not millisecond drift. Release measures ~90 ns
        // per call on the reference machine; Debug is several times that, hence 1 us.
        var p = Pigment.FromColor(Yellow, 0.35);
        var backdrops = new SKColor[256];
        for (var i = 0; i < 256; i++)
            backdrops[i] = new SKColor((byte)i, (byte)(255 - i), (byte)(i * 7 % 256));

        // Warm the JIT before the clock starts.
        for (var i = 0; i < 10_000; i++) p.Over(backdrops[i & 255], 0.5);

        var sink = 0;
        void Million()
        {
            for (var i = 0; i < 1_000_000; i++)
                sink += p.Over(backdrops[i & 255], 0.001 + (i & 1023) / 1024.0 * 0.999).Green;
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        Million();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Fastest of three. A million calls takes long enough that a scheduler
        // slice lands inside it on a loaded machine, which is how a budget with
        // ten times the headroom still went red — see Bench.
        var fastest = Bench.FastestMs(3, Million, warm: false);

        Assert.True(sink > 0);
        Assert.True(allocated < 4096, $"Over allocated {allocated} bytes over a million calls");
        Assert.True(fastest < 1000, $"a million Over calls took {fastest:F0} ms (budget 1000 ms)");
    }

    // --- helpers ---------------------------------------------------------------

    private static bool IsGreen(double hue) => hue is >= 70 and <= 165;

    /// <summary>Luminance-normalised green-vs-magenta opponent signal in linear light.</summary>
    private static double Greenness(SKColor c)
    {
        double r = Pigment.ToLinear(c.Red), g = Pigment.ToLinear(c.Green), b = Pigment.ToLinear(c.Blue);
        var sum = r + g + b;
        return sum <= 0 ? 0 : (g - 0.5 * (r + b)) / sum;
    }

    private static int Distance(SKColor a, SKColor b) => Math.Max(
        Math.Abs(a.Red - b.Red),
        Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));

    private static IEnumerable<(byte Got, byte Backdrop)> Channels(SKColor got, SKColor bd)
    {
        yield return (got.Red, bd.Red);
        yield return (got.Green, bd.Green);
        yield return (got.Blue, bd.Blue);
    }

    private static string Show(SKColor c) => $"({c.Red},{c.Green},{c.Blue})";

    private static double Luma(SKColor c) =>
        0.2126 * Pigment.ToLinear(c.Red) + 0.7152 * Pigment.ToLinear(c.Green) + 0.0722 * Pigment.ToLinear(c.Blue);

    /// <summary>Ordinary source-over, done on encoded values the way every 8-bit compositor does it.</summary>
    private static SKColor AlphaOver(SKColor top, SKColor bottom, double a) => new(
        (byte)Math.Round(top.Red * a + bottom.Red * (1 - a)),
        (byte)Math.Round(top.Green * a + bottom.Green * (1 - a)),
        (byte)Math.Round(top.Blue * a + bottom.Blue * (1 - a)));

    /// <summary>Source-over done properly in linear light — still an average, still not paint.</summary>
    private static SKColor LinearAlphaOver(SKColor top, SKColor bottom, double a) => new(
        Pigment.FromLinear(Pigment.ToLinear(top.Red) * a + Pigment.ToLinear(bottom.Red) * (1 - a)),
        Pigment.FromLinear(Pigment.ToLinear(top.Green) * a + Pigment.ToLinear(bottom.Green) * (1 - a)),
        Pigment.FromLinear(Pigment.ToLinear(top.Blue) * a + Pigment.ToLinear(bottom.Blue) * (1 - a)));

    private static (double Hue, double Sat, double Val) Hsv(SKColor c)
    {
        double r = c.Red / 255.0, g = c.Green / 255.0, b = c.Blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double hue;
        if (d == 0) hue = 0;
        else if (max == r) hue = 60 * (((g - b) / d) % 6);
        else if (max == g) hue = 60 * ((b - r) / d + 2);
        else hue = 60 * ((r - g) / d + 4);
        if (hue < 0) hue += 360;
        return (hue, max == 0 ? 0 : d / max, max);
    }
}
