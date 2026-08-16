using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// Adjusting a guide: the selection the Move tool's options are pointed at, the
/// numbers behind each kind, and the one button that also changes the default.
/// </summary>
/// <remarks>
/// <para>
/// The feature these guard is the half that was missing. A guide could always
/// be dragged and never adjusted — a grid's pitch and a height scale's
/// proportions lived two menus away in the configuration window, and a
/// vanishing point's fan lived nowhere at all, hard-coded at twenty-four.
/// </para>
/// <para>
/// The line every test here is drawing is the same one: <b>an edit changes this
/// guide on this document, undoably, and nothing else.</b> "Set as default" is
/// the separate act that also changes what comes next, which is why it is a
/// button and not a side effect.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class GuideOptionsTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null) { SmoothStrokes = false };

    private static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) < 1e-6, $"expected {expected}, got {actual}");

    // ---- what the options are pointed at ------------------------------------------

    [AvaloniaFact]
    public void NothingIsSelectedUntilAGuideIsClicked()
    {
        var vm = Vm();
        vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 40);

        Assert.False(vm.HasSelectedGuide);
        Assert.Null(vm.SelectedGuide);
        Assert.Equal("No guide selected", vm.SelectedGuideLabel);
    }

    [AvaloniaFact]
    public void SelectingAGuideSaysWhichNumbersItHas()
    {
        var vm = Vm();
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 40);
        var vp = vm.AddGuide(GuideKind.VanishingPoint, 10, 20);
        var scale = vm.AddGuide(GuideKind.HeightScale, 300, 900, spacing: 90, divisions: 6);

        vm.SelectGuide(grid.Id);
        Assert.True(vm.SelectedGuideIsGrid);
        Assert.True(vm.SelectedGuideHasAngle);
        Assert.False(vm.SelectedGuideIsVanishingPoint);
        Assert.Equal(40, vm.GuideSpacing);

        vm.SelectGuide(vp.Id);
        Assert.True(vm.SelectedGuideIsVanishingPoint);
        // A vanishing point has no angle of its own — its directions depend on
        // where you are standing — so the dial is absent rather than inert.
        Assert.False(vm.SelectedGuideHasAngle);
        Assert.Equal(Guide.DefaultRays, vm.GuideRays);

        vm.SelectGuide(scale.Id);
        Assert.True(vm.SelectedGuideIsHeightScale);
        Assert.Equal(6, vm.GuideHeads);
        Assert.Equal(90, vm.GuideSpacing);
    }

    [AvaloniaFact]
    public void ASelectionOutlivesAnUndoAndDiesWithTheGuide()
    {
        // By id, not by object or position: undo replaces the whole document,
        // so the object under the selection is gone after one.
        var vm = Vm();
        var guide = vm.AddGuide(GuideKind.Line, 100, 100);
        vm.SelectGuide(guide.Id);

        vm.SetGuidePosition(vm.SelectedGuide!, 140, 100);
        vm.UndoCommand.Execute(null);

        Assert.True(vm.HasSelectedGuide);
        Near(100, vm.GuideX);

        vm.RemoveSelectedGuideCommand.Execute(null);
        Assert.False(vm.HasSelectedGuide);
    }

    // ---- position ------------------------------------------------------------------

    [AvaloniaFact]
    public void TypingAPositionMovesTheGuideAndUndoPutsItBack()
    {
        var vm = Vm();
        var guide = vm.AddGuide(GuideKind.VanishingPoint, 10, 20);
        vm.SelectGuide(guide.Id);

        vm.GuideX = 512;
        vm.GuideY = 288;

        Near(512, vm.SelectedGuide!.X);
        Near(288, vm.SelectedGuide.Y);

        vm.UndoCommand.Execute(null);
        Near(20, vm.SelectedGuide!.Y);
        vm.UndoCommand.Execute(null);
        Near(10, vm.SelectedGuide!.X);
    }

    [AvaloniaFact]
    public void ALockedGuideCannotBeMovedByTypingEither()
    {
        // The lock means "pin it where it is", and a typed field must not be
        // the way round it any more than a drag is.
        var vm = Vm();
        var guide = vm.AddGuide(GuideKind.Line, 100, 100);
        vm.SelectGuide(guide.Id);
        vm.GuideLocked = true;

        vm.GuideX = 400;

        Near(100, vm.SelectedGuide!.X);
    }

    // ---- the numbers behind each kind ------------------------------------------------

    [AvaloniaFact]
    public void AGridsCellSizeIsTheDocumentsAndNotThePreference()
    {
        var vm = Vm();
        var before = vm.GridSpacing;
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: before);
        vm.SelectGuide(grid.Id);

        vm.GuideSpacing = before + 24;

        Near(before + 24, vm.SelectedGuide!.Spacing);
        // Untouched: changing one lattice must not decide what the next one is.
        Near(before, vm.GridSpacing);
    }

    [AvaloniaFact]
    public void AHeightScaleKeepsItsHeadCountWhenTheHeadHeightChanges()
    {
        var vm = Vm();
        var scale = vm.AddGuide(GuideKind.HeightScale, 300, 900, spacing: 90, divisions: 6);
        vm.SelectGuide(scale.Id);

        vm.GuideSpacing = 110;

        Near(110, vm.SelectedGuide!.Spacing);
        Assert.Equal(6, vm.SelectedGuide.Divisions);

        vm.GuideHeads = 8;
        Assert.Equal(8, vm.SelectedGuide!.Divisions);
        Near(110, vm.SelectedGuide.Spacing);
    }

    [AvaloniaFact]
    public void AVanishingPointsRaysAreEditableAndUndoable()
    {
        var vm = Vm();
        var vp = vm.AddGuide(GuideKind.VanishingPoint, 100, 300);
        vm.SelectGuide(vp.Id);

        vm.GuideRays = 8;
        Assert.Equal(8, vm.SelectedGuide!.RayCount);

        vm.UndoCommand.Execute(null);
        Assert.Equal(Guide.DefaultRays, vm.SelectedGuide!.RayCount);
    }

    // ---- the default, which is the other half of the ask ------------------------------

    [AvaloniaFact]
    public void OnlyTheKindsWithNumbersOfferADefault()
    {
        var vm = Vm();
        var line = vm.AddGuide(GuideKind.Line, 100, 100);
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 40);

        vm.SelectGuide(line.Id);
        Assert.False(vm.CanSaveGuideDefaults);

        vm.SelectGuide(grid.Id);
        Assert.True(vm.CanSaveGuideDefaults);
    }

    [AvaloniaFact]
    public void SettingAGridAsDefaultChangesWhatTheNextGridIsMadeFrom()
    {
        var vm = Vm();
        var grid = vm.AddGuide(GuideKind.Grid, 0, 0, spacing: 40);
        vm.SelectGuide(grid.Id);
        vm.GuideSpacing = 64;

        vm.SaveGuideDefaultsCommand.Execute(null);

        Near(64, vm.GridSpacing);
    }

    [AvaloniaFact]
    public void AHeightScaleSavesAProportionRatherThanAPixelHeight()
    {
        // So the default still lands as a figure on a canvas of another size.
        // Half the canvas over four heads, saved, is "four heads standing in
        // half the height" — not "an eighth of 1080 pixels".
        var vm = Vm();
        var height = vm.Doc.Scene.Height;
        var scale = vm.AddGuide(
            GuideKind.HeightScale, 100, height, spacing: height * 0.5 / 4, divisions: 4);
        vm.SelectGuide(scale.Id);

        vm.SaveGuideDefaultsCommand.Execute(null);

        Assert.Equal(4, vm.HeightScaleHeads);
        Near(0.5, vm.HeightScaleFill);
    }

    [AvaloniaFact]
    public void ANewVanishingPointTakesTheSavedFan()
    {
        var vm = Vm();
        var first = vm.AddGuide(GuideKind.VanishingPoint, 100, 300);
        vm.SelectGuide(first.Id);
        vm.GuideRays = 6;
        vm.SaveGuideDefaultsCommand.Execute(null);

        Assert.Equal(6, vm.VanishingPointRays);

        // And the one already on the canvas is untouched by a later change to
        // the preference — the rule the grid's pitch has always followed.
        vm.VanishingPointRays = 30;
        Assert.Equal(6, first.RayCount);
    }
}
