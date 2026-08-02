using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// What makes an all-layers-live smudge live: it is re-frozen whenever the
/// layers under it change.
/// </summary>
/// <remarks>
/// <para>
/// Q6 answered (c), built as (2). The alternative was handing a backdrop to
/// every render path at render time — canvas, PNG render, sequence export,
/// sprite-sheet export — which is four call sites that have to agree forever,
/// and whose failure mode is an export that differs from the canvas. Here the
/// stroke is given what it read once per edit, so every render path reproduces
/// it without knowing the layer stack exists.
/// </para>
/// <para>
/// The behaviour an artist sees is the same either way: repaint the background
/// and the smudge above it follows.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class LiveSampleRebakeTests : BrushStateIsolated
{
    private static Stroke Block(string colour, double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = colour,
        Points = [new StrokePoint(30, y, 1), new StrokePoint(160, y, 1)],
        Brush = new BrushSettings { Size = 30, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    private static Stroke LiveSmudge() => new()
    {
        Tool = ToolKind.Brush,
        Color = "#000000",
        Points = [new StrokePoint(40, 60, 1), new StrokePoint(150, 60, 1)],
        Brush = new BrushSettings
        {
            Kind = BrushKind.Smudge, Size = 22, Flow = 1, Spacing = 0.15,
            SmudgeLength = 0.95, ColorRate = 0, SampleSource = SampleSource.AllLayersLive,
        },
    };

    /// <summary>Two layers: something to smudge underneath, the smudge on top.</summary>
    private static MainViewModel TwoLayers(out PaintedFrame under, out PaintedFrame over)
    {
        var vm = new MainViewModel(null);
        var scene = vm.Doc.Scene;
        under = (PaintedFrame)scene.Layers[^1].Cels[0].Frame!;
        under.Strokes.Add(Block("#20c040", 60));

        var top = new Layer { Name = "Top", Kind = LayerKind.Painted };
        var frame = new PaintedFrame();
        top.Cels.Add(new Cel { Frame = frame });
        for (var i = 1; i < scene.Layers[^1].Cels.Count; i++) top.Cels.Add(new Cel());
        scene.Layers.Add(top);
        over = frame;
        vm.ActiveLayerIndex = scene.Layers.Count - 1;
        return vm;
    }

    /// <summary>
    /// Fire the edit funnel with a real edit on the bottom layer.
    /// </summary>
    /// <remarks>
    /// The Background layer a new document opens with is locked, so an append
    /// to it is refused and the funnel never runs — which looks exactly like
    /// the feature not working.
    /// </remarks>
    private static void EditUnderneath(MainViewModel vm, Stroke stroke)
    {
        vm.Doc.Scene.Layers[0].Locked = false;
        Assert.Equal(1, vm.AppendExternalStrokes(vm.Doc.Scene.Layers[0].Id, 0, [stroke]));
    }

    private static byte[] Composited(MainViewModel vm, PaintedFrame over, PaintedFrame under)
    {
        var scene = vm.Doc.Scene;
        using var beneath = FrameRasterizer.Materialize(under, scene.Width, scene.Height);
        using var bmp = FrameRasterizer.Materialize(over, scene.Width, scene.Height);
        return bmp.GetPixelSpan().ToArray();
    }

    [AvaloniaFact]
    public void ALiveSmudgeFollowsAnEditToTheLayerUnderIt()
    {
        // The promise. The smudge is not re-recorded and no stroke of it
        // changes — what changes is the backdrop it carries.
        var vm = TwoLayers(out var under, out var over);
        var smudge = LiveSmudge();
        over.Strokes.Add(smudge);
        EditUnderneath(vm, Block("#20c040", 62));      // fires the funnel, first bake
        var first = smudge.Baked?.PngBase64;
        Assert.NotNull(first);

        EditUnderneath(vm, Block("#c02040", 60));      // repaint underneath, in red

        Assert.NotEqual(first, smudge.Baked!.PngBase64);
    }

    [AvaloniaFact]
    public void ThatChangesWhatTheLayerRendersAs()
    {
        // The bake is not the point on its own — the pixels are.
        var vm = TwoLayers(out var under, out var over);
        over.Strokes.Add(LiveSmudge());
        EditUnderneath(vm, Block("#20c040", 62));
        var before = Composited(vm, over, under);

        EditUnderneath(vm, Block("#c02040", 60));

        Assert.NotEqual(before, Composited(vm, over, under));
    }

    [AvaloniaFact]
    public void ABakedSmudgeDoesNotFollow()
    {
        // The other half of the choice, and the reason both exist.
        var vm = TwoLayers(out _, out var over);
        var smudge = LiveSmudge();
        smudge.Brush.SampleSource = SampleSource.AllLayersBaked;
        smudge.Baked = new BakedSample { PngBase64 = "", X = 3, Y = 4 };
        over.Strokes.Add(smudge);

        EditUnderneath(vm, Block("#c02040", 60));

        Assert.Equal(3, smudge.Baked.X);
        Assert.Equal(4, smudge.Baked.Y);
    }

    [AvaloniaFact]
    public void AnOrdinaryDocumentIsNotTouched()
    {
        // Every edit in the application goes through this funnel, and almost
        // none of them sample anything. Asserted as "no stroke gained a sample"
        // rather than by timing, because a timing assertion here would be
        // measuring the machine. What keeps the cost down is structural — the
        // compose is behind the per-frame check — and is stated at the call
        // site rather than pinned here.
        var vm = TwoLayers(out var under, out var over);
        over.Strokes.Add(Block("#3070b0", 60));

        EditUnderneath(vm, Block("#c02040", 60));

        Assert.All(over.Strokes, s => Assert.Null(s.Baked));
        Assert.All(under.Strokes, s => Assert.Null(s.Baked));
    }

    [AvaloniaFact]
    public void WithNothingUnderneathTheSampleIsDroppedRatherThanKeptStale()
    {
        // A smudge on the bottom layer has no backdrop to follow, so it goes
        // back to reading its own layer. Keeping the last one would be a mark
        // blended with something no longer below it.
        var vm = new MainViewModel(null);
        // One layer only, so there is genuinely nothing underneath.
        var scene = vm.Doc.Scene;
        while (scene.Layers.Count > 1) scene.Layers.RemoveAt(scene.Layers.Count - 1);
        scene.Layers[0].Locked = false;
        vm.ActiveLayerIndex = 0;
        var only = (PaintedFrame)scene.Layers[0].Cels[0].Frame!;
        var smudge = LiveSmudge();
        smudge.Baked = new BakedSample { PngBase64 = "stale", X = 1, Y = 1 };
        only.Strokes.Add(smudge);

        vm.AppendExternalStrokes(scene.Layers[0].Id, 0, [Block("#c02040", 60)]);

        Assert.Null(smudge.Baked);
    }

    [AvaloniaFact]
    public void AHandDrawnBakedSmudgeFreezesWhatWasUnderIt()
    {
        // Regression. FreezeSampledBackdrop was wired into EndGradient and
        // EndShape and missed out of EndStroke — the path a pen actually
        // takes — so an all-layers-BAKED smudge drawn by hand froze nothing
        // and quietly fell back to reading its own layer.
        //
        // It went unnoticed because Live covered for it: the re-bake runs off
        // the edit funnel, which EndStroke does trigger, so the half that had
        // an end-to-end test worked and the half that only had engine tests
        // did not. Charter O7, arriving from the other direction.
        var vm = TwoLayers(out _, out _);
        vm.SmudgeSampleSource = SampleSource.AllLayersBaked;
        // The route an artist takes: pick the smudge preset.
        vm.SelectedBrushPreset = vm.BrushPresetChoices.First(p => p.Settings.Kind == BrushKind.Smudge);
        Assert.True(vm.IsSmudgeBrush);

        vm.BeginStroke(40, 60, 1);
        vm.MoveStroke(150, 60, 1);
        vm.EndStroke();

        var painted = (PaintedFrame)vm.Doc.Scene.Layers[^1].Cels[0].Frame!;
        var smudge = Assert.Single(painted.Strokes);
        Assert.Equal(SampleSource.AllLayersBaked, smudge.Brush.SampleSource);
        Assert.NotNull(smudge.Baked);
        Assert.NotEmpty(smudge.Baked!.PngBase64);
    }

    [AvaloniaFact]
    public void RebakingIsNotAnUndoStep()
    {
        // The bake is derived from the layers below, not authored, and a
        // history with "the background moved" between every real edit would be
        // unusable. Undo re-enters the same funnel, so the sample follows the
        // document back anyway.
        var vm = TwoLayers(out _, out var over);
        over.Strokes.Add(LiveSmudge());
        // Re-read through the document each time: an append is a snapshot undo
        // step, so undoing swaps the whole graph and a held reference goes
        // stale.
        int Bottom() => ((PaintedFrame)vm.Doc.Scene.Layers[0].Cels[0].Frame!).Strokes.Count;
        var before = Bottom();
        EditUnderneath(vm, Block("#20c040", 62));
        Assert.Equal(before + 1, Bottom());

        vm.UndoCommand.Execute(null);

        // One undo takes the stroke back. If the re-bake had been pushed as a
        // step of its own, this one would have spent itself on that instead and
        // the stroke would still be here.
        Assert.Equal(before, Bottom());
    }
}
