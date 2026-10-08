using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// A folder that keeps its layers inside a shape (Q215), from the artist's
/// side: the command and its undo, what the canvas publishes, and what the
/// docker says — the bracket, and the warning when a carve shows nothing.
/// </summary>
[Collection("BrushState")]
public sealed class FolderShapeDockerTests(ITestOutputHelper output) : BrushStateIsolated
{
    /// <summary>Paper, then a folder holding Flat (a bar at y 100) and Shade (bars at 100 and 300).</summary>
    private static (MainViewModel Vm, Layer Flat, Layer Shade) Character()
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        var flat = vm.Doc.Scene.Layers[1];
        flat.Name = "Flat";
        vm.ActiveLayerIndex = 1;
        Bar(vm, 100);
        vm.AddPaintedLayerCommand.Execute(null);
        var shade = vm.Doc.Scene.Layers[2];
        shade.Name = "Shade";
        vm.ActiveLayerIndex = 2;
        Bar(vm, 100);
        Bar(vm, 300);
        var (folder, refusal) = vm.ExternalGroup([flat.Id, shade.Id], "Character", false, out _);
        Assert.True(folder is not null, refusal);
        return (vm, Find(vm, flat.Id), Find(vm, shade.Id));
    }

    private static Layer Find(MainViewModel vm, string id) => vm.Doc.Scene.Layers.Single(l => l.Id == id);

    private static void Bar(MainViewModel vm, double y)
    {
        vm.BeginStroke(20, y, 1);
        vm.MoveStroke(200, y, 1);
        vm.EndStroke();
    }

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
    public void TheShadeOffTheFlatDisappearsAndComesBackOnRelease()
    {
        var (vm, flat, _) = Character();
        using var before = Published(vm);
        var paper = before.GetPixel(150, 450);
        var offFlat = before.GetPixel(110, 300);
        var onFlat = before.GetPixel(110, 100);
        output.WriteLine($"paper {paper}, shade off the flat {offFlat}, on it {onFlat}");
        Assert.NotEqual(paper, offFlat);

        vm.SetFolderShape(flat, keepInside: true);
        using var kept = Published(vm);
        Assert.Equal(paper, kept.GetPixel(110, 300)); // carved away
        Assert.Equal(onFlat, kept.GetPixel(110, 100)); // inside the shape: untouched

        // Nothing was cut: releasing shows the stroke again.
        vm.SetFolderShape(Find(vm, flat.Id), keepInside: false);
        using var released = Published(vm);
        Assert.Equal(offFlat, released.GetPixel(110, 300));
    }

    [AvaloniaFact]
    public void KeepingInsideIsOneUndoStepAndAbsentWhenReleased()
    {
        var (vm, flat, _) = Character();
        var folder = vm.Doc.Scene.LayerGroups.Single();
        Assert.Null(folder.ShapeLayerId);

        vm.SetFolderShape(flat, keepInside: true);
        Assert.Equal(flat.Id, vm.Doc.Scene.LayerGroups.Single().ShapeLayerId);
        vm.UndoCommand.Execute(null);
        Assert.Null(vm.Doc.Scene.LayerGroups.Single().ShapeLayerId);

        vm.SetFolderShape(Find(vm, flat.Id), keepInside: true);
        vm.SetFolderShape(Find(vm, flat.Id), keepInside: false);
        Assert.Null(vm.Doc.Scene.LayerGroups.Single().ShapeLayerId);
    }

    [AvaloniaFact]
    public void ALooseLayerIsToldWhyNothingHappened()
    {
        var vm = VmLayers.PaperVm();
        var loose = vm.Doc.Scene.Layers[1];
        var steps = vm.UndoHistory.Rows.Count;

        vm.SetFolderShape(loose, keepInside: true);

        Assert.Equal(steps, vm.UndoHistory.Rows.Count);
        Assert.Contains("folder first", vm.AiStatus);
    }

    [AvaloniaFact]
    public void TheDockerDrawsTheBracketAndNamesTheShape()
    {
        var (vm, flat, shade) = Character();
        vm.SetFolderShape(flat, keepInside: true);

        Assert.Equal(LayerShapeMark.Shape, vm.ShapeMarkOf(Find(vm, flat.Id)));
        Assert.Equal(LayerShapeMark.KeptTop, vm.ShapeMarkOf(Find(vm, shade.Id)));
        Assert.Contains("Flat", vm.ShapeTipOf(Find(vm, shade.Id)));
        Assert.Contains("Character", vm.ShapeTipOf(Find(vm, shade.Id)));
        Assert.Null(vm.ShapeWarningOf(Find(vm, shade.Id)));

        // A second layer kept inside joins the line rather than starting another.
        vm.ActiveLayerIndex = vm.Doc.Scene.Layers.FindIndex(l => l.Id == shade.Id);
        vm.AddPaintedLayerCommand.Execute(null);
        var light = vm.Doc.Scene.Layers[vm.ActiveLayerIndex];
        Assert.Equal(Find(vm, shade.Id).GroupId, light.GroupId);
        Assert.Equal(LayerShapeMark.KeptTop, vm.ShapeMarkOf(light));
        Assert.Equal(LayerShapeMark.Kept, vm.ShapeMarkOf(Find(vm, shade.Id)));

        // The rows say the same thing the view model does.
        var row = vm.LayerRows.Single(r => r.Layer.Id == flat.Id);
        Assert.True(row.IsFolderShape);
        Assert.True(row.HasShapeMark);
    }

    [AvaloniaFact]
    public void AShapeThatShowsNothingSaysSo()
    {
        var (vm, flat, shade) = Character();
        vm.SetFolderShape(flat, keepInside: true);
        vm.SetLayerVisible(Find(vm, flat.Id), false);

        var warning = vm.ShapeWarningOf(Find(vm, shade.Id));
        output.WriteLine(warning ?? "(none)");
        Assert.NotNull(warning);
        Assert.Contains("hidden", warning);
    }

    [AvaloniaFact]
    public void AClipWithNothingBelowInItsFolderSaysSo()
    {
        var (vm, flat, _) = Character();
        vm.SetLayerClipped(flat, true, alone: true);

        var warning = vm.ShapeWarningOf(Find(vm, flat.Id));
        output.WriteLine(warning ?? "(none)");
        Assert.NotNull(warning);
        Assert.Contains("in this folder", warning);
    }

    [AvaloniaFact]
    public void TheShortcutIsInTheRegistry()
    {
        var map = new ShortcutMap();
        var entry = map.Find("docker.folderShape")!;
        Assert.Equal(Key.G, entry.Default!.Key);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, entry.Default.KeyModifiers);
        Assert.Null(map.ConflictWith("docker.folderShape", entry.Default));
    }
}
