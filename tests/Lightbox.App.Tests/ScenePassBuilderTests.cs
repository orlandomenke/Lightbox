using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Xunit;

namespace Lightbox.App.Tests;

/// <summary>
/// The pass list, asserted on directly.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the whole point of B166's first seam.</b> Until pass building came
/// out of <c>MainViewModel.PublishSnapshot</c>, every property below could only
/// be checked by compositing and looking — which is why the ordering bugs the
/// production comments record ("the paper painted over every ghost") were found
/// by an artist rather than by a test, and why B156–B164 had to be diagnosed
/// from field captures on the owner's machine.
/// </para>
/// <para>
/// No surface, no window, no graphics context: a scene, some state, a cache,
/// a list.
/// </para>
/// </remarks>
public class ScenePassBuilderTests(ITestOutputHelper output)
{
    private static Layer LayerWith(string name, int cels, bool background = false)
    {
        var layer = new Layer { Name = name, IsBackground = background };
        for (var i = 0; i < cels; i++) layer.Cels.Add(new Cel { Frame = new Frame() });
        return layer;
    }

    private static Scene SceneWith(params Layer[] layers)
    {
        var scene = new Scene { Width = 64, Height = 48, FrameCount = 3 };
        scene.Layers.AddRange(layers);
        return scene;
    }

    private static ScenePassBuilder.State StateFor(
        Scene scene, Layer active, Action<OnionSettings>? onion = null,
        bool playing = false, bool lightTable = false, int frame = 1,
        bool unbounded = false, bool viewport = false)
    {
        var settings = new OnionSettings { Enabled = true, Before = 1, After = 1 };
        onion?.Invoke(settings);
        return new ScenePassBuilder.State(
            frame, active.Id, playing, lightTable, unbounded, viewport, settings);
    }

    private static ScenePassBuilder.Result Build(
        Scene scene, ScenePassBuilder.State state, FrameBitmapCache cache) =>
        ScenePassBuilder.Build(scene, state, cache, new TileFallbackTally(), ScenePassBuilder.LiveEdit.None);

    /// <summary>
    /// A ghost is tinted and a drawing is not, which is how the list is read
    /// back apart in these tests.
    /// </summary>
    private static bool IsGhost(RenderPass pass) => pass.Tint is not null;

    /// <summary>
    /// <b>Ghosts sit directly beneath the layer they belong to, not beneath the
    /// whole stack.</b> The comment in the builder records what queueing them
    /// all first cost: invisible while every layer was transparent, and the
    /// moment a document opened on opaque paper the paper painted over every
    /// ghost. Interleaving is also what makes multi-layer onion read correctly.
    /// </summary>
    [Fact]
    public void EachLayersGhostsSitDirectlyUnderThatLayer()
    {
        var paper = LayerWith("Paper", 3, background: true);
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(paper, ink);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, ink), cache);

        // paper's ghosts, paper, ink's ghosts, ink — two ghosts each side of
        // frame 1 with Before = After = 1.
        var kinds = built.Passes.Select(IsGhost).ToArray();
        output.WriteLine(string.Join(" ", kinds.Select(g => g ? "ghost" : "draw")));

        Assert.Equal([true, true, false, true, true, false], kinds);
    }

    /// <summary>
    /// Draw-over lifts them above instead — for checking, when a line you have
    /// just made would otherwise hide the one you are comparing it to.
    /// </summary>
    [Fact]
    public void DrawOverPutsAlayersGhostsAboveIt()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, ink, o => o.DrawOver = true), cache);

        Assert.Equal([false, true, true], built.Passes.Select(IsGhost).ToArray());
    }

    /// <summary>
    /// <b>Ghosts are absent during playback</b>, not merely faint. They are a
    /// drawing aid; while frames are flipping they are noise, and the one thing
    /// playback has to show is the animation. This is also load-bearing for the
    /// tile path, which assumes no ghost pass exists during playback.
    /// </summary>
    [Fact]
    public void PlaybackHasNoGhostPassesAtAll()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        var still = Build(scene, StateFor(scene, ink), cache);
        var playing = Build(scene, StateFor(scene, ink, playing: true), cache);

        Assert.Contains(still.Passes, IsGhost);
        Assert.DoesNotContain(playing.Passes, IsGhost);
    }

    /// <summary>
    /// A light table ghosts other <em>layers</em> rather than other frames, so
    /// the time-based ghosts go — the mode's whole effect on this list is an
    /// absence, which is easy to break and impossible to see in a screenshot.
    /// </summary>
    [Fact]
    public void TheLightTableModeDropsTheTimeBasedGhosts()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, ink, o => o.Mode = OnionMode.LightTable), cache);

        Assert.DoesNotContain(built.Passes, IsGhost);
    }

    /// <summary>
    /// <b>The paper is exempt from the light table's dimming.</b> It is the desk
    /// the sheets lie on, not one of them — dimming it would punch the
    /// checkerboard through an opaque document the moment the mode was switched
    /// on.
    /// </summary>
    [Fact]
    public void TheLightTableDimsTheOtherSheetsAndNotThePaper()
    {
        var paper = LayerWith("Paper", 3, background: true);
        var other = LayerWith("Other", 3);
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(paper, other, ink);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, ink, lightTable: true), cache);
        var drawings = built.Passes.Where(p => !IsGhost(p)).ToList();

        output.WriteLine(string.Join(", ", drawings.Select(p => $"{p.Opacity:F2}")));
        Assert.Equal(3, drawings.Count);
        Assert.Equal(paper.Opacity, drawings[0].Opacity, 6);        // the desk, undimmed
        Assert.True(drawings[1].Opacity < other.Opacity, "the sheet under this one was not dimmed");
        Assert.Equal(ink.Opacity, drawings[2].Opacity, 6);          // the sheet being drawn on
    }

    /// <summary>
    /// <b>The active segment bounds what the bake may fold.</b>
    /// <see cref="LayerStackBake"/> collapses everything outside
    /// <c>[ActiveStart, ActiveEnd)</c> into two baked bitmaps, so a segment that
    /// excluded the active layer's own ghosts would bake passes that change with
    /// the playhead — rebuilding on exactly the publishes the bake exists to
    /// make cheap.
    /// </summary>
    [Fact]
    public void TheActiveSegmentCoversTheActiveLayerAndItsGhosts()
    {
        var paper = LayerWith("Paper", 3, background: true);
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(paper, ink);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, ink), cache);

        output.WriteLine($"active segment [{built.ActiveStart}, {built.ActiveEnd}) of {built.Passes.Count}");
        Assert.Equal(3, built.ActiveStart);                 // after paper's ghosts and paper
        Assert.Equal(built.Passes.Count, built.ActiveEnd);
        // Every pass in the segment belongs to the active layer: its two ghosts
        // and its drawing.
        Assert.Equal(3, built.ActiveEnd - built.ActiveStart);
    }

    /// <summary>
    /// An empty cel still closes the active segment. It is the case the loop
    /// <c>continue</c>s out of early, and the one an "assign at the bottom"
    /// version silently gets wrong — leaving the segment open across the layers
    /// above it.
    /// </summary>
    [Fact]
    public void AnEmptyActiveCelStillClosesTheSegment()
    {
        var empty = new Layer { Name = "Empty" };
        var above = LayerWith("Above", 3);
        var scene = SceneWith(empty, above);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, empty, o => o.Enabled = false), cache);

        Assert.Equal(0, built.ActiveStart);
        Assert.Equal(0, built.ActiveEnd);   // it contributed nothing, and said so
        Assert.Single(built.Passes);        // the layer above still drew
    }

    /// <summary>
    /// <b>A tile-carrying pass is only built when the unbounded compositor will
    /// receive it.</b> That is the requirement <c>Result.TileNative</c> exists to
    /// keep honest: a pass carrying a frame instead of a bitmap is meaningless to
    /// the bounded compositor, which skips it rather than dereferencing nothing —
    /// so it would vanish silently rather than fail.
    /// </summary>
    [Fact]
    public void WithNoViewportNothingBuildsATilePass()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        // Playback turns the tile mode on; no viewport is what makes the
        // unbounded path unusable anyway.
        var built = Build(scene, StateFor(scene, ink, playing: true, viewport: false), cache);

        Assert.False(built.TileNative);
        Assert.DoesNotContain(built.Passes, p => p.SourceFrame is not null);
        Assert.All(built.Passes, p => Assert.NotNull(p.Bitmap));
    }

    /// <summary>
    /// <b>A document with no layers has no active layer, and that is a state
    /// rather than an error.</b> The inline version survived it by accident: it
    /// asked for the active layer inside the loop, which a layerless scene never
    /// entered. Hoisting the question out of the loop made it reachable, and
    /// <c>DocumentTabTests.ADocumentWithNoLayersOpensRatherThanThrowing</c> caught
    /// it — this is that case guarded where it now lives.
    /// </summary>
    [Fact]
    public void ALayerlessSceneBuildsAnEmptyListRatherThanThrowing()
    {
        var scene = SceneWith();
        using var cache = new FrameBitmapCache();

        var built = Build(
            scene,
            new ScenePassBuilder.State(0, null, false, false, false, false, new OnionSettings()),
            cache);

        Assert.Empty(built.Passes);
        Assert.Equal(-1, built.ActiveStart);
        Assert.Equal(-1, built.ActiveEnd);
    }

    /// <summary>
    /// A hidden layer contributes nothing — including its ghosts, which is the
    /// half that a "skip the drawing" implementation leaves behind.
    /// </summary>
    [Fact]
    public void AHiddenLayerContributesNeitherDrawingNorGhosts()
    {
        var hidden = LayerWith("Hidden", 3);
        hidden.Visible = false;
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(hidden, ink);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, ink), cache);

        Assert.Equal(3, built.Passes.Count);   // ink's two ghosts and ink
    }
}
