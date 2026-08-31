using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using Lightbox.Raster;

namespace Lightbox.App.Tests;

/// <summary>
/// Q171's last capture: several layers become one symbol.
/// </summary>
/// <remarks>
/// The gesture the whole question was asked for — a head is a lines layer, a
/// colour layer and two effect layers, and it should be one thing you can
/// place. The rule that took a decision is what is left behind: a layer the
/// capture empties goes, a layer still holding other drawings stays.
/// </remarks>
public class SymbolFromLayersTests : IDisposable
{
    private readonly string _root = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"lightbox-caplayers-{Guid.NewGuid():N}.lbproj");

    public void Dispose()
    {
        SymbolRegistry.Clear();
        if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
    }

    private static Stroke Bar(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(20, y, 1), new StrokePoint(120, y, 1)],
        Brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static Layer Sheet(string name, params Frame?[] cels)
    {
        var layer = new Layer { Name = name, Kind = LayerKind.Painted };
        foreach (var frame in cels) layer.Cels.Add(new Cel { Frame = frame });
        return layer;
    }

    /// <summary>A project document with these layers, bottom of the stack first.</summary>
    private MainViewModel With(params Layer[] layers)
    {
        var vm = VmLayers.PaperVm();
        vm.ProjectDocker.Project = ProjectIo.Create("Knight", _root);
        vm.RefreshProjectResources();
        vm.Doc.Scene.Layers.Clear();
        foreach (var layer in layers) vm.Doc.Scene.Layers.Add(layer);
        vm.Doc.Scene.FrameCount = layers.Max(l => l.Cels.Count);
        return vm;
    }

    // ---- the capture -------------------------------------------------------------

    [AvaloniaFact]
    public void FourLayersBecomeOneSymbolInStackOrder()
    {
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet("Lines", new Frame { Strokes = [Bar(80)] });
        var vm = With(colour, lines);

        var symbol = vm.MakeSymbolFromLayers("Head", [colour, lines], LayerCaptureDepth.ThisDrawing);

        Assert.NotNull(symbol);
        Assert.Equal(2, symbol!.Layers.Count);
        // Bottom first, because order is the whole meaning of a stack.
        Assert.Equal("Colour", symbol.Layers[0].Name);
        Assert.Equal("Lines", symbol.Layers[1].Name);
    }

    [AvaloniaFact]
    public void ThePlacementLandsWhereTheStackWas()
    {
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet("Lines", new Frame { Strokes = [Bar(80)] });
        var vm = With(colour, lines);

        vm.MakeSymbolFromLayers("Head", [colour, lines], LayerCaptureDepth.ThisDrawing);

        var host = vm.Doc.Scene.Layers.Single(l => l.Id == colour.Id);
        Assert.NotNull(host.Cels[0].Frame!.Placements);
        Assert.Single(host.Cels[0].Frame!.Placements!);
    }

    // ---- what is left behind (the owner's rule) ----------------------------------

    /// <summary>
    /// A layer the capture empties goes; the lowest stays to hold the placement.
    /// </summary>
    /// <remarks>
    /// The owner's rule, and better than either option offered: leaving four
    /// empty layers behind is clutter an artist would delete by hand, and
    /// removing a layer that still holds work would be destructive.
    /// </remarks>
    [AvaloniaFact]
    public void LayersTheCaptureEmptiesGoWithIt()
    {
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet("Lines", new Frame { Strokes = [Bar(80)] });
        var vm = With(colour, lines);

        vm.MakeSymbolFromLayers("Head", [colour, lines], LayerCaptureDepth.ThisDrawing);

        // The upper one had nothing left to say and went; the lower one stays
        // because the placement has to live somewhere.
        Assert.DoesNotContain(vm.Doc.Scene.Layers, l => l.Id == lines.Id);
        Assert.Contains(vm.Doc.Scene.Layers, l => l.Id == colour.Id);
    }

    [AvaloniaFact]
    public void ALayerStillHoldingOtherDrawingsStays()
    {
        // The other half of the rule: this layer draws on frame two as well, so
        // it keeps that and loses only what was taken.
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet(
            "Lines",
            new Frame { Strokes = [Bar(80)] },
            new Frame { Strokes = [Bar(100)] });
        var vm = With(colour, lines);

        vm.MakeSymbolFromLayers("Head", [colour, lines], LayerCaptureDepth.ThisDrawing);

        var kept = vm.Doc.Scene.Layers.SingleOrDefault(l => l.Id == lines.Id);
        Assert.NotNull(kept);
        // Its first drawing went into the symbol; its second is untouched.
        Assert.Null(kept!.Cels[0].Frame);
        Assert.NotNull(kept.Cels[1].Frame);
    }

    // ---- how much of each layer --------------------------------------------------

    [AvaloniaFact]
    public void TakingTheWholeLayersMakesAnAnimatedSymbol()
    {
        var lines = Sheet(
            "Lines",
            new Frame { Strokes = [Bar(40)] },
            new Frame { Strokes = [Bar(60)] });
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(100)] });
        var vm = With(colour, lines);

        var symbol = vm.MakeSymbolFromLayers("Walk", [colour, lines], LayerCaptureDepth.WholeLayers)!;

        Assert.Equal(2, symbol.Layers.Count);
        // The longest layer is the length of the animation.
        Assert.Equal(2, symbol.FrameCount);
    }

    [AvaloniaFact]
    public void TakingOnlyTheDrawingOnShowMakesAStillOne()
    {
        var lines = Sheet(
            "Lines",
            new Frame { Strokes = [Bar(40)] },
            new Frame { Strokes = [Bar(60)] });
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(100)] });
        var vm = With(colour, lines);

        var symbol = vm.MakeSymbolFromLayers("Head", [colour, lines], LayerCaptureDepth.ThisDrawing)!;

        Assert.Equal(1, symbol.FrameCount);
    }

    /// <summary>The question is only asked when the answers would differ.</summary>
    /// <remarks>
    /// A dialog in front of a gesture an artist uses often is a tax, and a head
    /// drawn once has the same answer whichever depth is chosen.
    /// </remarks>
    [AvaloniaFact]
    public void OneDrawingEachRaisesNoQuestion()
    {
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet("Lines", new Frame { Strokes = [Bar(80)] });
        var vm = With(colour, lines);

        Assert.False(vm.LayersHoldMoreThanTheDrawingOnShow([colour, lines]));
    }

    [AvaloniaFact]
    public void DrawingsOnOtherFramesRaiseTheQuestion()
    {
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet(
            "Lines",
            new Frame { Strokes = [Bar(80)] },
            new Frame { Strokes = [Bar(100)] });
        var vm = With(colour, lines);

        Assert.True(vm.LayersHoldMoreThanTheDrawingOnShow([colour, lines]));
    }

    [AvaloniaFact]
    public void AHoldIsNotAnotherDrawing()
    {
        // A hold is the same drawing continuing, so it raises no question — the
        // check is about drawings, not about how long they are exposed.
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet("Lines", new Frame { Strokes = [Bar(80)] }, null);
        var vm = With(colour, lines);

        Assert.False(vm.LayersHoldMoreThanTheDrawingOnShow([colour, lines]));
    }

    [AvaloniaFact]
    public void CapturingIsOneUndoStep()
    {
        var colour = Sheet("Colour", new Frame { Strokes = [Bar(40)] });
        var lines = Sheet("Lines", new Frame { Strokes = [Bar(80)] });
        var vm = With(colour, lines);
        vm.MakeSymbolFromLayers("Head", [colour, lines], LayerCaptureDepth.ThisDrawing);

        vm.UndoCommand.Execute(null);

        Assert.Equal(2, vm.Doc.Scene.Layers.Count);
        Assert.Contains(vm.Doc.Scene.Layers, l => l.Id == lines.Id);
    }
}
