using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The checker end to end (Q135): the misses land in the line selection —
/// highlighted with chrome the artist already reads, actionable with the
/// tools that already act on selections — the status line carries the
/// verdict with its numbers, an inferred judgement carries its hedge, and
/// the command is registered where the Configure window can see it.
/// </summary>
[Collection("BrushState")]
public sealed class PerspectiveCheckTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static MainViewModel Sketch(params (double Ax, double Ay, double Bx, double By)[] lines)
    {
        var vm = new MainViewModel(artist: null) { SmoothStrokes = false, ColorHex = "#000000", BrushSize = 6 };
        vm.NewDocument(new NewDocumentSettings("Persp", 400, 300, 12, 72, "#ffffff", false));
        foreach (var (ax, ay, bx, by) in lines)
        {
            vm.BeginStroke(ax, ay, 1);
            vm.MoveStroke(bx, by, 1);
            vm.EndStroke();
        }
        return vm;
    }

    [AvaloniaFact]
    public void TheMissesAreSelectedAndTheStatusSaysHowFarOff()
    {
        var vm = Sketch((0, 100, 100, 100), (0, 140, 100, 133), (50, 0, 50, 100));
        vm.Doc.Scene.Guides = [new Guide { Kind = GuideKind.VanishingPoint, X = 300, Y = 100, Name = "east" }];

        vm.CheckPerspective();
        output.WriteLine(vm.AiStatus);

        var layer = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        var frame = layer.Cels[0].Frame!;
        Assert.True(vm.Selection.IsStrokeSelected(frame.Strokes[1].Id), "the miss is not selected");
        Assert.False(vm.Selection.IsStrokeSelected(frame.Strokes[0].Id), "a converging line is selected");
        Assert.Contains("selected", vm.AiStatus);
        Assert.Contains("east", vm.AiStatus);
        // Judged against the artist's own point: no inference hedge.
        Assert.DoesNotContain("read off the ink", vm.AiStatus);
    }

    [AvaloniaFact]
    public void AnInferredJudgementCarriesItsHedge()
    {
        var vm = Sketch((0, 0, 100, 50), (0, 200, 100, 150), (0, 100, 100, 100), (0, 60, 100, 73));

        vm.CheckPerspective();
        output.WriteLine(vm.AiStatus);

        Assert.Contains("read off the ink", vm.AiStatus);
    }

    [AvaloniaFact]
    public void ACleanSketchReportsItsNumbersAndSelectsNothing()
    {
        var vm = Sketch((0, 100, 100, 100), (0, 25, 100, 50));
        vm.Doc.Scene.Guides = [new Guide { Kind = GuideKind.VanishingPoint, X = 300, Y = 100 }];

        vm.CheckPerspective();
        output.WriteLine(vm.AiStatus);

        Assert.Contains("nothing off", vm.AiStatus);
        var frame = vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[0].Frame!;
        Assert.All(frame.Strokes, s => Assert.False(vm.Selection.IsStrokeSelected(s.Id)));
    }

    [AvaloniaFact]
    public void NothingStraightIsSaidRatherThanSilent()
    {
        var vm = Sketch();

        vm.CheckPerspective();
        output.WriteLine(vm.AiStatus);

        Assert.Contains("straight line", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TheCommandIsRegisteredSoItCanBeFoundAndRebound()
    {
        var map = new Services.ShortcutMap { StorePathOverride = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"persp-shortcuts-{Guid.NewGuid():N}.json") };
        Assert.Contains(map.Definitions, d => d.Id == "canvas.checkPerspective");
    }
}
