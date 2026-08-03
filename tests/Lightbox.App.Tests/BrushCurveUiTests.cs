using Avalonia.Headless.XUnit;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;
using Lightbox.Raster.Tips;

namespace Lightbox.App.Tests;

/// <summary>
/// The tool bar's side of curves, blend modes and the tip picker. Driven
/// through the view model and the control's own edit path — synthetic pointer
/// input through Xvfb is unreliable here, and a dropped click looks exactly
/// like a bug.
/// </summary>
[Collection("BrushState")]
public class BrushCurveUiTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null);

    // ---- curves -------------------------------------------------------------

    [AvaloniaFact]
    public void ABrushWithNoCurveStillShowsTheShapeItsGammaMeans()
    {
        // The editor must never open on a straight line for a brush that is
        // not linear, or the first drag silently flattens a response the
        // artist had already tuned.
        var vm = Vm();
        vm.BrushPressureSizeGamma = 2;

        var shape = vm.BrushCurve(BrushDynamic.Size);

        Assert.True(Math.Abs(shape.Evaluate(0.5) - 0.25) < 0.03,
            $"the editor would have shown the wrong curve: {shape.Evaluate(0.5):F3}");
        Assert.True(vm.BrushDrives(BrushDynamic.Size));
    }

    [AvaloniaFact]
    public void DrawingACurveReplacesTheGammaThatWasDrivingIt()
    {
        var vm = Vm();
        vm.BrushPressureSizeGamma = 3;

        vm.SetBrushCurve(BrushDynamic.Size, new ResponseCurve { Points = [new(0, 0.5), new(1, 0.5)] });

        Assert.Equal(0.5, PressureResponse.Factor(vm.CurrentToolSettingsForTest, BrushDynamic.Size, 0.2), 1e-9);
    }

    [AvaloniaFact]
    public void TurningADynamicOffClearsBothWaysItCouldHaveBeenDriven()
    {
        // A curve left behind under an unchecked box would make the checkbox
        // lie about what the brush does.
        var vm = Vm();
        vm.BrushPressureFlowGamma = 1.5;
        vm.SetBrushCurve(BrushDynamic.Flow, ResponseCurve.Linear());

        vm.SetBrushDrives(BrushDynamic.Flow, false);

        Assert.False(vm.BrushDrives(BrushDynamic.Flow));
        Assert.Equal(1, PressureResponse.Factor(vm.CurrentToolSettingsForTest, BrushDynamic.Flow, 0.2), 1e-9);
        Assert.Equal(0, vm.BrushPressureFlowGamma);
    }

    [AvaloniaFact]
    public void ADynamicWithNoGammaOfItsOwnIsTurnedOnAsALine()
    {
        // Scatter, roundness and the two smudge controls never had an exponent,
        // so "on" has to mean something. A straight line is the only honest
        // starting point: it says pressure drives this, and nothing more.
        var vm = Vm();

        vm.SetBrushDrives(BrushDynamic.Scatter, true);

        Assert.True(vm.BrushDrives(BrushDynamic.Scatter));
        Assert.Equal(0.4, PressureResponse.Factor(vm.CurrentToolSettingsForTest, BrushDynamic.Scatter, 0.4), 1e-9);
    }

    [AvaloniaFact]
    public void TurningOnSomethingAlreadyDrivenLeavesItAlone()
    {
        // Otherwise re-checking a box an artist never unchecked — which the
        // page does when it rebuilds — would throw away their curve.
        var vm = Vm();
        var drawn = new ResponseCurve { Points = [new(0, 0), new(0.5, 0.9), new(1, 1)] };
        vm.SetBrushCurve(BrushDynamic.Size, drawn);

        vm.SetBrushDrives(BrushDynamic.Size, true);

        Assert.Equal(
            drawn.Evaluate(0.5),
            PressureResponse.Factor(vm.CurrentToolSettingsForTest, BrushDynamic.Size, 0.5), 1e-9);
    }

    [AvaloniaFact]
    public void ResetPutsADynamicBackToAStraightLine()
    {
        var vm = Vm();
        vm.SetBrushCurve(BrushDynamic.Hardness, new ResponseCurve { Points = [new(0, 1), new(1, 0)] });

        vm.ResetBrushCurve(BrushDynamic.Hardness);

        Assert.True(vm.BrushDrives(BrushDynamic.Hardness));
        Assert.Equal(0.3, PressureResponse.Factor(vm.CurrentToolSettingsForTest, BrushDynamic.Hardness, 0.3), 1e-9);
    }

    [AvaloniaFact]
    public void TheBrushAndTheEraserKeepSeparateCurves()
    {
        // They keep separate everything else, and a curve is a setting that
        // reaches pixels — so sharing one would be the tool bar changing a mark
        // the artist did not make.
        var vm = Vm();
        vm.ActiveTool = ToolId.Brush;
        vm.SetBrushCurve(BrushDynamic.Size, new ResponseCurve { Points = [new(0, 1), new(1, 1)] });

        vm.ActiveTool = ToolId.Eraser;

        Assert.NotEqual(1, PressureResponse.Factor(vm.CurrentToolSettingsForTest, BrushDynamic.Size, 0.2), 3);
    }

    [AvaloniaFact]
    public void TheEditorHandsBackAWholeCurveRatherThanMutatingOne()
    {
        // The control draws and reports; it does not decide. An editor that
        // mutated the record in place would land edits behind the view model's
        // back, and undo would never see them.
        var original = new ResponseCurve { Points = [new(0, 0), new(1, 1)] };
        var editor = new CurveEditor { Curve = original };
        ResponseCurve? reported = null;
        editor.CurveChanged += c => reported = c;

        editor.DragHandleForTest(1, 1, 0.25);

        Assert.NotNull(reported);
        Assert.Equal(0.25, reported!.Evaluate(1), 1e-9);
        Assert.Equal(1, original.Evaluate(1), 1e-9);
    }

    [AvaloniaFact]
    public void AnEditorWithNoCurveShowsTheIdentityRatherThanNothing()
    {
        var editor = new CurveEditor();

        editor.DragHandleForTest(0, 0, 0.4);

        Assert.Equal(0.4, editor.Curve!.Evaluate(0), 1e-9);
        Assert.Equal(2, editor.Curve.Points.Count);
    }

    // ---- blend mode ---------------------------------------------------------

    [AvaloniaFact]
    public void ChoosingNormalStoresNothingAtAll()
    {
        // Optional means absent: picking the default back must clear the key,
        // not write it, or every document that ever opened the dropdown grows
        // a line per stroke.
        var vm = Vm();
        vm.BrushBlend = LayerBlendMode.Multiply;
        Assert.Equal(LayerBlendMode.Multiply, vm.CurrentToolSettingsForTest.Blend);

        vm.BrushBlend = LayerBlendMode.Normal;

        Assert.Null(vm.CurrentToolSettingsForTest.Blend);
        Assert.Equal(LayerBlendMode.Normal, vm.BrushBlend);
    }

    [AvaloniaFact]
    public void TheBrushPickerOffersTheSameModesTheLayerDockerDoes()
    {
        // A brush's Multiply and a layer's Multiply are the same operation, so
        // offering different sets would imply they were not.
        var vm = Vm();

        Assert.Equal(Enum.GetValues<LayerBlendMode>(), vm.BlendModeChoices);
    }

    // ---- the tip picker -----------------------------------------------------

    [AvaloniaFact]
    public void ThePickerOffersRoundFirstAndThenEveryTipThatExists()
    {
        var vm = Vm();

        var tips = vm.AvailableTips();

        Assert.Equal(TipCatalogue.All.Count, tips.Count(t => TipCatalogue.IsBuiltIn(t.Id)));
        Assert.Equal("Round", TipChoice.Round().Name);
        Assert.Null(TipChoice.Round().Tip);
    }

    [AvaloniaFact]
    public void ChoosingATipCopiesItsPixelsIntoTheDrawing()
    {
        // Invariant 1 for tips. From here the drawing renders whether or not
        // the library still has it, which is what makes deleting a library tip
        // safe.
        var vm = Vm();
        var tip = TipCatalogue.All.Single(t => t.Name == "Spatter");

        vm.SetBrushTip(tip);

        Assert.Equal(tip.Id, vm.BrushTipId);
        Assert.True(vm.Doc.BrushTips.ContainsKey(tip.Id));
        Assert.Equal(tip.Png, vm.Doc.BrushTips[tip.Id]);
    }

    [AvaloniaFact]
    public void ChoosingRoundGoesBackToTheEnginesOwnDab()
    {
        var vm = Vm();
        vm.SetBrushTip(TipCatalogue.All[0]);

        vm.SetBrushTip(null);

        Assert.Null(vm.BrushTipId);
    }

    [AvaloniaFact]
    public void ADroppedTipLeavesTheDrawingAlone()
    {
        // Going back to Round must not remove the raster: strokes already
        // painted with that tip still reference it.
        var vm = Vm();
        var tip = TipCatalogue.All[3];
        vm.SetBrushTip(tip);

        vm.SetBrushTip(null);

        Assert.True(vm.Doc.BrushTips.ContainsKey(tip.Id), "the tip a finished stroke needs was thrown away");
    }

    [AvaloniaFact]
    public void EveryBuiltInTipHasAThumbnailToShowInThePicker()
    {
        // The picker is a grid of pictures; a tile with no picture is a tile
        // nobody can choose from.
        foreach (var tip in TipCatalogue.All)
        {
            Assert.NotNull(TipChoice.For(tip).Thumbnail);
        }
        Assert.NotNull(TipChoice.Round().Thumbnail);
    }
}
