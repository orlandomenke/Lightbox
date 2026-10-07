using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// <b>Insert blank keyframe</b> — I over the X-sheet: an empty cel (a hold)
/// becomes a keyframe with nothing drawn on it, in place.
/// </summary>
/// <remarks>
/// The owner's vocabulary, which this verb exists to make true on the sheet: a
/// cel is either <em>empty</em> — a hold, showing the drawing before it, as in
/// Krita — or <em>blank</em>, a key with no content, shown as a white cel. No
/// cel at all is the hatch past the end of the scene. Before this, I over the
/// cels picked up the eyedropper, and the only way to get a blank key was to
/// draw on a hold and rub the mark out.
/// </remarks>
[Collection("BrushState")]
public class XsheetBlankKeyframeTests(ITestOutputHelper output) : BrushStateIsolated
{
    private static FrameCell Cell(MainViewModel vm, int sceneLayer, int index) =>
        vm.LayerRows.First(r => r.SceneIndex == sceneLayer).Cells.First(c => c.Index == index);

    private static void Drag(MainViewModel vm)
    {
        vm.BeginStroke(100, 200, 1);
        vm.MoveStroke(250, 220, 1);
        vm.MoveStroke(400, 200, 1);
        vm.EndStroke();
    }

    /// <summary>
    /// Empty: a hold cel, or no cel yet because the row stops short of the scene —
    /// a row past its last cel holds that drawing, so the two read the same.
    /// </summary>
    private static bool IsEmpty(Layer layer, int i) => i >= layer.Cels.Count || layer.Cels[i].Frame is null;

    /// <summary>Paper and two drawing layers, frame 0 drawn on each, frames 1–2 holds.</summary>
    private static MainViewModel DrawnThenHeld()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        Drag(vm);
        vm.AddPaintedLayerCommand.Execute(null);
        Drag(vm);
        // Two holds on the top layer grow the scene to three; the layer below
        // is padded to match, and padding is empties.
        vm.ExtendExposureAtPlayhead();
        vm.ExtendExposureAtPlayhead();
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        foreach (var layer in vm.Doc.Scene.Layers.Where(l => !l.IsBackground))
        {
            Assert.NotNull(layer.Cels[0].Frame);
            Assert.True(IsEmpty(layer, 1));
            Assert.True(IsEmpty(layer, 2));
        }
        return vm;
    }

    [AvaloniaFact]
    public void AnEmptyCelBecomesABlankKeyInPlace()
    {
        var vm = DrawnThenHeld();
        var layer = vm.PaintLayer();
        vm.CurrentFrameIndex = 1;
        var steps = vm.RecordedStepCount;

        vm.InsertBlankKeyframeAtPlayhead();

        var key = Assert.IsType<Frame>(layer.Cels[1].Frame);
        Assert.Empty(key.Strokes);
        Assert.Equal(FrameRole.Key, key.Role);
        Assert.Null(layer.Cels[2].Frame);          // nothing moved
        Assert.Equal(3, vm.Doc.Scene.FrameCount);
        Assert.True(Cell(vm, vm.ActiveLayerIndex, 1).IsKeyed);
        Assert.Equal(steps + 1, vm.RecordedStepCount);

        // Undo restores a snapshot, so the layer is read again rather than held.
        vm.UndoCommand.Execute(null);
        Assert.True(IsEmpty(vm.PaintLayer(), 1));
    }

    /// <summary>A drawn cel is skipped, never emptied — that would be Delete, then this.</summary>
    [AvaloniaFact]
    public void ADrawnCelIsLeftAloneAndRecordsNothing()
    {
        var vm = DrawnThenHeld();
        var layer = vm.PaintLayer();
        vm.CurrentFrameIndex = 0;
        var drawing = layer.Cels[0].Frame;
        var strokes = drawing!.Strokes.Count;
        var steps = vm.RecordedStepCount;

        vm.InsertBlankKeyframeAtPlayhead();

        output.WriteLine(vm.AiStatus);
        Assert.Same(drawing, layer.Cels[0].Frame);
        Assert.Equal(strokes, drawing.Strokes.Count);
        Assert.Equal(steps, vm.RecordedStepCount);
        Assert.Contains("already has a drawing", vm.AiStatus);
    }

    /// <summary>
    /// A selection across rows, holes and drawings included: every empty cel in it
    /// becomes a blank key, the drawn ones stay, and it is one undo step.
    /// </summary>
    [AvaloniaFact]
    public void ASelectionAcrossLayersKeysEveryEmptyCelInOneStep()
    {
        var vm = DrawnThenHeld();
        foreach (var layer in new[] { 1, 2 })
        {
            foreach (var f in new[] { 0, 2 }) vm.ToggleCelSelection(Cell(vm, layer, f));
        }
        var drawnBefore = vm.Doc.Scene.Layers.Select(l => l.Cels[0].Frame).ToList();
        var steps = vm.RecordedStepCount;

        vm.InsertBlankKeyframeAtPlayhead();

        foreach (var layer in vm.Doc.Scene.Layers.Where(l => !l.IsBackground))
        {
            Assert.True(IsEmpty(layer, 1));                            // not picked
            Assert.Empty(Assert.IsType<Frame>(layer.Cels[2].Frame).Strokes);
        }
        Assert.Equal(drawnBefore, vm.Doc.Scene.Layers.Select(l => l.Cels[0].Frame).ToList());
        Assert.Equal(steps + 1, vm.RecordedStepCount);
    }

    /// <summary>
    /// Past the end of the scene a key is an edit that lands, as drawing there is
    /// (Q103), so the scene grows to reach it and the cels between are empties.
    /// </summary>
    [AvaloniaFact]
    public void PastTheEndOfTheSceneItGrowsTheScene()
    {
        var vm = DrawnThenHeld();
        var layer = vm.PaintLayer();
        vm.CurrentFrameIndex = 5;
        Assert.True(vm.PlayheadPastTheEnd);

        vm.InsertBlankKeyframeAtPlayhead();

        Assert.Equal(6, vm.Doc.Scene.FrameCount);
        Assert.Empty(Assert.IsType<Frame>(layer.Cels[5].Frame).Strokes);
        Assert.Null(layer.Cels[3].Frame);
        Assert.Null(layer.Cels[4].Frame);
    }

    // ---- the key ---------------------------------------------------------------

    [Fact]
    public void IOverTheXsheetIsInsertBlankKeyframe_AndKeepsItsOtherMeanings()
    {
        var map = new ShortcutMap();
        var entry = map.Find("xsheet.insertBlankKeyframe");
        Assert.NotNull(entry);
        Assert.Equal(new ShortcutScope(ShortcutContext.Panel, DockPanelId.Xsheet), entry!.Scope);
        Assert.Equal("I", entry.Default?.ToString());

        var i = new KeyEventArgs { Key = Key.I, KeyModifiers = KeyModifiers.None };
        Assert.Equal("xsheet.insertBlankKeyframe", map.IdFor(i, ShortcutScope.In(DockPanelId.Xsheet)));
        Assert.Equal("timeline.insertKey", map.IdFor(i, ShortcutScope.In(DockPanelId.Timeline)));
        Assert.Equal("canvas.pickColor", map.IdFor(i, ShortcutScope.Canvas));
        Assert.Null(map.ConflictWith("xsheet.insertBlankKeyframe", new KeyGesture(Key.I)));
    }

    [Fact]
    public void TheEmptyCellVerbIsRelabelledInTheOwnersWords_AndKeepsItsId()
    {
        var entry = new ShortcutMap().Find("xsheet.insertBlankFrame");
        Assert.NotNull(entry);
        Assert.StartsWith("Insert empty cell", entry!.Name);
    }
}
