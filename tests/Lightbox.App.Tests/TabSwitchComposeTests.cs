using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Coming back to a tab draws the picture it was showing instead of blending
/// every layer again.
/// </summary>
/// <remarks>
/// <para>
/// Reported 2026-10-16: <i>"switching document tabs often times has a big delay
/// especially with multiple layers this can take up to several seconds."</i>
/// Measured in a headless window on the lab's fixtures: switching to the
/// 31-layer document cost 0.5–1.0 s in one composite, with <b>zero</b> frame
/// cache misses — every layer's pixels were held (B362), and the switch threw
/// away the finished picture and blended them all again.
/// </para>
/// <para>
/// The picture is kept per document, the way the frame cache now is (B483), and
/// counted rather than timed: whether a return composes is exact, a duration on
/// this machine is not.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class TabSwitchComposeTests : BrushStateIsolated
{
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

    /// <summary>The picture the next tab switch publishes, caught as it goes out.</summary>
    private static SKBitmap Arrive(MainViewModel vm, DocumentTab tab)
    {
        SKBitmap? grabbed = null;
        void Capture(RenderSnapshot s)
        {
            grabbed?.Dispose();
            using var img = s.Materialise(null);
            var bmp = new SKBitmap(img.Width, img.Height);
            img.ReadPixels(bmp.Info, bmp.GetPixels(), bmp.RowBytes, 0, 0);
            grabbed = bmp;
        }
        vm.SnapshotChanged += Capture;
        try
        {
            vm.ActiveTab = tab;
        }
        finally
        {
            vm.SnapshotChanged -= Capture;
        }
        return grabbed ?? Published(vm);
    }

    /// <summary>A document with several layers, each with a mark, so a composite is not trivial.</summary>
    private static MainViewModel Layered(int layers = 4)
    {
        var vm = VmLayers.PaperVm();
        vm.SmoothStrokes = false;
        for (var l = 1; l < layers; l++) vm.AddPaintedLayerCommand.Execute(null);
        for (var l = 1; l < vm.Doc.Scene.Layers.Count; l++)
        {
            vm.ActiveLayerIndex = l;
            vm.BeginStroke(40 + l * 30, 60, 1);
            vm.MoveStroke(500, 300 + l * 10, 1);
            vm.EndStroke();
        }
        return vm;
    }

    /// <summary>
    /// Publishes that blended layers, rather than drew a kept picture. A switch
    /// publishes more than once, and the second always finds the first's
    /// picture, so counting hits alone would pass on a build that composes.
    /// </summary>
    private static int Composed(MainViewModel vm) => vm.ComposeCount - vm.RevisitHits;

    private static void Stroke(MainViewModel vm, float y)
    {
        vm.BeginStroke(100, y, 1);
        vm.MoveStroke(400, y, 1);
        vm.EndStroke();
    }

    private static DocumentTab OpenSecond(MainViewModel vm)
    {
        vm.NewDocument(new NewDocumentSettings("Second", 960, 540, 12, 72, Scene.DefaultBackgroundColor, false));
        Published(vm).Dispose();
        return vm.ActiveTab!;
    }

    [AvaloniaFact]
    public void ComingBackToATabComposesNothing()
    {
        var vm = Layered();
        Published(vm).Dispose();
        var first = vm.ActiveTab!;
        OpenSecond(vm);

        var composed = Composed(vm);
        using var shown = Arrive(vm, first);

        Assert.True(Composed(vm) == composed,
            $"coming back blended every layer {Composed(vm) - composed} time(s) instead of drawing the picture the tab was showing");
    }

    [AvaloniaFact]
    public void ComingBackShowsTheSamePicture()
    {
        // The other half of the guard above: cheap must not mean wrong. Drawn on
        // the other tab in between, so the history and the render epoch have
        // both moved on while this one was away.
        var vm = Layered();
        using var before = Published(vm);
        var first = vm.ActiveTab!;
        OpenSecond(vm);
        Stroke(vm, 200);

        using var after = Arrive(vm, first);

        Assert.True(before.Bytes.AsSpan().SequenceEqual(after.Bytes), "the tab came back showing a different picture");
    }

    [AvaloniaFact]
    public void AnEditOnTheReturnedTabStillShows()
    {
        // The kept picture must stop being used the moment its document changes.
        var vm = Layered();
        Published(vm).Dispose();
        var first = vm.ActiveTab!;
        OpenSecond(vm);
        Arrive(vm, first).Dispose();

        vm.ActiveLayerIndex = 1;
        Stroke(vm, 450);
        using var shot = Published(vm);

        Assert.NotEqual(shot.GetPixel(250, 450), shot.GetPixel(250, 520));
    }

    [AvaloniaFact]
    public void ADocumentChangedWhileAwayIsComposedAgain()
    {
        // A document can change without being looked at — an undo's history is
        // its own, but a drawing dropped from the cache (a swatch recolour reaches
        // every open document, B102) comes back as a new bitmap. Simulated here by
        // dropping the parked document's drawings, which is what that path does.
        var vm = Layered();
        Published(vm).Dispose();
        var first = vm.ActiveTab!;
        OpenSecond(vm);
        foreach (var frame in first.Doc.Scene.Layers.SelectMany(l => l.Cels).Select(c => c.Frame).OfType<Frame>())
        {
            vm.FrameCache.Invalidate(frame.Id);
        }

        var composed = Composed(vm);
        Arrive(vm, first).Dispose();

        Assert.True(Composed(vm) > composed, "a document whose drawings changed while away came back as the old picture");
    }

    [AvaloniaFact]
    public void ALeftTabKeepsOnePicture()
    {
        // One per open document is what a return needs. Every frame visited
        // before leaving would be the revisit cache's whole budget per tab.
        var vm = Layered();
        vm.AddFrameCommand.Execute(null);
        vm.AddFrameCommand.Execute(null);
        for (var f = 0; f < 3; f++)
        {
            vm.CurrentFrameIndex = f;
            Published(vm).Dispose();
        }
        var first = vm.ActiveTab!;
        OpenSecond(vm);

        Assert.Equal(1, first.State.Picture?.Count ?? 0);
    }

    [AvaloniaFact]
    public void ClosingATabLetsGoOfItsPicture()
    {
        var vm = Layered();
        Published(vm).Dispose();
        var first = vm.ActiveTab!;
        OpenSecond(vm);
        Assert.NotNull(first.State.Picture);

        vm.CloseTab(first);

        Assert.Null(first.State.Picture);
        Assert.Null(first.State.Bake);
    }

    [AvaloniaFact]
    public void DrawingAfterAReturnDoesNotRebuildTheBake()
    {
        // The second half of what a switch used to cost: the bake was reset on
        // the way in, so the first strokes after a return blended the layers
        // beneath into a new one — about the same again as the composite.
        var vm = Layered();
        for (var i = 0; i < 3; i++) Stroke(vm, 420 + i * 10);
        Published(vm).Dispose();
        var bake = vm.StackBake;
        Assert.True(bake.Rebuilds > 0, "the layers beneath should have baked before leaving, which is the case under test");
        var rebuilds = bake.Rebuilds;
        var first = vm.ActiveTab!;
        OpenSecond(vm);

        Arrive(vm, first).Dispose();
        Stroke(vm, 470);
        Stroke(vm, 480);
        Published(vm).Dispose();

        Assert.Same(bake, vm.StackBake);
        Assert.Equal(rebuilds, vm.StackBake.Rebuilds);
    }

    [AvaloniaFact]
    public void ARecolourWhileAwayShowsOnTheReturn()
    {
        // The one way a kept picture could go stale: a swatch edit reaches every
        // open document (B102) and changes how a drawing renders without
        // touching that document's history. With the bake travelling with the
        // tab, its cels are never fetched on the way back — so the bake has to
        // hear about the recolour while it is parked, or the old colour returns.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-tabpicture-{Guid.NewGuid():N}.lbproj");
        var vm = VmLayers.PaperVm();
        try
        {
            vm.SmoothStrokes = false;
            vm.NewProject(root, "Production");
            for (var l = 0; l < 3; l++) vm.AddPaintedLayerCommand.Execute(null);
            var swatch = Assert.Single(vm.ProjectDocker.Project!.Palettes).Swatches[0];
            var row = Assert.Single(vm.PaletteDocker.Swatches, r => r.Id == swatch.Id);
            row.Color = "#ff0000";
            vm.CommitSwatchEdit();

            // The swatch's mark on a layer beneath the one being drawn on, then
            // enough drawing on the top layer for the stack beneath to bake.
            vm.ActiveLayerIndex = 1;
            vm.PickSwatchForTest(swatch.Id);
            Stroke(vm, 100);
            vm.ActiveLayerIndex = vm.Doc.Scene.Layers.Count - 1;
            for (var i = 0; i < 4; i++) Stroke(vm, 300 + i * 20);
            Published(vm).Dispose();
            Assert.True(vm.StackBake.Rebuilds > 0, "the layers beneath should have baked, which is the case under test");
            var first = vm.ActiveTab!;
            using (var red = Published(vm))
            {
                var before = red.GetPixel(250, 100);
                Assert.True(before.Red > before.Green, $"the mark should start red, was {before}");
            }

            OpenSecond(vm);
            row = Assert.Single(vm.PaletteDocker.Swatches, r => r.Id == swatch.Id);
            row.Color = "#00ff00";
            vm.CommitSwatchEdit();

            using var shown = Arrive(vm, first);
            var after = shown.GetPixel(250, 100);
            Assert.True(after.Green > after.Red, $"the recoloured mark came back {after}, the colour it had before");
        }
        finally
        {
            vm.ProjectDocker.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    /// <summary>Pixels of the published picture that are not the paper.</summary>
    private static int Ink(SKBitmap shot)
    {
        var paper = shot.GetPixel(shot.Width - 2, shot.Height - 2);
        var count = 0;
        for (var y = 0; y < shot.Height; y += 2)
        {
            for (var x = 0; x < shot.Width; x += 2)
            {
                if (shot.GetPixel(x, y) != paper) count++;
            }
        }
        return count;
    }

    private static Stroke Bar(double y) => new()
    {
        Tool = ToolKind.Brush,
        Color = "#c02040",
        Points = [new StrokePoint(30, y, 1), new StrokePoint(80, y, 1)],
        Brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 1, Flow = 1, Spacing = 0.2 },
    };

    /// <summary>A project holding one symbol, a red bar, attached to <paramref name="vm"/>.</summary>
    private static Symbol Sword(MainViewModel vm, string root)
    {
        var project = Lightbox.Core.Projects.ProjectIo.Create("Knight", root);
        var sword = new Symbol { Name = "Sword", Fps = 12 };
        sword.Layers = Symbol.Flat("Sword", [new Frame { Strokes = [Bar(30)] }]);
        project.Symbols[sword.Id] = sword;
        vm.ProjectDocker.Project = project;
        vm.RefreshProjectResources();
        return sword;
    }

    [AvaloniaFact]
    public void AnEditToASymbolShowsOnTheReturn()
    {
        // B485. Editing a symbol in its own tab bumps the symbol's version and
        // touches no drawing of the animation that places it, so neither a
        // frame's cache key nor the bake's covered set sees it. Coming back must
        // still show the edit.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-tabsymbol-{Guid.NewGuid():N}.lbproj");
        var vm = VmLayers.PaperVm();
        try
        {
            vm.SmoothStrokes = false;
            var sword = Sword(vm, root);
            var animation = vm.ActiveTab!;
            vm.PlaceSymbol(sword.Id, 40, 40);
            // Draw on another layer as well, so the stack beneath bakes.
            vm.AddPaintedLayerCommand.Execute(null);
            for (var i = 0; i < 4; i++) Stroke(vm, 300 + i * 20);
            int before;
            using (var shot = Published(vm)) before = Ink(shot);

            vm.OpenSymbol(sword);
            vm.AppendExternalStrokes(vm.ActiveLayerForIpc.Id, 0, [Bar(70)]);

            using var back = Arrive(vm, animation);
            Assert.True(Ink(back) > before, $"the symbol's new stroke is missing on the return: {Ink(back)} inked against {before} before");
        }
        finally
        {
            vm.ProjectDocker.Dispose();
            SymbolRegistry.Clear();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public void AnEditToASymbolShowsInEveryDocumentThatPlacesIt()
    {
        // B485, the other half: the fan-out has to reach every open document,
        // not only the one the symbol was opened from.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-tabsymbol-{Guid.NewGuid():N}.lbproj");
        var vm = VmLayers.PaperVm();
        try
        {
            vm.SmoothStrokes = false;
            var sword = Sword(vm, root);
            var first = vm.ActiveTab!;
            vm.PlaceSymbol(sword.Id, 40, 40);
            int firstBefore;
            using (var shot = Published(vm)) firstBefore = Ink(shot);
            var second = OpenSecond(vm);
            vm.PlaceSymbol(sword.Id, 200, 200);
            int secondBefore;
            using (var shot = Published(vm)) secondBefore = Ink(shot);

            vm.OpenSymbol(sword);
            vm.AppendExternalStrokes(vm.ActiveLayerForIpc.Id, 0, [Bar(70)]);

            using (var back = Arrive(vm, first))
            {
                Assert.True(Ink(back) > firstBefore, $"the first document came back without the edit: {Ink(back)} inked against {firstBefore}");
            }
            using (var back = Arrive(vm, second))
            {
                Assert.True(Ink(back) > secondBefore, $"the second document came back without the edit: {Ink(back)} inked against {secondBefore}");
            }
        }
        finally
        {
            vm.ProjectDocker.Dispose();
            SymbolRegistry.Clear();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    [AvaloniaFact]
    public void AnEditToANestedSymbolShowsOnTheReturn()
    {
        // B485 through one level of nesting: the animation places a knight,
        // the knight places the sword, and the sword is what gets edited.
        var root = Path.Combine(Path.GetTempPath(), $"lightbox-tabsymbol-{Guid.NewGuid():N}.lbproj");
        var vm = VmLayers.PaperVm();
        try
        {
            vm.SmoothStrokes = false;
            var sword = Sword(vm, root);
            var knight = new Symbol { Name = "Knight", Fps = 12 };
            knight.Layers = Symbol.Flat("Knight", [new Frame
            {
                Placements = [new SymbolPlacement { SymbolId = sword.Id, X = 60, Y = 60 }],
            }]);
            vm.ProjectDocker.Project!.Symbols[knight.Id] = knight;
            vm.RefreshProjectResources();
            var animation = vm.ActiveTab!;
            vm.PlaceSymbol(knight.Id, 40, 40);
            int before;
            using (var shot = Published(vm)) before = Ink(shot);
            Assert.True(before > 0, "the nested sword should show through the knight");

            vm.OpenSymbol(sword);
            vm.AppendExternalStrokes(vm.ActiveLayerForIpc.Id, 0, [Bar(70)]);

            using var back = Arrive(vm, animation);
            Assert.True(Ink(back) > before, $"the nested edit is missing on the return: {Ink(back)} inked against {before} before");
        }
        finally
        {
            vm.ProjectDocker.Dispose();
            SymbolRegistry.Clear();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
