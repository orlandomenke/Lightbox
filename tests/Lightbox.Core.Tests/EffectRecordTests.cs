using Lightbox.Core.Documents;
using Lightbox.Core.Effects;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// The effects record (DESIGN-effects.md): absent until authored at every
/// level, keyable in the camera's vocabulary, and evaluated as a pure
/// function of the record and the frame.
/// </summary>
public class EffectRecordTests
{
    private static Doc NewDoc() => DocumentFactory.CreateDoc(64, 48, 12);

    private static Layer ArtLayer(Doc doc) =>
        doc.Scene.Layers.First(l => !l.IsBackground);

    [Fact]
    public void ADocumentThatNeverFiltersWritesNoKeys()
    {
        var json = DocJson.Serialize(NewDoc());
        Assert.DoesNotContain("\"effects\"", json);
        Assert.DoesNotContain("\"adjusts\"", json);
    }

    [Fact]
    public void AFreshUseWritesNoDisabledAndAConstantWritesNoKeysList()
    {
        var doc = NewDoc();
        ArtLayer(doc).Effects = new EffectStack
        {
            Uses = [new EffectUse
            {
                Kind = "grade.levels",
                Params = { ["gamma"] = new EffectParam { Value = 1.2 } },
            }],
        };

        var json = DocJson.Serialize(doc);
        Assert.Contains("\"effects\"", json);
        Assert.Contains("\"gamma\"", json);
        Assert.DoesNotContain("\"disabled\"", json);
        Assert.DoesNotContain("\"keys\"", json);
        Assert.DoesNotContain("\"colors\"", json); // absent until authored (Q153)
    }

    [Fact]
    public void TheStackMasterSwitchMutesEverythingAndStaysAbsentWhileOn()
    {
        // Q158: one click off, without touching any use's own switch — so
        // five tuned styles come back exactly as they were.
        var doc = NewDoc();
        var stack = new EffectStack { Uses = [new EffectUse { Kind = "grade.levels" }] };
        ArtLayer(doc).Effects = stack;

        Assert.True(stack.AppliesAnything);
        Assert.True(ArtLayer(doc).HasLiveEffects);
        Assert.DoesNotContain("\"disabled\"", DocJson.Serialize(doc)); // absent while it runs

        stack.Disabled = true;
        Assert.False(stack.AppliesAnything);
        Assert.False(ArtLayer(doc).HasLiveEffects);
        Assert.True(stack.Uses[0].Applies); // the use's own switch untouched

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        Assert.True(ArtLayer(back).Effects!.Disabled);
        Assert.False(ArtLayer(back).HasLiveEffects);
    }

    [Fact]
    public void AnAuthoredColourRoundTripsAndCloneDoesNotShareIt()
    {
        var doc = NewDoc();
        var use = new EffectUse
        {
            Kind = "style.outerGlow",
            Colors = new() { ["color"] = "#ff8800" },
        };
        ArtLayer(doc).Effects = new EffectStack { Uses = [use] };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var loaded = Assert.Single(ArtLayer(back).Effects!.Uses);
        Assert.Equal("#ff8800", loaded.ColorAt("color", "#000000"));
        Assert.Equal("#123456", loaded.ColorAt("unset", "#123456")); // fallback path

        var clone = loaded.Clone();
        clone.Colors!["color"] = "#00ff00";
        Assert.Equal("#ff8800", loaded.Colors!["color"]);
    }

    [Fact]
    public void AnAdjustmentLayerWithAKeyedParamSurvivesTheRoundTrip()
    {
        var doc = NewDoc();
        var layer = ArtLayer(doc);
        layer.Adjusts = true;
        layer.Effects = new EffectStack
        {
            Uses = [new EffectUse
            {
                Kind = "blur.gaussian",
                Params =
                {
                    ["radius"] = new EffectParam
                    {
                        Value = 4,
                        Keys =
                        [
                            new EffectKey { Frame = 0, Value = 0, Ease = Easing.Linear },
                            new EffectKey { Frame = 10, Value = 8 },
                        ],
                    },
                },
            }],
        };

        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var loaded = ArtLayer(back);

        Assert.True(loaded.IsAdjustment);
        var use = Assert.Single(loaded.Effects!.Uses);
        Assert.Equal("blur.gaussian", use.Kind);
        Assert.True(use.Applies);
        Assert.Equal(2, use.Params["radius"].Keys!.Count);
        Assert.Equal(Easing.Linear, use.Params["radius"].Keys![0].Ease);
    }

    [Fact]
    public void AnUnknownKindIsPreservedNotDropped()
    {
        // A document from a newer build round-trips through this one with the
        // stranger intact — forward compatibility is what the string id buys.
        var doc = NewDoc();
        ArtLayer(doc).Effects = new EffectStack
        {
            Uses = [new EffectUse { Kind = "warp.ripple" }],
        };
        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        Assert.Equal("warp.ripple", Assert.Single(ArtLayer(back).Effects!.Uses).Kind);
    }

    [Fact]
    public void SceneEffectsAreAbsentUntilAuthoredAndRoundTrip()
    {
        var doc = NewDoc();
        doc.Scene.Effects = new EffectStack
        {
            Uses = [new EffectUse { Kind = "grade.hsl" }],
        };
        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        Assert.Equal("grade.hsl", Assert.Single(back.Scene.Effects!.Uses).Kind);
    }

    [Fact]
    public void AConstantEvaluatesToItselfOnEveryFrame()
    {
        var param = new EffectParam { Value = 3.5 };
        Assert.Equal(3.5, param.At(0));
        Assert.Equal(3.5, param.At(999));
    }

    [Fact]
    public void KeysHoldOutsideTheRangeAndEaseBetween()
    {
        var param = new EffectParam
        {
            Value = 99, // ignored once keys exist
            Keys =
            [
                new EffectKey { Frame = 10, Value = 0, Ease = Easing.Linear },
                new EffectKey { Frame = 20, Value = 10, Ease = Easing.Linear },
            ],
        };

        Assert.Equal(0, param.At(0));    // hold before
        Assert.Equal(0, param.At(10));
        Assert.Equal(5, param.At(15));   // linear midpoint
        Assert.Equal(10, param.At(20));
        Assert.Equal(10, param.At(99));  // hold after
    }

    [Fact]
    public void EasingShapesTheTravel()
    {
        var param = new EffectParam
        {
            Keys =
            [
                new EffectKey { Frame = 0, Value = 0, Ease = Easing.EaseIn },
                new EffectKey { Frame = 10, Value = 10 },
            ],
        };
        // EaseIn at t=0.5 is 0.25 of the travel.
        Assert.Equal(2.5, param.At(5), 10);
    }

    [Fact]
    public void AParamTheUseNeverAuthoredFallsBack()
    {
        var use = new EffectUse { Kind = "grade.levels" };
        Assert.Equal(1.0, use.At("gamma", 0, fallback: 1.0));
    }

    [Fact]
    public void ADisabledUseStillRoundTripsItsSettings()
    {
        var doc = NewDoc();
        ArtLayer(doc).Effects = new EffectStack
        {
            Uses = [new EffectUse
            {
                Kind = "grade.levels",
                Disabled = true,
                Params = { ["gamma"] = new EffectParam { Value = 2 } },
            }],
        };
        var back = DocJson.Deserialize(DocJson.Serialize(doc));
        var use = Assert.Single(ArtLayer(back).Effects!.Uses);
        Assert.False(use.Applies);
        Assert.False(ArtLayer(back).HasLiveEffects);
        Assert.Equal(2, use.Params["gamma"].Value);
    }

    [Fact]
    public void CloningALayerCopiesTheStackRatherThanSharingIt()
    {
        var layer = new Layer
        {
            Effects = new EffectStack { Uses = [new EffectUse { Kind = "grade.hsl" }] },
        };
        var copy = layer.Clone();
        copy.Effects!.Uses.Clear();
        Assert.Single(layer.Effects!.Uses);
    }
}
