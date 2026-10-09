using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// B432: a selection takes what its region holds on every layer the transform
/// reaches — judged by the ink each mark puts down, not by the path it was
/// drawn along.
/// </summary>
/// <remarks>
/// <para>
/// The owner's report: <i>"When I have an active selection on screen and I
/// switch to another layer, Ctrl+T for transform or move does not work. So it
/// only samples the layer it became active on."</i> Measured, the selection was
/// never tied to a layer — a marquee made on one layer moves another's content
/// after a switch. What failed was the question asked of each mark: does its
/// <em>path</em> pass through the region. A fill's path is its outline, so a
/// wand or alpha selection that lies inside the colour contains none of it; a
/// line's path is its centre, so a selection hugging the colour beside it
/// misses the half of the line that overlaps. A selection made from one layer's
/// pixels follows that layer's ink, and every other layer's paths fall outside
/// it — which reads, at the canvas, exactly as "it only samples one layer".
/// </para>
/// <para>
/// The fixture is the owner's example: a foot drawn on three layers — line,
/// colour and shading.
/// </para>
/// </remarks>
[Collection("BrushState")]
public sealed class SelectionFindsInkTests(ITestOutputHelper output) : BrushStateIsolated
{
    private sealed record Foot(MainViewModel Vm, int Line, int Colour, int Shade);

    private static void Draw(MainViewModel vm, params (double X, double Y)[] pts)
    {
        vm.BeginStroke(pts[0].X, pts[0].Y, 1);
        foreach (var p in pts.Skip(1)) vm.MoveStroke(p.X, p.Y, 1);
        vm.EndStroke();
    }

    /// <summary>
    /// An outline from (200, 200) to (400, 350) on the line layer, the colour
    /// filled inside it, and a shading stroke along y = 320.
    /// </summary>
    private static Foot DrawFoot()
    {
        var vm = VmLayers.BareVm();
        vm.SmoothStrokes = false;
        vm.BrushHardness = 1;
        vm.BrushOpacity = 1;
        vm.BrushFlow = 1;
        while (vm.Doc.Scene.Layers.Count(l => l.Cels.Count > 0) < 3) vm.AddPaintedLayerCommand.Execute(null);
        var layers = vm.Doc.Scene.Layers;
        var painted = Enumerable.Range(0, layers.Count).Where(i => layers[i].Cels.Count > 0).ToList();
        int colour = painted[^3], shade = painted[^2], line = painted[^1];

        vm.ActiveLayerIndex = line;
        vm.ColorHex = "#000000";
        vm.BrushSize = 6;
        Draw(vm, (200, 200), (400, 200), (400, 350), (200, 350), (200, 200));

        vm.ActiveLayerIndex = colour;
        vm.ColorHex = "#e0b080";
        vm.ActiveTool = ToolId.Fill;
        vm.FillAt(300, 280);

        vm.ActiveLayerIndex = shade;
        vm.ActiveTool = ToolId.Brush;
        vm.ColorHex = "#806040";
        vm.BrushSize = 20;
        Draw(vm, (230, 320), (300, 320), (370, 320));

        vm.ActiveTool = ToolId.Select;
        return new Foot(vm, line, colour, shade);
    }

    /// <summary>One layer's drawing as pixels, on its own.</summary>
    private static SKBitmap Pixels(Foot foot, int layer)
    {
        var scene = foot.Vm.Doc.Scene;
        var frame = (Frame)scene.Layers[layer].Cels[0].Frame!;
        return FrameRasterizer.Materialize(frame, scene.Width, scene.Height);
    }

    private static byte AlphaAt(Foot foot, int layer, int x, int y)
    {
        using var bitmap = Pixels(foot, layer);
        return bitmap.GetPixel(x, y).Alpha;
    }

    private static void MoveDown(MainViewModel vm, double by)
    {
        Assert.True(vm.BeginTransform(), $"the transform refused: {vm.AiStatus}");
        vm.CommitTransformAffine(0, 0, 1, 1, 0, 0, by);
    }

    // ---- the report --------------------------------------------------------------

    /// <summary>
    /// The wand clicked inside the outline, on the line layer, selects the
    /// inside of the foot. On the colour layer that is colour — and Ctrl+T
    /// takes it.
    /// </summary>
    [AvaloniaFact]
    public void AWandSelectionInsideTheOutlineTransformsTheColourItEncloses()
    {
        var foot = DrawFoot();
        var vm = foot.Vm;
        vm.WandSampleAllLayers = false;
        vm.ActiveLayerIndex = foot.Line;
        vm.WandSelectAt(300, 280, false, false);
        Assert.True(vm.HasSelection);

        vm.ActiveLayerIndex = foot.Colour;
        output.WriteLine($"colour at (300, 380) before: {AlphaAt(foot, foot.Colour, 300, 380)}");
        MoveDown(vm, 100);

        // Below the foot, where nothing was: the colour moved there.
        var moved = AlphaAt(foot, foot.Colour, 300, 380);
        output.WriteLine($"colour at (300, 380) after moving the selection down 100: {moved}");
        Assert.True(moved > 200, $"no colour arrived: {moved}");
        // And only the colour layer: the outline is where it was.
        Assert.True(AlphaAt(foot, foot.Line, 300, 200) > 200);
        Assert.Equal(0, AlphaAt(foot, foot.Line, 300, 300));
    }

    /// <summary>
    /// The colour layer's own pixels made into a selection, then the line layer
    /// made active: the inner half of the line overlaps the colour, so there is
    /// line ink inside the selection, and Ctrl+T takes it.
    /// </summary>
    [AvaloniaFact]
    public void ASelectionOfTheColourFindsTheLineInkThatOverlapsIt()
    {
        var foot = DrawFoot();
        var vm = foot.Vm;
        vm.SelectLayerAlpha(vm.LayerRows.First(r => r.SceneIndex == foot.Colour), false, false);
        Assert.True(vm.HasSelection);

        vm.ActiveLayerIndex = foot.Line;
        var began = vm.BeginTransform();
        output.WriteLine($"Ctrl+T on the line layer: {began}; {vm.AiStatus}");
        Assert.True(began, vm.AiStatus);
    }

    // ---- the owner's three cases, and the one about empty layers -----------------

    /// <summary>
    /// The same selection, three different picks of layers: each moves exactly
    /// the layers picked, and nothing else.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("line")]
    [InlineData("line+shade")]
    [InlineData("line+shade+colour")]
    public void ASelectionMovesWhatItHoldsOnEveryPickedLayerAndNothingElse(string picked)
    {
        var foot = DrawFoot();
        var vm = foot.Vm;
        // A lasso round the left half of the foot — how the owner selects
        // (Q233) — so every layer has ink both inside and outside it, and each
        // picked one is cut at the edge.
        vm.ActiveLayerIndex = foot.Line;
        vm.ApplySelectionShape(
            [new(150, 150, 1), new(300, 150, 1), new(300, 400, 1), new(150, 400, 1)], false, false);
        Assert.True(vm.HasSelection);

        var rows = picked.Split('+').Select(n => n switch
        {
            "line" => foot.Line,
            "shade" => foot.Shade,
            _ => foot.Colour,
        }).ToList();
        vm.SelectLayer(vm.LayerRows.First(r => r.SceneIndex == rows[0]), toggle: false, range: false);
        foreach (var more in rows.Skip(1))
        {
            vm.SelectLayer(vm.LayerRows.First(r => r.SceneIndex == more), toggle: true, range: false);
        }

        using var lineBefore = Pixels(foot, foot.Line);
        using var shadeBefore = Pixels(foot, foot.Shade);
        using var colourBefore = Pixels(foot, foot.Colour);
        MoveDown(vm, 100);

        foreach (var (layer, name, before) in new[]
                 {
                     (foot.Line, "line", lineBefore),
                     (foot.Shade, "shade", shadeBefore),
                     (foot.Colour, "colour", colourBefore),
                 })
        {
            using var after = Pixels(foot, layer);
            var changed = Changed(before, after);
            var expected = rows.Contains(layer);
            output.WriteLine($"{name}: picked={expected}, pixels changed={changed}");
            Assert.Equal(expected, changed > 0);
        }
    }

    /// <summary>
    /// A picked layer with nothing inside the selection does not stop the
    /// others: they move, it stays as it was.
    /// </summary>
    [AvaloniaFact]
    public void APickedLayerWithNothingInTheSelectionDoesNotStopTheOthers()
    {
        var foot = DrawFoot();
        var vm = foot.Vm;
        // A box over the right-hand end of the shading stroke and nothing of
        // the outline: the colour has ink there, the line has none.
        vm.ApplySelectionShape(
            [new(330, 300, 1), new(380, 300, 1), new(380, 340, 1), new(330, 340, 1)], false, false);

        vm.SelectLayer(vm.LayerRows.First(r => r.SceneIndex == foot.Line), toggle: false, range: false);
        vm.SelectLayer(vm.LayerRows.First(r => r.SceneIndex == foot.Shade), toggle: true, range: false);

        using var lineBefore = Pixels(foot, foot.Line);
        using var shadeBefore = Pixels(foot, foot.Shade);
        MoveDown(vm, 100);
        using var lineAfter = Pixels(foot, foot.Line);
        using var shadeAfter = Pixels(foot, foot.Shade);

        output.WriteLine($"line changed {Changed(lineBefore, lineAfter)}, shade changed {Changed(shadeBefore, shadeAfter)}");
        Assert.Equal(0, Changed(lineBefore, lineAfter));
        Assert.True(Changed(shadeBefore, shadeAfter) > 0);
    }

    private static int Changed(SKBitmap a, SKBitmap b)
    {
        var n = 0;
        for (var y = 0; y < a.Height; y += 2)
        {
            for (var x = 0; x < a.Width; x += 2)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
            }
        }
        return n;
    }
}
