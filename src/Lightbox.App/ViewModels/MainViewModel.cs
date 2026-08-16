using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

public sealed partial class FrameCell(int index) : ObservableObject
{
    public int Index { get; } = index;

    /// <summary>Index of the owning layer in Scene.Layers (0 = bottom).</summary>
    public int LayerIndex { get; set; }

    public string Display => (Index + 1).ToString();

    [ObservableProperty]
    private bool _isKeyed;

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBreakdown))]
    [NotifyPropertyChangedFor(nameof(IsInbetween))]
    private FrameRole _role = FrameRole.Key;

    public bool IsBreakdown => Role == FrameRole.Breakdown;

    public bool IsInbetween => Role == FrameRole.Inbetween;

    /// <summary>Beyond the timeline's last frame — insertable, but not playable yet.</summary>
    [ObservableProperty]
    private bool _isVirtual;

    /// <summary>Outside the selected playback range (greyed out).</summary>
    [ObservableProperty]
    private bool _outOfRange;

    /// <summary>Part of the Shift+click cel range selection.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _thumb;

    /// <summary>Frame id the current thumb was rendered from (staleness check).</summary>
    public string? ThumbFrameId { get; set; }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly FrameBitmapCache _cache = new();

    /// <summary>
    /// The tile-native sibling of <see cref="_cache"/>, used during playback
    /// so a frame costs its ink rather than its paper. Both are
    /// invalidated through <see cref="InvalidateFrameRender"/> and
    /// <see cref="ClearFrameRenders"/> — one funnel, so they cannot disagree.
    /// </summary>
    private readonly TileFrameCache _tileFrames = new();

    /// <summary>
    /// Flattened viewport bitmaps derived from <see cref="_tileFrames"/>
    /// (B167 phase 2).
    /// </summary>
    /// <remarks>
    /// <b>Not on the invalidation funnel, and that is deliberate rather than an
    /// omission.</b> Its keys carry <see cref="TileFrameCache.StampOf"/>, which
    /// changes whenever a frame's tiles do — so a stale entry cannot be found by
    /// a lookup and ages out of its own LRU. Adding it to the funnel as well
    /// would be a second answer to a question the key already answers, which is
    /// how two mechanisms come to disagree. It is cleared with the document,
    /// where the point is releasing memory rather than correctness.
    /// </remarks>
    private readonly TileFlattenCache _tileFlats = new();

    /// <summary>Diagnostics for tests and the render report.</summary>
    internal TileFrameCache TileFrames => _tileFrames;

    /// <inheritdoc cref="TileFrames"/>
    internal TileFlattenCache TileFlats => _tileFlats;

    /// <inheritdoc cref="TileFrames"/>
    internal long BitmapCacheBytes => _cache.CachedBytes;

    /// <summary>
    /// How many cached bitmaps published snapshots are currently holding
    /// (B125 stage 3). Zero when nothing is in flight.
    /// </summary>
    internal int PinnedPassBitmaps => _cache.PinnedCount;

    /// <summary>
    /// Rasterizes the frames playback is about to show, off the UI thread.
    /// </summary>
    /// <remarks>
    /// Flushed from the same funnel that invalidates the caches, because a warm
    /// is a render of the document as it was: the moment a frame changes,
    /// anything in flight describes a document nobody is looking at. See
    /// <see cref="FramePrewarmer"/> for why that is a generation counter rather
    /// than a per-frame check — a warm is cheap to throw away and expensive to
    /// get subtly wrong.
    /// </remarks>
    private readonly FramePrewarmer _prewarm = new();

    /// <summary>Diagnostics for tests and the render report.</summary>
    internal FramePrewarmer Prewarm => _prewarm;

    /// <summary>How well the frame clock kept time on the last run of playback (B150).</summary>
    internal PlaybackClock.Pacing PlaybackPacing => _clock.Stats;

    /// <summary>Frame-cache behaviour over the last run of playback.</summary>
    internal (long Hits, long Misses, long Evictions, long Bytes, long Budget) FrameCacheTraffic =>
        (_cache.Hits, _cache.Misses, _cache.Evictions, _cache.CachedBytes, FrameBitmapCache.ByteBudget);

    /// <summary>Flatten-cache behaviour over the last run of playback (B167 phase 2).</summary>
    internal (int Hits, int Misses, int Evictions, long Bytes, long Budget) FlattenCacheTraffic =>
        (_tileFlats.Hits, _tileFlats.Misses, _tileFlats.Evictions,
         _tileFlats.CachedBytes, TileFlattenCache.ByteBudget);

    /// <summary>
    /// Bytes held by bitmaps that have left a cache and cannot be freed yet,
    /// because a published snapshot is still reading them (B179).
    /// </summary>
    /// <remarks>
    /// <b>Every cache tracked this and nothing ever printed it.</b> That is the
    /// blind spot B179 was reported through: a machine at 12 GB while every
    /// budget line in the report read comfortably inside its limit. These bytes
    /// are in neither <c>CachedBytes</c> nor the budget by design — they are not
    /// cache contents — but they are real memory, they are unbounded, and a
    /// report that omits them cannot be used to find them.
    /// </remarks>
    internal (long Frames, long Flattens) AwaitingUnpinBytes =>
        (_cache.AwaitingUnpinBytes, _tileFlats.AwaitingUnpinBytes);

    /// <summary>How many distinct bitmaps published snapshots are pinning.</summary>
    internal (int Frames, int Flattens) PinnedBitmaps =>
        (_cache.PinnedCount, _tileFlats.PinnedCount);

    /// <summary>
    /// Tile bytes held, which the report counted in passes and never in bytes.
    /// </summary>
    internal (long Bytes, long Budget) TileStoreBytes =>
        (_tileFrames.AllocatedBytes, TileFrameCache.ByteBudget);

    /// <summary>
    /// The shape of the scene, which is what decides whether it fits the cache.
    /// </summary>
    /// <remarks>
    /// Reported because the first report to show the symptom said the canvas was
    /// 1920x1080 and nothing else — and at 8.3 MB a frame, how many frames and
    /// how many layers is precisely the difference between a cache that holds
    /// the scene and one that thrashes.
    /// </remarks>
    internal (int Frames, int Layers, int Strokes, double Fps) SceneShape
    {
        get
        {
            var scene = Scene;
            var strokes = 0;
            var seen = new HashSet<string>();
            foreach (var layer in scene.Layers)
            {
                foreach (var cel in layer.Cels)
                {
                    if (cel.Frame is { } frame && seen.Add(frame.Id)) strokes += frame.Strokes.Count;
                }
            }
            // The rate actually being played, not the scene's nominal one: the
            // speed percentage is what the clock was started with, so it is what
            // decides the frame period every tick is measured against. Reporting
            // 12 fps for a scene playing at half speed would make the budget in
            // the report twice as generous as the real one.
            var rate = scene.Fps * PlaybackSpeedPercent / 100.0;
            return (scene.FrameCount, scene.Layers.Count, strokes, rate);
        }
    }

    /// <summary>Timeline thumbnails, keyed by drawing rather than by cell (B202).</summary>
    private readonly ThumbnailCache _thumbs = new();

    // InvalidateFrameRender / ClearFrameRenders / AppendToFrameRender — the
    // render-cache funnel — live in MainViewModel.Rendering.cs with the
    // publish pipeline they serve.

    private readonly StrokeBuilder _strokeBuilder = new();
    private readonly PlaybackClock _clock = new();
    private readonly SelectionManager _selectionManager = new();

    /// <summary>Measured repaint cost, shown as headroom in the info strip.</summary>
    public PerformanceMonitor Performance { get; } = new();

    /// <summary>Unified selection manager for canvas objects.</summary>
    public SelectionManager Selection => _selectionManager;

    private DocumentEditor _editor;
    private readonly ComposeRing _composeRing = new();

    /// <summary>
    /// Baked below-active/above-active layer stacks, so a repaint while
    /// drawing blends three passes instead of one per layer — and, since the
    /// key became the described pass list, so the cels under a valid bake are
    /// never fetched at all (B198). Structural changes invalidate through the
    /// key; content changes arrive through <c>InvalidateFrameRender</c>, which
    /// is why that funnel must not grow bypasses.
    /// </summary>
    private readonly LayerStackBake _stackBake = new();

    /// <summary>The bake's counters, for tests and the render report.</summary>
    internal LayerStackBake StackBake => _stackBake;

    /// <summary>The frame cache's counters, for the residency tests.</summary>
    internal FrameBitmapCache FrameCache => _cache;

    /// <summary>Cache of TileStores by bitmap identity to avoid reconverting unchanged frames.</summary>



    /// <summary>What the canvas says it can display: document pixels per screen pixel.</summary>
    private double _displayScale = 1.0;

    /// <summary>
    /// Resolution to composite at, combining what the canvas can show with the
    /// user's quality preference. Capped at 1: compositing beyond document
    /// resolution would invent detail that is not in the record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every quality is display-bounded, including Full.</b> Full used to be
    /// a constant 1.0 — composite the whole document however little of it the
    /// screen can show — which made a frame change on an 8K document cost
    /// ~33 megapixels against the ~2 the monitor can display. Measured by the
    /// bench harness: n^1.04 in canvas area, ~1 s per frame change at 8K on the
    /// dev container, which is the difference between "4K plays back" and "4K
    /// is a slideshow" on real hardware.
    /// </para>
    /// <para>
    /// What distinguishes Full now is a 2× supersampling margin over what the
    /// display needs: strokes are composited with sub-pixel coverage and then
    /// downscaled, which is where the "sharpest" in its description lives. At
    /// 100% zoom and above the margin saturates the cap and Full composites at
    /// document resolution exactly as before — working close is unchanged; only
    /// the zoomed-out case stops paying for pixels nobody can see.
    /// </para>
    /// <para>
    /// This is invariant 7's cheap side: output scale is a canvas transform
    /// (<c>FrameRasterizer</c> scales the surface, never the geometry), so a
    /// composite at 0.4 and a composite at 1.0 are the same marks at different
    /// sizes — view-only, deterministic, and already exercised end-to-end by
    /// the Display and Half paths. The frame cache keys on the scale, so a zoom
    /// step is a cache miss rather than a wrong-sized hit.
    /// </para>
    /// </remarks>
    private double ComposeScale => WorthScaling(EffectiveCanvasQuality switch
    {
        CanvasQuality.Full => Math.Clamp(_displayScale * 2.0, 0.125, 1.0),
        CanvasQuality.Half => Math.Clamp(_displayScale * 0.5, 0.125, 1.0),
        _ => Math.Clamp(_displayScale, 0.125, 1.0),
    });

    /// <summary>
    /// Above this, compositing smaller costs more than compositing everything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A scaled composite is about 2.5× the per-pixel cost of an unscaled
    /// one, and that changes the arithmetic completely (B160).</b> Measured at a
    /// fixed 1920×1080 document, varying only this scale:
    /// </para>
    /// <list type="bullet">
    /// <item>scale 1.0 — 2.07 Mpx, 34.00 ms/tick, <b>16.4 ms/Mpx</b></item>
    /// <item>scale 0.75 — 1.17 Mpx, <b>45.92 ms/tick</b>, 39.4 ms/Mpx</item>
    /// <item>scale 0.625 — 0.81 Mpx, 33.00 ms/tick, 40.7 ms/Mpx</item>
    /// <item>scale 0.5 — 0.52 Mpx, 20.85 ms/tick, 40.2 ms/Mpx</item>
    /// </list>
    /// <para>
    /// At 1.0 the layer blit is a straight copy; below it every tile is drawn
    /// through a scale transform and resampled. The filter is already
    /// <see cref="SKFilterMode.Linear"/> — the cheapest sensible choice, and
    /// <c>SceneRenderer.Downscale</c> says why — so this is not a bad sampler,
    /// it is what interpolation costs.
    /// </para>
    /// <para>
    /// <b>So scaling down to 0.75 composites 44% fewer pixels and takes 35%
    /// longer.</b> Cost is <c>40·s²</c> against <c>16.4</c> for the whole
    /// document, which cross over at <c>s = √(16.4/40) ≈ 0.64</c>. Anything
    /// above that is strictly worse than not scaling at all, so it rounds up to
    /// 1.0 and takes the cheap path. Below it, scaling genuinely pays: 0.5 is
    /// 21 ms against 34.
    /// </para>
    /// <para>
    /// <b>0.7 rather than 0.64</b>, because the ratio is a measurement on one
    /// machine and the penalty is asymmetric: rounding up too eagerly costs a
    /// few milliseconds and sharper pixels, rounding up too late costs more than
    /// the feature saves. Nothing here changes what is drawn — only how many
    /// pixels it is drawn into, which invariant 5 already makes view-only.
    /// </para>
    /// </remarks>
    internal const double ScalingStopsPaying = 0.7;

    private static double WorthScaling(double scale) =>
        scale > ScalingStopsPaying ? 1.0 : scale;

    /// <summary>
    /// The numbers the render report needs, named for that use so nobody mistakes
    /// them for view state. Read-only, and deliberately narrow: the alternative
    /// was making <c>Scene</c>, <c>ComposeScale</c> and <c>_displayScale</c>
    /// public, which would expose three things to everything in order to tell one
    /// diagnostic three facts.
    /// </summary>
    internal int ReportDocWidth => Scene.Width;

    /// <inheritdoc cref="ReportDocWidth"/>
    internal int ReportDocHeight => Scene.Height;

    /// <inheritdoc cref="ReportDocWidth"/>
    internal double ReportComposeScale => ComposeScale;

    /// <summary>
    /// Why frames did not come from tiles, counted across the session.
    /// </summary>
    /// <remarks>
    /// B144's tile path is worth 145 → 14 ms a frame at 1080p, and a frame that
    /// falls back pays the old cost silently. This is what lets
    /// <c>Help ▸ Write a render report</c> answer "why is my playback slow"
    /// with the actual reason rather than with a guess about the machine.
    /// </remarks>
    private readonly TileFallbackTally _tileFallbacks = new();

    /// <summary>The tally, for the render report and for tests.</summary>
    internal TileFallbackTally ReportTileFallbacks => _tileFallbacks;

    /// <inheritdoc cref="ReportDocWidth"/>
    internal double ReportDisplayScale => _displayScale;

    /// <summary>Called by the canvas when zoom or window size changes what it can show.</summary>
    public void SetDisplayScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0) return;
        if (Math.Abs(scale - _displayScale) < 0.001) return;
        _displayScale = scale;
        _publish.InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        Performance.Reset(); // old timings were taken at a different resolution
        PublishSnapshot();
        RefreshDocumentStats();
    }

    /// <summary>
    /// Set the visible document rectangle (B82: viewport culling).
    /// Called from CanvasControl with the rectangle of document space visible at
    /// the current zoom/pan/rotation. Null means the whole document is visible.
    /// This enables the compositor to cull work to only what the view shows,
    /// improving compositing and playback performance.
    /// </summary>
    public void SetViewport(SKRectI? viewport)
    {
        // Avoid triggering a full publish on every view change by comparing
        // and only publishing if the viewport actually changed.
        if (_publish.Viewport == viewport) return;
        _publish.Viewport = viewport;
        PublishSnapshot();
    }


    /// <summary>
    /// Gate for anything that changes pixels or geometry. Hidden and locked
    /// are both refusals, with different reasons, and a locked folder reports
    /// itself rather than blaming the layer inside it. Every mutating path
    /// goes through here — the old hand-written visibility checks covered
    /// paint, fill and AI draw, which is how transform, cel edits and the
    /// external writers ended up unguarded.
    /// </summary>
    private bool CanEdit(Layer? layer, string verb)
    {
        if (layer is null) return false;
        if (!Scene.IsLayerVisible(layer))
        {
            AiStatus = $"Layer \u201c{layer.Name}\u201d is hidden \u2014 enable its visibility to {verb}.";
            return false;
        }
        if (!Scene.IsLayerEditable(layer))
        {
            AiStatus = Scene.GroupOf(layer) is { Locked: true } folder
                ? $"Folder \u201c{folder.Name}\u201d is locked \u2014 unlock it to {verb}."
                : $"Layer \u201c{layer.Name}\u201d is locked \u2014 unlock it to {verb}.";
            return false;
        }
        return true;
    }

    /// <summary>True when the active layer refuses edits — drives the blocked cursor.</summary>
    public bool ActiveLayerBlocked =>
        ActiveLayer is { } layer && (!Scene.IsLayerVisible(layer) || !Scene.IsLayerEditable(layer));

    /// <summary>
    /// Give a smudge or blur that samples all layers the pixels it sampled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once, at the moment the stroke is committed, and it is what makes
    /// the feature affordable: the stroke carries what it read, so every render
    /// path afterwards reproduces it without knowing anything about the layer
    /// stack. Canvas, PNG render, sequence export and sprite-sheet export agree
    /// by construction rather than by four call sites staying in step.
    /// </para>
    /// <para>
    /// Live freezes here too, and that is not a contradiction: the difference
    /// between the two is not whether a sample is taken but whether it is
    /// retaken. A Live stroke is re-frozen by <see cref="RebakeLiveSamples"/>
    /// on every subsequent edit; a Baked one keeps what it got here forever.
    /// </para>
    /// </remarks>
    private void FreezeSampledBackdrop(Stroke stroke)
    {
        if (stroke.Brush.SampleSource == SampleSource.ThisLayer) return;
        using var beneath = CompositeBelowActiveLayer();
        if (beneath is null) return;
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        stroke.Baked = BrushEngine.BakeSample(stroke, beneath, info);
    }

    /// <summary>
    /// Everything visible below the layer being painted on, at the playhead, or
    /// null when there is nothing there.
    /// </summary>
    /// <remarks>
    /// Null rather than a transparent bitmap for the bottom layer, so a smudge
    /// there costs nothing and behaves exactly as it always did.
    /// </remarks>
    private SKBitmap? CompositeBelowActiveLayer()
    {
        var scene = Scene;
        var active = ActiveLayer;
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (ReferenceEquals(layer, active)) break;
            if (!scene.IsLayerVisible(layer)) continue;
            if (ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } frame) continue;
            passes.Add(new RenderPass(
                _cache.Get(frame, scene.Width, scene.Height, celIndex: CurrentFrameIndex),
                null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
        }
        if (passes.Count == 0) return null;
        using var image = SceneRenderer.Compose(
            scene.Width, scene.Height, passes, SKColors.Transparent);
        return SKBitmap.FromImage(image);
    }

    /// <summary>Frame times measured on the render thread.</summary>
    /// <remarks>
    /// The one place a frame cost arrives, so it is also where the app notices
    /// the canvas is not keeping up. See <see cref="ConsiderCanvasRelief"/>.
    /// </remarks>
    public void RecordFrameTime(double milliseconds)
    {
        Performance.RecordFrame(milliseconds);
        ConsiderCanvasRelief();
    }

    /// <summary>
    /// Whether the stroke in progress has committed to a guide, and which one — the
    /// "decide once, then hold" state machine (Q78).
    /// </summary>
    private readonly GuideSnap _guideSnap = new();

    /// <summary>Which AI provider is configured and the artist it makes (Q78).</summary>
    private ConfiguredArtist _ai;

    /// <summary>Character-sheet views flattened to PNG for AI and MCP, cached (B31, Q78).</summary>
    private readonly ReferenceViewImages _referenceImages;

    /// <summary>
    /// What has changed since the last publish, what the last publish was, and whether
    /// the canvas has caught up (Q77, extended with PR222's pacing).
    /// </summary>
    private readonly PublishState _publish = new();

    /// <summary>
    /// The state of the stroke under the pen — scratch bitmaps, dab bookkeeping, the
    /// live post-process draft and its staleness generation (Q77, B189).
    /// </summary>
    private readonly LivePaintSession _live = new();

    // ---- state shared across sections ---------------------------------------
    //
    // Every field below is used by more than one section, so it stays here rather than
    // travelling into one of the partials beside this file. That is the whole rule of
    // the split (Q78): a section's own state goes with it, shared state does not move.
    //
    // The count is the honest measure of how coupled this class still is.
    // `python3 scripts/monolith.py hot` names the worst of them.

    private bool _switchingTabs;

    private bool _settingColorFromSwatch;

    /// <summary>
    /// Repaint what a swatch edit actually changed. Only frames holding a
    /// stroke that references the swatch are dropped from the cache — walking
    /// the stroke record is far cheaper than re-rendering frames whose pixels
    /// cannot have moved, and a palette used on one layer must not cost a
    /// whole-document re-render on every pointer event of a wheel drag.
    /// </summary>
    /// <summary>
    /// Which frames a swatch actually reaches, worked out once per run of edits.
    /// </summary>
    /// <remarks>
    /// <b>B151.</b> The answer cannot change while the wheel is being dragged —
    /// recolouring a swatch does not change which strokes reference it, and
    /// nothing can draw during the drag — so the scan belongs to the run rather
    /// than to the event. Cleared by <see cref="CommitSwatchEdit"/>, which is
    /// what every boundary that could invalidate it already goes through, and by
    /// <see cref="OnDocumentChanged"/> for the structural edits that do not.
    /// </remarks>
    private (string SwatchId, List<Frame> Frames, int Repaints)? _swatchRepaint;

    private readonly AutosaveService _autosave;

    /// <summary>
    /// The two brush configurations being edited, the saved presets, and the guard that
    /// fences applying a preset from writing back into it.
    /// </summary>
    private readonly BrushWorkingSet _brushes = new();

    private string? _backgroundSwatchId = DocumentFactory.WhiteSwatchId;

    private List<List<StrokePoint>> _selectionContours = [];

    /// <summary>
    /// The stabilisation a brush that carries none falls back to. Persisted
    /// with the rest of the brush state, which is where it already lived.
    /// </summary>
    private readonly BrushStabilisation _appStabilisation = new();

    [ObservableProperty]
    private int _activeLayerIndex;

    /// <summary>
    /// True while a playback tick is running, so <c>PublishSnapshot</c> knows to
    /// time itself. It publishes on every stroke as well, and a report that
    /// folded drawing into the playback numbers would be measuring the wrong
    /// thing entirely.
    /// </summary>
    private bool _profilingTick;

    /// <summary>Where a playback tick's time went, for the render report.</summary>
    private readonly Services.TickProfile _tickProfile = new();

    /// <summary>
    /// Where the last committed stroke on this layer ended, or null.
    /// </summary>
    /// <remarks>
    /// What Shift+click joins to. Kept as a remembered point rather than read
    /// back off the record at the moment of the click: an undo, a layer change
    /// or a frame change should all lose the anchor, because "carry on from
    /// where I was" stops being true the moment any of them happens.
    /// </remarks>
    private (double X, double Y)? _lastStrokeEnd;

    /// <summary>
    /// The gradient being dragged. Two points — the axis — and no incremental
    /// state, so it stays out of the brush's stamped-so-far machinery: a
    /// gradient is not built up along the drag, it is redefined by it.
    /// </summary>
    private Stroke? _liveGradient;

    private Stroke? _liveShape;

    private int _playDirection = 1;

    /// <summary>
    /// The transform being dragged — scope, filter, preview matrix and the cached
    /// moving/staying split, which have to be raised and dropped together.
    /// </summary>
    private readonly TransformSession _transform = new();

    /// <summary>
    /// Invalidate only what an undo/redo actually touched: one frame for a
    /// stroke delta, everything for a structural snapshot. Full invalidation
    /// re-rendered every visible frame and every thumbnail — the undo lag.
    /// </summary>
    /// <summary>
    /// Undo/redo lands in two steps: the editor raises Changed while the
    /// cached bitmaps still hold the OLD pixels, and only afterwards do we
    /// learn which frame to drop. Publishing in between would show stale
    /// pixels and pay for a repaint twice, so the resync waits for this.
    /// </summary>
    private bool _applyingEditScope;

    private (int Layer, int Index) _celAnchor;

    /// <summary>
    /// True while stepping through a frame the clock has already decided to
    /// drop. See <see cref="OnPlaybackTick"/> — the playhead moves, the pixels
    /// are not made.
    /// </summary>
    private bool _skippingFrame;

    /// <summary>
    /// Set while committing an edit whose visible effect the caller already
    /// knows and will publish itself (a stroke or fill). Without this, every
    /// pen lift also ran the full document resync — a whole-canvas repaint
    /// and a thumbnail sweep on top of the bounded ones that were enough.
    /// </summary>
    private bool _committingScopedEdit;

    private readonly HashSet<string> _dirtyThumbIds = [];
    private bool _allThumbsDirty;

    /// <summary>How close a point has to be, in document pixels, to be pulled.</summary>
    [ObservableProperty]
    private double _snapTolerance = 12;




    /// <summary>
    /// How much detail to composite. Only affects what is shown while you
    /// work — the record, exports and thumbnails are always full resolution.
    /// </summary>
    [ObservableProperty]
    private CanvasQuality _canvasQuality = CanvasQuality.Display;
}
