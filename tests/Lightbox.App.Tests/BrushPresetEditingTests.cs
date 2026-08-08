using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Editing the brush you are on: the modified indicator, updating in place,
/// saving a copy, reverting a shipped brush, and filing brushes under tags.
/// </summary>
[Collection("BrushState")]
public class BrushPresetEditingTests : BrushStateIsolated
{
    private static MainViewModel Vm() => VmLayers.PaperVm();

    private static BrushPreset Pencil(MainViewModel vm) =>
        vm.BrushPresetChoices.Single(p => p.Id == "builtin-pencil");

    // ---- the indicator ------------------------------------------------------

    [AvaloniaFact]
    public void APresetJustChosenIsNotModified()
    {
        var vm = Vm();

        vm.SelectedBrushPreset = Pencil(vm);

        Assert.False(vm.BrushIsModified);
        Assert.Equal("", vm.BrushModifiedBadge);
        Assert.False(vm.CanUpdateBrushPreset);
    }

    [AvaloniaFact]
    public void NudgingAnythingLightsTheIndicator()
    {
        // The state this exists to disambiguate: the picker says "Pencil", the
        // brush has been nudged, and without the dot nothing on screen tells
        // that apart from the pencil as shipped.
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);

        vm.BrushSize += 7;

        Assert.True(vm.BrushIsModified);
        Assert.True(vm.CanUpdateBrushPreset);
        Assert.Contains("Pencil", vm.BrushModifiedTip);
    }

    [AvaloniaFact]
    public void PuttingASettingBackClearsTheIndicator()
    {
        // Comparison by value, not a sticky "touched" flag. An artist who
        // nudged something and put it back has an unmodified brush, and a dot
        // that stayed lit would train them to ignore it.
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);
        var size = vm.BrushSize;

        vm.BrushSize = size + 5;
        vm.BrushSize = size;

        Assert.False(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void ADeepSettingCountsAsMuchAsASurfaceOne()
    {
        // The reason the comparison serializes rather than listing properties:
        // a hand-written comparer forgets the nested ones, and the failure is
        // silent — the dot says "unchanged" for a brush that is not.
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);

        vm.SetBrushCurve(BrushDynamic.Scatter, new ResponseCurve { Points = [new(0, 1), new(1, 0)] });

        Assert.True(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void AntiAliasingIsNotPartOfTheBrush()
    {
        // It lives in Configure, and the preset apply path deliberately carries
        // it across — so comparing it would light the dot on a brush nobody
        // touched, the moment somebody changed a global.
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);

        vm.AntiAliasing = !vm.AntiAliasing;

        Assert.False(vm.BrushIsModified);
    }

    // ---- updating in place --------------------------------------------------

    [AvaloniaFact]
    public void UpdatingWritesTheChangesBackAndClearsTheIndicator()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = vm.BrushPresetChoices.Single(p => p.Id == "builtin-ink");
        vm.BrushSize = 42;

        Assert.True(vm.UpdateSelectedPreset());

        Assert.False(vm.BrushIsModified);
        Assert.Equal(42, vm.SelectedBrushPreset!.Settings.Size);
    }

    [AvaloniaFact]
    public void UpdatingAShippedBrushSurvivesARestart()
    {
        // Tweaking Pencil and keeping it is the most ordinary thing anybody
        // does with a preset list. It works by shadowing: a user preset that
        // reuses the built-in's id, which the merge prefers.
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);
        vm.BrushSize = 33;
        vm.UpdateSelectedPreset();

        var reopened = Vm();

        Assert.Equal(33, Pencil(reopened).Settings.Size);
    }

    [AvaloniaFact]
    public void AShadowedBuiltInAppearsOnceAndKeepsItsPlace()
    {
        // The failure the merge exists to prevent: two Pencils in the list.
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);
        vm.BrushSize = 33;
        vm.UpdateSelectedPreset();

        var reopened = Vm();

        Assert.Single(reopened.BrushPresetChoices, p => p.Id == "builtin-pencil");
        Assert.Equal("Pencil", Pencil(reopened).Name);
    }

    [AvaloniaFact]
    public void RevertingAShippedBrushGivesBackTheOriginal()
    {
        // What "delete" means on something that is not yours to delete, and
        // why the shadow is a separate object rather than an edit in place.
        var vm = Vm();
        var shipped = Pencil(vm).Settings.Size;
        vm.SelectedBrushPreset = Pencil(vm);
        vm.BrushSize = 99;
        vm.UpdateSelectedPreset();
        Assert.True(vm.CanRevertBrushPreset);

        Assert.True(vm.RevertBrushPreset());

        Assert.Equal(shipped, Pencil(vm).Settings.Size);
        Assert.Equal(shipped, vm.BrushSize);
        Assert.False(vm.CanRevertBrushPreset);
        Assert.False(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void ThereIsNothingToRevertOnAShippedBrushNobodyTouched()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);

        Assert.False(vm.CanRevertBrushPreset);
        Assert.False(vm.RevertBrushPreset());
    }

    // ---- saving a copy, renaming, deleting ----------------------------------

    [AvaloniaFact]
    public void SavingACopyLeavesTheOriginalAlone()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);
        var shipped = Pencil(vm).Settings.Size;
        vm.BrushSize = shipped + 20;

        var saved = vm.SaveCurrentAsPreset("My pencil");

        Assert.Equal(shipped + 20, saved.Settings.Size);
        Assert.Equal(shipped, Pencil(vm).Settings.Size);
        Assert.False(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void DeletingABrushYouMadeRemovesIt()
    {
        var vm = Vm();
        var saved = vm.SaveCurrentAsPreset("Throwaway");

        Assert.True(vm.DeletePreset(saved));

        Assert.DoesNotContain(vm.BrushPresetChoices, p => p.Id == saved.Id);
        Assert.DoesNotContain(Vm().BrushPresetChoices, p => p.Id == saved.Id);
    }

    [AvaloniaFact]
    public void DeletingAShippedBrushRevertsItRatherThanRemovingIt()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);
        vm.BrushSize = 77;
        vm.UpdateSelectedPreset();

        vm.DeletePreset(Pencil(vm));

        Assert.Contains(vm.BrushPresetChoices, p => p.Id == "builtin-pencil");
        Assert.NotEqual(77, Pencil(vm).Settings.Size);
    }

    [AvaloniaFact]
    public void AShippedBrushCannotBeRenamed()
    {
        var vm = Vm();

        Assert.False(vm.RenamePreset(Pencil(vm), "Not a pencil"));
        Assert.Equal("Pencil", Pencil(vm).Name);
    }

    [AvaloniaFact]
    public void ABrushYouMadeCanBeRenamed()
    {
        var vm = Vm();
        var saved = vm.SaveCurrentAsPreset("Old name");

        Assert.True(vm.RenamePreset(saved, "  New name  "));

        Assert.Equal("New name", saved.Name);
        Assert.Equal("New name", Vm().BrushPresetChoices.Single(p => p.Id == saved.Id).Name);
    }

    // ---- tags ---------------------------------------------------------------

    [AvaloniaFact]
    public void TagsPersistAndFeedTheFilterList()
    {
        var vm = Vm();
        var saved = vm.SaveCurrentAsPreset("Liner", ["inking", "roughs"]);

        Assert.Equal(["inking", "roughs"], saved.Tags);
        Assert.Contains("inking", vm.BrushTagChoices);
        Assert.Contains("roughs", vm.BrushTagChoices);
        Assert.Equal(["inking", "roughs"], Vm().BrushPresetChoices.Single(p => p.Id == saved.Id).Tags);
    }

    [AvaloniaFact]
    public void ABrushNobodyFiledWritesNoTagsKey()
    {
        // Optional means absent, down to the file.
        var vm = Vm();

        var saved = vm.SaveCurrentAsPreset("Plain");

        Assert.Null(saved.Tags);
    }

    [AvaloniaFact]
    public void BlankAndDuplicateTagsAreDroppedRatherThanStored()
    {
        var vm = Vm();

        var saved = vm.SaveCurrentAsPreset("Messy", ["  inking ", "", "INKING", "roughs"]);

        Assert.Equal(["inking", "roughs"], saved.Tags);
    }

    [AvaloniaFact]
    public void TaggingAShippedBrushShadowsItRatherThanEditingTheList()
    {
        // BuiltInPresets.Create() rebuilds from scratch every launch, so an
        // edit in place would vanish. Filing one is an edit like any other.
        var vm = Vm();

        vm.SetPresetTags(Pencil(vm), ["favourites"]);

        Assert.Equal(["favourites"], Pencil(Vm()).Tags);
        Assert.Single(Vm().BrushPresetChoices, p => p.Id == "builtin-pencil");
    }

    [AvaloniaFact]
    public void TaggingAShippedBrushDoesNotCountAsModifyingIt()
    {
        // A tag is filing, not a setting. Lighting the "changed from Pencil"
        // dot because somebody filed it would be a lie about the mark.
        var vm = Vm();
        vm.SelectedBrushPreset = Pencil(vm);

        vm.SetPresetTags(vm.SelectedBrushPreset!, ["favourites"]);

        Assert.False(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void ATagDroppedFromTheLastBrushUsingItLeavesTheChoiceList()
    {
        var vm = Vm();
        var saved = vm.SaveCurrentAsPreset("Liner", ["inking"]);
        Assert.Contains("inking", vm.BrushTagChoices);

        vm.DeletePreset(saved);

        Assert.DoesNotContain("inking", vm.BrushTagChoices);
    }
}

/// <summary>
/// The merge that lets a user preset stand in for a shipped one.
/// </summary>
public class BuiltInPresetMergeTests
{
    [Fact]
    public void AUserPresetReusingABuiltInIdReplacesItInPlace()
    {
        var shadow = new BrushPreset { Id = "builtin-pencil", Name = "Pencil", Settings = new() { Size = 99 } };

        var merged = BuiltInPresets.Merge([shadow]);

        Assert.Single(merged, p => p.Id == "builtin-pencil");
        Assert.Equal(99, merged.Single(p => p.Id == "builtin-pencil").Settings.Size);
        Assert.Equal(BuiltInPresets.Create().Count, merged.Count);
    }

    [Fact]
    public void AShadowKeepsTheOriginalsPositionRatherThanGoingToTheEnd()
    {
        // Otherwise overwriting a brush moves it, and an artist reaching for
        // where Pencil has always been finds something else there.
        var order = BuiltInPresets.Create().Select(p => p.Id).ToList();
        var shadow = new BrushPreset { Id = "builtin-pencil", Name = "Pencil", Settings = new() };

        Assert.Equal(order, BuiltInPresets.Merge([shadow]).Select(p => p.Id));
    }

    [Fact]
    public void OrdinaryUserPresetsComeAfterTheShippedOnes()
    {
        var mine = new BrushPreset { Name = "Mine", Settings = new() };

        var merged = BuiltInPresets.Merge([mine]);

        Assert.Equal(mine.Id, merged[^1].Id);
        Assert.Equal(BuiltInPresets.Create().Count + 1, merged.Count);
    }
}

/// <summary>
/// Telling two brushes apart, which is what the modified dot is built on.
/// </summary>
public class BrushComparisonTests
{
    [Fact]
    public void TwoUntouchedBrushesAreTheSame()
    {
        Assert.True(BrushComparison.SameMark(new BrushSettings(), new BrushSettings()));
    }

    [Fact]
    public void EverySettingThatReachesPixelsIsCompared()
    {
        // Rather than a list somebody has to extend, the comparison serializes
        // — so a setting added tomorrow is covered without anybody noticing it
        // needed to be. This walks a sample across the shape of the record.
        var changes = new List<Action<BrushSettings>>
        {
            b => b.Size = 99,
            b => b.Hardness = 0.1,
            b => b.Flow = 0.2,
            b => b.Spacing = 0.9,
            b => b.Scatter = 0.4,
            b => b.TipId = "tip-builtin-spatter",
            b => b.Blend = LayerBlendMode.Multiply,
            b => b.Medium.Kind = MediumKind.Watercolour,
            b => b.Medium.Wetness = 0.9,
            b => b.Curves = new() { [BrushDynamic.Size] = ResponseCurve.Linear() },
            b => b.SecondaryColor = "#ff0000",
            b => b.TextureSurface = PaperKind.Rough,
        };

        foreach (var change in changes)
        {
            var changed = new BrushSettings();
            change(changed);
            Assert.False(BrushComparison.SameMark(new BrushSettings(), changed));
        }
    }

    [Fact]
    public void TheTwoConfigureSettingsAreNotPartOfTheBrush()
    {
        var a = new BrushSettings { AntiAlias = true, SampleSource = SampleSource.ThisLayer };
        var b = new BrushSettings { AntiAlias = false, SampleSource = SampleSource.AllLayersLive };

        Assert.True(BrushComparison.SameMark(a, b));
    }

    [Fact]
    public void ComparingDoesNotDisturbEitherBrush()
    {
        var a = new BrushSettings { AntiAlias = false, SampleSource = SampleSource.AllLayersBaked };

        BrushComparison.SameMark(a, new BrushSettings());

        Assert.False(a.AntiAlias);
        Assert.Equal(SampleSource.AllLayersBaked, a.SampleSource);
    }
}
