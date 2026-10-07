using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// B398 / Q212: Delete and pull on empty cells after a row's last drawing trims
/// the scene to end at the last drawing on any layer.
/// </summary>
/// <remarks>
/// On a document with more than one layer the row was pulled back and padded
/// back with the same empties, so the command changed nothing and said nothing
/// — reported by the owner several times, each time after a fix whose tests
/// used one layer (where every selection is a column and the scene shortens)
/// or a hold between drawings (which does pull).
/// </remarks>
[Collection("BrushState")]
public sealed class DeleteAndPullTrailingTests : BrushStateIsolated
{
    /// <summary>
    /// Ink: <c>A · · B · · · ·</c>; Other: <c>X · Y · · · · ·</c>; eight frames.
    /// </summary>
    private static MainViewModel TwoLayers(out Layer ink, out Layer other, bool otherDrawsOnTheLast = false)
    {
        var doc = DocumentFactory.CreateDoc(400, 300, paperColor: "#ffffff");
        ink = doc.Scene.Layers.First(l => !l.IsBackground);
        ink.Name = "Ink";
        ink.Cels.Clear();
        foreach (var id in new[] { "A", null, null, "B", null, null, null, null })
        {
            ink.Cels.Add(new Cel { Frame = id is null ? null : new Frame { Id = id } });
        }
        other = new Layer { Name = "Other" };
        foreach (var id in new[] { "X", null, "Y", null, null, null, null, otherDrawsOnTheLast ? "Z" : null })
        {
            other.Cels.Add(new Cel { Frame = id is null ? null : new Frame { Id = id } });
        }
        doc.Scene.Layers.Add(other);
        doc.Scene.FrameCount = 8;
        var vm = new MainViewModel(null);
        vm.ReplaceDocument(doc);
        return vm;
    }

    private static FrameCell Cell(MainViewModel vm, Layer layer, int index) =>
        vm.LayerRows.First(r => r.Layer.Id == layer.Id).Cells.First(c => c.Index == index);

    private static Layer Live(MainViewModel vm, Layer layer) => vm.Doc.Scene.Layers.First(l => l.Id == layer.Id);

    private static string Row(MainViewModel vm, Layer layer) =>
        string.Join(" ", Live(vm, layer).Cels.Take(vm.Doc.Scene.FrameCount).Select(c => c.Frame?.Id ?? "."));

    [AvaloniaFact]
    public void ATrailingEmptyTrimsTheSceneToItsLastDrawing()
    {
        var vm = TwoLayers(out var ink, out var other);
        vm.DeleteCelAt(Cell(vm, ink, 6));

        Assert.Equal(4, vm.Doc.Scene.FrameCount); // B, at frame 4, is the last drawing on any layer
        Assert.Equal("A . . B", Row(vm, ink));
        Assert.Equal("X . Y .", Row(vm, other));
        Assert.Contains("frame 4", vm.AiStatus);
    }

    [AvaloniaFact]
    public void AHoldBetweenDrawingsStillOnlyPulls()
    {
        var vm = TwoLayers(out var ink, out var other);
        vm.DeleteCelAt(Cell(vm, ink, 1));

        Assert.Equal(8, vm.Doc.Scene.FrameCount);
        Assert.Equal("A . B . . . . .", Row(vm, ink));
        Assert.Equal("X . Y . . . . .", Row(vm, other));
    }

    /// <summary>Nothing to trim is said, not silent — the silence was the complaint.</summary>
    [AvaloniaFact]
    public void WhenAnotherLayerDrawsOnTheLastFrameNothingIsTrimmedAndTheLayerIsNamed()
    {
        var vm = TwoLayers(out var ink, out _, otherDrawsOnTheLast: true);
        vm.DeleteCelAt(Cell(vm, ink, 6));

        Assert.Equal(8, vm.Doc.Scene.FrameCount);
        Assert.Contains("Other", vm.AiStatus);
    }

    [AvaloniaFact]
    public void OneUndoPutsTheTrimmedFramesBack()
    {
        var vm = TwoLayers(out var ink, out var other);
        vm.DeleteCelAt(Cell(vm, ink, 6));
        Assert.Equal(4, vm.Doc.Scene.FrameCount);

        vm.UndoCommand.Execute(null);

        Assert.Equal(8, vm.Doc.Scene.FrameCount);
        Assert.Equal("A . . B . . . .", Row(vm, ink));
        Assert.Equal("X . Y . . . . .", Row(vm, other));
    }
}
