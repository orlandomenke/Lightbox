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
