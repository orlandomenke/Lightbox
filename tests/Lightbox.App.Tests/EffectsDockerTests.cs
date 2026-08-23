using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The effects docker's view model: every effect command lives on it (the
/// design's decoupling bar), every edit is an undo step, and the record it
/// writes returns to absent when the last effect leaves.
/// </summary>
[Collection("BrushState")]
public sealed class EffectsDockerTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        return vm;
    }

    private static Layer Paint(MainViewModel vm) => vm.Doc.Scene.Layers[1];

    private static EffectChoice Choice(MainViewModel vm, string kind) =>
        vm.EffectsPanel.Catalogue.First(c => c.Kind == kind);

    private static SKBitmap Published(MainViewModel vm)
    {
        SKBitmap? grabbed = null;
        void Capture(RenderSnapshot s)
        {
            using var img = s.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            grabbed = bmp;
        }
        vm.SnapshotChanged += Capture;
        try
        {
            vm.PublishSnapshot();
        }
        finally
        {
            vm.SnapshotChanged -= Capture;
        }
        return grabbed ?? throw new InvalidOperationException("nothing was published");
    }

    [AvaloniaFact]
    public void AddingAnEffectSeedsDefaultsAndRemovingTheLastReturnsToAbsent()
    {
        var vm = Vm();
        vm.EffectsPanel.AddUseCommand.Execute(Choice(vm, "blur.gaussian"));

        var layer = Paint(vm);
        var use = Assert.Single(layer.Effects!.Uses);
        Assert.Equal("blur.gaussian", use.Kind);
        Assert.Equal(4, use.Params["radius"].Value); // the spec's default
        Assert.Single(vm.EffectsPanel.Uses);
        Assert.Equal("Radius", Assert.Single(vm.EffectsPanel.Params).Label);

        vm.EffectsPanel.RemoveUseCommand.Execute(vm.EffectsPanel.Uses[0]);
        // Absent, not empty: the record returns to writing no key at all.
        Assert.Null(Paint(vm).Effects);
        Assert.Empty(vm.EffectsPanel.Uses);
    }

    [AvaloniaFact]
    public void EveryEditIsAnUndoStep()
    {
        var vm = Vm();
        vm.EffectsPanel.AddUseCommand.Execute(Choice(vm, "grade.levels"));
        vm.EffectsPanel.Params.First(p => p.Label == "Gamma").Value = 2.5;
        Assert.Equal(2.5, Paint(vm).Effects!.Uses[0].Params["gamma"].Value);

        vm.UndoCommand.Execute(null);
        Assert.Equal(1, Paint(vm).Effects!.Uses[0].Params["gamma"].Value);
        vm.UndoCommand.Execute(null);
        Assert.Null(Paint(vm).Effects);
        // The docker re-read the record both times — it shows what is there.
        Assert.Empty(vm.EffectsPanel.Uses);
    }

    [AvaloniaFact]
    public void AnAdjustmentLayerLandsAboveTheActiveOneCarryingItsEffect()
    {
        var vm = Vm();
        var activeBefore = vm.ActiveLayerIndex;
        vm.EffectsPanel.AddAdjustmentLayerCommand.Execute(Choice(vm, "grade.hsl"));

        var layer = vm.Doc.Scene.Layers[activeBefore + 1];
        Assert.True(layer.IsAdjustment);
        Assert.Equal("Hue / Saturation", layer.Name);
        Assert.Equal("grade.hsl", Assert.Single(layer.Effects!.Uses).Kind);
        Assert.Equal(activeBefore + 1, vm.ActiveLayerIndex);
        // Its cels are empty holds: an adjustment layer has no drawings.
        Assert.All(layer.Cels, cel => Assert.Null(cel.Frame));

        vm.UndoCommand.Execute(null);
        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.IsAdjustment);
    }

    [AvaloniaFact]
    public void TheSceneScopeEditsTheSceneStack()
    {
        var vm = Vm();
        vm.EffectsPanel.EditingScene = true;
        vm.EffectsPanel.AddUseCommand.Execute(Choice(vm, "grade.hsl"));
        Assert.NotNull(vm.Doc.Scene.Effects);
        Assert.Null(Paint(vm).Effects);

        vm.EffectsPanel.RemoveUseCommand.Execute(vm.EffectsPanel.Uses[0]);
        Assert.Null(vm.Doc.Scene.Effects);
    }

    [AvaloniaFact]
    public void ABackdropOnlyEffectIsNotOfferedOnAPlainLayersOwnStack()
    {
        // Hue / Saturation runs as a CPU pass on the backdrop path and is
        // identity in a self stack — offering it there would be a control
        // wired to nothing. The adjustment-layer row still carries it, and
        // clipping that layer to the one below is the per-layer use.
        var vm = Vm();
        Assert.DoesNotContain(vm.EffectsPanel.AddChoices, c => c.Kind == "grade.hsl");
        Assert.Contains(vm.EffectsPanel.Catalogue, c => c.Kind == "grade.hsl");

        // A programmatic add is refused the same way the button is absent.
        vm.EffectsPanel.AddUseCommand.Execute(Choice(vm, "grade.hsl"));
        Assert.Null(Paint(vm).Effects);

        // The scene scope and an adjustment layer take the full catalogue.
        vm.EffectsPanel.EditingScene = true;
        Assert.Contains(vm.EffectsPanel.AddChoices, c => c.Kind == "grade.hsl");
        vm.EffectsPanel.EditingScene = false;
        vm.EffectsPanel.AddAdjustmentLayerCommand.Execute(Choice(vm, "grade.levels"));
        Assert.Contains(vm.EffectsPanel.AddChoices, c => c.Kind == "grade.hsl");
    }

    [AvaloniaFact]
    public void AStyleIsOfferedOnlyWhereItHasASilhouette()
    {
        // The mirror of the backdrop-only gate: a glow reads the layer's own
        // silhouette, so the scene grade and adjustment layers never offer
        // it — and a programmatic add is refused the same way.
        var vm = Vm();
        Assert.Contains(vm.EffectsPanel.AddChoices, c => c.Kind == "style.outerGlow");
        Assert.DoesNotContain(vm.EffectsPanel.AdjustmentChoices, c => c.Kind == "style.stroke");

        vm.EffectsPanel.EditingScene = true;
        Assert.DoesNotContain(vm.EffectsPanel.AddChoices, c => c.Kind == "style.outerGlow");
        vm.EffectsPanel.AddUseCommand.Execute(Choice(vm, "style.outerGlow"));
        Assert.Null(vm.Doc.Scene.Effects);
        vm.EffectsPanel.EditingScene = false;
    }

    [AvaloniaFact]
    public void AColourRowWritesTheRecordAsAnUndoStep()
    {
        var vm = Vm();
        vm.EffectsPanel.AddUseCommand.Execute(Choice(vm, "style.outerGlow"));
        var row = Assert.Single(vm.EffectsPanel.ColorRows);
        Assert.Equal("#ffffbe", row.Value); // the spec default shows before authoring
        Assert.Null(Paint(vm).Effects!.Uses[0].Colors); // and is not yet written

        row.Value = "#ff0000";
        Assert.Equal("#ff0000", Paint(vm).Effects!.Uses[0].Colors!["color"]);

        vm.UndoCommand.Execute(null);
        Assert.Null(Paint(vm).Effects!.Uses[0].Colors); // back to absent
    }

    [AvaloniaFact]
    public void AnAdjustmentLayerChangesThePublishedComposite()
    {
        var vm = Vm();
        // A red bar on the paint layer, then a full desaturation above it.
        vm.ColorHex = "#c02020";
        vm.BeginStroke(20, 100, 1);
        vm.MoveStroke(200, 100, 1);
        vm.EndStroke();
        using var before = Published(vm);
        var red = before.GetPixel(100, 100);
        Assert.True(red.Red > red.Green + 40, $"the bar should be red, got {red}");

        vm.EffectsPanel.AddAdjustmentLayerCommand.Execute(Choice(vm, "grade.hsl"));
        vm.EffectsPanel.Params.First(p => p.Label == "Saturation").Value = -100;
        using var after = Published(vm);
        var grey = after.GetPixel(100, 100);
        output.WriteLine($"before {red}, adjusted {grey}");
        Assert.True(Math.Abs(grey.Red - grey.Green) <= 2,
            $"the adjustment should desaturate the bar, got {grey}");
    }
}
