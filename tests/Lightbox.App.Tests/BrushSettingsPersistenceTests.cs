using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Tests;

/// <summary>
/// B71: a brush you nudged stays nudged — while you use other brushes, and
/// after Lightbox closes.
/// </summary>
/// <remarks>
/// <para>
/// Before this, the working brush was the only tweak that existed. Choose
/// another preset and it was gone; close the app and only the brush in hand
/// came back. An afternoon tuning three brushes kept one of them, which is the
/// report as filed: "individual brush settings are not kept for the session…
/// after closing and reopening they are back to defaults."
/// </para>
/// <para>
/// The tweak is kept <em>beside</em> the preset, never in it. Update is still
/// the only thing that writes a preset, and picking the brush again in the
/// picker still gives the saved one back — so the three moves the manual
/// promises mean what they did, and a fourth (keep it, silently) joins them.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class BrushSettingsPersistenceTests : BrushStateIsolated
{
    private static MainViewModel Vm() => VmLayers.PaperVm();

    private static BrushPreset Preset(MainViewModel vm, string id) =>
        vm.BrushPresetChoices.Single(p => p.Id == id);

    private static string Store() => File.ReadAllText(MainViewModel.BrushStorePath!);

    [AvaloniaFact]
    public void SwitchingBrushesKeepsEachOnesTweaks()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        var pencil = vm.BrushSize + 7;
        vm.BrushSize = pencil;

        vm.SelectedBrushPreset = Preset(vm, "builtin-soft-round");
        var soft = vm.BrushSize + 11;
        vm.BrushSize = soft;

        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        Assert.Equal(pencil, vm.BrushSize);
        Assert.True(vm.BrushIsModified);

        vm.SelectedBrushPreset = Preset(vm, "builtin-soft-round");
        Assert.Equal(soft, vm.BrushSize);
        Assert.True(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void BrushSettingsSurviveARestart()
    {
        double pencil, soft;
        {
            var vm = Vm();
            vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
            pencil = vm.BrushSize + 7;
            vm.BrushSize = pencil;
            vm.SelectedBrushPreset = Preset(vm, "builtin-soft-round");
            soft = vm.BrushSize + 11;
            vm.BrushSize = soft;
        }

        // A new view model on the same store is a restart.
        var next = Vm();
        Assert.Equal("builtin-soft-round", next.SelectedBrushPreset?.Id);
        Assert.Equal(soft, next.BrushSize);

        next.SelectedBrushPreset = Preset(next, "builtin-pencil");
        Assert.Equal(pencil, next.BrushSize);
        Assert.True(next.BrushIsModified);
    }

    [AvaloniaFact]
    public void ATweakMadeWhereNothingPersistsStillSurvivesTheSwitch()
    {
        // The curve editor writes straight to the settings without persisting,
        // which is why leaving a preset stashes as well as every save.
        var vm = Vm();
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        vm.SetBrushDrives(Lightbox.Core.Documents.BrushDynamic.Scatter, true);
        Assert.True(vm.BrushIsModified);

        vm.SelectedBrushPreset = Preset(vm, "builtin-soft-round");
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");

        Assert.True(vm.BrushDrives(Lightbox.Core.Documents.BrushDynamic.Scatter));
    }

    [AvaloniaFact]
    public void ABrushLeftAtItsDefaultsWritesNoKey()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        vm.SelectedBrushPreset = Preset(vm, "builtin-soft-round");
        Assert.DoesNotContain("\"tweaks\"", Store());

        // A nudge put back is not a tweak either — the same rule the dot uses.
        var size = vm.BrushSize;
        vm.BrushSize = size + 5;
        Assert.Contains("\"tweaks\"", Store());
        vm.BrushSize = size;
        Assert.DoesNotContain("\"tweaks\"", Store());
    }

    [AvaloniaFact]
    public void PickingTheBrushAgainGivesTheSavedOneBack()
    {
        var vm = Vm();
        var pencil = Preset(vm, "builtin-pencil");
        vm.SelectedBrushPreset = pencil;
        var size = vm.BrushSize;
        vm.BrushSize = size + 7;

        vm.ApplyPreset(pencil);

        Assert.Equal(size, vm.BrushSize);
        Assert.False(vm.BrushIsModified);
        Assert.DoesNotContain("\"tweaks\"", Store());
        // And it stays given back: the next launch does not resurrect it.
        var next = Vm();
        Assert.Equal(size, next.BrushSize);
        Assert.False(next.BrushIsModified);
    }

    [AvaloniaFact]
    public void UpdatingAbsorbsTheTweakIntoThePreset()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        var size = vm.BrushSize + 7;
        vm.BrushSize = size;

        Assert.True(vm.UpdateSelectedPreset());

        Assert.False(vm.BrushIsModified);
        Assert.DoesNotContain("\"tweaks\"", Store());
        Assert.Equal(size, Preset(vm, "builtin-pencil").Settings.Size);
    }

    [AvaloniaFact]
    public void SavingAsNewMovesTheTweakToTheNewName()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        var original = vm.BrushSize;
        vm.BrushSize = original + 7;

        var mine = vm.SaveCurrentAsPreset("Fat pencil", []);

        Assert.Equal(original + 7, mine.Settings.Size);
        // The original is untouched, as the manual says — and untouched means
        // picking it does not hand the changes back.
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        Assert.Equal(original, vm.BrushSize);
        Assert.False(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void DeletingABrushForgetsItsTweak()
    {
        var vm = Vm();
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        var mine = vm.SaveCurrentAsPreset("Mine", []);
        vm.BrushSize += 7;
        vm.SelectedBrushPreset = Preset(vm, "builtin-pencil");
        Assert.Contains("\"tweaks\"", Store());

        Assert.True(vm.DeletePreset(mine));

        Assert.DoesNotContain("\"tweaks\"", Store());
    }
}
