using Avalonia.Headless.XUnit;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Onion skin as the artist meets it: what ends up on the canvas, and what
/// survives closing the application.
///
/// The planner — which drawings, in what order, at what opacity — is covered
/// without a window by <c>Lightbox.Core.Tests.OnionSkinTests</c>. What is left
/// to check here is the part only the composite can get wrong: that the ghosts
/// are actually visible, that draw-over really is over, that a light table
/// dims the right layers, and that none of it is on during playback.
/// </summary>
[Collection("BrushState")]
public class OnionTests : BrushStateIsolated
{
    private static MainViewModel Vm() => new(null)
    {
        SmoothStrokes = false,
        ColorHex = "#000000",
        BrushSize = 30,
        BrushHardness = 1,
        BrushOpacity = 1,
        BrushFlow = 1,
        BrushWetEdge = 0,
        BrushGranulation = 0,
        BrushScatter = 0,
        AntiAliasing = false,
    };

    /// <summary>A short horizontal bar centred on (x, y).</summary>
    private static void Bar(MainViewModel vm, double x, double y)
    {
        vm.BeginStroke(x - 30, y, 1);
        vm.MoveStroke(x + 30, y, 1);
        vm.EndStroke();
    }

    private static RenderSnapshot Publish(MainViewModel vm)
    {
        RenderSnapshot? latest = null;
        vm.SnapshotChanged += s => latest = s;
        vm.PublishSnapshot();
        Assert.NotNull(latest);
        return latest!;
    }

    private static SKColor PixelAt(RenderSnapshot snapshot, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(snapshot.Image);
        return bmp.GetPixel(x, y);
    }

    private static SKColor Sample(MainViewModel vm, int x, int y) => PixelAt(Publish(vm), x, y);

    /// <summary>Three keyed drawings, each in its own place, playhead on the last.</summary>
    private static MainViewModel ThreeDrawings()
    {
        var vm = Vm();
        Bar(vm, 100, 100);
        vm.AddFrameCommand.Execute(null);
        Bar(vm, 300, 100);
        vm.AddFrameCommand.Execute(null);
        Bar(vm, 500, 100);
        Assert.Equal(2, vm.CurrentFrameIndex);
        return vm;
    }

    // ---- persistence ------------------------------------------------------------

    /// <summary>
    /// Pillar 2: "persistent onion-skin settings across sessions". Depth and
    /// falloff are how an animator works, not a question each session should
    /// ask again.
    /// </summary>
    [AvaloniaFact]
    public void OnionSettingsSurviveTheApplicationClosing()
    {
        var vm = Vm();
        vm.OnionBefore = 3;
        vm.OnionAfter = 0;
        vm.OnionOpacity = 0.8;
        vm.OnionFalloff = 1;
        vm.OnionKeysOnly = true;
        vm.OnionDrawOver = true;
        vm.OnionPreviousTint = "#00ff00";
        vm.OnionMode = OnionMode.LightTable;

        var next = new MainViewModel(null); // the next launch

        Assert.Equal(3, next.OnionBefore);
        Assert.Equal(0, next.OnionAfter);
        Assert.Equal(0.8, next.OnionOpacity, 5);
        Assert.Equal(1, next.OnionFalloff, 5);
        Assert.True(next.OnionKeysOnly);
        Assert.True(next.OnionDrawOver);
        Assert.Equal("#00ff00", next.OnionPreviousTint);
        Assert.Equal(OnionMode.LightTable, next.OnionMode);
    }

    /// <summary>
    /// Pillar 2: "onion skin that survives a workspace switch". Rearranging
    /// panels is a statement about the screen; how far back you can see is a
    /// statement about the drawing, and the two must not be filed together.
    /// </summary>
    [AvaloniaFact]
    public void RearrangingThePanelsLeavesOnionSkinAlone()
    {
        var vm = Vm();
        vm.OnionBefore = 4;
        vm.OnionOpacity = 0.9;
        vm.OnionKeysOnly = true;

        vm.Workspace.Dock(Lightbox.App.Docking.DockPanelId.Color, Lightbox.App.Docking.DockSide.Left, 0);
        vm.Workspace.SaveAs("Onion test");
        vm.Workspace.Reset();

        Assert.Equal(4, vm.OnionBefore);
        Assert.Equal(0.9, vm.OnionOpacity, 5);
        Assert.True(vm.OnionKeysOnly);

        // And the settings the next session reads are still the same ones.
        Assert.Equal(4, new MainViewModel(null).OnionBefore);
    }

    /// <summary>A new document does not carry onion settings of its own.</summary>
    [AvaloniaFact]
    public void OpeningADocumentDoesNotResetHowFarBackYouCanSee()
    {
        var vm = Vm();
        vm.OnionBefore = 5;

        vm.NewDocument(new NewDocumentSettings("Second", 640, 480, 12, 72, "#ffffff", true));

        Assert.Equal(5, vm.OnionBefore);
    }

    // ---- what lands on the canvas -----------------------------------------------

    [AvaloniaFact]
    public void TheDrawingBeforeThisOneShowsThroughInItsTint()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 1;
        vm.OnionAfter = 1;

        var ghost = Sample(vm, 300, 100);
        Assert.True(ghost.Red > ghost.Blue, $"expected a warm previous-frame ghost, got {ghost}");
    }

    [AvaloniaFact]
    public void DepthDecidesHowFarBackTheGhostsReach()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionAfter = 0;

        vm.OnionBefore = 1;
        Assert.Equal(SKColors.White, Sample(vm, 100, 100));

        vm.OnionBefore = 2;
        var further = Sample(vm, 100, 100);
        Assert.NotEqual(SKColors.White, further);
    }

    [AvaloniaFact]
    public void FalloffMakesTheFurtherGhostFainter()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 2;
        vm.OnionAfter = 0;
        vm.OnionFalloff = 0.5;

        var near = Sample(vm, 300, 100);
        var far = Sample(vm, 100, 100);
        // Both are dark marks over white paper, so fainter means lighter.
        Assert.True(far.Green > near.Green, $"expected the further ghost fainter: near {near}, far {far}");

        // At a falloff of 1 they are equally visible — the setting that is
        // right when you are checking registration across a whole sequence.
        vm.OnionFalloff = 1;
        var flatNear = Sample(vm, 300, 100);
        var flatFar = Sample(vm, 100, 100);
        Assert.Equal(flatNear, flatFar);
    }

    [AvaloniaFact]
    public void PlaybackShowsTheAnimation_NotTheGhosts()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 2;

        vm.IsPlaying = true;
        Assert.Equal(SKColors.White, Sample(vm, 300, 100));

        vm.IsPlaying = false;
        Assert.NotEqual(SKColors.White, Sample(vm, 300, 100));
    }

    [AvaloniaFact]
    public void ACelThatExposesNothingStillShowsItsGhosts()
    {
        // A layer that starts later than the playhead exposes nothing here —
        // and this is exactly the gap you are about to draw into, so the
        // drawings ahead of it must still show. Draw-over used to lose them:
        // the layer bailed out on the empty cel before adding them.
        var vm = Vm();
        Bar(vm, 100, 100);
        vm.AddFrameCommand.Execute(null);
        vm.PaintLayer().Cels[1].Frame = new PaintedFrame();
        Bar(vm, 300, 100);
        vm.PaintLayer().Cels[0].Frame = null; // the layer now begins at frame 1
        vm.CurrentFrameIndex = 0;
        Assert.Null(Lightbox.Core.Timeline.ExposureSheet.ExposedFrame(vm.PaintLayer(), 0));

        vm.OnionSkin = true;
        vm.OnionBefore = 0;
        vm.OnionAfter = 1;

        vm.OnionDrawOver = false;
        Assert.NotEqual(SKColors.White, Sample(vm, 300, 100));

        vm.OnionDrawOver = true;
        Assert.NotEqual(SKColors.White, Sample(vm, 300, 100));
    }

    // ---- draw-over ---------------------------------------------------------------

    /// <summary>
    /// Pillar 2: "draw-over mode". Under is how a lightbox works; over is for
    /// checking a line you have just made against the one it should match, and
    /// under a solid mark that comparison is impossible.
    /// </summary>
    [AvaloniaFact]
    public void DrawOverPutsTheGhostOnTopOfTheCurrentDrawing()
    {
        var vm = Vm();
        Bar(vm, 200, 100);
        vm.AddFrameCommand.Execute(null);
        vm.PaintLayer().Cels[1].Frame = new PaintedFrame();
        Bar(vm, 200, 100); // the same place, on the new drawing
        vm.OnionSkin = true;
        vm.OnionBefore = 1;

        // Under: the opaque black mark hides the ghost entirely.
        vm.OnionDrawOver = false;
        var under = Sample(vm, 200, 100);
        Assert.Equal(under.Red, under.Blue);

        // Over: the red tint washes across it.
        vm.OnionDrawOver = true;
        var over = Sample(vm, 200, 100);
        Assert.True(over.Red > over.Blue, $"expected the ghost over the mark, got {over}");
    }

    // ---- light table --------------------------------------------------------------

    /// <summary>
    /// Pillar 2: "light table mode". A genuinely different question from what
    /// came before — this drawing against the other elements at the same
    /// instant, rather than against its own neighbours in time.
    /// </summary>
    [AvaloniaFact]
    public void ALightTableDimsTheOtherLayersAndDropsTheTimeGhosts()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 2;
        vm.AddPaintedLayerCommand.Execute(null);
        Bar(vm, 700, 100); // on the new, active layer

        var under = vm.Doc.Scene.Layers[vm.ActiveLayerIndex - 1];
        Assert.False(under.IsBackground, "the layer under the active one must be a drawing layer");

        vm.OnionMode = OnionMode.Frames;
        var crisp = Sample(vm, 500, 100);

        vm.OnionMode = OnionMode.LightTable;
        var dimmed = Sample(vm, 500, 100);
        Assert.True(dimmed.Green > crisp.Green, $"expected the sheet under to go faint: {crisp} -> {dimmed}");

        // The active layer stays crisp — it is the sheet you are drawing on.
        Assert.Equal(SKColors.Black, Sample(vm, 700, 100));

        // And the time-based ghosts are gone: this mode answers a different
        // question, and showing both at once is unreadable.
        Assert.Equal(SKColors.White, Sample(vm, 300, 100));
    }

    [AvaloniaFact]
    public void ALightTableLeavesThePaperAlone()
    {
        // The paper is the desk the sheets lie on, not one of them. Dimming it
        // punches the checkerboard through an opaque document.
        var vm = Vm();
        Assert.True(vm.Doc.Scene.Layers[0].IsBackground);
        Bar(vm, 200, 100);

        vm.OnionMode = OnionMode.LightTable;
        Assert.Equal(SKColors.White, Sample(vm, 600, 300));
    }

    // ---- ghost poses ---------------------------------------------------------------

    /// <summary>
    /// Pillar 2: "ghost poses". The extreme you are animating towards stays on
    /// screen however far away it is — depth cannot express that, because the
    /// distance is the whole point.
    /// </summary>
    [AvaloniaFact]
    public void APinnedFrameGhostsFromAnywhereInTheSequence()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 1;
        vm.OnionAfter = 0;

        // Out of onion range, so nothing of drawing 1 is on screen.
        Assert.Equal(SKColors.White, Sample(vm, 100, 100));

        vm.CurrentFrameIndex = 0;
        vm.ToggleGhostFrameCommand.Execute(null);
        Assert.True(vm.CurrentFrameIsGhost);
        vm.CurrentFrameIndex = 2;

        var pinned = Sample(vm, 100, 100);
        Assert.True(pinned.Red > pinned.Blue, $"expected the pinned extreme ghosted, got {pinned}");
    }

    [AvaloniaFact]
    public void APinnedFrameNeverGhostsItself()
    {
        // Standing on the pin, the pin is the drawing on screen. Ghosting it
        // would tint the current drawing and read as a rendering fault.
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 0;
        vm.OnionAfter = 0;
        vm.ToggleGhostFrameCommand.Execute(null);

        Assert.Equal(SKColors.Black, Sample(vm, 500, 100));
    }

    [AvaloniaFact]
    public void UnpinningAndClearingPutTheCanvasBack()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 0;
        vm.OnionAfter = 0;

        vm.CurrentFrameIndex = 0;
        vm.ToggleGhostFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 1;
        vm.ToggleGhostFrameCommand.Execute(null);
        vm.CurrentFrameIndex = 2;
        Assert.True(vm.HasGhostFrames);
        Assert.NotEqual(SKColors.White, Sample(vm, 100, 100));

        vm.CurrentFrameIndex = 0;
        vm.ToggleGhostFrameCommand.Execute(null); // unpin
        Assert.False(vm.CurrentFrameIsGhost);
        vm.CurrentFrameIndex = 2;
        Assert.Equal(SKColors.White, Sample(vm, 100, 100));

        vm.ClearGhostFramesCommand.Execute(null);
        Assert.False(vm.HasGhostFrames);
        Assert.Null(vm.Doc.Scene.GhostFrames);
        Assert.Equal(SKColors.White, Sample(vm, 300, 100));
    }

    /// <summary>
    /// The camera's rule again: optional means absent. A document that never
    /// pins a pose writes no key for it.
    /// </summary>
    [AvaloniaFact]
    public void ADocumentThatPinsNothingWritesNoPinKey()
    {
        var vm = ThreeDrawings();
        Assert.DoesNotContain("ghostFrames", Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc));

        vm.ToggleGhostFrameCommand.Execute(null);
        var json = Lightbox.Core.Serialization.DocJson.Serialize(vm.Doc);
        Assert.Contains("ghostFrames", json);
        Assert.Equal([2], Lightbox.Core.Serialization.DocJson.Deserialize(json).Scene.GhostFrames);
    }

    // ---- per-layer ------------------------------------------------------------------

    [AvaloniaFact]
    public void ALayerWithOnionOffContributesNoGhosts()
    {
        var vm = ThreeDrawings();
        vm.OnionSkin = true;
        vm.OnionBefore = 1;

        Assert.NotEqual(SKColors.White, Sample(vm, 300, 100));

        vm.SetLayerOnionEnabled(vm.PaintLayer(), false);
        Assert.Equal(SKColors.White, Sample(vm, 300, 100));
    }
}
