using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Tests;

/// <summary>
/// Stabilisation as a brush setting rather than one global value. An inking
/// brush wants heavy lazy-mouse and a pencil wants none; that is most of what
/// the setting is for, and it is the thing a single global cannot say.
/// </summary>
[Collection("BrushState")]
public class BrushStabilisationTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null);

    [AvaloniaFact]
    public void ABrushFollowsTheApplicationUntilItIsToldNotTo()
    {
        // Absent is the ordinary state and means exactly today's behaviour.
        var vm = Vm();

        Assert.False(vm.BrushHasOwnStabilisation);
        Assert.Null(vm.CurrentToolSettingsForTest.Stabilisation);
    }

    [AvaloniaFact]
    public void TickingPerBrushChangesNothingAboutHowItDraws()
    {
        // It copies what is already in effect, so the box is about what the
        // sliders now belong to — not about the mark. A checkbox that silently
        // reset the smoothing would be unusable for its actual purpose.
        var vm = Vm();
        vm.SmoothingMode = SmoothingMode.PulledString;
        vm.LazyRadius = 60;

        vm.BrushHasOwnStabilisation = true;

        Assert.Equal(SmoothingMode.PulledString, vm.SmoothingMode);
        Assert.Equal(60, vm.LazyRadius);
    }

    [AvaloniaFact]
    public void TwoBrushesCanSteadyTheHandDifferently()
    {
        // The whole point. The brush and the eraser keep separate settings, so
        // they stand in here for two brushes.
        var vm = Vm();
        vm.ActiveTool = ToolId.Brush;
        vm.BrushHasOwnStabilisation = true;
        vm.SmoothingMode = SmoothingMode.PulledString;
        vm.LazyRadius = 80;

        vm.ActiveTool = ToolId.Eraser;
        vm.BrushHasOwnStabilisation = true;
        vm.SmoothingMode = SmoothingMode.Off;

        Assert.Equal(SmoothingMode.Off, vm.SmoothingMode);

        vm.ActiveTool = ToolId.Brush;
        Assert.Equal(SmoothingMode.PulledString, vm.SmoothingMode);
        Assert.Equal(80, vm.LazyRadius);
    }

    [AvaloniaFact]
    public void UntickingHandsTheSlidersBackAndDropsTheBrushesCopy()
    {
        // The way back to absent. Without it a brush that ever had its own
        // could never stop having one, and the file would keep the block
        // forever.
        var vm = Vm();
        vm.SmoothingMode = SmoothingMode.Laplacian;
        vm.BrushHasOwnStabilisation = true;
        vm.SmoothingMode = SmoothingMode.SavitzkyGolay;

        vm.BrushHasOwnStabilisation = false;

        Assert.Null(vm.CurrentToolSettingsForTest.Stabilisation);
        Assert.Equal(SmoothingMode.Laplacian, vm.SmoothingMode);
    }

    [AvaloniaFact]
    public void EditingABrushesOwnDoesNotMoveTheApplicationsDefault()
    {
        var vm = Vm();
        vm.SmoothingMode = SmoothingMode.Laplacian;
        vm.BrushHasOwnStabilisation = true;

        vm.SmoothingMode = SmoothingMode.Ema;
        vm.BrushHasOwnStabilisation = false;

        Assert.Equal(SmoothingMode.Laplacian, vm.SmoothingMode);
    }

    [AvaloniaFact]
    public void APresetCarriesItsOwnStabilisation()
    {
        // The reason this is on the brush at all: picking a brush should bring
        // its stabilisation with it rather than leaving the artist to remember
        // a setting.
        var vm = Vm();
        vm.BrushHasOwnStabilisation = true;
        vm.SmoothingMode = SmoothingMode.PulledString;
        vm.LazyRadius = 96;

        var inker = vm.SaveCurrentAsPreset("Inker");
        vm.BrushHasOwnStabilisation = false;
        vm.SmoothingMode = SmoothingMode.Off;

        vm.ApplyPreset(inker);

        Assert.True(vm.BrushHasOwnStabilisation);
        Assert.Equal(SmoothingMode.PulledString, vm.SmoothingMode);
        Assert.Equal(96, vm.LazyRadius);
    }

    [AvaloniaFact]
    public void APresetsStabilisationSurvivesARestart()
    {
        var vm = Vm();
        vm.BrushHasOwnStabilisation = true;
        vm.SmoothingMode = SmoothingMode.SavitzkyGolay;
        vm.SmoothingWindow = 15;
        var saved = vm.SaveCurrentAsPreset("Steady");

        var reopened = Vm();
        var back = reopened.BrushPresetChoices.Single(p => p.Id == saved.Id);

        Assert.Equal(SmoothingMode.SavitzkyGolay, back.Settings.Stabilisation!.Mode);
        Assert.Equal(15, back.Settings.Stabilisation.Window);
    }

    [AvaloniaFact]
    public void ChangingStabilisationCountsAsModifyingTheBrush()
    {
        // It is part of the brush now, so the dot has to say so — otherwise
        // Update would silently drop it.
        var vm = Vm();
        vm.SelectedBrushPreset = vm.BrushPresetChoices.Single(p => p.Id == "builtin-pencil");

        vm.BrushHasOwnStabilisation = true;

        Assert.True(vm.BrushIsModified);
    }

    [AvaloniaFact]
    public void ABrushThatFollowsTheApplicationWritesNoStabilisationKey()
    {
        // Optional means absent, down to the file.
        var doc = DocumentFactory.CreateDoc(64, 64, 12);
        ((Frame)doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Add(new Stroke
        {
            Tool = ToolKind.Brush,
            Points = [new StrokePoint(1, 1, 1)],
            Brush = new BrushSettings(),
        });

        Assert.DoesNotContain("\"stabilisation\"", DocJson.Serialize(doc));
    }
}
