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
        bool viewport = false, bool scrubbing = false)
    {
        var settings = new OnionSettings { Enabled = true, Before = 1, After = 1 };
        onion?.Invoke(settings);
        return new ScenePassBuilder.State(
            frame, active.Id, playing, lightTable, viewport, settings, scrubbing);
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
    /// <b>Q216: a ghost whose drawing the transform is moving follows the drag.</b>
    /// Scaling every drawing on a layer while their ghosts sit at the old size
    /// previews one frame and guesses the rest. A ghost of a drawing outside the
    /// session stays where it is.
    /// </summary>
    [Fact]
    public void GhostsOfDrawingsInTheTransformFollowTheDrag()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();
        var drag = SkiaSharp.SKMatrix.CreateScale(2, 2);
        var live = new ScenePassBuilder.LiveEdit(
            TransformPreview: drag, TransformFrames: [ink.Cels[0].Frame!, ink.Cels[1].Frame!], GhostsFollow: true,
            TransformFrameIds: new HashSet<string> { ink.Cels[0].Frame!.Id, ink.Cels[1].Frame!.Id });
        var built = ScenePassBuilder.Build(scene, StateFor(scene, ink), cache, new TileFallbackTally(), live);
        var ghosts = built.Passes.Where(IsGhost).ToList();

        Assert.Equal(2, ghosts.Count);
        Assert.Contains(ghosts, g => g.Matrix is { } m && m.ScaleX == 2);  // frame 0: in the session
        Assert.Contains(ghosts, g => g.Matrix is null);                     // frame 2: not
    }

    /// <summary>
    /// <b>The perf-warden's measurement:</b> each following ghost resampled a
    /// whole-canvas bitmap per pointer event. When every ghost of a layer
    /// follows, they share a matrix, so they arrive as one composited sheet
    /// drawn through it — one resample however deep the onion is.
    /// </summary>
    [Fact]
    public void FollowingGhostsArriveAsOneSheetThroughTheDrag()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();
        using var sheet = new SkiaSharp.SKBitmap(64, 48);
        var asked = 0;
        var live = new ScenePassBuilder.LiveEdit(
            TransformPreview: SkiaSharp.SKMatrix.CreateScale(2, 2),
            TransformFrames: [.. ink.Cels.Select(c => c.Frame!)], GhostsFollow: true,
            TransformFrameIds: ink.Cels.Select(c => c.Frame!.Id).ToHashSet(),
            GhostSheet: (_, ghosts) => { asked = ghosts.Count; return sheet; });

        var plan = ScenePassBuilder.Describe(scene, StateFor(scene, ink), cache, new TileFallbackTally(), live);
        var sheets = plan.Specs.Where(s => ReferenceEquals(s.Bitmap, sheet)).ToList();

        Assert.Equal(2, asked);                                // both ghosts went into it
        Assert.Single(sheets);                                 // and came out as one pass
        Assert.Equal(2, sheets[0].Matrix!.Value.ScaleX);       // drawn through the drag
        Assert.DoesNotContain(plan.Specs, s => s.Tint is not null); // no loose ghosts left
    }

    /// <summary>
    /// <b>Q216's ramp:</b> each ghost shows the share of the cel it stands at,
    /// so ghosts carry different matrices — and therefore are not merged into
    /// one sheet, which would put them all at one share.
    /// </summary>
    [Fact]
    public void UnderARampEachGhostShowsItsOwnShare()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();
        using var sheet = new SkiaSharp.SKBitmap(64, 48);
        var live = new ScenePassBuilder.LiveEdit(
            TransformPreview: SkiaSharp.SKMatrix.CreateTranslation(100, 0),
            TransformFrames: [.. ink.Cels.Select(c => c.Frame!)], GhostsFollow: true,
            TransformFrameIds: ink.Cels.Select(c => c.Frame!.Id).ToHashSet(),
            GhostSheet: (_, _) => sheet,
            RampPreview: (_, index) => SkiaSharp.SKMatrix.CreateTranslation(50 * index, 0));

        var plan = ScenePassBuilder.Describe(scene, StateFor(scene, ink), cache, new TileFallbackTally(), live);
        var ghosts = plan.Specs.Where(s => s.Tint is not null).ToList();

        Assert.DoesNotContain(plan.Specs, s => ReferenceEquals(s.Bitmap, sheet));
        Assert.Equal([0f, 100f], ghosts.Select(g => g.Matrix!.Value.TransX).Order().ToArray());
    }

    // ---- the owner's pick for the ramp's ghost cost: near live, rest on pause ----

    /// <summary>
    /// Five cels, playhead on the last, two ghosts back: the nearest (cel 3) is
    /// one step away and the far one (cel 2) two.
    /// </summary>
    private static (Layer Ink, Scene Scene, ScenePassBuilder.State State) TwoBack()
    {
        var ink = LayerWith("Ink", 5);
        var scene = new Scene { Width = 64, Height = 48, FrameCount = 5 };
        scene.Layers.Add(ink);
        return (ink, scene, StateFor(scene, ink, o => { o.Before = 2; o.After = 0; }, frame: 4));
    }

    private static ScenePassBuilder.LiveEdit RampEdit(
        Layer ink, SkiaSharp.SKBitmap? farSheet, Action<IReadOnlyList<ScenePassBuilder.PassSpec>>? handed = null) => new(
        TransformPreview: SkiaSharp.SKMatrix.CreateTranslation(100, 0),
        TransformFrames: [.. ink.Cels.Select(c => c.Frame!)], GhostsFollow: true,
        TransformFrameIds: ink.Cels.Select(c => c.Frame!.Id).ToHashSet(),
        RampPreview: (_, index) => SkiaSharp.SKMatrix.CreateTranslation(10 * index, 0),
        RampSettledPreview: (_, index) => SkiaSharp.SKMatrix.CreateTranslation(1000 + index, 0),
        RampFarSheet: (_, far) =>
        {
            handed?.Invoke(far);
            return farSheet;
        });

    /// <summary>
    /// The nearest ghost each side follows every drag event at its own share;
    /// the farther ones arrive as one sheet, drawn untransformed — the resample
    /// they cost was paid once, when the sheet was made at the last pause.
    /// </summary>
    [Fact]
    public void UnderARampOnlyTheNearestGhostIsResampledPerEvent()
    {
        var (ink, scene, state) = TwoBack();
        using var cache = new FrameBitmapCache();
        using var sheet = new SkiaSharp.SKBitmap(64, 48);

        var plan = ScenePassBuilder.Describe(scene, state, cache, new TileFallbackTally(), RampEdit(ink, sheet));
        var sheetPasses = plan.Specs.Where(s => ReferenceEquals(s.Bitmap, sheet)).ToList();
        var loose = plan.Specs.Where(s => s.Tint is not null).ToList();

        Assert.Single(sheetPasses);
        Assert.Null(sheetPasses[0].Matrix);                        // a plain blit, not a resample
        Assert.Single(loose);                                      // the nearest ghost alone
        Assert.Equal(30f, loose[0].Matrix!.Value.TransX);          // at its live share (cel 3)
        Assert.True(plan.Specs.IndexOf(sheetPasses[0]) < plan.Specs.IndexOf(loose[0]),
            "the far ghosts must sit beneath the near one, as their order always had them");
    }

    /// <summary>
    /// What goes into the sheet is the far ghosts at the box as it was at the
    /// last pause — not the live box, which is the point of the sheet.
    /// </summary>
    [Fact]
    public void TheFarGhostsAreHandedOverAtTheSettledShare()
    {
        var (ink, scene, state) = TwoBack();
        using var cache = new FrameBitmapCache();
        using var sheet = new SkiaSharp.SKBitmap(64, 48);
        IReadOnlyList<ScenePassBuilder.PassSpec>? far = null;

        ScenePassBuilder.Describe(scene, state, cache, new TileFallbackTally(), RampEdit(ink, sheet, f => far = f));

        Assert.NotNull(far);
        Assert.Single(far!);                                       // cel 2 only
        Assert.Equal(1002f, far![0].Matrix!.Value.TransX);         // the settled share, not the live one
    }

    /// <summary>
    /// With no sheet to hand (none made yet, or no resolver), the far ghosts are
    /// drawn one by one at the settled share — still right, only not cheap.
    /// </summary>
    [Fact]
    public void WithoutASheetTheFarGhostsAreDrawnAtTheSettledShare()
    {
        var (ink, scene, state) = TwoBack();
        using var cache = new FrameBitmapCache();

        var plan = ScenePassBuilder.Describe(scene, state, cache, new TileFallbackTally(), RampEdit(ink, farSheet: null));
        var ghosts = plan.Specs.Where(s => s.Tint is not null).Select(s => s.Matrix!.Value.TransX).Order().ToArray();

        Assert.Equal([30f, 1002f], ghosts);
    }

    /// <summary>
    /// A region-limited transform moves part of each drawing, and a ghost is one
    /// bitmap — moving it would show strokes moving that are going to stay.
    /// </summary>
    [Fact]
    public void GhostsStayWhenOnlyPartOfEachDrawingMoves()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        var live = new ScenePassBuilder.LiveEdit(
            TransformPreview: SkiaSharp.SKMatrix.CreateScale(2, 2),
            TransformFrames: [.. ink.Cels.Select(c => c.Frame!)], GhostsFollow: false,
            TransformFrameIds: ink.Cels.Select(c => c.Frame!.Id).ToHashSet());
        var built = ScenePassBuilder.Build(scene, StateFor(scene, ink), cache, new TileFallbackTally(), live);

        Assert.All(built.Passes.Where(IsGhost), g => Assert.Null(g.Matrix));
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
    /// <b>A tile-carrying pass is only built when the tiled compositor will
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
        // tiled path unusable anyway.
        var built = Build(scene, StateFor(scene, ink, playing: true, viewport: false), cache);

        Assert.False(built.TileNative);
        Assert.DoesNotContain(built.Passes, p => p.SourceFrame is not null);
        Assert.All(built.Passes, p => Assert.NotNull(p.Bitmap));
    }

    /// <summary>
    /// <b>Dragging the playhead composites through tiles, exactly as playback
    /// does.</b> The gate read <c>IsPlaying</c> alone, so a scrub — B29's own
    /// repro, and a far commoner gesture than pressing play — paid the
    /// full-bitmap cost: 49.8 ms and 570 MB resident at 1080p on sparse cels
    /// against the tile path's 13.4 ms and 135 MB, and 260.9 ms against 32.2 at
    /// 4K. Both states mean the same thing to the tile store, which is that the
    /// sequence is moving.
    /// </summary>
    [Fact]
    public void ScrubbingBuildsTilePassesTheSameWayPlaybackDoes()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        var scrubbed = Build(
            scene,
            StateFor(scene, ink, o => o.Enabled = false, scrubbing: true, viewport: true),
            cache);
        var played = Build(
            scene,
            StateFor(scene, ink, o => o.Enabled = false, playing: true, viewport: true),
            cache);

        output.WriteLine(
            $"scrubbing: tileNative={scrubbed.TileNative}, "
            + $"tilePasses={scrubbed.Passes.Count(p => p.SourceFrame is not null)}");
        output.WriteLine(
            $"playing:   tileNative={played.TileNative}, "
            + $"tilePasses={played.Passes.Count(p => p.SourceFrame is not null)}");

        Assert.True(scrubbed.TileNative);
        Assert.Contains(scrubbed.Passes, p => p.SourceFrame is not null);

        // Stated as an equality rather than two separate assertions: the claim is
        // that a scrub and a playback tick reach the tile store identically, so a
        // future change that turns one off has to turn both off.
        Assert.Equal(played.TileNative, scrubbed.TileNative);
        Assert.Equal(
            played.Passes.Count(p => p.SourceFrame is not null),
            scrubbed.Passes.Count(p => p.SourceFrame is not null));
    }

    /// <summary>
    /// <b>A still document does not, and that is what licenses the tile route at
    /// all.</b> Tiles are allowed while the sequence moves because the pyramid's
    /// resample difference below 100% zoom lasts only as long as the motion; the
    /// still picture is always composed by the bounded route. A gate that stayed
    /// on after the drag would leave a resampled still on screen, so this is the
    /// other half of the test above rather than a restatement of it.
    /// </summary>
    [Fact]
    public void NeitherPlayingNorScrubbingBuildsNoTilePassAtAll()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        var built = Build(
            scene,
            StateFor(scene, ink, o => o.Enabled = false, viewport: true),
            cache);

        Assert.False(built.TileNative);
        Assert.DoesNotContain(built.Passes, p => p.SourceFrame is not null);
        Assert.All(built.Passes, p => Assert.NotNull(p.Bitmap));
    }

    /// <summary>
    /// <b>A scrub keeps its onion ghosts, and they stay bitmap passes.</b> This is
    /// the one real difference from playback — which suppresses ghosts entirely —
    /// and it is why the mixed pass list matters: <c>FlattenTilePasses</c> converts
    /// the tile-native passes and leaves the rest alone, so a scrub with onion on
    /// composes correctly and simply wins less. A ghost that silently vanished
    /// while dragging would be the failure this guards.
    /// </summary>
    [Fact]
    public void AScrubStillDrawsItsGhostsAndTheyAreNotTilePasses()
    {
        var ink = LayerWith("Ink", 3);
        var scene = SceneWith(ink);
        using var cache = new FrameBitmapCache();

        var built = Build(scene, StateFor(scene, ink, scrubbing: true, viewport: true), cache);

        var ghosts = built.Passes.Where(IsGhost).ToList();
        output.WriteLine($"{ghosts.Count} ghost pass(es) of {built.Passes.Count} while scrubbing");

        Assert.NotEmpty(ghosts);
        Assert.All(ghosts, g => Assert.Null(g.SourceFrame));
        Assert.All(ghosts, g => Assert.NotNull(g.Bitmap));

        // And the drawing itself still tiled, or the mixture being asserted is
        // not a mixture.
        Assert.Contains(built.Passes, p => p.SourceFrame is not null);
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
            new ScenePassBuilder.State(0, null, false, false, false, new OnionSettings()),
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
