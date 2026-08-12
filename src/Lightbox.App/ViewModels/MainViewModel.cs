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
    /// The tile-native sibling of <see cref="_cache"/>, used when the canvas
    /// is unbounded so a frame costs its ink rather than its paper. Both are
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

    /// <summary>Whether this document opted into the unbounded canvas.</summary>
    private bool UnboundedCanvasOn =>
        Doc?.Features?.TryGetValue(
            nameof(Lightbox.Core.Projects.FeatureKey.UnboundedCanvas), out var on) == true && on;

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

    /// <summary>Every frame mutation goes through here, whichever cache holds it.</summary>
    private void InvalidateFrameRender(string frameId)
    {
        _cache.Invalidate(frameId);
        _tileFrames.Invalidate(frameId);
        _prewarm.Flush();
    }

    /// <inheritdoc cref="InvalidateFrameRender"/>
    private void ClearFrameRenders()
    {
        _cache.Clear();
        _tileFrames.Clear();
        // Correctness does not need this — every flatten key carries the stamp of
        // tiles that no longer exist, so none of them can be found again. It is
        // here because "the whole document changed" is the moment those bytes are
        // certainly dead, and waiting for an LRU to notice would hold a document's
        // worth of viewports across a document switch.
        _tileFlats.Clear();
        _prewarm.Flush();
    }

    /// <summary>
    /// Commit one stroke's pixels incrementally — onto the cached bitmap, or
    /// into the cached tiles when the unbounded canvas holds this frame as
    /// tiles. Both are invariant 6's shape: work proportional to the stroke.
    /// </summary>
    /// <remarks>
    /// The bitmap path is deliberately not warmed for a tileable unbounded
    /// frame: <c>_cache.Get</c> on a miss materialises a document-sized
    /// bitmap, which is the exact allocation the tile store exists to avoid.
    /// </remarks>
    private void AppendToFrameRender(Lightbox.Core.Documents.Frame target, Stroke stroke)
    {
        // Unconditionally, now that playback warms the tile cache on bounded
        // documents too: a no-op for a frame tiles do not hold, and a stroke
        // tiles cannot say evicts the frame's entry itself. Skipping this on
        // the bounded arm would leave playback-warmed tiles one stroke stale
        // — the next play would show the drawing without its newest line.
        // A warm in flight was started from this frame's record as it stood a
        // stroke ago. The bitmap arm below caches the frame either way, so a
        // stale bitmap warm would be refused on arrival — but the tile arm
        // returns without caching anything, and a stale tile warm would then
        // install a version of the drawing missing its newest line. Flushing is
        // free here: warms are only ever requested while playing.
        _prewarm.Flush();
        _tileFrames.Append(target, stroke, Scene.Width, Scene.Height);
        if (UnboundedCanvasOn && TileFrameCache.CanTileFrame(target)) return;
        FrameRasterizer.Append(_cache.Get(target, Scene.Width, Scene.Height), stroke);
    }

    private readonly StrokeBuilder _strokeBuilder = new();
    private readonly PlaybackClock _clock = new();
    private readonly SelectionManager _selectionManager = new();
    private readonly Lightbox.Core.Projects.FeatureDefaults _featureDefaults = new();

    /// <summary>Measured repaint cost, shown as headroom in the info strip.</summary>
    public PerformanceMonitor Performance { get; } = new();

    /// <summary>Unified selection manager for canvas objects.</summary>
    public SelectionManager Selection => _selectionManager;

    private DocumentEditor _editor;
    private readonly ComposeRing _composeRing = new();

    /// <summary>
    /// Baked below-active/above-active layer stacks, so a repaint while
    /// drawing blends three passes instead of one per layer. Self-invalidating
    /// by key (see its remarks); the only explicit resets are the wholesale
    /// ones where the document itself changes under it.
    /// </summary>
    private readonly LayerStackBake _stackBake = new();

    /// <summary>The bake's counters, for tests and the render report.</summary>
    internal LayerStackBake StackBake => _stackBake;
    private long _publishSeq;

    /// <summary>Cache of TileStores by bitmap identity to avoid reconverting unchanged frames.</summary>


    /// <summary>Document region changed since the last publish (null = everything).</summary>
    private SKRectI? _pendingDirty;
    private bool _dirtyIsWholeCanvas = true;

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
    private double ComposeScale => WorthScaling(CanvasQuality switch
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
        InvalidateWholeCanvas();
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
    /// unblocking infinite canvas and improving playback performance.
    /// </summary>
    public void SetViewport(SKRectI? viewport)
    {
        // Avoid triggering a full publish on every view change by comparing
        // and only publishing if the viewport actually changed.
        if (_pendingViewport == viewport) return;
        _pendingViewport = viewport;
        PublishSnapshot();
    }

    private SKRectI? _pendingViewport;

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

    // ---- brush cursor ----------------------------------------------------------

    private double _cursorPressure = 1;

    /// <summary>
    /// Track live pen pressure in the brush ring. On means the ring shrinks
    /// and grows with the stroke it is about to lay down; off pins it to the
    /// brush's maximum, which some artists prefer as a stable target.
    /// </summary>
    [ObservableProperty]
    private bool _cursorTracksPressure = true;

    partial void OnCursorTracksPressureChanged(bool value) => OnPropertyChanged(nameof(BrushCursorDiameter));

    /// <summary>
    /// Diameter the ring should draw, in document pixels. Taken from
    /// <see cref="BrushEngine.RadiusAt"/> — the same call the engine makes for
    /// each dab — so the ring cannot drift away from the stroke. A mouse
    /// reports pressure 1, so it always shows the true full thickness.
    /// </summary>
    public double BrushCursorDiameter
    {
        get
        {
            var pressure = CursorTracksPressure ? _cursorPressure : 1;
            return Math.Max(1, BrushEngine.RadiusAt(CurrentToolSettings, pressure) * 2);
        }
    }

    /// <summary>
    /// The tip the ring should outline, or null for the round dab.
    /// </summary>
    /// <remarks>
    /// <b>B74.</b> Read from the same <see cref="CurrentToolSettings"/> the engine
    /// stamps from, and passed as an id so the canvas traces the actual tip bitmap
    /// rather than being told a shape. Null on a brush with no tip, which is the
    /// honest answer and not a shrug — the round dab really is an ellipse.
    /// </remarks>
    public string? BrushCursorTipId => CurrentToolSettings.TipId;

    /// <summary>How flat the ring should be: 1 round, less an ellipse.</summary>
    /// <remarks>
    /// The brush's nominal roundness, deliberately <b>not</b>
    /// <c>BrushEngine.RoundnessAt</c>. That one folds in a per-dab jitter seeded
    /// from the dab's position, so a cursor built on it would wobble as the pointer
    /// moved and report the jitter rather than the brush — the same reason
    /// invariant 2 exists, seen from the UI side.
    /// </remarks>
    public double BrushCursorRoundness => Math.Clamp(CurrentToolSettings.Roundness, 0.05, 1);

    /// <summary>
    /// The ring's angle in degrees, so a chisel is previewed at the angle it will
    /// print at.
    /// </summary>
    /// <remarks>
    /// <c>TipRotationDeg</c> only — the base rotation
    /// <see cref="BrushEngine.StampDab"/> starts from. <c>AngleFollowsDirection</c>
    /// adds the stroke's heading and a hovering pointer has no heading, so folding
    /// it in here would mean inventing one; <c>RotationJitter</c> is seeded from the
    /// dab's position and belongs to the mark rather than to the brush.
    /// </remarks>
    public double BrushCursorAngle => CurrentToolSettings.TipRotationDeg;

    /// <summary>Canvas reports what the pen is doing; the ring follows it.</summary>
    public void SetCursorPressure(double pressure, bool penDown)
    {
        // Hovering shows the maximum: a pen off the tablet has no pressure to
        // report, and a ring that collapsed to nothing on hover would be
        // useless for aiming.
        var next = penDown ? Math.Clamp(pressure, 0, 1) : 1;
        if (Math.Abs(next - _cursorPressure) < 0.0001) return;
        _cursorPressure = next;
        OnPropertyChanged(nameof(BrushCursorDiameter));
    }

    /// <summary>Fired with a fresh snapshot whenever the canvas must repaint.</summary>
    public event Action<RenderSnapshot>? SnapshotChanged;

    /// <summary>
    /// The real constructor. Builds the artist through the same path the
    /// Configure window uses, so there is one way a provider becomes an
    /// artist rather than a startup way and a settings way.
    /// </summary>
    public MainViewModel() : this(artist: null)
    {
        ReloadAiProvider();
    }

    /// <summary>Test seam: inject a fake artist (or null for "no API key").</summary>
    public MainViewModel(IAiArtist? artist)
    {
        _artist = artist;
        // Nothing is open. The application no longer invents a document to sit
        // behind the start screen, because Cancel then adopted a canvas nobody
        // chose — the artist got a 960×540 they never asked for, and only
        // noticed once they had drawn on it.
        //
        // `_editor` still points at something, and that is a deliberate choice
        // rather than an oversight. It is read from 86 places here and `Scene`
        // from 192 more; making it genuinely null would put a guard on every one
        // of them, in the second-riskiest file in the repository, to express a
        // state the UI can describe with one boolean. So the placeholder exists,
        // is never in `Tabs`, is never saved, and is never shown —
        // `HasDocument` is what everything else asks.
        _editor = new DocumentEditor(StartupDoc());
        _activeLayerIndex = FirstPaintableLayer(_editor.Doc);
        _editor.Changed += OnDocumentChanged;
        // B147: the canvas holds its own copy of the selected outlines, and only
        // OnStrokeSelectionChanged refreshes it. Every path that reaches the
        // manager directly — picking a guide, a symbol or a reference box, all of
        // which call ClearAllSelections — used to leave that copy behind, still
        // drawn. Subscribing here makes the manager the single source: whatever
        // changes the selection, the outlines follow.
        _selectionManager.SelectionChanged += OnStrokeSelectionChanged;
        _clock.Tick += OnPlaybackTick;
        Settings = AppSettings.Load();
        _snapTolerance = Settings.SnapTolerance;
        // Mirror it where the render thread can see it (B125): the draw op has no
        // route to the view model, and an environment variable still forces it on
        // for headless runs.
        Rendering.GpuComposite.SettingEnabled = Settings.GpuCompositing;
        if (Enum.TryParse<CanvasQuality>(Settings.CanvasQuality, out var storedQuality))
        {
            _canvasQuality = storedQuality;
        }
        _autosave = new AutosaveService(
            () => SaveTargetTab?.Doc ?? Doc,
            Settings.AutosaveInterval,
            () => SaveTargetTab?.FilePath)
        {
            InPlace = Settings.AutosaveInPlace,
        };
        LoadTimingPresets();
        ColorPicker = new ColorPickerViewModel();
        ColorPicker.SetHex(ColorHex);
        ColorPicker.HexCommitted += hex => ColorHex = hex;
        // Picking from the palette is not the same as landing on that colour
        // with the wheel: the stroke records the swatch, so a later recolour
        // reaches the art. Without this the palette inside a picker would be
        // a convenient list of colours and nothing more.
        ColorPicker.SwatchPicked += PaintWithSwatch;
        // The pair adopts rather than duplicates. Adding a colour that is
        // already in the palette is a request to paint with a live colour, and
        // the swatch that is already there does that — making a second one
        // would give you the literal you were trying to get away from.
        ColorPicker.AddIntent = PaletteAddIntent.Adopt;
        // The other half of the pair gets a picker of its own rather than a
        // hex field. Reaching for the background colour is the same act as
        // reaching for the foreground one, and offering a wheel for one and a
        // text box for the other is the kind of asymmetry you notice every
        // single time.
        BackgroundPicker = new ColorPickerViewModel { AddIntent = PaletteAddIntent.Adopt };
        BackgroundPicker.SetHex(BackgroundColorHex);
        BackgroundPicker.HexCommitted += hex =>
        {
            _backgroundSwatchId = null;
            BackgroundColorHex = hex;
        };
        BackgroundPicker.SwatchPicked += id =>
        {
            _backgroundSwatchId = id;
            if (PaletteRegistry.ResolveSwatch(id) is { } swatch) BackgroundColorHex = swatch.Color;
        };
        PaletteDocker = new PaletteDockerViewModel(
            OnSwatchRecoloured, PerformPaletteEdit, PaintWithSwatch, () => ColorHex);
        PaletteDocker.SwatchEditRunEnded += CommitSwatchEdit;
        // The docker shows both scopes once a project is open. Set through
        // properties rather than the constructor because the project docker is
        // built below this one, and because a docker with no project should
        // need no ceremony to say so.
        PaletteDocker.ProjectSource = () => ProjectDocker?.Project;
        PaletteDocker.ProjectEdited = OnProjectChanged;
        GradientDocker = new GradientDockerViewModel(OnGradientEdited, PerformGradientEdit);
        ProjectDocker = new ProjectViewModel(NewAnimationDoc, OpenProjectDocument, OnProjectChanged);
        // HasProject is a forwarding property, so it has no notification of its
        // own. Without this relay the project panel stays hidden after New or
        // Open project: the docker's own callback only fires when the docker
        // edits the project, and adopting one is not an edit.
        InitialiseSymbolBrowser();
        ProjectDocker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not nameof(ProjectViewModel.HasProject)) return;
            OnPropertyChanged(nameof(HasProject));
            // The browser shows the project's symbols, so adopting a project is
            // what fills it — and closing one is what empties it.
            SymbolBrowser.Refresh();
        };
        // The forwarding properties above have no backing field to notify from,
        // so the workspace's notifications are relayed under the names the
        // View menu and the tests already bind to.
        Workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null) return;
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(WorkspaceViewModel.TimelineVisible))
            {
                OnPropertyChanged(nameof(ShowTimeline));
            }
        };
        // Through RegisterResources rather than resetting the registry
        // directly, so the pickers' palette source is wired from the first
        // moment too — otherwise every flyout opens with no palette until
        // something else happens to change the document.
        RegisterResources();
        PaletteDocker.Load(Doc);
        GradientDocker.Load(Doc);
        LoadBrushState();
        SyncLayerChoices();
        SyncLayerRows();
        RefreshThumbnails();
        RefreshDocumentStats();
        // Start on the palette's black rather than on a literal. The first
        // stroke of a session is as worth being recolourable as any other,
        // and it is the one an artist is least likely to go back and re-link.
        ResetColors();
    }

    // ---- document tabs --------------------------------------------------------

    public ObservableCollection<DocumentTab> Tabs { get; } = [];

    [ObservableProperty]
    private DocumentTab? _activeTab;

    private bool _switchingTabs;
    private int _untitledCounter = 1;

    partial void OnActiveTabChanged(DocumentTab? value)
    {
        if (value is null) return;
        foreach (var tab in Tabs) tab.IsActive = tab == value;
        OnPropertyChanged(nameof(ShowTimeline));
        OnPropertyChanged(nameof(ReferenceSheetsView));
        // The template state is per document, so switching tabs changes all
        // three. Without this the File menu kept the previous tab's answer:
        // "Use as template" ticked on a document that is not one, and Update
        // from template greyed out on a copy that could be updated.
        OnPropertyChanged(nameof(IsActiveDocumentTemplate));
        OnPropertyChanged(nameof(TemplateLabel));
        OnPropertyChanged(nameof(CanUpdateFromTemplate));
        if (value.Editor == _editor) return;

        _switchingTabs = true;
        var leaving = Tabs.FirstOrDefault(t => t.Editor == _editor);
        if (leaving is not null)
        {
            leaving.State.FrameIndex = CurrentFrameIndex;
            leaving.State.LayerIndex = ActiveLayerIndex;
            leaving.State.ReferenceIndex = ActiveReferenceIndex;
            // B171. Handed over rather than copied: AttachEditor is about to
            // drop the view model's reference, so the tab becomes the only
            // owner and there is nothing left to alias.
            leaving.State.Selection = HasSelection ? _selectionContours : null;
        }
        AttachEditor(value.Editor);
        // B56, and note that the line below it already had the guard: a document with no layers
        // is loadable, `Clamp(0, 0, -1)` throws, and the frame clamp beside this one was written
        // defensively while the layer clamp was not.
        ActiveLayerIndex = Math.Clamp(value.State.LayerIndex, 0, Math.Max(0, Scene.Layers.Count - 1));
        CurrentFrameIndex = Math.Clamp(value.State.FrameIndex, 0, Math.Max(0, Scene.FrameCount - 1));
        // B67. Not clamped, because the index is already bounds-checked where it
        // is read and an out-of-range value means "this document has fewer
        // strips than that one did" rather than an error to repair.
        ActiveReferenceIndex = value.State.ReferenceIndex;
        // B171. After AttachEditor cleared it, so this is a restore rather than
        // a survival: a tab with no remembered selection arrives with none.
        if (value.State.Selection is { Count: > 0 } remembered)
        {
            _selectionContours = remembered;
            NotifySelection();
        }
        RecallDocumentBrush();
        _switchingTabs = false;
        // After the switch, so a handler asking the view model anything sees the
        // arriving document rather than a half-swapped one. The canvas framing
        // rides on this: it is view-only state (invariant 5) and belongs to the
        // window, which is the only thing that owns a CanvasControl.
        TabSwitched?.Invoke(leaving, value);
    }

    /// <summary>
    /// A different document became active: <c>(leaving, arriving)</c>.
    /// </summary>
    /// <remarks>
    /// <b>B67.</b> Exists because the canvas framing is per document and the
    /// view model must not know what a canvas is. Both tabs are handed over
    /// because a subscriber has to put something down before it picks the next
    /// one up, and by the time <c>PropertyChanged</c> fires for
    /// <see cref="ActiveTab"/> the tab being left has already been forgotten.
    /// </remarks>
    public event Action<DocumentTab?, DocumentTab>? TabSwitched;

    // ---- whose brush is it (Q9) -------------------------------------------------

    /// <summary>
    /// Whether the brush follows the tool or the drawing, right now.
    /// </summary>
    /// <remarks>
    /// A chosen preference wins; otherwise the project type decides, and with
    /// no project open there is no type to ask and it is Global — which is
    /// what the application has always done.
    /// </remarks>
    public BrushScope BrushScope =>
        // No project is not a preference that can be overridden — there is
        // nowhere to keep a brush, so the honest answer is the one the
        // application has always given.
        ProjectDocker.Project is not { } project
            ? BrushScope.Global
            : Settings.BrushScopeChoice ?? BrushScopeDefaults.For(project.Manifest.Type);

    /// <summary>The three answers, in the order they are offered.</summary>
    public IReadOnlyList<string> BrushMemoryChoices { get; } =
        ["Follow the project", "Global", "Per project"];

    /// <summary>
    /// The chosen answer, as the Configure page words it. "Follow the project"
    /// stores nothing, so the default keeps tracking the project type rather
    /// than freezing to whatever it happened to mean the day it was read.
    /// </summary>
    public string BrushMemoryChoice
    {
        get => Settings.BrushScopeChoice switch
        {
            BrushScope.Global => "Global",
            BrushScope.PerProject => "Per project",
            _ => "Follow the project",
        };
        set
        {
            var stored = value switch
            {
                "Global" => nameof(BrushScope.Global),
                "Per project" => nameof(BrushScope.PerProject),
                _ => null,
            };
            if (Settings.BrushMemory == stored) return;
            Settings.BrushMemory = stored;
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(BrushScope));
            // Switching to per-project mid-session should hand back what the
            // project already remembers, rather than waiting for a tab change.
            RecallDocumentBrush();
        }
    }

    /// <summary>
    /// Write the working brush onto the document, so reopening it hands the
    /// brush back.
    /// </summary>
    /// <remarks>
    /// Called on stroke commit rather than on save: the case this exists for
    /// is a session that ended without one, and a bookmark that only survives
    /// a deliberate save is no use to somebody who closed the laptop.
    /// </remarks>
    private void RememberDocumentBrush()
    {
        if (BrushScope != BrushScope.PerProject) return;
        if (ProjectDocker.Project is not { } project) return;
        project.Manifest.Brush = _brushWork.Clone();
    }

    /// <summary>Put the document's remembered brush back in the tool bar, if it has one.</summary>
    /// <remarks>
    /// Silent when the document has no brush recorded — an older file, or one
    /// made under Global — because the alternative is resetting the artist's
    /// brush to a default every time they open something, which is worse than
    /// the problem this solves.
    /// </remarks>
    private void RecallDocumentBrush()
    {
        if (BrushScope != BrushScope.PerProject) return;
        if (ProjectDocker.Project?.Manifest.Brush is not { } remembered) return;
        _brushWork = remembered.Clone();
        // The preset combo would otherwise still name whatever was chosen
        // before the switch, describing a brush that is no longer loaded.
        _applyingPreset = true;
        SelectedBrushPreset = null;
        _applyingPreset = false;
        NotifyBrushProperties();
    }

    /// <summary>The animation tab a save/AI call should target (a reference tab defers to its owner).</summary>
    public DocumentTab? SaveTargetTab => ActiveTab?.Kind switch
    {
        DocumentTabKind.Reference => ActiveTab.Owner ?? ActiveTab,
        // A symbol has no file of its own — it is written by the project's
        // save. Offering Save As on one would produce a document nothing
        // references.
        DocumentTabKind.Symbol => null,
        _ => ActiveTab,
    };

    /// <summary>
    /// The tab a document-scoped operation acts on, or null when nothing is open.
    /// </summary>
    /// <remarks>
    /// Every caller of this used to end in <c>?? Tabs[0]</c>. That fallback was
    /// only ever reached on a symbol tab — where <see cref="SaveTargetTab"/> is
    /// deliberately null — and it was safe because the application could not be
    /// empty. It can now, and <c>Tabs[0]</c> throws there, so the fallback has to
    /// become a question rather than an assumption. Each caller answers it the
    /// same way: do nothing, because the surface that reached it is disabled with
    /// no document open.
    /// </remarks>
    private DocumentTab? TargetTab => SaveTargetTab ?? Tabs.FirstOrDefault();

    /// <summary>Timeline is hidden on reference tabs regardless of the View-menu toggle.</summary>
    public bool ShowTimeline => TimelineVisible && ActiveTab?.Kind != DocumentTabKind.Reference;


    [RelayCommand]
    private void ActivateTab(DocumentTab tab) => ActiveTab = tab;

    private void AttachEditor(DocumentEditor editor)
    {
        _clock.Stop();
        IsPlaying = false;
        StopAudio();
        _strokeBuilder.Cancel();
        ClearLiveEffectState();
        _editor.Changed -= OnDocumentChanged;
        _editor = editor;
        _editor.Changed += OnDocumentChanged;
        // B171. A selection describes *this* document's canvas, in that
        // document's coordinates, so it cannot follow the editor being swapped
        // out. Cleared here rather than in each caller because this is the
        // funnel every document change goes through — a new document, a tab
        // switch, an open, a close — and the previous bug was precisely that
        // four callers each cleared some of the per-document state and none of
        // them cleared this. A tab switch puts its own selection back
        // immediately afterwards; every other path wants the empty one.
        ClearSelectionState();
        // ClearFrameRenders subsumes the _cache.Clear() this used to be: it
        // empties the tile cache alongside it, through the one funnel.
        ClearFrameRenders();
        // The bakes hold bitmaps folded from the old document's cache; the
        // keys would miss anyway, but a document switch should not keep two
        // document-sized bitmaps of a scene nobody is looking at.
        _stackBake.Reset();
        _allThumbsDirty = true;
        ClearPlaybackRange();
        OnDocumentChanged();
    }

    /// <summary>
    /// The document the app opens on. It used to come from
    /// <c>CreateDoc()</c> with no paper colour, which produced a document
    /// whose scene declared white paper while no layer supplied it: the canvas
    /// and the layer thumbnail both showed the transparency checkerboard, and
    /// there was nothing called Background to lock. It is now made the same
    /// way File → New makes one, from the scene's own default.
    /// </summary>
    private static Doc StartupDoc() =>
        DocumentFactory.CreateDoc(paperColor: Scene.DefaultBackgroundColor);

    /// <summary>Index of the first layer an artist can actually draw on.</summary>
    private static int FirstPaintableLayer(Doc doc) =>
        doc.Scene.Layers.FindIndex(l => !l.IsBackground) is var i && i >= 0 ? i : 0;

    /// <summary>Create a document from the File → New dialog in a new tab.</summary>
    /// <summary>
    /// Whether the only thing open is an untouched, unsaved, blank document.
    /// </summary>
    /// <remarks>
    /// The start screen sits over one of these, which is what lets Create on
    /// the document tab reuse it instead of opening a second. Opening the app
    /// and pressing the default button must not leave two tabs, one of which
    /// you never asked for.
    /// </remarks>
    /// <remarks>
    /// <b>B99</b> split this question in two. "Untouched" means *nothing was
    /// drawn*, which is <see cref="DocumentTab.HasWorkToLose"/>; it is no longer
    /// <c>IsDirty</c>, because a never-saved document badges from the moment it
    /// exists and would make every blank document look touched.
    /// </remarks>
    public bool OnlyAnUntouchedBlankDocument =>
        Tabs.Count == 1 && Tabs[0].FilePath is null && !Tabs[0].HasWorkToLose;

    /// <summary>
    /// Whether anything is open at all. The one question every document-scoped
    /// command and every docker asks before doing anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Backed by <see cref="Tabs"/> rather than by the editor, because the editor
    /// deliberately always has a document — see the constructor. A tab is the
    /// thing an artist opened; the placeholder behind it is an implementation
    /// detail that must never become visible, and asking `Tabs` is what keeps
    /// that true.
    /// </para>
    /// <para>
    /// Raised whenever the collection changes, so the UI can bind to it directly.
    /// </para>
    /// </remarks>
    public bool HasDocument => Tabs.Count > 0;

    public void NewDocument(NewDocumentSettings settings) => NewDocument(settings, reuseBlank: false);

    /// <param name="reuseBlank">
    /// Apply the settings to the blank document already on screen rather than
    /// adding a tab, when that is all there is. Only ever true from the start
    /// screen, where a document tab is already open behind it.
    /// </param>
    public void NewDocument(NewDocumentSettings settings, bool reuseBlank)
    {
        if (reuseBlank && OnlyAnUntouchedBlankDocument)
        {
            ReplaceOnlyTab(settings);
            return;
        }
        var doc = DocumentFactory.CreateDoc(
            settings.Width, settings.Height, settings.Fps,
            settings.TransparentBackground ? null : settings.BackgroundColor);
        doc.Scene.Name = settings.Name;
        doc.Scene.Ppi = settings.Ppi;
        doc.Scene.BackgroundColor = settings.BackgroundColor;
        doc.Scene.TransparentBackground = settings.TransparentBackground;

        // Apply feature defaults based on project type if a project is open
        ApplyFeatureDefaults(doc);
        var fresh = new DocumentTab(new DocumentEditor(doc), settings.Name);
        // Land on something paintable. The paper is layer 0 and locked, so
        // selecting it would make the very first stroke bounce.
        fresh.State.LayerIndex = FirstPaintableLayer(doc);
        AddTab(fresh);
        // B99. A document made while a project is open belongs to that project:
        // it gets a row, marked not saved yet, and a project save writes it.
        // Without a Source it was in limbo — no manifest entry, no row, and
        // skipped by SaveProject, which writes only tabs that have one.
        //
        // After AddTab rather than before, and the ordering is load-bearing:
        // adopting announces a project change, which marks the *active* tab's
        // document edited. With `fresh` already active that lands on the right
        // document, and `Source` still being null at that instant costs nothing
        // because adopting has already put the id in the docker's dirty set.
        fresh.Source = ProjectDocker.AdoptNewDocument(settings.Name, doc);
        // The kind of work chosen at creation is a reason to offer that kind's
        // panels — offered, not imposed, which is why it is a choice on the
        // dialog and defaults to leaving the arrangement alone.
        if (settings.Workspace == WorkspaceChoice.ProjectDefaults)
        {
            Workspace.UseDefaultFor(settings.ProjectType);
        }
    }

    /// <summary>Make the one open blank document be the one that was asked for.</summary>
    private void ReplaceOnlyTab(NewDocumentSettings settings)
    {
        var doc = DocumentFactory.CreateDoc(
            settings.Width, settings.Height, settings.Fps,
            settings.TransparentBackground ? null : settings.BackgroundColor);
        doc.Scene.Name = settings.Name;
        doc.Scene.Ppi = settings.Ppi;
        doc.Scene.BackgroundColor = settings.BackgroundColor;
        doc.Scene.TransparentBackground = settings.TransparentBackground;

        var tab = Tabs[0];
        tab.Editor = new DocumentEditor(doc) { MaxUndo = tab.Editor.MaxUndo };
        tab.Title = settings.Name;
        tab.State.LayerIndex = FirstPaintableLayer(doc);
        // B67. A different document in the same tab, so the framing the blank
        // one was left at is not this one's. Same reasoning as the tab switch —
        // it is the *document* the view belongs to, not the slot.
        tab.State.View = null;
        // Attached directly, not through ActivateTab: the tab is already the
        // active one, so the property setter sees no change and the view model
        // would keep pointing at the editor that was just replaced.
        AttachEditor(tab.Editor);
        ActiveLayerIndex = FirstPaintableLayer(doc);
        CurrentFrameIndex = 0;
        // Nothing to put down — the record it belonged to is gone.
        TabSwitched?.Invoke(null, tab);
        if (settings.Workspace == WorkspaceChoice.ProjectDefaults)
        {
            Workspace.UseDefaultFor(settings.ProjectType);
        }
    }

    // ---- project commands ---------------------------------------------------

    /// <summary>
    /// Start a project at <paramref name="root"/>, adopting the document that
    /// is already open as its first animation.
    ///
    /// Adopting rather than starting empty is the point: the artist has been
    /// drawing, and the container should form around that work instead of
    /// asking them to recreate it somewhere else.
    /// </summary>
    public void NewProject(
        string root, string name,
        ProjectType? type = null,
        WorkspaceChoice workspace = WorkspaceChoice.Keep)
    {
        var project = ProjectIo.Create(name, root);
        project.Manifest.Type = type;

        if (SaveTargetTab is { } tab)
        {
            // B83/B84. A project-level document, not an animation of an invented
            // character. Creating one named after the project put the artist's
            // first drawing at `characters/<project>/animations/…` and left a
            // folder called "project" inside "characters" — which is what B84
            // reports, and the two unrequested folders B83 counts.
            var reference = ProjectIo.AddDocument(project, tab.Title, tab.Doc);
            tab.Source = reference;
            // The document's palettes and gradients become the project's:
            // shared is the whole reason the container exists.
            project.Palettes.AddRange(tab.Doc.Palettes);
            foreach (var (id, gradient) in tab.Doc.Gradients) project.Gradients[id] = gradient;
        }

        ProjectDocker.Adopt(project);
        SaveProject(everything: true);
        Remember(root, RecentKind.Project);
        if (workspace == WorkspaceChoice.ProjectDefaults) Workspace.UseDefaultFor(type);
        AiStatus = $"Created project “{name}”.";
    }

    public void OpenProject(string root)
    {
        try
        {
            var project = ProjectIo.Load(root);
            ProjectDocker.Adopt(project);
            // Open the first animation so the project is not an empty shell —
            // and so the registries have something to resolve against.
            if (project.Manifest.Documents.FirstOrDefault() is { } first
                && ProjectIo.LoadDocument(project, first) is { } doc)
            {
                OpenProjectDocument(first, doc);
            }
            OnProjectChanged();
            Remember(root, RecentKind.Project);
            AiStatus = $"Opened project “{project.Name}”.";
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            AiStatus = $"Could not open that project: {ex.Message}";
        }
    }

    /// <summary>
    /// Write the project, and only the animations that changed.
    /// </summary>
    /// <param name="everything">
    /// Write every loaded document regardless. True for the first save of a
    /// new project, where nothing has been "changed" since it was created but
    /// none of it is on disk yet.
    /// </param>
    public void SaveProject(bool everything = false)
    {
        if (ProjectDocker.Project is not { } project) return;
        try
        {
            ProjectIo.Save(project, everything ? null : ProjectDocker.Dirty);
            ProjectDocker.MarkAllSaved();
            foreach (var tab in Tabs)
            {
                // B99. A tab with no Source is not in the project, so a project
                // save does not write it and must not claim to have. It keeps
                // its badge, which is now the truth rather than a stale flag.
                if (tab.Source is not null) tab.MarkSaved();
            }
            AiStatus = $"Saved “{project.Name}”.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AiStatus = $"Could not save the project: {ex.Message}";
        }
    }

    // ---- templates (Q12) --------------------------------------------------------

    /// <summary>
    /// Whether the active document is marked as a template.
    /// </summary>
    /// <remarks>
    /// A template is an ordinary document with a flag, so this is genuinely all
    /// that "make one" does: it does not move, it does not change, it gains a
    /// flag and starts appearing in one more list. Setting it marks the document
    /// edited, because it is a change to the document and has to be saved like
    /// one.
    /// </remarks>
    public bool IsActiveDocumentTemplate
    {
        get => SaveTargetTab?.Doc.IsTemplateDocument ?? false;
        set
        {
            if (SaveTargetTab is not { } tab || tab.Doc.IsTemplateDocument == value) return;
            // B98. Through the editor, not around it. Dirtiness is now derived
            // from the edit record, so a mutation that bypasses it changes the
            // document without the badge noticing — the opposite failure to the
            // one B98 fixes, and the more dangerous of the two.
            tab.Editor.Perform(doc => Core.Projects.Templates.SetTemplate(doc, value));
            MarkDocumentEdited();
            OnPropertyChanged(nameof(IsActiveDocumentTemplate));
            OnPropertyChanged(nameof(TemplateLabel));
            OnPropertyChanged(nameof(CanUpdateFromTemplate));
            AiStatus = value
                ? "Marked as a template. New from template… will offer it."
                : "No longer a template. The document is otherwise unchanged.";
        }
    }

    public string TemplateLabel =>
        IsActiveDocumentTemplate ? "This document is a template" : "Use as template";

    /// <summary>
    /// The project's templates, for the New from template… list.
    /// </summary>
    /// <remarks>
    /// Empty without a project, and that is the whole reason the feature is
    /// project-scoped: a standalone template is a file you Open and then Save as,
    /// which has always worked. What a project adds is being able to <em>list</em>
    /// them.
    /// </remarks>
    public IReadOnlyList<DocumentRef> TemplateChoices =>
        ProjectDocker.Project is { } project ? Core.Projects.Templates.InProject(project) : [];

    /// <summary>Start a new document from a template — a copy, with no live link.</summary>
    public void NewFromTemplate(DocumentRef reference)
    {
        if (ProjectDocker.Project is not { } project) return;
        if (ProjectIo.LoadDocument(project, reference) is not { } template) return;

        var copy = Core.Projects.Templates.NewFromTemplate(template, reference.Id);
        var name = $"{reference.Name} copy";
        var added = ProjectIo.AddDocument(project, name, copy, ProjectDocker.TargetFolder);

        ProjectDocker.Adopt(project);
        ProjectDocker.MarkDirty(added);
        OpenProjectDocument(added, copy);
        AiStatus = $"New from \"{reference.Name}\". It is a copy — editing the template later leaves it alone.";
    }

    /// <summary>
    /// Whether this document can be asked to pull from the template it came from.
    /// </summary>
    /// <remarks>
    /// Needs a project, a recorded template id, and that template still to exist.
    /// A document whose template has been deleted simply cannot be asked, which
    /// is the whole point of the link pointing document → template: nothing
    /// breaks, the option just is not there.
    /// </remarks>
    public bool CanUpdateFromTemplate => TemplateOfActiveDocument() is not null;

    private Doc? TemplateOfActiveDocument()
    {
        if (ProjectDocker.Project is not { } project) return null;
        if (SaveTargetTab?.Doc.TemplateId is not { Length: > 0 } id) return null;
        // B114. Was a concat of three lists; the project has one.
        var reference = project.Manifest.Documents.FirstOrDefault(r => r.Id == id);
        if (reference is null) return null;
        var template = ProjectIo.LoadDocument(project, reference);
        return template is { IsTemplateDocument: true } ? template : null;
    }

    /// <summary>What a pull would change, or null when there is nothing to pull from.</summary>
    public Core.Projects.Templates.PullPreview? PreviewTemplatePull() =>
        TemplateOfActiveDocument() is { } template && SaveTargetTab is { } tab
            ? Core.Projects.Templates.Preview(tab.Doc, template)
            : null;

    /// <summary>
    /// Pull the ticked changes from the template, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The direction is the safety property: the artist reaches out to the
    /// template, one document at a time, when they say so. Nothing ever travels
    /// the other way, so a finished shot cannot change under anybody.
    /// </remarks>
    public int UpdateFromTemplate(Core.Projects.Templates.PullOptions options)
    {
        if (TemplateOfActiveDocument() is not { } template) return 0;
        var changed = 0;
        _editor.Perform(doc => changed = Core.Projects.Templates.Apply(doc, template, options));
        if (changed == 0)
        {
            // Nothing moved, so the undo step would be an empty one the artist
            // has to press through. Drop it.
            _editor.Undo();
            AiStatus = "Nothing to pull — the document already matches its template.";
            return 0;
        }

        OnDocumentChanged();
        MarkDocumentEdited();
        AiStatus = $"Pulled {changed} change{(changed == 1 ? "" : "s")} from the template. One undo puts it back.";
        return changed;
    }

    /// <summary>
    /// Can the current tab be saved without asking where? True for a project
    /// animation and for a loose document that already has a path.
    /// </summary>
    public bool CanSaveInPlace =>
        ProjectDocker.HasProject && SaveTargetTab?.Source is not null
        || SaveTargetTab?.FilePath is { Length: > 0 };

    /// <summary>
    /// Save without a picker. Missing entirely until now — every save opened a
    /// dialog even when the tab already knew where it came from.
    /// </summary>
    public void Save()
    {
        if (ProjectDocker.HasProject && SaveTargetTab?.Source is not null)
        {
            SaveProject();
            return;
        }
        if (SaveTargetTab is not { FilePath: { Length: > 0 } path } tab) return;
        try
        {
            DocJson.Save(tab.Doc, path);
            tab.MarkSaved();
            AiStatus = $"Saved {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AiStatus = $"Could not save: {ex.Message}";
        }
    }

    /// <summary>
    /// A standalone copy of the active document, with every project resource
    /// it references inlined — what "Export document…" writes.
    /// </summary>
    public string ExportStandaloneDocument()
    {
        var doc = SaveTargetTab?.Doc ?? Doc;
        if (ProjectDocker.Project is { } project) doc = ProjectIo.Flatten(doc, project);
        return DocJson.Serialize(doc);
    }

    /// <summary>Open a loaded document in a new tab.</summary>
    public void OpenDocumentTab(Doc doc, string? filePath)
    {
        var title = filePath is null ? NextUntitledName() : TitleFromPath(filePath);
        var tab = new DocumentTab(new DocumentEditor(doc), title) { FilePath = filePath };
        // B136. Land on something paintable — the same line File ▸ New has
        // carried since B56's era, missing here. A saved document's layer 0 is
        // its locked paper, so every document opened from disk started with
        // the one layer that refuses strokes: the cursor showed, the status
        // strip said "locked", and nothing appeared. Reported as "unable to
        // draw on the last build".
        tab.State.LayerIndex = FirstPaintableLayer(doc);
        // B99. Opened from disk means it *is* what is on disk — without this it
        // would inherit the never-saved default and badge a file nobody touched.
        if (filePath is not null) tab.MarkSaved();
        AddTab(tab);
        if (filePath is not null) Remember(filePath, RecentKind.Document);
    }

    // ---- what you had open last -----------------------------------------------

    /// <summary>
    /// Record that something was opened or saved.
    /// </summary>
    /// <remarks>
    /// Saved as well as opened: a document written for the first time is one
    /// you have every reason to come back to, and leaving it out means the
    /// entry only appears the second time you use it.
    /// </remarks>
    public void Remember(string path, RecentKind kind)
    {
        Settings.Recent.Add(path, "", kind, DateTimeOffset.Now);
        Settings.Save();
        OnPropertyChanged(nameof(RecentEntries));
        OnPropertyChanged(nameof(HasRecents));
    }

    /// <summary>The recents that are still on disk, newest first.</summary>
    public IReadOnlyList<RecentItem> RecentEntries => Settings.Recent.Existing();

    public bool HasRecents => RecentEntries.Count > 0;

    [RelayCommand]
    public void ForgetRecents()
    {
        Settings.Recent.Clear();
        Settings.Save();
        OnPropertyChanged(nameof(RecentEntries));
        OnPropertyChanged(nameof(HasRecents));
    }

    /// <summary>
    /// Open something from the recents list, whichever kind it is.
    /// </summary>
    /// <remarks>
    /// One entry point so the menu, the start screen and a double-click all
    /// take the same route — including the part where a file that has since
    /// been moved says so instead of doing nothing.
    /// </remarks>
    public void OpenRecent(RecentItem? item)
    {
        if (item is null) return;
        if (item.Kind == RecentKind.Project)
        {
            if (!Directory.Exists(item.Path))
            {
                AiStatus = $"“{item.Name}” is no longer at {item.Path}.";
                return;
            }
            OpenProject(item.Path);
            return;
        }
        if (!File.Exists(item.Path))
        {
            AiStatus = $"“{item.Name}” is no longer at {item.Path}.";
            return;
        }
        try
        {
            OpenDocumentTab(DocJson.Load(item.Path), item.Path);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            AiStatus = $"Could not open {item.Name}: {ex.Message}";
        }
    }

    /// <summary>Close a tab. The view confirms unsaved changes before calling this.</summary>
    public void CloseTab(DocumentTab tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0) return;
        Tabs.Remove(tab);
        // An animation tab takes its reference-view tabs with it.
        foreach (var orphan in Tabs.Where(t => t.Owner == tab).ToList()) Tabs.Remove(orphan);
        // B99. Closing a document that was never written takes its row with it.
        // Here rather than in the close handler because this is the one funnel
        // every close goes through, and the handler has already resolved the
        // save-or-discard question by the time it calls this: if the artist chose
        // Save the file now exists, so the row stays.
        //
        // A reference view belongs to the document it was opened from, so only a
        // tab that owns its own document can take a row out of the project.
        if (tab.Owner is null) ProjectDocker.ForgetIfNeverWritten(tab.Source);

        // Closing the last tab used to conjure a replacement, which meant there
        // was no way to arrive at an empty application and the canvas of that
        // invented document became whatever you drew on next. Now the workspace
        // simply empties, and the window asks what to open — the same question
        // the start screen asks, at the only other moment it is the right one.
        if (Tabs.Count == 0)
        {
            ActiveTab = null;
            OnPropertyChanged(nameof(HasDocument));
            LastDocumentClosed?.Invoke();
            return;
        }

        OnPropertyChanged(nameof(HasDocument));
        if (ActiveTab == tab || ActiveTab is null || !Tabs.Contains(ActiveTab))
        {
            ActiveTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }
    }

    /// <summary>
    /// The last document was closed and nothing is open.
    /// </summary>
    /// <remarks>
    /// An event rather than the view model opening a dialog itself, for the
    /// reason every other dialog here is the window's job: a view model that
    /// shows windows cannot be tested headlessly, and this one is reached by
    /// `CloseTab`, which the suite drives constantly.
    /// </remarks>
    public event Action? LastDocumentClosed;

    /// <summary>The active document was written to disk: adopt the name, clear the dirty dot.</summary>
    public void NotifySaved(string filePath)
    {
        if (SaveTargetTab is not { } tab) return;
        tab.FilePath = filePath;
        tab.Title = TitleFromPath(filePath);
        tab.MarkSaved();
        Remember(filePath, RecentKind.Document);
        // B99's other half. A document adopted at creation has to be released
        // when the artist gives it a home outside the project — otherwise its row
        // stays, pointing at a file that was never written there, and the next
        // project save writes a second copy inside the project. Saved into the
        // project instead, the record follows the file.
        if (tab.Source is { } source && !ProjectDocker.AdoptSavedPath(source, filePath))
        {
            tab.Source = null;
        }
    }

    private void AddTab(DocumentTab tab)
    {
        var wasEmpty = Tabs.Count == 0;
        Tabs.Add(tab);
        ActiveTab = tab;
        // Coming back from empty is the transition the whole UI hangs off, and a
        // property that only ever falls is worse than no property at all.
        if (wasEmpty) OnPropertyChanged(nameof(HasDocument));
    }

    private string NextUntitledName() => $"Untitled-{++_untitledCounter}";

    private static string TitleFromPath(string path)
    {
        var name = Path.GetFileName(path);
        const string suffix = ".lightbox.json";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(path);
    }

    private void MarkDocumentEdited()
    {
        _autosave.MarkDirty();
        // B31: the encoded reference views are only valid while the drawing is. This is the
        // funnel that sees a stroke commit — OnDocumentChanged returns early for those — so a
        // cache invalidated anywhere else would hand a model art that had since changed.
        InvalidateReferenceViewCache();
        if (_switchingTabs || ActiveTab is not { } tab) return;
        // Here rather than in OnDocumentChanged: stroke commits take that
        // method's scoped-edit early return, and a stroke is exactly the edit
        // an incremental save must not miss.
        if ((tab.Owner ?? tab).Source is { } source) ProjectDocker.MarkDirty(source);
        // A switch, so that adding a third kind of tab cannot quietly re-bind
        // an else onto the wrong branch — which is exactly what adding the
        // second one did, and it dirtied every reference tab.
        switch (tab.Kind)
        {
            case DocumentTabKind.Reference:
                // Undo/redo replaces the wrapper doc's layer list; keep the
                // owning document's view pointed at whatever the editor holds.
                if (tab.View is { } view) view.Layers = Doc.Scene.Layers;
                // The edit belongs to the owning document. B95: refresh this
                // tab too, so the sheet an artist is looking at shows the badge
                // rather than making them go and find the parent.
                if (tab.Owner is { } owner) owner.RefreshDirty();
                tab.RefreshDirty();
                break;

            case DocumentTabKind.Symbol:
                // A symbol belongs to the project, so there is no owning
                // document to dirty — the project's own save writes it. What
                // has to happen here is the version bump, which is what makes
                // every placement of it redraw.
                SyncEditedSymbol();
                break;

            default:
                // B98. Not "this is now dirty" — "look again at whether it is".
                // The edit that got us here already moved the editor's revision
                // if it changed anything, and if it did not, nothing should
                // change here either.
                tab.RefreshDirty();
                break;
        }
        RebakeLiveSamples();
    }

    /// <summary>
    /// Re-freeze the all-layers-live strokes at the playhead, because something
    /// underneath them may have just moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes Live live. The alternative was handing a backdrop to
    /// every render path at render time; this hands it to the stroke once per
    /// edit instead, so there is one place that can be wrong rather than four,
    /// and canvas and export cannot disagree.
    /// </para>
    /// <para>
    /// Not an undo step, on purpose. The bake is derived from the layers below
    /// and the stroke that owns it, not something anybody authored, and an undo
    /// history with a "the background moved" entry between every real edit
    /// would be unusable. Undo re-enters here anyway — it goes through the same
    /// funnel — so the sample follows the document back.
    /// </para>
    /// <para>
    /// Only the playhead's frames, and that is exact rather than a shortcut: an
    /// edit happens at the playhead, so the strokes it can invalidate are the
    /// ones exposed there. The one case it does not cover is a held cel shown
    /// across a range whose backdrop differs along it — a frame carries one
    /// sample and can only answer for one index.
    /// </para>
    /// </remarks>
    private void RebakeLiveSamples()
    {
        var scene = Scene;
        // Nothing is rendered until a live stroke is actually found, so the
        // cost on an ordinary document is the exposure lookups plus a scan of
        // the strokes on the playhead's frames — no compose, no materialize.
        //
        // There was a document-wide "does anything sample?" guard in front of
        // this. It was removed on measurement grounds rather than taste: it
        // walked every cel of every layer and every stroke of every frame,
        // which on a long scene is more work than the loop it was protecting,
        // and it ran on every edit.
        var below = new List<(Layer Layer, Frame Frame)>();
        foreach (var layer in scene.Layers)
        {
            if (ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } exposed) continue;
            if (exposed is Frame painted && LiveStrokes(painted) is { Count: > 0 } live)
            {
                Rebake(live, below, scene.Width, scene.Height);
                InvalidateFrameRender(painted.Id);
                _dirtyThumbIds.Add(painted.Id);
            }
            if (scene.IsLayerVisible(layer)) below.Add((layer, exposed));
        }
    }

    private static List<Stroke> LiveStrokes(Frame frame) =>
        [.. frame.Strokes.Where(s => s.Brush.SampleSource == SampleSource.AllLayersLive)];

    /// <summary>Re-freeze one frame's live strokes against the stack beneath it.</summary>
    private void Rebake(List<Stroke> live, List<(Layer Layer, Frame Frame)> below, int width, int height)
    {
        if (below.Count == 0)
        {
            // Nothing underneath: there is no backdrop to follow, so the stroke
            // reverts to reading its own layer rather than keeping a stale one.
            foreach (var stroke in live) stroke.Baked = null;
            return;
        }

        var passes = below
            .Select(b => new RenderPass(
                _cache.Get(b.Frame, width, height, celIndex: CurrentFrameIndex),
                null, b.Layer.Opacity, SceneRenderer.ToSkia(b.Layer.BlendMode)))
            .ToList();
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var image = SceneRenderer.Compose(width, height, passes, SKColors.Transparent);
        using var beneath = SKBitmap.FromImage(image);
        foreach (var stroke in live) stroke.Baked = BrushEngine.BakeSample(stroke, beneath, info);
    }

    // ---- character sheets -----------------------------------------------------

    /// <summary>Sheets of the active (or owning) document — fresh list so the docker re-reads.</summary>
    public IReadOnlyList<ReferenceSheet> ReferenceSheetsView =>
        (SaveTargetTab?.Doc ?? Doc).ReferenceSheets.ToList();

    /// <remarks>
    /// <para>
    /// These two write straight to <c>Doc.ReferenceSheets</c> rather than going through
    /// <c>DocumentEditor</c>, which is why they announce the edit themselves. That was already
    /// true of undo — adding a sheet is not undoable — and B31 gave it a second consequence: the
    /// encoded reference views are cached, and a document edit that does not reach
    /// <see cref="MarkDocumentEdited"/> is a document edit the cache never hears about.
    /// </para>
    /// <para>
    /// Harmless today, since both only add an <em>empty</em> container under a fresh id and
    /// there is nothing stale to serve. It is the day somebody adds "duplicate view", cloning an
    /// existing view's layers, that this path would quietly serve the original's picture — so
    /// the call goes in now, while it costs one line, rather than in the commit that would need
    /// to know to add it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A character sheet was created on a document that has no file behind it.
    /// </summary>
    /// <remarks>
    /// <b>B66.</b> A sheet lives in <c>Doc.ReferenceSheets</c>, so it is saved
    /// when its document is saved and nowhere otherwise — and on an untitled
    /// document that is nowhere at all. Reported as "character sheets are not
    /// saved to disk", which is true and is a property of the document rather
    /// than of the sheet. Q25 answered (a): sheets stay part of a document, and
    /// the fix is to make sure the document has somewhere to live rather than to
    /// give the sheet a file of its own.
    /// </remarks>
    public event Action? ReferenceSheetNeedsAFile;

    /// <summary>
    /// Whether a sheet created now would have nothing on disk behind it.
    /// </summary>
    /// <remarks>
    /// Both halves matter and neither is enough alone. <c>FilePath</c> is null
    /// for a document that has never been saved; <c>Source</c> is null for one
    /// that is not in a project. A document in a project is saved by the
    /// project, so it needs no prompt even before its first write — which is the
    /// "in a project, a character sheet is directly added" half of the report.
    /// </remarks>
    public bool AReferenceSheetWouldBeUnsaved =>
        TargetTab is { FilePath: null, Source: null };

    /// <param name="name">
    /// What to call it. Null keeps the old numbered default, which is what the
    /// existing callers pass and what B65's rule says a *new* surface should
    /// stop doing — the prompt supplies a real name before anything is written.
    /// </param>
    /// <returns>
    /// The sheet, or null when nothing is open — a sheet belongs to a document
    /// and there is no document to put it in.
    /// </returns>
    public ReferenceSheet? AddReferenceSheet(string? name = null)
    {
        if (TargetTab is not { } target) return null;
        var needsAFile = AReferenceSheetWouldBeUnsaved;

        var sheet = new ReferenceSheet
        {
            Name = string.IsNullOrWhiteSpace(name)
                ? $"Character {target.Doc.ReferenceSheets.Count + 1}"
                : name.Trim(),
        };
        // B98. Through the editor rather than around it, so adding a sheet is
        // one undoable step and moves the revision — which is what raises the
        // badge now. Mutating the list directly left the badge to be asserted
        // by hand, and an add that could not be undone.
        target.Editor.Perform(doc => doc.ReferenceSheets.Add(sheet));
        MarkDocumentEdited();
        OnPropertyChanged(nameof(ReferenceSheetsView));

        // Announced after the sheet exists, not before: if the artist cancels the
        // save the work is still there to save later, which is the opposite of
        // creating nothing and telling them why.
        if (needsAFile) ReferenceSheetNeedsAFile?.Invoke();
        return sheet;
    }

    /// <inheritdoc cref="AddReferenceSheet"/>
    public void AddReferenceView(ReferenceSheet sheet)
    {
        if (TargetTab is not { } target) return;
        var view = ReferenceView.Create($"view {sheet.Views.Count + 1}", Scene.Width, Scene.Height);
        target.Editor.Perform(_ => sheet.Views.Add(view));
        MarkDocumentEdited();
        OnPropertyChanged(nameof(ReferenceSheetsView));
        OpenReferenceView(view);
    }

    /// <summary>A sheet or view really was renamed in the docker.</summary>
    /// <remarks>
    /// <b>B95.</b> This used to be called from a <c>LostFocus</c> handler and to
    /// mark the document dirty unconditionally, so clicking into a name box and
    /// out again — typing nothing — raised the badge. Every other rename handler
    /// in the window guards; this one did not. The caller now compares the text
    /// and only calls when it actually changed, and the mark goes through the
    /// editor so the rename is undoable like any other edit.
    /// </remarks>
    public void MarkReferenceRenamed()
    {
        if (SaveTargetTab is { } tab) tab.Editor.Perform(_ => { });
        MarkDocumentEdited();
        OnPropertyChanged(nameof(ReferenceSheetsView));
    }

    /// <summary>Redraw the sheet list without claiming anything changed.</summary>
    public void RefreshReferenceList() => OnPropertyChanged(nameof(ReferenceSheetsView));

    /// <summary>Open (or focus) the tab editing a character-sheet view.</summary>
    public void OpenReferenceView(ReferenceView view)
    {
        if (Tabs.FirstOrDefault(t => t.View == view) is { } open)
        {
            ActiveTab = open;
            return;
        }
        if (TargetTab is not { } owner) return;
        var sheet = owner.Doc.ReferenceSheets.FirstOrDefault(s => s.Views.Contains(view));
        // The wrapper scene SHARES the view's layer list: edits land in the
        // owning document directly.
        var wrapper = new Doc
        {
            Scene = new Scene
            {
                Name = view.Name,
                Width = view.Width,
                Height = view.Height,
                FrameCount = 1,
                Layers = view.Layers,
            },
        };
        var referenceTab = new DocumentTab(new DocumentEditor(wrapper), $"{sheet?.Name ?? "Sheet"} / {view.Name}")
        {
            Kind = DocumentTabKind.Reference,
            Owner = owner,
            View = view,
        };
        // B98. The owner has to know about the view, because a stroke drawn here
        // moves *this* editor's revision while landing in the owner's document —
        // without the registration the owner never notices it was changed.
        owner?.Views.Add(referenceTab);
        AddTab(referenceTab);
    }

    /// <summary>Flatten one character-sheet view to PNG (for AI reference and MCP).</summary>
    /// <summary>
    /// The longest edge of a reference image sent to a model.
    /// </summary>
    /// <remarks>
    /// <b>Capped on the way out, never on the view.</b> An artist's sheet stays whatever size
    /// they drew it; this is only what leaves the machine. Providers bill by area regardless
    /// of file size, so per `docs/DESIGN-ai-payload.md` a 768 px long edge is **442 image
    /// tokens against 691, and 244 KB against 333 KB** for a 960×540 view. Line art survives
    /// the downscale — it is the shape the model is being asked to read, not the pixels.
    /// </remarks>
    private const int ReferenceLongEdge = 768;

    /// <summary>
    /// Encoded reference views, keyed by view id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// B31. <c>Compose</c> + PNG + base64 cost <b>52 ms for one 960×540 view</b>, and it ran
    /// on the UI thread before every AI call — about 100 ms of stall for the default two
    /// views, producing byte-identical output each time because the sheet had not changed.
    /// <c>_cache.Get</c> already memoised the per-layer render; what was uncached was the
    /// expensive half.
    /// </para>
    /// <para>
    /// <b>Invalidated from <see cref="MarkDocumentEdited"/>, not <c>OnDocumentChanged</c>.</b>
    /// The bug entry proposed the latter and it would have been wrong in the dangerous
    /// direction: <c>OnDocumentChanged</c> takes an early return for scoped edits, and a
    /// stroke commit is exactly that — so a cache hung off it would survive the edit and hand
    /// the model a picture of art the artist had already changed. Wrong quietly, which is
    /// worse than slow. <c>MarkDocumentEdited</c> exists precisely because it catches what the
    /// other one misses, and its own comment says so.
    /// </para>
    /// <para>
    /// Cleared wholesale rather than per view, because over-invalidating costs one re-encode
    /// and under-invalidating costs correctness.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, string> _referenceViewPngs = new(StringComparer.Ordinal);

    /// <summary>Throw away encoded reference views — something in the document moved.</summary>
    private void InvalidateReferenceViewCache() => _referenceViewPngs.Clear();

    /// <summary>
    /// Run the edit funnel, for a test that changes the model directly.
    /// </summary>
    /// <remarks>
    /// Some edits an artist makes to a sheet — hiding one of its layers, for instance — are
    /// property sets on the record rather than commands on this view model, and they reach the
    /// funnel through the UI that made them. A test poking the record has no UI, so it needs a
    /// way to say "and that was an edit" without a second copy of what an edit means.
    /// </remarks>
    internal void MarkDocumentEditedForTests() => MarkDocumentEdited();

    /// <summary>One view as base64 PNG, at the size it was drawn.</summary>
    /// <remarks>
    /// <b>Uncapped, and that is the contract.</b> This overload is what the MCP surface answers
    /// <c>render_reference_view</c> with, and an agent asking for a picture of a view should get
    /// the view — the 768 px cap belongs to an AI *request*, where it exists because providers
    /// bill by area. Capping here instead shrank the MCP reply as a side effect of B31 and was
    /// caught by <c>RenderReferenceView_ProducesDecodablePng</c>, which had asserted the
    /// authored width since the feature landed. The cap is applied at
    /// <see cref="EncodedReferenceView"/>, one call site, where the reason for it is true.
    /// </remarks>
    public string RenderReferenceViewPng(ReferenceView view) => RenderReferenceViewPng(view, 0);

    /// <summary>One view as base64 PNG, no wider or taller than <paramref name="longEdge"/>.</summary>
    /// <remarks>
    /// <paramref name="longEdge"/> of 0 or less means the authored size. Explicit rather than
    /// defaulted, so a new caller has to say which of the two it wants.
    /// </remarks>
    public string RenderReferenceViewPng(ReferenceView view, int longEdge)
    {
        var passes = new List<RenderPass>();
        foreach (var layer in view.Layers)
        {
            if (!layer.Visible) continue;
            var frame = ExposureSheet.ExposedFrame(layer, 0);
            if (frame is null) continue;
            passes.Add(new RenderPass(_cache.Get(frame, view.Width, view.Height), null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
        }

        // Composed at the authored size so the warm per-layer cache entries are the ones every
        // other consumer already made, then scaled once. Scaling the composed surface rather
        // than the geometry is the same rule as invariant 7 — no stroke coordinate is touched,
        // and this is an outbound image rather than a document render.
        using var image = SceneRenderer.Compose(view.Width, view.Height, passes);
        using var sized = Downscaled(image, longEdge);
        using var data = (sized ?? image).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    /// <summary>The image no larger than <paramref name="longEdge"/>, or null if it already is.</summary>
    private static SkiaSharp.SKImage? Downscaled(SkiaSharp.SKImage image, int longEdge)
    {
        var longest = Math.Max(image.Width, image.Height);
        if (longEdge <= 0 || longest <= longEdge) return null;

        var scale = longEdge / (double)longest;
        var w = Math.Max(1, (int)Math.Round(image.Width * scale));
        var h = Math.Max(1, (int)Math.Round(image.Height * scale));

        var info = new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        using var surface = SkiaSharp.SKSurface.Create(info);
        if (surface is null) return null; // no surface, no downscale — send the full size
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        // Mipmapped linear: a plain bilinear minification of line art drops thin strokes
        // entirely, which is the one thing the model must not lose.
        surface.Canvas.DrawImage(
            image,
            new SkiaSharp.SKRect(0, 0, w, h),
            new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear));
        surface.Canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>Up to two rendered character-sheet views to ride along with AI requests.</summary>
    /// <remarks>
    /// Encoded once per view and reused until the document changes — see
    /// <see cref="_referenceViewPngs"/>. The first call after an edit pays; the ones after it
    /// do not, which is the whole of B31.
    /// </remarks>
    private IReadOnlyList<string>? CollectReferenceImages()
    {
        var views = (SaveTargetTab?.Doc ?? Doc).ReferenceSheets
            .SelectMany(s => s.Views)
            .Where(v => v.Layers.Any(l => l.Visible))
            .Take(2)
            .Select(EncodedReferenceView)
            .ToList();
        return views.Count > 0 ? views : null;
    }

    private string EncodedReferenceView(ReferenceView view)
    {
        if (_referenceViewPngs.TryGetValue(view.Id, out var cached)) return cached;
        // The one place the cap applies: this is a request, and a request is billed by area.
        var encoded = RenderReferenceViewPng(view, ReferenceLongEdge);
        _referenceViewPngs[view.Id] = encoded;
        return encoded;
    }

    /// <summary>The color docker's state, kept in sync with <see cref="ColorHex"/>.</summary>
    public ColorPickerViewModel ColorPicker { get; }

    /// <summary>The background half of the pair, with the same wheel and palette.</summary>
    public ColorPickerViewModel BackgroundPicker { get; }

    partial void OnColorHexChanged(string value)
    {
        OnPropertyChanged(nameof(ForegroundColorHex));
        ColorPicker.SetHex(value);
        if (_settingColorFromSwatch) return;

        // In swatch-edit mode the picker drives the selected swatch, which is
        // what makes dragging the wheel recolour the drawing live.
        if (PaletteDocker.ApplyPickerColor(value)) return;

        // Otherwise the artist has chosen a colour by some other means — the
        // wheel, the hex box, the eyedropper — and the link to the swatch has
        // to go. Keeping it would mean painting in one colour while recording
        // a reference to another, and the stroke would jump the moment anyone
        // touched the palette.
        ActiveSwatchId = null;
        ActivePaletteId = null;
    }

    /// <summary>
    /// Keep the background picker showing the background colour, however it
    /// changed — X swaps it, D resets it, and the wheel must not still be
    /// pointing at the colour it used to be.
    /// </summary>
    partial void OnBackgroundColorHexChanged(string value) => BackgroundPicker.SetHex(value);

    // ---- live palettes ------------------------------------------------------

    /// <summary>The palette docker's state.</summary>
    public PaletteDockerViewModel PaletteDocker { get; }

    /// <summary>
    /// The swatch the next stroke should reference, or null to record a
    /// literal colour. Set by selecting a swatch; cleared by choosing a colour
    /// any other way (see <see cref="OnColorHexChanged"/>).
    /// </summary>
    public string? ActiveSwatchId { get; private set; }

    /// <summary>Which palette <see cref="ActiveSwatchId"/> came from — Q30 step 2.</summary>
    public string? ActivePaletteId { get; private set; }

    private bool _settingColorFromSwatch;

    /// <summary>The swatch and the colour it held when the current edit run began.</summary>
    private (string Id, string Before)? _pendingSwatchEdit;

    private void PaintWithSwatch(string swatchId)
    {
        if (PaletteRegistry.ResolveSwatch(swatchId) is not { } swatch) return;
        _settingColorFromSwatch = true;
        try
        {
            ColorHex = swatch.Color;
        }
        finally
        {
            _settingColorFromSwatch = false;
        }
        ActiveSwatchId = swatchId;
        // Q30 step 2: which palette, not just which swatch. Two palettes
        // duplicated from one another share swatch ids, so a bare id stops
        // being an answer the moment palettes are scoped.
        ActivePaletteId = PaletteRegistry.PaletteOf(swatchId);
    }

    /// <summary>A structural palette edit — one undo step, then a full resync.</summary>
    private void PerformPaletteEdit(Action<Doc> mutate)
    {
        CommitSwatchEdit();
        _editor.Perform(mutate);
    }

    private void OnSwatchRecoloured(SwatchRow row, string before)
    {
        // A run is one swatch at a time; touching a different one closes the
        // previous run off so each lands as its own undo step.
        if (_pendingSwatchEdit is { } pending && pending.Id != row.Id) CommitSwatchEdit();
        _pendingSwatchEdit ??= (row.Id, before);

        if (row.Id == ActiveSwatchId) PaintWithSwatch(row.Id);
        RepaintForSwatch(row.Id);
        MarkDocumentEdited();
    }

    /// <summary>
    /// Close off a run of colour edits as a single undo step. Dragging the
    /// colour wheel fires an edit per pointer event; one step each would bury
    /// the drawing history under sixty entries of the same swatch.
    /// </summary>
    internal void CommitSwatchEdit()
    {
        // Owed before the early returns below, because the sweep the run skipped
        // is owed whether or not the colour ended up different from where it
        // started. A drag out and back to the same colour still invalidated
        // every thumbnail on the way past.
        var owedSweep = _swatchRepaint is { Repaints: > 1 };
        _swatchRepaint = null;
        if (owedSweep) RefreshThumbnails();

        if (_pendingSwatchEdit is not { } pending) return;
        _pendingSwatchEdit = null;
        if (PaletteRegistry.ResolveSwatch(pending.Id)?.Color is not { } after) return;
        if (after == pending.Before) return;

        var (id, before) = (pending.Id, pending.Before);
        // Looked up by id inside the closure rather than captured: a snapshot
        // undo replaces Doc wholesale, so the Swatch object this ran against
        // will not be the one a later redo has to write to.
        _editor.PerformDelta(d => SetSwatchColor(d, id, after), d => SetSwatchColor(d, id, before));
    }

    /// <summary>
    /// Set a swatch wherever it actually lives — this document, or the project.
    /// </summary>
    /// <remarks>
    /// <b>B103.</b> This walked <c>doc.Palettes</c> alone, which does not contain
    /// a <em>project</em> palette's swatches. Editing looked correct because the
    /// drag mutates the <c>Swatch</c> instance in place and the registry holds
    /// that instance; only undo revealed that the recorded step and the object
    /// had parted company, and undoing a project recolour appeared to do nothing.
    /// </remarks>
    private void SetSwatchColor(Doc doc, string swatchId, string color)
    {
        var found = false;
        foreach (var palette in doc.Palettes)
        {
            foreach (var swatch in palette.Swatches)
            {
                if (swatch.Id != swatchId) continue;
                swatch.Color = color;
                found = true;
            }
        }
        if (found || ProjectDocker.Project is not { } project) return;
        foreach (var palette in project.Palettes)
        {
            foreach (var swatch in palette.Swatches)
            {
                if (swatch.Id == swatchId) swatch.Color = color;
            }
        }
    }

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

    private void RepaintForSwatch(string swatchId)
    {
        var scope = _swatchRepaint is { } cached && cached.SwatchId == swatchId
            ? cached
            : (SwatchId: swatchId, Frames: FramesUsing(swatchId), Repaints: 0);

        scope.Repaints++;
        _swatchRepaint = scope;

        foreach (var frame in scope.Frames)
        {
            InvalidateFrameRender(frame.Id);
            _dirtyThumbIds.Add(frame.Id);
        }
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();

        // The first repaint of a run lands immediately, so a one-shot recolour —
        // a swatch typed into, a palette applied — is as synchronous as it ever
        // was. Everything after it is a pointer event in a drag, and a drag wants
        // a repaint per *displayed frame* rather than per event: publishing sixty
        // times to show sixty times is the shape invariant 6 forbids, and the
        // coalescing post is what the shape tool's own live drag already uses.
        if (scope.Repaints == 1)
        {
            PublishSnapshot();
            RefreshThumbnails();
        }
        else
        {
            // No thumbnail sweep mid-drag: nobody reads a thumbnail while the
            // wheel is moving, and the sweep walks every cell of every row. The
            // ids are still marked dirty, so the one CommitSwatchEdit runs at the
            // end repaints exactly what changed.
            RequestSnapshot();
        }
    }

    /// <summary>
    /// Every frame holding a stroke painted with this swatch, across every open
    /// document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B102. Every open document, not just the active one.</b> A shared
    /// palette is precisely what a <em>set</em> of documents paint from — that is
    /// the whole feature — so two of a character's animations being open at once
    /// is the ordinary case rather than the edge, and repainting only the focused
    /// tab leaves the other showing the old colour from cache.
    /// </para>
    /// <para>
    /// Still only the frames that hold a stroke referencing the swatch: walking
    /// the stroke record stays far cheaper than re-rendering frames whose pixels
    /// cannot have moved. What B151 changed is <em>how often</em> — this is a
    /// scan of every stroke in every open document, and it used to run on every
    /// pointer event of a wheel drag.
    /// </para>
    /// </remarks>
    /// <summary>
    /// How many times the swatch scan has run, for the budget test.
    /// </summary>
    /// <remarks>
    /// <b>A count rather than a stopwatch, because the property is countable.</b>
    /// B151 is not "this got faster", it is "this happens once per drag instead
    /// of once per pointer event" — and a test that asserts the second is exact,
    /// instant and cannot be flaky, where a timing test on the same behaviour
    /// would be all three of the opposite. The performance test beside it still
    /// measures the end-to-end cost, because a count cannot see a scan that got
    /// slower.
    /// </remarks>
    internal int SwatchScans { get; private set; }

    private List<Frame> FramesUsing(string swatchId)
    {
        SwatchScans++;
        var frames = new List<Frame>();
        var seen = new HashSet<string>();
        foreach (var layer in Tabs.SelectMany(t => t.Doc.Scene.Layers))
        {
            foreach (var cel in layer.Cels)
            {
                // A held cel names the same frame as the one it holds, so without
                // this a ten-frame hold invalidates the same render ten times.
                if (cel.Frame is not { } frame || !seen.Add(frame.Id)) continue;
                if (!StrokesOf(frame).Any(s => s.SwatchId == swatchId)) continue;
                frames.Add(frame);
            }
        }
        return frames;
    }

    // ---- projects -----------------------------------------------------------

    /// <summary>
    /// The project docker's state. Holds no project until one is created or
    /// opened — the app is document-first and shows no project UI until then.
    /// </summary>
    public ProjectViewModel ProjectDocker { get; }

    /// <summary>
    /// Which panels are open, where, and how big — the whole workspace.
    /// </summary>
    /// <remarks>
    /// Owned here rather than by the window so a layout survives the window
    /// being rebuilt, and so the tests can drive it without one.
    /// </remarks>
    public WorkspaceViewModel Workspace { get; } = new();

    /// <summary>Preferences that are not about pixels — see <see cref="AppSettings"/>.</summary>
    public AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// Composite layers on the GPU rather than the CPU (B125, experimental).
    /// </summary>
    /// <remarks>
    /// Goes through here rather than the settings object directly, so the render
    /// thread's mirror and the persisted value cannot drift apart — and so the
    /// canvas repaints immediately instead of on the next thing that happens to
    /// dirty it.
    /// </remarks>
    public bool GpuCompositing
    {
        get => Settings.GpuCompositing;
        set
        {
            if (Settings.GpuCompositing == value) return;
            Settings.GpuCompositing = value;
            Rendering.GpuComposite.SettingEnabled = value;
            Settings.Save();
            OnPropertyChanged();
            // The composite path changed under the canvas, so what is on screen
            // was produced by the other one. Republish rather than wait.
            InvalidateWholeCanvas();
            PublishSnapshot();
        }
    }


    /// <summary>Minutes between autosaves; 0 turns it off. Persists immediately.</summary>
    public double AutosaveMinutes
    {
        get => Settings.AutosaveMinutes;
        set
        {
            if (Math.Abs(Settings.AutosaveMinutes - value) < 1e-9) return;
            Settings.AutosaveMinutes = value;
            _autosave.Reschedule(Settings.AutosaveInterval);
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutosaveLabel));
        }
    }

    /// <summary>Also write over the document's own file, once it has one.</summary>
    /// <summary>
    /// Whether the start screen is offered when the application opens.
    /// </summary>
    /// <remarks>
    /// The screen has a "don't show this again" of its own, which is where it
    /// gets turned off. This is the way back — a setting you can only switch
    /// off from a screen you no longer see is a setting you cannot switch on.
    /// </remarks>
    public bool ShowStartScreen
    {
        get => Settings.ShowStartScreen;
        set
        {
            if (Settings.ShowStartScreen == value) return;
            Settings.ShowStartScreen = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether a console window opens at startup carrying the diagnostic traces.
    /// </summary>
    /// <remarks>
    /// Takes effect on the next start rather than immediately, and the menu
    /// says so. Opening one mid-session is possible but would produce a window
    /// that had missed everything up to that point — which is the opposite of
    /// what somebody turning this on wants.
    /// </remarks>
    public bool ShowDiagnosticsConsole
    {
        get => Settings.ShowDiagnosticsConsole;
        set
        {
            if (Settings.ShowDiagnosticsConsole == value) return;
            Settings.ShowDiagnosticsConsole = value;
            Settings.Save();
            OnPropertyChanged();
            AiStatus = value
                ? "The diagnostics console will open the next time Lightbox starts."
                : "The diagnostics console will not open next time.";
        }
    }

    /// <summary>Where the crash reports and the survivable-failure log live.</summary>
    public string DiagnosticsFolder => Services.DiagnosticLog.Directory;

    /// <summary>The exact build, for a bug report to name.</summary>
    public string BuildLabel => $"Lightbox {Services.DiagnosticLog.Build}";

    public bool AutosaveInPlace
    {
        get => Settings.AutosaveInPlace;
        set
        {
            if (Settings.AutosaveInPlace == value) return;
            Settings.AutosaveInPlace = value;
            _autosave.InPlace = value;
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutosaveLabel));
        }
    }

    public string AutosaveLabel =>
        Settings.AutosaveInterval is null
            ? "Autosave off"
            : $"Autosave every {Settings.AutosaveMinutes:0.##} min{(Settings.AutosaveInPlace ? ", in place" : "")}";

    /// <summary>Whether any project UI should exist at all.</summary>
    public bool HasProject => ProjectDocker.HasProject;

    /// <summary>
    /// Change what the open project is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a migration: the type is a statement about intent that tooling and
    /// export read, so converting is exactly a change of that statement. No
    /// document is read, rewritten or recreated, and nothing already authored
    /// is dropped — a camera keyframed under Animation is still there under
    /// Game art, ignored rather than erased.
    /// </para>
    /// <para>
    /// The workspace is left alone. Which panels somebody wants is a
    /// preference and converting a project is a decision about the project;
    /// rearranging the screen as a side effect of a menu item is how a tool
    /// loses trust. <see cref="TakeProjectTypeWorkspace"/> is the separate,
    /// asked-for move.
    /// </para>
    /// </remarks>
    public ProjectIo.ConversionReport? ConvertProject(ProjectType? to)
    {
        if (ProjectDocker.Project is not { } project) return null;
        var report = ProjectIo.Convert(project, to);
        SaveProject();
        OnProjectChanged();
        OnPropertyChanged(nameof(ProjectTypeLabel));
        AiStatus = string.Join("  ", report.Notes);
        return report;
    }

    /// <summary>Switch to the current project type's default panels, when asked.</summary>
    public void TakeProjectTypeWorkspace()
    {
        if (ProjectDocker.Project?.Manifest.Type is { } type) Workspace.UseDefaultFor(type);
    }

    /// <summary>What the project is for, for a menu header.</summary>
    public string ProjectTypeLabel => ProjectDocker.Project?.Manifest.Type is { } type
        ? $"Project type — {type}"
        : "Project type — unset";

    private void OnProjectChanged()
    {
        OnPropertyChanged(nameof(HasProject));
        RegisterResources();
        MarkDocumentEdited();
    }

    /// <summary>A blank document for a new animation, matching the active scene's shape.</summary>
    private Doc NewAnimationDoc()
    {
        var scene = Scene;
        return DocumentFactory.CreateDoc(
            scene.Width, scene.Height, scene.Fps,
            scene.TransparentBackground ? null : scene.BackgroundColor);
    }

    /// <summary>Open a project animation as a tab, or focus the tab it is already in.</summary>
    private void OpenProjectDocument(DocumentRef reference, Doc doc)
    {
        if (Tabs.FirstOrDefault(t => t.Source?.Id == reference.Id) is { } already)
        {
            ActiveTab = already;
            return;
        }
        var opened = new DocumentTab(new DocumentEditor(doc), reference.Name) { Source = reference };
        opened.State.LayerIndex = FirstPaintableLayer(doc);
        AddTab(opened);
    }

    /// <summary>
    /// Point the engine's registries at the project's shared resources AND the
    /// active document's.
    ///
    /// This is the whole of Pillar 1's sharing, and it needs no engine change:
    /// the brush engine already resolves swatches, gradients, tips and clips by
    /// id at render time. Widening the scope is all it takes for two animations
    /// under one character to paint from one palette.
    /// </summary>
    /// <summary>
    /// Re-scope the registries after the project's shared resources changed
    /// outside a document edit — importing a palette, or a test adding one.
    /// </summary>
    public void RefreshProjectResources() => RegisterResources();

    /// <summary>Whether a frame's render is still cached — B102's test probe.</summary>
    internal bool IsFrameCached(string frameId) => _cache.Holds(frameId);

    /// <summary>Paint from a palette swatch, as picking one in the panel does.</summary>
    internal void PickSwatchForTest(string swatchId) => PaintWithSwatch(swatchId);

    private void RegisterResources()
    {
        // Imported textures come in with everything else the document carries,
        // so a file opened on a machine that has never seen one still paints
        // the paper it was drawn on.
        if (Doc.Textures is { Count: > 0 } textures) TextureRegistry.Register(textures);

        var palettes = Doc.Palettes.AsEnumerable();
        var gradients = new Dictionary<string, Gradient>(Doc.Gradients);
        if (ProjectDocker.Project is { } project)
        {
            // Document first, project second, so a document's own copy of a
            // swatch id loses to the project's — the shared one is the live one.
            //
            // Q30 step 2: only the project palettes this document can actually
            // see. Until now every palette went in for every document, which
            // reads as working until a project has two characters and the
            // goblin's reds turn up in the knight's picker. A project that
            // declares no scopes still gets everything — that is the
            // new-projects-only migration, at the one place a reader can tell
            // the two shapes apart.
            var visible = PaletteScopes.VisibleTo(
                project.Manifest, (SaveTargetTab ?? ActiveTab)?.Source);
            palettes = palettes.Concat(
                visible is null
                    ? project.Palettes
                    : project.Palettes.Where(p => visible.Contains(p.Id)));
            // Q30 step 4: the same scoping palettes got, for the same reason —
            // a gradient made for the knight's shield has no business in the
            // goblin's picker. Null still means the project scopes none.
            var visibleGradients = GradientScopes.VisibleTo(
                project.Manifest, (SaveTargetTab ?? ActiveTab)?.Source);
            foreach (var (id, gradient) in project.Gradients)
            {
                if (visibleGradients is null || visibleGradients.Contains(id)) gradients[id] = gradient;
            }
        }
        var resolved = palettes.ToList();
        PaletteRegistry.Reset(resolved, gradients);
        // Symbols are project-scoped while a project is open, which is the
        // point of them: the sword lives above the animations that hold it. A
        // document carries its own only when it arrived flattened from
        // somewhere else, and then the project's copy of an id wins — the same
        // precedence the palettes use, for the same reason.
        var symbols = new Dictionary<string, Lightbox.Core.Documents.Symbol>();
        foreach (var (id, symbol) in Doc.Symbols ?? []) symbols[id] = symbol;
        if (ProjectDocker.Project is { } withSymbols)
        {
            foreach (var (id, symbol) in withSymbols.Symbols) symbols[id] = symbol;
        }
        SymbolRegistry.Reset(symbols);
        // Every colour picker in the app — the panel's and every flyout's —
        // offers the same swatches, because they are all looking at the same
        // document.
        _paletteSwatches = resolved.SelectMany(p => p.Swatches).ToList();
        ColorPickerViewModel.PaletteSource = () => _paletteSwatches;
        // The way back. Every picker in the app can put its colour in the
        // palette, and they all mean the same palette — the one the docker has
        // selected — because there is one document.
        ColorPickerViewModel.PaletteSink = request =>
        {
            var outcome = PaletteDocker.AddColor(request);
            // The docker's own status line is easy to miss when the wheel is
            // open over the canvas, so a refusal says so in the status bar too.
            if (outcome.Message is { Length: > 0 } message) AiStatus = message;
            return outcome;
        };
        ColorPickerViewModel.PaletteTargetSource = () => PaletteDocker.PaletteTargets;
        ColorPicker.RefreshPalette();
        // The source is static, so both halves of the pair see the new list —
        // but each has to be told to look again, or the background picker keeps
        // showing the previous document's swatches.
        BackgroundPicker.RefreshPalette();

        if (Scene.References is { Count: > 0 } strips)
        {
            Lightbox.Raster.ReferenceStripRegistry.Register(
                strips.Select(s => (s.Id, s.Png)));
            // Video references carry no PNG — their pixels come back off the
            // footage itself (Q56).
            foreach (var strip in strips.Where(s => s.VideoPath is not null || s.VideoData is not null))
            {
                RegisterVideoReference(strip);
            }
        }
    }

    private IReadOnlyList<Swatch> _paletteSwatches = [];

    // ---- gradients ----------------------------------------------------------

    /// <summary>The gradient docker's state.</summary>
    public GradientDockerViewModel GradientDocker { get; }

    /// <summary>Opacity the gradient tool lays its ramp down at, 0–1.</summary>
    [ObservableProperty]
    private double _gradientOpacity = 1.0;

    private void PerformGradientEdit(Action<Doc> mutate)
    {
        CommitSwatchEdit();
        _editor.Perform(mutate);
    }

    /// <summary>
    /// A gradient definition changed. Same scoping as a swatch edit: only
    /// frames holding a stroke that paints this gradient are dropped.
    /// </summary>
    private void OnGradientEdited(string gradientId)
    {
        // The registry holds the same Gradient object the docker just edited,
        // so nothing needs re-registering — only the cached pixels are stale.
        foreach (var layer in Scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is not { } frame) continue;
                if (!StrokesOf(frame).Any(s => s.GradientId == gradientId)) continue;
                InvalidateFrameRender(frame.Id);
                _dirtyThumbIds.Add(frame.Id);
            }
        }
        // A gradient being dragged right now redefines its own preview.
        if (_liveGradient is not null) RenderGradientPreview();
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        MarkDocumentEdited();
        PublishSnapshot();
        RefreshThumbnails();
    }

    private readonly AutosaveService _autosave;

    public Doc Doc => _editor.Doc;

    private Scene Scene => _editor.Doc.Scene;

    private Layer ActiveLayer => Scene.Layers[Math.Clamp(ActiveLayerIndex, 0, Scene.Layers.Count - 1)];

    // ---- observable state ---------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrameLabel))]
    private int _currentFrameIndex;

    // ---- brush tool state -----------------------------------------------------
    // Two working configurations (brush + eraser); the bound properties edit
    // whichever tool is active. Everything persists to brushes.json so B
    // always returns to the brush exactly as last configured.

    /// <summary>Test seam: redirect brush persistence away from the real settings dir.</summary>
    internal static string? BrushStorePath { get; set; }

    private BrushSettings _brushWork = new();
    private BrushSettings _eraserWork = new() { Size = 14, Hardness = 0.9 };
    private readonly List<BrushPreset> _userPresets = [];
    private bool _applyingPreset;

    private BrushSettings CurrentToolSettings => IsEraser ? _eraserWork : _brushWork;

    /// <summary>
    /// The settings the tool bar is editing. A test seam: the curve properties
    /// are read back through <see cref="PressureResponse"/>, and asserting on
    /// the record is how a test says "the brush now says this" rather than
    /// "the view model agrees with itself".
    /// </summary>
    internal BrushSettings CurrentToolSettingsForTest => CurrentToolSettings;

    public ObservableCollection<BrushPreset> BrushPresetChoices { get; } = [];

    [ObservableProperty]
    private BrushPreset? _selectedBrushPreset;

    /// <summary>
    /// Put a preset on, whether or not it is already the one selected.
    /// </summary>
    /// <remarks>
    /// Assigning <see cref="SelectedBrushPreset"/> the value it already holds
    /// raises nothing, so picking the brush you are already on used to do
    /// nothing — and after the modified indicator existed, that became the
    /// obvious gesture for "give me this brush back". This is the path the
    /// picker uses.
    /// </remarks>
    public void ApplyPreset(BrushPreset preset)
    {
        if (SelectedBrushPreset?.Id == preset.Id)
        {
            OnSelectedBrushPresetChanged(preset);
            return;
        }
        SelectedBrushPreset = preset;
    }

    partial void OnSelectedBrushPresetChanged(BrushPreset? value)
    {
        if (value is null || _applyingPreset) return;
        _applyingPreset = true;
        IsEraser = value.Tool == ToolKind.Eraser;
        // Both of these are settings in Configure rather than parts of a
        // brush, so a preset never overrides them. Sample source was missing
        // from this list and picking any preset silently reset it to
        // "this layer" — which made a window that says the choice applies to
        // the next mark tell the truth only until you changed brush.
        var antiAlias = AntiAliasing;
        var sampleSource = SmudgeSampleSource;
        _brushWork = value.Settings.Clone();
        _brushWork.AntiAlias = antiAlias;
        _brushWork.SampleSource = sampleSource;
        _eraserWork.SampleSource = sampleSource;
        EnsurePresetTip(value);
        NotifyBrushProperties();
        _applyingPreset = false;
        PersistBrushState();
    }

    /// <summary>
    /// Global anti-aliasing for everything that paints (brush, eraser, fill).
    /// The value is stamped into each stroke at paint time, so existing art
    /// re-renders bit-identically no matter how the toggle changes later.
    /// </summary>
    public bool AntiAliasing
    {
        get => _brushWork.AntiAlias;
        set
        {
            if (_brushWork.AntiAlias == value) return;
            _brushWork.AntiAlias = value;
            _eraserWork.AntiAlias = value;
            OnPropertyChanged();
            if (!_applyingPreset) PersistBrushState();
        }
    }

    public IReadOnlyList<SampleSource> SampleSourceChoices { get; } = Enum.GetValues<SampleSource>();

    /// <summary>
    /// What a smudge or blur reads: its own layer, or everything under it.
    /// </summary>
    /// <remarks>
    /// Set here and stamped into each stroke at paint time, exactly like
    /// <see cref="AntiAliasing"/> — so changing it never alters a mark already
    /// made. That is invariant 4, and it is the reason a preference like this
    /// can live in a settings window at all: the window sets what the next
    /// stroke will be, not what every past stroke becomes.
    /// </remarks>
    public SampleSource SmudgeSampleSource
    {
        get => _brushWork.SampleSource;
        set
        {
            if (_brushWork.SampleSource == value) return;
            _brushWork.SampleSource = value;
            _eraserWork.SampleSource = value;
            OnPropertyChanged();
            if (!_applyingPreset) PersistBrushState();
        }
    }

    /// <summary>A preset's custom tip must live in the document so it re-renders standalone.</summary>
    private void EnsurePresetTip(BrushPreset preset)
    {
        if (preset.TipPng is null || preset.Settings.TipId is null) return;
        var doc = (SaveTargetTab?.Doc ?? Doc);
        if (doc.BrushTips.TryAdd(preset.Settings.TipId, preset.TipPng))
        {
            BrushTipRegistry.Register(doc.BrushTips);
            MarkDocumentEdited();
        }
    }

    private double GetBrush(Func<BrushSettings, double> get) => get(CurrentToolSettings);

    /// <summary>For settings that are not doubles — enums, flags, counts.</summary>
    private T GetBrushValue<T>(Func<BrushSettings, T> get) => get(CurrentToolSettings);

    private void SetBrush(Action<BrushSettings> set, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        set(CurrentToolSettings);
        OnPropertyChanged(name);
        // The on-canvas ring is computed from the settings, not stored, so it
        // has to be told. Size is the obvious one, but the minimum diameter
        // and the pressure-size curve move it too, which is why this is
        // unconditional rather than a special case on BrushSize.
        OnPropertyChanged(nameof(BrushCursorDiameter));
        // Any change to the working brush can be the one that makes it differ
        // from the preset it came from, so the dot is recomputed here rather
        // than at a handful of places somebody has to remember.
        NotifyPresetProperties();
        if (!_applyingPreset) PersistBrushState();
    }

    public double BrushSize
    {
        get => GetBrush(s => s.Size);
        set => SetBrush(s => s.Size = Math.Clamp(value, 1, 500));
    }

    public double BrushHardness
    {
        get => GetBrush(s => s.Hardness);
        set => SetBrush(s => s.Hardness = Math.Clamp(value, 0, 1));
    }

    public double BrushOpacity
    {
        get => GetBrush(s => s.Opacity);
        set => SetBrush(s => s.Opacity = Math.Clamp(value, 0.01, 1));
    }

    public double BrushFlow
    {
        get => GetBrush(s => s.Flow);
        set => SetBrush(s => s.Flow = Math.Clamp(value, 0.01, 1));
    }

    public double BrushSpacing
    {
        get => GetBrush(s => s.Spacing);
        set => SetBrush(s => s.Spacing = Math.Clamp(value, 0.02, 2));
    }

    public double BrushWetEdge
    {
        get => GetBrush(s => s.WetEdge);
        set => SetBrush(s => s.WetEdge = Math.Clamp(value, 0, 1));
    }

    public double BrushGranulation
    {
        get => GetBrush(s => s.Granulation);
        set => SetBrush(s => s.Granulation = Math.Clamp(value, 0, 1));
    }

    public double BrushScatter
    {
        get => GetBrush(s => s.Scatter);
        set => SetBrush(s => s.Scatter = Math.Clamp(value, 0, 1));
    }

    // ---- brush dynamics (Photoshop's grouping) ---------------------------------

    public double BrushSizeJitter
    {
        get => GetBrush(s => s.SizeJitter);
        set => SetBrush(s => s.SizeJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushMinimumDiameter
    {
        get => GetBrush(s => s.MinimumDiameter);
        set => SetBrush(s => s.MinimumDiameter = Math.Clamp(value, 0, 1));
    }

    public double BrushRoundness
    {
        get => GetBrush(s => s.Roundness);
        set => SetBrush(s => s.Roundness = Math.Clamp(value, 0.05, 1));
    }

    public double BrushRoundnessJitter
    {
        get => GetBrush(s => s.RoundnessJitter);
        set => SetBrush(s => s.RoundnessJitter = Math.Clamp(value, 0, 1));
    }

    public bool BrushAngleFollowsDirection
    {
        get => GetBrushValue(s => s.AngleFollowsDirection);
        set => SetBrush(s => s.AngleFollowsDirection = value);
    }

    public double BrushFlowJitter
    {
        get => GetBrush(s => s.FlowJitter);
        set => SetBrush(s => s.FlowJitter = Math.Clamp(value, 0, 1));
    }

    public IReadOnlyList<string> TextureSurfaceChoices { get; } =
        ["None", .. Enum.GetNames<PaperKind>()];

    public string BrushTextureSurface
    {
        get => GetBrushValue(s => s.TextureSurface?.ToString() ?? "None");
        set => SetBrush(s => s.TextureSurface =
            Enum.TryParse<PaperKind>(value, out var kind) ? kind : null);
    }

    public double BrushTextureScale
    {
        get => GetBrush(s => s.TextureScale);
        set => SetBrush(s => s.TextureScale = Math.Clamp(value, 1, 128));
    }

    public double BrushTextureDepth
    {
        get => GetBrush(s => s.TextureDepth);
        set => SetBrush(s => s.TextureDepth = Math.Clamp(value, 0, 1));
    }

    /// <summary>Empty means colour dynamics drift only in hue/saturation/value.</summary>
    public string BrushSecondaryColor
    {
        get => GetBrushValue(s => s.SecondaryColor ?? "");
        set => SetBrush(s => s.SecondaryColor = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public double BrushColorJitter
    {
        get => GetBrush(s => s.ColorJitter);
        set => SetBrush(s => s.ColorJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushHueJitter
    {
        get => GetBrush(s => s.HueJitter);
        set => SetBrush(s => s.HueJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushSaturationJitter
    {
        get => GetBrush(s => s.SaturationJitter);
        set => SetBrush(s => s.SaturationJitter = Math.Clamp(value, 0, 1));
    }

    public double BrushBrightnessJitter
    {
        get => GetBrush(s => s.BrightnessJitter);
        set => SetBrush(s => s.BrightnessJitter = Math.Clamp(value, 0, 1));
    }

    // ---- smudge ----------------------------------------------------------------

    public IReadOnlyList<SmudgeMode> SmudgeModeChoices { get; } = Enum.GetValues<SmudgeMode>();

    /// <summary>Only meaningful for a smudge brush; the page hides otherwise.</summary>
    public bool IsSmudgeBrush => GetBrushValue(s => s.Kind) == BrushKind.Smudge;

    /// <summary>
    /// A brush that reworks pixels already down rather than adding new ones.
    /// </summary>
    /// <remarks>
    /// The tool options bar swaps to this group's controls when one is
    /// selected, because the generic brush row is mostly wrong for them: a
    /// smudge has no opacity in the usual sense, and what you actually reach
    /// for is strength and how wide an area each dab samples.
    /// </remarks>
    public bool IsEffectBrush =>
        GetBrushValue(s => s.Kind) is BrushKind.Smudge or BrushKind.Blur;

    /// <summary>
    /// Whether the effect row belongs in the tool options bar right now.
    /// </summary>
    /// <remarks>
    /// The brush kind and the active tool are different questions, and the bar
    /// was only asking the first. Pick the smudge brush, then switch to the
    /// selection tool, and Strength and Radius were still sitting there —
    /// controls for a brush you are not currently holding, taking room from
    /// the ones you are.
    /// </remarks>
    public bool ShowsEffectOptions => IsBrushTool && IsEffectBrush;

    /// <summary>
    /// How hard the effect bites: <see cref="BrushSettings.Flow"/>, for every
    /// effect brush.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B90.</b> This used to read and write <see cref="BrushSmudgeLength"/>
    /// for a smudge and label itself "Length", which put a *drag distance* under
    /// the artist's strength affordance and left the knob that actually decides
    /// how hard each dab pulls — flow, the <c>strength</c> argument
    /// <c>BrushEngine.StampSmudge</c> hands to <c>LerpDab</c> — unreachable from
    /// the bar entirely. `docs/manual/04-brushes.md` already described the
    /// behaviour this now has, so the app was the half that was wrong.
    /// </para>
    /// <para>
    /// Smudge length is not lost and is not diminished: it keeps its own labelled
    /// row on the ⚙ → Effects page beside smearing/dulling and radius, which is
    /// where a value you set once per brush belongs. The bar carries the three
    /// you reach for mid-stroke.
    /// </para>
    /// </remarks>
    public double EffectStrength
    {
        get => BrushFlow;
        set
        {
            BrushFlow = value;
            OnPropertyChanged();
        }
    }

    public SmudgeMode BrushSmudgeMode
    {
        get => GetBrushValue(s => s.SmudgeMode);
        set => SetBrush(s => s.SmudgeMode = value);
    }

    public double BrushSmudgeLength
    {
        get => GetBrush(s => s.SmudgeLength);
        set => SetBrush(s => s.SmudgeLength = Math.Clamp(value, 0, 1));
    }

    public double BrushSmudgeRadius
    {
        get => GetBrush(s => s.SmudgeRadius);
        set => SetBrush(s => s.SmudgeRadius = Math.Clamp(value, 0.05, 1));
    }

    public double BrushColorRate
    {
        get => GetBrush(s => s.ColorRate);
        set => SetBrush(s => s.ColorRate = Math.Clamp(value, 0, 1));
    }

    // ---- medium ---------------------------------------------------------------
    //
    // The physical simulation. Picking a medium decides which parts of the
    // model run; these numbers tune it. All of them live on the stroke, so a
    // change here never reaches back into paint already down.

    public IReadOnlyList<MediumKind> MediumKindChoices { get; } = Enum.GetValues<MediumKind>();

    public IReadOnlyList<PaperKind> PaperKindChoices { get; } = Enum.GetValues<PaperKind>();

    public MediumKind BrushMedium
    {
        get => GetBrushValue(s => s.Medium.Kind);
        set
        {
            SetBrush(s => s.Medium.Kind = value);
            OnPropertyChanged(nameof(MediumIsSimulated));
            OnPropertyChanged(nameof(MediumHasBody));
        }
    }

    /// <summary>Whether the fluid controls apply at all — everything is inert under None.</summary>
    public bool MediumIsSimulated => BrushMedium != MediumKind.None;

    /// <summary>Body, relief and bristle drag only mean something for paint that has thickness.</summary>
    public bool MediumHasBody => BrushMedium is MediumKind.Gouache or MediumKind.Oil;

    public double MediumWetness
    {
        get => GetBrush(s => s.Medium.Wetness);
        set => SetBrush(s => s.Medium.Wetness = Math.Clamp(value, 0, 1));
    }

    public double MediumViscosity
    {
        get => GetBrush(s => s.Medium.Viscosity);
        set => SetBrush(s => s.Medium.Viscosity = Math.Clamp(value, 0, 1));
    }

    public double MediumDrag
    {
        get => GetBrush(s => s.Medium.Drag);
        set => SetBrush(s => s.Medium.Drag = Math.Clamp(value, 0, 1));
    }

    /// <summary>
    /// Capped at 32. Cost is linear in this, and it is the one control here
    /// that can make a stroke commit feel slow, so the ceiling is deliberate.
    /// </summary>
    public int MediumFlowSteps
    {
        get => GetBrushValue(s => s.Medium.FlowSteps);
        set => SetBrush(s => s.Medium.FlowSteps = Math.Clamp(value, 0, 32));
    }

    public double MediumAbsorbency
    {
        get => GetBrush(s => s.Medium.Absorbency);
        set => SetBrush(s => s.Medium.Absorbency = Math.Clamp(value, 0, 1));
    }

    public double MediumEdgePull
    {
        get => GetBrush(s => s.Medium.EdgePull);
        set => SetBrush(s => s.Medium.EdgePull = Math.Clamp(value, 0, 1));
    }

    public double MediumPigmentDensity
    {
        get => GetBrush(s => s.Medium.PigmentDensity);
        set => SetBrush(s => s.Medium.PigmentDensity = Math.Clamp(value, 0, 1));
    }

    public double MediumGranularity
    {
        get => GetBrush(s => s.Medium.Granularity);
        set => SetBrush(s => s.Medium.Granularity = Math.Clamp(value, 0, 1));
    }

    public double MediumHiding
    {
        get => GetBrush(s => s.Medium.Hiding);
        set => SetBrush(s => s.Medium.Hiding = Math.Clamp(value, 0, 1));
    }

    public bool MediumPhysicalMixing
    {
        get => GetBrushValue(s => s.Medium.PhysicalMixing);
        set => SetBrush(s => s.Medium.PhysicalMixing = value);
    }

    public PaperKind MediumPaper
    {
        get => GetBrushValue(s => s.Medium.Paper);
        set => SetBrush(s => s.Medium.Paper = value);
    }

    public double MediumPaperScale
    {
        get => GetBrush(s => s.Medium.PaperScale);
        set => SetBrush(s => s.Medium.PaperScale = Math.Clamp(value, 1, 128));
    }

    public double MediumPaperInfluence
    {
        get => GetBrush(s => s.Medium.PaperInfluence);
        set => SetBrush(s => s.Medium.PaperInfluence = Math.Clamp(value, 0, 1));
    }

    public double MediumBody
    {
        get => GetBrush(s => s.Medium.Body);
        set => SetBrush(s => s.Medium.Body = Math.Clamp(value, 0, 1));
    }

    public double MediumRelief
    {
        get => GetBrush(s => s.Medium.Relief);
        set => SetBrush(s => s.Medium.Relief = Math.Clamp(value, 0, 1));
    }

    // No MediumBristleDrag or MediumPickup, and their absence is the fix rather than an
    // oversight. Both settings exist on the record and the engine reads neither — they
    // are the directional advection loop that DESIGN-fluid-media.md sequences last —
    // so exposing them gave an artist two sliders that moved and changed nothing.
    // Charter O7, and B23. `MediumSettings` says the same thing at the declarations.

    public double MediumPaintLoad
    {
        get => GetBrush(s => s.Medium.PaintLoad);
        set => SetBrush(s => s.Medium.PaintLoad = Math.Clamp(value, 0, 1));
    }

    public double MediumPressureWater
    {
        get => GetBrush(s => s.Medium.PressureWater);
        set => SetBrush(s => s.Medium.PressureWater = Math.Clamp(value, 0, 1));
    }

    public double MediumPressureMix
    {
        get => GetBrush(s => s.Medium.PressureMix);
        set => SetBrush(s => s.Medium.PressureMix = Math.Clamp(value, 0, 1));
    }

    public double MediumRewetting
    {
        get => GetBrush(s => s.Medium.Rewetting);
        set => SetBrush(s => s.Medium.Rewetting = Math.Clamp(value, 0, 1));
    }

    public double BrushRotationJitter
    {
        get => GetBrush(s => s.RotationJitter);
        set => SetBrush(s => s.RotationJitter = Math.Clamp(value, 0, 1));
    }

    // ---- pen pressure (per brush, per setting — Krita-style) --------------------

    /// <summary>Master pen-pressure switch of the current brush.</summary>
    public bool BrushPressureEnabled
    {
        get => GetBrush(s => s.PressureEnabled ? 1.0 : 0.0) > 0;
        set => SetBrush(s => s.PressureEnabled = value);
    }

    public double BrushPressureSizeGamma
    {
        get => GetBrush(s => s.PressureSizeGamma);
        set => SetBrush(s => s.PressureSizeGamma = Math.Clamp(value, 0, 4));
    }

    public double BrushPressureFlowGamma
    {
        get => GetBrush(s => s.PressureFlowGamma);
        set => SetBrush(s => s.PressureFlowGamma = Math.Clamp(value, 0, 4));
    }

    public double BrushPressureHardnessGamma
    {
        get => GetBrush(s => s.PressureHardnessGamma);
        set => SetBrush(s => s.PressureHardnessGamma = Math.Clamp(value, 0, 4));
    }

    // Per-setting on/off view of the curves (checkbox semantics: off = curve 0).

    public bool BrushPressureAffectsSize
    {
        get => BrushPressureSizeGamma > 0;
        set
        {
            BrushPressureSizeGamma = value ? 1 : 0;
            NotifyBrushProperties();
        }
    }

    public bool BrushPressureAffectsFlow
    {
        get => BrushPressureFlowGamma > 0;
        set
        {
            BrushPressureFlowGamma = value ? 1 : 0;
            NotifyBrushProperties();
        }
    }

    public bool BrushPressureAffectsHardness
    {
        get => BrushPressureHardnessGamma > 0;
        set
        {
            BrushPressureHardnessGamma = value ? 1 : 0;
            NotifyBrushProperties();
        }
    }

    // ---- pressure curves -------------------------------------------------------
    //
    // The editor edits a shape; the record keeps either a curve or a gamma. So
    // these three do the whole of the translation between the two, and nothing
    // else in the app has to know that a brush can be written either way.

    /// <summary>
    /// The shape to draw for a dynamic: the artist's curve, or the one its
    /// gamma describes. Never null, so the editor always has something to show
    /// — and for a brush made before curves existed, what it shows is that
    /// brush's real response rather than a straight line that would flatten it
    /// the moment anybody touched it.
    /// </summary>
    public ResponseCurve BrushCurve(BrushDynamic target) =>
        PressureResponse.Shape(CurrentToolSettings, target);

    /// <summary>Whether anything at all drives a dynamic — what the checkbox reads.</summary>
    public bool BrushDrives(BrushDynamic target) =>
        PressureResponse.IsDriven(CurrentToolSettings, target);

    /// <summary>
    /// Store an artist-drawn curve for a dynamic, replacing whatever drove it.
    /// </summary>
    public void SetBrushCurve(BrushDynamic target, ResponseCurve curve)
    {
        SetBrush(s =>
        {
            s.Curves ??= [];
            s.Curves[target] = curve.Clone();
        }, nameof(BrushCurve));
        NotifyBrushProperties();
    }

    /// <summary>
    /// Turn a dynamic on or off. On means linear unless something already
    /// drives it; off clears the curve and the gamma together, because leaving
    /// one behind would make the checkbox lie about what the brush does.
    /// </summary>
    public void SetBrushDrives(BrushDynamic target, bool on)
    {
        if (on && BrushDrives(target)) return;

        SetBrush(s =>
        {
            s.Curves?.Remove(target);
            switch (target)
            {
                case BrushDynamic.Size: s.PressureSizeGamma = on ? 1 : 0; break;
                case BrushDynamic.Flow: s.PressureFlowGamma = on ? 1 : 0; break;
                case BrushDynamic.Hardness: s.PressureHardnessGamma = on ? 1 : 0; break;
                default:
                    if (on) (s.Curves ??= [])[target] = ResponseCurve.Linear();
                    break;
            }
            if (s.Curves is { Count: 0 }) s.Curves = null;
        }, nameof(BrushCurve));
        NotifyBrushProperties();
    }

    /// <summary>Put a dynamic back to the plain <c>p^1</c> line.</summary>
    public void ResetBrushCurve(BrushDynamic target)
    {
        SetBrushDrives(target, false);
        SetBrushDrives(target, true);
    }

    /// <summary>How the brush's strokes land on the layer.</summary>
    public LayerBlendMode BrushBlend
    {
        get => GetBrushValue(s => s.BlendOrNormal);
        // Normal is stored as absent, so a document that never leaves the
        // default never grows the key. See BrushSettings.Blend.
        set => SetBrush(s => s.Blend = value == LayerBlendMode.Normal ? null : value);
    }

    // The brush picker offers the same list the layer docker does — see
    // BlendModeChoices further down. One list, because a brush's Multiply and
    // a layer's Multiply are the same operation and offering different sets
    // would imply they were not.

    // ---- imported paper texture --------------------------------------------------

    /// <summary>The imported texture this brush bites into, or null for a built-in paper.</summary>
    public string? BrushTextureId => GetBrushValue(s => s.TextureId);

    /// <summary>True when an imported paper is in charge, so the surface list can stand down.</summary>
    public bool HasImportedTexture => BrushTextureId is not null;

    /// <summary>
    /// Take an image into the document as a paper texture and point the brush
    /// at it.
    /// </summary>
    /// <remarks>
    /// Stored in the document rather than referenced on disk, the same as a
    /// brush tip: a file pointing at somebody's scans folder renders
    /// differently on the next machine, which is invariant 1 broken somewhere
    /// nobody looks. Returns the id so a caller can name it back.
    /// </remarks>
    public string ImportBrushTexture(string png)
    {
        var doc = SaveTargetTab?.Doc ?? Doc;
        var id = Ids.NewId("tex");
        (doc.Textures ??= [])[id] = png;
        TextureRegistry.Register(doc.Textures);
        MarkDocumentEdited();

        SetBrush(s =>
        {
            s.TextureId = id;
            // A texture nobody can see is a texture that looks broken. The
            // depth slider starts at zero, so importing one has to open it.
            if (s.TextureDepth <= 0) s.TextureDepth = 0.5;
        }, nameof(BrushTextureId));
        NotifyBrushProperties();
        return id;
    }

    /// <summary>Go back to the built-in papers.</summary>
    public void ClearBrushTexture()
    {
        // The document keeps the pixels: strokes already painted with it still
        // reference the id, the same rule the tip picker follows.
        SetBrush(s => s.TextureId = null, nameof(BrushTextureId));
        NotifyBrushProperties();
    }

    // ---- brush tip -------------------------------------------------------------

    /// <summary>The tip the current brush stamps, or null for a plain round dab.</summary>
    public string? BrushTipId => GetBrushValue(s => s.TipId);

    /// <summary>
    /// Every tip that can be chosen right now: the project's, the artist's own,
    /// and the eight built-ins.
    /// </summary>
    public IReadOnlyList<BrushTip> AvailableTips() =>
        // Q30: which document the tips are for, so a scoped project can narrow
        // the picker. Null with no project slot, which is the unscoped answer.
        TipStore.Available(ProjectDocker.Project, inView: ActiveTab?.Source);

    /// <summary>
    /// Point the brush at a library tip, or at nothing for a round dab.
    /// </summary>
    /// <remarks>
    /// The raster is copied into the document under the same id on the way
    /// past. That is invariant 1 for tips: from here on the drawing renders
    /// whether or not the library still has it, and deleting the library copy
    /// cannot reach back and change a picture.
    /// </remarks>
    public void SetBrushTip(BrushTip? tip)
    {
        if (tip is not null)
        {
            var doc = SaveTargetTab?.Doc ?? Doc;
            TipStore.AdoptInto(doc, tip);
            BrushTipRegistry.Register(doc.BrushTips);
        }

        SetBrush(s => s.TipId = tip?.Id, nameof(BrushTipId));
        NotifyBrushProperties();
    }

    private static readonly string[] BrushPropertyNames =
    [
        nameof(BrushSize), nameof(BrushHardness), nameof(BrushOpacity), nameof(BrushFlow),
        nameof(BrushSpacing), nameof(BrushWetEdge), nameof(BrushGranulation), nameof(BrushScatter),
        nameof(BrushRotationJitter), nameof(BrushPressureEnabled),
        nameof(BrushPressureSizeGamma), nameof(BrushPressureFlowGamma), nameof(BrushPressureHardnessGamma),
        nameof(BrushPressureAffectsSize), nameof(BrushPressureAffectsFlow), nameof(BrushPressureAffectsHardness),
        nameof(BrushBlend), nameof(BrushTextureId), nameof(HasImportedTexture),
        // B74. The ring's shape comes from the brush, so it has to be re-read
        // whenever the brush changes — same list, same reason as its diameter.
        nameof(BrushCursorDiameter), nameof(BrushCursorTipId),
        nameof(BrushCursorRoundness), nameof(BrushCursorAngle),
        nameof(BrushSizeJitter), nameof(BrushMinimumDiameter), nameof(BrushRoundness),
        nameof(BrushRoundnessJitter), nameof(BrushAngleFollowsDirection), nameof(BrushFlowJitter),
        nameof(BrushTextureSurface), nameof(BrushTextureScale), nameof(BrushTextureDepth),
        nameof(BrushSecondaryColor), nameof(BrushColorJitter), nameof(BrushHueJitter),
        nameof(BrushSaturationJitter), nameof(BrushBrightnessJitter),
        nameof(IsSmudgeBrush), nameof(IsEffectBrush), nameof(ShowsEffectOptions), nameof(EffectStrength),
        nameof(BrushSmudgeMode), nameof(BrushSmudgeLength),
        nameof(BrushSmudgeRadius), nameof(BrushColorRate),
        nameof(BrushMedium), nameof(MediumIsSimulated), nameof(MediumHasBody),
        nameof(MediumWetness), nameof(MediumViscosity), nameof(MediumDrag), nameof(MediumFlowSteps),
        nameof(MediumAbsorbency), nameof(MediumEdgePull),
        nameof(MediumPigmentDensity), nameof(MediumGranularity), nameof(MediumHiding),
        nameof(MediumPhysicalMixing),
        nameof(MediumPaper), nameof(MediumPaperScale), nameof(MediumPaperInfluence),
        nameof(MediumBody), nameof(MediumRelief), nameof(MediumPaintLoad),
        nameof(MediumPressureWater), nameof(MediumPressureMix), nameof(MediumRewetting),
    ];

    private void NotifyBrushProperties()
    {
        foreach (var name in BrushPropertyNames) OnPropertyChanged(name);
        NotifyPresetProperties();
        // Switching brush or preset can change which stabilisation is in
        // effect, and the sliders show whichever it is.
        NotifySmoothingProperties();
    }

    /// <summary>Save the working brush as a reusable preset.</summary>
    public BrushPreset SaveCurrentAsPreset(string name, IEnumerable<string>? tags = null)
    {
        var preset = new BrushPreset
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Preset {BrushPresetChoices.Count + 1}" : name.Trim(),
            Tool = IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            Settings = CurrentToolSettings.Clone(),
            Tags = CleanTags(tags),
        };
        _userPresets.Add(preset);
        BrushPresetChoices.Add(preset);
        _applyingPreset = true;
        SelectedBrushPreset = preset;
        _applyingPreset = false;
        RefreshTagChoices();
        PersistBrushState();
        return preset;
    }

    // ---- editing the preset you are on -----------------------------------------

    /// <summary>
    /// Whether the working brush has drifted from the preset it came from.
    /// </summary>
    /// <remarks>
    /// The tool bar's small dot. Without it the state is genuinely ambiguous:
    /// the picker says "Pencil", the brush has been nudged four times, and
    /// nothing on screen distinguishes that from the pencil as shipped — so an
    /// artist either loses the tweaks or saves a duplicate to be safe.
    /// </remarks>
    public bool BrushIsModified =>
        SelectedBrushPreset is { } preset && !BrushComparison.SameMark(preset.Settings, CurrentToolSettings);

    /// <summary>Small enough to sit next to the picker, loud enough to notice.</summary>
    public string BrushModifiedBadge => BrushIsModified ? "●" : "";

    public string BrushModifiedTip => BrushIsModified
        ? $"Changed from “{SelectedBrushPreset?.Name}”. Update it, or save the changes as a new brush."
        : "";

    /// <summary>Can the current preset be updated in place?</summary>
    public bool CanUpdateBrushPreset => SelectedBrushPreset is not null && BrushIsModified;

    /// <summary>
    /// Write the working brush back over the preset it came from.
    /// </summary>
    /// <remarks>
    /// A built-in is updated by <em>shadowing</em> it — a user preset that
    /// reuses its id, which the merge prefers. So the change persists, and
    /// <see cref="RevertBrushPreset"/> can uncover the original by deleting
    /// the shadow. Editing the shipped list in place would have no way back.
    /// </remarks>
    public bool UpdateSelectedPreset()
    {
        if (SelectedBrushPreset is not { } preset) return false;

        var updated = new BrushPreset
        {
            Id = preset.Id,
            Name = preset.Name,
            Tool = IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            Settings = CurrentToolSettings.Clone(),
            TipPng = preset.TipPng,
            Tags = preset.Tags is null ? null : [.. preset.Tags],
        };

        _userPresets.RemoveAll(p => p.Id == preset.Id);
        _userPresets.Add(updated);
        ReplaceInChoices(preset, updated);
        PersistBrushState();
        return true;
    }

    /// <summary>True when the selected preset is a built-in that has been overwritten.</summary>
    public bool CanRevertBrushPreset =>
        SelectedBrushPreset is { IsBuiltIn: true } preset && _userPresets.Any(p => p.Id == preset.Id);

    /// <summary>Delete the shadow over a built-in, uncovering what shipped.</summary>
    public bool RevertBrushPreset()
    {
        if (SelectedBrushPreset is not { IsBuiltIn: true } preset) return false;
        if (_userPresets.RemoveAll(p => p.Id == preset.Id) == 0) return false;

        var original = BuiltInPresets.Create().FirstOrDefault(p => p.Id == preset.Id);
        if (original is null) return false;

        ReplaceInChoices(preset, original);
        _applyingPreset = true;
        SelectedBrushPreset = original;
        _applyingPreset = false;
        // Apply it, or the tool bar would show the shipped brush's name over
        // the edited brush's settings and the dot would say "unchanged".
        OnSelectedBrushPresetChanged(original);
        PersistBrushState();
        return true;
    }

    /// <summary>Rename a preset the artist made. Built-ins keep their names.</summary>
    public bool RenamePreset(BrushPreset preset, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || preset.IsBuiltIn) return false;
        preset.Name = trimmed;
        // The list is bound to the objects, so nudge it into re-reading them.
        ReplaceInChoices(preset, preset);
        PersistBrushState();
        return true;
    }

    /// <summary>
    /// Remove a preset. A built-in is reverted rather than removed — it is not
    /// the artist's to delete, and "delete" on one plainly means "give me back
    /// the one that shipped".
    /// </summary>
    public bool DeletePreset(BrushPreset preset)
    {
        if (preset.IsBuiltIn) return RevertBrushPreset();
        if (_userPresets.RemoveAll(p => p.Id == preset.Id) == 0) return false;

        var at = BrushPresetChoices.IndexOf(preset);
        if (at >= 0) BrushPresetChoices.RemoveAt(at);
        if (SelectedBrushPreset?.Id == preset.Id)
        {
            _applyingPreset = true;
            SelectedBrushPreset = null;
            _applyingPreset = false;
        }
        RefreshTagChoices();
        NotifyPresetProperties();
        PersistBrushState();
        return true;
    }

    /// <summary>
    /// Remove several presets at once. Returns how many went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a loop over <see cref="DeletePreset"/>, and the difference is the point of the
    /// method.</b> Each single delete persists the whole store, refreshes the tag list and
    /// raises five property notifications; clearing an imported collection of fifty-six that
    /// way writes the file fifty-six times and rebuilds the tag list fifty-six times, on the
    /// UI thread, which is the same shape of stall as the import that put them there.
    /// </para>
    /// <para>
    /// Built-ins are skipped rather than reverted. On a single delete "give me back the one
    /// that shipped" is the obvious reading of the button; inside a multi-selection it is
    /// not — somebody clearing a folder of imports did not ask for a shipped brush to be
    /// silently restored to factory settings on the way past.
    /// </para>
    /// </remarks>
    public int DeletePresets(IEnumerable<BrushPreset> presets)
    {
        var ids = presets.Where(p => !p.IsBuiltIn).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return 0;

        var removed = _userPresets.RemoveAll(p => ids.Contains(p.Id));
        if (removed == 0) return 0;

        for (var i = BrushPresetChoices.Count - 1; i >= 0; i--)
        {
            if (ids.Contains(BrushPresetChoices[i].Id)) BrushPresetChoices.RemoveAt(i);
        }

        if (SelectedBrushPreset is { } selected && ids.Contains(selected.Id))
        {
            _applyingPreset = true;
            SelectedBrushPreset = null;
            _applyingPreset = false;
        }

        RefreshTagChoices();
        NotifyPresetProperties();
        PersistBrushState();
        return removed;
    }

    // ---- tags -------------------------------------------------------------------

    /// <summary>Every tag any preset carries, in use order. What the picker filters by.</summary>
    public ObservableCollection<string> BrushTagChoices { get; } = [];

    /// <summary>Set the tags on a preset. Built-ins can be tagged too — by shadowing.</summary>
    public bool SetPresetTags(BrushPreset preset, IEnumerable<string> tags)
    {
        var cleaned = CleanTags(tags);

        if (preset.IsBuiltIn && _userPresets.All(p => p.Id != preset.Id))
        {
            // Filing a shipped brush is an edit like any other, so it goes
            // through the same shadow rather than mutating the list Create()
            // rebuilds from scratch every launch.
            var shadow = new BrushPreset
            {
                Id = preset.Id,
                Name = preset.Name,
                Tool = preset.Tool,
                Settings = preset.Settings.Clone(),
                TipPng = preset.TipPng,
                Tags = cleaned,
            };
            _userPresets.Add(shadow);
            ReplaceInChoices(preset, shadow);
            if (SelectedBrushPreset?.Id == preset.Id)
            {
                _applyingPreset = true;
                SelectedBrushPreset = shadow;
                _applyingPreset = false;
            }
        }
        else
        {
            preset.Tags = cleaned;
        }

        RefreshTagChoices();
        PersistBrushState();
        return true;
    }

    private static List<string>? CleanTags(IEnumerable<string>? tags)
    {
        var cleaned = (tags ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        // Null rather than empty, so a preset nobody filed writes no key.
        return cleaned.Count == 0 ? null : cleaned;
    }

    private void RefreshTagChoices()
    {
        var seen = BrushPresetChoices
            .SelectMany(p => p.Tags ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BrushTagChoices.Clear();
        foreach (var tag in seen) BrushTagChoices.Add(tag);
    }

    private void ReplaceInChoices(BrushPreset old, BrushPreset replacement)
    {
        var at = BrushPresetChoices.IndexOf(old);
        if (at < 0)
        {
            BrushPresetChoices.Add(replacement);
        }
        else
        {
            // Removing and re-inserting rather than assigning, so a bound list
            // re-reads the row even when the object is the same one renamed.
            BrushPresetChoices.RemoveAt(at);
            BrushPresetChoices.Insert(at, replacement);
        }

        if (SelectedBrushPreset?.Id == replacement.Id)
        {
            _applyingPreset = true;
            SelectedBrushPreset = replacement;
            _applyingPreset = false;
        }
        NotifyPresetProperties();
    }

    private void NotifyPresetProperties()
    {
        OnPropertyChanged(nameof(BrushIsModified));
        OnPropertyChanged(nameof(BrushModifiedBadge));
        OnPropertyChanged(nameof(BrushModifiedTip));
        OnPropertyChanged(nameof(CanUpdateBrushPreset));
        OnPropertyChanged(nameof(CanRevertBrushPreset));
    }

    /// <summary>Add imported presets (from .abr/.gbr/.gih/.kpp) and persist them.</summary>
    public int AddImportedPresets(IEnumerable<BrushPreset> presets)
    {
        var added = 0;
        foreach (var preset in presets)
        {
            _userPresets.Add(preset);
            BrushPresetChoices.Add(preset);
            added++;
        }
        if (added > 0) PersistBrushState();
        return added;
    }

    /// <summary>
    /// Import brush files (.abr/.gbr/.gih/.kpp) into presets, on this thread.
    /// </summary>
    /// <remarks>
    /// Kept for callers with a handful of files and no window to hold — the MCP surface and
    /// the tests. <b>Anything an artist starts should use
    /// <see cref="ImportBrushFilesAsync"/></b>: the reading is what made the window stop
    /// answering the compositor on a fifty-six brush collection, and this overload does it
    /// right here.
    /// </remarks>
    public (int Added, int Failed) ImportBrushFiles(IEnumerable<(string Name, byte[] Bytes)> files)
    {
        var outcome = BrushImportJob.Read(files.ToList());
        var added = AddImportedPresets(outcome.Presets);
        AiStatus = BrushImportJob.Summarise(outcome);
        return (added, outcome.Unreadable.Count);
    }

    /// <summary>
    /// Import brush files off the UI thread, reporting progress as it goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reading runs on a worker; only the two steps that touch bound state — adding to
    /// <c>BrushPresetChoices</c> and persisting — happen back here, once, when it is done.
    /// That is the whole fix for the reported "the main window became transparent as if about
    /// to crash": nothing was crashing, the UI thread was simply inside a parser for several
    /// seconds and had stopped painting.
    /// </para>
    /// <para>
    /// <b>Cancellable, because an import of the wrong folder is a real mistake to make.</b>
    /// Giving up keeps the brushes already read rather than throwing them away — they are
    /// what the artist would have got if they had picked fewer files, and discarding them
    /// would make the cancel button cost work rather than save it.
    /// </para>
    /// </remarks>
    public async Task<(int Added, BrushImportOutcome Outcome)> ImportBrushFilesAsync(
        IReadOnlyList<(string Name, byte[] Bytes)> files,
        IProgress<BrushImportProgress>? progress = null,
        CancellationToken cancel = default)
    {
        var outcome = await Task.Run(() => BrushImportJob.Read(files, progress, cancel), cancel)
            .ConfigureAwait(true);

        var added = AddImportedPresets(outcome.Presets);
        AiStatus = BrushImportJob.Summarise(outcome);
        return (added, outcome);
    }

    /// <summary>
    /// Presets grouped by what they cost, stably within each group.
    /// </summary>
    /// <remarks>
    /// <c>OrderBy</c> is a stable sort in LINQ, so a brush never moves
    /// relative to its neighbours of the same cost — the list an artist has
    /// learned the shape of stays learnable.
    /// </remarks>
    private static IEnumerable<BrushPreset> Ordered(IEnumerable<BrushPreset> presets) =>
        presets.OrderBy(p => p.Cost);

    private void PersistBrushState()
    {
        PresetStore.Save(new PresetStore.State
        {
            UserPresets = _userPresets,
            LastBrushPresetId = SelectedBrushPreset?.Id,
            LastBrush = _brushWork.Clone(),
            LastEraser = _eraserWork.Clone(),
            SmoothingMode = _appStabilisation.Mode.ToString(),
            SmoothingWindow = _appStabilisation.Window,
            SmoothingStrength = _appStabilisation.Strength,
            LazyRadius = _appStabilisation.LazyRadius,
        }, BrushStorePath);
    }

    private void LoadBrushState()
    {
        var state = PresetStore.Load(BrushStorePath);
        foreach (var preset in state.UserPresets) _userPresets.Add(preset);
        // Fast brushes first, expressive ones after, each group keeping the
        // order it was declared in. The badge marks them individually; the
        // grouping is what makes the two kinds legible as kinds — an artist
        // scanning for something cheap should not have to read every row.
        foreach (var preset in Ordered(BuiltInPresets.Merge(state.UserPresets)))
        {
            BrushPresetChoices.Add(preset);
        }
        RefreshTagChoices();
        if (state.LastBrush is not null) _brushWork = state.LastBrush.Clone();
        else _brushWork = new BrushSettings { Size = 6, Hardness = 0.8 };
        if (state.LastEraser is not null) _eraserWork = state.LastEraser.Clone();
        if (Enum.TryParse<SmoothingMode>(state.SmoothingMode, out var mode)) _appStabilisation.Mode = mode;
        if (state.SmoothingWindow is { } window) _appStabilisation.Window = Math.Clamp(window | 1, 3, 25);
        if (state.SmoothingStrength is { } strength) _appStabilisation.Strength = Math.Clamp(strength, 0, 0.95);
        if (state.LazyRadius is { } radius) _appStabilisation.LazyRadius = Math.Clamp(radius, 4, 200);
        // Restore the selection WITHOUT re-applying the preset (the working
        // settings above already carry the user's last tweaks on top of it).
        _applyingPreset = true;
        SelectedBrushPreset = BrushPresetChoices.FirstOrDefault(p => p.Id == state.LastBrushPresetId);
        _applyingPreset = false;
    }

    // ---- the active colour pair -------------------------------------------------
    //
    // Foreground and background, the way every painting tool has had them
    // since Photoshop 1: one pair, shared by the brush, the fill and the
    // gradient, swapped with X and reset with D. They are global on purpose —
    // reaching for the same colour in three tools and finding three different
    // answers is the thing this arrangement exists to prevent.

    /// <summary>The colour tools paint with. <c>ColorHex</c> is its old name.</summary>
    [ObservableProperty]
    private string _colorHex = "#000000";

    /// <summary>
    /// The other one. What the eraser reveals conceptually, what a
    /// foreground-to-background gradient ends on, and what X swaps to.
    /// </summary>
    [ObservableProperty]
    private string _backgroundColorHex = "#ffffff";

    /// <summary>Alias, so views can say what they mean.</summary>
    public string ForegroundColorHex
    {
        get => ColorHex;
        set => ColorHex = value;
    }


    /// <summary>
    /// Trade foreground and background (X).
    /// </summary>
    /// <remarks>
    /// The swatch link goes with them. Swapping to a palette colour and back
    /// has to leave the stroke still following that swatch, or X quietly turns
    /// live palette colours into literals — which is a data loss you would not
    /// notice until the recolour did nothing.
    /// </remarks>
    [RelayCommand]
    public void SwapColors()
    {
        var (foreground, background) = (ColorHex, BackgroundColorHex);
        var (foregroundSwatch, backgroundSwatch) = (ActiveSwatchId, _backgroundSwatchId);
        BackgroundColorHex = foreground;
        _backgroundSwatchId = foregroundSwatch;
        if (backgroundSwatch is not null) PaintWithSwatch(backgroundSwatch);
        else
        {
            ActiveSwatchId = null;
            ColorHex = background;
        }
    }

    private string? _backgroundSwatchId = DocumentFactory.WhiteSwatchId;

    /// <summary>Back to black over white (D).</summary>
    [RelayCommand]
    public void ResetColors()
    {
        BackgroundColorHex = "#ffffff";
        _backgroundSwatchId = DocumentFactory.WhiteSwatchId;
        if (Doc.Palettes.SelectMany(p => p.Swatches)
                .Any(sw => sw.Id == DocumentFactory.BlackSwatchId))
        {
            PaintWithSwatch(DocumentFactory.BlackSwatchId);
        }
        else
        {
            ActiveSwatchId = null;
            ColorHex = "#000000";
        }
    }

    // ---- active tool ----------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEraser))]
    [NotifyPropertyChangedFor(nameof(ShowsEffectOptions))]
    [NotifyPropertyChangedFor(nameof(PointerIntent))]
    [NotifyPropertyChangedFor(nameof(PointerRefusal))]
    [NotifyPropertyChangedFor(nameof(IsBrushTool))]
    [NotifyPropertyChangedFor(nameof(IsEraserTool))]
    [NotifyPropertyChangedFor(nameof(IsFillTool))]
    [NotifyPropertyChangedFor(nameof(IsSelectTool))]
    [NotifyPropertyChangedFor(nameof(IsPickerTool))]
    [NotifyPropertyChangedFor(nameof(IsGradientTool))]
    [NotifyPropertyChangedFor(nameof(IsMoveTool))]
    // Missing, and it cost the whole shape options group: nothing ever told
    // the bar the tool had changed, so IsVisible stayed false and there was no
    // way to pick a shape.
    [NotifyPropertyChangedFor(nameof(IsShapeTool))]
    [NotifyPropertyChangedFor(nameof(IsPaintTool))]
    [NotifyPropertyChangedFor(nameof(IsArrowTool))]
    [NotifyPropertyChangedFor(nameof(IsDirectSelectTool))]
    [NotifyPropertyChangedFor(nameof(IsPenTool))]
    [NotifyPropertyChangedFor(nameof(IsWidthTool))]
    private ToolId _activeTool = ToolId.Brush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectVariantGlyph))]
    [NotifyPropertyChangedFor(nameof(IsFreehandVariant))]
    [NotifyPropertyChangedFor(nameof(IsPolygonVariant))]
    [NotifyPropertyChangedFor(nameof(IsBoxVariant))]
    [NotifyPropertyChangedFor(nameof(IsEllipseVariant))]
    [NotifyPropertyChangedFor(nameof(IsWandVariant))]
    private SelectVariant _activeSelectVariant = SelectVariant.Freehand;

    public bool IsFreehandVariant => ActiveSelectVariant == SelectVariant.Freehand;

    public bool IsPolygonVariant => ActiveSelectVariant == SelectVariant.Polygon;

    public bool IsBoxVariant => ActiveSelectVariant == SelectVariant.Box;

    public bool IsEllipseVariant => ActiveSelectVariant == SelectVariant.Ellipse;

    public bool IsWandVariant => ActiveSelectVariant == SelectVariant.Wand;

    public bool IsBrushTool => ActiveTool == ToolId.Brush;

    /// <summary>
    /// What the pointer should say the active tool will do over the active
    /// layer — bound straight onto the canvas control.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decision itself is <see cref="Rendering.CanvasCursor"/>, which is
    /// pure and tested without a window; this only gathers the facts. Keeping
    /// the gathering here and the rule there is what stops the canvas control
    /// and anything else that reports the same refusal from drifting apart.
    /// </para>
    /// <para>
    /// <b>Two of the five facts are still assumed, and they are the two that
    /// vary per pixel rather than per layer:</b> whether the pointer is inside
    /// the selection and whether there is paint under it for an alpha-locked
    /// layer. Both need a hover position the view model is not told about yet,
    /// so they are left at their permissive default — the cursor under-reports
    /// rather than lying, which is the right way round for a refusal.
    /// </para>
    /// </remarks>
    public Rendering.CanvasCursorKind PointerIntent =>
        Rendering.CanvasCursor.For(ActiveTool, CurrentTarget, _hoverModifiers);

    /// <summary>Why the active tool would do nothing here, or null.</summary>
    /// <remarks>
    /// Nothing shows this yet: the application has no status line, and inventing
    /// one to carry a sentence is a bigger decision than this change. The
    /// pointer already refuses, which is the half an artist notices; this is the
    /// half that says why, and it is ready for the surface that will hold it.
    /// </remarks>
    public string? PointerRefusal =>
        Rendering.CanvasCursor.Refusal(ActiveTool, CurrentTarget, _hoverModifiers);

    /// <summary>Whether the refusal has anything to say, for the status line.</summary>
    public bool HasPointerRefusal => PointerRefusal is not null;

    private Rendering.CanvasTarget CurrentTarget
    {
        get
        {
            var alphaLocked = ActiveLayer.AlphaLocked;
            return new Rendering.CanvasTarget(
                LayerHidden: !ActiveLayer.Visible,
                LayerLocked: ActiveLayer.Locked,
                AlphaLocked: alphaLocked,
                // The two that need a place. With the pointer off the canvas
                // there is no place, so both stay permissive — the cursor
                // under-reports rather than inventing a refusal.
                NothingUnderPointer:
                    alphaLocked && _hoverPoint is { } a && !PaintUnder(a.X, a.Y),
                OutsideSelection:
                    _hoverPoint is { } b && !InsideSelection(b.X, b.Y));
        }
    }

    /// <summary>
    /// Re-ask the mapping, because something it reads has changed underneath it.
    /// </summary>
    /// <remarks>
    /// <b>Needed because a layer's flags are plain properties on a document
    /// model that raises nothing.</b> `Layer.Visible` and `Layer.Locked` are set
    /// through the editor so they undo correctly, and neither notifies — so
    /// hiding the layer you are drawing on would otherwise leave the pointer
    /// still promising a stroke until you happened to switch tools.
    /// </remarks>
    public void RefreshPointerIntent()
    {
        OnPropertyChanged(nameof(PointerIntent));
        OnPropertyChanged(nameof(PointerRefusal));
        OnPropertyChanged(nameof(HasPointerRefusal));
    }

    private Avalonia.Input.KeyModifiers _hoverModifiers;
    private (double X, double Y)? _hoverPoint;

    /// <summary>
    /// The pointer moved: remember where, and re-ask what the tool would do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what lets the last two refusals be true rather than assumed.</b>
    /// Whether the pointer is inside the selection and whether there is paint
    /// under an alpha-locked brush are the only two facts that vary pixel by
    /// pixel rather than per layer, so they need a position and nothing else did.
    /// </para>
    /// <para>
    /// <b>Bounded, as invariant 6 requires.</b> The selection test is
    /// point-in-polygon over the outline — proportional to the contour, not to
    /// the canvas — where <c>SelectionMask</c> would rasterise a full
    /// <c>w × h</c> mask on every move. The alpha read is one pixel, and only
    /// when the layer is alpha-locked, because that is the only case whose
    /// answer is used.
    /// </para>
    /// </remarks>
    public void UpdatePointerContext(double x, double y, Avalonia.Input.KeyModifiers modifiers)
    {
        _hoverPoint = (x, y);
        _hoverModifiers = modifiers;
        RefreshPointerIntent();
    }

    /// <summary>The pointer left the canvas: stop claiming to know where it is.</summary>
    public void ClearPointerContext()
    {
        if (_hoverPoint is null) return;
        _hoverPoint = null;
        RefreshPointerIntent();
    }

    /// <summary>Whether a stroke coordinate falls inside the active selection.</summary>
    /// <remarks>
    /// Even-odd, matching <c>BrushEngine.PathFromContours</c> — a hole in a
    /// selection is outside it, and the cursor has to agree with the renderer
    /// about that or it forbids in the wrong places.
    /// </remarks>
    private bool InsideSelection(double x, double y)
    {
        if (!HasSelection) return true;
        // Surface space: the contours are the mask's, and so is the question.
        var (px, py) = (x - Scene.Left, y - Scene.Top);
        var inside = false;
        foreach (var contour in _selectionContours)
        {
            for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
            {
                var a = contour[i];
                var b = contour[j];
                if (a.Y > py != b.Y > py &&
                    px < (b.X - a.X) * (py - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }
        }
        return inside;
    }

    /// <summary>Whether the active layer has any paint at a stroke coordinate.</summary>
    /// <remarks>
    /// Only asked when the layer is alpha-locked, because that is the only time
    /// the answer changes what the pointer says — and it costs a bitmap read, so
    /// asking it otherwise would be paying per pointer event for nothing.
    /// </remarks>
    private bool PaintUnder(double x, double y)
    {
        var px = (int)Math.Round(x - Scene.Left);
        var py = (int)Math.Round(y - Scene.Top);
        if (px < 0 || py < 0 || px >= Scene.Width || py >= Scene.Height) return false;
        if (ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex) is not { } frame) return false;
        try
        {
            return _cache.Get(frame, Scene.Width, Scene.Height).GetPixel(px, py).Alpha > 0;
        }
        catch
        {
            // A cache miss mid-resize is not worth a crash on a hover; assume
            // paint, which is the permissive answer and never forbids wrongly.
            return true;
        }
    }

    /// <summary>
    /// Tell the view the paper moved, after a resize changed the origin.
    /// </summary>
    /// <remarks>
    /// <c>Scene.OriginX</c> is a plain field on the document model, so nothing
    /// is raised when a resize changes it — and every pointer conversion in the
    /// canvas reads it. Without this the tools would keep converting against the
    /// old corner, which lands strokes exactly one resize out of place.
    /// </remarks>
    public void RefreshDocumentOrigin() => OnPropertyChanged(nameof(DocumentOrigin));

    /// <summary>
    /// Apply a resize the artist confirmed, as one undo step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One <c>Perform</c> around the whole thing</b>, and that is not
    /// bookkeeping: an image resize walks every stroke, clip contour, guide,
    /// camera key, symbol placement and collision box in the document. An artist
    /// who had to press undo once per stroke would have lost the drawing.
    /// </para>
    /// <para>
    /// Here rather than in the window because it changes the document, and
    /// because everything that has to be told afterwards — the origin the
    /// pointer converts against, the caches, the snapshot — is already this
    /// type's to notify. The window's remaining job is the window: show the
    /// dialog, then refit the view to paper that is now a different size.
    /// </para>
    /// </remarks>
    public bool ApplyResize(ResizeDialogViewModel choice)
    {
        var changed = false;
        _editor.Perform(doc => changed = choice.Apply(doc, new Lightbox.Raster.PixelResampler()));
        if (!changed) return false;

        RefreshDocumentOrigin();
        InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = choice.IsImage
            ? $"Resized the image to {Scene.Width} × {Scene.Height}."
            : $"Resized the canvas to {Scene.Width} × {Scene.Height}.";
        return true;
    }

    /// <summary>The black arrow — picks things (lines, guides, symbols) rather than an area of pixels.</summary>
    public bool IsArrowTool => ActiveTool == ToolId.Arrow;

    /// <summary>The white arrow — nodes and handles on one isolated line.</summary>
    public bool IsDirectSelectTool => ActiveTool == ToolId.DirectSelect;

    /// <summary>The pen — places nodes and draws the curve between them.</summary>
    public bool IsPenTool => ActiveTool == ToolId.Pen;

    /// <summary>The width tool — drag off a line to fatten it, towards it to thin it.</summary>
    public bool IsWidthTool => ActiveTool == ToolId.Width;

    public bool IsEraserTool => ActiveTool == ToolId.Eraser;

    public bool IsFillTool => ActiveTool == ToolId.Fill;

    public bool IsSelectTool => ActiveTool == ToolId.Select;

    public bool IsPickerTool => ActiveTool == ToolId.Picker;

    public bool IsGradientTool => ActiveTool == ToolId.Gradient;

    public bool IsMoveTool => ActiveTool == ToolId.Move;

    /// <summary>Brush or eraser — the tools whose strokes the brush-parameter flyout edits.</summary>
    public bool IsPaintTool => ActiveTool is ToolId.Brush or ToolId.Eraser;

    /// <summary>Eyedropper click: the color under the cursor (what the eye sees, incl. paper).</summary>
    public void PickColorAt(double x, double y)
    {
        var (px, py) = ToSurface(x, y);
        if (px < 0 || py < 0 || px >= Scene.Width || py >= Scene.Height) return;
        using var composite = CompositeVisibleLayers();
        var color = composite.GetPixel(px, py);
        if (color.Alpha == 0)
        {
            ColorHex = Scene.TransparentBackground ? "#ffffff" : Scene.BackgroundColor;
            return;
        }
        ColorHex = $"#{color.Red:x2}{color.Green:x2}{color.Blue:x2}";
    }

    /// <summary>
    /// What marking on a held cel does.
    /// </summary>
    /// <remarks>
    /// A preference, not document data, and the default is the animator's
    /// answer: a hold is somebody else's drawing shown again, and drawing on
    /// it silently rewrites the frame you were holding — every mark you make
    /// at frame 4 appears at frame 3 as well, which is a very confusing way to
    /// ruin a hold. Keying first means the timeline shows a new drawing where
    /// you made one.
    /// </remarks>
    public HoldDrawing DrawingOnAHold
    {
        get => Enum.TryParse<HoldDrawing>(Settings.DrawingOnAHold, out var v) ? v : HoldDrawing.StartANewDrawing;
        set
        {
            if (DrawingOnAHold == value) return;
            Settings.DrawingOnAHold = value.ToString();
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<HoldDrawing> HoldDrawingChoices { get; } = Enum.GetValues<HoldDrawing>();

    /// <summary>Timeline-context shortcut: key the active layer's cel at the playhead.</summary>
    public void InsertKeyframeAtPlayhead() =>
        _editor.SetKeyAt(ActiveLayer.Id, CurrentFrameIndex, FrameRole.Key);

    /// <summary>The tool button's icon, so it says which selection it will make.</summary>
    /// <remarks>
    /// The wand variant used to be U+1FA84, an emoji: full colour beside eleven
    /// monoline glyphs on the platforms that had the character, and an empty box
    /// on the ones that did not.
    /// </remarks>
    public Avalonia.Media.Geometry? SelectVariantGlyph =>
        Rendering.IconSet.Resolve(Rendering.IconSet.ForSelect(ActiveSelectVariant));

    /// <summary>Compat view of the tool (old XAML/tests): eraser vs everything else painting as brush.</summary>
    public bool IsEraser
    {
        get => ActiveTool == ToolId.Eraser;
        set => ActiveTool = value ? ToolId.Eraser : ToolId.Brush;
    }

    partial void OnActiveToolChanged(ToolId value)
    {
        // The bound sliders edit the active tool's brush configuration.
        NotifyBrushProperties();
        OnPropertyChanged(nameof(LazyRadiusForCursor));

        // A borrow is not a decision — see MainViewModel.Momentary.cs. Everything
        // below is about an artist choosing to leave a tool, and holding Ctrl is
        // not that. The two lines above are display, which a borrow does want:
        // the picker's cursor is the whole reason the borrow is legible.
        //
        // Everything that throws modal work away lives below this line, the
        // half-drawn lasso included. It was above it, which was harmless only
        // because the Select tool is not borrowable — and "harmless because of
        // a fact recorded somewhere else" is what makes adding a row to the
        // table a thing to reason about instead of a thing to do.
        if (_suppressToolSideEffects) return;

        CancelPolygonInProgress();

        // B147: leaving the arrow lets the lines go. A stroke selection is only
        // reachable from the arrow — nothing else moves, recolours or deletes it
        // — so a highlight that outlives the tool is drawn state pointing at a
        // capability the artist no longer has. Every other modal thing here
        // behaves the same way (the polygon above it, the transform session),
        // which is why this is one line rather than a mode.
        if (value != ToolId.Arrow) ClearStrokeSelection();

        // Same one-line rule for the hover preview: it is drawn state that only
        // the white arrow can act on, so it must not outlive the tool. Without
        // this the last-hovered line keeps its points on screen while the brush
        // is painting over them.
        if (value != ToolId.DirectSelect) ClearPathHover();

        // The pen keeps what it drew rather than dropping it, which is the same
        // answer Escape gets and for the same reason: reaching for another tool
        // mid-path is not a request to throw a minute of authoring away.
        if (value != ToolId.Pen) FinishPen();

        // B147's shape one tool along, and phase 2 shipped it: the node overlay
        // is drawn whatever the tool is, so leaving isolation for the brush left
        // glyphs on screen over a line nothing could reshape any more. Both
        // arrows keep the session — you enter it by double-clicking with the
        // black one and work it with the white one — and everything else ends it.
        if (value is not (ToolId.Arrow or ToolId.DirectSelect or ToolId.Width)) EndPathEdit();
    }

    [RelayCommand]
    private void SelectTool(ToolId tool)
    {
        if (tool == ToolId.Select && ActiveTool == ToolId.Select)
        {
            CycleSelectVariant();
            return;
        }
        ActiveTool = tool;
    }

    /// <summary>Pressing the selection shortcut repeatedly cycles its variants.</summary>
    public void CycleSelectVariant()
    {
        ActiveSelectVariant = ActiveSelectVariant switch
        {
            SelectVariant.Freehand => SelectVariant.Polygon,
            SelectVariant.Polygon => SelectVariant.Box,
            SelectVariant.Box => SelectVariant.Ellipse,
            SelectVariant.Ellipse => SelectVariant.Wand,
            _ => SelectVariant.Freehand,
        };
    }

    [RelayCommand]
    private void SelectVariantOf(SelectVariant variant)
    {
        ActiveSelectVariant = variant;
        ActiveTool = ToolId.Select;
    }

    // ---- fill tool -------------------------------------------------------------

    [ObservableProperty]
    private double _fillTolerance = 32;

    /// <summary>Openings up to this many pixels still count as closed ("connected").</summary>
    [ObservableProperty]
    private double _fillGapPx = 4;

    /// <summary>Overfill (+) or underfill (−) the region by pixels.</summary>
    [ObservableProperty]
    private double _fillGrowPx = 2;

    /// <summary>Sample every visible layer (fill what LOOKS empty) instead of only the active one.</summary>
    [ObservableProperty]
    private bool _smartFill = true;

    /// <summary>Insert the fill under the line work (tucks beneath the line); off = fill on top, preserving lines.</summary>
    [ObservableProperty]
    private bool _fillBelowLines = true;

    /// <summary>
    /// Where a "below line work" fill belongs in the stroke list.
    ///
    /// Index 0 was the obvious answer and the wrong one: it put the fill under
    /// EVERYTHING, so a second fill disappeared beneath the first, and a fill
    /// made after erasing was wiped by the eraser it had been slipped behind.
    /// Both read to the artist as "the fill did nothing".
    ///
    /// The rule that holds instead: go under the line work, but no further
    /// back than the last stroke that would swallow you. Only a brush stroke
    /// is line work to tuck beneath; a fill, a gradient or an eraser already
    /// on the layer is content this fill must sit on top of — an eraser
    /// especially, because it removed what was there when it ran, and putting
    /// later content underneath makes it delete something that never existed.
    /// </summary>
    internal static int UnderLineWorkIndex(IReadOnlyList<Stroke> strokes)
    {
        for (var i = strokes.Count - 1; i >= 0; i--)
        {
            if (strokes[i].Tool != ToolKind.Brush) return i + 1;
        }
        return 0;
    }

    /// <summary>Fill tool click: flood at a document position, record a fill stroke.</summary>
    /// <summary>
    /// A colour was dragged from the swatch onto the canvas. Fills there,
    /// choosing the sensible method rather than making the artist pick one:
    /// inside a selection the selection is the region, otherwise it is a
    /// flood fill from the dropped point. The colour becomes the current
    /// colour too — dragging it out is a statement of intent.
    /// </summary>
    public void DropColorAt(string hex, double x, double y)
    {
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;

        ColorHex = hex;

        // Inside a selection, the selection is obviously the region the
        // artist means — filling only the contiguous patch under the drop
        // point would be a strange reading of the gesture. Outside one, fall
        // back to a flood fill from where it landed.
        if (HasSelection && SelectionContainsPoint(x, y))
        {
            FillWholeSelection();
            return;
        }
        FillAtInternal(x, y);
    }

    private bool SelectionContainsPoint(double x, double y)
    {
        if (_selectionContours.Count == 0) return false;
        using var path = BrushEngine.PathFromContours(_selectionContours);
        return path.Contains((float)x, (float)y);
    }

    /// <summary>Fill every pixel of the current selection, as one undo step.</summary>
    private void FillWholeSelection() =>
        StrokeOverWholeSelection(ToolKind.Fill, ColorHex, ActiveSwatchId, "fill-selection");

    /// <summary>
    /// Lay one region-shaped stroke over the whole selection, as one undo step.
    /// </summary>
    /// <remarks>
    /// <b>B173.</b> The fill and the clear are the same operation with a
    /// different <see cref="ToolKind"/>, so they share a body rather than the
    /// clear being a copy that drifts. Both go through the record — invariant 1
    /// and invariant 3 — which is what makes undo free and what stops "delete"
    /// meaning something the reload cannot reproduce.
    /// </remarks>
    /// <param name="swatchId">
    /// Null for the eraser and for the background fill: a swatch reference
    /// exists so recolouring a palette entry moves the art with it, and neither
    /// of these is art in that sense — the eraser has no colour at all, and
    /// Backspace means "the background as it is now" rather than "follow this
    /// swatch forever".
    /// </param>
    private void StrokeOverWholeSelection(
        ToolKind tool, string color, string? swatchId, string label)
    {
        if (_selectionContours.Count == 0) return;
        if (PaintTargetOrKey() is not { } target) return;
        var scene = Scene;

        var stroke = new Stroke
        {
            Tool = tool,
            Color = color,
            SwatchId = swatchId,
            PaletteId = swatchId is null ? null : ActivePaletteId,
            Brush = new BrushSettings { Opacity = 1, AntiAlias = AntiAliasing },
            Points = [.. _selectionContours[0]],
            Holes = _selectionContours.Count > 1
                ? _selectionContours.Skip(1).Select(c => c.ToList()).ToList()
                : null,
            Label = label,
        };
        if (PrepareClipForSelection() is { } clip) stroke.ClipId = clip.Id;

        AppendToFrameRender(target, stroke);
        _committingScopedEdit = true;
        try
        {
            _editor.Perform(_ => StrokesOf(target).Add(stroke));
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = tool == ToolKind.ClearRegion
            ? "Cleared the selection."
            : $"Filled the selection with {color}.";
    }

    /// <summary>
    /// Delete: clear what is inside the selection, leaving the outline up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B173, and the precedence is the decided part.</b> A live marquee wins
    /// and the selected lines are the fallback — Photoshop's rule, and the one
    /// an artist arrives with. It is worth writing down that this goes
    /// <em>against</em> the precedent next door: <see cref="NudgeSelection"/>
    /// asks the line selection first and says why. So the two keys disagree
    /// about which selection they mean, knowingly.
    /// </para>
    /// <para>
    /// The cost of that disagreement is real and is why <b>B171 had to land
    /// first</b>: Delete silently changes meaning while a stale marquee is up,
    /// and before B171 a marquee could be left up by a document the artist had
    /// already closed. With selections scoped to their document, a marquee
    /// being up is something the artist did to *this* drawing.
    /// </para>
    /// <para>
    /// The outline stays after the clear, because the next thing an artist does
    /// with an emptied region is usually put something else in it.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void DeleteSelectionContents()
    {
        if (IsPlaying) return;
        if (HasSelection)
        {
            if (!CanEdit(ActiveLayer, "erase on it")) return;
            StrokeOverWholeSelection(ToolKind.ClearRegion, ColorHex, null, "clear-selection");
            return;
        }
        DeleteSelectedLinesCommand.Execute(null);
    }

    /// <summary>
    /// Backspace: flood the selection with the background colour.
    /// </summary>
    /// <remarks>
    /// <b>B173.</b> The counterpart to <see cref="DeleteSelectionContents"/>,
    /// and deliberately a *fill* rather than an erase — the two keys differ in
    /// what is left behind, which is the whole reason both exist. No fallback
    /// when nothing is selected: Backspace has never meant anything on the
    /// canvas, so there is no established behaviour to preserve, and inventing
    /// one here would be a second decision smuggled in beside the asked-for one.
    /// </remarks>
    [RelayCommand]
    private void FillSelectionWithBackground()
    {
        if (IsPlaying) return;
        if (!HasSelection) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;
        StrokeOverWholeSelection(
            ToolKind.Fill, BackgroundColorHex, null, "fill-selection-background");
    }

    /// <param name="invertSmart">
    /// Shift was held. Fill the other way from whatever the option currently
    /// says — a one-click override, not a setting change, so the option is
    /// still where it was for the next fill.
    /// </param>
    public void FillAt(double x, double y, bool invertSmart = false)
    {
        if (ActiveTool != ToolId.Fill) return;
        FillAtInternal(x, y, invertSmart);
    }

    /// <summary>
    /// The fill itself, without the tool check — a colour dropped on the
    /// canvas fills whatever tool happens to be selected.
    /// </summary>
    private void FillAtInternal(double x, double y, bool invertSmart = false)
    {
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it")) return;
        if (PaintTargetOrKey() is not { } target) return;

        var scene = Scene;
        SKBitmap? owned = null;
        try
        {
            // Held Shift flips it for this click only. A line-art layer over a
            // painted background wants smart fill nine times out of ten and
            // the active layer alone on the tenth; going to the options bar
            // and back for that one is the interruption worth removing.
            var smart = SmartFill ^ invertSmart;
            SKBitmap sample;
            if (smart)
            {
                owned = CompositeVisibleLayers();
                sample = owned;
            }
            else
            {
                sample = _cache.Get(target, scene.Width, scene.Height);
            }

            var (seedX, seedY) = ToSurface(x, y);
            var result = FloodFill.Fill(
                sample,
                seedX,
                seedY,
                new FloodFill.Options(FillTolerance, FillGapPx, FillGrowPx),
                SelectionMask(scene.Width, scene.Height));
            if (result is null)
            {
                AiStatus = "Nothing fillable at that spot.";
                return;
            }

            var stroke = new Stroke
            {
                Tool = ToolKind.Fill,
                Color = ColorHex,
                SwatchId = ActiveSwatchId,
                PaletteId = ActivePaletteId,
                Brush = new BrushSettings { Opacity = 1, AntiAlias = AntiAliasing },
                // Out of surface space: a fill is a stroke, and a stroke's
                // points are the record's coordinates (invariant 1).
                Points = ToDocument([result.Outer])[0],
                Holes = result.Holes.Count > 0 ? ToDocument(result.Holes) : null,
                Label = "fill",
            };
            var clip = PrepareClipForSelection();
            if (clip is not null) stroke.ClipId = clip.Value.Id;
            var below = FillBelowLines;

            // Fill-above stamps incrementally onto the cached frame; fill-below
            // changes stroke order, so only that path pays a frame re-render.
            if (below)
            {
                InvalidateFrameRender(target.Id);
            }
            else
            {
                AppendToFrameRender(target, stroke);
            }

            var frameId = target.Id;
            var addedClip = false;
            _committingScopedEdit = true;
            try
            {
                _editor.PerformDelta(
                    apply: doc =>
                    {
                        if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                        var list = StrokeListIn(doc, frameId);
                        if (list is null) return;
                        if (below) list.Insert(UnderLineWorkIndex(list), stroke);
                        else list.Add(stroke);
                    },
                    revert: doc =>
                    {
                        RemoveStrokeById(doc, frameId, stroke.Id);
                        if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                    },
                    affectedFrameId: frameId);
            }
            finally
            {
                _committingScopedEdit = false;
            }
            _dirtyThumbIds.Add(target.Id);
            PublishSnapshot();
            RefreshThumbnails();
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>
    /// Every visible layer composited over transparency at the playhead —
    /// "what the eye sees minus the paper". Caller owns the returned bitmap.
    /// </summary>
    private SKBitmap CompositeVisibleLayers()
    {
        var scene = Scene;
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, CurrentFrameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(
                _cache.Get(frame, scene.Width, scene.Height, celIndex: CurrentFrameIndex), null, layer.Opacity,
                SceneRenderer.ToSkia(layer.BlendMode)));
        }
        using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SkiaSharp.SKColors.Transparent);
        return SKBitmap.FromImage(image);
    }

    // ---- magic wand -------------------------------------------------------------

    [ObservableProperty]
    private double _wandTolerance = 32;

    /// <summary>Openings up to this many pixels read as closed for the wand.</summary>
    [ObservableProperty]
    private double _wandGapPx;

    /// <summary>Sample the composited visible layers instead of only the active one.</summary>
    [ObservableProperty]
    private bool _wandSampleAllLayers = true;

    /// <summary>Magic-wand click: select the connected color region at a document position.</summary>
    public void WandSelectAt(double x, double y, bool add, bool subtract)
    {
        if (ActiveTool != ToolId.Select || IsPlaying) return;
        int w = Scene.Width, h = Scene.Height;
        SKBitmap? owned = null;
        try
        {
            SKBitmap sample;
            if (WandSampleAllLayers)
            {
                owned = CompositeVisibleLayers();
                sample = owned;
            }
            else
            {
                var frame = ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex);
                if (frame is null)
                {
                    AiStatus = "The active layer has nothing drawn to select here.";
                    return;
                }
                sample = _cache.Get(frame, w, h);
            }

            var (wandX, wandY) = ToSurface(x, y);
            var result = FloodFill.Fill(
                sample, wandX, wandY,
                new FloodFill.Options(WandTolerance, WandGapPx));
            if (result is null)
            {
                AiStatus = "Nothing selectable at that spot.";
                return;
            }
            var contours = new List<List<StrokePoint>> { result.Outer };
            contours.AddRange(result.Holes);
            ApplySelectionMask(MaskFromContours(contours, w, h), add, subtract);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    // ---- selection --------------------------------------------------------------

    private List<List<StrokePoint>> _selectionContours = [];

    /// <summary>Current selection outlines (document space) for the canvas overlay.</summary>
    public IReadOnlyList<List<StrokePoint>> SelectionContours => _selectionContours;

    public bool HasSelection => _selectionContours.Count > 0;

    /// <summary>Raised when the selection outline (or polygon-in-progress) changes.</summary>
    public event Action? SelectionChanged;

    [ObservableProperty]
    private double _selectionFeather;

    /// <summary>Pixels for the Grow/Shrink selection buttons.</summary>
    [ObservableProperty]
    private double _selectionAdjustPx = 2;

    private readonly List<StrokePoint> _polygonPoints = [];

    public IReadOnlyList<StrokePoint> PolygonInProgress => _polygonPoints;

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(HasSelection));
        SelectionChanged?.Invoke();
        PublishSnapshot();
    }

    /// <summary>Combine a closed shape into the selection (Shift adds, Alt subtracts).</summary>
    internal IReadOnlyList<List<StrokePoint>> SelectionContoursForTests => _selectionContours;

    public void ApplySelectionShape(List<StrokePoint> contour, bool add, bool subtract)
    {
        if (contour.Count < 3) return;
        int w = Scene.Width, h = Scene.Height;
        ApplySelectionMask(MaskFromContours([contour], w, h), add, subtract);
    }

    /// <summary>Combine any shape mask into the selection with the standard modifiers.</summary>
    private void ApplySelectionMask(bool[] shape, bool add, bool subtract)
    {
        int w = Scene.Width, h = Scene.Height;
        bool[] mask;
        if (!HasSelection || (!add && !subtract))
        {
            mask = subtract ? new bool[w * h] : shape;
        }
        else
        {
            mask = MaskFromContours(_selectionContours, w, h);
            for (var i = 0; i < mask.Length; i++)
            {
                if (add) mask[i] |= shape[i];
                else if (subtract) mask[i] &= !shape[i];
            }
        }
        SetSelectionFromMask(mask, w, h);
    }

    /// <summary>
    /// Ctrl+click on a layer thumbnail: select the layer's visible pixels
    /// (the exposed drawing at the playhead). Shift adds, Alt subtracts.
    /// </summary>
    public void SelectLayerAlpha(LayerRow row, bool add, bool subtract)
    {
        var frame = ExposureSheet.ExposedFrame(row.Layer, CurrentFrameIndex);
        if (frame is null)
        {
            AiStatus = $"Layer “{row.Layer.Name}” has nothing drawn to select here.";
            return;
        }
        int w = Scene.Width, h = Scene.Height;
        var bmp = _cache.Get(frame, w, h);
        var pixels = bmp.Pixels;
        var shape = new bool[w * h];
        var any = false;
        for (var i = 0; i < shape.Length; i++)
        {
            // Low threshold keeps soft brush edges inside the selection.
            if (pixels[i].Alpha > 25)
            {
                shape[i] = true;
                any = true;
            }
        }
        if (!any)
        {
            AiStatus = $"Layer “{row.Layer.Name}” has nothing drawn to select here.";
            return;
        }
        ApplySelectionMask(shape, add, subtract);
    }

    public void AddPolygonVertex(double x, double y)
    {
        _polygonPoints.Add(new StrokePoint(x, y, 1));
        SelectionChanged?.Invoke();
    }

    public void CompletePolygon(bool add, bool subtract)
    {
        var contour = new List<StrokePoint>(_polygonPoints);
        _polygonPoints.Clear();
        SelectionChanged?.Invoke();
        ApplySelectionShape(contour, add, subtract);
    }

    public void CancelPolygon() => CancelPolygonInProgress();

    private void CancelPolygonInProgress()
    {
        if (_polygonPoints.Count == 0) return;
        _polygonPoints.Clear();
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Whether Select All and Deselect should mean the objects on the canvas
    /// rather than a region of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B168.</b> Both commands used to ask <c>ActiveTool == ToolId.Select</c>,
    /// which names the wrong tool — and not by a near miss. <see cref="ToolId.Select"/>
    /// is the <em>marquee</em>: the tool whose entire job is making the region
    /// these commands then could not touch. The tools that pick objects are the
    /// two arrows, exactly as <c>ToolId.Arrow</c>'s own documentation says
    /// ("picks <b>things</b> … not areas of pixels").
    /// </para>
    /// <para>
    /// So the old condition was inverted in effect: Ctrl+D with the marquee in
    /// hand cleared object selections and left the marquee up, and Ctrl+A took
    /// all the objects instead of the canvas. One property, asked by both, so a
    /// fix to one cannot drift from the other.
    /// </para>
    /// </remarks>
    private bool ObjectSelectionIsTheSubject =>
        ActiveTool is ToolId.Arrow or ToolId.DirectSelect;

    [RelayCommand]
    private void SelectAll()
    {
        // With an arrow in hand, "all" means the objects; otherwise the canvas.
        if (ObjectSelectionIsTheSubject)
        {
            var frame = PaintTargetOrKey();
            if (frame is not Frame pf) return;

            // Select all placements on current frame
            if (pf.Placements is not null && pf.Placements.Count > 0)
            {
                _selectionManager.ClearAllSelections();
                foreach (var placement in pf.Placements)
                {
                    _selectionManager.AddPlacementToSelection(placement.Id);
                }
                return;
            }

            // Select all guides in the document
            var guides = Doc?.Scene?.Guides;
            if (guides is not null && guides.Count > 0)
            {
                _selectionManager.ClearAllSelections();
                for (int i = 0; i < guides.Count; i++)
                {
                    _selectionManager.AddGuideToSelection(i);
                }
                return;
            }

            // Select all reference boxes
            var activeRef = ActiveReference;
            if (activeRef?.Cells is not null && activeRef.Cells.Count > 0)
            {
                _selectionManager.ClearAllSelections();
                for (int i = 0; i < activeRef.Cells.Count; i++)
                {
                    _selectionManager.AddRefBoxToSelection(i);
                }
                return;
            }

            // Select all anchors (if rig edit mode is on)
            if (RigEditMode && Doc?.Scene?.Anchors is not null && Doc.Scene.Anchors.Count > 0)
            {
                _selectionManager.ClearAllSelections();
                foreach (var anchor in Doc.Scene.Anchors)
                {
                    _selectionManager.AddAnchorToSelection(anchor.Id);
                }
                return;
            }

            // Select all collision shapes (if rig edit mode is on)
            if (RigEditMode && Doc?.Scene?.Shapes is not null && Doc.Scene.Shapes.Count > 0)
            {
                _selectionManager.ClearAllSelections();
                foreach (var shape in Doc.Scene.Shapes)
                {
                    _selectionManager.AddShapeToSelection(shape.Id);
                }
            }
        }
        else
        {
            // Pixel/stroke selection (existing behavior)
            _selectionContours =
            [
                [new(0, 0, 1), new(Scene.Width, 0, 1), new(Scene.Width, Scene.Height, 1), new(0, Scene.Height, 1)],
            ];
            NotifySelection();
        }
    }

    /// <summary>
    /// Deselect: afterwards, nothing is selected.
    /// </summary>
    /// <remarks>
    /// <b>B168.</b> Unlike <see cref="SelectAll"/> this asks no question at all,
    /// and the asymmetry is the point. "Select all" has to know what *all*
    /// means, so it consults <see cref="ObjectSelectionIsTheSubject"/>; "none"
    /// means none, and a deselect that clears one kind of selection while
    /// leaving another live is the bug rather than a subtlety. It also means
    /// the artist can never be left holding a selection they cannot see how to
    /// remove — which is what made this a P1: a marquee clips painting, so the
    /// symptom of a failed deselect is <em>the application stopped drawing</em>.
    /// </remarks>
    [RelayCommand]
    private void Deselect()
    {
        // Already a no-op when nothing is picked — it guards its own event.
        _selectionManager.ClearAllSelections();
        if (!HasSelection && _polygonPoints.Count == 0) return;
        _selectionContours = [];
        _polygonPoints.Clear();
        NotifySelection();
    }

    /// <summary>
    /// Drop every selection without announcing it — for a document swap, where
    /// the announcement belongs to the swap rather than to the selection.
    /// </summary>
    /// <remarks>
    /// <b>B171.</b> Deliberately not <see cref="Deselect"/>: that is a command
    /// an artist issued and it publishes a snapshot, which during
    /// <c>AttachEditor</c> would publish a frame from a half-swapped document.
    /// </remarks>
    private void ClearSelectionState()
    {
        _selectionManager.ClearAllSelections();
        _selectionContours = [];
        _polygonPoints.Clear();
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Arrow keys over the canvas: shift the selection outline by whole pixels.</summary>
    public void NudgeSelection(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        // A line selection wins, because the two cannot both be live in a way
        // that matters: the Arrow holds lines, the Select tools hold an area, and
        // only one of them is what the artist is looking at. Asked first rather
        // than given its own keys — `canvas.nudge*` is already the registered,
        // rebindable, canvas-scoped binding for "move what is selected", and
        // adding a second set would mean an artist rebinding one and finding the
        // other still on the old key.
        if (NudgeSelectionFromKeyboard(dx, dy, coarse: false)) return;
        if (!HasSelection) return;
        foreach (var contour in _selectionContours)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                var p = contour[i];
                contour[i] = p with { X = p.X + dx, Y = p.Y + dy };
            }
        }
        NotifySelection();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        int w = Scene.Width, h = Scene.Height;
        var mask = HasSelection ? MaskFromContours(_selectionContours, w, h) : new bool[w * h];
        for (var i = 0; i < mask.Length; i++) mask[i] = !mask[i];
        SetSelectionFromMask(mask, w, h);
    }

    [RelayCommand]
    private void GrowSelection() => AdjustSelection(Math.Abs(SelectionAdjustPx));

    [RelayCommand]
    private void ShrinkSelection() => AdjustSelection(-Math.Abs(SelectionAdjustPx));

    private void AdjustSelection(double px)
    {
        if (!HasSelection || Math.Abs(px) < 0.5) return;
        int w = Scene.Width, h = Scene.Height;
        // Boundary included: these contours were traced off a mask, and
        // without their own boundary ring back the shape walks into its
        // top-left corner a little on every adjustment.
        var mask = MaskFromContours(_selectionContours, w, h, includeBoundary: true);
        var r = (int)Math.Round(Math.Abs(px));
        mask = px > 0 ? FloodFill.Dilate(mask, w, h, r) : FloodFill.Erode(mask, w, h, r);
        SetSelectionFromMask(mask, w, h);
    }

    private void SetSelectionFromMask(bool[] mask, int w, int h)
    {
        _selectionContours = FloodFill.TraceAllContours(mask, w, h);
        NotifySelection();
    }

    /// <summary>Rasterize contours (even-odd) to a boolean mask.</summary>
    /// <param name="includeBoundary">
    /// Also paint the boundary pixels the contour runs through.
    /// </param>
    /// <remarks>
    /// <para>
    /// Off for an ordinary mask: a contour drawn by hand is a geometric
    /// outline and filling it is exactly right.
    /// </para>
    /// <para>
    /// On when the contour <i>came from</i> a mask. <c>TraceBoundary</c> walks
    /// pixel centres, so the polygon it returns runs down the middle of the
    /// boundary ring rather than around the outside of it; filling that back
    /// keeps only the pixels whose centres are strictly inside, and Skia's
    /// fill rule resolves the exact-half case towards the bottom right. The
    /// top and left rings are therefore lost on every round trip, and Shrink
    /// followed by Grow walked a selection two pixels into its own top-left
    /// corner each time. Stroking the same path with a one-pixel pen puts the
    /// ring back, which makes the round trip stable — the property Grow and
    /// Shrink actually need.
    /// </para>
    /// </remarks>
    private static bool[] MaskFromContours(
        IReadOnlyList<List<StrokePoint>> contours, int w, int h, bool includeBoundary = false)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException("Could not create mask surface.");
        surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
        using (var path = BrushEngine.PathFromContours(contours))
        using (var paint = new SKPaint { Color = SkiaSharp.SKColors.White, IsAntialias = false })
        {
            surface.Canvas.DrawPath(path, paint);
            if (includeBoundary)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1;
                surface.Canvas.DrawPath(path, paint);
            }
        }
        using var image = surface.Snapshot();
        using var bmp = SKBitmap.FromImage(image);
        var pixels = bmp.Pixels;
        var mask = new bool[w * h];
        for (var i = 0; i < mask.Length; i++) mask[i] = pixels[i].Alpha > 127;
        return mask;
    }

    /// <summary>The selection as a flood-fill constraint (null when nothing is selected).</summary>
    private bool[]? SelectionMask(int w, int h) =>
        HasSelection ? MaskFromContours(_selectionContours, w, h) : null;

    /// <summary>
    /// Freeze the active selection as a document clip region (content-hashed,
    /// deduped) so strokes painted under it re-render identically forever.
    /// </summary>
    /// <summary>
    /// Where the paper's top-left sits in stroke coordinates, for the canvas
    /// control's pointer conversions.
    /// </summary>
    public Avalonia.PixelPoint DocumentOrigin => new(Scene.Left, Scene.Top);

    /// <summary>
    /// A stroke coordinate as a pixel in a document-sized bitmap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three tools index pixels rather than reasoning about the record</b> —
    /// flood fill, the wand and the colour picker — and they are the only place
    /// in this file that needs this. Everything else works in stroke
    /// coordinates, which is what <c>CanvasControl.ViewToDoc</c> now hands over.
    /// </para>
    /// <para>
    /// The two spaces are the same until the canvas is grown or cropped on the
    /// left or top, which is exactly why this is easy to forget and impossible
    /// to notice: every test written on an unresized document passes either way.
    /// </para>
    /// </remarks>
    private (int X, int Y) ToSurface(double x, double y) =>
        ((int)Math.Round(x - Scene.Left), (int)Math.Round(y - Scene.Top));

    /// <summary>Surface-space contours as stroke coordinates.</summary>
    /// <remarks>
    /// The other direction, for the two results that become part of the record:
    /// a fill's outline (invariant 1) and a selection's clip region
    /// (invariant 3). Both come out of <c>FloodFill</c> indexed from a bitmap's
    /// own corner and have to be told where that corner is.
    /// </remarks>
    private List<List<StrokePoint>> ToDocument(IEnumerable<List<StrokePoint>> contours)
    {
        int dx = Scene.Left, dy = Scene.Top;
        return [.. contours.Select(c => dx == 0 && dy == 0
            ? new List<StrokePoint>(c)
            : [.. c.Select(pt => pt with { X = pt.X + dx, Y = pt.Y + dy })])];
    }

    private (string Id, ClipRegion Region)? PrepareClipForSelection()
    {
        if (!HasSelection) return null;
        var region = new ClipRegion
        {
            // The selection is kept as a surface mask, because that is what it
            // is; a clip region is part of the record, so it crosses here.
            Contours = ToDocument(_selectionContours),
            Feather = SelectionFeather,
        };
        var payload = System.Text.Json.JsonSerializer.Serialize(region);
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)))[..12];
        var id = $"clip_{hash.ToLowerInvariant()}";
        // The renderer resolves clips by id, so it must know this one before
        // the stroke's first re-render — not only after a document reload.
        ClipRegionRegistry.Register(id, region);
        return (id, region);
    }

    // Onion skin's settings live in AppSettings, not here and not in the
    // document: it is a drawing aid that never reaches pixels, and an
    // animator's depth and falloff are how they work rather than a property of
    // each scene. These forward, so the views and the existing tests keep the
    // names they had.

    public bool OnionSkin
    {
        get => Onion.Enabled;
        set => SetOnion(value, v => Onion.Enabled = v, Onion.Enabled);
    }

    /// <summary>Drawings shown behind and ahead. One number sets both.</summary>
    public int OnionDepth
    {
        get => Math.Max(Onion.Before, Onion.After);
        set
        {
            var depth = Math.Clamp(value, 0, 10);
            if (Onion.Before == depth && Onion.After == depth) return;
            Onion.Before = Onion.After = depth;
            AfterOnionChange();
            OnPropertyChanged(nameof(OnionBefore));
            OnPropertyChanged(nameof(OnionAfter));
        }
    }

    public int OnionBefore
    {
        get => Onion.Before;
        set => SetOnion(Math.Clamp(value, 0, 10), v => Onion.Before = v, Onion.Before, nameof(OnionDepth));
    }

    public int OnionAfter
    {
        get => Onion.After;
        set => SetOnion(Math.Clamp(value, 0, 10), v => Onion.After = v, Onion.After, nameof(OnionDepth));
    }

    public double OnionOpacity
    {
        get => Onion.Opacity;
        set => SetOnion(Math.Clamp(value, 0.02, 1), v => Onion.Opacity = v, Onion.Opacity);
    }

    public double OnionFalloff
    {
        get => Onion.Falloff;
        set => SetOnion(Math.Clamp(value, 0.05, 1), v => Onion.Falloff = v, Onion.Falloff);
    }

    public bool OnionKeysOnly
    {
        get => Onion.KeysOnly;
        set => SetOnion(value, v => Onion.KeysOnly = v, Onion.KeysOnly);
    }

    public bool OnionDrawOver
    {
        get => Onion.DrawOver;
        set => SetOnion(value, v => Onion.DrawOver = v, Onion.DrawOver);
    }

    public string OnionPreviousTint
    {
        get => Onion.PreviousTint;
        set => SetOnion(value, v => Onion.PreviousTint = v, Onion.PreviousTint);
    }

    public string OnionNextTint
    {
        get => Onion.NextTint;
        set => SetOnion(value, v => Onion.NextTint = v, Onion.NextTint);
    }

    public IReadOnlyList<Services.OnionMode> OnionModeChoices { get; } =
        Enum.GetValues<Services.OnionMode>();

    public Services.OnionMode OnionMode
    {
        get => Onion.Mode;
        set => SetOnion(value, v => Onion.Mode = v, Onion.Mode, nameof(IsLightTable));
    }

    /// <summary>
    /// A light table shows the other layers at this instant rather than this
    /// layer's own neighbours in time — a genuinely different question, and
    /// the reason it is a mode rather than another depth.
    /// </summary>
    public bool IsLightTable => Onion.Mode == Services.OnionMode.LightTable;

    private void SetOnion<T>(T value, Action<T> apply, T current, string? also = null,
        [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        apply(value);
        OnPropertyChanged(name);
        if (also is not null) OnPropertyChanged(also);
        AfterOnionChange();
    }

    /// <summary>
    /// Repaint, and remember. Onion settings persist across sessions because
    /// setting them up again every morning is the kind of small friction that
    /// makes a tool feel like it is not paying attention.
    /// </summary>
    private void AfterOnionChange()
    {
        InvalidateWholeCanvas();
        PublishSnapshot();
        Settings.Save();
    }

    // ---- pinned ghosts ------------------------------------------------------

    /// <summary>Whether the playhead's frame is pinned as a ghost.</summary>
    public bool CurrentFrameIsGhost =>
        Scene.GhostFrames?.Contains(CurrentFrameIndex) == true;

    public string GhostPinLabel => CurrentFrameIsGhost ? "Unpin ghost" : "Pin as ghost";

    /// <summary>
    /// Pin or unpin the playhead's frame, so it stays ghosted wherever the
    /// playhead goes — the two sheets you leave on the pegs while drawing the
    /// breakdown between them.
    /// </summary>
    [RelayCommand]
    public void ToggleGhostFrame()
    {
        var index = CurrentFrameIndex;
        var scene = Scene;
        var pinned = scene.GhostFrames ?? [];
        if (!pinned.Remove(index)) pinned.Add(index);
        // Absent unless used, so a document that never pins writes no key.
        scene.GhostFrames = pinned.Count > 0 ? pinned : null;
        NotifyGhostPins();
    }

    [RelayCommand]
    public void ClearGhostFrames()
    {
        if (Scene.GhostFrames is null) return;
        Scene.GhostFrames = null;
        NotifyGhostPins();
    }

    private void NotifyGhostPins()
    {
        OnPropertyChanged(nameof(CurrentFrameIsGhost));
        OnPropertyChanged(nameof(GhostPinLabel));
        OnPropertyChanged(nameof(HasGhostFrames));
        MarkDocumentEdited();
        InvalidateWholeCanvas();
        PublishSnapshot();
    }

    public bool HasGhostFrames => Scene.HasGhostFrames;

    // ---- stroke stabilizer (input smoothing) -----------------------------------

    private readonly StrokeStabilizer _stabilizer = new();

    /// <summary>
    /// The stabilisation a brush that carries none falls back to. Persisted
    /// with the rest of the brush state, which is where it already lived.
    /// </summary>
    private readonly BrushStabilisation _appStabilisation = new();

    public IReadOnlyList<SmoothingMode> SmoothingChoices { get; } = Enum.GetValues<SmoothingMode>();

    /// <summary>
    /// The settings the stabilizer should run with for the brush in hand.
    /// </summary>
    private BrushStabilisation EffectiveStabilisation =>
        CurrentToolSettings.Stabilisation ?? _appStabilisation;

    /// <summary>
    /// Does this brush steady the hand its own way, or follow the application?
    /// </summary>
    /// <remarks>
    /// Turning it on copies whatever is currently in effect, so ticking the box
    /// never changes how the brush draws — it only changes what the sliders are
    /// now editing. Turning it off drops the brush's copy and hands the sliders
    /// back to the application's, which is the way back to absent.
    /// </remarks>
    public bool BrushHasOwnStabilisation
    {
        get => CurrentToolSettings.Stabilisation is not null;
        set
        {
            if (value == BrushHasOwnStabilisation) return;
            CurrentToolSettings.Stabilisation = value ? EffectiveStabilisation.Clone() : null;
            OnPropertyChanged();
            NotifySmoothingProperties();
            NotifyPresetProperties();
            PersistBrushState();
        }
    }

    private void NotifySmoothingProperties()
    {
        OnPropertyChanged(nameof(SmoothingMode));
        OnPropertyChanged(nameof(SmoothingWindow));
        OnPropertyChanged(nameof(SmoothingStrength));
        OnPropertyChanged(nameof(LazyRadius));
        OnPropertyChanged(nameof(LazyRadiusForCursor));
        OnPropertyChanged(nameof(BrushHasOwnStabilisation));
    }

    // Each of these edits whichever settings are in effect — the brush's own
    // when it has them, the application's otherwise. One set of controls, and
    // the checkbox beside them says which they are pointed at.

    public SmoothingMode SmoothingMode
    {
        get => EffectiveStabilisation.Mode;
        set
        {
            if (EffectiveStabilisation.Mode == value) return;
            EffectiveStabilisation.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SmoothStrokes));
            OnPropertyChanged(nameof(IsWindowSmoothing));
            OnPropertyChanged(nameof(IsEmaSmoothing));
            OnPropertyChanged(nameof(IsPulledStringSmoothing));
            OnPropertyChanged(nameof(LazyRadiusForCursor));
            PersistBrushState();
        }
    }

    /// <summary>Compat view (old XAML/tests): on = the classic light smoothing.</summary>
    public bool SmoothStrokes
    {
        get => SmoothingMode != SmoothingMode.Off;
        set => SmoothingMode = value ? SmoothingMode.Laplacian : SmoothingMode.Off;
    }

    public bool IsWindowSmoothing =>
        SmoothingMode is SmoothingMode.MovingAverage or SmoothingMode.SavitzkyGolay;

    public bool IsEmaSmoothing => SmoothingMode == SmoothingMode.Ema;

    public bool IsPulledStringSmoothing => SmoothingMode == SmoothingMode.PulledString;

    public double SmoothingWindow
    {
        get => EffectiveStabilisation.Window;
        set
        {
            var window = Math.Clamp((int)Math.Round(value) | 1, 3, 25);
            if (EffectiveStabilisation.Window == window) return;
            EffectiveStabilisation.Window = window;
            OnPropertyChanged();
            PersistBrushState();
        }
    }

    public double SmoothingStrength
    {
        get => EffectiveStabilisation.Strength;
        set
        {
            var strength = Math.Clamp(value, 0, 0.95);
            if (Math.Abs(EffectiveStabilisation.Strength - strength) < 0.001) return;
            EffectiveStabilisation.Strength = strength;
            OnPropertyChanged();
            PersistBrushState();
        }
    }

    public double LazyRadius
    {
        get => EffectiveStabilisation.LazyRadius;
        set
        {
            var radius = Math.Clamp(value, 4, 200);
            if (Math.Abs(EffectiveStabilisation.LazyRadius - radius) < 0.5) return;
            EffectiveStabilisation.LazyRadius = radius;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LazyRadiusForCursor));
            PersistBrushState();
        }
    }

    /// <summary>Pulled-string dead-zone radius for the canvas gizmo (0 = hidden).</summary>
    public double LazyRadiusForCursor =>
        SmoothingMode == SmoothingMode.PulledString && IsPaintTool ? LazyRadius : 0;

    /// <summary>Raised while painting with a live smoothing mode: the smoothed brush anchor moved.</summary>
    public event Action<double, double>? LazyBrushMoved;

    /// <summary>Raised when the stroke ends and the gizmo anchor should clear.</summary>
    public event Action? LazyBrushCleared;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseGlyph))]
    private bool _isPlaying;

    /// <summary>
    /// Playback walks the sheet in order, which is the one access pattern an
    /// LRU is worst at — it evicts the frames at the start to make room for
    /// the ones at the end, so coming round the loop finds everything it is
    /// about to need has just been thrown away (B28). Evicting the most recent
    /// instead keeps the head of the sheet resident and turns a zero hit rate
    /// into about half.
    /// </summary>
    /// <remarks>
    /// Only for the duration of the scan. While drawing, the frames an artist
    /// returns to are the ones they touched last, which is the opposite
    /// prediction and the reason LRU is the default.
    /// </remarks>
    /// <summary>What the frame cache is currently evicting by. Test seam.</summary>
    internal FrameBitmapCache.EvictionOrder FrameCacheEviction => _cache.Eviction;

    partial void OnIsPlayingChanged(bool value)
    {
        _cache.Eviction = value
            ? FrameBitmapCache.EvictionOrder.MostRecent
            : FrameBitmapCache.EvictionOrder.LeastRecent;

        // Cleared at the START of a run rather than the end, so a report always
        // describes the last thing the artist actually watched. Accumulating
        // across runs would average a cold first pass with warm later ones,
        // which is exactly the distinction B148 exists to make.
        if (value)
        {
            _tickProfile.Reset();
            _cache.ResetCounters();
        }

        // B152: the layer thumbnails were skipped for the whole run, so they are
        // showing whatever frame playback started on. Catching up once on the
        // stop costs one sweep; catching up per tick cost the frame rate.
        if (!value) RefreshLayerThumbs();
    }

    [ObservableProperty]
    private int _activeLayerIndex;

    [ObservableProperty]
    private bool _sidebarVisible = true;

    // Which panels are open is the workspace's business now, not a set of
    // loose booleans. These stay because the View menu and a good deal of the
    // test suite already speak them, and forwarding is cheaper than renaming
    // both — but the layout is the single source of truth underneath.

    public bool ColorDockerVisible
    {
        get => Workspace.ColorDockerVisible;
        set => Workspace.ColorDockerVisible = value;
    }

    public bool SheetsDockerVisible
    {
        get => Workspace.SheetsDockerVisible;
        set => Workspace.SheetsDockerVisible = value;
    }

    public bool PaletteDockerVisible
    {
        get => Workspace.PaletteDockerVisible;
        set => Workspace.PaletteDockerVisible = value;
    }

    public bool GradientDockerVisible
    {
        get => Workspace.GradientDockerVisible;
        set => Workspace.GradientDockerVisible = value;
    }

    public bool ReferenceDockerVisible
    {
        get => Workspace.ReferenceDockerVisible;
        set => Workspace.ReferenceDockerVisible = value;
    }

    [RelayCommand]
    private void ToggleReferenceDocker() => ReferenceDockerVisible = !ReferenceDockerVisible;

    [RelayCommand]
    private void TogglePaletteDocker() => PaletteDockerVisible = !PaletteDockerVisible;

    [RelayCommand]
    private void ToggleGradientDocker() => GradientDockerVisible = !GradientDockerVisible;

    [RelayCommand]
    private void ToggleColorDocker() => ColorDockerVisible = !ColorDockerVisible;

    [RelayCommand]
    private void ToggleSheetsDocker() => SheetsDockerVisible = !SheetsDockerVisible;

    [RelayCommand]
    private void ToggleProjectPanel() =>
        Workspace.SetVisible(Docking.DockPanelId.Project, !Workspace.ProjectPanelVisible);

    [RelayCommand]
    private void ToggleSymbolsPanel() =>
        Workspace.SetVisible(Docking.DockPanelId.Symbols, !Workspace.SymbolsPanelVisible);

    [RelayCommand]
    private void ToggleLayersPanel() =>
        Workspace.SetVisible(Docking.DockPanelId.Layers, !Workspace.LayersPanelVisible);

    /// <summary>The toolbar's gear: always OPENS — a gear that closed the
    /// panel you were looking at would read as a broken button.</summary>
    [RelayCommand]
    private void OpenToolOptions() =>
        Workspace.SetVisible(Docking.DockPanelId.ToolOptions, true);

    [RelayCommand]
    private void ToggleToolOptionsDocker() =>
        Workspace.SetVisible(Docking.DockPanelId.ToolOptions, !Workspace.ToolOptionsDockerVisible);

    [RelayCommand]
    private void ToggleXsheetDocker() =>
        Workspace.SetVisible(Docking.DockPanelId.Xsheet, !Workspace.XsheetDockerVisible);

    [RelayCommand]
    private void ToggleGraphEditorDocker() =>
        Workspace.SetVisible(Docking.DockPanelId.GraphEditor, !Workspace.GraphEditorDockerVisible);

    /// <summary>Which side the docker sidebar collapses to / sits on.</summary>
    [ObservableProperty]
    private bool _sidebarOnRight = true;

    public bool TimelineVisible
    {
        get => Workspace.TimelineVisible;
        set => Workspace.TimelineVisible = value;
    }

    public ObservableCollection<Layer> LayerChoices { get; } = [];

    [ObservableProperty]
    private int _playbackSpeedPercent = 100;

    partial void OnPlaybackSpeedPercentChanged(int value)
    {
        var clamped = Math.Clamp(value, 10, 400);
        if (clamped != value)
        {
            PlaybackSpeedPercent = clamped;
            return;
        }
        if (IsPlaying)
        {
            _clock.Stop();
            _clock.Start(Scene.Fps, clamped);
        }
    }

    public int Fps
    {
        get => Scene.Fps;
        set
        {
            var fps = Math.Clamp(value, 1, 60);
            if (Scene.Fps == fps) return;
            Scene.Fps = fps;
            OnPropertyChanged();
            MarkDocumentEdited();
            if (IsPlaying)
            {
                _clock.Stop();
                _clock.Start(fps, PlaybackSpeedPercent);
            }
        }
    }


    partial void OnActiveLayerIndexChanged(int value)
    {
        // "Carry on from where I stopped" stops being true on another layer.
        _lastStrokeEnd = null;
        PruneStrokeSelection();   // and neither is a line picked on the old one
        foreach (var row in LayerRows) row.IsActive = row.SceneIndex == value;
        OnPropertyChanged(nameof(FrameCells));
        OnPropertyChanged(nameof(TimelineTracks));
        OnPropertyChanged(nameof(TimelineFrameCount));
        OnPropertyChanged(nameof(GraphSeriesList));
        OnPropertyChanged(nameof(ActiveLayerOnion));
        // A different layer can refuse the tool the last one accepted.
        RefreshPointerIntent();
        NotifyActiveLayerCompositing();
        PublishSnapshot();
    }

    [ObservableProperty]
    private int _tweenCount = 3;

    [ObservableProperty]
    private Easing _tweenEasing = Easing.EaseInOut;

    public IReadOnlyList<Easing> EasingChoices { get; } =
        [Easing.Linear, Easing.EaseIn, Easing.EaseOut, Easing.EaseInOut];

    /// <summary>Layer rows for the layer docker and the timeline, topmost layer first.</summary>
    public ObservableCollection<LayerRow> LayerRows { get; } = [];

    /// <summary>The active layer's timeline cells (topmost-first rows carry the rest).</summary>
    public ObservableCollection<FrameCell> FrameCells =>
        LayerRows[LayerRows.Count - 1 - Math.Clamp(ActiveLayerIndex, 0, LayerRows.Count - 1)].Cells;

    /// <summary>How many frames the track timeline spans.</summary>
    public int TimelineFrameCount => Scene.FrameCount;

    /// <summary>
    /// The graph editor's curves: the camera's framing when one exists, and
    /// the measured spacing of the active layer's drawings — the chart the
    /// stroke record makes possible (Q58). Same lifetime as
    /// <see cref="TimelineTracks"/>: a fresh list on every relevant change.
    /// </summary>
    public IReadOnlyList<Lightbox.App.Controls.GraphSeries> GraphSeriesList
    {
        get
        {
            var list = new List<Lightbox.App.Controls.GraphSeries>();
            var n = Scene.FrameCount;

            if (Scene.Camera is { } camera)
            {
                var x = new double[n];
                var y = new double[n];
                var zoom = new double[n];
                var rot = new double[n];
                for (var f = 0; f < n; f++)
                {
                    var framing = CameraOps.At(camera, f, Scene.Width, Scene.Height);
                    x[f] = framing.X;
                    y[f] = framing.Y;
                    zoom[f] = framing.Zoom;
                    rot[f] = framing.RotationDeg;
                }
                var keys = CameraKeyFrames;
                list.Add(new("Camera X", Avalonia.Media.Color.Parse("#FF9F45"), x, keys, Editable: true));
                list.Add(new("Camera Y", Avalonia.Media.Color.Parse("#E8C55F"), y, keys, Editable: true));
                list.Add(new("Zoom", Avalonia.Media.Color.Parse("#4FA3FF"), zoom, keys, Editable: true));
                list.Add(new("Rotation", Avalonia.Media.Color.Parse("#E85FBE"), rot, keys, Editable: true));
            }

            var spacing = new double[n];
            Array.Fill(spacing, double.NaN);
            var spans = Lightbox.Core.Timeline.SpacingChart.Measure(ActiveLayer);
            var spanFrames = new List<int>();
            foreach (var span in spans)
            {
                if (span.Frame >= n) continue;
                spacing[span.Frame] = span.Distance;
                spanFrames.Add(span.Frame);
            }
            list.Add(new("Spacing (measured)", Avalonia.Media.Color.Parse("#2FD1B9"),
                spacing, spanFrames, Editable: false));

            // The intent laid over the measurement: the same travel,
            // redistributed by the easing picked on the X-sheet bar. The gap
            // between the hollow dots and the filled ones is the drawing
            // that misses the ease.
            var intended = new double[n];
            Array.Fill(intended, double.NaN);
            var wantFrames = new List<int>();
            foreach (var span in Lightbox.Core.Timeline.SpacingChart.Intended(ActiveLayer, TweenEasing))
            {
                if (span.Frame >= n) continue;
                intended[span.Frame] = span.Distance;
                wantFrames.Add(span.Frame);
            }
            list.Add(new("Spacing (intended)", Avalonia.Media.Color.Parse("#8FE8DC"),
                intended, wantFrames, Editable: false, Dashed: true));

            SyncGraphLegend(list);
            return list.Where(s => !_hiddenGraphSeries.Contains(s.Name)).ToList();
        }
    }

    /// <summary>
    /// Series the artist switched off in the graph's legend. By name, which
    /// survives the projection rebuilding its lists on every change.
    /// </summary>
    private readonly HashSet<string> _hiddenGraphSeries = [];

    /// <summary>The legend's rows — stable instances, synced by name.</summary>
    public ObservableCollection<GraphLegendItem> GraphLegend { get; } = [];

    internal void SetGraphSeriesShown(string name, bool shown)
    {
        if (shown ? _hiddenGraphSeries.Remove(name) : _hiddenGraphSeries.Add(name))
        {
            OnPropertyChanged(nameof(GraphSeriesList));
        }
    }

    /// <summary>
    /// Keep the legend's rows matching the series that exist, without
    /// replacing instances — a toggle mid-click must not be swapped out from
    /// under the pointer.
    /// </summary>
    private void SyncGraphLegend(IReadOnlyList<Lightbox.App.Controls.GraphSeries> all)
    {
        for (var i = GraphLegend.Count - 1; i >= 0; i--)
        {
            if (all.All(s => s.Name != GraphLegend[i].Name)) GraphLegend.RemoveAt(i);
        }
        foreach (var s in all)
        {
            if (GraphLegend.All(l => l.Name != s.Name))
            {
                GraphLegend.Add(new GraphLegendItem(this, s.Name,
                    new Avalonia.Media.SolidColorBrush(s.Colour), !_hiddenGraphSeries.Contains(s.Name)));
            }
        }
    }

    /// <summary>
    /// The graph editor's dot drag, applied: retime the key when the frame
    /// changed (refusing an occupied destination), then write the dragged
    /// value into whichever channel the series names.
    /// </summary>
    public void EditCameraKey(string series, int fromFrame, int toFrame, double value)
    {
        if (Scene.Camera is not { } camera) return;
        if (CameraOps.KeyAt(camera, fromFrame) is not { } key) return;

        if (toFrame != fromFrame && CameraOps.KeyAt(camera, toFrame) is null)
        {
            key.Frame = toFrame;
        }
        switch (series)
        {
            case "Camera X": key.X = value; break;
            case "Camera Y": key.Y = value; break;
            case "Zoom": key.Zoom = Math.Clamp(value, 0.05, 32); break;
            case "Rotation": key.RotationDeg = value; break;
            default: return;
        }
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// The exposure sheet and the camera, projected for the track timeline:
    /// the camera on top (as the reference draws it), then the layers,
    /// topmost first. A fresh list every read — assigning it is what tells
    /// the TrackView to re-render, so it is raised wherever the cels or the
    /// camera change.
    /// </summary>
    public IReadOnlyList<Lightbox.App.Controls.TrackRow> TimelineTracks
    {
        get
        {
            var tracks = new List<Lightbox.App.Controls.TrackRow>();
            if (Scene.Camera is not null)
            {
                var frames = CameraKeyFrames;
                tracks.Add(new Lightbox.App.Controls.TrackRow(
                    "Camera", frames, frames, frames.Select(_ => false).ToList(), IsCamera: true));
            }
            foreach (var row in LayerRows)
            {
                var keys = new List<int>();
                var holdEnds = new List<int>();
                var breakdowns = new List<bool>();
                for (var i = 0; i < row.Cells.Count; i++)
                {
                    var cell = row.Cells[i];
                    if (!cell.IsKeyed || cell.IsVirtual) continue;
                    keys.Add(cell.Index);
                    breakdowns.Add(cell.IsBreakdown);
                    // The hold runs until the next drawing (or the sheet's end).
                    var end = cell.Index;
                    for (var j = i + 1; j < row.Cells.Count; j++)
                    {
                        if (row.Cells[j].IsKeyed || row.Cells[j].IsVirtual) break;
                        end = row.Cells[j].Index;
                    }
                    holdEnds.Add(end);
                }
                tracks.Add(new Lightbox.App.Controls.TrackRow(row.Name, keys, holdEnds, breakdowns, IsCamera: false));
            }
            return tracks;
        }
    }

    // ---- how big the timeline is ---------------------------------------------

    /// <summary>
    /// How wide one frame's cell is, in pixels.
    /// </summary>
    /// <remarks>
    /// Adjustable, because how many frames you want on screen at once depends
    /// entirely on what you are doing: laying out a two-hundred-frame scene
    /// wants them narrow enough to see the shape of the timing, and working a
    /// twelve-drawing cycle wants them wide enough to read the thumbnails. A
    /// preference rather than document data — it is how you are looking at the
    /// animation, not something about it.
    /// </remarks>
    public double TimelineFrameWidth
    {
        get => Math.Clamp(Settings.TimelineFrameWidth, 14, 72);
        set
        {
            var clamped = Math.Clamp(value, 14, 72);
            if (Math.Abs(TimelineFrameWidth - clamped) < 0.5) return;
            Settings.TimelineFrameWidth = clamped;
            Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimelineRulerCellWidth));
            OnPropertyChanged(nameof(TimelineThumbWidth));
        }
    }

    /// <summary>
    /// The ruler's pitch: a cell plus the gap after it.
    /// </summary>
    /// <remarks>
    /// Derived rather than set twice. The ruler numbers have to sit over the
    /// cells they name, and two independent constants is how they stop doing
    /// that the first time either one moves.
    /// </remarks>
    public double TimelineRulerCellWidth => TimelineFrameWidth + CellGap;

    private const double CellGap = 2;

    /// <summary>
    /// How tall a timeline row is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched to the layer rows beside it. They were 44 against the Layers
    /// docker's shorter rows, which made the two lists of the same layers read
    /// as two unrelated things.
    /// </para>
    /// <para>
    /// <b>26, matching the Layers docker again after the density retune.</b> The
    /// two lists show the same layers and have to be read across, so the number
    /// that matters is not this one on its own — it is that this one and a
    /// docker row agree. They had drifted to 28 against 33; both are 26 now,
    /// which is the icon tile with no padding either side.
    /// </para>
    /// <para>
    /// The floor is the thumbnail, not the row: <see cref="TimelineThumbHeight"/>
    /// is 16 and the cel needs a couple of pixels around it. <c>DESIGN.md</c>
    /// protects timeline cells from being shrunk — "a 12 px cell is a misdrop
    /// waiting to happen" — and 26 is nowhere near that. It is a scale entry
    /// rather than a free number, so it moves when the scale does.
    /// </para>
    /// </remarks>
    public double TimelineRowHeight => 26;

    public double TimelineThumbWidth => Math.Max(12, TimelineFrameWidth - 8);

    public double TimelineThumbHeight => 16;

    /// <summary>Cells shown per row: the real frames plus empty tail cells to insert into.</summary>
    public int TimelineExtent => Scene.FrameCount + VirtualTail;

    private const int VirtualTail = 24;

    /// <summary>Last frame the ruler may scrub to.</summary>
    public int MaxScrubFrame => Scene.FrameCount - 1;

    public string FrameLabel => $"{CurrentFrameIndex + 1} / {Scene.FrameCount}";

    partial void OnCurrentFrameIndexChanged(int value)
    {
        _lastStrokeEnd = null;   // and it stops being true on another drawing

        // Only while playing: the same path serves scrubbing, and blending a
        // budgeted path with an unbudgeted one makes every number mean nothing.
        var profiling = IsPlaying;
        if (profiling) _tickProfile.Tick();

        // A line selected on another drawing is not on this one. Left alone the
        // count keeps reporting lines nothing can show, which reads as the
        // arrow having stopped working.
        PruneStrokeSelection();

        using (Profile(profiling, Services.TickProfile.Phase.Highlights))
        {
            RefreshCellHighlights();
        }

        // B152: NOT while playing. The exposed frame changes on every tick, so
        // the id check below always misses and every tick rasterized a full
        // 1920x1080 frame per layer — from the stroke record, because the
        // compositor is reading TILES during playback and never fills this
        // cache — to make a 44x26 picture nobody is looking at. Measured at
        // 159 ms of mean tick lateness on a 16-core machine. OnIsPlayingChanged
        // catches them up when playback stops.
        // The scope sits INSIDE the guard, not around it. Around it, the phase
        // logs a zero-length call on every tick and the report says "0 ms over
        // 336 of 336 ticks" — which is true, unreadable, and indistinguishable
        // from a phase that ran and was cheap. Inside it, the phase genuinely
        // never runs and the report says so, which is the finding.
        if (!IsPlaying)
        {
            using (Profile(profiling, Services.TickProfile.Phase.Thumbnails))
            {
                RefreshLayerThumbs();
            }
        }
        using (Profile(profiling, Services.TickProfile.Phase.Bookkeeping))
        {
            RefreshCamera();
            // Whether THIS frame is pinned changes with the playhead, and the pin
            // button has to say which way it will go.
            OnPropertyChanged(nameof(CurrentFrameIsGhost));
            OnPropertyChanged(nameof(GhostPinLabel));
            // Which reference frame is showing, and therefore which cell the
            // alignment fields are editing, is a property of the playhead.
            NotifyReference();
        }
        using (Profile(profiling, Services.TickProfile.Phase.Audio))
        {
            ScrubAudioTick();
        }
        // A frame the clock has already dropped is not composited: the pixels
        // would be replaced before anything drew them, and making them is what
        // turned "catch up" into the reason it was behind (B162).
        if (_skippingFrame) return;

        // No scope around this one: PublishSnapshot times its own two halves as
        // siblings (Compose and Handoff), and a scope here would be their sum
        // counted a second time in ALL PHASES. The flag is how the method knows
        // it is on a playback tick and should measure at all — it is called on
        // every stroke too, where none of this applies.
        _profilingTick = profiling;
        try
        {
            PublishSnapshot();
        }
        finally
        {
            _profilingTick = false;
        }
    }

    /// <summary>
    /// True while a playback tick is running, so <c>PublishSnapshot</c> knows to
    /// time itself. It publishes on every stroke as well, and a report that
    /// folded drawing into the playback numbers would be measuring the wrong
    /// thing entirely.
    /// </summary>
    private bool _profilingTick;

    /// <summary>Where a playback tick's time went, for the render report.</summary>
    private readonly Services.TickProfile _tickProfile = new();

    internal Services.TickProfile TickProfile => _tickProfile;

    /// <summary>
    /// Time a phase, or do nothing at all when this is not a playback tick.
    /// </summary>
    /// <remarks>
    /// A nullable scope rather than a branch at each call site: the alternative
    /// is an <c>if</c> around every phase, and the one somebody forgets is the
    /// one that then reports a scrub as playback.
    /// </remarks>
    private Services.TickProfile.Scope? Profile(bool on, Services.TickProfile.Phase phase) =>
        on ? _tickProfile.Measure(phase) : null;


    // ---- painting -----------------------------------------------------------

    /// <summary>The keyed frame paint lands on (exposure-sheet: the key at or before the playhead).</summary>
    private Frame? PaintTarget()
    {
        var i = ExposureSheet.KeyIndexAtOrBefore(ActiveLayer, CurrentFrameIndex);
        return i < 0 ? null : ActiveLayer.Cels[i].Frame;
    }

    /// <summary>
    /// The frame a mark is about to land on, <b>keying the cel if there is
    /// nothing to land on</b>.
    ///
    /// Clearing every cel on a layer used to make it permanently undrawable:
    /// with no key at or before the playhead, <see cref="PaintTarget"/>
    /// returned null and every tool returned silently. Drawing where there is
    /// no drawing is the ordinary way to start one — every animation tool
    /// auto-keys on the first mark — and silence was the worst part of it.
    ///
    /// The new cel is a separate undo step from the stroke that prompted it,
    /// so one undo takes the mark back and a second takes the cel away.
    /// </summary>
    private Frame? PaintTargetOrKey()
    {
        if (ActiveLayer is not { } layer || layer.Cels.Count == 0) return null;
        var here = Math.Clamp(CurrentFrameIndex, 0, layer.Cels.Count - 1);
        // A cel that holds an earlier drawing is not a drawing of its own. What
        // happens when you mark on one is the single most consequential
        // decision the timeline makes, so it is a setting rather than a
        // hard-coded answer — see DrawingOnAHold.
        var holding = layer.Cels[here].Frame is null;
        if (!holding || DrawingOnAHold == HoldDrawing.EditTheHeldDrawing)
        {
            if (PaintTarget() is { } existing) return existing;
        }
        var index = here;
        var layerId = layer.Id;
        var fresh = new Frame();        // one class, so the layer's kind decides nothing here
        _editor.PerformDelta(
            apply: doc =>
            {
                if (CelIn(doc, layerId, index) is { } cel) cel.Frame = fresh;
            },
            revert: doc =>
            {
                if (CelIn(doc, layerId, index) is { } cel) cel.Frame = null;
            });
        return PaintTarget();
    }

    private static Cel? CelIn(Doc doc, string layerId, int index)
    {
        var layer = doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId);
        return layer is not null && index < layer.Cels.Count ? layer.Cels[index] : null;
    }

    /// <summary>Get placements from the current frame for selection feedback.</summary>
    public IReadOnlyList<SymbolPlacement>? GetCurrentFramePlacements()
    {
        if (PaintTargetOrKey() is Frame frame && frame.Placements is not null)
            return frame.Placements.AsReadOnly();
        return null;
    }

    /// <summary>One pointer sample in document space.</summary>
    public readonly record struct PointerSample(double X, double Y, double Pressure);

    /// <summary>
    /// Live report of what the OS delivers while drawing, shown on the
    /// Pen-pressure settings page. "Pen detected — pressure 0.63" means
    /// the tablet works; a Mouse report from a pen means the tablet driver
    /// isn't exposing the pen to Windows Ink (enable "Windows Ink" in the
    /// Huion/Wacom driver settings).
    /// </summary>
    [ObservableProperty]
    private string _penDiagnostic = "No input seen yet — draw a stroke.";

    // Live-preview state: a persistent copy of the target frame that only the
    // NEW segment of the stroke gets stamped into per pointer event — this is
    // what keeps painting O(stroke length) instead of O(length²).
    // Whole-stroke dab accumulator, WITHOUT the stroke's opacity — the
    // compositor lays it over the layer and applies opacity once, so a
    // self-crossing stroke looks the same live as committed. Pooled across
    // strokes: at 4K this bitmap is 33 MB, far too big to allocate per stroke.
    private SKBitmap? _liveScratch;
    private SKCanvas? _liveScratchCanvas;

    // Region of the scratch actually touched by the current stroke, so
    // pen-up can clear just that much instead of the whole canvas.
    private SKRectI? _liveScratchUsed;

    // Blur brushes read the canvas underneath them, so they cannot be
    // composited from a separate scratch — they keep the copy-based path.
    private SKBitmap? _liveComposite;

    /// <summary>
    /// The pristine pre-stroke pixels an effect brush samples, kept apart
    /// from the composite it writes into. See the note in BeginStroke.
    /// </summary>
    private SKBitmap? _liveEffectBase;
    private int _liveStampedCount;

    /// <summary>
    /// How many of the live stroke's dabs are already in the scratch.
    /// </summary>
    /// <remarks>
    /// Counted in <b>dabs</b>, not in points, and the two are not interchangeable: the
    /// walk emits a dab every <c>spacing × diameter</c> of arc length, so a slow pointer
    /// produces many points and few dabs and a fast one the reverse. This is the number
    /// that lets the engine walk the whole stroke and draw only what is new (B45).
    /// </remarks>
    private int _liveDabCount;

    /// <summary>
    /// The scratch pixels under the provisional tail, before it was stamped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the tail is drawn and then taken back rather than simply held until it
    /// settles.</b> A dab's position is provisional until the next point arrives, so
    /// stamping it immediately means stamping it in the wrong place; holding it back
    /// instead was tried and measured, and it costs too much — the live mark came out 4%
    /// short on a long stroke and <b>39% short on a six-event flick</b>, which reads as
    /// the stroke lagging behind the pen. An artist notices that far more than they would
    /// notice a settled dab.
    /// </para>
    /// <para>
    /// So the tail is stamped for the tip to be live, its region is remembered, and the
    /// next event restores those exact pixels before re-stamping. Restoring by copy makes
    /// it byte-exact, and because the stable count only ever grows, the dabs still land in
    /// index order — which is what keeps a self-crossing accumulating the same way it does
    /// in a single-pass render.
    /// </para>
    /// </remarks>
    private SKBitmap? _liveTailBackup;
    private SKRectI? _liveTailRegion;

    /// <summary>Dabs in the scratch whose position is settled, so never taken back.</summary>
    private int _liveStableDabs;

    /// <summary>
    /// The dab walk from the previous pointer event.
    /// </summary>
    /// <remarks>
    /// Kept for two reasons, both measured. It is what the new walk is compared against to
    /// find the settled prefix, which avoids walking the stroke a second time just to ask
    /// that question. And walking is not free: four walks an event made a 600-event stroke
    /// cost 3.2× more per event at the end than at the start, which invariant 6 forbids.
    /// </remarks>
    private List<BrushEngine.Dab>? _liveDabs;

    /// <summary>
    /// The densify cache for the stroke in hand (B46).
    /// </summary>
    /// <remarks>
    /// One per view model rather than one per stroke: it keys off the points it last saw, so a new
    /// stroke simply looks like a wholesale change and rebuilds. Kept alive between strokes so the
    /// common case allocates nothing.
    /// </remarks>
    private readonly Lightbox.Core.Geometry.IncrementalDensify _liveDensify = new();

    /// <summary>The effect draft's own dab bookkeeping, mirroring <see cref="_liveDabs"/>.</summary>
    /// <remarks>
    /// Separate because the two paths are exclusive — a stroke is either an effect or paint — and
    /// sharing one field would make whichever ran second compare against the other's walk. B54.
    /// </remarks>
    private List<BrushEngine.Dab>? _liveEffectDabs;

    /// <summary>How many effect dabs are already on the composite and must not be drawn again.</summary>
    private int _liveEffectSettled;

    /// <summary>
    /// What the smudge was carrying at dab <see cref="_liveEffectSettled"/>, so a resumed range
    /// starts where a single pass would have (B69/B89).
    /// </summary>
    /// <remarks>
    /// The blur needs no equivalent: its dabs read the pre-stroke pixels and are independent of one
    /// another. A smudge's are a chain, and this is the one link that has to survive between pointer
    /// events. It is two values, so checkpointing it every event costs nothing.
    /// </remarks>
    private BrushEngine.SmudgeCarry _liveSmudgeCarry;

    /// <summary>
    /// The composite's pixels under the provisional smudge tail, before it was stamped.
    /// </summary>
    /// <remarks>
    /// The same lend-and-take-back the paint scratch does in <see cref="StampLiveDabs"/>, for the
    /// same reason and with one extra: a smudge <em>reads</em> the bitmap it writes, so re-stamping
    /// an unsettled dab over its own previous deposit compounds the smear rather than replacing it.
    /// Restoring first is what makes the replayed range see what a single pass would have seen.
    /// </remarks>
    private SKBitmap? _liveSmudgeBackup;
    private SKRectI? _liveSmudgeRegion;

    private bool _snapshotQueued;

    // ---- live post-processing (medium, wet edge, texture, granulation) --------
    //
    // These are STROKE-GLOBAL: the wet edge is derived from the whole
    // silhouette and the fluid lattice flows across the whole wet area, so
    // running them per segment would rim and pool each segment separately —
    // visibly wrong, and not what commits. They have to be recomputed over the
    // whole stroke so far, which at 45–143 ms on a 4K canvas cannot happen on
    // every pointer event.
    //
    // So: raw dabs go into _liveScratch immediately (2 ms, the pen never
    // lags), and a full render of the stroke-so-far lands in _livePostScratch
    // as often as its own measured cost allows. The compositor shows the
    // rendered one when it exists. The artist sees the true mark converging a
    // fraction behind the tip rather than seeing flat dabs until pen-up, which
    // is how wet media behave in every tool that has them.
    private SKBitmap? _livePostScratch;
    private SKRectI? _livePostUsed;
    /// <summary>Cost of the last pass, milliseconds — reported by the performance panel.</summary>
    private double _livePostCostMs;
    private int _livePostStampedCount = -1;
    private bool _livePostQueued;

    /// <summary>How many times the live post-process has rendered. Tests only.</summary>
    internal int LivePostPasses { get; private set; }

    /// <summary>Total milliseconds spent in those passes. Tests only.</summary>
    internal double LivePostTotalMs { get; private set; }

    /// <summary>
    /// Effects that cannot be applied per segment because they read the whole
    /// stroke. Texture and granulation are pointwise and could be incremental,
    /// but they are cheap enough to come along for the ride.
    /// </summary>
    private static bool NeedsLivePostProcess(BrushSettings brush) =>
        brush.Medium.Kind != MediumKind.None
        || brush.WetEdge > 0
        || brush.TextureSurface is not null
        || brush.Granulation > 0;

    public void BeginStroke(double x, double y, double pressure) =>
        BeginStroke(x, y, pressure, eraseWithCurrentBrush: false);

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

    internal (double X, double Y)? LastStrokeEndForTests => _lastStrokeEnd;

    /// <param name="eraseWithCurrentBrush">
    /// Alt was held. The stroke erases but keeps the brush's own size, shape
    /// and dynamics — unlike switching to the eraser, which brings its own.
    /// </param>
    public void BeginStroke(double x, double y, double pressure, bool eraseWithCurrentBrush) =>
        BeginStroke(x, y, pressure, eraseWithCurrentBrush, joinFromLast: false);

    /// <param name="joinFromLast">
    /// Shift was held at the press. The stroke starts at the previous one's
    /// end and runs straight to here, which is how Photoshop draws a long
    /// straight without a ruler — and, chained, how a polyline gets drawn.
    /// </param>
    public void BeginStroke(
        double x, double y, double pressure, bool eraseWithCurrentBrush, bool joinFromLast)
    {
        // A mode that has taken the canvas takes it from every tool, not from
        // the ones the canvas control happens to route through itself. Half a
        // mark made while adjusting a grid is one you then have to find.
        if (SuppressesPainting) return;
        if (ActiveTool is not (ToolId.Brush or ToolId.Eraser)) return;
        if (IsPlaying) return;
        if (!CanEdit(ActiveLayer, "draw on it")) return;
        if (PaintTargetOrKey() is not { } target) return;
        // Drawing ends any run of palette edits, so the recolour lands on the
        // undo stack before the stroke does rather than after it.
        CommitSwatchEdit();
        // A stroke's guide is chosen once, from a direction it has committed
        // to. The anchor is where that direction is measured from, so it is
        // the unsnapped start — snapping the anchor first would measure the
        // heading from a point the hand never visited.
        _lockedGuide = null;
        _lockDecided = false;
        _strokeAnchor = (x, y);
        if (SnapToGuides && Scene.Guides is { Count: > 0 } startGuides)
        {
            (x, y) = Snapper.Point(startGuides, x, y, SnapTolerance);
            _strokeAnchor = (x, y);
        }
        // Shift+click: begin at the end of the last stroke and run straight to
        // the click. The segment is stamped now rather than on release, so the
        // mark is complete even if the artist never drags at all — which is
        // the whole gesture.
        var join = joinFromLast ? _lastStrokeEnd : null;
        var startX = join?.X ?? x;
        var startY = join?.Y ?? y;

        // Whichever brush is in hand decides how the hand is steadied, and it
        // is decided here rather than when a slider moves — switching brushes
        // mid-drawing has to change the smoothing with them.
        _stabilizer.Settings = EffectiveStabilisation;
        _stabilizer.Begin(startX, startY);
        _strokeBuilder.Begin(
            IsEraser || eraseWithCurrentBrush ? ToolKind.Eraser : ToolKind.Brush,
            ColorHex,
            CurrentToolSettings.Clone(),
            startX, startY, pressure,
            ActiveSwatchId);
        if (join is not null)
        {
            // Straight to the click, past the stabiliser: a segment the artist
            // asked to be straight must not be rounded off by smoothing.
            _strokeBuilder.Add(x, y, pressure);
            _strokeAnchor = (startX, startY);
            _lockDecided = true;   // it has a direction already; no guide may re-aim it
        }
        // Live preview clips to the selection too (the registry already knows
        // the region; the document copy is added at commit).
        if (PrepareClipForSelection() is { } liveClip) _strokeBuilder.Current!.ClipId = liveClip.Id;
        // Stamped onto the stroke, not read from the layer at render time, so
        // unlocking the layer later cannot repaint what is already down.
        _strokeBuilder.Current!.AlphaLocked = ActiveLayer.AlphaLocked;

        _liveComposite?.Dispose();
        _liveComposite = null;
        _liveEffectBase?.Dispose();
        _liveEffectBase = null;
        if (CurrentToolSettings.Kind is BrushKind.Blur or BrushKind.Smudge)
        {
            // Blur and smudge read the pixels they sit on, so they need a real
            // copy of the layer to work into. Without this a smudge preview
            // stamps plain dabs of the foreground colour for the whole drag
            // and only snaps to the real smear on pen-up.
            //
            // Two copies, not one, and the second is the fix for B33. The
            // composite is written into; the base is never written and is what
            // every dab reads. The exact render gives all of a stroke's dabs
            // the same pre-stroke pixels, so a preview that sampled the
            // composite would re-apply the effect once per pointer event — a
            // blur of a blur of a blur, forty deep by the end of a drag.
            _liveEffectBase = _cache.Get(target, Scene.Width, Scene.Height).Copy();
            _liveComposite = _liveEffectBase.Copy();
        }
        else
        {
            EnsureLiveScratch();
            ClearLiveScratch();
        }
        ResetLivePostProcess();
        _liveStampedCount = 0;
        _liveDabCount = 0;
        _liveStableDabs = 0;
        _liveTailRegion = null;
        _liveDabs = null;
        _liveEffectDabs = null;
        _liveEffectSettled = 0;
        _liveSmudgeCarry = default;
        _liveSmudgeRegion = null;
        FlushLivePreview();
        PublishSnapshot();
    }

    /// <summary>A document-sized scratch bitmap for the live preview overlay.</summary>
    private void EnsureLiveScratch()
    {
        if (_liveScratch is not null && _liveScratch.Width == Scene.Width && _liveScratch.Height == Scene.Height)
        {
            return;
        }
        _liveScratchCanvas?.Dispose();
        _liveScratch?.Dispose();
        _liveScratch = new SKBitmap(
            new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        _liveScratchCanvas = new SKCanvas(_liveScratch);
        _liveScratchUsed = null;
    }

    /// <summary>Wipe only the region the previous stroke actually touched.</summary>
    private void ClearLiveScratch()
    {
        if (_liveScratchCanvas is null) return;
        if (_liveScratchUsed is not { } used)
        {
            _liveScratchCanvas.Clear(SkiaSharp.SKColors.Transparent);
            return;
        }
        _liveScratchCanvas.Save();
        _liveScratchCanvas.ClipRect(SKRect.Create(used.Left, used.Top, used.Width, used.Height));
        _liveScratchCanvas.Clear(SkiaSharp.SKColors.Transparent);
        _liveScratchCanvas.Restore();
        _liveScratchUsed = null;
    }

    // ---- gradient tool ------------------------------------------------------

    /// <summary>
    /// The gradient being dragged. Two points — the axis — and no incremental
    /// state, so it stays out of the brush's stamped-so-far machinery: a
    /// gradient is not built up along the drag, it is redefined by it.
    /// </summary>
    private Stroke? _liveGradient;

    /// <summary>The axis the canvas overlay draws while dragging (document coordinates).</summary>
    public event Action<(double X, double Y)?, (double X, double Y)?>? GradientAxisChanged;

    internal Stroke? LiveGradient => _liveGradient;

    internal Stroke? LiveGradientForTests => _liveGradient;

    internal Stroke? LiveShapeForTests => _liveShape;

    /// <summary>
    /// Whether anything being dragged right now would actually reach the
    /// screen.
    /// </summary>
    /// <remarks>
    /// The condition the compositor tests, named once so it can be asserted.
    /// A tool that renders a preview nothing composites looks correct at every
    /// call site and shows nothing, which is how the shape tool shipped.
    /// </remarks>
    internal bool LivePreviewIsVisible =>
        _liveScratch is not null
        && (_liveShape is not null || _liveGradient is not null || _strokeBuilder.IsActive);

    public void BeginGradient(double x, double y)
    {
        if (ActiveTool != ToolId.Gradient || IsPlaying) return;
        if (!CanEdit(ActiveLayer, "fill on it") || PaintTargetOrKey() is null) return;
        // A brand-new document has no gradients, and telling someone who just
        // picked the gradient tool to go and make one first is a dead end. A
        // fresh Gradient is already black to white, which is the ramp anyone
        // would have made by hand.
        if (GradientDocker.SelectedGradient is null) GradientDocker.AddGradientCommand.Execute(null);
        if (GradientDocker.SelectedGradient is not { } gradient)
        {
            AiStatus = "Could not create a gradient to paint with.";
            return;
        }
        CommitSwatchEdit();

        _liveGradient = new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = gradient.Id,
            Color = ColorHex,
            Brush = new BrushSettings { Opacity = GradientOpacity, AntiAlias = AntiAliasing },
            Points = [new StrokePoint(x, y, 1), new StrokePoint(x, y, 1)],
            // Stamped onto the stroke like a brush stroke's, so unlocking the
            // layer later cannot repaint what is already down.
            AlphaLocked = ActiveLayer.AlphaLocked,
            Label = "gradient",
        };
        if (PrepareClipForSelection() is { } clip) _liveGradient.ClipId = clip.Id;

        EnsureLiveScratch();
        RenderGradientPreview();
        PublishSnapshot();
    }

    /// <param name="snapAngle">
    /// Shift. A gradient's angle is the whole of it, and a ramp meant to be
    /// level almost never lands level by hand.
    /// </param>
    public void MoveGradient(double x, double y, bool snapAngle = false)
    {
        if (_liveGradient is not { } stroke) return;
        stroke.Points[1] = GradientEnd(stroke, x, y, snapAngle);
        RenderGradientPreview();
        RequestSnapshot();
    }

    /// <summary>
    /// How far apart the snapped angles are, in degrees.
    /// </summary>
    /// <remarks>
    /// Fifteen, so the four squares and the four diagonals are all on it and
    /// there is still somewhere to put an angle between them. The same number
    /// the guide lock uses, for the same reason.
    /// </remarks>
    public const double GradientSnapDegrees = 15;

    private static StrokePoint GradientEnd(Stroke stroke, double x, double y, bool snapAngle)
    {
        if (!snapAngle) return new StrokePoint(x, y, 1);
        var ax = stroke.Points[0].X;
        var ay = stroke.Points[0].Y;
        var dx = x - ax;
        var dy = y - ay;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) return new StrokePoint(x, y, 1);
        // The angle snaps; the length does not. Same division of labour as a
        // ruler — the guide decides the direction, the hand decides how far.
        var degrees = Math.Atan2(dy, dx) * 180 / Math.PI;
        var snapped = Math.Round(degrees / GradientSnapDegrees) * GradientSnapDegrees * Math.PI / 180;
        return new StrokePoint(ax + Math.Cos(snapped) * length, ay + Math.Sin(snapped) * length, 1);
    }

    public void EndGradient(double x, double y, bool snapAngle = false)
    {
        if (_liveGradient is not { } stroke) return;
        stroke.Points[1] = GradientEnd(stroke, x, y, snapAngle);
        CancelGradient(); // clears the preview; the record gets the stroke below

        if (PaintTarget() is not { } target) return;
        var dx = stroke.Points[1].X - stroke.Points[0].X;
        var dy = stroke.Points[1].Y - stroke.Points[0].Y;
        // A click with no drag has no axis. Committing it would paint a
        // degenerate shader over the whole layer, which is never the intent.
        if (dx * dx + dy * dy < 1.0)
        {
            AiStatus = "Drag to set the gradient's direction and length.";
            return;
        }

        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;

        FreezeSampledBackdrop(stroke);
        RememberDocumentBrush();
        AppendToFrameRender(target, stroke);

        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                    StrokeListIn(doc, frameId)?.Add(stroke);
                },
                revert: doc =>
                {
                    RemoveStrokeById(doc, frameId, stroke.Id);
                    if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        AiStatus = $"Laid down “{GradientDocker.SelectedGradient?.Name}”.";
    }

    /// <summary>Abandon the drag — Escape, or capture lost.</summary>
    // ---- the shape tool ------------------------------------------------------------

    /// <summary>Which shape the shape tool draws.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPolygonShape))]
    [NotifyPropertyChangedFor(nameof(ShapeGlyph))]
    private ShapeKind _activeShape = ShapeKind.Rectangle;

    /// <summary>Corners, when the shape is a polygon.</summary>
    [ObservableProperty]
    private int _polygonSides = 5;

    public bool IsPolygonShape => ActiveShape == ShapeKind.Polygon;

    /// <summary>The tool button's icon, so it says which shape is loaded.</summary>
    /// <remarks>
    /// A geometry from <see cref="Rendering.IconSet"/> rather than the box-drawing
    /// characters this used to return. Those were the system font's idea of a
    /// rectangle sitting next to eight hand-drawn monoline glyphs, at whatever
    /// weight and baseline that font happened to have.
    /// </remarks>
    public Avalonia.Media.Geometry? ShapeGlyph =>
        Rendering.IconSet.Resolve(Rendering.IconSet.ForShape(ActiveShape));

    /// <summary>Pick a shape, and make the shape tool active while you are at it.</summary>
    /// <remarks>
    /// Same bargain as the select variants: choosing one from the hold-list is
    /// a statement that you want to draw it, and making you click the tool
    /// again afterwards is a step with no decision in it.
    /// </remarks>
    [RelayCommand]
    private void SelectShape(ShapeKind kind)
    {
        ActiveShape = kind;
        ActiveTool = ToolId.Shape;
    }

    public IReadOnlyList<ShapeKind> ShapeChoices { get; } =
        [ShapeKind.Line, ShapeKind.Rectangle, ShapeKind.Ellipse, ShapeKind.Polygon];

    public bool IsShapeTool => ActiveTool == ToolId.Shape;

    private Stroke? _liveShape;

    private (double X, double Y) _shapeStart;

    /// <summary>
    /// Start a shape.
    /// </summary>
    /// <remarks>
    /// The corners are snapped like any other point, so a rectangle dropped on
    /// a grid lands on the grid — which is most of why anybody turns a grid on.
    /// </remarks>
    public void BeginShape(double x, double y)
    {
        if (ActiveTool != ToolId.Shape || IsPlaying) return;
        if (!CanEdit(ActiveLayer, "draw on it") || PaintTargetOrKey() is null) return;
        CommitSwatchEdit();

        if (SnapToGuides && Scene.Guides is { Count: > 0 } guides)
        {
            (x, y) = Snapper.Point(guides, x, y, SnapTolerance);
        }
        _shapeStart = (x, y);
        _liveShape = new Stroke
        {
            Tool = IsEraser ? ToolKind.Eraser : ToolKind.Brush,
            Color = ColorHex,
            SwatchId = ActiveSwatchId,
            PaletteId = ActivePaletteId,
            Brush = CurrentToolSettings.Clone(),
            Points = ShapeBuilder.Outline(ActiveShape, x, y, x, y, sides: PolygonSides),
            AlphaLocked = ActiveLayer.AlphaLocked,
            Label = ActiveShape.ToString().ToLowerInvariant(),
        };
        if (PrepareClipForSelection() is { } clip) _liveShape.ClipId = clip.Id;

        EnsureLiveScratch();
        RenderShapePreview();
        PublishSnapshot();
    }

    /// <param name="fromCentre">Alt: grow from the first corner rather than to it.</param>
    /// <param name="regular">Shift: a square, a circle, a regular polygon.</param>
    public void MoveShape(double x, double y, bool fromCentre = false, bool regular = false)
    {
        if (_liveShape is not { } stroke) return;
        if (SnapToGuides && Scene.Guides is { Count: > 0 } guides)
        {
            (x, y) = Snapper.Point(guides, x, y, SnapTolerance);
        }
        stroke.Points = ShapeBuilder.Outline(
            ActiveShape, _shapeStart.X, _shapeStart.Y, x, y, fromCentre, regular, PolygonSides);
        RenderShapePreview();
        RequestSnapshot();
    }

    public void EndShape(double x, double y, bool fromCentre = false, bool regular = false)
    {
        if (_liveShape is not { } stroke) return;
        if (SnapToGuides && Scene.Guides is { Count: > 0 } guides)
        {
            (x, y) = Snapper.Point(guides, x, y, SnapTolerance);
        }
        stroke.Points = ShapeBuilder.Outline(
            ActiveShape, _shapeStart.X, _shapeStart.Y, x, y, fromCentre, regular, PolygonSides);
        CancelShape();

        if (PaintTarget() is not { } target) return;
        // A click with no drag is not a shape. Committing it would leave a
        // single dab where the artist expected a rectangle.
        var dx = x - _shapeStart.X;
        var dy = y - _shapeStart.Y;
        if (dx * dx + dy * dy < 1.0)
        {
            AiStatus = "Drag to size the shape.";
            return;
        }

        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;
        FreezeSampledBackdrop(stroke);
        RememberDocumentBrush();
        AppendToFrameRender(target, stroke);

        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                    StrokeListIn(doc, frameId)?.Add(stroke);
                },
                revert: doc =>
                {
                    RemoveStrokeById(doc, frameId, stroke.Id);
                    if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        InvalidateWholeCanvas();
        PublishSnapshot();
    }

    public void CancelShape()
    {
        if (_liveShape is null) return;
        _liveShape = null;
        ClearLiveScratch();
        InvalidateWholeCanvas();
        PublishSnapshot();
    }

    private void RenderShapePreview()
    {
        if (_liveShape is not { } stroke || _liveScratchCanvas is null) return;
        ClearLiveScratch();
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // The same stroke the commit will record, at full opacity — the
        // overlay applies the brush's own, so baking it here would double it.
        var preview = new Stroke
        {
            Tool = ToolKind.Brush,
            Color = stroke.Color,
            SwatchId = stroke.SwatchId,
            PaletteId = stroke.PaletteId,
            ClipId = stroke.ClipId,
            Brush = stroke.Brush.Clone(),
            Points = [.. stroke.Points],
        };
        preview.Brush.Opacity = 1;
        BrushEngine.StampStroke(_liveScratchCanvas, preview, info);
        _liveScratchCanvas.Flush();
        _liveScratchUsed = new SKRectI(0, 0, Scene.Width, Scene.Height);
        InvalidateWholeCanvas();
    }

    public void CancelGradient()
    {
        if (_liveGradient is null) return;
        _liveGradient = null;
        ClearLiveScratch();
        GradientAxisChanged?.Invoke(null, null);
        InvalidateWholeCanvas();
        PublishSnapshot();
    }

    /// <summary>
    /// Re-render the whole preview rather than an increment. A gradient is
    /// full-canvas by nature — one shader-filled rect, which Skia does in a
    /// single native pass — and every pointer move redefines the axis, so
    /// there is nothing from the previous frame worth keeping.
    /// </summary>
    private void RenderGradientPreview()
    {
        if (_liveGradient is not { } stroke || _liveScratchCanvas is null) return;
        ClearLiveScratch();
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // Opacity and the alpha lock stay on the overlay so they are not baked
        // in twice; the scratch holds the unmodulated ramp.
        var preview = new Stroke
        {
            Tool = ToolKind.Gradient,
            GradientId = stroke.GradientId,
            Color = stroke.Color,
            ClipId = stroke.ClipId,
            Brush = new BrushSettings { Opacity = 1, AntiAlias = stroke.Brush.AntiAlias },
            Points = [.. stroke.Points],
        };
        BrushEngine.StampStroke(_liveScratchCanvas, preview, info);
        _liveScratchCanvas.Flush();
        _liveScratchUsed = new SKRectI(0, 0, Scene.Width, Scene.Height);
        GradientAxisChanged?.Invoke(
            (stroke.Points[0].X, stroke.Points[0].Y), (stroke.Points[1].X, stroke.Points[1].Y));
        InvalidateWholeCanvas();
    }

    /// <summary>All coalesced samples of one pointer event → one stamp + one (coalesced) repaint.</summary>
    public void MoveStrokeBatch(IReadOnlyList<PointerSample> samples)
    {
        if (!_strokeBuilder.IsActive) return;
        foreach (var s in samples)
        {
            var (fx, fy) = _stabilizer.FilterLive(s.X, s.Y);
            var (x, y) = Guided(fx, fy);
            _strokeBuilder.Add(x, y, s.Pressure);
        }
        if (_stabilizer.BrushPosition is { } anchor) LazyBrushMoved?.Invoke(anchor.X, anchor.Y);
        FlushLivePreview();
        RequestSnapshot();
    }

    public void MoveStroke(double x, double y, double pressure) =>
        MoveStrokeBatch([new PointerSample(x, y, pressure)]);

    /// <summary>
    /// Stamp the dabs of the live stroke that are not in the preview yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole stroke goes to the engine every time, and only the new dabs are
    /// drawn.</b> This used to hand over a two-point <c>tail</c> instead, which was the
    /// cause of B45: the dab walk carries a spacing phase, a travelled distance, a
    /// heading and a step size, and a tail restarts all four. An artist saw that as the
    /// mark changing the instant they let go — denser live than committed, paint load that
    /// only depleted on release, and a tip texture that jumped because dabs landing
    /// elsewhere are seeded elsewhere.
    /// </para>
    /// <para>
    /// The tail survives for <em>bounds</em>, which is a different question: what changed
    /// on screen since the last event is genuinely the segment, and repainting the whole
    /// stroke's region every event would break invariant 6.
    /// </para>
    /// </remarks>
    private void FlushLivePreview()
    {
        if (_strokeBuilder.Current is not { } live) return;
        var points = live.Points;
        if (_liveStampedCount >= points.Count) return;

        var from = Math.Max(0, _liveStampedCount - 1); // overlap one point so segments connect
        var tail = new Stroke
        {
            Tool = live.Tool,
            Color = live.Color,
            SwatchId = live.SwatchId,
            Brush = live.Brush,
            ClipId = live.ClipId,
            AlphaLocked = live.AlphaLocked,
            Points = points.Skip(from).ToList(),
        };
        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var segment = BrushEngine.DraftSegmentBounds(tail, info);

        if (_liveComposite is not null)
        {
            // The pristine base is for BLUR only, and the distinction is the
            // physics rather than a detail. A blur re-derives its dab from the
            // pre-stroke pixels, so every dab of a stroke must see the same
            // source or the effect compounds once per pointer event (B33). A
            // smudge *carries*: each dab has to see the deposits the previous
            // ones left, which is how a smear travels at all — the committed
            // path gives it the very bitmap it is writing, and the draft has to
            // match or the preview stops matching the commit.
            // The whole stroke, walked with the shared densify cache, so the dabs land where the
            // commit will put them — a per-segment walk restarts the spacing phase and Densify sees
            // two points, which was 1148 px of over-coverage on the blur (B54) and the same defect
            // on the smudge (B69/B89). Only the dabs that are not settled yet are stamped.
            var walk = BrushEngine.WalkDabs(live, _liveDensify);
            var settled = BrushEngine.StableCount(walk, _liveEffectDabs);

            if (live.Brush.Kind == BrushKind.Blur)
            {
                // A blur's dabs are independent — each reads the pre-stroke pixels — so the range
                // is all it needs, and StampBlurDraft restores under the tail itself.
                FrameRasterizer.AppendDraft(_liveComposite, live, _liveEffectBase, walk, _liveEffectSettled);
            }
            else
            {
                StampLiveSmudge(live, walk, settled);
            }

            _liveEffectSettled = settled;
            _liveEffectDabs = walk;
        }
        else if (_liveScratchCanvas is not null)
        {
            // Dabs only — no opacity, no layer copy. The compositor lays the
            // scratch over the layer and applies the stroke's opacity once,
            // so self-crossings look identical live and committed.
            //
            // The whole stroke, with the dabs already in the scratch skipped: the walk
            // has to run from the start for its phase, travel and heading to match the
            // commit, and only the drawing is incremental.
            StampLiveDabs(live, info);
            if (segment is { } used)
            {
                _liveScratchUsed = _liveScratchUsed is { } prior ? UnionRect(prior, used) : used;
            }
        }
        // Only the segment's neighbourhood changed on screen.
        if (segment is { } rect) MarkDirtyRegion(rect);
        else InvalidateWholeCanvas();
        _liveStampedCount = points.Count;

        if (_liveComposite is null && NeedsLivePostProcess(live.Brush)) RequestLivePostProcess();
    }

    /// <summary>
    /// Bring the smudge composite up to date: settled dabs permanently, the provisional tail on
    /// loan, and the carried colour checkpointed at the boundary between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B69/B89.</b> This used to be a per-segment <c>AppendDraft</c>, which restarted the dab
    /// walk's spacing phase, the carried colour and the heading on every pointer event, and
    /// re-smeared the one-point overlap where segments join. All four differences appeared at once
    /// when the pen lifted and the whole stroke was re-rendered from the record.
    /// </para>
    /// <para>
    /// The three steps are ordered exactly as <see cref="StampLiveDabs"/>'s are, and for the same
    /// reason — dabs have to reach the surface in index order for the accumulation to match a
    /// single pass. Take back the old tail, extend the settled prefix, lend the new tail.
    /// </para>
    /// <para>
    /// <b>Why this is exact rather than merely closer.</b> After the restore the composite holds
    /// precisely "pre-stroke pixels + dabs 0..settled-1", and <see cref="_liveSmudgeCarry"/> is the
    /// colour a single pass would be carrying at that index. Replaying the rest is then the same
    /// sequence the commit runs. Reads that reach outside the restored region — a smudge samples up
    /// to <c>radius × SmudgeRadius</c> away — can only touch settled pixels, which are already
    /// final.
    /// </para>
    /// <para>
    /// <b>And why not simply replay every dab each event</b>, which is exact by construction: the
    /// blur measured that at 194 ms an event by the 300th point against 20.8 ms for the settled
    /// range, and <c>LerpDab</c> is a per-pixel loop with the same shape of cost. Invariant 6 says
    /// no.
    /// </para>
    /// </remarks>
    private void StampLiveSmudge(Stroke live, IReadOnlyList<BrushEngine.Dab> dabs, int settled)
    {
        if (_liveComposite is not { } composite) return;
        var info = new SKImageInfo(
            composite.Width, composite.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(composite);

        // 1. Take back the tail lent out last time, so the composite is the settled prefix again.
        //    Only the part of the buffer that tail used — the backup is sized to the largest seen,
        //    so drawing all of it would scale a bigger image into a smaller rect.
        if (_liveSmudgeRegion is { } lent && _liveSmudgeBackup is not null)
        {
            using var restore = SKImage.FromBitmap(_liveSmudgeBackup);
            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
            canvas.DrawImage(
                restore,
                new SKRect(0, 0, lent.Width, lent.Height),
                new SKRect(lent.Left, lent.Top, lent.Right, lent.Bottom),
                src);
            canvas.Flush();
            _liveSmudgeRegion = null;
        }

        // 2. Everything whose position has stopped moving, permanently — and the carry it ends on
        //    becomes the checkpoint the next event resumes from.
        if (settled > _liveEffectSettled)
        {
            _liveSmudgeCarry = BrushEngine.StampSmudgeRange(
                canvas, composite, live, dabs, _liveEffectSettled, settled, _liveSmudgeCarry);
        }

        // 3. The rest on loan, so the smear reaches the pen tip. Backed up first, because a smudge
        //    reads what it writes: re-stamping these next event without taking them back would
        //    compound the smear instead of replacing it.
        if (BrushEngine.RangeBounds(dabs, settled, live.Brush, info) is { } tail)
        {
            canvas.Flush();
            if (_liveSmudgeBackup is null
                || _liveSmudgeBackup.Width < tail.Width || _liveSmudgeBackup.Height < tail.Height)
            {
                _liveSmudgeBackup?.Dispose();
                _liveSmudgeBackup = new SKBitmap(new SKImageInfo(
                    Math.Max(tail.Width, 64), Math.Max(tail.Height, 64),
                    SKColorType.Rgba8888, SKAlphaType.Premul));
            }
            // A real copy, not a subset view: SKBitmap.ExtractSubset SHARES the source's pixels, so
            // using it as the backup would make it track the composite and the rollback a no-op.
            // The subset is taken first and only then wrapped, so no full-canvas SKImage is built.
            using (var region = new SKBitmap())
            {
                if (composite.ExtractSubset(region, tail))
                {
                    using var pixels = region.PeekPixels();
                    using var view = pixels is null ? null : SKImage.FromPixels(pixels);
                    if (view is not null)
                    {
                        using var into = new SKCanvas(_liveSmudgeBackup);
                        using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                        into.DrawImage(view, 0, 0, src);
                        into.Flush();
                        _liveSmudgeRegion = tail;
                    }
                }
            }

            // The returned carry is deliberately dropped: these dabs are provisional, so the
            // checkpoint must stay at the settled boundary.
            BrushEngine.StampSmudgeRange(
                canvas, composite, live, dabs, settled, dabs.Count, _liveSmudgeCarry);
            canvas.Flush();
        }
    }

    /// <summary>
    /// Bring the scratch up to date with the stroke: settled dabs permanently, the
    /// provisional tail on loan.
    /// </summary>
    /// <remarks>
    /// The three steps are ordered the way they are because the dabs have to reach the
    /// scratch in index order for the accumulation to match a single-pass render: take
    /// back the old tail, add whatever became settled, then lend the new tail.
    /// </remarks>
    private void StampLiveDabs(Stroke live, SKImageInfo info)
    {
        if (_liveScratchCanvas is null) return;

        // One walk, then every question answered from its result.
        // The walk reuses the densified prefix rather than rebuilding it, which is B46: the whole
        // stroke has to be walked every pointer event (BR1) and re-densifying it was 0.84 ms of a
        // 1.15 ms walk at 600 points, all but a fraction of it recomputing spans that cannot have
        // changed.
        var dabs = BrushEngine.WalkDabs(live, _liveDensify);
        var stable = BrushEngine.StableCount(dabs, _liveDabs);
        _liveDabs = dabs;

        // 1. Take back the tail lent out last time. Only the part of the buffer this
        // tail actually used: the backup is sized to the largest tail seen, so drawing
        // the whole thing would scale a bigger image into a smaller rect.
        if (_liveTailRegion is { } lent && _liveTailBackup is not null)
        {
            using var restore = SKImage.FromBitmap(_liveTailBackup);
            using var src = new SKPaint { BlendMode = SKBlendMode.Src };
            _liveScratchCanvas.DrawImage(
                restore,
                new SKRect(0, 0, lent.Width, lent.Height),
                new SKRect(lent.Left, lent.Top, lent.Right, lent.Bottom),
                src);
            _liveScratchCanvas.Flush();
            _liveTailRegion = null;
        }

        // 2. Everything whose position has stopped moving, permanently.
        BrushEngine.StampDabRange(_liveScratchCanvas, live, dabs, _liveStableDabs, stable);
        _liveStableDabs = Math.Max(_liveStableDabs, Math.Min(stable, dabs.Count));

        // 3. The rest on loan, so the mark reaches the pen tip.
        if (BrushEngine.RangeBounds(dabs, _liveStableDabs, live.Brush, info) is { } tail
            && _liveScratch is not null)
        {
            _liveScratchCanvas.Flush();
            if (_liveTailBackup is null
                || _liveTailBackup.Width < tail.Width || _liveTailBackup.Height < tail.Height)
            {
                _liveTailBackup?.Dispose();
                _liveTailBackup = new SKBitmap(new SKImageInfo(
                    Math.Max(tail.Width, 64), Math.Max(tail.Height, 64),
                    SKColorType.Rgba8888, SKAlphaType.Premul));
            }
            // A real copy, not a subset view. SKBitmap.ExtractSubset hands back a bitmap
            // that SHARES the source's pixels, so using it as the backup made it track the
            // scratch and the rollback a no-op — the tail accumulated instead of being
            // taken back, which measured as the live mark 9% heavier than the commit.
            //
            // The subset is taken FIRST and only then wrapped, which is the same trap
            // PostProcessDabs records: SKImage.FromBitmap on the whole scratch sets up a
            // 33 MB image at 4K, every pointer event, to read back a region a few hundred
            // pixels across.
            using (var region = new SKBitmap())
            {
                if (_liveScratch.ExtractSubset(region, tail))
                {
                    using var pixels = region.PeekPixels();
                    using var view = pixels is null ? null : SKImage.FromPixels(pixels);
                    if (view is not null)
                    {
                        using var into = new SKCanvas(_liveTailBackup);
                        using var src = new SKPaint { BlendMode = SKBlendMode.Src };
                        into.DrawImage(view, 0, 0, src);
                        into.Flush();
                        _liveTailRegion = tail;
                    }
                }
            }

            BrushEngine.StampDabRange(_liveScratchCanvas, live, dabs, _liveStableDabs, dabs.Count);
            _liveScratchCanvas.Flush();
        }
        _liveDabCount = dabs.Count;
    }

    /// <summary>
    /// Ask for a re-render of the stroke so far.
    ///
    /// Scheduling is left to the dispatcher rather than to a wall-clock
    /// throttle of our own. Background priority yields to pointer input and to
    /// the Default-priority snapshot, so during a fast drag the pass runs in
    /// whatever gaps exist and during a pause it runs immediately — which is
    /// exactly the cadence wanted, and the dispatcher already knows how busy
    /// the thread is. A cost-based throttle was tried first and was worse in
    /// the way that matters: it blocked the pass that would have settled the
    /// preview, so the mark froze part-drawn until the pen lifted.
    ///
    /// Only one pass is ever outstanding, and a pass with nothing new to draw
    /// returns immediately.
    /// </summary>
    private void RequestLivePostProcess()
    {
        if (_livePostQueued) return;
        _livePostQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            RenderLivePostProcess, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// The whole stroke so far, rendered exactly as it will commit — minus the
    /// stroke opacity and the masks, which the compositor still applies once.
    /// </summary>
    private void RenderLivePostProcess()
    {
        _livePostQueued = false;
        if (_strokeBuilder.Current is not { } live || !_strokeBuilder.IsActive) return;
        if (!NeedsLivePostProcess(live.Brush)) return;
        if (_livePostStampedCount == live.Points.Count) return; // nothing new since last pass
        // The pass reads the dabs from the live scratch; the blur and smudge
        // brushes use the copy-based path instead and have none.
        if (_liveScratch is not { } dabs) return;

        var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (_livePostScratch is null || _livePostScratch.Width != info.Width || _livePostScratch.Height != info.Height)
        {
            _livePostScratch?.Dispose();
            _livePostScratch = new SKBitmap(info);
            _livePostUsed = null;
        }

        // The same stroke, minus the opacity the compositor applies — the
        // masks stay on the overlay so they cannot be baked in twice.
        var whole = new Stroke
        {
            Tool = live.Tool,
            Color = live.Color,
            SwatchId = live.SwatchId,
            Brush = live.Brush,
            Points = [.. live.Points],
        };

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using (var canvas = new SKCanvas(_livePostScratch))
        {
            ClearRegion(canvas, _livePostUsed);
        }
        // The dabs are already in the live scratch, so the pass runs the
        // effects over them rather than re-stamping every dab — the cost of a
        // pass stops growing with the length of the stroke.
        // targetPixels is the committed layer: the medium re-wets what is
        // already there, exactly as it will on commit.
        var beneath = PaintTarget() is { } frame ? _cache.Get(frame, Scene.Width, Scene.Height) : null;
        var bounds = BrushEngine.PostProcessDabs(dabs, _livePostScratch, whole, info, beneath);

        _livePostCostMs = (System.Diagnostics.Stopwatch.GetTimestamp() - started)
                          * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _livePostStampedCount = live.Points.Count;
        _livePostUsed = bounds;
        LivePostPasses++;
        LivePostTotalMs += _livePostCostMs;

        if (bounds is { } rect) MarkDirtyRegion(rect);
        else InvalidateWholeCanvas();
        // Through the coalescing path, not straight to PublishSnapshot. A
        // direct publish here put an extra frame on the wire for every pass on
        // top of the one the pointer event had already queued, and publishing
        // faster than the compositor draws is what let the canvas free an
        // image out from under it.
        RequestSnapshot();

        // Points arrived while this pass was rendering: go round again so the
        // preview settles on the whole stroke rather than stopping wherever
        // the pen happened to be when the pass started.
        if (_strokeBuilder.Current is { } now && now.Points.Count != _livePostStampedCount)
        {
            RequestLivePostProcess();
        }
    }

    private static void ClearRegion(SKCanvas canvas, SKRectI? region)
    {
        if (region is not { } used)
        {
            canvas.Clear(SKColors.Transparent);
            return;
        }
        canvas.Save();
        canvas.ClipRect(SKRect.Create(used.Left, used.Top, used.Width, used.Height));
        canvas.Clear(SKColors.Transparent);
        canvas.Restore();
    }

    private void ResetLivePostProcess()
    {
        if (_livePostScratch is not null && _livePostUsed is not null)
        {
            using var canvas = new SKCanvas(_livePostScratch);
            ClearRegion(canvas, _livePostUsed);
        }
        _livePostUsed = null;
        _livePostCostMs = 0;
        _livePostStampedCount = -1;
    }

    private static SKRectI UnionRect(SKRectI a, SKRectI b)
    {
        a.Union(b);
        return a;
    }

    /// <summary>
    /// Coalesce repaints: at most one queued snapshot at a time, published after the pointer events
    /// already waiting rather than in between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Input priority, and B73 is why.</b> Avalonia runs
    /// <c>Render &gt; Loaded &gt; Default &gt; Input &gt; Background</c> — <c>Default</c> is
    /// <em>above</em> <c>Input</c> here, which is the reverse of WPF, where it sits below. This was
    /// posted at <c>Default</c>, so a publish jumped ahead of every pen event already in the queue.
    /// Two consequences, and the artist feels them as one thing:
    /// </para>
    /// <para>
    /// The frame drawn was <b>already behind</b> — published before a single queued event had been
    /// handled, so the ink on screen stopped where the pen had been several events ago. And because
    /// the publish ran between events instead of after them, a burst of <em>n</em> events produced
    /// <em>n</em> publishes rather than one: measured at <b>11 events → 11 publishes</b>. A publish
    /// is the expensive half, so the faster the stroke the more the work multiplied, and the lag
    /// compounded along it. That is why the report was about <em>fast</em> strokes specifically.
    /// </para>
    /// <para>
    /// <c>Input</c> is right rather than merely lower because that queue is FIFO: this lands behind
    /// the events already waiting, so one frame covers the burst and is current — and ahead of
    /// events that arrive afterwards, so a continuous drag still renders. <c>Background</c> also
    /// drains the burst and can be starved by continuous input, which is the state an artist is in
    /// for the whole of a long stroke.
    /// </para>
    /// <para>
    /// <b>Never Render priority</b>, whatever the latency argument: jobs in the dispatcher's render
    /// phase swallow the <c>InvalidateVisual</c> they trigger, which leaves the canvas permanently
    /// un-scheduled — strokes appeared only after the next unrelated event, the "frozen cursor, no
    /// lines" bug. <c>StrokeLatencyTests</c> guards the priority from the other side.
    /// </para>
    /// </remarks>
    private void RequestSnapshot()
    {
        if (_snapshotQueued) return;
        _snapshotQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _snapshotQueued = false;
            PublishSnapshot();
        }, Avalonia.Threading.DispatcherPriority.Input);
    }

    /// <summary>
    /// Drop the Blur/Smudge live preview and the ordinary-paint dab-walk
    /// bookkeeping. <see cref="EndStroke"/> calls this after a real commit;
    /// anything that abandons a stroke without going through it —
    /// <see cref="AttachEditor"/> on a tab switch, <see cref="StartPlayback"/>
    /// — must call it too. <c>_strokeBuilder.Cancel()</c> alone leaves
    /// <see cref="_liveComposite"/> non-null, and every publish after that
    /// treats a non-null <see cref="_liveComposite"/> as "an effect brush is
    /// live on this layer" — which, left stale, silently suppressed the
    /// overlay for every ordinary stroke, gradient and shape drag afterward,
    /// on any document, until ink happened to reset it (B39's fix).
    /// </summary>
    private void ClearLiveEffectState()
    {
        _liveComposite?.Dispose();
        _liveComposite = null;
        _liveEffectBase?.Dispose();
        _liveEffectBase = null;
        _liveStampedCount = 0;
        _liveDabCount = 0;
        _liveStableDabs = 0;
        _liveTailRegion = null;
        _liveDabs = null;
        _liveEffectDabs = null;
        _liveEffectSettled = 0;
        // The carry and the lent region go with the composite they described. The backup bitmap
        // does not: it is reused across strokes and only ever written before it is read, so keeping
        // it saves an allocation per stroke without any state surviving.
        _liveSmudgeCarry = default;
        _liveSmudgeRegion = null;
        ResetLivePostProcess();
    }

    public void EndStroke()
    {
        var stroke = _strokeBuilder.End();
        ClearLiveEffectState();
        if (stroke is null) return;
        var target = PaintTarget();
        if (target is null) return;

        _stabilizer.End();
        LazyBrushCleared?.Invoke();
        stroke.Points = _stabilizer.PostProcess(stroke.Points);

        // Remembered for the next Shift+click. The post-processed end, not the
        // raw one, so the next segment starts exactly where this mark stops.
        _lastStrokeEnd = stroke.Points.Count > 0
            ? (stroke.Points[^1].X, stroke.Points[^1].Y)
            : null;

        // A stroke painted under a selection carries it forever (provenance).
        var clip = PrepareClipForSelection();
        if (clip is not null) stroke.ClipId = clip.Value.Id;

        // Both of these were wired into EndGradient and EndShape and missed
        // here, which is the path a pen actually takes. The freeze mattered:
        // an all-layers-BAKED smudge drawn by hand never froze anything and
        // silently fell back to reading its own layer. Live hid it, because
        // the re-bake runs off the edit funnel and covered for the missing
        // call — so the half that was tested end to end worked and the half
        // that was not did not.
        FreezeSampledBackdrop(stroke);
        RememberDocumentBrush();

        // Commit the pixels incrementally: stamp the EXACT stroke onto the
        // cached frame bitmap instead of invalidating it — invalidation would
        // replay every stroke in the frame, which is why lifting the pen used
        // to pause on drawings with many strokes. Appending the exact stroke
        // to the previously exact bitmap is the same sequence Materialize
        // would run, so the pixels stay bit-identical.
        AppendToFrameRender(target, stroke); // pre-stroke state (record not yet updated)

        // Undo without snapshotting the whole document (the other pen-lift
        // pause). The frame is resolved by id at apply/revert time: a
        // snapshot-undo in between replaces the doc instance tree.
        var frameId = target.Id;
        var addedClip = false;
        _committingScopedEdit = true;
        try
        {
            _editor.PerformDelta(
                apply: doc =>
                {
                    if (clip is { } c) addedClip = doc.ClipRegions.TryAdd(c.Id, c.Region);
                    StrokeListIn(doc, frameId)?.Add(stroke);
                },
                revert: doc =>
                {
                    RemoveStrokeById(doc, frameId, stroke.Id);
                    if (clip is { } c && addedClip) doc.ClipRegions.Remove(c.Id);
                },
                affectedFrameId: frameId);
        }
        finally
        {
            _committingScopedEdit = false;
        }
        _dirtyThumbIds.Add(target.Id);
        // Only the stroke's own neighbourhood changed: the layer gained the
        // committed pixels and the live scratch stopped contributing there.
        var commitInfo = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (BrushEngine.CommitBounds(stroke, commitInfo) is { } touched) MarkDirtyRegion(touched);
        else InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
    }

    // ---- commands -----------------------------------------------------------

    // ---- playback transport --------------------------------------------------

    private int _playDirection = 1;

    /// <summary>Playback start (index, -1 = unset → first frame).</summary>
    [ObservableProperty]
    private int _playbackStartFrame = -1;

    /// <summary>Playback end (index, -1 = unset → last frame).</summary>
    [ObservableProperty]
    private int _playbackEndFrame = -1;

    partial void OnPlaybackStartFrameChanged(int value) => RefreshRangeHighlights();

    partial void OnPlaybackEndFrameChanged(int value) => RefreshRangeHighlights();

    internal int EffectiveStartFrame =>
        Math.Clamp(PlaybackStartFrame < 0 ? 0 : PlaybackStartFrame, 0, Math.Max(0, Scene.FrameCount - 1));

    internal int EffectiveEndFrame =>
        Math.Clamp(PlaybackEndFrame < 0 ? Scene.FrameCount - 1 : PlaybackEndFrame, EffectiveStartFrame, Math.Max(0, Scene.FrameCount - 1));

    [RelayCommand]
    private void Play() => StartPlayback(1);

    [RelayCommand]
    private void PlayBackwards() => StartPlayback(-1);

    [RelayCommand]
    private void Pause()
    {
        if (!IsPlaying) return;
        _clock.Stop();
        IsPlaying = false;
        StopAudio();
        PublishSnapshot();
    }

    private void StartPlayback(int direction)
    {
        _playDirection = direction;
        if (IsPlaying) return;
        _strokeBuilder.Cancel();
        ClearLiveEffectState();
        IsPlaying = true;
        _clock.Start(Scene.Fps, PlaybackSpeedPercent);
        TickAudio();
        PublishSnapshot();
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    /// <summary>One button for both, so the shortcut bar costs one slot.</summary>
    public string PlayPauseGlyph => IsPlaying ? "⏸" : "▶";

    /// <summary>
    /// Whether transport controls are worth showing at all.
    /// </summary>
    /// <remarks>
    /// Workspace-relevant, which here means: an illustration is not going to
    /// be played, so the shortcut bar does not carry a play button on one. The
    /// rest — an animation, a game sprite, a storyboard, or a plain document
    /// with no project saying otherwise — might be.
    /// </remarks>
    public bool ShowsTransport =>
        ProjectDocker.Project?.Manifest.Type != Lightbox.Core.Projects.ProjectType.Illustration;

    /// <summary>Onion skin on the layer being drawn on — the per-layer opt-out, on the canvas.</summary>
    public bool ActiveLayerOnion
    {
        get => ActiveLayer.OnionEnabled;
        set
        {
            if (ActiveLayer.OnionEnabled == value) return;
            SetLayerOnionEnabled(ActiveLayer, value);
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private void GoToStartFrame() => CurrentFrameIndex = EffectiveStartFrame;

    [RelayCommand]
    private void GoToEndFrame() => CurrentFrameIndex = EffectiveEndFrame;

    [RelayCommand]
    private void PreviousKeyframe()
    {
        var layer = ActiveLayer;
        for (var i = Math.Min(CurrentFrameIndex, Scene.FrameCount) - 1; i >= 0; i--)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not null)
            {
                CurrentFrameIndex = i;
                return;
            }
        }
    }

    [RelayCommand]
    private void NextKeyframe()
    {
        var layer = ActiveLayer;
        for (var i = CurrentFrameIndex + 1; i < Scene.FrameCount; i++)
        {
            if (ExposureSheet.FrameAtExactIndex(layer, i) is not null)
            {
                CurrentFrameIndex = i;
                return;
            }
        }
    }

    /// <summary>
    /// Whether playback wraps at the end of the range.
    /// </summary>
    /// <remarks>
    /// On, because a cycle is the thing you are usually looking at and
    /// stopping after one pass means reaching for the button every time. Off
    /// is for watching a shot end, which is the other half of the job — and a
    /// preference rather than a document property, because it is how you are
    /// reviewing right now, not something about the animation.
    /// </remarks>
    public bool LoopPlayback
    {
        get => Settings.LoopPlayback;
        set
        {
            if (Settings.LoopPlayback == value) return;
            Settings.LoopPlayback = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>One playback tick: advance in the play direction, looping inside the selected range.</summary>
    public void StepPlayback()
    {
        var start = EffectiveStartFrame;
        var end = EffectiveEndFrame;
        var next = CurrentFrameIndex + _playDirection;
        if (next > end || next < start)
        {
            if (!LoopPlayback)
            {
                // Stop on the last frame of the range rather than wrapping —
                // and stop, rather than sitting there still "playing", so the
                // transport button says what is true.
                CurrentFrameIndex = Math.Clamp(
                    _playDirection >= 0 ? end : start, 0, Math.Max(0, Scene.FrameCount - 1));
                Pause();
                return;
            }
            // The loop wrapped: the sound cannot wrap with it, so it stops
            // here and TickAudio restarts it at the range's start.
            next = next > end ? start : end;
            StopAudio();
        }
        CurrentFrameIndex = Math.Clamp(next, 0, Math.Max(0, Scene.FrameCount - 1));
        TickAudio();
    }

    // ---- playback range + frame insertion (timeline context menu) -----------

    public void SetPlaybackStart(FrameCell cell) =>
        PlaybackStartFrame = Math.Min(cell.Index, Scene.FrameCount - 1);

    public void SetPlaybackEnd(FrameCell cell) =>
        PlaybackEndFrame = Math.Min(cell.Index, Scene.FrameCount - 1);

    public void ClearPlaybackRange()
    {
        PlaybackStartFrame = -1;
        PlaybackEndFrame = -1;
    }

    /// <summary>
    /// Insert a drawn frame with the given role at a timeline cell (possibly a
    /// virtual one beyond the current end — the timeline extends to reach it),
    /// or re-mark an existing frame's role.
    /// </summary>
    public void InsertFrameAt(FrameCell cell, FrameRole role)
    {
        if (cell.LayerIndex < 0 || cell.LayerIndex >= Scene.Layers.Count) return;
        _editor.SetKeyAt(Scene.Layers[cell.LayerIndex].Id, cell.Index, role);
        ActiveLayerIndex = cell.LayerIndex;
        CurrentFrameIndex = Math.Min(cell.Index, Scene.FrameCount - 1);
    }

    // ---- exposure editing + cel clipboard --------------------------------------

    private Layer? LayerOfCell(FrameCell cell) =>
        cell.LayerIndex >= 0 && cell.LayerIndex < Scene.Layers.Count ? Scene.Layers[cell.LayerIndex] : null;

    public void ExtendExposureAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        _editor.ExtendExposure(layer.Id, cell.Index);
    }

    public void ReduceExposureAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        _editor.ReduceExposure(layer.Id, cell.Index);
    }

    /// <summary>Frames each drawing is held for by the two re-timing commands.</summary>
    [ObservableProperty]
    private int _exposureStep = 2;

    partial void OnExposureStepChanged(int value)
    {
        if (value is < 1 or > 8) ExposureStep = Math.Clamp(value, 1, 8);
    }

    /// <summary>
    /// Hold every drawing in the range for <see cref="ExposureStep"/> frames.
    /// The range gets longer and nothing is lost — this is what "animate on
    /// 2s" means to an animator.
    /// </summary>
    public void StretchExposureAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "re-time it")) return;
        var (start, end) = OpRangeFor(cell);
        var grew = _editor.StretchExposure(layer.Id, start, end, ExposureStep);
        AfterRetime(layer);
        AiStatus = grew > 0
            ? $"Stretched to {ExposureStep}s — the range grew by {grew} frame{(grew == 1 ? "" : "s")}."
            : "Nothing to stretch in that range.";
    }

    /// <summary>
    /// Keep every <see cref="ExposureStep"/>-th drawing and discard the rest,
    /// holding what survives so the range keeps its length. Destructive, which
    /// is why it is a separate command rather than a mode on the first.
    /// </summary>
    public void ReduceToStepAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "re-time it")) return;
        var (start, end) = OpRangeFor(cell);
        var dropped = _editor.ReduceToStep(layer.Id, start, end, Math.Max(2, ExposureStep));
        AfterRetime(layer);
        AiStatus = dropped > 0
            ? $"Reduced to {Math.Max(2, ExposureStep)}s — discarded {dropped} drawing{(dropped == 1 ? "" : "s")}."
            : "Nothing to reduce in that range.";
    }

    // ---- timing presets (Q11's UI half) ----------------------------------------

    /// <summary>
    /// The patterns on offer: the built-ins, then whatever the artist has saved.
    /// </summary>
    /// <remarks>
    /// Built-ins first and never stored, so a later correction to "slow in"
    /// reaches everybody instead of being frozen into their settings file the
    /// first time they opened the app.
    /// </remarks>
    public ObservableCollection<TimingPreset> TimingPresets { get; } = [];

    [ObservableProperty]
    private TimingPreset? _selectedTimingPreset;

    /// <summary>Whether the selected pattern is one of the artist's own.</summary>
    public bool CanDeleteTimingPreset =>
        SelectedTimingPreset is { } preset && !TimingPreset.BuiltIns.Contains(preset);

    /// <summary>The cel menu's re-time item, naming the pattern the bar has chosen.</summary>
    /// <remarks>
    /// Naming it rather than offering the whole list again: a submenu on the cel
    /// would be a second picker to keep in step with the first, and the two
    /// disagreeing is the kind of thing an artist notices at the worst moment.
    /// </remarks>
    public string RetimeMenuLabel =>
        SelectedTimingPreset is { } preset ? $"Re-time to {preset.Name}" : "Re-time";

    partial void OnSelectedTimingPresetChanged(TimingPreset? value)
    {
        OnPropertyChanged(nameof(CanDeleteTimingPreset));
        OnPropertyChanged(nameof(RetimeMenuLabel));
    }

    private void LoadTimingPresets()
    {
        TimingPresets.Clear();
        foreach (var preset in TimingPreset.BuiltIns) TimingPresets.Add(preset);
        foreach (var preset in TimingPresetStore.Load()) TimingPresets.Add(preset);
        SelectedTimingPreset ??= TimingPresets.FirstOrDefault(p => p.Name == "On 2s") ?? TimingPresets.FirstOrDefault();
    }

    /// <summary>
    /// Re-time the cel's range to the selected pattern, as one undoable step.
    /// </summary>
    /// <remarks>
    /// The row grows or shrinks to fit the pattern rather than the selection,
    /// because "on 2s" must never mean "throw away half my drawings". The status
    /// line says which way it went, since a silent change of length on a long
    /// row is easy to miss.
    /// </remarks>
    public void ApplyTimingAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (SelectedTimingPreset is not { } preset) return;
        if (!CanEdit(layer, "re-time it")) return;

        var (start, end) = OpRangeFor(cell);
        var change = _editor.ApplyTiming(layer.Id, start, end, preset);
        if (change.Drawings == 0)
        {
            AiStatus = "Nothing to re-time there — that range holds no drawing of its own.";
            return;
        }

        AfterRetime(layer);
        var length = change.Grew switch
        {
            > 0 => $", {change.Grew} frame{(change.Grew == 1 ? "" : "s")} longer",
            < 0 => $", {-change.Grew} frame{(change.Grew == -1 ? "" : "s")} shorter",
            _ => "",
        };
        AiStatus =
            $"{preset.Name}: {change.Drawings} drawing{(change.Drawings == 1 ? "" : "s")} " +
            $"over {change.Frames} frame{(change.Frames == 1 ? "" : "s")}{length}.";
    }

    [RelayCommand]
    private void ApplySelectedTiming()
    {
        if (CurrentCell() is { } cell) ApplyTimingAt(cell);
    }

    [ObservableProperty]
    private string _newTimingPresetName = "";

    [ObservableProperty]
    private string _newTimingPresetPattern = "";

    /// <summary>
    /// Save the typed pattern under the typed name. False when it will not parse.
    /// </summary>
    /// <remarks>
    /// A name already in use replaces that preset rather than adding a second
    /// with the same label, which is the only behaviour that leaves the list
    /// usable. Built-ins cannot be shadowed — an artist who saves "On 2s" gets
    /// their own entry beside it rather than silently overriding the one the
    /// manual describes.
    /// </remarks>
    public bool SaveTimingPreset()
    {
        var name = NewTimingPresetName.Trim();
        if (name.Length == 0) name = "Custom";
        if (!TimingPreset.TryParse(name, NewTimingPresetPattern, out var preset))
        {
            AiStatus = "A timing pattern is whole numbers of frames — \"2\", or \"1, 1, 2, 3, 4\".";
            return false;
        }

        var mine = TimingPresets.Where(p => !TimingPreset.BuiltIns.Contains(p)).ToList();
        if (mine.FirstOrDefault(p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            TimingPresets[TimingPresets.IndexOf(existing)] = preset;
        }
        else
        {
            TimingPresets.Add(preset);
        }

        TimingPresetStore.Save(TimingPresets.Where(p => !TimingPreset.BuiltIns.Contains(p)));
        SelectedTimingPreset = preset;
        NewTimingPresetName = "";
        NewTimingPresetPattern = "";
        AiStatus = $"Saved \"{preset.Name}\" — {preset.Pattern}.";
        return true;
    }

    /// <summary>Forget one of the artist's own patterns. Built-ins are not deletable.</summary>
    public void DeleteSelectedTimingPreset()
    {
        if (SelectedTimingPreset is not { } preset || TimingPreset.BuiltIns.Contains(preset)) return;
        TimingPresets.Remove(preset);
        TimingPresetStore.Save(TimingPresets.Where(p => !TimingPreset.BuiltIns.Contains(p)));
        SelectedTimingPreset = TimingPresets.FirstOrDefault();
        AiStatus = $"Deleted \"{preset.Name}\".";
    }

    private void AfterRetime(Layer layer)
    {
        foreach (var cel in layer.Cels)
        {
            if (cel.Frame is { } frame) _dirtyThumbIds.Add(frame.Id);
        }
        // Every re-timing operation can change the row's length, and stretching
        // already grew the scene with it. These three are derived from
        // Scene.FrameCount and have no notification of their own, so without
        // them the ruler and the scrub limit kept the old length until something
        // else happened to refresh them.
        OnPropertyChanged(nameof(TimelineExtent));
        OnPropertyChanged(nameof(MaxScrubFrame));
        OnPropertyChanged(nameof(FrameLabel));
        SyncLayerRows();
        ClampCurrentFrame(publishIfUnchanged: false);
        InvalidateWholeCanvas();
        PublishSnapshot();
        RefreshThumbnails();
        MarkDocumentEdited();
    }

    [RelayCommand]
    private void StretchSelectedExposure()
    {
        if (CurrentCell() is { } cell) StretchExposureAt(cell);
    }

    [RelayCommand]
    private void ReduceSelectedExposure()
    {
        if (CurrentCell() is { } cell) ReduceToStepAt(cell);
    }

    /// <summary>Clear the drawing(s) at the cell — or the whole selected range when the cell is inside it.</summary>
    /// <summary>
    /// Delete the cel (or the selected range) and pull the rest of the row
    /// back. "Clear cel" blanks a drawing and keeps its slot; this removes the
    /// slot, which is the operation the timeline was missing entirely.
    /// </summary>
    public void DeleteCelAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "delete a cel on it")) return;
        var (start, end) = OpRangeFor(cell);
        _editor.DeleteCels(layer.Id, start, end);
        _allThumbsDirty = true;
        RefreshThumbnails();
    }

    public void ClearCelAt(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "clear a cel on it")) return;
        var (start, end) = OpRangeFor(cell);
        if (start == end && ExposureSheet.FrameAtExactIndex(layer, cell.Index) is null)
        {
            AiStatus = "That cel is a hold — there is no drawing to clear.";
            return;
        }
        _editor.ClearCels(layer.Id, start, end);
        RefreshThumbnails();
    }

    /// <summary>App-internal cel clipboard: a cel sequence (null = hold) + its source layer kind.</summary>
    private (List<Frame?> Frames, LayerKind Kind)? _celClipboard;

    public bool HasCelClipboard => _celClipboard is not null;

    /// <summary>
    /// Copy the cell — or the whole Shift+click range when the cell is inside
    /// it. A single hold cel copies the drawing it shows; ranges copy cels
    /// verbatim, holds included, so timing survives the round trip.
    /// </summary>
    public void CopyCel(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        var (start, end) = OpRangeFor(cell);
        List<Frame?> frames;
        if (start == end)
        {
            var exposed = ExposureSheet.ExposedFrame(layer, cell.Index);
            if (exposed is null)
            {
                AiStatus = "Nothing to copy — the cel is empty.";
                return;
            }
            frames = [DocumentEditor.CloneFrame(exposed)];
        }
        else
        {
            frames = [];
            for (var i = start; i <= end; i++)
            {
                frames.Add(DocumentEditor.CloneFrame(ExposureSheet.FrameAtExactIndex(layer, i)));
            }
        }
        _celClipboard = (frames, layer.Kind);
        OnPropertyChanged(nameof(HasCelClipboard));
        AiStatus = frames.Count == 1 ? "Cel copied." : $"{frames.Count} cels copied.";
    }

    public void CutCel(FrameCell cell)
    {
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "cut a cel from it")) return;
        var (start, end) = OpRangeFor(cell);
        if (start == end && ExposureSheet.FrameAtExactIndex(layer, cell.Index) is null)
        {
            AiStatus = "Nothing to cut — the cel is a hold.";
            return;
        }
        CopyCel(cell);
        _editor.ClearCels(layer.Id, start, end);
        RefreshThumbnails();
    }

    /// <summary>Paste the copied cel(s) starting at the cell (holds paste as holds).</summary>
    public void PasteCel(FrameCell cell)
    {
        if (_celClipboard is not { } clip)
        {
            AiStatus = "The cel clipboard is empty.";
            return;
        }
        if (LayerOfCell(cell) is not { } layer) return;
        if (!CanEdit(layer, "paste onto it")) return;

        var frames = new List<Frame?>(clip.Frames.Count);
        foreach (var source in clip.Frames)
        {
            // No conversion, and nothing refused. One frame class means a copied
            // cel is already the shape every layer takes — this used to rebuild
            // the frame as the other kind, and to reject a paste outright when a
            // baseline could not "become vector". Both were consequences of the
            // split rather than decisions: pixels and strokes always coexisted on
            // one frame, so there was never anything to convert.
            frames.Add(DocumentEditor.CloneFrame(source)); // fresh id per paste
        }
        _editor.SetFrameRange(layer.Id, cell.Index, frames);
        ActiveLayerIndex = cell.LayerIndex;
        CurrentFrameIndex = Math.Min(cell.Index, Scene.FrameCount - 1);
    }

    /// <summary>Ctrl+C/X/V target: the active layer's cel at the playhead.</summary>
    private FrameCell? CurrentCell()
    {
        var row = LayerRows.FirstOrDefault(r => r.SceneIndex == ActiveLayerIndex);
        return row?.Cells.FirstOrDefault(c => c.Index == CurrentFrameIndex);
    }

    public void CopyCurrentCel()
    {
        if (CurrentCell() is { } cell) CopyCel(cell);
    }

    public void CutCurrentCel()
    {
        if (CurrentCell() is { } cell) CutCel(cell);
    }

    public void PasteCurrentCel()
    {
        if (CurrentCell() is { } cell) PasteCel(cell);
    }

    // ---- transform tool (Ctrl+T) ------------------------------------------------

    [ObservableProperty]
    private TransformScope _transformScope = TransformScope.ActiveCel;

    public IReadOnlyList<TransformScope> TransformScopeChoices { get; } = Enum.GetValues<TransformScope>();

    /// <summary>Pixel solver for raster baselines (strokes never resample).</summary>
    [ObservableProperty]
    private TransformSampling _transformSampling = TransformSampling.Bilinear;

    public IReadOnlyList<TransformSampling> TransformSamplingChoices { get; } = Enum.GetValues<TransformSampling>();

    [ObservableProperty]
    private bool _transformActive;

    private readonly List<Frame> _transformFrames = [];
    private Func<Stroke, bool>? _transformFilter;

    /// <summary>Session started/restarted: bounds of the transformable content (doc space).</summary>
    public event Action<double, double, double, double>? TransformBegun;

    public event Action? TransformEnded;

    /// <summary>Start (or restart) a transform session over the current scope.</summary>
    /// <param name="gizmo">
    /// Raise <see cref="TransformBegun"/> so the canvas puts a handled box
    /// round the drawing. The Move tool passes false: it is one drag with no
    /// handles, and a gizmo appearing under the pointer for the length of a
    /// nudge is noise.
    /// </param>
    public bool BeginTransform(bool gizmo = true)
    {
        if (!CanEdit(ActiveLayer, "transform it")) return false;
        var frames = CollectTransformFrames();
        Func<Stroke, bool>? filter = null;
        if (HasSelection)
        {
            int w = Scene.Width, h = Scene.Height;
            var mask = MaskFromContours(_selectionContours, w, h);
            filter = s => TransformOps.MajorityInside(s, mask, w, h);
        }
        var bounds = TransformOps.Bounds(frames, filter);
        if (frames.Count == 0 || bounds is null)
        {
            AiStatus = "Nothing to transform in this scope.";
            if (TransformActive) CancelTransform();
            return false;
        }
        _transformFrames.Clear();
        _transformFrames.AddRange(frames);
        _transformFilter = filter;
        TransformActive = true;
        var b = bounds.Value;
        if (gizmo) TransformBegun?.Invoke(b.MinX, b.MinY, b.MaxX, b.MaxY);
        return true;
    }

    // ---- move tool ---------------------------------------------------------------

    /// <summary>
    /// A move in progress: where it started and how far it has come.
    /// </summary>
    /// <remarks>
    /// A move is a transform session with the handles left off. That is not a
    /// shortcut — it is the same operation, so it gets the same live preview
    /// (composite-time, one matrix, no geometry touched until the release),
    /// the same selection filter, the same scopes and the same single undo
    /// step. Re-implementing translation next to it would be a second way for
    /// the drawing to move, and the two would drift.
    /// </remarks>
    private (double X, double Y)? _moveAnchor;

    private (double X, double Y) _moveDelta;

    public bool MoveActive => _moveAnchor is not null;

    /// <summary>
    /// Pick the drawing up.
    /// </summary>
    /// <param name="wholeLayer">
    /// Move every drawing on the layer by the same amount, rather than the one
    /// under the playhead. Shifting a finished cycle sideways is a real job
    /// and doing it a frame at a time is not a job anybody finishes.
    /// </param>
    public bool BeginMove(double x, double y, bool wholeLayer)
    {
        // A placed symbol under the cursor wins, and only when the grab is on
        // the drawing rather than on the whole layer. Moving a placement is an
        // edit to two numbers on the placement; moving the drawing rewrites
        // stroke coordinates. They are different operations that happen to
        // share a gesture, and which one you get is decided by what you
        // grabbed — the same way it is in Photoshop.
        if (!wholeLayer && BeginPlacementMove(x, y)) return true;

        var scope = wholeLayer ? TransformScope.ActiveLayerAllFrames : TransformScope.ActiveCel;
        // Assigned before the session starts, so the property's change handler
        // has no live session to restart and simply records the scope.
        TransformScope = scope;
        if (!BeginTransform(gizmo: false)) return false;
        _moveAnchor = (x, y);
        _moveDelta = default;
        AiStatus = wholeLayer
            ? $"Moving every drawing on {ActiveLayer.Name}"
            : "Moving this drawing — hold Ctrl to move the whole layer";
        return true;
    }

    /// <param name="axisLock">
    /// Shift: hold the move to one axis, whichever it has gone furthest along.
    /// The same thing Shift means on every other tool here.
    /// </param>
    public void UpdateMove(double x, double y, bool axisLock)
    {
        if (PlacementMoveActive)
        {
            UpdatePlacementMove(x, y, axisLock);
            return;
        }
        if (_moveAnchor is not { } anchor) return;
        var dx = x - anchor.X;
        var dy = y - anchor.Y;
        if (axisLock)
        {
            if (Math.Abs(dx) >= Math.Abs(dy)) dy = 0;
            else dx = 0;
        }
        _moveDelta = (dx, dy);
        PreviewTransform(SKMatrix.CreateTranslation((float)dx, (float)dy));
    }

    /// <summary>Put it down. One undo step for the whole drag, or nothing at all.</summary>
    public void EndMove()
    {
        if (PlacementMoveActive)
        {
            EndPlacementMove();
            return;
        }
        if (_moveAnchor is null) return;
        var (dx, dy) = _moveDelta;
        _moveAnchor = null;
        _moveDelta = default;
        // A click that went nowhere is a click, not an edit. Committing it
        // would put an identity transform in the history for every stray tap.
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9)
        {
            CancelMove();
            return;
        }
        CommitTransformAffine(0, 0, 1, 1, 0, dx, dy);
        TransformActive = false;
    }

    public void CancelMove()
    {
        if (PlacementMoveActive)
        {
            CancelPlacementMove();
            return;
        }
        _moveAnchor = null;
        _moveDelta = default;
        if (TransformActive) CancelTransform();
    }

    /// <summary>Begin moving selected guides.</summary>
    public void BeginGuidesMove()
    {
        if (_selectionManager.SelectedGuideIndices.Count == 0) return;
        _guidesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedGuideIndices.Count} guide(s)";
    }

    /// <summary>
    /// Move the selected guides by the delta since the last pointer event.
    /// </summary>
    /// <remarks>
    /// Live on the record and accumulated, which is <see cref="DragGuide"/> for
    /// a group — the same reason it gives: a pointer move arrives every few
    /// milliseconds, so recording each one would bury the last real edit under
    /// fifty identical nudges. It has to be added rather than assigned; the
    /// canvas reports the change since the previous event and advances its own
    /// anchor, and assigning keeps only the final increment (B109).
    /// </remarks>
    public void UpdateGuidesMove(double dx, double dy)
    {
        foreach (var guide in SelectedGuides())
        {
            guide.X += dx;
            guide.Y += dy;
        }
        _guidesMoveDelta = (_guidesMoveDelta.X + dx, _guidesMoveDelta.Y + dy);
        NotifyGuides();
    }

    /// <summary>Close a group guide drag: the whole of it becomes one undo step.</summary>
    public void EndGuidesMove()
    {
        var (dx, dy) = _guidesMoveDelta;
        _guidesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveGuidesBy(dx, dy);
    }

    /// <summary>
    /// The selected guides that may actually be moved.
    /// </summary>
    /// <remarks>
    /// A locked guide is skipped, the way <see cref="MoveGuide"/> and
    /// <see cref="DragGuide"/> both skip one. Locking exists so a perspective
    /// set can be leaned on without being knocked out of place, and a group
    /// move that ignored it would be the one gesture that could.
    /// </remarks>
    private List<Guide> SelectedGuides()
    {
        var guides = Scene.Guides;
        if (guides is null || guides.Count == 0) return [];
        var picked = new List<Guide>();
        foreach (var index in _selectionManager.SelectedGuideIndices)
        {
            if (index < 0 || index >= guides.Count) continue;
            if (guides[index].Locked) continue;
            picked.Add(guides[index]);
        }
        return picked;
    }

    private (double X, double Y) _guidesMoveDelta;

    /// <summary>Record a finished group guide move as one step.</summary>
    /// <remarks>
    /// Rewound first and then replayed forward, which is what
    /// <see cref="EndGuideDrag"/> does and for the same reason: the guides are
    /// already sitting at the end of the drag, so undo has to return them to
    /// where they were picked up rather than to the last pointer event. The
    /// guides are held by reference rather than by index, because an undo can
    /// replace the list and leave an index pointing at a different guide.
    /// </remarks>
    private void MoveGuidesBy(double dx, double dy)
    {
        var moved = SelectedGuides();
        if (moved.Count == 0) return;

        foreach (var guide in moved)
        {
            guide.X -= dx;
            guide.Y -= dy;
        }
        _editor.PerformDelta(
            _ =>
            {
                foreach (var guide in moved)
                {
                    guide.X += dx;
                    guide.Y += dy;
                }
                NotifyGuides();
            },
            _ =>
            {
                foreach (var guide in moved)
                {
                    guide.X -= dx;
                    guide.Y -= dy;
                }
                NotifyGuides();
            });
        NotifyGuides();
    }

    /// <summary>Begin moving selected reference boxes.</summary>
    public void BeginRefBoxesMove()
    {
        if (_selectionManager.SelectedRefBoxIndices.Count == 0) return;
        _refBoxesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedRefBoxIndices.Count} reference box(es)";
    }

    /// <summary>
    /// Move the selected reference boxes by the delta since the last pointer
    /// event.
    /// </summary>
    /// <remarks>
    /// Live and accumulated, for the reason on <see cref="UpdateGuidesMove"/>.
    /// </remarks>
    public void UpdateRefBoxesMove(double dx, double dy)
    {
        foreach (var cell in SelectedRefBoxes())
        {
            cell.Dx += dx;
            cell.Dy += dy;
        }
        _refBoxesMoveDelta = (_refBoxesMoveDelta.X + dx, _refBoxesMoveDelta.Y + dy);
        AfterReferenceChange();
    }

    /// <summary>Close a group box drag: the whole of it becomes one undo step.</summary>
    public void EndRefBoxesMove()
    {
        var (dx, dy) = _refBoxesMoveDelta;
        _refBoxesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveRefBoxesBy(dx, dy);
    }

    private (double X, double Y) _refBoxesMoveDelta;

    /// <summary>The selected boxes on the active sheet.</summary>
    private List<ReferenceCell> SelectedRefBoxes()
    {
        if (ActiveReference?.Cells is not { Count: > 0 } cells) return [];
        var picked = new List<ReferenceCell>();
        foreach (var index in _selectionManager.SelectedRefBoxIndices)
        {
            if (index < 0 || index >= cells.Count) continue;
            picked.Add(cells[index]);
        }
        return picked;
    }

    /// <summary>Record a finished group box move as one step.</summary>
    /// <remarks>
    /// <para>
    /// <c>Dx</c>/<c>Dy</c>, which is what <see cref="MoveReferenceCell"/>
    /// moves, and the distinction is the whole of this method. A cell's
    /// <c>X</c>/<c>Y</c> are the window onto the sheet in sheet pixels — moving
    /// those scrolls the photograph inside the box and leaves the box where it
    /// was, which is a different operation and destroys the registration the
    /// pivot exists to hold. They are also <c>int</c>, so a drag reported in
    /// fractions of a pixel would be rounded away a frame at a time.
    /// </para>
    /// <para>
    /// Rewound and replayed like the guides, for the reason on
    /// <see cref="MoveGuidesBy"/>, and holding the cells by reference for the
    /// same one.
    /// </para>
    /// </remarks>
    private void MoveRefBoxesBy(double dx, double dy)
    {
        var moved = SelectedRefBoxes();
        if (moved.Count == 0) return;

        foreach (var cell in moved)
        {
            cell.Dx -= dx;
            cell.Dy -= dy;
        }
        _editor.PerformDelta(
            _ =>
            {
                foreach (var cell in moved)
                {
                    cell.Dx += dx;
                    cell.Dy += dy;
                }
                NotifyReference();
            },
            _ =>
            {
                foreach (var cell in moved)
                {
                    cell.Dx -= dx;
                    cell.Dy -= dy;
                }
                NotifyReference();
            });
        AfterReferenceChange();
    }

    /// <summary>Begin moving selected anchors.</summary>
    public void BeginAnchorsMove()
    {
        if (_selectionManager.SelectedAnchorIds.Count == 0) return;
        _anchorsMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedAnchorIds.Count} anchor(s)";
    }

    /// <summary>Update anchor move by the delta since the last pointer event.</summary>
    public void UpdateAnchorsMove(double dx, double dy)
    {
        _anchorsMoveDelta = (_anchorsMoveDelta.X + dx, _anchorsMoveDelta.Y + dy);
        RequestSnapshot();
    }

    /// <summary>Commit anchor moves.</summary>
    public void EndAnchorsMove()
    {
        var (dx, dy) = _anchorsMoveDelta;
        _anchorsMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveAnchorsBy(dx, dy);
    }

    private (double X, double Y) _anchorsMoveDelta;

    /// <summary>Apply movement delta to selected anchors.</summary>
    private void MoveAnchorsBy(double dx, double dy)
    {
        var selectedAnchorIds = _selectionManager.SelectedAnchorIds.ToList();
        if (selectedAnchorIds.Count == 0) return;

        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.PerformDelta(
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var anchors = Anchors.ResolvedAt(doc.Scene, frame);
                foreach (var anchorId in selectedAnchorIds)
                {
                    if (anchors.TryGetValue(anchorId, out var point))
                    {
                        Anchors.SetAcross(layer, frame, 1, anchorId, new Core.Documents.AnchorPoint(point.X + dx, point.Y + dy));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            },
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var anchors = Anchors.ResolvedAt(doc.Scene, frame);
                foreach (var anchorId in selectedAnchorIds)
                {
                    if (anchors.TryGetValue(anchorId, out var point))
                    {
                        Anchors.SetAcross(layer, frame, 1, anchorId, new Core.Documents.AnchorPoint(point.X - dx, point.Y - dy));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            });
    }

    /// <summary>Begin moving selected collision shapes.</summary>
    public void BeginShapesMove()
    {
        if (_selectionManager.SelectedShapeIds.Count == 0) return;
        _shapesMoveDelta = (0, 0);
        AiStatus = $"Moving {_selectionManager.SelectedShapeIds.Count} shape(s)";
    }

    /// <summary>Update collision shape move by the delta since the last pointer event.</summary>
    public void UpdateShapesMove(double dx, double dy)
    {
        _shapesMoveDelta = (_shapesMoveDelta.X + dx, _shapesMoveDelta.Y + dy);
        RequestSnapshot();
    }

    /// <summary>Commit collision shape moves.</summary>
    public void EndShapesMove()
    {
        var (dx, dy) = _shapesMoveDelta;
        _shapesMoveDelta = default;
        if (Math.Abs(dx) < 1e-9 && Math.Abs(dy) < 1e-9) return;
        MoveShapesBy(dx, dy);
    }

    private (double X, double Y) _shapesMoveDelta;

    /// <summary>Apply movement delta to selected collision shapes.</summary>
    private void MoveShapesBy(double dx, double dy)
    {
        var selectedShapeIds = _selectionManager.SelectedShapeIds.ToList();
        if (selectedShapeIds.Count == 0) return;

        var layerId = ActiveLayer.Id;
        var frame = CurrentFrameIndex;

        _editor.PerformDelta(
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var shapes = CollisionShapes.ResolvedAt(doc.Scene, frame);
                foreach (var shapeId in selectedShapeIds)
                {
                    if (shapes.TryGetValue(shapeId, out var box))
                    {
                        CollisionShapes.SetAcross(layer, frame, 1, shapeId, new Core.Documents.ShapeBox(box.X + dx, box.Y + dy, box.W, box.H));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            },
            doc =>
            {
                if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
                var shapes = CollisionShapes.ResolvedAt(doc.Scene, frame);
                foreach (var shapeId in selectedShapeIds)
                {
                    if (shapes.TryGetValue(shapeId, out var box))
                    {
                        CollisionShapes.SetAcross(layer, frame, 1, shapeId, new Core.Documents.ShapeBox(box.X - dx, box.Y - dy, box.W, box.H));
                    }
                }
                OnPropertyChanged(nameof(RigMarks));
            });
    }

    partial void OnTransformScopeChanged(TransformScope value)
    {
        // Changing scope mid-session re-collects under the new scope.
        if (TransformActive) BeginTransform();
    }

    /// <summary>Distinct drawings in scope (holds share Frame instances — dedupe by id).</summary>
    private List<Frame> CollectTransformFrames()
    {
        var frames = new List<Frame>();
        var seen = new HashSet<string>();
        void Add(Frame? f)
        {
            if (f is not null && seen.Add(f.Id)) frames.Add(f);
        }
        switch (TransformScope)
        {
            case TransformScope.ActiveCel:
                Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex));
                break;
            case TransformScope.AllLayersAtFrame:
                foreach (var layer in Scene.Layers)
                {
                    if (Scene.IsLayerVisible(layer)) Add(ExposureSheet.ExposedFrame(layer, CurrentFrameIndex));
                }
                break;
            case TransformScope.ActiveLayerAllFrames:
                foreach (var cel in ActiveLayer.Cels) Add(cel.Frame);
                break;
            case TransformScope.CelRange:
                if (_celRange is { } r && r.Layer >= 0 && r.Layer < Scene.Layers.Count)
                {
                    var layer = Scene.Layers[r.Layer];
                    for (var i = r.Start; i <= r.End; i++) Add(ExposureSheet.ExposedFrame(layer, i));
                }
                else
                {
                    Add(ExposureSheet.ExposedFrame(ActiveLayer, CurrentFrameIndex)); // no range marked
                }
                break;
            case TransformScope.EntireAnimation:
                foreach (var layer in Scene.Layers)
                {
                    foreach (var cel in layer.Cels) Add(cel.Frame);
                }
                break;
        }
        return frames;
    }

    public void CancelTransform()
    {
        if (!TransformActive) return;
        EndTransformSession();
    }

    private void EndTransformSession()
    {
        TransformActive = false;
        _transformFrames.Clear();
        _transformFilter = null;
        ClearTransformPreview();
        TransformEnded?.Invoke();
    }

    // ---- live transform preview -------------------------------------------------

    /// <summary>
    /// The gizmo's current shape, in document space, or null when nothing is
    /// being previewed.
    /// </summary>
    /// <remarks>
    /// A transform used to show only its outline: the drawing sat still under
    /// a moving box, and the artist did not see the result until they pressed
    /// Enter. Judging a rotation from a rectangle is guesswork, and the same
    /// argument the live-stroke preview settled applies here — what you are
    /// making has to be visible while you make it.
    ///
    /// This is a <b>composite-time</b> matrix and nothing else. The stroke
    /// record is mapped once, on apply, by <see cref="CommitTransformCore"/>.
    /// Re-mapping N frames of geometry on every pointer event would be both
    /// slow and destructive of undo; moving finished pixels for one frame is
    /// neither.
    /// </remarks>
    private SKMatrix? _transformPreview;

    /// <summary>
    /// A frame's pixels split into the part the transform moves and the part
    /// it leaves behind. With no selection everything moves and
    /// <see cref="Static"/> is null, which is the ordinary case and costs no
    /// extra render at all — <see cref="Moving"/> is the frame's own cached
    /// bitmap, borrowed rather than copied.
    /// </summary>
    private sealed record TransformParts(SKBitmap Moving, SKBitmap? Static, bool Owned);

    private readonly Dictionary<string, TransformParts> _transformParts = [];

    /// <summary>
    /// Show the drag. Null clears the preview and puts the pixels back where
    /// the record says they are.
    /// </summary>
    public void PreviewTransform(SKMatrix? matrix)
    {
        if (!TransformActive)
        {
            if (_transformPreview is null) return;
            matrix = null;
        }
        // Identity is "no preview": it renders the same and skips the split.
        if (matrix is { } m && m.IsIdentity) matrix = null;
        if (_transformPreview is null && matrix is null) return;
        _transformPreview = matrix;
        // The drawing can land anywhere on the canvas, so no dirty region is
        // safe here.
        InvalidateWholeCanvas();
        RequestSnapshot();
    }

    private void ClearTransformPreview()
    {
        _transformPreview = null;
        foreach (var parts in _transformParts.Values)
        {
            if (!parts.Owned) continue;
            parts.Moving.Dispose();
            parts.Static?.Dispose();
        }
        _transformParts.Clear();
    }

    /// <summary>
    /// The moving/static split for one in-scope frame, built once per session
    /// and reused for every pointer event of the drag. Null when this frame
    /// cannot be previewed honestly — a partial selection over a frame that is
    /// not a stroke record — in which case the gizmo alone stands in, as it
    /// did before.
    /// </summary>
    private TransformParts? PartsFor(Frame frame)
    {
        if (_transformFilter is null)
        {
            // Everything moves, so the layer bitmap already IS the moving part
            // and no render is needed. It is deliberately re-fetched rather
            // than remembered: the cache owns and disposes these, and a
            // remembered one becomes a dangling pointer the moment anything
            // invalidates the frame.
            return new TransformParts(_cache.Get(frame, Scene.Width, Scene.Height), null, Owned: false);
        }

        if (_transformParts.TryGetValue(frame.Id, out var cached)) return cached;

        TransformParts? parts;
        if (frame is Frame painted && _transformFilter is { } filter)
        {
            var moving = painted.Strokes.Where(filter).ToList();
            var rest = painted.Strokes.Where(s => !filter(s)).ToList();
            SKBitmap stay;
            if (painted.PngBase64 is { Length: > 0 })
            {
                // The baseline stays put under a region-limited transform,
                // exactly as the commit leaves it — and it goes underneath the
                // strokes that stay, because that is the order it renders in.
                stay = new SKBitmap(new SKImageInfo(
                    Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
                using (var canvas = new SKCanvas(stay))
                {
                    canvas.Clear(SKColors.Transparent);
                    using var baseline = Lightbox.Raster.PngCodec.Decode(painted.PngBase64);
                    canvas.DrawBitmap(baseline, 0, 0);
                }
                foreach (var s in rest) FrameRasterizer.Append(stay, s);
            }
            else
            {
                stay = FrameRasterizer.Rasterize(rest, Scene.Width, Scene.Height);
            }
            parts = new TransformParts(
                FrameRasterizer.Rasterize(moving, Scene.Width, Scene.Height), stay, Owned: true);
        }
        else
        {
            parts = null;
        }

        if (parts is not null) _transformParts[frame.Id] = parts;
        return parts;
    }

    /// <summary>Commit a move/scale/rotate/mirror (mirror = negative scale) around a pivot.</summary>
    public void CommitTransformAffine(
        double pivotX, double pivotY,
        double scaleX, double scaleY,
        double angleRadians,
        double offsetX, double offsetY)
    {
        if (!TransformActive) return;
        var map = TransformOps.Affine(pivotX, pivotY, scaleX, scaleY, angleRadians, offsetX, offsetY);
        var sizeScale = Math.Sqrt(Math.Abs(scaleX * scaleY));
        var m = SKMatrix.CreateTranslation((float)-pivotX, (float)-pivotY);
        m = m.PostConcat(SKMatrix.CreateScale((float)scaleX, (float)scaleY));
        m = m.PostConcat(SKMatrix.CreateRotation((float)angleRadians));
        m = m.PostConcat(SKMatrix.CreateTranslation((float)(pivotX + offsetX), (float)(pivotY + offsetY)));
        CommitTransformCore(map, sizeScale, m);
    }

    /// <summary>Commit a four-corner perspective transform.</summary>
    public void CommitTransformPerspective(double[] srcQuad, double[] dstQuad)
    {
        if (!TransformActive) return;
        double[] h;
        try
        {
            h = TransformOps.PerspectiveCoefficients(srcQuad, dstQuad);
        }
        catch (InvalidOperationException)
        {
            AiStatus = "That corner arrangement collapses the drawing — adjust the corners and try again.";
            return;
        }
        var map = TransformOps.Perspective(srcQuad, dstQuad);
        var m = new SKMatrix(
            (float)h[0], (float)h[1], (float)h[2],
            (float)h[3], (float)h[4], (float)h[5],
            (float)h[6], (float)h[7], 1f);
        CommitTransformCore(map, 1, m);
    }

    private void CommitTransformCore(TransformOps.PointMap map, double sizeScale, SKMatrix baselineMatrix)
    {
        var frames = _transformFrames.ToList();
        var filter = _transformFilter;
        // The preview goes first, and not only for tidiness: it borrows the
        // cache's own bitmaps, and the invalidation below disposes them.
        ClearTransformPreview();
        // Invalidate before the edit so the Changed refresh re-renders from
        // the transformed record.
        foreach (var frame in frames) InvalidateFrameRender(frame.Id);
        _editor.Perform(_ =>
        {
            foreach (var frame in frames)
            {
                TransformOps.TransformFrame(frame, map, sizeScale, filter);
                // Raster baselines resample once per commit; a region-limited
                // transform moves strokes only (baseline pixels stay put).
                if (filter is null && frame is Frame { PngBase64.Length: > 0 } painted)
                {
                    ResampleBaseline(painted, baselineMatrix);
                }
            }
        });
        // The selection outline rides along so a follow-up transform lines up.
        if (HasSelection)
        {
            foreach (var contour in _selectionContours)
            {
                for (var i = 0; i < contour.Count; i++)
                {
                    var pt = contour[i];
                    var (x, y) = map(pt.X, pt.Y);
                    contour[i] = pt with { X = x, Y = y };
                }
            }
            NotifySelection();
        }
        EndTransformSession();
        AiStatus = $"Transformed {frames.Count} drawing{(frames.Count == 1 ? "" : "s")}.";
    }

    private SKSamplingOptions SamplingFor(TransformSampling mode) => mode switch
    {
        TransformSampling.Nearest => new SKSamplingOptions(SKFilterMode.Nearest),
        TransformSampling.Bicubic => new SKSamplingOptions(SKCubicResampler.Mitchell),
        _ => new SKSamplingOptions(SKFilterMode.Linear),
    };

    /// <summary>
    /// Re-render a frame's baseline pixels through a matrix. Does nothing when the
    /// frame has no baseline.
    /// </summary>
    /// <remarks>
    /// The early return rather than a <c>!</c> at the one call site: the baseline
    /// is nullable now, every caller already tests it, and a method that is correct
    /// on its own does not need the next caller to remember. Nothing to resample is
    /// not an error.
    /// </remarks>
    private void ResampleBaseline(Frame frame, SKMatrix matrix)
    {
        if (frame.PngBase64 is not { Length: > 0 } encoded) return;
        try
        {
            var bytes = Convert.FromBase64String(encoded);
            using var src = SKBitmap.Decode(bytes);
            if (src is null) return;
            var info = new SKImageInfo(Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            if (surface is null) return;
            surface.Canvas.Clear(SkiaSharp.SKColors.Transparent);
            surface.Canvas.SetMatrix(matrix);
            using var image = SKImage.FromBitmap(src);
            surface.Canvas.DrawImage(image, 0, 0, SamplingFor(TransformSampling));
            using var snap = surface.Snapshot();
            using var data = snap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            frame.PngBase64 = Convert.ToBase64String(data.ToArray());
        }
        catch (FormatException)
        {
            // Corrupt baseline: leave it untouched rather than destroy pixels.
        }
    }

    // ---- layer reordering -------------------------------------------------------

    /// <summary>Move a layer toward the viewer (+1) or away (−1), keeping it active.</summary>
    internal void MoveLayer(LayerRow row, int delta)
    {
        var id = row.Layer.Id;
        _editor.MoveLayer(id, delta);
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == id);
    }

    [RelayCommand]
    private void MoveLayerUp(LayerRow row) => MoveLayer(row, +1);

    [RelayCommand]
    private void MoveLayerDown(LayerRow row) => MoveLayer(row, -1);

    // ---- layer folders ----------------------------------------------------------

    /// <summary>The docker's item list: folder headers followed by their (uncollapsed) member rows.</summary>
    public ObservableCollection<object> LayerPanelItems { get; } = [];

    private void RebuildLayerPanel()
    {
        LayerPanelItems.Clear();
        var emitted = new HashSet<string>();
        foreach (var row in LayerRows) // topmost first
        {
            var group = Scene.GroupOf(row.Layer);
            if (group is null)
            {
                LayerPanelItems.Add(row);
                continue;
            }
            if (emitted.Add(group.Id)) LayerPanelItems.Add(new GroupRow(this, group));
            if (!group.Collapsed) LayerPanelItems.Add(row);
        }
    }

    /// <summary>New folder containing the active layer.</summary>
    [RelayCommand]
    private void CreateLayerFolder()
    {
        var layer = ActiveLayer;
        _editor.Perform(doc =>
        {
            var group = new LayerGroup { Name = $"Folder {doc.Scene.LayerGroups.Count + 1}" };
            doc.Scene.LayerGroups.Add(group);
            layer.GroupId = group.Id;
        });
    }

    /// <summary>Put the active layer into this folder (moved adjacent so the folder stays one block).</summary>
    [RelayCommand]
    private void AddActiveLayerToGroup(GroupRow header)
    {
        var layer = ActiveLayer;
        if (layer.GroupId == header.Group.Id) return;
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            layers.Remove(layer);
            var top = -1;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].GroupId == header.Group.Id) top = i;
            }
            if (top < 0)
            {
                layers.Add(layer); // empty folder: just append
            }
            else
            {
                layers.Insert(top + 1, layer); // top of the folder's block
            }
            layer.GroupId = header.Group.Id;
        });
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary>Take a layer out of its folder (placed just above the folder's block).</summary>
    [RelayCommand]
    private void RemoveLayerFromGroup(LayerRow row)
    {
        var layer = row.Layer;
        if (layer.GroupId is null) return;
        var groupId = layer.GroupId;
        _editor.Perform(doc =>
        {
            var layers = doc.Scene.Layers;
            layers.Remove(layer);
            var top = -1;
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i].GroupId == groupId) top = i;
            }
            layers.Insert(top < 0 ? layers.Count : top + 1, layer);
            layer.GroupId = null;
        });
        ActiveLayerIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
    }

    /// <summary>Dissolve the folder: its layers stay, ungrouped.</summary>
    [RelayCommand]
    private void DissolveGroup(GroupRow header)
    {
        _editor.Perform(doc =>
        {
            foreach (var layer in doc.Scene.Layers)
            {
                if (layer.GroupId == header.Group.Id) layer.GroupId = null;
            }
            doc.Scene.LayerGroups.RemoveAll(g => g.Id == header.Group.Id);
        });
    }

    internal void CommitGroupRename(LayerGroup group, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || group.Name == trimmed) return;
        _editor.Perform(_ => group.Name = trimmed);
    }

    internal void SetGroupVisible(LayerGroup group, bool visible)
    {
        if (group.Visible == visible) return;
        _editor.Perform(_ => group.Visible = visible);
    }

    internal void SetGroupColor(LayerGroup group, string color)
    {
        if (group.Color == color) return;
        _editor.Perform(_ => group.Color = color);
    }

    /// <summary>Collapse is a view preference: persisted, but not an undo step.</summary>
    internal void SetGroupCollapsed(LayerGroup group, bool collapsed)
    {
        if (group.Collapsed == collapsed) return;
        group.Collapsed = collapsed;
        MarkDocumentEdited();
        RebuildLayerPanel();
    }

    private void RefreshRangeHighlights()
    {
        var rangeSet = PlaybackStartFrame >= 0 || PlaybackEndFrame >= 0;
        var start = EffectiveStartFrame;
        var end = EffectiveEndFrame;
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells)
            {
                cell.OutOfRange = rangeSet && !cell.IsVirtual && (cell.Index < start || cell.Index > end);
            }
        }
    }

    [RelayCommand]
    private void AddFrame()
    {
        _editor.AddFrameAfter(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DuplicateFrame()
    {
        _editor.DuplicateFrame(CurrentFrameIndex);
        CurrentFrameIndex++;
    }

    [RelayCommand]
    private void DeleteFrame()
    {
        if (Scene.FrameCount <= 1) return;
        _editor.DeleteFrame(CurrentFrameIndex);
        CurrentFrameIndex = Math.Min(CurrentFrameIndex, Scene.FrameCount - 1);
    }

    [RelayCommand]
    private void Undo()
    {
        // The stroke the next Shift+click would have joined to may be the one
        // going away. Joining to a mark that is no longer there draws a line
        // out of nowhere, which is worse than not joining at all.
        _lastStrokeEnd = null;
        // An uncommitted palette edit is not on the stack yet; undoing before
        // it lands would step over it and then have it reappear.
        CommitSwatchEdit();
        ApplyEditScope(WhileApplyingScope(_editor.UndoScoped));
    }

    [RelayCommand]
    private void Redo()
    {
        CommitSwatchEdit();
        ApplyEditScope(WhileApplyingScope(_editor.RedoScoped));
    }

    private DocumentEditor.EditScope WhileApplyingScope(Func<DocumentEditor.EditScope> step)
    {
        _applyingEditScope = true;
        try
        {
            return step();
        }
        finally
        {
            _applyingEditScope = false;
        }
    }

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

    private void ApplyEditScope(DocumentEditor.EditScope scope)
    {
        if (!scope.Any) return;
        if (scope.FrameId is { } frameId)
        {
            InvalidateFrameRender(frameId);
            _dirtyThumbIds.Add(frameId);
        }
        else
        {
            ClearFrameRenders();
            _allThumbsDirty = true;
        }
        ClampCurrentFrame(publishIfUnchanged: false);
        PublishSnapshot();
        RefreshThumbnails();
    }

    /// <summary>
    /// Adds a drawing layer. There is one kind of layer to add.
    /// </summary>
    /// <remarks>
    /// This was three commands and a dropdown — <c>AddPaintedLayer</c>,
    /// <c>AddVectorLayer</c> and an <c>AddLayerOfSelectedKind</c> reading a
    /// Raster/Vector picker. The choice never meant anything an artist could act
    /// on: both kinds drew the same strokes through the same engine, and picking
    /// Vector only subtracted the ability to hold imported pixels or a symbol
    /// placement. Asking the question up front made the artist guess at a
    /// limitation instead of choosing a capability. Q52.
    /// </remarks>
    [RelayCommand]
    private void AddPaintedLayer() => AddLayer(LayerKind.Painted);

    [RelayCommand]
    private void ToggleSidebar() => SidebarVisible = !SidebarVisible;

    [RelayCommand]
    private void SwitchSidebarSide() => SidebarOnRight = !SidebarOnRight;

    [RelayCommand]
    private void ToggleTimeline() => TimelineVisible = !TimelineVisible;

    [RelayCommand]
    private void ActivateLayer(LayerRow row) => ActiveLayerIndex = row.SceneIndex;

    /// <summary>Rename as one undoable step (called by the row on commit).</summary>
    internal void CommitLayerRename(LayerRow row, string name)
    {
        var layer = row.Layer;
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            // Snap the row back to the document name instead of storing a blank.
            row.SyncFromModel(layer, row.SceneIndex);
            return;
        }
        if (layer.Name == trimmed) return;
        _editor.Perform(_ => layer.Name = trimmed);
    }

    internal void SetLayerVisible(Layer layer, bool visible)
    {
        if (layer.Visible == visible) return;
        _editor.Perform(_ => layer.Visible = visible);
    }

    /// <summary>
    /// Undoable, like visibility — locking is a document decision an artist
    /// can change their mind about, not a view preference.
    /// </summary>
    internal void SetLayerLocked(Layer layer, bool locked)
    {
        if (layer.Locked == locked) return;
        _editor.Perform(_ => layer.Locked = locked);
        NotifyLayerGating();
    }

    internal void SetLayerAlphaLocked(Layer layer, bool locked)
    {
        if (layer.AlphaLocked == locked) return;
        _editor.Perform(_ => layer.AlphaLocked = locked);
        NotifyLayerGating();
    }

    /// <summary>Locking a folder locks every layer inside it.</summary>
    internal void SetGroupLocked(LayerGroup group, bool locked)
    {
        if (group.Locked == locked) return;
        _editor.Perform(_ => group.Locked = locked);
        SyncLayerRows();
        NotifyLayerGating();
    }

    /// <summary>Lock or unlock the active layer (keyboard and menu path).</summary>
    [RelayCommand]
    private void ToggleActiveLayerLocked()
    {
        if (ActiveLayer is { } layer) SetLayerLocked(layer, !layer.Locked);
    }

    [RelayCommand]
    private void ToggleActiveLayerAlphaLocked()
    {
        if (ActiveLayer is { } layer) SetLayerAlphaLocked(layer, !layer.AlphaLocked);
    }

    private void NotifyLayerGating()
    {
        OnPropertyChanged(nameof(ActiveLayerBlocked));
        OnPropertyChanged(nameof(ActiveLayerAlphaLocked));
    }

    /// <summary>A row needs this to dim itself without reaching into the scene.</summary>
    internal bool IsLayerLockedByFolder(Layer layer) => Scene.GroupOf(layer) is { Locked: true };

    /// <summary>Shown in the tool options so the restriction is never invisible.</summary>
    public bool ActiveLayerAlphaLocked => ActiveLayer is { AlphaLocked: true };

    /// <summary>
    /// Per-layer onion-skin participation. A display preference, so it is
    /// persisted (autosave) but deliberately not an undo step.
    /// </summary>
    internal void SetLayerOnionEnabled(Layer layer, bool enabled)
    {
        if (layer.OnionEnabled == enabled) return;
        layer.OnionEnabled = enabled;
        // The same switch exists in two places — the Layers panel's ◉ and the
        // shortcut bar's — and they have to agree. The row does not read the
        // layer except when it is rebuilt, so pushing the value across is what
        // stops one of them showing yesterday's answer.
        if (LayerRows.FirstOrDefault(r => r.Layer.Id == layer.Id) is { } row)
        {
            row.OnionEnabled = enabled;
        }
        if (layer.Id == ActiveLayer.Id) OnPropertyChanged(nameof(ActiveLayerOnion));
        MarkDocumentEdited();
        PublishSnapshot();
    }

    // ---- active layer compositing (opacity + blend mode) ----------------------

    public IReadOnlyList<LayerBlendMode> BlendModeChoices { get; } = Enum.GetValues<LayerBlendMode>();

    /// <summary>
    /// Active layer's opacity, 0–100 for the docker slider. Applied live while
    /// dragging, so deliberately not an undo step (an undo snapshot per slider
    /// tick would flood the history).
    /// </summary>
    public double ActiveLayerOpacity
    {
        get => Math.Round(ActiveLayer.Opacity * 100);
        set
        {
            var clamped = Math.Clamp(value / 100.0, 0, 1);
            if (Math.Abs(ActiveLayer.Opacity - clamped) < 0.0005) return;
            ActiveLayer.Opacity = clamped;
            MarkDocumentEdited();
            OnPropertyChanged();
            PublishSnapshot();
        }
    }

    /// <summary>Active layer's blend mode — a deliberate compositing choice, one undo step.</summary>
    public LayerBlendMode ActiveLayerBlendMode
    {
        get => ActiveLayer.BlendMode;
        set
        {
            if (ActiveLayer.BlendMode == value) return;
            var layer = ActiveLayer;
            _editor.Perform(_ => layer.BlendMode = value);
            OnPropertyChanged();
        }
    }

    private void NotifyActiveLayerCompositing()
    {
        OnPropertyChanged(nameof(ActiveLayerOpacity));
        OnPropertyChanged(nameof(ActiveLayerBlendMode));
    }

    /// <summary>Delete the active layer; an empty document always regrows one blank layer.</summary>
    [RelayCommand]
    private void DeleteActiveLayer() => DeleteLayer(ActiveLayer);

    public void DeleteLayer(Layer layer)
    {
        if (!CanEdit(layer, "delete it")) return;
        var removedIndex = Scene.Layers.FindIndex(l => l.Id == layer.Id);
        if (removedIndex < 0) return;
        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            var index = scene.Layers.FindIndex(l => l.Id == layer.Id);
            if (index < 0) return;
            var wasPaper = scene.Layers[index].IsBackground;
            scene.Layers.RemoveAt(index);
            // Deleting the paper means there is no paper. Without this the
            // composite falls back to clearing to the scene's colour, so the
            // canvas goes opaque white and the deletion looks like it did
            // nothing — the one thing it must not look like.
            if (wasPaper && !scene.Layers.Exists(l => l.IsBackground))
            {
                scene.TransparentBackground = true;
            }
            // Regrow when nothing PAINTABLE is left, not merely when nothing is
            // left: a document down to its locked paper has layers and still
            // nowhere to draw.
            if (!scene.Layers.Any(l => !l.IsBackground))
            {
                var fresh = new Layer
                {
                    Name = "Paint 1",
                    Cels = [new Cel { Frame = new Frame() }],
                };
                while (fresh.Cels.Count < scene.FrameCount) fresh.Cels.Add(new Cel());
                scene.Layers.Add(fresh);
            }
        });
        var next = Math.Clamp(removedIndex, 0, Scene.Layers.Count - 1);
        // Never land on the paper: it is locked, so the next stroke would bounce.
        ActiveLayerIndex = Scene.Layers[next].IsBackground ? FirstPaintableLayer(Doc) : next;
    }

    /// <summary>Blank the active layer: every drawing on it loses its content, the timing stays.</summary>
    [RelayCommand]
    private void ClearActiveLayer() => ClearLayerContent(ActiveLayer);

    /// <summary>
    /// Set a project document's status, and export it if that is what the artist asked
    /// the app to do on that status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shortcut through the whole export pillar: finish an asset, mark it Ready, and
    /// the sheet lands where the engine is already looking. Nobody has to remember to
    /// export — and the export nobody remembered is the one that makes a designer think
    /// the artist has not started.
    /// </para>
    /// <para>
    /// <b>The order is load-bearing. The status is written and saved first; the export is
    /// a consequence.</b> If the destination is missing, locked by the engine, or on a
    /// drive that is not mounted, the artist gets a message and keeps their status.
    /// Refusing the status change because a file could not be written would make a
    /// production field hostage to a network share.
    /// </para>
    /// <para>
    /// The document is read from the open tab when there is one, so an unsaved edit
    /// exports as the artist sees it rather than as the file last had it. Otherwise it
    /// comes off disk, which is the point of statuses living on the manifest: marking
    /// something Ready never needed it open.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Where a project row's document lives, and whether the file is behind the edits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The facts <see cref="Services.SaveRequirement"/> needs, for <em>this row</em> rather
    /// than for whatever tab happens to be in front. Marking a row Ready is a statement
    /// about that row's drawing, and gating it on the active tab would ask about the wrong
    /// file — which is exactly the kind of near-miss that reads as the app being random.
    /// </para>
    /// <para>
    /// A row whose document is open answers from the tab, because that is where the unsaved
    /// edits are. Otherwise the answer is the project's own path for it: a freshly added
    /// animation has a <c>DocumentRef</c> before it has a file, so this returns a path that
    /// does not exist yet and the gate reports it honestly.
    /// </para>
    /// </remarks>
    public (string? FilePath, bool HasUnsavedEdits) SaveFactsFor(ProjectRow row)
    {
        if (row.Animation is not { } reference) return (null, false);

        if (Tabs.FirstOrDefault(t => t.Source?.Id == reference.Id) is { } open)
        {
            return (open.FilePath ?? Resolve(reference), open.IsDirty);
        }
        // Not open, so nothing can be unsaved about it beyond whether it was ever written.
        return (Resolve(reference), false);

        string? Resolve(DocumentRef r) =>
            ProjectDocker.RootPath is { Length: > 0 } root && r.Path is { Length: > 0 }
                ? System.IO.Path.Combine(root, r.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))
                : null;
    }

    public void SetProjectStatus(ProjectRow row, AssetStatus? status)
    {
        if (row.Animation is not { } reference) return;

        var before = ProjectDocker.SetStatus(row, status);
        if (status is not { } now) return;

        var settings = Settings.AutoExport;
        var root = ProjectDocker.RootPath;

        // Decided before anything is loaded, so the ordinary case — auto-export off —
        // costs a comparison rather than reading a document off disk.
        var (folder, outcome) = AutoExport.Decide(before, now, settings, root);
        if (folder is null)
        {
            if (AutoExport.Explain(outcome, settings) is { Length: > 0 } why) AiStatus = why;
            return;
        }

        var doc = Tabs.FirstOrDefault(t => t.Source?.Id == reference.Id)?.Editor.Doc
                  ?? (ProjectDocker.Project is { } project
                      ? ProjectIo.LoadDocument(project, reference)
                      : null);
        if (doc is null)
        {
            AiStatus = $"Status saved, but {reference.Name} could not be read to export it.";
            return;
        }

        var report = AutoExport.Run(
            doc, reference.Name, before, now, settings, ProjectDocker.RootPath, ExportPresets());
        if (report.Message is { Length: > 0 }) AiStatus = report.Message;
    }

    /// <summary>Built-in export presets plus the artist's own.</summary>
    private static List<ExportPreset> ExportPresets() =>
        ExportPreset.BuiltIns.Concat(ExportPresetStore.Load()).ToList();

    /// <summary>
    /// One status line for a finished export, including what it left out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The omissions are in the sentence rather than behind a dialog, and that is the
    /// point of reporting them at all: "a layer I wanted is missing" is invisible in
    /// the sheet and surfaces in the engine, on a build, days later. Naming the layers
    /// is what turns that into something an artist notices now.
    /// </para>
    /// <para>
    /// Names, not a count. "2 layers left out" is exactly as unhelpful as silence when
    /// the question is <em>which</em>.
    /// </para>
    /// </remarks>
    public string DescribeExport(Lightbox.App.Services.ExportRun run)
    {
        var parts = new List<string> { run.Summary };

        if (run.Omitted.Count > 0)
        {
            var named = run.Omitted.Select(o => $"{o.Name} ({Reason(o.Signal)})");
            parts.Add("left out: " + string.Join(", ", named));
        }
        if (run.Suspected.Count > 0)
        {
            parts.Add("kept but looks like a background: "
                      + string.Join(", ", run.Suspected.Select(s => s.Name)));
        }
        return string.Join(" — ", parts);

        static string Reason(Lightbox.Core.Export.BackgroundSignal signal) => signal switch
        {
            Lightbox.Core.Export.BackgroundSignal.Pinned => "never export",
            Lightbox.Core.Export.BackgroundSignal.Paper => "paper",
            Lightbox.Core.Export.BackgroundSignal.Hidden => "hidden",
            _ => "fills the canvas",
        };
    }

    /// <summary>
    /// Pin a layer out of exports, into them, or back to letting the export decide.
    /// </summary>
    /// <param name="pin">
    /// <c>true</c> never export, <c>false</c> always export, <c>null</c> let
    /// <see cref="Lightbox.Core.Export.BackgroundHandling"/> decide.
    /// </param>
    /// <remarks>
    /// Through the editor, so it is one undo step and marks the document dirty — this
    /// changes what leaves the app, which makes it document state rather than a
    /// preference. No cache invalidation and no thumbnail refresh: it reaches the
    /// export and never a pixel on the canvas.
    /// </remarks>
    public void SetLayerExportPin(Layer layer, bool? pin)
    {
        if (layer.OmitFromExport == pin) return;
        _editor.Perform(doc =>
        {
            if (doc.Scene.Layers.FirstOrDefault(l => l.Id == layer.Id) is { } target)
                target.OmitFromExport = pin;
        });
        SyncLayerRows();
    }

    public void ClearLayerContent(Layer layer)
    {
        // Mark before the edit so the thumbnail refresh inside Changed sees them.
        foreach (var cel in layer.Cels)
        {
            if (cel.Frame is { } frame)
            {
                InvalidateFrameRender(frame.Id);
                _dirtyThumbIds.Add(frame.Id);
            }
        }
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.FirstOrDefault(l => l.Id == layer.Id);
            if (target is null) return;
            foreach (var cel in target.Cels)
            {
                if (cel.Frame is not { } frame) continue;
                frame.Strokes.Clear();
                // Null rather than "": clearing a layer should leave frames that
                // write no baseline key, the same as ones that never had one.
                frame.PngBase64 = null;
            }
        });
    }

    /// <remarks>
    /// <paramref name="kind"/> is still written, because <c>Layer.Kind</c> is kept
    /// as provenance — it records where a layer's content came from, and every
    /// layer created in the app comes from the same place. It no longer picks a
    /// frame class, because there is one.
    /// </remarks>
    private void AddLayer(LayerKind kind)
    {
        _editor.Perform(doc =>
        {
            var layer = new Layer
            {
                Name = $"Paint {doc.Scene.Layers.Count + 1}",
                Kind = kind,
                Cels = [new Cel { Frame = new Frame() }],
            };
            while (layer.Cels.Count < doc.Scene.FrameCount) layer.Cels.Add(new Cel());
            doc.Scene.Layers.Add(layer);
        });
        ActiveLayerIndex = Scene.Layers.Count - 1;
    }

    [RelayCommand]
    private void ToggleActiveLayerVisible()
    {
        _editor.Perform(_ => ActiveLayer.Visible = !ActiveLayer.Visible);
        RefreshPointerIntent();
    }

    /// <summary>Clicking a cel selects both the frame and the layer it belongs to.</summary>
    [RelayCommand]
    private void SelectFrame(FrameCell cell)
    {
        if (cell.IsVirtual) return; // no frame there yet — insert one via right-click
        if (cell.LayerIndex >= 0 && cell.LayerIndex < Scene.Layers.Count)
            ActiveLayerIndex = cell.LayerIndex;
        CurrentFrameIndex = cell.Index;
        _celAnchor = (cell.LayerIndex, cell.Index);
        ClearCelRange();
    }

    // ---- multi-cel range selection ------------------------------------------------

    private (int Layer, int Index) _celAnchor;
    private (int Layer, int Start, int End)? _celRange;

    /// <summary>The selected cel range on one layer row (Shift+click), if any.</summary>
    public (int Layer, int Start, int End)? CelRange => _celRange;

    /// <summary>Shift+click: select the contiguous range from the last clicked cel to this one.</summary>
    public void RangeSelectTo(FrameCell cell)
    {
        if (cell.IsVirtual) return;
        var anchor = _celAnchor.Layer == cell.LayerIndex ? _celAnchor : (cell.LayerIndex, cell.Index);
        _celRange = (cell.LayerIndex, Math.Min(anchor.Index, cell.Index), Math.Max(anchor.Index, cell.Index));
        RefreshCelSelectionHighlights();
    }

    public void ClearCelRange()
    {
        if (_celRange is null) return;
        _celRange = null;
        RefreshCelSelectionHighlights();
    }

    private void RefreshCelSelectionHighlights()
    {
        foreach (var row in LayerRows)
        {
            foreach (var c in row.Cells)
            {
                c.IsSelected = _celRange is { } r
                    && c.LayerIndex == r.Layer && c.Index >= r.Start && c.Index <= r.End;
            }
        }
    }

    /// <summary>The range the operation on this cell should cover: the selection when the cell is inside it, else just the cell.</summary>
    private (int Start, int End) OpRangeFor(FrameCell cell) =>
        _celRange is { } r && r.Layer == cell.LayerIndex && cell.Index >= r.Start && cell.Index <= r.End
            ? (r.Start, r.End)
            : (cell.Index, cell.Index);

    /// <summary>Drop of a dragged cel: move (or Ctrl-copy) the drawing along its row.</summary>
    public void MoveCel(FrameCell from, FrameCell to, bool copy)
    {
        if (from.LayerIndex != to.LayerIndex)
        {
            AiStatus = "Cels move along their own layer row.";
            return;
        }
        if (LayerOfCell(from) is not { } layer) return;
        _editor.MoveCel(layer.Id, from.Index, to.Index, copy);
        ActiveLayerIndex = from.LayerIndex;
        CurrentFrameIndex = Math.Min(to.Index, Scene.FrameCount - 1);
    }

    // ---- frame markers --------------------------------------------------------------

    /// <summary>Ruler tags, refreshed as a new list so the ruler re-renders.</summary>
    [ObservableProperty]
    private IReadOnlyList<FrameMarker> _markersView = [];

    public FrameMarker? MarkerAt(int frame) => Scene.Markers.FirstOrDefault(m => m.Frame == frame);

    /// <summary>
    /// Set or replace the marker at a frame, keeping what the label does not own.
    /// </summary>
    /// <remarks>
    /// This <em>replaces</em> the marker, so its note and its event flag have to be
    /// carried across explicitly. Without that, renaming a marker would silently
    /// throw away the prose attached to it and un-export an engine event — a
    /// deletion disguised as an edit, and the kind that is only noticed later.
    /// </remarks>
    public void SetMarkerAt(int frame, string label, string color)
    {
        var existing = MarkerAt(frame);
        var note = existing?.Note;
        var isEvent = existing?.IsEvent;

        _editor.Perform(doc =>
        {
            doc.Scene.Markers.RemoveAll(m => m.Frame == frame);
            doc.Scene.Markers.Add(new FrameMarker
            {
                Frame = frame,
                Label = label.Trim(),
                Color = color,
                Note = note,
                IsEvent = isEvent,
            });
            doc.Scene.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
    }

    /// <summary>
    /// Attach prose to the marker at a frame, making one if there is none.
    /// </summary>
    /// <remarks>
    /// A note needs somewhere to live, and a frame the artist wants to write about
    /// is a frame worth marking — so writing a note on an unmarked frame creates
    /// the marker rather than refusing. Clearing the text back to nothing removes
    /// the note but keeps the marker, because the marker may be doing its own job.
    /// </remarks>
    public void SetMarkerNoteAt(int frame, string? note)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is null && MarkerAt(frame) is null) return;

        _editor.Perform(doc =>
        {
            if (doc.Scene.Markers.FirstOrDefault(m => m.Frame == frame) is { } marker)
            {
                marker.Note = trimmed;
                return;
            }
            doc.Scene.Markers.Add(new FrameMarker { Frame = frame, Note = trimmed });
            doc.Scene.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        });
    }

    /// <summary>Whether the marker at a frame is exported to an engine.</summary>
    public void SetMarkerIsEventAt(int frame, bool isEvent)
    {
        if (MarkerAt(frame) is null) return;
        // Null rather than false when off, so an ordinary marker writes no key.
        _editor.Perform(doc =>
        {
            if (doc.Scene.Markers.FirstOrDefault(m => m.Frame == frame) is { } marker)
            {
                marker.IsEvent = isEvent ? true : null;
            }
        });
    }

    /// <summary>Markers carrying prose, in frame order. What a notes list shows.</summary>
    public IReadOnlyList<FrameMarker> Notes =>
        Scene.Markers.Where(m => m.HasNote).OrderBy(m => m.Frame).ToList();

    /// <summary>
    /// Jump to the next marker after the playhead. False when there is none.
    /// </summary>
    /// <remarks>
    /// What "timeline bookmarks" actually wanted, and the one thing genuinely
    /// missing: a named point you can *reach*. Markers have existed since M9c and
    /// there has never been a way to walk between them, so on a long sheet they
    /// were labels you had to hunt for by eye.
    /// </remarks>
    public bool GoToNextMarker()
    {
        var next = Scene.Markers.Where(m => m.Frame > CurrentFrameIndex).OrderBy(m => m.Frame).FirstOrDefault();
        if (next is null) return false;
        CurrentFrameIndex = Math.Clamp(next.Frame, 0, Math.Max(0, Scene.FrameCount - 1));
        return true;
    }

    /// <summary>Jump to the marker before the playhead. False when there is none.</summary>
    public bool GoToPreviousMarker()
    {
        var previous = Scene.Markers
            .Where(m => m.Frame < CurrentFrameIndex)
            .OrderByDescending(m => m.Frame)
            .FirstOrDefault();
        if (previous is null) return false;
        CurrentFrameIndex = Math.Clamp(previous.Frame, 0, Math.Max(0, Scene.FrameCount - 1));
        return true;
    }

    [RelayCommand]
    private void NextMarker() => GoToNextMarker();

    [RelayCommand]
    private void PreviousMarker() => GoToPreviousMarker();

    public void RemoveMarkerAt(int frame)
    {
        if (MarkerAt(frame) is null) return;
        _editor.Perform(doc => doc.Scene.Markers.RemoveAll(m => m.Frame == frame));
    }

    /// <summary>
    /// Deterministic inbetweens between the key at/before the playhead and
    /// the next key. Strokes are interpolated, then re-rendered by the same
    /// brush pipeline as hand-painted frames when the cels are displayed.
    /// </summary>
    [RelayCommand]
    private void InsertInbetweens()
    {
        var layer = ActiveLayer;
        var aIndex = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        if (aIndex < 0) return;
        var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
        if (bIndex < 0) return;

        var a = StrokesOf(layer.Cels[aIndex].Frame!);
        var b = StrokesOf(layer.Cels[bIndex].Frame!);
        var series = Inbetweener.InbetweenSeries(a, b, TweenCount, TweenEasing);
        var frames = series.Select(strokes => NewFrameFor(layer, strokes, FrameRole.Inbetween)).ToList();

        _editor.InsertInbetweens(layer.Id, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
    }

    private static List<Stroke> StrokesOf(Frame frame) => frame.Strokes;

    /// <summary>
    /// Resolve a frame's stroke list by id inside a given document instance —
    /// delta undo steps must not capture object references, because a
    /// snapshot-undo in between replaces the whole instance tree.
    /// </summary>
    /// <summary>Remove a stroke by id — reference equality dies when a snapshot-undo swaps in a cloned tree.</summary>
    private static void RemoveStrokeById(Doc doc, string frameId, string strokeId)
    {
        var list = StrokeListIn(doc, frameId);
        var index = list?.FindLastIndex(s => s.Id == strokeId) ?? -1;
        if (index >= 0) list!.RemoveAt(index);
    }

    private static List<Stroke>? StrokeListIn(Doc doc, string frameId)
    {
        foreach (var layer in doc.Scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is { } frame && frame.Id == frameId) return StrokesOf(frame);
            }
        }
        return null;
    }

    /// <summary>A new frame carrying the given strokes.</summary>
    /// <remarks>
    /// Took a <see cref="Layer"/> to pick a frame class from its kind, and both
    /// arms of that switch now construct the same thing. The parameter stays so
    /// every call site keeps reading as "a frame for this layer" — the layer is
    /// what a caller has to hand, and threading it through costs nothing.
    /// </remarks>
    private static Frame NewFrameFor(Layer layer, List<Stroke> strokes, FrameRole role = FrameRole.Key) =>
        new() { Strokes = strokes, Role = role };

    // ---- AI -----------------------------------------------------------------

    /// <summary>
    /// Not readonly: Edit ▸ Configure ▸ AI can swap the provider while the app
    /// is running, and an artist that could only be chosen at startup would
    /// make "test it, then use it" a two-launch operation.
    /// </summary>
    private IAiArtist? _artist;

    private CancellationTokenSource? _aiCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAi))]
    private bool _aiBusy;

    [ObservableProperty]
    private string _aiStatus = "";

    public bool IsAiAvailable => _artist is not null;

    public bool CanUseAi => IsAiAvailable && !AiBusy;

    private bool _aiEnabled = true;

    /// <summary>
    /// Whether AI assistance is switched on at all. The AI bar binds its
    /// visibility here rather than its enabled state: a studio that turns AI
    /// off wants it gone, not greyed, and a permanently disabled row is a
    /// worse answer than an absent one — the camera's rule again.
    /// </summary>
    public bool AiEnabled => _aiEnabled;

    public string AiUnavailableHint => IsAiAvailable
        ? ""
        : "Choose an AI provider in Edit ▸ Configure ▸ AI — Claude, GPT, OpenRouter, a local model "
          + "through Ollama, any OpenAI-compatible endpoint, or an agent of your own over MCP. "
          + "No provider at all? Drive Lightbox from an MCP client instead — see the README.";

    private string _aiProviderLabel = "None";

    /// <summary>
    /// The configured model name, cached beside the provider label for the
    /// same reason, and nullable because not every provider names one.
    /// Recorded in <see cref="AiProvenance"/> on frames the AI drew.
    /// </summary>
    private string? _aiModelLabel;

    /// <summary>
    /// Which provider is in use. Cached rather than read from disk on every
    /// get: it is a bound property, and a binding that touches the filesystem
    /// each time it refreshes is a trap waiting for someone to bind it in a
    /// list.
    /// </summary>
    public string AiProviderLabel => _artist is null ? "None" : _aiProviderLabel;

    /// <summary>
    /// Rebuild the artist from what is stored. Called after the Configure
    /// window changes the connection, so a provider picked at 3pm is the one
    /// that draws at 3.01 without a restart.
    /// </summary>
    public void ReloadAiProvider()
    {
        (_artist as IDisposable)?.Dispose();
        var connection = AiSettings.Load();
        _aiProviderLabel = connection.Provider.Name;
        _aiModelLabel = connection.Value("model");
        _aiEnabled = connection.Enabled;
        _artist = AiArtistFactory.Create(connection);
        OnPropertyChanged(nameof(IsAiAvailable));
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(AiEnabled));
        OnPropertyChanged(nameof(AiUnavailableHint));
        OnPropertyChanged(nameof(AiProviderLabel));
    }

    [RelayCommand]
    private void CancelAi() => _aiCts?.Cancel();

    /// <summary>
    /// The chosen model draws the inbetweens between the key at/before the
    /// playhead and the next key. Same insertion path as the deterministic
    /// engine — only the frame producer differs.
    /// </summary>
    [RelayCommand]
    private async Task AiInbetweenAsync()
    {
        if (_artist is null || AiBusy) return;
        var layer = ActiveLayer;
        // The AI paths are held to the same layer rules as the artist's own
        // hand: a hidden or locked layer refuses both. This guard used to live
        // only on the prompt-drawing command, so removing that would have left
        // the in-app AI able to write where a brush cannot.
        if (!CanEdit(layer, "insert inbetweens on it")) return;
        var aIndex = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        if (aIndex < 0) return;
        var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
        if (bIndex < 0)
        {
            AiStatus = "Needs a second keyframe after the current one.";
            return;
        }

        // Worked out before the request goes, reported after it comes back: the
        // status line in between belongs to progress, and a warning written into
        // it now would be overwritten before anybody read it.
        var unseen = AiEnabled
            ? UnseenByTheModel(layer.Cels[aIndex].Frame!, layer.Cels[bIndex].Frame!)
            : null;

        var ts = Enumerable.Range(1, TweenCount)
            .Select(k => (double)k / (TweenCount + 1))
            .ToList();
        // Send the effective drawings — erased strokes must not leak into
        // the model's input any more than into the deterministic tweens.
        var request = new InbetweenRequest(
            new SceneInfo(Scene.Width, Scene.Height, Scene.Fps),
            StrokeRecordCleaner.EffectiveStrokes(StrokesOf(layer.Cels[aIndex].Frame!)),
            StrokeRecordCleaner.EffectiveStrokes(StrokesOf(layer.Cels[bIndex].Frame!)),
            ts,
            TweenEasing,
            CollectReferenceImages(),
            TaxonomyForActiveDocument());

        var result = await RunAiAsync(
            $"{AiProviderLabel} is drawing {TweenCount} inbetween(s)…",
            ct => _artist.GenerateInbetweensAsync(request, ct));
        if (result is null) return;

        // The model proposes; Lightbox disposes. Every frame is verified
        // against the keys before it can reach the document, and a frame that
        // fails is refused rather than repaired or swapped for the
        // deterministic answer — per Q32, the AI never inserts a frame it
        // cannot defend, and the deterministic engine stays its own command.
        var ordered = result.OrderBy(f => f.T).ToList();
        var candidates = ordered
            .Select(f => new CandidateInbetween(f.T, f.Strokes))
            .ToList();
        var judgement = InbetweenVerifier.Verify(
            request.KeyframeA, request.KeyframeB, candidates, TweenEasing);

        // Refusal is per frame: the ones that passed are inserted, each at its
        // own t's slot — a null keeps the slot a hold, so partial acceptance
        // never shifts a surviving frame onto somebody else's timing.
        var provenance = new AiProvenance(AiProviderLabel, _aiModelLabel);
        var slots = new List<Frame?>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!judgement.Frames[i].Accepted)
            {
                slots.Add(null);
                continue;
            }
            var frame = NewFrameFor(layer, ordered[i].Strokes, FrameRole.Inbetween);
            frame.Ai = provenance;
            slots.Add(frame);
        }

        var refused = judgement.Frames
            .Select((f, i) => (Judgement: f, Slot: i))
            .Where(x => !x.Judgement.Accepted)
            .Select(x => $"frame {x.Slot + 1} of {candidates.Count} was refused: {x.Judgement.Refusal}")
            .ToList();

        var accepted = judgement.AcceptedCount;
        if (accepted == 0)
        {
            // A refusal and a silent no-op are different outcomes: the document
            // is untouched, and the status says which t and why.
            AiStatus = $"Nothing was inserted — {string.Join(" ", refused)}";
            return;
        }

        _editor.InsertInbetweens(layer.Id, aIndex, slots);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        var inserted = unseen is null
            ? $"Inserted {accepted} AI inbetween(s)."
            : $"Inserted {accepted} AI inbetween(s) — drawn lines only, {unseen} not tweened.";
        AiStatus = refused.Count == 0 ? inserted : $"{inserted} {string.Join(" ", refused)}";
    }

    /// <summary>
    /// What the two keys hold that an inbetween cannot carry across, or null when
    /// they hold nothing but strokes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inbetweening — the model's and the deterministic engine's alike — works
    /// from the stroke record. An imported pixel baseline and a symbol placement
    /// are neither of them strokes, so a key holding one produces a tween with
    /// that part of the drawing missing. That is not new behaviour, but it is
    /// newly <em>reachable</em>: before the two frame classes merged, a layer
    /// created as Vector had nowhere to put either, and the two that could hold
    /// them were rarer.
    /// </para>
    /// <para>
    /// <b>This asks the frame what it holds, deliberately, rather than asking the
    /// layer what kind it is.</b> Keying it on <c>Layer.Kind</c> would be the
    /// obvious shortcut and would fire on every document that exists: every
    /// pre-merge layer is <c>LayerKind.Painted</c>, including the hand-drawn ones
    /// that have no baseline at all. A warning that appears on every old file
    /// teaches an artist to ignore warnings.
    /// </para>
    /// <para>
    /// Only worth saying when there is an AI to say it about, which is why the
    /// caller gates it on <c>AiEnabled</c> — a studio that switched AI off does
    /// not need to hear what the model would have missed. Q52.
    /// </para>
    /// </remarks>
    private static string? UnseenByTheModel(Frame a, Frame b) =>
        (a.HasBaseline || b.HasBaseline, a.HasPlacements || b.HasPlacements) switch
        {
            (true, true) => "imported pixels and placed symbols",
            (true, false) => "imported pixels",
            (false, true) => "placed symbols",
            _ => null,
        };

    /// <summary>
    /// What the project knows about the subject this document belongs to, or
    /// null when there is no project, no subject above it, or nothing read yet.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Walks up the folder tree rather than searching a list of
    /// characters, so a drawing two folders below Knight is still Knight's — the
    /// old model could not express that at all.
    /// <para>
    /// Null is the ordinary answer and costs nothing: a request with no
    /// taxonomy is byte-for-byte the request Lightbox sent before this feature
    /// existed. Optional means absent here too.
    /// </para>
    /// </remarks>
    private SubjectTaxonomy? TaxonomyForActiveDocument() =>
        ProjectDocker.Project is { } project && SaveTargetTab?.Source is { } source
            ? project.ReadingFor(source)?.Taxonomy
            : null;

    /// <summary>
    /// Read what the selected character is, from the sheets drawn of it, and
    /// keep the answer on the character.
    /// </summary>
    /// <remarks>
    /// Once per character rather than once per frame — the whole economic
    /// argument for storing it. A 24-frame cycle pays for this once, and the
    /// next animation of the same character pays nothing.
    ///
    /// It refuses to overwrite a reading somebody has edited. A guess is a
    /// default, never an override of something a person stated, and a re-read
    /// that silently discarded an artist's corrections would teach them not to
    /// make any.
    /// </remarks>
    [RelayCommand]
    private async Task AiReadSubjectAsync()
    {
        if (_artist is null || AiBusy) return;
        if (ProjectDocker.Project is null)
        {
            AiStatus = "Reading a subject needs a project — that is where a character lives.";
            return;
        }
        // B114. A folder, not a `Character` — and the folder need not already be
        // one, because reading it is what makes it one. Selecting an ordinary
        // folder full of a character's drawings and asking to read it is the
        // whole gesture; the old model needed the character to exist first.
        if (ProjectDocker.TargetFolder is not { } character)
        {
            AiStatus = "Select a folder in the Project panel first — that is what gets read.";
            return;
        }
        if (character.Taxonomy is { Reviewed: true })
        {
            AiStatus = $"“{character.Name}” has a reading you edited. Clear it first to read again.";
            return;
        }
        if (CollectReferenceImages() is not { Count: > 0 } sheets)
        {
            AiStatus = "No character sheet to read — draw one, or make a layer on it visible.";
            return;
        }

        var taxonomy = await RunAiAsync(
            $"{AiProviderLabel} is reading “{character.Name}”…",
            ct => _artist.ReadSubjectAsync(new SubjectRequest(character.Name, sheets), ct));
        if (taxonomy is null) return;

        character.Taxonomy = taxonomy;
        ProjectDocker.MarkManifestChanged();
        AiStatus = $"Read “{character.Name}”: {taxonomy.Kind}, "
                 + $"{Plural(taxonomy.Parts.Count, "part")}. Edit it and it will not be overwritten.";
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Shared busy/cancel/error plumbing for AI calls; null on failure.</summary>
    private async Task<T?> RunAiAsync<T>(string busyMessage, Func<CancellationToken, Task<AiResult<T>>> call)
        where T : class
    {
        _aiCts = new CancellationTokenSource();
        AiBusy = true;
        AiStatus = busyMessage;
        try
        {
            var result = await call(_aiCts.Token);
            if (result.Outcome == AiOutcome.Success) return result.Value;
            AiStatus = result.Message ?? "AI request failed.";
            return null;
        }
        finally
        {
            AiBusy = false;
            _aiCts.Dispose();
            _aiCts = null;
        }
    }

    // ---- document I/O -------------------------------------------------------

    // ---- external producers (IPC/MCP) ---------------------------------------

    /// <summary>The layer external tools target when they don't name one.</summary>
    public Layer ActiveLayerForIpc => ActiveLayer;

    /// <summary>Composite one timeline frame to PNG (no onion skin, no live stroke).</summary>
    public string RenderFramePng(int frameIndex)
    {
        var scene = Scene;
        var passes = new List<RenderPass>();
        foreach (var layer in scene.Layers)
        {
            if (!scene.IsLayerVisible(layer)) continue;
            var frame = ExposureSheet.ExposedFrame(layer, frameIndex);
            if (frame is null) continue;
            passes.Add(new RenderPass(_cache.Get(frame, scene.Width, scene.Height, celIndex: frameIndex), null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode)));
        }
        using var image = SceneRenderer.Compose(scene.Width, scene.Height, passes, SceneRenderer.BackgroundOf(scene));
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encode failed.");
        return Convert.ToBase64String(data.AsSpan());
    }

    /// <summary>
    /// Insert externally produced inbetween frames (already validated, sorted
    /// by t) after key <paramref name="aIndex"/>. One undo step. Returns the
    /// number of frames inserted.
    /// </summary>
    public int InsertExternalInbetweens(string layerId, int aIndex, List<List<Stroke>> strokeFrames)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        if (!CanEdit(layer, "insert inbetweens on it")) return 0;
        var frames = strokeFrames.Select(s =>
        {
            var frame = NewFrameFor(layer, s, FrameRole.Inbetween);
            // Q31: provenance on every frame AI touched — an agent working the
            // document over MCP is exactly that, whatever model drives it.
            frame.Ai = new AiProvenance("MCP agent");
            return frame;
        }).ToList();
        _editor.InsertInbetweens(layerId, aIndex, frames);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        return frames.Count;
    }

    /// <summary>
    /// Append externally produced strokes to the key exposed at
    /// <paramref name="frameIndex"/>. One undo step. Returns strokes added
    /// (0 when the layer has no key there).
    /// </summary>
    public int AppendExternalStrokes(string layerId, int frameIndex, List<Stroke> strokes)
    {
        var layer = Scene.Layers.First(l => l.Id == layerId);
        if (!CanEdit(layer, "draw on it")) return 0;
        var keyIndex = ExposureSheet.KeyIndexAtOrBefore(layer, frameIndex);
        if (keyIndex < 0) return 0;
        var frame = layer.Cels[keyIndex].Frame!;
        _editor.Perform(_ =>
        {
            StrokesOf(frame).AddRange(strokes);
            // Q31: the frame was AI-touched, even when the artist drew the
            // rest of it. Absent stays absent on frames no agent reaches.
            frame.Ai ??= new AiProvenance("MCP agent");
        });
        InvalidateFrameRender(frame.Id);
        _dirtyThumbIds.Add(frame.Id);
        PublishSnapshot();
        RefreshThumbnails();
        return strokes.Count;
    }

    /// <summary>Replace the ACTIVE tab's document (fresh editor, clean state).</summary>
    /// <remarks>
    /// With nothing open there is nothing to replace, and refusing would hand
    /// back a document the caller asked to see and cannot. So the empty case
    /// opens it instead — same intent, one tab either way.
    /// </remarks>
    public void ReplaceDocument(Doc doc)
    {
        if (Tabs.Count == 0)
        {
            var opened = new DocumentTab(new DocumentEditor(doc), doc.Scene.Name);
            opened.State.LayerIndex = FirstPaintableLayer(doc);
            AddTab(opened);
            opened.MarkSaved();
            return;
        }

        _switchingTabs = true;
        var tab = ActiveTab ?? Tabs[0];
        tab.Editor = new DocumentEditor(doc);
        AttachEditor(tab.Editor);
        // B136's other door: index 0 is the locked paper on any document that
        // has one, and a replace is how tests and the MCP surface open files.
        ActiveLayerIndex = FirstPaintableLayer(doc);
        CurrentFrameIndex = 0;
        // A fresh editor sits at revision 0 and this document came from disk,
        // so that is its saved point.
        tab.MarkSaved();
        _switchingTabs = false;
    }

    /// <summary>Serialize the save target (a reference tab serializes its owning document).</summary>
    public string SerializeDocument() => DocJson.Serialize(SaveTargetTab?.Doc ?? Doc);

    // ---- internals ----------------------------------------------------------

    /// <summary>
    /// Advance by what the clock says, not by one.
    /// </summary>
    /// <remarks>
    /// The count is the whole of <see cref="PlaybackClock"/>'s contract: a tick
    /// that arrived a frame and a half late owes two frames, and honouring only
    /// one is the accumulating drift the paced clock exists to remove. Written
    /// as a loop rather than an index jump so a wrap, a range end and the audio
    /// resync all happen exactly as they do at speed.
    /// </remarks>
    /// <summary>
    /// Advance the playhead by the frames this tick owes, compositing only the
    /// one that will actually be seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B162. This loop rendered every frame it had just decided to skip, so
    /// dropping frames to keep up cost four times as much as keeping them.</b>
    /// <c>PlaybackClock.Pace</c> returns a frame count — 1 normally, up to
    /// <c>MaxCatchUpFrames</c> when the tick is late — and "drop 3 to catch up"
    /// was implemented as three full <c>StepPlayback</c> calls, each of which
    /// writes <c>CurrentFrameIndex</c> and therefore runs a whole publish.
    /// </para>
    /// <para>
    /// <b>It is a feedback loop, not a constant overhead, which is why it ran
    /// away.</b> A tick that is late asks for more frames; more frames cost
    /// proportionally more; the extra cost makes the next tick later still. The
    /// capture that found it: <b>178 ticks advanced the playhead and 568
    /// composites came out of them</b> — 3.2 per tick — with mean lateness
    /// climbing to 232 ms and <b>104 occasions where the clock gave up and
    /// re-based</b>, on a five-frame scene.
    /// </para>
    /// <para>
    /// <b>It also made the report lie in the reader's favour</b>, which is why
    /// four rounds of captures looked survivable. The tick breakdown divides by
    /// profiled ticks — one per <c>StepPlayback</c> — so it reported 38 ms
    /// against an 83 ms budget and called it comfortable, when a single timer
    /// fire was doing 3.2 of them: <b>138 ms of an 83.3 ms period, 165%.</b> The
    /// per-tick number was true and the per-<em>frame-period</em> number, which
    /// is the one that decides whether a clock can keep time, was never shown.
    /// </para>
    /// <para>
    /// A skipped frame still moves the playhead, still wraps the loop, still
    /// stops at the end of a range and still tracks audio — everything except
    /// the composite, which is the one part nobody was ever going to see.
    /// </para>
    /// </remarks>
    // The clock's own handler, reachable by the tests: the catch-up path is
    // driven by a real DispatcherTimer being late, which the headless harness
    // cannot produce — the same reason PlaybackClock's docs give for testing
    // Pace as pure arithmetic rather than through a timer.
    internal void HandlePlaybackTickForTests(int frames) => OnPlaybackTick(frames);

    private void OnPlaybackTick(int frames)
    {
        for (var i = 0; i < frames - 1 && IsPlaying; i++)
        {
            _skippingFrame = true;
            try { StepPlayback(); }
            finally { _skippingFrame = false; }
        }
        if (IsPlaying) StepPlayback();
    }

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

    private void OnDocumentChanged()
    {
        // B151: the swatch's frame list describes a tree that just moved. An undo
        // or a structural edit can add, remove or replace the frames it names, and
        // a stale list would repaint the wrong ones — or, worse, none of them.
        _swatchRepaint = null;

        if (_committingScopedEdit)
        {
            MarkDocumentEdited();
            RefreshDocumentStats(); // memory grows as frames get cached
            return;
        }
        MarkDocumentEdited();
        InvalidateWholeCanvas(); // a document-wide change can move any pixel
        _composeRing.InvalidateAll();
        BrushTipRegistry.Register(Doc.BrushTips);
        ClipRegionRegistry.Register(Doc.ClipRegions);
        RegisterResources();
        PaletteDocker.Load(Doc);
        GradientDocker.Load(Doc);
        RefreshDocumentStats();
        OnPropertyChanged(nameof(ReferenceSheetsView));
        SyncLayerChoices();
        ClampCurrentFrame(publishIfUnchanged: !_applyingEditScope);
        SyncLayerRows();
        OnPropertyChanged(nameof(FrameLabel));
        OnPropertyChanged(nameof(TimelineExtent));
        OnPropertyChanged(nameof(MaxScrubFrame));
        OnPropertyChanged(nameof(Fps));
        NotifyAudioSurface();
        NotifyActiveLayerCompositing();
        MarkersView = Scene.Markers.ToList();
        RefreshCelSelectionHighlights();
        // Undo/redo publishes from ApplyEditScope instead, once the stale
        // frame bitmaps have been dropped.
        if (_applyingEditScope) return;
        PublishSnapshot();
        RefreshThumbnails();
    }

    private void SyncLayerChoices()
    {
        if (LayerChoices.SequenceEqual(Scene.Layers)) return;
        LayerChoices.Clear();
        foreach (var layer in Scene.Layers) LayerChoices.Add(layer);
        if (ActiveLayerIndex >= LayerChoices.Count) ActiveLayerIndex = LayerChoices.Count - 1;
    }

    /// <summary>
    /// Keep the playhead inside the timeline. Moving it repaints through the
    /// property setter; <paramref name="publishIfUnchanged"/> covers callers
    /// that rely on this to repaint even when nothing moved.
    /// </summary>
    private void ClampCurrentFrame(bool publishIfUnchanged = true)
    {
        var max = Math.Max(0, Scene.FrameCount - 1);
        if (CurrentFrameIndex > max) CurrentFrameIndex = max;
        else if (publishIfUnchanged) PublishSnapshot();
    }

    /// <summary>
    /// Mirror Scene.Layers into LayerRows (topmost layer first) and keep each
    /// row's cell strip in step with the timeline.
    /// </summary>
    private void SyncLayerRows()
    {
        var layers = Scene.Layers;
        while (LayerRows.Count > layers.Count) LayerRows.RemoveAt(LayerRows.Count - 1);
        while (LayerRows.Count < layers.Count) LayerRows.Add(new LayerRow(this));

        // Math.Max, because a document with no layers is loadable and `Clamp(_, 0, -1)` throws.
        // `new Doc()` has an empty layer list and nothing in `DocJson` backfills one, so a
        // `.lightbox.json` with `"layers": []` — hand-edited, machine-written, or from a version
        // that allowed it — took down File → Open with an ArgumentException about 0 and -1. B56.
        var active = Math.Clamp(ActiveLayerIndex, 0, Math.Max(0, layers.Count - 1));
        for (var i = 0; i < LayerRows.Count; i++)
        {
            var row = LayerRows[i];
            var sceneIndex = layers.Count - 1 - i;
            var layer = layers[sceneIndex];
            row.SyncFromModel(layer, sceneIndex);
            row.IsActive = sceneIndex == active;

            while (row.Cells.Count > TimelineExtent) row.Cells.RemoveAt(row.Cells.Count - 1);
            while (row.Cells.Count < TimelineExtent) row.Cells.Add(new FrameCell(row.Cells.Count));
            foreach (var cell in row.Cells)
            {
                var frame = ExposureSheet.FrameAtExactIndex(layer, cell.Index);
                cell.LayerIndex = sceneIndex;
                cell.IsKeyed = frame is not null;
                cell.Role = frame?.Role ?? FrameRole.Key;
                cell.IsVirtual = cell.Index >= Scene.FrameCount;
                cell.IsCurrent = cell.Index == CurrentFrameIndex;
            }
        }
        RebuildLayerPanel();
        OnPropertyChanged(nameof(FrameCells));
        OnPropertyChanged(nameof(TimelineTracks));
        OnPropertyChanged(nameof(TimelineFrameCount));
        OnPropertyChanged(nameof(GraphSeriesList));
        RefreshRangeHighlights();
    }

    // ---- thumbnails ----------------------------------------------------------

    private readonly HashSet<string> _dirtyThumbIds = [];
    private bool _allThumbsDirty;

    /// <summary>
    /// The bitmap a thumbnail shrinks from. Bounded documents hand back the
    /// full-size cached render — the thumbnail rides a bitmap the canvas needs
    /// anyway. An unbounded document has no such bitmap: materialising one
    /// would allocate the document-sized surface the whole tile path exists to
    /// avoid, and only to be shrunk to 32×18. So it renders small instead —
    /// output scale is the surface transform (invariant 7), the stroke record
    /// is untouched, and the cache keys on size+scale so the entry stays tiny.
    /// </summary>
    private SKBitmap ThumbSource(Frame frame, int celIndex)
    {
        if (!UnboundedCanvasOn)
        {
            return _cache.Get(frame, Scene.Width, Scene.Height, celIndex: celIndex);
        }
        var scale = Math.Min(1.0, Math.Min(
            ThumbnailRenderer.Width * 4.0 / Scene.Width,
            ThumbnailRenderer.Height * 4.0 / Scene.Height));
        return _cache.Get(frame, Scene.Width, Scene.Height, outputScale: scale, celIndex: celIndex);
    }

    /// <summary>
    /// Update timeline thumbnails lazily: only cells whose keyed frame is new,
    /// changed, or explicitly marked dirty are re-rendered.
    /// </summary>
    private void RefreshThumbnails()
    {
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells)
            {
                var frame = ExposureSheet.FrameAtExactIndex(row.Layer, cell.Index);
                if (frame is null)
                {
                    cell.Thumb = null;
                    cell.ThumbFrameId = null;
                    continue;
                }
                var stale = _allThumbsDirty
                            || cell.ThumbFrameId != frame.Id
                            || _dirtyThumbIds.Contains(frame.Id);
                if (!stale && cell.Thumb is not null) continue;

                var bmp = ThumbSource(frame, cell.Index);
                cell.Thumb = ThumbnailRenderer.Render(bmp);
                cell.ThumbFrameId = frame.Id;
            }
        }
        RefreshLayerThumbs();
        _dirtyThumbIds.Clear();
        _allThumbsDirty = false;
    }

    /// <summary>
    /// Layer-docker thumbnails show the exposed drawing at the playhead
    /// (holds resolve to the drawing they hold) over a checkerboard.
    /// Also called on playhead moves, where only rows whose exposed frame
    /// actually changed re-render.
    /// </summary>
    /// <summary>
    /// How many layer thumbnails have actually been rasterized, for the budget
    /// test (B152).
    /// </summary>
    /// <remarks>
    /// A count rather than a stopwatch, for B151's reason: the property is not
    /// "this got faster" but "this does not happen per tick", which is exact and
    /// cannot go flaky. Each one is a full-resolution frame render behind it, so
    /// the count is a fair proxy for the cost as well as for the rule.
    /// </remarks>
    internal int LayerThumbRenders { get; private set; }

    private void RefreshLayerThumbs()
    {
        foreach (var row in LayerRows)
        {
            var frame = ExposureSheet.ExposedFrame(row.Layer, CurrentFrameIndex);
            if (frame is null)
            {
                row.Thumb = null;
                row.ThumbFrameId = null;
                continue;
            }
            var stale = _allThumbsDirty
                        || row.ThumbFrameId != frame.Id
                        || _dirtyThumbIds.Contains(frame.Id);
            if (!stale && row.Thumb is not null) continue;

            var bmp = ThumbSource(frame, CurrentFrameIndex);
            row.Thumb = ThumbnailRenderer.RenderChecker(bmp, 44, 26);
            row.ThumbFrameId = frame.Id;
            LayerThumbRenders++;
        }
    }

    private void RefreshCellHighlights()
    {
        foreach (var row in LayerRows)
        {
            foreach (var cell in row.Cells) cell.IsCurrent = cell.Index == CurrentFrameIndex;
        }
    }

    // ---- onion skin -------------------------------------------------------------

    /// <summary>Onion skin as the artist has set it up. Global, not per document.</summary>
    public Services.OnionSettings Onion => Settings.Onion;

    // ---- imported references ------------------------------------------------------

    // ---- guides -----------------------------------------------------------------

    /// <summary>The guides on this document, or an empty list.</summary>
    public IReadOnlyList<Guide> Guides => Scene.Guides ?? [];

    public bool HasGuides => Scene.HasGuides;

    /// <summary>
    /// Whether guides constrain what you draw right now.
    /// </summary>
    /// <remarks>
    /// A working state rather than a document property, the same side of the
    /// line as onion skin: the guides are authored and saved, but whether you
    /// are currently drawing against them is how you are working this minute.
    /// It survives the session through settings, not through the file.
    /// </remarks>
    [ObservableProperty]
    private bool _snapToGuides = true;

    /// <summary>How close a point has to be, in document pixels, to be pulled.</summary>
    [ObservableProperty]
    private double _snapTolerance = 12;

    partial void OnSnapToleranceChanged(double value)
    {
        if (Math.Abs(Settings.SnapTolerance - value) < 1e-9) return;
        Settings.SnapTolerance = value;
        Settings.Save();
    }

    /// <summary>
    /// The pitch a new grid is made with, in document pixels.
    /// </summary>
    /// <remarks>
    /// A preference, not document data: once a grid exists, its spacing lives
    /// on the guide, so changing this never moves a lattice somebody has
    /// already drawn against.
    /// </remarks>
    public double GridSpacing
    {
        get => Settings.GridSpacing;
        set
        {
            var clamped = Math.Clamp(value, 1, 4096);
            if (Math.Abs(Settings.GridSpacing - clamped) < 1e-9) return;
            Settings.GridSpacing = clamped;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The grid guides on this document, if any.</summary>
    public IReadOnlyList<Guide> GridGuides =>
        Guides.Where(g => g.Kind == GuideKind.Grid).ToList();

    /// <summary>
    /// Change a placed grid's pitch, as one undoable step.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GridSpacing"/> on purpose. That is what the
    /// next grid will be; this reaches into one that exists, and only an
    /// explicit edit should ever do that.
    /// </remarks>
    public void SetGridSpacing(Guide guide, double spacing)
    {
        var clamped = Math.Clamp(spacing, 1, 4096);
        var before = guide.Spacing;
        if (Math.Abs(before - clamped) < 1e-9) return;
        _editor.PerformDelta(_ => guide.Spacing = clamped, _ => guide.Spacing = before);
        NotifyGuides();
    }

    /// <summary>Change a placed grid's angle, as one undoable step.</summary>
    public void SetGridAngle(Guide guide, double angle)
    {
        var before = guide.Angle;
        if (Math.Abs(before - angle) < 1e-9) return;
        _editor.PerformDelta(_ => guide.Angle = angle, _ => guide.Angle = before);
        NotifyGuides();
    }

    /// <summary>Turn a placed guide's drawing or snapping on or off, undoably.</summary>
    public void SetGuideFlags(Guide guide, bool visible, bool snaps)
    {
        var before = (guide.Visible, guide.Snaps);
        if (before == (visible, snaps)) return;
        _editor.PerformDelta(
            _ => { guide.Visible = visible; guide.Snaps = snaps; },
            _ => { guide.Visible = before.Visible; guide.Snaps = before.Snaps; });
        NotifyGuides();
    }

    /// <summary>The guide the stroke in progress has locked to, if any.</summary>
    private Guide? _lockedGuide;

    private (double X, double Y) _strokeAnchor;

    private bool _lockDecided;

    /// <summary>
    /// Put a raw point where the guides say it belongs.
    /// </summary>
    /// <remarks>
    /// After stabilisation, not before. Snapping first and smoothing after
    /// would drag the point back off the guide, which is the wrong way round —
    /// the wobble is what you want removed, the guide is what you want obeyed.
    /// </remarks>
    private (double X, double Y) Guided(double x, double y)
    {
        if (!SnapToGuides || Scene.Guides is not { Count: > 0 } guides) return (x, y);

        // Locked already: hold the line, and stop reconsidering. A wobbly hand
        // that re-chooses mid-stroke makes the line kink.
        if (_lockedGuide is { } locked)
        {
            return Snapper.Along(locked, _strokeAnchor.X, _strokeAnchor.Y, x, y);
        }
        if (!_lockDecided)
        {
            if (Snapper.Lock(guides, _strokeAnchor.X, _strokeAnchor.Y, x, y) is { } found)
            {
                _lockedGuide = found;
                _lockDecided = true;
                return Snapper.Along(found, _strokeAnchor.X, _strokeAnchor.Y, x, y);
            }
            // Far enough to have meant something, and it matched nothing:
            // this is a freehand stroke and asking again every event would
            // only let a late wobble grab it.
            var dx = x - _strokeAnchor.X;
            var dy = y - _strokeAnchor.Y;
            if (Math.Sqrt(dx * dx + dy * dy) >= Snapper.LockDistance) _lockDecided = true;
        }
        return Snapper.Point(guides, x, y, SnapTolerance);
    }

    /// <summary>Add a guide. The first one brings the machinery into being.</summary>
    public Guide AddGuide(GuideKind kind, double x, double y, double angle = 0, double spacing = 32)
    {
        var guide = new Guide { Kind = kind, X = x, Y = y, Angle = angle, Spacing = spacing };
        _editor.Perform(doc => (doc.Scene.Guides ??= []).Add(guide));
        NotifyGuides();
        return guide;
    }

    public void RemoveGuide(Guide guide)
    {
        var id = guide.Id;
        _editor.Perform(doc =>
        {
            doc.Scene.Guides?.RemoveAll(g => g.Id == id);
            // Absent, not empty: a document whose last guide goes writes no
            // guide key again.
            if (doc.Scene.Guides is { Count: 0 }) doc.Scene.Guides = null;
        });
        NotifyGuides();
    }

    /// <summary>Move a guide's anchor, in document pixels.</summary>
    public void MoveGuide(Guide guide, double dx, double dy)
    {
        if (guide.Locked) return;
        _editor.PerformDelta(
            _ => { guide.X += dx; guide.Y += dy; },
            _ => { guide.X -= dx; guide.Y -= dy; });
        NotifyGuides();
    }

    private (double X, double Y) _guideDragTotal;

    /// <summary>
    /// Move a guide while the pointer is still down.
    /// </summary>
    /// <remarks>
    /// Nothing is recorded until the drag ends. A pointer move arrives every
    /// few milliseconds, so recording each one would bury the last real edit
    /// under fifty identical nudges and make undoing a drag a job rather than
    /// a keystroke.
    /// </remarks>
    public void DragGuide(Guide guide, double dx, double dy)
    {
        if (guide.Locked) return;
        guide.X += dx;
        guide.Y += dy;
        _guideDragTotal = (_guideDragTotal.X + dx, _guideDragTotal.Y + dy);
        NotifyGuides();
    }

    /// <summary>Close a guide drag: the whole of it becomes one undo step.</summary>
    public void EndGuideDrag(Guide guide)
    {
        var (dx, dy) = _guideDragTotal;
        _guideDragTotal = default;
        if (dx == 0 && dy == 0) return;
        // Back to where the drag started, then forward again through the
        // recorded path — so undo returns it to the place it was picked up
        // from rather than to the last pointer event.
        guide.X -= dx;
        guide.Y -= dy;
        MoveGuide(guide, dx, dy);
    }

    [RelayCommand]
    private void ClearGuides()
    {
        if (!HasGuides) return;
        _editor.Perform(doc => doc.Scene.Guides = null);
        NotifyGuides();
    }

    private void NotifyGuides()
    {
        OnPropertyChanged(nameof(Guides));
        OnPropertyChanged(nameof(HasGuides));
        OnPropertyChanged(nameof(GridGuides));
        GuidesChanged?.Invoke();
        PublishSnapshot();
        MarkDocumentEdited();
    }

    /// <summary>The guides changed; the canvas redraws its chrome from this.</summary>
    public event Action? GuidesChanged;

    /// <summary>The references on this document, or an empty list.</summary>
    public IReadOnlyList<ReferenceStrip> References =>
        Scene.References is { } strips ? strips : [];

    public bool HasReferences => Scene.HasReferences;

    /// <summary>
    /// The reference being edited. Index rather than the object, so the
    /// selection survives an undo — which replaces the whole document.
    /// </summary>
    [ObservableProperty]
    private int _activeReferenceIndex;

    public ReferenceStrip? ActiveReference =>
        Scene.References is { } strips && ActiveReferenceIndex >= 0 && ActiveReferenceIndex < strips.Count
            ? strips[ActiveReferenceIndex]
            : null;

    partial void OnActiveReferenceIndexChanged(int value) => NotifyReference();

    /// <summary>The cell of the active reference showing at the playhead, or null.</summary>
    public ReferenceCell? ActiveReferenceCell => ActiveReference?.CellAt(CurrentFrameIndex);

    public bool HasReferenceCell => ActiveReferenceCell is not null;

    /// <summary>
    /// Import a sheet, slice it, and lay it against the timeline from the
    /// playhead.
    /// </summary>
    /// <param name="addFrames">
    /// Extend the timeline to fit the reference. On by default because it is
    /// what importing a run cycle means: you are here to draw those frames,
    /// and being handed a twelve-frame reference on a one-frame document with
    /// eleven of it invisible is not a state anybody asked for.
    /// </param>
    public ReferenceStrip? ImportReference(
        string name, string pngBase64, SliceOptions options = default, bool addFrames = true)
    {
        SKBitmap sheet;
        try
        {
            sheet = Lightbox.Raster.PngCodec.Decode(pngBase64);
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException)
        {
            return null;
        }

        var strip = new ReferenceStrip
        {
            Name = name,
            Png = pngBase64,
            SheetWidth = sheet.Width,
            SheetHeight = sheet.Height,
            Cells = SliceSheet(sheet, options),
        };
        sheet.Dispose();
        if (strip.Cells.Count == 0) return null;

        strip.LayOutFrom(CurrentFrameIndex);
        strip.Scale = FitScale(strip, Scene);
        strip.CentreOn(Scene.Width, Scene.Height);

        var index = 0;
        _editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
            if (addFrames && strip.Slots.Count > doc.Scene.FrameCount)
            {
                doc.Scene.FrameCount = strip.Slots.Count;
            }
        });

        Lightbox.Raster.ReferenceStripRegistry.Register([(strip.Id, strip.Png)]);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return strip;
    }

    /// <summary>
    /// Footage to draw against (Q56): the clip's frames extracted at the
    /// scene's fps and laid against the timeline like any reference. The
    /// document keeps the path — relative when the file lives near it — and
    /// the pixels are rebuilt from the footage on load. Returns null on
    /// success or a sentence saying why not.
    /// </summary>
    public async Task<string?> ImportVideoReference(
        string path, Services.ClipStorage storage = Services.ClipStorage.ReferenceByPath)
    {
        if (Services.VideoExporter.FindFfmpeg() is not { } ffmpeg)
        {
            return "FFmpeg was not found — reinstall Lightbox, or install FFmpeg and put it on PATH.";
        }
        // The extraction is an FFmpeg run — seconds, off the UI thread. The
        // document edit below stays on it, like every other edit.
        var fps = Math.Max(1, Scene.Fps);
        var (extracted, error) = await Task.Run(() =>
        {
            var r = Services.VideoReferenceImporter.Extract(ffmpeg, path, fps, out var e);
            return (r, e);
        });
        if (extracted is not { } result) return error ?? "The clip could not be read.";

        var stored = path;
        if (System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is { Length: > 0 } docDir)
        {
            var relative = System.IO.Path.GetRelativePath(docDir, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal)
                && !System.IO.Path.IsPathRooted(relative))
            {
                stored = relative;
            }
        }

        var strip = new ReferenceStrip
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            SheetWidth = result.Sheet.Width,
            SheetHeight = result.Sheet.Height,
            Cells = result.Cells,
        };
        switch (storage)
        {
            case Services.ClipStorage.ReferenceByPath:
                strip.VideoPath = stored;
                break;
            case Services.ClipStorage.ReferenceEmbedded:
                // The contact sheet itself, stored the way image references
                // store — self-contained, no path, no FFmpeg on reopen.
                strip.Png = Lightbox.Raster.PngCodec.Encode(result.Sheet);
                break;
            case Services.ClipStorage.Production:
                strip.VideoData = Convert.ToBase64String(File.ReadAllBytes(path));
                strip.RendersInExport = true;
                // Material, not a ghost: production footage shows and
                // exports at full strength unless the artist dials it back.
                strip.Opacity = 1.0;
                break;
        }
        strip.LayOutFrom(CurrentFrameIndex);
        strip.Scale = FitScale(strip, Scene);
        strip.CentreOn(Scene.Width, Scene.Height);

        var index = 0;
        _editor.Perform(doc =>
        {
            doc.Scene.References ??= [];
            index = doc.Scene.References.Count;
            doc.Scene.References.Add(strip);
            if (strip.Slots.Count > doc.Scene.FrameCount)
            {
                doc.Scene.FrameCount = strip.Slots.Count;
            }
        });

        Lightbox.Raster.ReferenceStripRegistry.Register(strip.Id, result.Sheet);
        ActiveReferenceIndex = index;
        AfterReferenceChange();
        return null;
    }

    /// <summary>
    /// Rebuild a loaded video reference's pixels from its footage. Quiet when
    /// FFmpeg or the file is gone — drawing against nothing is the reference
    /// system's standing answer to a source it cannot read.
    /// </summary>
    private void RegisterVideoReference(ReferenceStrip strip)
    {
        if (Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id) is not null) return;
        if (Services.VideoExporter.FindFfmpeg() is not { } ffmpeg) return;

        string resolved;
        if (strip.VideoData is { } data)
        {
            // Production footage (Q57): the clip travels in the document, so
            // the extraction reads a temp copy of its own bytes.
            resolved = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"lightbox-footage-{strip.Id}.bin");
            try
            {
                if (!File.Exists(resolved))
                {
                    File.WriteAllBytes(resolved, Convert.FromBase64String(data));
                }
            }
            catch (Exception ex) when (ex is FormatException or IOException)
            {
                return;
            }
        }
        else
        {
            resolved = strip.VideoPath!;
            if (!System.IO.Path.IsPathRooted(resolved))
            {
                if (System.IO.Path.GetDirectoryName(SaveTargetTab?.FilePath) is not { Length: > 0 } docDir) return;
                resolved = System.IO.Path.Combine(docDir, resolved);
            }
        }
        if (!File.Exists(resolved)) return;

        var extracted = Services.VideoReferenceImporter.Extract(ffmpeg, resolved, Math.Max(1, Scene.Fps), out _);
        if (extracted is { } result)
        {
            Lightbox.Raster.ReferenceStripRegistry.Register(strip.Id, result.Sheet);
        }
    }

    private static List<ReferenceCell> SliceSheet(SKBitmap sheet, SliceOptions options)
    {
        // Grid mode never reads a pixel, so a sheet the artist has described
        // does not pay for the scan.
        if (options.Columns > 0 && options.Rows > 0)
        {
            return StripSlicer.Grid(sheet.Width, sheet.Height, options.Columns, options.Rows);
        }

        using var rgba = sheet.ColorType == SKColorType.Rgba8888
            ? null
            : sheet.Copy(SKColorType.Rgba8888);
        var source = rgba ?? sheet;
        using var pixmap = source.PeekPixels();
        var occupied = pixmap is null
            ? new bool[source.Width * source.Height]
            : StripSlicer.Occupancy(pixmap.GetPixelSpan(), source.Width, source.Height, options);
        // Detect finds the drawings and discards the furniture — a title
        // banner, a watermark, a signature. Slice projects occupancy onto the
        // axes, which is exact for a clean atlas and hopeless for a page: the
        // banner is content in every column, so the projection never returns
        // to zero and the whole sheet reads as one cell. Fall back to Slice
        // only when nothing looked like a drawing.
        var found = StripSlicer.Detect(occupied, source.Width, source.Height, options);
        return found.Count > 0
            ? found
            : StripSlicer.Slice(occupied, source.Width, source.Height, options);
    }

    /// <summary>
    /// Shrink an oversized sheet to fit the canvas. A reference bigger than
    /// the document is the common case — a 2000px sprite sheet against a 960px
    /// scene — and landing at 1:1 puts the character off screen with no
    /// obvious way back.
    /// </summary>
    private static double FitScale(ReferenceStrip strip, Scene scene)
    {
        var cell = strip.Cells[0];
        if (cell.Width <= 0 || cell.Height <= 0) return 1;
        var fit = Math.Min(scene.Width / (double)cell.Width, scene.Height / (double)cell.Height);
        return fit >= 1 ? 1 : Math.Round(fit, 3);
    }

    /// <summary>
    /// Columns and rows for the grid override. Zero on both means "work it
    /// out from the pixels", which is what <c>Detect</c> restores.
    /// </summary>
    [ObservableProperty]
    private int _referenceColumns;

    [ObservableProperty]
    private int _referenceRows;

    /// <summary>Dragging on the canvas lines the reference up instead of drawing.</summary>
    [ObservableProperty]
    private bool _referenceAlignMode;

    [RelayCommand]
    private void ApplyReferenceGrid() =>
        ResliceReference(new SliceOptions(Math.Max(0, ReferenceColumns), Math.Max(0, ReferenceRows)));

    [RelayCommand]
    private void DetectReferenceFrames()
    {
        ReferenceColumns = 0;
        ReferenceRows = 0;
        ResliceReference(default);
    }

    /// <summary>Cut the active sheet up again — a different grid, or auto-detect.</summary>
    public void ResliceReference(SliceOptions options)
    {
        if (ActiveReference is not { } strip) return;
        if (Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id) is not { } sheet) return;

        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells = SliceSheet(sheet, options);
            live.LayOutFrom(Math.Max(0, first));
        });
        AfterReferenceChange();
    }

    [RelayCommand]
    private void RemoveReference()
    {
        if (ActiveReference is not { } strip) return;
        var id = strip.Id;
        _editor.Perform(doc =>
        {
            doc.Scene.References?.RemoveAt(ActiveReferenceIndex);
            // Absent, not empty: a document whose last reference is removed
            // goes back to writing no key at all.
            if (doc.Scene.References is { Count: 0 }) doc.Scene.References = null;
        });
        Lightbox.Raster.ReferenceStripRegistry.Forget(id);
        ActiveReferenceIndex = Math.Max(0, ActiveReferenceIndex - 1);
        AfterReferenceChange();
    }

    // ---- editing the grid by hand ---------------------------------------------

    /// <summary>
    /// The grid gizmos are showing and everything else is off.
    /// </summary>
    /// <remarks>
    /// A mode rather than a tool. While it is on, every box on the sheet is
    /// editable at once and the canvas is not a place to draw — the same
    /// bargain <see cref="ReferenceAlignMode"/> makes, for the same reason: a
    /// half-drawn mark made while adjusting a grid is one you then have to
    /// find and undo. Escape leaves it.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuppressesPainting))]
    private bool _referenceGridEditMode;

    partial void OnReferenceGridEditModeChanged(bool value)
    {
        if (!value) SelectedReferenceCell = -1;
        PublishSnapshot();
    }

    /// <summary>Whether some mode has taken the canvas away from the tools.</summary>
    public bool SuppressesPainting => ReferenceGridEditMode;

    /// <summary>Which box the gizmos have selected, or -1.</summary>
    [ObservableProperty]
    private int _selectedReferenceCell = -1;

    /// <summary>
    /// Where a cell lands on the canvas, in document pixels.
    /// </summary>
    /// <remarks>
    /// The same arithmetic the compositor does in <see cref="ScenePassBuilder.ReferencePasses"/>,
    /// exposed so the gizmos can be drawn and hit-tested against exactly what
    /// is on screen. Two copies of this would drift and the boxes would stop
    /// sitting on the drawings they describe.
    /// </remarks>
    public (double X, double Y, double W, double H) CellRect(ReferenceStrip strip, ReferenceCell cell)
    {
        var scale = Math.Max(0.01, strip.Scale);
        return (
            strip.OffsetX + cell.Dx + cell.X * scale,
            strip.OffsetY + cell.Dy + cell.Y * scale,
            cell.Width * scale,
            cell.Height * scale);
    }

    /// <summary>A document point in the active sheet's own pixels.</summary>
    public (double X, double Y) DocToSheet(ReferenceStrip strip, ReferenceCell cell, double x, double y)
    {
        var scale = Math.Max(0.01, strip.Scale);
        return ((x - strip.OffsetX - cell.Dx) / scale, (y - strip.OffsetY - cell.Dy) / scale);
    }

    /// <summary>The box under a document point, or -1.</summary>
    public int ReferenceCellAt(double x, double y)
    {
        if (ActiveReference is not { } strip) return -1;
        // Backwards, so the box drawn last — the one on top — wins.
        for (var i = strip.Cells.Count - 1; i >= 0; i--)
        {
            var (cx, cy, w, h) = CellRect(strip, strip.Cells[i]);
            if (x >= cx && x <= cx + w && y >= cy && y <= cy + h) return i;
        }
        return -1;
    }

    /// <summary>Move one box, in document pixels.</summary>
    public void MoveReferenceCell(int index, double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var cell = strip.Cells[index];
        _editor.PerformDelta(
            _ => { cell.Dx += dx; cell.Dy += dy; },
            _ => { cell.Dx -= dx; cell.Dy -= dy; });
        AfterReferenceChange();
    }

    /// <summary>
    /// Resize one box by dragging a corner, in document pixels.
    /// </summary>
    /// <remarks>
    /// The window onto the sheet changes, not the nudge: growing a box shows
    /// more of the drawing rather than scaling it. A box that scaled its
    /// contents would be a second zoom control with no way to tell it from the
    /// first.
    /// </remarks>
    public void ResizeReferenceCell(int index, bool left, bool top, double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var scale = Math.Max(0.01, strip.Scale);
        var cell = strip.Cells[index];
        var before = cell.Clone();

        var sx = (int)Math.Round(dx / scale);
        var sy = (int)Math.Round(dy / scale);
        var x = left ? cell.X + sx : cell.X;
        var y = top ? cell.Y + sy : cell.Y;
        var w = left ? cell.Width - sx : cell.Width + sx;
        var h = top ? cell.Height - sy : cell.Height + sy;
        // A box with no area is a box you cannot get hold of again.
        if (w < 4 || h < 4) return;

        _editor.PerformDelta(
            _ => { cell.X = x; cell.Y = y; cell.Width = w; cell.Height = h; },
            _ =>
            {
                cell.X = before.X;
                cell.Y = before.Y;
                cell.Width = before.Width;
                cell.Height = before.Height;
            });
        AfterReferenceChange();
    }

    /// <summary>
    /// Put a box's pivot at a document point.
    /// </summary>
    /// <remarks>
    /// Recorded in sheet pixels, so it stays on the same part of the drawing
    /// when the sheet is nudged or rescaled afterwards.
    /// </remarks>
    public void SetReferencePivot(int index, double docX, double docY)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var cell = strip.Cells[index];
        var (x, y) = DocToSheet(strip, cell, docX, docY);
        var (beforeX, beforeY) = (cell.PivotX, cell.PivotY);
        _editor.PerformDelta(
            _ => { cell.PivotX = x; cell.PivotY = y; },
            _ => { cell.PivotX = beforeX; cell.PivotY = beforeY; });
        AfterReferenceChange();
    }

    /// <summary>Remove one box. The sheet is untouched; only the window goes.</summary>
    public void DeleteReferenceCell(int index)
    {
        if (ActiveReference is not { } strip) return;
        if (index < 0 || index >= strip.Cells.Count) return;
        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells.RemoveAt(index);
            live.LayOutFrom(Math.Max(0, first));
        });
        SelectedReferenceCell = -1;
        AfterReferenceChange();
    }

    /// <summary>
    /// Draw a box by hand, from a rectangle in document pixels.
    /// </summary>
    /// <remarks>
    /// The escape hatch from detection. A sheet whose figures overlap, or
    /// whose rows have no gutter, cannot be found from the pixels — no amount
    /// of looking finds a boundary that is not there — so the answer is to let
    /// the artist draw it rather than to guess.
    /// </remarks>
    public void AddReferenceCell(double x, double y, double w, double h)
    {
        if (ActiveReference is not { } strip) return;
        if (w < 4 || h < 4) return;
        var scale = Math.Max(0.01, strip.Scale);
        var sheetX = (int)Math.Round((x - strip.OffsetX) / scale);
        var sheetY = (int)Math.Round((y - strip.OffsetY) / scale);
        var cell = new ReferenceCell
        {
            X = sheetX,
            Y = sheetY,
            Width = (int)Math.Round(w / scale),
            Height = (int)Math.Round(h / scale),
        };
        var first = strip.Slots.FindIndex(s => s >= 0);
        _editor.Perform(doc =>
        {
            var live = doc.Scene.References![ActiveReferenceIndex];
            live.Cells.Add(cell);
            live.LayOutFrom(Math.Max(0, first));
        });
        SelectedReferenceCell = strip.Cells.Count - 1;
        AfterReferenceChange();
    }

    /// <summary>Whether the timeline is short of frames for the boxes found.</summary>
    public bool ReferenceNeedsKeyframes =>
        ActiveReference is { Cells.Count: > 0 } strip && strip.Cells.Count > Scene.FrameCount;

    /// <summary>
    /// One keyframe per box, lined up on the pivots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things at once, because they are one intention: the timeline grows
    /// to hold the reference, and the cells are registered so the pivot sits
    /// still. Being handed an eight-frame reference on a one-frame document is
    /// not a state anybody asked for, and neither is a run cycle that has to
    /// be nudged into place eight times.
    /// </para>
    /// <para>
    /// Alignment is separable — <see cref="ReferenceStrip.AlignByPivot"/> is
    /// its own call — because someone matching a walk wants the travel left in
    /// and someone matching a drawing does not.
    /// </para>
    /// </remarks>
    [RelayCommand]
    public void GenerateReferenceKeyframes()
    {
        if (ActiveReference is not { Cells.Count: > 0 } strip) return;
        var wanted = strip.Cells.Count;
        var first = Math.Max(0, strip.Slots.FindIndex(s => s >= 0));

        _editor.Perform(doc =>
        {
            var scene = doc.Scene;
            while (scene.FrameCount < first + wanted) DocumentEditor.AppendFrame(scene);
            var live = scene.References![ActiveReferenceIndex];
            live.LayOutFrom(first);
            live.AlignByPivot();
        });
        AfterReferenceChange();
        AiStatus = $"{wanted} frames from “{strip.Name}”, aligned on their pivots.";
    }

    /// <summary>Move the cell showing at the playhead, in document pixels.</summary>
    public void NudgeReferenceCell(double dx, double dy)
    {
        if (ActiveReferenceCell is not { } cell) return;
        _editor.PerformDelta(
            _ => { cell.Dx += dx; cell.Dy += dy; },
            _ => { cell.Dx -= dx; cell.Dy -= dy; });
        AfterReferenceChange();
    }

    /// <summary>Move the whole sheet, every frame together.</summary>
    public void NudgeReference(double dx, double dy)
    {
        if (ActiveReference is not { } strip) return;
        _editor.PerformDelta(
            _ => { strip.OffsetX += dx; strip.OffsetY += dy; },
            _ => { strip.OffsetX -= dx; strip.OffsetY -= dy; });
        AfterReferenceChange();
    }

    /// <summary>Undo every per-frame nudge on the active sheet.</summary>
    [RelayCommand]
    private void ClearReferenceAlignment()
    {
        if (ActiveReference is not { } strip) return;
        var before = strip.Cells.ConvertAll(c => (c.Dx, c.Dy));
        _editor.PerformDelta(
            _ => { foreach (var c in strip.Cells) (c.Dx, c.Dy) = (0, 0); },
            _ =>
            {
                for (var i = 0; i < strip.Cells.Count && i < before.Count; i++)
                {
                    (strip.Cells[i].Dx, strip.Cells[i].Dy) = before[i];
                }
            });
        AfterReferenceChange();
    }

    public double ReferenceScale
    {
        get => ActiveReference?.Scale ?? 1;
        set => SetReference(Math.Clamp(value, 0.05, 8), (s, v) => s.Scale = v, ActiveReference?.Scale ?? 1);
    }

    public double ReferenceOpacity
    {
        get => ActiveReference?.Opacity ?? 0.5;
        set => SetReference(Math.Clamp(value, 0, 1), (s, v) => s.Opacity = v, ActiveReference?.Opacity ?? 0.5);
    }

    public bool ReferenceVisible
    {
        get => ActiveReference?.Visible ?? false;
        set => SetReference(value, (s, v) => s.Visible = v, ActiveReference?.Visible ?? false);
    }

    public bool ReferenceFollowsTimeline
    {
        get => ActiveReference?.FollowsTimeline ?? true;
        set => SetReference(value, (s, v) => s.FollowsTimeline = v, ActiveReference?.FollowsTimeline ?? true);
    }

    public double ReferenceCellDx
    {
        get => ActiveReferenceCell?.Dx ?? 0;
        set => NudgeReferenceCell(value - (ActiveReferenceCell?.Dx ?? 0), 0);
    }

    public double ReferenceCellDy
    {
        get => ActiveReferenceCell?.Dy ?? 0;
        set => NudgeReferenceCell(0, value - (ActiveReferenceCell?.Dy ?? 0));
    }

    /// <summary>Which frame of the sheet the playhead is on, for the panel's label.</summary>
    public string ReferenceCellLabel
    {
        get
        {
            if (ActiveReference is not { } strip) return "";
            var slot = CurrentFrameIndex < strip.Slots.Count ? strip.Slots[CurrentFrameIndex] : -1;
            return slot < 0 ? "no reference on this frame" : $"reference frame {slot + 1} of {strip.Cells.Count}";
        }
    }

    private void SetReference<T>(T value, Action<ReferenceStrip, T> apply, T current,
        [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        if (ActiveReference is not { } strip || EqualityComparer<T>.Default.Equals(value, current)) return;
        // A view setting, not an edit to the artwork: no undo entry, the same
        // treatment layer visibility gets.
        apply(strip, value);
        OnPropertyChanged(property);
        AfterReferenceChange();
    }

    /// <summary>
    /// A reference or one of its cells changed. The window redraws the grid
    /// gizmos from this — they are a snapshot, so nothing else would tell it.
    /// </summary>
    public event Action? ReferenceChanged;

    private void AfterReferenceChange()
    {
        NotifyReference();
        OnPropertyChanged(nameof(TimelineVideoClips));
        PublishSnapshot();
        MarkDocumentEdited();
        ReferenceChanged?.Invoke();
    }

    // ---- video clip bars (Q57) --------------------------------------------------

    /// <summary>
    /// The footage clips the timeline shows as bars: every video strip with
    /// at least one frame on the timeline. Image references stay out — their
    /// timing is the sheet's business, and a bar for every sprite sheet
    /// would bury the clips the feature exists for.
    /// </summary>
    public IReadOnlyList<Controls.ClipBar> TimelineVideoClips
    {
        get
        {
            if (Scene.References is not { Count: > 0 } strips) return [];
            var clips = new List<Controls.ClipBar>();
            for (var i = 0; i < strips.Count; i++)
            {
                var strip = strips[i];
                if (strip.VideoPath is null && strip.VideoData is null) continue;
                var first = strip.FirstAssignedSlot();
                if (first < 0) continue;
                clips.Add(new Controls.ClipBar(strip.Name, first, strip.LastAssignedSlot(), i));
            }
            return clips;
        }
    }

    /// <summary>The clip bar's body drag: the whole strip slides along the timeline.</summary>
    public void SlideVideoClip(int stripIndex, int deltaFrames)
    {
        if (deltaFrames == 0 || Scene.References is not { } strips
            || stripIndex < 0 || stripIndex >= strips.Count) return;
        var strip = strips[stripIndex];
        strip.SlideSlots(deltaFrames);
        GrowTimelineTo(strip.LastAssignedSlot() + 1);
        AfterReferenceChange();
        NotifyAudioSurface();   // the shared track surface redraws
    }

    /// <summary>
    /// Drag a video clip's IN edge (Q57): +d hides d more leading frames of
    /// the footage; −d brings hidden ones back. The frames themselves never
    /// leave the strip — trimming is which of them the timeline shows.
    /// </summary>
    public void TrimVideoClipIn(int stripIndex, int deltaFrames)
    {
        if (deltaFrames == 0 || Scene.References is not { } strips
            || stripIndex < 0 || stripIndex >= strips.Count) return;
        var strip = strips[stripIndex];
        var first = strip.FirstAssignedSlot();
        var last = strip.LastAssignedSlot();
        if (first < 0) return;

        if (deltaFrames > 0)
        {
            // Hide leading frames, never the last one standing.
            for (var i = first; i < Math.Min(first + deltaFrames, last); i++)
            {
                strip.Assign(i, -1);
            }
        }
        else
        {
            // Bring hidden leading frames back, while both the timeline and
            // the footage have room.
            var cell = strip.Slots[first];
            for (var step = 1; step <= -deltaFrames; step++)
            {
                var slot = first - step;
                var earlier = cell - step;
                if (slot < 0 || earlier < 0) break;
                strip.Assign(slot, earlier);
            }
        }
        AfterReferenceChange();
        NotifyAudioSurface();
    }

    /// <summary>Drag a video clip's OUT edge: +d shows d more trailing frames, −d hides them.</summary>
    public void TrimVideoClipOut(int stripIndex, int deltaFrames)
    {
        if (deltaFrames == 0 || Scene.References is not { } strips
            || stripIndex < 0 || stripIndex >= strips.Count) return;
        var strip = strips[stripIndex];
        var first = strip.FirstAssignedSlot();
        var last = strip.LastAssignedSlot();
        if (last < 0) return;

        if (deltaFrames < 0)
        {
            for (var i = last; i > Math.Max(last + deltaFrames, first); i--)
            {
                strip.Assign(i, -1);
            }
        }
        else
        {
            var cell = strip.Slots[last];
            for (var step = 1; step <= deltaFrames; step++)
            {
                var later = cell + step;
                if (later >= strip.Cells.Count) break;
                strip.Assign(last + step, later);
            }
            GrowTimelineTo(strip.LastAssignedSlot() + 1);
        }
        AfterReferenceChange();
        NotifyAudioSurface();
    }

    private void GrowTimelineTo(int frameCount)
    {
        if (frameCount <= Scene.FrameCount) return;
        _editor.Perform(doc => doc.Scene.FrameCount = Math.Max(doc.Scene.FrameCount, frameCount));
    }

    private void NotifyReference()
    {
        OnPropertyChanged(nameof(References));
        OnPropertyChanged(nameof(HasReferences));
        OnPropertyChanged(nameof(ActiveReference));
        OnPropertyChanged(nameof(ActiveReferenceCell));
        OnPropertyChanged(nameof(HasReferenceCell));
        OnPropertyChanged(nameof(ReferenceScale));
        OnPropertyChanged(nameof(ReferenceOpacity));
        OnPropertyChanged(nameof(ReferenceVisible));
        OnPropertyChanged(nameof(ReferenceFollowsTimeline));
        OnPropertyChanged(nameof(ReferenceCellDx));
        OnPropertyChanged(nameof(ReferenceCellDy));
        OnPropertyChanged(nameof(ReferenceCellLabel));
        OnPropertyChanged(nameof(ReferenceSummary));
        RemoveReferenceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>"Run — 12 frames, 240×160" for the panel's subtitle.</summary>
    public string ReferenceSummary =>
        ActiveReference is not { } strip
            ? "No reference imported."
            : $"{strip.Cells.Count} frames · {strip.SheetWidth}×{strip.SheetHeight} sheet";

    /// <summary>
    /// The document region the last publish actually recomposited (null = the
    /// whole canvas). What the artist feels as a stutter is this rect growing,
    /// so tests assert on it rather than on wall-clock, which is unusable on a
    /// shared runner.
    /// </summary>
    internal SKRectI? LastPublishClip { get; private set; }

    /// <summary>What the frame currently on screen was composed for (B165).</summary>
    private Rendering.FrameFingerprint? _lastPublished;

    /// <summary>
    /// Playback frames that composed to the pixels already on screen and were
    /// therefore not composed at all.
    /// </summary>
    public int FramesReused { get; private set; }

    /// <summary>
    /// Forget the frame on screen, so the next publish composes rather than
    /// reusing.
    /// </summary>
    /// <remarks>
    /// <b>Called wherever the composite could change without the document or the
    /// playhead changing</b> — a new document, a layer shown or hidden, a
    /// setting that reaches pixels. The fingerprint cannot see those, and a
    /// reuse across one of them shows the previous document's art. Cheap to call
    /// and expensive to forget, so it is wired to the same places that already
    /// invalidate the whole canvas.
    /// </remarks>
    internal void ForgetPublishedFrame() => _lastPublished = null;

    /// <summary>Mark everything dirty, as any pixel-changing edit does. Tests only.</summary>
    internal void MarkWholeCanvasDirtyForTests() => InvalidateWholeCanvas();

    /// <summary>
    /// The builder's view of a frame under a live transform. A method with a
    /// cached delegate rather than a lambda at the call site, so the publish
    /// path allocates nothing for it.
    /// </summary>
    private ScenePassBuilder.TransformSplit? TransformSplitFor(Frame frame) =>
        PartsFor(frame) is { } parts
            ? new ScenePassBuilder.TransformSplit(parts.Moving, parts.Static)
            : null;

    private Func<Frame, ScenePassBuilder.TransformSplit?>? _passTransformSplit;

    /// <summary>
    /// Hold every cached bitmap a published pass list borrows, and give back the
    /// release that lets go of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The lifetime protocol B125 stage 1 built and nothing had yet used.</b>
    /// A pass carries an <see cref="SKBitmap"/> the frame cache owns, and cache
    /// eviction disposes it. While the composite was synchronous on this thread
    /// that was safe by construction — only the finished image crossed, and
    /// <c>CanvasControl._retired</c> managed its lifetime. A published pass list
    /// outlives the call, so an eviction between publish and render would free
    /// pixels Skia is about to read: a use-after-free in native code, which is
    /// B130's exact signature — no managed stack, an empty crash log, and
    /// "Lightbox dies as soon as I touch anything".
    /// </para>
    /// <para>
    /// <b>Counted rather than flagged</b>, because one bitmap can be in two live
    /// pass lists at once — the same cel exposed on two layers, or a publish
    /// overlapping the one before it — and a boolean would free it on the first
    /// release while the second reader was still going.
    /// </para>
    /// <para>
    /// Bitmaps the cache does not own (the live scratch, a bake) pass through
    /// harmlessly: a pin the cache never evicts is a dictionary entry that the
    /// release takes straight back out.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Let go of everything a published snapshot held: unpin what it borrowed,
    /// free what this publish allocated.
    /// </summary>
    /// <remarks>
    /// <b>Two opposite operations on one list, which is why they are named
    /// together.</b> Layer bitmaps belong to the frame cache and must be released
    /// without being freed; flattened tile bitmaps belong to nobody and must be
    /// freed. Disposing a borrowed one is B130; leaking an owned one is a
    /// viewport-sized bitmap per frame of playback.
    /// </remarks>
    private Action ReleaseFor(List<RenderPass> passes, List<SKBitmap>? owned)
    {
        var unpin = PinPasses(passes);
        if (owned is null || owned.Count == 0) return unpin;
        return () =>
        {
            unpin();
            for (var i = 0; i < owned.Count; i++) owned[i].Dispose();
        };
    }

    private Action PinPasses(List<RenderPass> passes)
    {
        // One list per publish, sized to the passes rather than grown. A publish
        // runs per pointer event while drawing, so this is on the drawing path.
        var held = new List<SKBitmap>(passes.Count);
        for (var i = 0; i < passes.Count; i++)
        {
            if (passes[i].Bitmap is not { } bmp) continue;
            // Both caches, because a pass list mixes their bitmaps: layer rasters
            // belong to the frame cache and flattened tiles to the flatten cache
            // (B167 phase 2). Asking which owns a given bitmap would be a third
            // answer to a question two caches already answer for themselves — and
            // a pin on a cache that does not own it is a dictionary entry the
            // matching unpin takes straight back out, which is the same way a
            // live scratch bitmap has always passed through here.
            _cache.Pin(bmp);
            _tileFlats.Pin(bmp);
            held.Add(bmp);
        }

        return () =>
        {
            for (var i = 0; i < held.Count; i++)
            {
                _cache.Unpin(held[i]);
                _tileFlats.Unpin(held[i]);
            }
        };
    }

    /// <summary>Composite the scene for the current playhead and hand it to the view.</summary>
    public void PublishSnapshot()
    {
        var scene = Scene;

        // Take whatever the prewarmer finished since the last publish, BEFORE
        // the pass list asks the caches for anything. Draining afterwards would
        // install a frame one publish after the one that needed it, which is the
        // whole of the benefit gone.
        TakeWarmedFrames();

        // B165, second half: if this frame would compose to exactly the pixels
        // already on screen, do not compose it.
        //
        // On 2s every second playhead position exposes the drawings the last one
        // did, so the composite is identical — and at 4K that composite measured
        // 62.76 ms against an 83.3 ms budget, the largest single cost in the tick.
        // Unlike B125's GPU work this is route-independent: it applies wherever
        // the composite would have happened, including the tiled path playback
        // actually takes.
        //
        // Playback only. While drawing, a publish is how a mark reaches the
        // screen, and the dirty-region machinery already makes that cheap; adding
        // a second "did anything change" question there would be two answers to
        // one question, which is how they come to disagree.
        var fingerprint = new Rendering.FrameFingerprint(
            CurrentFrameIndex, ComposeScale, _pendingViewport,
            CameraViewTransform(ComposeScale),
            Rendering.UnchangedLayerRun.VisibleLayers(scene));
        if (IsPlaying
            && Rendering.FrameFingerprint.WouldBeIdentical(
                _lastPublished, fingerprint, scene,
                anythingDirty: _dirtyIsWholeCanvas || _pendingDirty is not null))
        {
            FramesReused++;
            // Still prewarm: the worker is guessing at frames after this one, and
            // a reused frame is exactly when there is spare time to do it in.
            RequestPlaybackPrewarm(UnboundedCanvasOn || IsPlaying, ComposeScale);
            return;
        }

        // The pass list is built by ScenePassBuilder (B166): a pure function
        // from the document and the state below to an ordered list, with no
        // surface and no graphics context in it. It was 230 lines here, and
        // it is what B125 stage 3 hands across a thread — so it is worth
        // being a named unit whose inputs are written down.
        var passState = new ScenePassBuilder.State(
            CurrentFrameIndex,
            // Asked once here rather than per layer inside the loop, which means
            // a layerless document now reaches it — ActiveLayer indexes and
            // throws on one. It used to be unreachable rather than safe.
            scene.Layers.Count > 0 ? ActiveLayer.Id : null,
            IsPlaying, IsLightTable,
            UnboundedCanvasOn,
            HaveViewport: _pendingViewport is { Width: > 0, Height: > 0 },
            Onion);
        var live = new ScenePassBuilder.LiveEdit(
            _liveComposite, _liveScratch, _livePostScratch, _livePostStampedCount,
            _liveShape, _liveGradient, _strokeBuilder.Current,
            _transformPreview, _transformFrames,
            // The moving/staying split stays behind a delegate because building
            // it caches bitmaps and owns their disposal — state with a lifetime,
            // which is the one thing the pure builder must not hold. Held in a
            // field rather than written as a lambda here: a lambda capturing
            // `this` allocates a closure and a delegate on every publish, and a
            // publish happens per pointer event while drawing.
            _passTransformSplit ??= TransformSplitFor);

        var built = ScenePassBuilder.Build(scene, passState, _cache, _tileFallbacks, live);
        var passes = built.Passes;
        var activeStart = built.ActiveStart;
        var activeEnd = built.ActiveEnd;
        var tileNativeDoc = built.TileNative;

        // Fold the layers that are not being drawn on into two baked bitmaps —
        // see LayerStackBake for the whole argument. Held off during playback,
        // where the pass list changes every frame and a bake could never be
        // reused before it was stale. Downstream (the ring, the culled path,
        // the unbounded path) sees a shorter list of the same pixels.
        // Both folds are asked every publish, even the one that will decline.
        // Each owns a "was I serving a bake last time" flag, and skipping the call
        // leaves that flag stale — so stopping playback could miss a fold
        // transition, and a missed transition is folded and unfolded pixels mixed
        // on one surface by a dirty-region patch. Declining is cheap; not asking
        // is not.
        passes = _stackBake.Fold(
            passes, activeStart, activeEnd, scene.Width, scene.Height, hold: IsPlaying,
            out var foldTransitioned);

        // B165. During playback the not-being-drawn-on segment changes every
        // frame, so Fold above gives up — but the layers holding still for the
        // whole range do not, and folding those is one blend instead of however
        // many there are. A five-frame two-layer capture showed 1 138 layer passes
        // over 568 ticks: a held BG plate, a colour hold and a locked prop
        // re-blended twelve times a second to produce pixels already on screen.
        //
        // The count comes from the exposure sheet rather than from the passes,
        // because they are two different questions: which layers hold still over
        // time is a property of the document, and whether a run may be pre-folded
        // at all is a property of their blends, which the bake checks itself.
        // Zero when not playing, which is how the held segment gets reset.
        var heldRun = IsPlaying
            ? UnchangedLayerRun.HeldPrefix(scene, EffectiveStartFrame, EffectiveEndFrame)
            : 0;
        passes = _stackBake.FoldHeldRun(
            passes, heldRun, scene.Width, scene.Height, out var heldTransitioned);
        foldTransitioned |= heldTransitioned;

        // A fold transition repaints everything once (see the out parameter's
        // remarks): folded and unfolded pixels can differ by an LSB, and a
        // dirty-region patch must never mix the two on one surface.
        if (foldTransitioned) _dirtyIsWholeCanvas = true;

        // Compose at the resolution the canvas can actually show. A 4K document
        // in a laptop window is displayed at roughly 40%, and handing the
        // renderer full detail makes it rescale 8.3 M pixels on every frame —
        // ~29 ms, which is the whole stutter budget before anything is drawn.
        var renderScale = ComposeScale;
        var cameraView = CameraViewTransform(renderScale);

        // What changed since the last publish. Null means "everything", which is
        // what a frame change, a layer edit or a view change produces.
        //
        // Read BEFORE the routing decision on purpose: whether culling is worth
        // taking depends entirely on this, per B121 in ComposePlan.
        var dirty = _dirtyIsWholeCanvas ? null : _pendingDirty;
        _pendingDirty = null;
        _dirtyIsWholeCanvas = false;

        // Which compositor, on what surface, covering what (B166). Arithmetic on
        // six numbers, and the three conditions in it were each learned by
        // breaking them — so it lives where it can be asserted on directly
        // rather than only through a composed image.
        var plan = ComposePlan.For(
            scene.Width, scene.Height,
            cameraView is null ? null : new SKSizeI(scene.Camera!.OutputWidth, scene.Camera!.OutputHeight),
            _pendingViewport, dirty, tileNativeDoc, renderScale);
        var viewWidth = plan.ViewWidth;
        var viewHeight = plan.ViewHeight;
        var info = plan.Info;

        var seq = ++_publishSeq;
        var background = SceneRenderer.BackgroundOf(scene);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var composeScope = Profile(_profilingTick, Services.TickProfile.Phase.Compose);
        SKRectI? usedClip = null;

        // Which document rectangle the finished image actually covers. Decided by
        // the route rather than set in each branch, so it cannot disagree with the
        // surface it describes.
        var imageCovers = plan.ImageCovers;

        SKImage? image;
        DeferredCompose? deferred = null;
        // Bitmaps this publish allocated rather than borrowed (B167 phase 3a):
        // flattened tiles. Borrowed passes are unpinned when the snapshot dies;
        // these have to be freed, which is the opposite operation and the one
        // thing easy to get wrong now that they cross a thread.
        List<SKBitmap>? flattenedOwned = null;
        if (plan.Route == ComposeRoute.Unbounded)
        {
            // Unbounded canvas: use tiled compositing for only visible viewport.
            //
            // Tile passes are flattened into bitmap passes first (B167 phase 3),
            // which is what makes the composite below a pure function of bitmaps
            // and therefore movable to the render thread. A flattened bitmap is
            // now usually borrowed from the flatten cache (B167 phase 2) and
            // unpinned; only one the cache refused is owned here and disposed.
            // B167 phase 3b: describe it rather than do it. Flattening still
            // happens here — it needs the tile cache — but the blending, which
            // phase 1 measured at roughly two thirds of Compose, moves to the
            // draw op where the graphics context is.
            var vp = _pendingViewport!.Value;
            flattenedOwned = [];
            passes = FlattenTilePasses(passes, scene, vp, renderScale, flattenedOwned);
            deferred = new DeferredCompose(
                passes, background, renderScale, info, vp, Tiled: true);
            image = null;
            usedClip = imageCovers;
        }
        else if (plan.CullRect is { } cullRect)
        {
            // B82: bounded canvas, culled to the clamped visible rectangle.
            // B125 stage 3b: describe it rather than do it. The culled route is
            // the one that can move — it already built a fresh surface every
            // publish and filled all of it, so nothing is lost by building it on
            // the render thread instead, where the graphics context is. The ring
            // and the unbounded path stay here; see DeferredCompose for why.
            deferred = new DeferredCompose(passes, background, renderScale, info, cullRect);
            image = null;
            usedClip = cullRect;
            // This publish went around the ring, so every buffer in it now holds
            // an older frame than the artist is looking at. ComposeRing decides
            // what to repaint from its own staleness, so a buffer that believes
            // it is current would repaint a dab onto the previous frame's art and
            // leave the rest of it showing.
            //
            // **Honest note: this is unproven defence, not a tested fix.** I could
            // not construct the stale case through the public API —
            // `AnIncrementalPublishAfterACulledOneDoesNotShowThePreviousFrame`
            // passes with this line deleted, because every EndStroke publishes
            // whole-canvas and marks the other two buffers NeedsFull, so the
            // rotation lands on a buffer that repaints in full anyway. It is kept
            // because it costs nothing (it only runs on a publish that repaints
            // everything regardless) and because stale pixels are wrong quietly,
            // which is the failure this codebase is least able to notice. If a
            // later change makes ComposeRing keep buffers warm across full
            // publishes, this line stops being redundant and starts being load-
            // bearing — do not delete it as dead code on the strength of the test
            // passing without it.
            _composeRing.InvalidateAll();
        }
        else
        {
            // Bounded canvas without culling: use full-document compositing as before
            image = _composeRing.Publish(info, dirty, (surface, clip) =>
            {
                usedClip = clip;
                SceneRenderer.ComposeInto(surface, passes, background, clip, renderScale, cameraView);
            }, renderScale, cameraView);
        }
        sw.Stop();
        composeScope?.Dispose();
        if (Environment.GetEnvironmentVariable("LIGHTBOX_PERFTRACE") is not null)
        {
            Console.Error.WriteLine($"[publish] dirty={dirty} clip={usedClip} passes={passes.Count} {sw.Elapsed.TotalMilliseconds:0.0}ms");
        }
        Performance.RecordPublish(sw.Elapsed.TotalMilliseconds);
        LastPublishClip = usedClip;
        _lastPublished = fingerprint;
        // Everything from here is the handoff: the snapshot swap, the retired
        // images being disposed, and the invalidate. Timed apart from the
        // composite above because one number for both is what sent B156 after
        // the wrong half.
        using var handoffScope = Profile(_profilingTick, Services.TickProfile.Phase.Handoff);
        if (SnapshotChanged is { } handler)
        {
            // ALWAYS the full document size, whatever the compositor did. The canvas
            // derives its fit scale and its pointer mapping from these two numbers,
            // so reporting a culled image's size here moves the cursor off its mark
            // (CursorAlignmentTests measures exactly how far).
            //
            // The viewport passed alongside is the rectangle THIS image covers — not
            // the rectangle the canvas last asked for — because it is what tells the
            // painter where to put the image. Null means "the whole document", which
            // is what every uncalled path produces.
            handler(new RenderSnapshot(
                image, (int)viewWidth, (int)viewHeight, seq, imageCovers,
                SnapshotGeometry.ChangedInImageSpace(
                    usedClip, imageCovers, renderScale, throughCamera: cameraView is not null),
                passes, ReleaseFor(passes, flattenedOwned), deferred));
        }
        else
        {
            // No canvas attached (headless or IPC-only): nobody would ever
            // free this image, and a live snapshot makes the next repaint
            // duplicate the whole buffer. A deferred composite needs no
            // disposal at all — it was never performed, which is the cheapest
            // this path has ever been.
            image?.Dispose();
            if (flattenedOwned is not null) foreach (var b in flattenedOwned) b.Dispose();
        }

        // Last, and after the frame is on its way to the screen: the worker
        // starts on the frames after this one while the artist is looking at
        // this one. Queued from here rather than from the playback tick so that
        // the guess is refreshed by every publish that moves the playhead —
        // scrubbing and stepping included, not only the timer.
        RequestPlaybackPrewarm(tileNativeDoc, renderScale);
    }

    /// <summary>
    /// Install whatever the prewarmer finished, or dispose it if the cache will
    /// not have it.
    /// </summary>
    /// <remarks>
    /// A cache refuses a warm when it already holds the frame, when the warm
    /// does not fit inside the byte budget, or when the frame is one that is
    /// never stored at all. All three are ordinary — speculative work is allowed
    /// to be wasted — and every one of them ends in the pixels being freed here
    /// rather than leaked.
    /// </remarks>
    private void TakeWarmedFrames() => _prewarm.Drain(warmed =>
    {
        var want = warmed.Request;
        if (want.Want == WarmProduct.Tiles)
        {
            return warmed is { Store: { } store, Pyramid: { } pyramid }
                && _tileFrames.InsertWarm(want.Frame, store, pyramid);
        }
        return warmed.Bitmap is { } bmp
            && _cache.InsertWarm(want.Frame, want.Width, want.Height, 1.0, want.CelIndex, bmp);
    });

    /// <summary>
    /// Queue the frames the playhead is about to reach, so the tick that shows
    /// them does not have to rasterize them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only while playing.</b> The prediction is only worth making when the
    /// playhead is moving on its own: an artist drawing goes where they like,
    /// and warming a guess about that would spend a core to fill the cache with
    /// frames nobody asked for. Playback is the one time the next few frames are
    /// known rather than guessed.
    /// </para>
    /// <para>
    /// <b>The tile-or-bitmap decision is <see cref="TileFallback.Reason"/>, the
    /// same call the publish makes.</b> Warming a frame as a bitmap that the
    /// publish then wants as tiles is not wrong, but it is the whole cost paid
    /// for nothing — and it is exactly the kind of divergence that appears when
    /// two places restate one rule. One function, asked twice.
    /// </para>
    /// </remarks>
    private void RequestPlaybackPrewarm(bool tileNativeDoc, double renderScale)
    {
        if (!IsPlaying) return;

        var scene = Scene;
        var last = Math.Max(0, scene.FrameCount - 1);
        var ahead = FramePrewarmer.Upcoming(
            CurrentFrameIndex, _playDirection,
            EffectiveStartFrame, EffectiveEndFrame, LoopPlayback, FramePrewarmer.Lookahead);
        if (ahead.Count == 0) return;

        var level = TilePyramid.LevelFor(renderScale);
        var jobs = new List<WarmRequest>();

        // One drawing, one job. A held cel is the same frame at every exposure
        // it covers, and the paper is one frame under the whole sequence — so
        // without this the background alone is rendered once per position
        // looked ahead, every publish, for a frame that never changes. Measured
        // at six warms where four were wanted on a two-layer document, which is
        // the smallest document there is.
        var queued = new HashSet<string>();
        foreach (var index in ahead)
        {
            var celIndex = Math.Clamp(index, 0, last);
            foreach (var layer in scene.Layers)
            {
                if (!scene.IsLayerVisible(layer)) continue;
                if (ExposureSheet.ExposedFrame(layer, celIndex) is not { } frame) continue;

                // A frame that places a symbol renders differently at different
                // exposures — a placed cycle advances with the sequence — and is
                // cached per index for that reason. Every other frame is the same
                // picture wherever it sits, which is what makes deduplicating by
                // id alone correct for them and wrong for these.
                var once = frame.HasPlacements ? $"{frame.Id}#{celIndex}" : frame.Id;
                if (!queued.Add(once)) continue;

                // No live effect can be in progress: playback abandons a stroke
                // in flight, which is what makes this false rather than a guess.
                var why = tileNativeDoc
                    ? TileFallback.Reason(frame, scene.Camera is not null, true, liveEffectHere: false)
                    : TileFallbackReason.NoViewport;

                if (why == TileFallbackReason.None)
                {
                    if (_tileFrames.Holds(frame.Id)) continue;
                    jobs.Add(new WarmRequest(
                        frame, scene.Width, scene.Height, celIndex, WarmProduct.Tiles, level));
                }
                else
                {
                    if (!FrameBitmapCache.CanCache(frame)) continue;
                    if (_cache.Holds(frame, scene.Width, scene.Height, 1.0, celIndex)) continue;
                    jobs.Add(new WarmRequest(
                        frame, scene.Width, scene.Height, celIndex, WarmProduct.Bitmap));
                }
            }
        }

        // An empty list still supersedes: everything the playhead was going to
        // need is already held, so anything still queued is a frame it has
        // passed.
        _prewarm.Request(jobs);
    }

    /// <summary>
    /// Composite the visible rectangle of an unbounded document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Each pass is one direct draw with a viewport source rectangle</b> —
    /// Skia reads only the source region a draw covers, so this is already
    /// proportional to the viewport rather than the document, and a single
    /// image has no interior edges to seam at any zoom. The previous version
    /// split every layer bitmap into a cached <c>TileStore</c> first, which
    /// bought nothing (the document-sized bitmap it split already existed)
    /// and cost two real defects: the cached store was disposed at the end of
    /// the very loop that cached it, so every repaint after the first drew
    /// from freed tiles — "the infinite canvas does not work at all" — and
    /// the cache keyed on bitmap identity, which a stroke commit does not
    /// change because <c>FrameRasterizer.Append</c> stamps into the cached
    /// bitmap in place (see <c>BitmapVersion</c>).
    /// </para>
    /// <para>
    /// Tileable frames arrive as <c>SourceFrame</c> passes and composite from
    /// <see cref="TileFrameCache"/> — rasterised stroke→tile with no
    /// document-sized bitmap anywhere, through <c>TilePyramid</c> levels so
    /// deep zoom-outs get mip quality. Frames tiles cannot say (baseline
    /// pixels, placements, effect strokes) and ghost passes still ride the
    /// bitmap path below. Ink beyond the nominal canvas is the remaining
    /// step: stroke bounds are still clamped to the document (B134), so the
    /// record keeps off-paper strokes but no tile holds them yet.
    /// </para>
    /// <para>
    /// Pass opacity, tint and blend ride on the draw's paint — the same math
    /// as <c>SceneRenderer.DrawPass</c>, isolation included. The live-stroke
    /// overlay draws in document space under the same viewport transform with
    /// its erase mode and opacity honoured; the alpha-lock and selection-clip
    /// masks are commit-time-only on this path for now — noted rather than
    /// silently dropped.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Turn tile-native passes into ordinary bitmap passes with a placement
    /// matrix, flattening their visible tiles here (B167 phase 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what dissolves the blocker rather than confronting it.</b> The
    /// tiled composite is the one playback takes and the one that has to move to
    /// the render thread — but it reads <c>_tileFrames</c>, which the view model
    /// owns and the draw op cannot reach. Flattening first turns every tile pass
    /// into a bitmap pass, and a bitmap pass is something the deferred compositor
    /// already carries across a thread with a lifetime protocol that works.
    /// </para>
    /// <para>
    /// <b>The flattening still happens here, on the UI thread.</b> Phase 1
    /// measured it at 30–35% of Compose; phase 3b moved the other two thirds to
    /// the render thread.
    /// </para>
    /// <para>
    /// <b>B167 phase 2: most flattens are now looked up rather than done.</b> The
    /// inputs are the frame's tiles, the pyramid level and the level-space
    /// rectangle, and all three are identical from one playback frame to the next
    /// for a layer that holds — so <see cref="TileFlattenCache"/> keys on exactly
    /// those. B165 measured the share of layer draws that repeat a drawing at 26%
    /// at two layers and 59% at ten, and that is the share of this work that
    /// disappears.
    /// </para>
    /// <para>
    /// <b>So a flattened bitmap is now usually borrowed, and the two cases must
    /// not be confused.</b> A cached one is pinned and unpinned — disposing it
    /// would free pixels another live snapshot is still reading, which is B130.
    /// One the cache refused (a viewport larger than the whole budget) is owned
    /// exactly as every flatten was before this phase, and disposed with the
    /// snapshot. Leaking that one is a viewport-sized bitmap per frame of
    /// playback, so neither mistake is quiet.
    /// </para>
    /// </remarks>
    private List<RenderPass> FlattenTilePasses(
        List<RenderPass> passes, Scene scene, SKRectI viewport, double renderScale,
        List<SKBitmap> owned)
    {
        List<RenderPass>? flattened = null;
        for (var i = 0; i < passes.Count; i++)
        {
            if (passes[i].SourceFrame is not { } tileSrc) continue;

            flattened ??= [.. passes];
            // A tile-native pass: composite the visible tiles 1:1 at the pyramid
            // level nearest the screen's resolution, then place that one image in
            // document space — the outer transform does the rest. The residual
            // resample is a single ≤2× downscale of one image, so deep zoom-outs
            // get box-mip quality instead of skip-sampling shimmer, and the
            // intermediate is bounded by the surface however many document pixels
            // the viewport spans.
            var (_, pyramid) = _tileFrames.Get(tileSrc, scene.Width, scene.Height);
            var level = Lightbox.Raster.TilePyramid.LevelFor(renderScale);
            var step = Lightbox.Raster.TilePyramid.StepOf(level);
            var lvp = SKRectI.Create(
                FloorDiv(viewport.Left, step),
                FloorDiv(viewport.Top, step),
                Math.Max(1, viewport.Width / step + 2),
                Math.Max(1, viewport.Height / step + 2));

            // The stamp is what makes a hit safe: it changes whenever this
            // frame's tiles do, so a cached flatten can only match pixels that
            // are still current. See TileFrameCache.StampOf.
            var stamp = _tileFrames.StampOf(tileSrc.Id);
            var flat = _tileFlats.Get(tileSrc.Id, stamp, level, lvp);
            if (flat is null)
            {
                using (Profile(_profilingTick, Services.TickProfile.Phase.TileFlatten))
                {
                    flat = Lightbox.Raster.TileCompositor.CompositeToBitmap(
                        pyramid.Level(level), lvp);
                }
                // Refused means the cache could never hold it — a viewport larger
                // than the whole budget — and then this publish owns it, exactly
                // as every publish did before phase 2.
                if (!_tileFlats.Insert(tileSrc.Id, stamp, level, lvp, flat)) owned.Add(flat);
            }

            // Translate then scale, which is what the canvas did around the draw.
            var placement = SKMatrix.CreateScaleTranslation(
                step, step, lvp.Left * step, lvp.Top * step);
            var p = passes[i];
            flattened[i] = new RenderPass(
                flat, p.Tint, p.Opacity, p.Blend, p.Overlay, placement);
        }
        return flattened ?? passes;
    }


    /// <summary>A pass drawn in full — a windowed reference cell, or one under its own matrix.</summary>

    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear);

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

    /// <summary>
    /// B82: compose only the visible rectangle of a bounded canvas, so the cost
    /// is proportional to what the artist can see rather than to the document.
    /// </summary>
    /// <remarks>
    /// <paramref name="viewport"/> must already be clamped to the document — see
    /// <see cref="ComposePlan.ClampToDocument"/>. The surface covers exactly that
    /// so the painter draws the result into the same rectangle in document space
    /// and the pointer mapping never has to know this happened.
    /// </remarks>
    private static SKImage ComposeViewportCulled(
        List<RenderPass> passes,
        SKColor background,
        double renderScale,
        SKImageInfo info,
        SKRectI viewport)
    {
        var surface = SKSurface.Create(info);
        if (surface is null) throw new InvalidOperationException("Failed to create render surface");

        var canvas = surface.Canvas;
        canvas.Clear(background);

        // Document space, offset so the viewport's top-left is the surface origin.
        // Every pass then draws at its own document coordinates, exactly as it
        // would into a full-document surface — which is the point: the passes do
        // not learn about culling, so a culled and an uncalled compose agree.
        canvas.Scale((float)renderScale, (float)renderScale);
        canvas.Translate(-viewport.Left, -viewport.Top);

        var visible = new SKRect(viewport.Left, viewport.Top, viewport.Right, viewport.Bottom);

        foreach (var pass in passes)
        {
            if (pass.Bitmap is null) continue;

            using var paint = new SKPaint { BlendMode = pass.Blend };
            if (pass.Opacity < 1.0)
                paint.Color = paint.Color.WithAlpha((byte)(pass.Opacity * 255));
            if (pass.Tint.HasValue)
                paint.ColorFilter = SKColorFilter.CreateBlendMode(pass.Tint.Value, SKBlendMode.Multiply);

            // Only the visible sub-rectangle is read, which is where the saving is:
            // src and dst are the same rectangle in document space, so no scaling
            // beyond renderScale and no resampling of the parts nobody can see.
            canvas.DrawBitmap(pass.Bitmap, visible, visible, paint);

            if (pass.Overlay is { } overlay)
            {
                using var overlayPaint = new SKPaint
                {
                    BlendMode = overlay.Erases ? SKBlendMode.DstOut : SKBlendMode.SrcOver,
                };
                if (overlay.Opacity < 1.0)
                    overlayPaint.Color = overlayPaint.Color.WithAlpha((byte)(overlay.Opacity * 255));
                canvas.DrawBitmap(overlay.Scratch, visible, visible, overlayPaint);
            }
        }

        canvas.Flush();
        var image = surface.Snapshot();
        surface.Dispose();
        return image;
    }

    /// <summary>
    /// Limit the next publish to a document region. Only safe when nothing
    /// outside the region can change; every other edit path must leave the
    /// default (whole-canvas) invalidation alone, or stale pixels linger.
    /// </summary>
    private void MarkDirtyRegion(SKRectI region)
    {
        if (_dirtyIsWholeCanvas) return;
        if (_pendingDirty is { } existing)
        {
            existing.Union(region);
            _pendingDirty = existing;
        }
        else
        {
            _pendingDirty = region;
        }
    }

    /// <summary>The next publish repaints everything (the safe default).</summary>
    private void InvalidateWholeCanvas()
    {
        _dirtyIsWholeCanvas = true;
        _pendingDirty = null;
        // B165: and forget what is on screen, so the next publish composes.
        //
        // The reuse guard already refuses while anything is dirty, so this is
        // belt and braces — but it is the cheap half of a pair whose expensive
        // half is stale art. The guard rests on an existing contract: anything
        // that changes pixels must already mark the canvas dirty, or it would not
        // reach the screen today either. This makes that dependency explicit
        // instead of implicit, so a future invalidation path that forgets to mark
        // dirty fails loudly at the canvas rather than quietly here.
        _lastPublished = null;
    }

    // ---- camera ---------------------------------------------------------------

    /// <summary>
    /// Whether this document has a camera at all. Everything camera-related in
    /// the UI hangs off this: a document without one shows no overlay, no
    /// controls and no ruler keys. Optional means absent, not disabled.
    /// </summary>
    public bool HasCamera => Scene.Camera is not null;

    /// <summary>
    /// The camera frame's corners in document coordinates, or null. The canvas
    /// draws this as view-only chrome — it never reaches a pixel.
    /// </summary>
    public SKPoint[]? CameraFrameCorners { get; private set; }

    /// <summary>Fired when the camera appears, disappears, or reframes.</summary>
    public event Action? CameraChanged;

    /// <summary>The framing at the playhead — what the overlay and the fields show.</summary>
    private CameraFraming FramingNow() =>
        CameraOps.At(Scene.Camera, CurrentFrameIndex, Scene.Width, Scene.Height);

    /// <summary>
    /// Give the scene a camera, framed on the whole canvas at 1:1 so the first
    /// thing the artist sees is what they already had. Output defaults to the
    /// canvas size for the same reason — a camera should start by changing
    /// nothing.
    /// </summary>
    [RelayCommand]
    private void AddCamera()
    {
        if (Scene.Camera is not null) return;
        Scene.Camera = new Camera { OutputWidth = Scene.Width, OutputHeight = Scene.Height };
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Take the camera away entirely, keys and all, returning the document to
    /// the state it saves in when it never had one.
    /// </summary>
    [RelayCommand]
    private void RemoveCamera()
    {
        if (Scene.Camera is null) return;
        Scene.Camera = null;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Key the current framing at the playhead.</summary>
    [RelayCommand]
    private void SetCameraKey()
    {
        if (Scene.Camera is not { } camera) return;
        CameraOps.SetKey(camera, CurrentFrameIndex, FramingNow());
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Retime a camera key — the track timeline's dot drag. Refuses an
    /// occupied destination rather than clobbering a framing the artist
    /// authored; the status line says why nothing moved.
    /// </summary>
    public void MoveCameraKey(int fromFrame, int toFrame)
    {
        if (Scene.Camera is not { } camera) return;
        if (CameraOps.KeyAt(camera, fromFrame) is not { } key) return;
        if (CameraOps.KeyAt(camera, toFrame) is not null)
        {
            AiStatus = "There is already a camera key on that frame.";
            return;
        }
        key.Frame = toFrame;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Author a key at the given frame with the framing already interpolated
    /// there — the graph's double-click. Keying what is already true changes
    /// nothing visually, which is exactly what makes it safe to then drag.
    /// </summary>
    public void AddCameraKeyAt(int frame)
    {
        if (Scene.Camera is not { } camera) return;
        if (CameraOps.KeyAt(camera, frame) is not null) return;
        CameraOps.SetKey(camera, frame, CameraOps.At(camera, frame, Scene.Width, Scene.Height));
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Remove the key at the given frame — the graph's key menu.</summary>
    public void RemoveCameraKeyAt(int frame)
    {
        if (Scene.Camera is not { } camera) return;
        if (!CameraOps.ClearKey(camera, frame)) return;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>The easing a key runs into its successor with, for the menu's check mark.</summary>
    public Easing? CameraKeyEaseAt(int frame) => CameraOps.KeyAt(Scene.Camera, frame)?.Ease;

    /// <summary>Set how the key at the given frame eases into the next one.</summary>
    public void SetCameraKeyEase(int frame, Easing ease)
    {
        if (CameraOps.KeyAt(Scene.Camera, frame) is not { } key || key.Ease == ease) return;
        key.Ease = ease;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>Remove the key at the playhead, if there is one.</summary>
    [RelayCommand]
    private void ClearCameraKey()
    {
        if (Scene.Camera is not { } camera) return;
        if (!CameraOps.ClearKey(camera, CurrentFrameIndex)) return;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>True when the playhead sits on an authored camera key.</summary>
    public bool IsOnCameraKey => CameraOps.KeyAt(Scene.Camera, CurrentFrameIndex) is not null;

    /// <summary>Frames carrying a camera key, for the timeline ruler.</summary>
    public IReadOnlyList<int> CameraKeyFrames =>
        CameraOps.Ordered(Scene.Camera).Select(k => k.Frame).ToList();

    public int CameraOutputWidth
    {
        get => Scene.Camera?.OutputWidth ?? Scene.Width;
        set => SetCameraOutput(Math.Clamp(value, 1, 16384), CameraOutputHeight);
    }

    public int CameraOutputHeight
    {
        get => Scene.Camera?.OutputHeight ?? Scene.Height;
        set => SetCameraOutput(CameraOutputWidth, Math.Clamp(value, 1, 16384));
    }

    private void SetCameraOutput(int width, int height)
    {
        if (Scene.Camera is not { } camera) return;
        if (camera.OutputWidth == width && camera.OutputHeight == height) return;
        camera.OutputWidth = width;
        camera.OutputHeight = height;
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// The framing at the playhead, editable. Writing any of these moves the
    /// live framing; it only becomes part of the shot once keyed, which is the
    /// same bargain as a transform gizmo before it is committed.
    /// </summary>
    public double CameraX
    {
        get => FramingNow().X;
        set => SetFraming(FramingNow() with { X = value });
    }

    public double CameraY
    {
        get => FramingNow().Y;
        set => SetFraming(FramingNow() with { Y = value });
    }

    public double CameraZoom
    {
        get => FramingNow().Zoom;
        set => SetFraming(FramingNow() with { Zoom = Math.Clamp(value, 0.05, 64) });
    }

    public double CameraRotationDeg
    {
        get => FramingNow().RotationDeg;
        set => SetFraming(FramingNow() with { RotationDeg = value });
    }

    /// <summary>
    /// The canvas gizmo's three drags. Each goes through
    /// <see cref="SetFraming"/>, so a drag keys the framing at the playhead
    /// exactly as typing into the fields does — adjusting IS keying.
    /// </summary>
    public void NudgeCamera(double dx, double dy)
    {
        var now = FramingNow();
        SetFraming(now with { X = now.X + dx, Y = now.Y + dy });
    }

    public void ZoomCameraBy(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) return;
        var now = FramingNow();
        SetFraming(now with { Zoom = Math.Clamp(now.Zoom * factor, 0.05, 64) });
    }

    public void RotateCameraBy(double deltaDeg)
    {
        if (!double.IsFinite(deltaDeg)) return;
        var now = FramingNow();
        SetFraming(now with { RotationDeg = now.RotationDeg + deltaDeg });
    }

    /// <summary>
    /// Editing a framing field writes it straight to the key at the playhead,
    /// creating one if there is none. A framing you cannot see keyed is a
    /// framing you will lose by scrubbing away from it.
    /// </summary>
    private void SetFraming(CameraFraming framing)
    {
        if (Scene.Camera is not { } camera) return;
        CameraOps.SetKey(camera, CurrentFrameIndex, framing);
        RefreshCamera();
        NotifyCameraSurface();
        _autosave.MarkDirty();
    }

    /// <summary>
    /// Show the canvas through the camera rather than the world. Off by
    /// default: the artist draws in the world, and this is for checking the
    /// shot.
    /// </summary>
    [ObservableProperty]
    private bool _viewThroughCamera;

    partial void OnViewThroughCameraChanged(bool value)
    {
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        RefreshCamera();
        PublishSnapshot();
    }

    /// <summary>The matrix a publish composites through, or null for the world.</summary>
    private SKMatrix? CameraViewTransform(double renderScale) =>
        ViewThroughCamera && Scene.Camera is { } camera
            ? CameraTransform.Matrix(
                FramingNow(), camera.OutputWidth, camera.OutputHeight, renderScale)
            : null;

    private void RefreshCamera()
    {
        // No camera, or looking through it — in which case the frame IS the
        // viewport and an overlay would just outline the window.
        CameraFrameCorners = Scene.Camera is { } camera && !ViewThroughCamera
            ? CameraTransform.FrameCorners(FramingNow(), camera.OutputWidth, camera.OutputHeight)
            : null;
        CameraChanged?.Invoke();
    }

    private void NotifyCameraSurface()
    {
        OnPropertyChanged(nameof(TimelineTracks));
        OnPropertyChanged(nameof(GraphSeriesList));
        OnPropertyChanged(nameof(HasCamera));
        OnPropertyChanged(nameof(IsOnCameraKey));
        OnPropertyChanged(nameof(CameraKeyFrames));
        OnPropertyChanged(nameof(CameraOutputWidth));
        OnPropertyChanged(nameof(CameraOutputHeight));
        OnPropertyChanged(nameof(CameraX));
        OnPropertyChanged(nameof(CameraY));
        OnPropertyChanged(nameof(CameraZoom));
        OnPropertyChanged(nameof(CameraRotationDeg));
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        PublishSnapshot();
    }

    // ---- performance preferences -----------------------------------------------

    /// <summary>
    /// How much detail to composite. Only affects what is shown while you
    /// work — the record, exports and thumbnails are always full resolution.
    /// </summary>
    [ObservableProperty]
    private CanvasQuality _canvasQuality = CanvasQuality.Display;

    public IReadOnlyList<CanvasQuality> CanvasQualityChoices { get; } = Enum.GetValues<CanvasQuality>();

    partial void OnCanvasQualityChanged(CanvasQuality value)
    {
        Settings.CanvasQuality = value.ToString();
        Settings.Save();
        InvalidateWholeCanvas();
        _composeRing.InvalidateAll();
        Performance.Reset();
        PublishSnapshot();
        RefreshDocumentStats();
    }

    /// <summary>The artist chose this quality, so nothing may revise it again.</summary>
    public void ChooseCanvasQuality(CanvasQuality value)
    {
        Settings.CanvasQualityChosen = true;
        CanvasQuality = value;   // its handler saves
    }

    /// <summary>
    /// React to finding out the frame is being presented without a GPU.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The label alone was a diagnosis with no treatment. On the software
    /// rasteriser, presenting the canvas — rescaling the whole document for
    /// every frame — overtakes editing it as the dominant cost, and
    /// <see cref="CanvasQuality.Half"/> is the one setting that changes how
    /// many pixels that is. Turning it down is the difference between a
    /// laggy canvas and a usable one on exactly the machines that cannot fix
    /// it any other way.
    /// </para>
    /// <para>
    /// It only ever revises a default. Once somebody has picked a quality
    /// themselves the app has no business overruling it, however slow the
    /// machine — the whole point of a preference is that it is theirs. And it
    /// is announced rather than done quietly: a canvas that silently got
    /// softer is a bug report.
    /// </para>
    /// </remarks>
    public void NoteGraphicsBackend()
    {
        if (Rendering.CanvasControl.SoftwareRendering is not true) return;
        RefreshDocumentStats();
        if (!TurnTheCanvasQualityDown()) return;
        AiStatus =
            "No GPU here, so the canvas is being drawn in software — quality lowered to Half "
            + "while you work. Exports are unaffected. Edit ▸ Configure ▸ Performance changes it.";
    }

    /// <summary>
    /// The one lever, pulled once, and never over the artist.
    /// </summary>
    /// <remarks>
    /// Shared by the two things that can decide the canvas needs help — the
    /// backend coming back as software, and the measurement saying so — because
    /// the conditions under which it is allowed to happen at all are the same
    /// and must not drift apart.
    /// </remarks>
    private bool TurnTheCanvasQualityDown()
    {
        // Two rules, and between them they also carry "only once": the first
        // pull leaves the quality at Half, which is no longer the default.
        if (Settings.CanvasQualityChosen || CanvasQuality != CanvasQuality.Display) return false;
        CanvasQuality = CanvasQuality.Half;
        return true;
    }

    /// <summary>
    /// Turn the quality down when the canvas is <em>measurably</em> not keeping
    /// up, whatever the graphics backend says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The software-rendering path above acts on a well-founded guess: no GPU
    /// context means presenting the frame will dominate, and that is true
    /// before a single frame has been measured. What it cannot do is help the
    /// machine that <em>has</em> a GPU and is still too slow — an integrated
    /// chip on a 4K canvas with a dozen onion ghosts is slower than a software
    /// rasteriser on a sprite sheet, and it was getting a label saying
    /// everything was fine.
    /// </para>
    /// <para>
    /// So the backend is an input rather than the trigger. Software renderers
    /// get help at the first sign of trouble because there is no other lever on
    /// those machines; a GPU has to be visibly struggling first, since the more
    /// likely fix there is something about the document and the advice already
    /// names it.
    /// </para>
    /// <para>
    /// Once per session, only over a default, and announced — the same three
    /// rules the backend path follows. A canvas that silently got softer is a
    /// bug report.
    /// </para>
    /// </remarks>
    public void ConsiderCanvasRelief()
    {
        if (Settings.CanvasQualityChosen || CanvasQuality != CanvasQuality.Display) return;
        if (!Performance.HasSettled) return;

        var software = Rendering.CanvasControl.SoftwareRendering is true;
        var threshold = software ? 40 : 20;
        if (Performance.HeadroomPercent > threshold) return;
        if (!TurnTheCanvasQualityDown()) return;

        AiStatus =
            $"The canvas is taking {Performance.FrameMs:0} ms a frame to show, so quality has been "
            + "lowered to Half while you work. The drawing, exports and thumbnails are unaffected. "
            + "Edit ▸ Configure ▸ Performance changes it.";
    }

    /// <summary>Undo steps kept. Deltas are cheap; snapshots hold a whole document each.</summary>
    public int UndoDepth
    {
        get => _editor.MaxUndo;
        set
        {
            var clamped = Math.Clamp(value, 5, 500);
            if (_editor.MaxUndo == clamped) return;
            foreach (var tab in Tabs) tab.Editor.MaxUndo = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>Ceiling for cached frame bitmaps, in megabytes.</summary>
    /// <remarks>
    /// <para>
    /// <b>The artist's setting stays the final word</b> — the derivation in
    /// <see cref="Services.MemoryBudget"/> decides the <em>default</em>, which is
    /// the part that was wrong.
    /// </para>
    /// <para>
    /// <b>Its floor is deliberately below the derived one</b> and that is not an
    /// inconsistency. The derived floor is what a minimum-spec machine needs for
    /// the cache to be worth having; this floor is how far somebody may go when
    /// they have decided they would rather have the memory — which is what the
    /// Configure page's own text offers ("lowering it frees memory on a large
    /// canvas"). The ceiling is shared, because above it the cache is holding
    /// bytes it will never spend whoever asked for them.
    /// </para>
    /// </remarks>
    public int FrameCacheBudgetMb
    {
        get => (int)(FrameBitmapCache.ByteBudget / (1024 * 1024));
        set
        {
            var clamped = Math.Clamp(
                value, 64, (int)(Services.MemoryBudget.FrameCacheCeilingBytes / (1024 * 1024)));
            if (FrameCacheBudgetMb == clamped) return;
            FrameBitmapCache.ByteBudget = clamped * 1024L * 1024L;
            OnPropertyChanged();
            RefreshDocumentStats();
        }
    }

    // ---- info strip ------------------------------------------------------------

    /// <summary>"3840 × 2160 px · 72 ppi" for the info strip.</summary>
    [ObservableProperty]
    private string _documentSizeLabel = "";

    /// <summary>"4 layers · 24 drawings" for the info strip.</summary>
    [ObservableProperty]
    private string _documentContentLabel = "";

    /// <summary>Approximate image memory this document is holding.</summary>
    [ObservableProperty]
    private string _memoryLabel = "";

    private void RefreshDocumentStats()
    {
        var scene = Scene;
        var drawings = new HashSet<string>();
        foreach (var layer in scene.Layers)
        {
            foreach (var cel in layer.Cels)
            {
                if (cel.Frame is { } frame) drawings.Add(frame.Id);
            }
        }

        DocumentSizeLabel = $"{scene.Width} × {scene.Height} px · {scene.Ppi} ppi";
        DocumentContentLabel =
            $"{scene.Layers.Count} layer{(scene.Layers.Count == 1 ? "" : "s")} · " +
            $"{drawings.Count} drawing{(drawings.Count == 1 ? "" : "s")}";

        var bytes = _cache.CachedBytes + _composeRing.AllocatedBytes;
        var backend = Rendering.CanvasControl.GraphicsBackend;
        MemoryLabel = (bytes >= 1024L * 1024 * 1024
            ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB images"
            : $"{bytes / (1024.0 * 1024):0} MB images")
            + (backend == "unknown" ? "" : $" · {backend}");
        Performance.DescribeDocument(scene.Width, scene.Height, scene.Layers.Count, drawings.Count, bytes);
    }

    /// <summary>
    /// Apply feature defaults to a new document based on the project's type.
    /// If no project is open, no features are set (document uses implicit defaults).
    /// </summary>
    private void ApplyFeatureDefaults(Doc doc)
    {
        if (ProjectDocker.Project?.Manifest.Type is not { } projectType) return;

        var defaults = new Lightbox.Core.Projects.FeatureDefaults();
        var features = Enum.GetValues<Lightbox.Core.Projects.FeatureKey>();

        // Build a dictionary of features that differ from false (our implicit default).
        // Only write overrides; absent features use their defaults.
        var overrides = new Dictionary<string, bool>();
        foreach (var feature in features)
        {
            var defaultValue = defaults.GetDefault(projectType, feature);
            if (defaultValue)
            {
                overrides[feature.ToString()] = true;
            }
        }

        // Only set Features if there are any overrides
        if (overrides.Count > 0)
        {
            doc.Features = overrides;
        }
    }

    /// <summary>Update a document feature toggle (Configure → Features page).</summary>
    public void SetDocumentFeature(Lightbox.Core.Projects.FeatureKey feature, bool value, bool projectDefault)
    {
        if (ActiveTab?.Doc is not { } doc) return;

        doc.Features ??= [];

        if (value == projectDefault)
        {
            // Value matches default; remove from overrides
            doc.Features.Remove(feature.ToString());
            if (doc.Features.Count == 0) doc.Features = null;
        }
        else
        {
            // Override the default
            doc.Features[feature.ToString()] = value;
        }

        // A feature can change how frames are rendered — unbounded canvas
        // swaps document-sized bitmaps for sparse tiles — so every render made
        // under the old setting is stale the moment the toggle lands.
        ClearFrameRenders();
        PublishSnapshot();

        _autosave.MarkDirty();
    }
}
