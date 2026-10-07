using Avalonia.Headless.XUnit;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// An X-sheet edit composites the canvas once.
/// </summary>
/// <remarks>
/// The performance lab, 2026-10-07, on the owner-shaped document: every
/// X-sheet verb spent 1 ms changing the record and 215-465 ms in what listens
/// to the change, most of it two full-canvas publishes of ~200 ms each — and
/// adding a frame paid four. Each publish after a structural edit composites
/// the whole canvas, so every extra one is that cost again for a picture
/// nobody sees.
/// </remarks>
[Collection("BrushState")]
public sealed class OnePublishPerEditTests : BrushStateIsolated
{
    private static MainViewModel Vm()
    {
        var doc = DocumentFactory.CreateDoc(400, 300, paperColor: "#ffffff");
        var ink = doc.Scene.Layers.First(l => !l.IsBackground);
        ink.Cels.Clear();
        foreach (var id in new[] { "A", null, null, "B", null, "C", null, null })
        {
            ink.Cels.Add(new Cel { Frame = id is null ? null : new Frame { Id = id } });
        }
        doc.Scene.FrameCount = 8;
        var vm = new MainViewModel(null);
        vm.ReplaceDocument(doc);
        vm.CurrentFrameIndex = 2;
        return vm;
    }

    private static FrameCell Cell(MainViewModel vm, int index) =>
        vm.LayerRows.First(r => !r.Layer.IsBackground).Cells.First(c => c.Index == index);

    public static TheoryData<string> Verbs() => new()
    {
        "delete", "delete and pull", "insert empty cell", "insert blank keyframe", "extend exposure", "add frame", "undo",
    };

    [AvaloniaTheory]
    [MemberData(nameof(Verbs))]
    public void AnEditPublishesTheCanvasOnce(string verb)
    {
        var vm = Vm();
        if (verb == "undo") vm.ClearCelAt(Cell(vm, 3));
        var before = vm.PublishCount;

        switch (verb)
        {
            case "delete": vm.ClearCelAt(Cell(vm, 3)); break;
            case "delete and pull": vm.DeleteCelAt(Cell(vm, 1)); break;
            case "insert empty cell": vm.InsertBlankFrameAt(Cell(vm, 4)); break;
            case "insert blank keyframe": vm.InsertBlankKeyframeAt(Cell(vm, 4)); break;
            case "extend exposure": vm.ExtendExposureAt(Cell(vm, 3)); break;
            case "add frame": vm.AddFrameCommand.Execute(null); break;
            case "undo": vm.UndoCommand.Execute(null); break;
        }

        Assert.Equal(1, vm.PublishCount - before);
    }
}
