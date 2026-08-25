using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;

namespace Lightbox.Core.Tests;

/// <summary>
/// The keyed-value vocabulary <c>DESIGN-effects.md</c> specified, built because
/// wind is its first user (Q122).
/// </summary>
public class EffectParamTests
{
    [Fact]
    public void An_Unkeyed_Parameter_Is_Just_Its_Value()
    {
        var p = new EffectParam(4);

        Assert.False(p.IsAnimated);
        Assert.Equal(4, p.At(0));
        Assert.Equal(4, p.At(-99));
        Assert.Equal(4, p.At(9999));
    }

    [Fact]
    public void Keys_Interpolate_With_The_Easing_The_Earlier_One_Names()
    {
        var p = new EffectParam
        {
            Keys =
            [
                new EffectKey { Frame = 0, Value = 0, Ease = Easing.Linear },
                new EffectKey { Frame = 10, Value = 100, Ease = Easing.Linear },
            ],
        };

        Assert.True(p.IsAnimated);
        Assert.Equal(0, p.At(0));
        Assert.Equal(50, p.At(5));
        Assert.Equal(100, p.At(10));
    }

    /// <summary>
    /// The rule <c>CameraOps.At</c> follows: an effect does not snap back to a
    /// default because the timeline ran past where somebody stopped keying.
    /// </summary>
    [Fact]
    public void Outside_The_Keyed_Range_The_Nearest_Key_Holds()
    {
        var p = new EffectParam(999)
        {
            Keys =
            [
                new EffectKey { Frame = 5, Value = 1 },
                new EffectKey { Frame = 15, Value = 3 },
            ],
        };

        Assert.Equal(1, p.At(0));
        Assert.Equal(1, p.At(5));
        Assert.Equal(3, p.At(15));
        Assert.Equal(3, p.At(500));
    }

    [Fact]
    public void One_Key_Is_A_Constant_At_That_Keys_Value()
    {
        var p = new EffectParam(7) { Keys = [new EffectKey { Frame = 12, Value = 2 }] };

        Assert.Equal(2, p.At(0));
        Assert.Equal(2, p.At(12));
        Assert.Equal(2, p.At(99));
    }

    /// <summary>
    /// A record arriving from an agent or a hand edit should not be able to make
    /// a document unopenable.
    /// </summary>
    [Fact]
    public void Keys_Out_Of_Order_Resolve_Rather_Than_Throw()
    {
        var p = new EffectParam
        {
            Keys =
            [
                new EffectKey { Frame = 10, Value = 100, Ease = Easing.Linear },
                new EffectKey { Frame = 0, Value = 0, Ease = Easing.Linear },
            ],
        };

        Assert.Equal(50, p.At(5));
    }

    [Fact]
    public void Easing_Bends_The_Middle_Without_Moving_The_Ends()
    {
        var eased = new EffectParam
        {
            Keys =
            [
                new EffectKey { Frame = 0, Value = 0, Ease = Easing.EaseIn },
                new EffectKey { Frame = 10, Value = 100 },
            ],
        };

        Assert.Equal(0, eased.At(0));
        Assert.Equal(100, eased.At(10));
        Assert.True(eased.At(5) < 50, "ease-in should still be below halfway at the midpoint");
    }

    // ---- the record ------------------------------------------------------------

    [Fact]
    public void An_Element_Nobody_Has_Blown_On_Writes_No_Wind()
    {
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["s"] = new() } };

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"windX\"", json);
        Assert.DoesNotContain("\"windY\"", json);
        Assert.DoesNotContain("\"hasWind\"", json);
        Assert.DoesNotContain("\"preRoll\"", json);
        Assert.False(new SimElement().HasWind);
    }

    [Fact]
    public void A_Constant_Wind_Writes_A_Value_And_No_Keys()
    {
        var element = new SimElement { WindX = new EffectParam(-0.4) };
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["s"] = element } };

        var json = DocJson.Serialize(doc);

        Assert.Contains("\"windX\"", json);
        Assert.DoesNotContain("\"keys\"", json);
        Assert.DoesNotContain("\"isAnimated\"", json);
    }

    [Fact]
    public void Keyed_Wind_Survives_A_Round_Trip()
    {
        var element = new SimElement
        {
            PreRoll = 6,
            WindX = new EffectParam
            {
                Keys =
                [
                    new EffectKey { Frame = 0, Value = -0.5, Ease = Easing.Linear },
                    new EffectKey { Frame = 20, Value = 0.5, Ease = Easing.EaseOut },
                ],
            },
        };
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["s"] = element } };

        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Sims!["s"];

        Assert.Equal(6, back.PreRoll);
        Assert.True(back.HasWind);
        Assert.Equal(2, back.WindX!.Keys!.Count);
        Assert.Equal(Easing.EaseOut, back.WindX.Keys[1].Ease);
        Assert.Equal(0, back.WindAt(10).X, 6);
        Assert.Equal(0, back.WindAt(10).Y);
    }

    // ---- painted emission (Q125) ------------------------------------------------

    [Fact]
    public void A_Plain_Emitter_Writes_No_Mask_No_Motion_And_No_Obstacle()
    {
        var element = new SimElement();
        element.Emitters.Add(new Emitter { Shape = EmitterShape.Disc, X = 4, Y = 4 });
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["s"] = element } };

        var json = DocJson.Serialize(doc);

        Assert.DoesNotContain("\"maskLayerId\"", json);
        Assert.DoesNotContain("\"motionX\"", json);
        Assert.DoesNotContain("\"motionY\"", json);
        Assert.DoesNotContain("\"travels\"", json);
        Assert.DoesNotContain("\"obstacleLayerId\"", json);
        Assert.False(element.Emitters[0].Travels);
    }

    [Fact]
    public void A_Painted_Emitter_Survives_A_Round_Trip()
    {
        var element = new SimElement { ObstacleLayerId = "costume" };
        element.Emitters.Add(new Emitter
        {
            Id = "em",
            MaskLayerId = "emission",
            MotionX = new EffectParam { Keys = [new EffectKey { Frame = 0, Value = 0 }, new EffectKey { Frame = 10, Value = 12 }] },
        });
        var doc = new Doc { Sims = new Dictionary<string, SimElement> { ["s"] = element } };

        var back = DocJson.Deserialize(DocJson.Serialize(doc)).Sims!["s"];

        Assert.Equal("costume", back.ObstacleLayerId);
        Assert.Equal("emission", back.Emitters[0].MaskLayerId);
        Assert.True(back.Emitters[0].Travels);
        Assert.Equal(6, back.Emitters[0].OriginAt(5).X, 6);
    }

    /// <summary>
    /// An offset rather than the position itself, so placing an emitter and
    /// animating it stay separate acts.
    /// </summary>
    [Fact]
    public void Motion_Is_An_Offset_From_Where_The_Emitter_Was_Placed()
    {
        var emitter = new Emitter
        {
            X = 30,
            Y = 8,
            MotionX = new EffectParam { Keys = [new EffectKey { Frame = 0, Value = 0 }, new EffectKey { Frame = 10, Value = 10 }] },
        };

        Assert.Equal((30, 8), emitter.OriginAt(0));
        Assert.Equal(40, emitter.OriginAt(10).X);
        Assert.Equal(8, emitter.OriginAt(10).Y);
    }

    [Fact]
    public void Cloning_An_Emitter_Copies_Its_Motion_Keys()
    {
        var emitter = new Emitter
        {
            MotionX = new EffectParam { Keys = [new EffectKey { Frame = 0, Value = 1 }] },
        };

        var copy = emitter.Clone();
        copy.MotionX!.Keys![0].Value = 9;

        Assert.Equal(1, emitter.MotionX!.Keys![0].Value);
    }

    [Fact]
    public void Cloning_An_Element_Copies_Its_Wind_Keys()
    {
        var element = new SimElement
        {
            WindX = new EffectParam { Keys = [new EffectKey { Frame = 0, Value = 1 }] },
        };

        var copy = element.Clone();
        copy.WindX!.Keys![0].Value = 9;

        Assert.Equal(1, element.WindX!.Keys![0].Value);
    }

    /// <summary>
    /// A character running right is wind blowing left — a change of reference
    /// frame rather than weather, and the one thing about this field an artist
    /// will get backwards.
    /// </summary>
    [Fact]
    public void A_Turn_Is_Two_Keys_And_Reads_Through_Zero()
    {
        var running = new SimElement
        {
            WindX = new EffectParam
            {
                Keys =
                [
                    new EffectKey { Frame = 0, Value = -0.6, Ease = Easing.Linear },
                    new EffectKey { Frame = 12, Value = 0.6, Ease = Easing.Linear },
                ],
            },
        };

        Assert.True(running.WindAt(0).X < 0, "she starts running right, so the wind blows left");
        Assert.Equal(0, running.WindAt(6).X, 6);
        Assert.True(running.WindAt(12).X > 0, "and finishes running left");
    }
}
