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

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q78, which was 13,628 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- video clip bars (Q57) --------------------------------------------------

    /// <summary>
    /// The footage clips the timeline shows as bars: one per section of every
    /// video strip with at least one frame on the timeline (Q57) — an unsplit
    /// clip is one bar, a split one is a bar per section. Image references
    /// stay out — their timing is the sheet's business, and a bar for every
    /// sprite sheet would bury the clips the feature exists for.
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
                var runs = strip.AssignedRuns();
                for (var r = 0; r < runs.Count; r++)
                {
                    var name = runs.Count == 1 ? strip.Name : $"{strip.Name} {r + 1}";
                    clips.Add(new Controls.ClipBar(name, runs[r].Start, runs[r].End, i));
                }
            }
            return clips;
        }
    }

    /// <summary>
    /// The section starting at <paramref name="runStart"/> and its timeline
    /// neighbours in the same strip, or null when no section starts there —
    /// the drag began on a bar the last edit has already replaced.
    /// </summary>
    private static ((int Start, int End) Run, (int Start, int End)? Prev, (int Start, int End)? Next)?
        VideoRunAt(ReferenceStrip strip, int runStart)
    {
        var runs = strip.AssignedRuns();
        for (var i = 0; i < runs.Count; i++)
        {
            if (runs[i].Start != runStart) continue;
            return (runs[i],
                i > 0 ? runs[i - 1] : null,
                i < runs.Count - 1 ? runs[i + 1] : null);
        }
        return null;
    }

    /// <summary>
    /// Two sections an edit has butted together stay two sections: the split
    /// point at the junction is what keeps <see cref="ReferenceStrip.AssignedRuns"/>
    /// from reading them as one bar again.
    /// </summary>
    private static void KeepSectionsApart(ReferenceStrip strip, int junction)
    {
        strip.SplitPoints ??= [];
        if (!strip.SplitPoints.Contains(junction)) strip.SplitPoints.Add(junction);
        strip.NormaliseSplitPoints();
    }

    /// <summary>
    /// The clip bar's body drag (Q57): the section starting at
    /// <paramref name="runStart"/> slides along the timeline, stopping at its
    /// neighbours — sections never ride over each other.
    /// </summary>
    public void SlideVideoClip(int stripIndex, int runStart, int deltaFrames)
    {
        if (deltaFrames == 0 || Scene.References is not { } strips
            || stripIndex < 0 || stripIndex >= strips.Count) return;
        var strip = strips[stripIndex];
        if (VideoRunAt(strip, runStart) is not { } hit) return;
        var (run, prev, next) = hit;

        var delta = deltaFrames;
        if (prev is { } p) delta = Math.Max(delta, p.End + 1 - run.Start);
        if (next is { } n) delta = Math.Min(delta, n.Start - 1 - run.End);
        if (delta == 0) return;

        strip.SlideRange(run.Start, run.End, delta);
        if (prev is { } pb && run.Start + delta == pb.End + 1) KeepSectionsApart(strip, run.Start + delta);
        if (next is { } nb && run.End + delta == nb.Start - 1) KeepSectionsApart(strip, nb.Start);
        GrowTimelineTo(strip.LastAssignedSlot() + 1);
        AfterReferenceChange();
        NotifyAudioSurface();   // the shared track surface redraws
    }

    /// <summary>
    /// Drag a video section's IN edge (Q57): +d hides d more leading frames of
    /// the footage; −d brings hidden ones back until the section meets its
    /// neighbour or the footage runs out. The frames themselves never leave
    /// the strip — trimming is which of them the timeline shows.
    /// </summary>
    public void TrimVideoClipIn(int stripIndex, int runStart, int deltaFrames)
    {
        if (deltaFrames == 0 || Scene.References is not { } strips
            || stripIndex < 0 || stripIndex >= strips.Count) return;
        var strip = strips[stripIndex];
        if (VideoRunAt(strip, runStart) is not { } hit) return;
        var (run, prev, _) = hit;

        if (deltaFrames > 0)
        {
            // Hide leading frames, never the last one standing.
            for (var i = run.Start; i < Math.Min(run.Start + deltaFrames, run.End); i++)
            {
                strip.Assign(i, -1);
            }
            strip.NormaliseSplitPoints();
        }
        else
        {
            // Bring hidden leading frames back, while the timeline, the
            // footage and the previous section all leave room.
            var cell = strip.Slots[run.Start];
            for (var step = 1; step <= -deltaFrames; step++)
            {
                var slot = run.Start - step;
                var earlier = cell - step;
                if (slot < 0 || earlier < 0) break;
                if (slot < strip.Slots.Count && strip.Slots[slot] >= 0) break;
                strip.Assign(slot, earlier);
                if (prev is { } p && slot == p.End + 1)
                {
                    KeepSectionsApart(strip, slot);
                    break;
                }
            }
        }
        AfterReferenceChange();
        NotifyAudioSurface();
    }

    /// <summary>
    /// Drag a video section's OUT edge: +d shows d more trailing frames until
    /// the footage ends or the next section starts, −d hides them.
    /// </summary>
    public void TrimVideoClipOut(int stripIndex, int runStart, int deltaFrames)
    {
        if (deltaFrames == 0 || Scene.References is not { } strips
            || stripIndex < 0 || stripIndex >= strips.Count) return;
        var strip = strips[stripIndex];
        if (VideoRunAt(strip, runStart) is not { } hit) return;
        var (run, _, next) = hit;

        if (deltaFrames < 0)
        {
            for (var i = run.End; i > Math.Max(run.End + deltaFrames, run.Start); i--)
            {
                strip.Assign(i, -1);
            }
            strip.NormaliseSplitPoints();
        }
        else
        {
            var cell = strip.Slots[run.End];
            for (var step = 1; step <= deltaFrames; step++)
            {
                var slot = run.End + step;
                var later = cell + step;
                if (later >= strip.Cells.Count) break;
                if (slot < strip.Slots.Count && strip.Slots[slot] >= 0) break;
                strip.Assign(slot, later);
                if (next is { } n && slot == n.Start - 1)
                {
                    KeepSectionsApart(strip, n.Start);
                    break;
                }
            }
            GrowTimelineTo(strip.LastAssignedSlot() + 1);
        }
        AfterReferenceChange();
        NotifyAudioSurface();
    }

    /// <summary>
    /// Cut the video section under the playhead in two (Q57). False when the
    /// playhead is not inside a section of this strip — an edge or a gap has
    /// nothing to split.
    /// </summary>
    public bool SplitVideoAtPlayhead(int stripIndex)
    {
        if (Scene.References is not { } strips
            || stripIndex < 0 || stripIndex >= strips.Count) return false;
        var strip = strips[stripIndex];
        var frame = CurrentFrameIndex;
        foreach (var run in strip.AssignedRuns())
        {
            if (frame <= run.Start || frame > run.End) continue;
            KeepSectionsApart(strip, frame);
            AfterReferenceChange();
            NotifyAudioSurface();
            return true;
        }
        return false;
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
    internal SKRectI? LastPublishClip => _publish.LastPublishClip;

    /// <summary>
    /// Playback frames that composed to the pixels already on screen and were
    /// therefore not composed at all.
    /// </summary>
    public int FramesReused => _publish.FramesReused;

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
    internal void ForgetPublishedFrame() => _publish.LastPublished = null;

    /// <summary>Mark everything dirty, as any pixel-changing edit does. Tests only.</summary>
    internal void MarkWholeCanvasDirtyForTests() => _publish.InvalidateWholeCanvas();

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
    /// The fetch the fold defers — cached in a field for the same reason as
    /// <see cref="_passTransformSplit"/>: a lambda capturing <c>this</c>
    /// allocates per publish, and a publish runs per pointer event.
    /// </summary>
    private Func<ScenePassBuilder.PassSpec, RenderPass>? _materializePass;

    /// <summary>
    /// Every cel fetched for the publish in flight, pinned from the moment it
    /// leaves the cache until the publish is over.
    /// </summary>
    /// <remarks>
    /// <b>Closes a use-after-free that was reachable before anything here
    /// changed.</b> A publish materializes its whole pass list before
    /// compositing, and the cache disposes an unpinned bitmap the moment
    /// eviction picks it — so on a document whose single-frame working set
    /// exceeds the byte budget (about 64 layers at 1080p against the 512 MB
    /// default, B198's exact measurement), fetching layer N evicted and freed
    /// layer 1's bitmap while the pass list still referenced it, and the
    /// composite drew disposed pixels. The bench never hit it because it
    /// composes each pass as it fetches; the publish path does not have that
    /// luxury. <c>PinPasses</c> could not help — it pins at snapshot creation,
    /// after the whole list is already materialized. So the pin moves to the
    /// fetch, and the release to the end of the publish, by which point the
    /// snapshot holds its own pins on everything that is still referenced.
    /// </remarks>
    private readonly List<SKBitmap> _fetchHolds = [];

    private RenderPass MaterializePass(ScenePassBuilder.PassSpec spec)
    {
        var pass = ScenePassBuilder.Materialize(spec, _cache, Scene.Width, Scene.Height);
        // Only cache-owned bitmaps need the hold: eviction is the only thing
        // that frees pixels mid-publish, and it only reaches what the cache
        // owns. Live scratches, transform parts and reference sheets are
        // owned elsewhere and outlive the publish on their own.
        if (spec.CelFrame is not null && pass.Bitmap is { } bmp)
        {
            _cache.Pin(bmp);
            _fetchHolds.Add(bmp);
        }
        return pass;
    }

    /// <inheritdoc cref="_fetchHolds"/>
    private void ReleaseFetchHolds()
    {
        for (var i = 0; i < _fetchHolds.Count; i++) _cache.Unpin(_fetchHolds[i]);
        _fetchHolds.Clear();
    }

    /// <summary>
    /// B170's debug tripwire: a live stroke's scratch is owned and mutated by
    /// the UI thread, so a deferred compose that carries one is reading — and
    /// possibly outliving — a bitmap nobody handed over. The culled route
    /// should only go deferred on whole-canvas publishes, which a live stroke
    /// does not ordinarily produce; the known exception is a live pass that
    /// returns no bounds and invalidates the whole canvas mid-stroke, which is
    /// exactly the unusual publish the diagnosis wants caught in the act.
    /// Debug builds only — the shipped build records the breadcrumb instead.
    /// </summary>
    /// <remarks>
    /// The tiled route is deliberately not asserted: with the unbounded canvas
    /// on, every publish goes deferred, live overlays included — a known open
    /// hazard recorded in B170, and an assert that fires on every unbounded
    /// stroke would be noise rather than a tripwire.
    /// </remarks>
    [System.Diagnostics.Conditional("DEBUG")]
    private static void AssertNoLiveScratchCrossesTheThread(List<RenderPass> passes)
    {
        for (var i = 0; i < passes.Count; i++)
        {
            System.Diagnostics.Debug.Assert(
                passes[i].Overlay is null,
                "B170: a live stroke's scratch is crossing to the render thread "
                + "on the culled route — record how this publish came about.");
        }
    }

    /// <summary>Every frame mutation goes through here, whichever cache holds it.</summary>
    /// <remarks>
    /// The stack bake is in the list because its spec key compares frames by
    /// reference and an edit mutates the frame behind the reference — this
    /// funnel is the only thing standing between an under-the-bake edit and
    /// stale art on screen. A mutation path that bypasses the funnel is a
    /// defect here even if every cache happens to survive it.
    /// </remarks>
    /// <param name="repaintBounds">
    /// Every pixel the edit could have moved, in document coordinates — B327's
    /// hint. With one, the frame's cached bitmap is <em>patched</em> over that
    /// rectangle instead of dropped, so undoing a mark costs what making it
    /// cost. Null, or a frame the patch cannot promise, falls back to dropping
    /// it. Only the full-render cache takes the hint: the tiles and the
    /// thumbnail are cheap to rebuild and the bake only wants telling.
    /// </param>
    private void InvalidateFrameRender(string frameId, GeometryOps.BBox? repaintBounds = null)
    {
        _publish.BumpRenderEpoch();
        // Before the cache work rather than after it (B327). A warm in flight was
        // started from the frame as it stood before this edit; on the drop path it
        // would be refused on arrival because the entry it targets is gone, but a
        // *patched* entry is still there to be overwritten — so a stale warm could
        // land on top of the repair. Flushing first removes the question.
        _prewarm.Flush();
        if (TryRepaintFrameRegion(frameId, repaintBounds))
        {
            FrameRegionRepaints++;
        }
        else
        {
            FrameRenderDrops++;
            _cache.Invalidate(frameId);
        }
        _tileFrames.Invalidate(frameId);
        _thumbs.Invalidate(frameId);
        _stackBake.NoteFrameChanged(frameId);
    }

    /// <summary>
    /// A committed mark's footprint, as the rectangle an undo of it would have
    /// to repaint (B327). Null for a mark with no computable reach, which is
    /// the answer that keeps the old whole-drawing behaviour.
    /// </summary>
    /// <remarks>
    /// <b><see cref="BrushEngine.CommitBounds"/>, and with no origin, because
    /// that is the space the frame cache renders in.</b> <c>FrameRasterizer</c>
    /// materializes a cel with no origin argument, so the bitmap this rectangle
    /// indexes into starts at zero whatever <c>Scene.Left</c> says. Handing an
    /// origin-adjusted rectangle to the patch would repair the wrong part of the
    /// picture on a document that had been grown.
    /// </remarks>
    private GeometryOps.BBox? RepaintBoundsOf(Stroke stroke)
    {
        var info = new SKImageInfo(
            Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        return BrushEngine.CommitBounds(stroke, info) is { } rect
            ? new GeometryOps.BBox(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : null;
    }

    /// <summary>
    /// Patch the cached render of one frame over <paramref name="repaintBounds"/>,
    /// or say it could not be done. See B327.
    /// </summary>
    /// <summary>
    /// How many frame invalidations were served by patching a rectangle rather
    /// than by dropping the drawing's pixels (B327), and how many were not.
    /// </summary>
    /// <remarks>
    /// Counters rather than timings, for the reason the performance budgets
    /// give: what the fix is about is <em>which path ran</em>, and a millisecond
    /// assertion measures the machine it ran on. The refused count is the one
    /// worth watching — it is how a new commit path that forgets to declare its
    /// footprint shows up as something other than a mystery.
    /// </remarks>
    internal int FrameRegionRepaints { get; private set; }

    /// <inheritdoc cref="FrameRegionRepaints"/>
    internal int FrameRenderDrops { get; private set; }

    private bool TryRepaintFrameRegion(string frameId, GeometryOps.BBox? repaintBounds)
    {
        if (repaintBounds is not { } bounds) return false;
        if (FrameById(Doc, frameId) is not { } frame) return false;

        // Outward to whole pixels: a mark covering part of a pixel dirties all of
        // it, and rounding inward leaves a hairline of the old drawing behind.
        var rect = new SKRectI(
            (int)Math.Floor(bounds.MinX),
            (int)Math.Floor(bounds.MinY),
            (int)Math.Ceiling(bounds.MaxX),
            (int)Math.Ceiling(bounds.MaxY));
        return _cache.RepaintRegion(frame, rect);
    }

    /// <inheritdoc cref="InvalidateFrameRender"/>
    private void ClearFrameRenders()
    {
        _publish.BumpRenderEpoch();
        _cache.Clear();
        _tileFrames.Clear();
        // Correctness does not need this — every flatten key carries the stamp of
        // tiles that no longer exist, so none of them can be found again. It is
        // here because "the whole document changed" is the moment those bytes are
        // certainly dead, and waiting for an LRU to notice would hold a document's
        // worth of viewports across a document switch.
        _tileFlats.Clear();
        _thumbs.Clear();
        _stackBake.Reset();
        _prewarm.Flush();
    }

    /// <summary>
    /// Commit one stroke's pixels incrementally — onto the cached bitmap, and
    /// into the cached tiles when playback holds this frame as tiles. Both
    /// are invariant 6's shape: work proportional to the stroke.
    /// </summary>
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
        FrameRasterizer.Append(_cache.Get(target, Scene.Width, Scene.Height), stroke);
        // Almost always a no-op — the active layer's own segment is never
        // baked — but the same Frame can be exposed on another layer too, and
        // a bake covering that layer would otherwise keep the pre-stroke
        // pixels. Two hash lookups buys never having to think about it.
        _stackBake.NoteFrameChanged(target.Id);
    }

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

    /// <summary>
    /// Who published while the clock was running (B178) — the counter its
    /// entry asks for instead of another change to the publish path. Declared
    /// in this partial because only the publish path writes it.
    /// </summary>
    private readonly PublishTally _publishTally = new();

    /// <inheritdoc cref="_publishTally"/>
    internal PublishTally ReportPublishTally => _publishTally;

    /// <summary>Composite the scene for the current playhead and hand it to the view.</summary>
    /// <param name="publisher">
    /// Compiler-stamped name of the member that asked, for B178's per-caller
    /// tally. Never pass it by hand — the value's worth is that it cannot lie
    /// about where the call came from.
    /// </param>
    /// <summary>
    /// The last few published frames and the buffers behind them, for looking
    /// at an artifact after it has gone. Off until armed from the Help menu.
    /// </summary>
    /// <remarks>
    /// Here rather than in <c>MainViewModel.cs</c> because that file is under a
    /// ratchet and this is new work: the rule is that new work goes into a
    /// partial rather than onto the end of a file that is already too big. The
    /// publish path this records from is in this partial anyway, which makes it
    /// the right home rather than merely an available one.
    /// </remarks>
    internal Services.FrameCapture Capture { get; } = new();

    /// <summary>
    /// Every frame the UI thread has built and what each cost (B321) — as a
    /// distribution, not a running total.
    /// </summary>
    /// <remarks>
    /// A <see cref="Services.Tally"/> rather than three fields because the mean
    /// alone was read off this and believed: one 2,062 ms stall in a session of
    /// 381 publishes put 5.4 ms on a build whose typical cost is 3.2, and the
    /// report drew a confident and wrong conclusion from it.
    /// </remarks>
    private readonly Services.Tally _buildTally = new();

    /// <inheritdoc cref="_buildTally"/>
    internal int ComposeCount => (int)_buildTally.Count;

    /// <inheritdoc cref="_buildTally"/>
    internal double ComposeTotalMs => _buildTally.TotalMs;

    /// <inheritdoc cref="_buildTally"/>
    internal double ComposeWorstMs => _buildTally.WorstMs;

    /// <inheritdoc cref="_buildTally"/>
    internal double ComposeMedianMs => _buildTally.MedianMs;

    /// <inheritdoc cref="Services.Tally.MeanIsDistorted"/>
    internal bool ComposeMeanIsDistorted => _buildTally.MeanIsDistorted;

    /// <summary>
    /// The whole publish cycle — one publish to the next — so every part of it
    /// can be measured as a share of something rather than in isolation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Because the parts stopped adding up.</b> After the dam's release was
    /// made prompt, the owner's capture read a hold of 28.31 ms and a
    /// <c>publish -&gt; drawn</c> of 31.92 — which together allow about sixteen
    /// publishes a second, and the session managed nine and a half. Roughly
    /// <b>forty-five milliseconds a cycle</b> is in neither, and no number in
    /// the report went anywhere near it.
    /// </para>
    /// <para>
    /// So the cycle is measured whole and the known parts are subtracted from
    /// it, the same discipline the frame build's phases were given after three
    /// of them summed to 2.52 ms of 22.63 and the report said nothing about the
    /// difference. A remainder that has to be printed cannot be a blind spot.
    /// </para>
    /// </remarks>
    private readonly Services.Tally _cycleTally = new();

    /// <summary>
    /// From the dam letting go to the publish it released actually running.
    /// </summary>
    /// <remarks>
    /// <b>The prime suspect, and it is there by choice.</b> The announcement now
    /// jumps ahead of pointer input, but <c>NoteFramePresented</c> releases the
    /// dam and then goes through <c>RequestSnapshot</c>, whose own post stays at
    /// Input — deliberately, because B73's ordering says a released publish must
    /// land behind the events already queued. So the release is prompt and the
    /// publish it triggers still waits for every queued pointer event, each
    /// paying its own stamping. Whether that is most of the missing time or a
    /// slice of it is exactly what this says.
    /// </remarks>
    private readonly Services.Tally _releaseToPublishTally = new();

    /// <inheritdoc cref="_cycleTally"/>
    internal Services.Tally CycleTally => _cycleTally;

    /// <inheritdoc cref="_releaseToPublishTally"/>
    internal Services.Tally ReleaseToPublishTally => _releaseToPublishTally;

    /// <summary>When the dam last let go, for the split above. Zero when it has not.</summary>
    internal long DamReleasedAtTicks { get; set; }

    /// <summary>
    /// The three phases inside one frame build, so a slow build names its own
    /// cause instead of being one number (B321's split, one level down).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Asked because the one number stopped discriminating.</b> B321 split
    /// the pen-to-screen wait and the split is what caught its own verdict
    /// being wrong. Its remaining term, "building each frame", then grew from
    /// 11.27 ms to 27.73 ms on the owner's machine and is now the largest work
    /// item in the chain — seven times the draw — and there is nothing in the
    /// report that says which of the three things it does is responsible.
    /// </para>
    /// <para>
    /// The three are genuinely different fixes, which is the point of
    /// separating them: <b>describing</b> is the pass list, the stack fold and
    /// the cel fetches, and a cost here is cache or bookkeeping;
    /// <b>compositing</b> is the CPU blend on the UI thread, and a cost here is
    /// B125 stage 6, which is architectural and expensive; <b>handing off</b>
    /// is the snapshot swap and the retire, and a cost here is neither.
    /// </para>
    /// </remarks>
    internal double BuildDescribeMs { get; private set; }

    /// <inheritdoc cref="BuildDescribeMs"/>
    internal double BuildComposeMs { get; private set; }

    /// <inheritdoc cref="BuildDescribeMs"/>
    internal double BuildHandoffMs { get; private set; }

    /// <summary>
    /// Close off one frame's build, timed from the publish stamp (B321).
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="PublishState.LastPublishTicks"/> rather than taking a
    /// second timestamp: it is stamped at the top of this method for the dam's
    /// liveness check, so it already marks the moment the build began and a
    /// second reading would only invite the two to drift.
    /// </remarks>
    private void NoteComposeCost()
    {
        var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _publish.LastPublishTicks)
                 * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _buildTally.Add(ms);
    }

    public void PublishSnapshot(
        [System.Runtime.CompilerServices.CallerMemberName] string publisher = "")
    {
        // B178: publishing outran drawing 1.5× in the field capture — 757
        // published against 339 ticks — and which of PublishSnapshot's 45 call
        // sites supply the surplus is a question for a counter, not a grep.
        // Tallied during playback only, so the table is the tick's surplus
        // rather than thousands of legitimate pointer publishes.
        if (IsPlaying) _publishTally.Note(publisher);

        // Any publish satisfies a deferred one — the snapshot it hands over is
        // built from the current state, which includes whatever the deferral
        // was waiting to show.
        // A publish satisfies whatever deferral was open, however it came to be
        // let through — and until now only two of the three ways closed the
        // BOOKS on it. The pointer-event path (CanvasIsBehind answering false
        // because AdoptRenderedSeq saw the draw) published and left the tally
        // open, so `publish held back` was a mean over the quarter of deferrals
        // that happened to go through the announcement or the timer. Measured
        // 2026-08-27: 1,568 deferrals against 388 accounted releases.
        if (_publish.WaitingForPresent) NoteDamReleased(byPresent: true, byEvent: true);
        _publish.WaitingForPresent = false;
        var publishAt = System.Diagnostics.Stopwatch.GetTimestamp();
        // Before LastPublishTicks is overwritten, because that field IS the
        // previous publish until this line moves it.
        if (_publish.LastPublishTicks != 0)
        {
            _cycleTally.Add((publishAt - _publish.LastPublishTicks)
                            * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }
        if (DamReleasedAtTicks != 0)
        {
            _releaseToPublishTally.Add((publishAt - DamReleasedAtTicks)
                                       * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            DamReleasedAtTicks = 0;
        }
        _publish.LastPublishTicks = publishAt;

        // Belt to the release at the end of this method: if an exception left
        // holds behind, the next publish must not stack a second set on top.
        ReleaseFetchHolds();

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
            CurrentFrameIndex, ComposeScale, _publish.Viewport,
            CameraViewTransform(ComposeScale),
            Rendering.UnchangedLayerRun.VisibleLayers(scene));
        if (IsPlaying
            && Rendering.FrameFingerprint.WouldBeIdentical(
                _publish.LastPublished, fingerprint, scene,
                anythingDirty: _publish.AnythingDirty))
        {
            _publish.FramesReused++;
            // Still prewarm: the worker is guessing at frames after this one, and
            // a reused frame is exactly when there is spare time to do it in.
            RequestPlaybackPrewarm(IsPlaying, ComposeScale);
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
            HaveViewport: _publish.Viewport is { Width: > 0, Height: > 0 },
            Onion,
            IsScrubbing,
            // Depth answers to a camera move, so it applies exactly when the
            // composite is about to be drawn under the camera's matrix.
            ThroughCamera: ViewThroughCamera);
        var live = new ScenePassBuilder.LiveEdit(
            _live.Composite, _live.Scratch, _live.PostScratch, _live.PostStampedCount,
            _liveShape, _liveGradient, LiveTextPaint, _strokeBuilder.Current,
            _transform.Preview, _transform.Frames,
            // The moving/staying split stays behind a delegate because building
            // it caches bitmaps and owns their disposal — state with a lifetime,
            // which is the one thing the pure builder must not hold. Held in a
            // field rather than written as a lambda here: a lambda capturing
            // `this` allocates a closure and a delegate on every publish, and a
            // publish happens per pointer event while drawing.
            _passTransformSplit ??= TransformSplitFor,
            MaskEditing: EditingLayerMask,
            TipScratch: BuildLiveTip(),
            TipBounds: _live.TipUsed);

        var built = ScenePassBuilder.Describe(scene, passState, _cache, _tileFallbacks, live);
        var tileNativeDoc = built.TileNative;

        // Fold the layers that are not being drawn on into two baked bitmaps,
        // and materialize the rest — see LayerStackBake for the whole
        // argument. The fold sits BETWEEN describing and fetching on purpose
        // (B198): a spec under a valid bake is never handed to the cache, so
        // its cel does not need to be resident at all, which is what dissolves
        // the wall where one frame's working set outgrows the cache budget and
        // every recomposite re-rasterizes what the last one evicted. Held off
        // during playback, where the pass list changes every frame and a bake
        // could never be reused before it was stale. Downstream (the ring, the
        // culled path, the tiled path) sees a shorter list of the same pixels.
        // Both folds are asked every publish, even the one that will decline.
        // Each owns a "was I serving a bake last time" flag, and skipping the call
        // leaves that flag stale — so stopping playback could miss a fold
        // transition, and a missed transition is folded and unfolded pixels mixed
        // on one surface by a dirty-region patch. Declining is cheap; not asking
        // is not.
        var passes = _stackBake.Fold(
            built, scene.Width, scene.Height, hold: IsPlaying,
            _materializePass ??= MaterializePass,
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

        // Q143: what the viewed variant wears, one pass over the whole stack.
        // Appended after the folds so every compositor route carries it; the
        // bitmap is cached per frame index in MainViewModel.Variants.cs, and a
        // bitmap neither cache owns passes through pin/unpin harmlessly. Null
        // — one delegate test — for everyone not viewing a dressed variant.
        if (WornOverlayFor(scene, CurrentFrameIndex) is { } worn)
        {
            passes.Add(new RenderPass(worn, null, 1.0));
        }

        // A fold transition repaints everything once (see the out parameter's
        // remarks): folded and unfolded pixels can differ by an LSB, and a
        // dirty-region patch must never mix the two on one surface.
        if (foldTransitioned) _publish.RepaintEverythingThisPublish();

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
        var dirty = _publish.TakeDirty();

        // A dirty rect is document-space on the layer being painted, and a
        // layer with a depth lands its pixels somewhere else on screen while
        // the view is through the camera. Widened here — the one funnel every
        // MarkDirty drains through — so the ring's and the cull's clips cover
        // the plane without either learning about parallax. The whole check
        // costs a null test on documents that never author a depth.
        if (dirty is { } dirtyRect && ViewThroughCamera
            && scene.Camera is { } dirtyCam
            && scene.Layers.Count > 0 && ActiveLayer.HasDepth)
        {
            var parallaxFrame = Rendering.ParallaxTransform.Prepare(
                CameraOps.At(dirtyCam, CurrentFrameIndex, scene.Width, scene.Height),
                CameraFraming.Centred(scene.Width, scene.Height),
                dirtyCam.OutputWidth, dirtyCam.OutputHeight);
            if (parallaxFrame?.MatrixFor(ActiveLayer.Depth) is { } planeMatrix)
            {
                dirty = Rendering.ParallaxTransform.CoverPlane(dirtyRect, planeMatrix);
            }
        }

        // Which compositor, on what surface, covering what (B166). Arithmetic on
        // six numbers, and the three conditions in it were each learned by
        // breaking them — so it lives where it can be asserted on directly
        // rather than only through a composed image.
        var plan = ComposePlan.For(
            scene.Width, scene.Height,
            cameraView is null ? null : new SKSizeI(scene.Camera!.OutputWidth, scene.Camera!.OutputHeight),
            _publish.Viewport, dirty, tileNativeDoc, renderScale);
        var viewWidth = plan.ViewWidth;
        var viewHeight = plan.ViewHeight;
        var info = plan.Info;

        // B170's breadcrumb: if the next thing that happens is a native crash,
        // the report can at least say whether a stroke was live and which
        // compositor the last publish took — the two facts its diagnosis
        // needs from the next sighting. Const strings on purpose; this runs
        // per pointer event and an enum ToString allocates.
        Services.DiagnosticLog.NoteRender(
            live.BrushStroke is not null || live.Shape is not null
                || live.Gradient is not null || live.Composite is not null,
            plan.Route switch
            {
                ComposeRoute.Ring => "ring",
                ComposeRoute.ViewportCulled => "culled (deferred)",
                _ => "tiled (deferred)",
            });

        var seq = _publish.NextSequence();
        var background = SceneRenderer.BackgroundOf(scene);
        // Everything above this line is describing the frame: the pass list,
        // the stack fold and the cel fetches. Timed from the publish stamp for
        // NoteComposeCost's reason — one clock, no drift between the parts and
        // the whole (B321).
        BuildDescribeMs += (System.Diagnostics.Stopwatch.GetTimestamp() - _publish.LastPublishTicks)
                           * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
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
        if (plan.Route == ComposeRoute.Tiled)
        {
            // Tiled compositing covers only the visible viewport.
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
            var vp = _publish.Viewport!.Value;
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
            // and the tiled path stay here; see DeferredCompose for why.
            AssertNoLiveScratchCrossesTheThread(passes);
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
            // The ring, over the whole document or over a window onto it (B291).
            // `plan.Origin` is zero for the whole-document case, so that route is
            // byte-for-byte what it always was.
            image = _composeRing.Publish(info, dirty, (surface, clip) =>
            {
                usedClip = clip;
                SceneRenderer.ComposeInto(
                    surface, passes, background, clip, renderScale, cameraView, plan.Origin);
            }, renderScale, cameraView, plan.Origin);
        }
        sw.Stop();
        BuildComposeMs += sw.Elapsed.TotalMilliseconds;
        composeScope?.Dispose();
        if (Environment.GetEnvironmentVariable("LIGHTBOX_PERFTRACE") is not null)
        {
            Console.Error.WriteLine($"[publish] dirty={dirty} clip={usedClip} passes={passes.Count} {sw.Elapsed.TotalMilliseconds:0.0}ms");
        }
        Performance.RecordPublish(sw.Elapsed.TotalMilliseconds);
        _publish.LastPublishClip = usedClip;
        _publish.LastPublished = fingerprint;
        // Everything from here is the handoff: the snapshot swap, the retired
        // images being disposed, and the invalidate. Timed apart from the
        // composite above because one number for both is what sent B156 after
        // the wrong half.
        var handoffFrom = System.Diagnostics.Stopwatch.GetTimestamp();
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
            var snapshot = new RenderSnapshot(
                image, (int)viewWidth, (int)viewHeight, seq, imageCovers,
                SnapshotGeometry.ChangedInImageSpace(
                    usedClip, imageCovers, renderScale, throughCamera: cameraView is not null),
                passes, ReleaseFor(passes, flattenedOwned), deferred)
            {
                // B167 phase 7: which composite this is, when it is one worth
                // keeping. Only while playing, and only with no live edit —
                // the same scope B165's reuse check already takes, for the same
                // reason. A composite taken mid-stroke is one nothing will ever
                // ask for again, and caching it would spend the budget on
                // frames that cannot be hit.
                // `deferred is not null` is the load-bearing half and it is not
                // belt-and-braces: a snapshot that already carries an image has
                // nothing left to compose, so Materialise returns before it ever
                // reaches the cache. Keying one would be a setting wired to
                // nothing — the failure this codebase keeps finding. The ring
                // route publishes an image; the tiled route playback takes does
                // not, which is where the saving is.
                CacheKey = deferred is not null && IsPlaying
                    && _live.Composite is null && _live.Scratch is null
                    ? new Rendering.ComposeKey(fingerprint, _publish.RenderEpoch)
                    : null,
            };
            // B321: the last unmeasured box in the chain from pen to screen.
            // Everything from the publish stamp at the top of this method to
            // here is the UI thread BUILDING the frame — the composite B189
            // sized at ~27 ms when it added the publish dam, and which nothing
            // has re-measured since. The rest of the chain is instrumented end
            // to end now, and a gap in the middle is the one place a cost can
            // still hide.
            BuildHandoffMs += (System.Diagnostics.Stopwatch.GetTimestamp() - handoffFrom)
                              * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            NoteComposeCost();
            Record();
            handler(snapshot);
        }
        else
        {
            // Recorded here too, before the image below is freed. A capture
            // that only worked when a canvas happened to be listening is a
            // diagnostic with a silent hole in it, and three tests found the
            // hole the same minute it was made.
            Record();
            // No canvas attached (headless or IPC-only): nobody would ever
            // free this image, and a live snapshot makes the next repaint
            // duplicate the whole buffer. A deferred composite needs no
            // disposal at all — it was never performed, which is the cheapest
            // this path has ever been.
            image?.Dispose();
            if (flattenedOwned is not null) foreach (var b in flattenedOwned) b.Dispose();
        }

        // Kept as a local so both arms above record the same thing from the
        // same state, rather than one of them drifting.
        //
        // Called AFTER the build is timed, and that is not tidiness. Recording
        // costs three scaled blits of document-sized bitmaps, and from inside
        // the timed window it added ~20 ms to a 22.63 ms "building each frame"
        // on the owner's machine — an instrument that made the thing it was
        // measuring look four times worse than it is, on 1,279 of 1,337
        // publishes. The buffers are unchanged by the handoff, so recording
        // here records exactly what the composite read.
        void Record()
        {
            if (!Capture.Armed) return;
            Capture.Note(
                image, _live.Scratch, _live.PostScratch,
                $"route {plan.Route} clip {usedClip} dirty {dirty} "
                + $"points {_strokeBuilder.Current?.Points.Count ?? 0} "
                + $"passRendered {_live.PostStampedCount} passes {LivePostPasses}");
        }

        // The snapshot above holds its own pins on every bitmap still in the
        // pass list, so the per-fetch holds have done their job: from here a
        // later fetch evicting an earlier one is deferred disposal, not a
        // use-after-free.
        ReleaseFetchHolds();

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
            for (var layerIndex = 0; layerIndex < scene.Layers.Count; layerIndex++)
            {
                var layer = scene.Layers[layerIndex];
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
                    ? TileFallback.Reason(
                        frame, scene.Camera is not null, true, liveEffectHere: false,
                        posed: _cache.Rig.IsPosed(frame),
                        shaped: LayerShapes.Carves(scene, layerIndex),
                        // tileNativeDoc already folded the document-level
                        // effects gate in (the builder computed it), so the
                        // per-frame ask cannot be reached with effects live.
                        docEffects: false)
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
    /// Composite the visible rectangle through the tiled route.
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
            // Shapes ride along unchanged; a shaped pass never goes
            // tile-native (TileFallbackReason.Shaped), so this is null today
            // and carrying it is what keeps that a fallback decision rather
            // than a silent drop here.
            flattened[i] = new RenderPass(
                flat, p.Tint, p.Opacity, p.Blend, p.Overlay, placement, Shapes: p.Shapes);
        }
        return flattened ?? passes;
    }


    /// <summary>A pass drawn in full — a windowed reference cell, or one under its own matrix.</summary>

    private static readonly SKSamplingOptions Linear = new(SKFilterMode.Linear);

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

    /// <summary>
    /// How far, in document pixels, this document's live effects spread a
    /// change — what every dirty region grows by (see
    /// <see cref="PublishState.DirtyInflationOf"/>). Conservative on purpose:
    /// the active layer's own stack, every visible adjustment stack, and the
    /// scene grade are summed whether or not each one actually covers the
    /// edit, because a too-wide repaint costs milliseconds and a too-narrow
    /// one leaves a smear at the region's edge that nobody traces back.
    /// </summary>
    private int EffectDirtyInflation()
    {
        var scene = Scene;
        if (!EffectPasses.AnyLive(scene)) return 0;
        var frame = CurrentFrameIndex;
        var reach = Lightbox.Raster.Effects.EffectRegistry.ReachOf(scene.Effects, frame);
        var active = ActiveLayer;
        foreach (var layer in scene.Layers)
        {
            if (!layer.HasLiveEffects || !scene.IsLayerVisible(layer)) continue;
            if (layer.IsAdjustment || ReferenceEquals(layer, active))
            {
                reach += Lightbox.Raster.Effects.EffectRegistry.ReachOf(layer.Effects, frame);
            }
        }
        return (int)Math.Ceiling(reach);
    }

    /// <summary>
    /// Everything visible below the layer being painted on, at the playhead, or
    /// null when there is nothing there.
    /// </summary>
    /// <remarks>
    /// Null rather than a transparent bitmap for the bottom layer, so a smudge
    /// there costs nothing and behaves exactly as it always did. Here rather
    /// than in MainViewModel.cs for the ratchet's reason: it is render-path
    /// code, and the main file may not grow.
    /// </remarks>
    private SKBitmap? CompositeBelowActiveLayer()
    {
        var scene = Scene;
        var active = ActiveLayer;
        var passes = new List<RenderPass>();
        for (var layerIndex = 0; layerIndex < scene.Layers.Count; layerIndex++)
        {
            var layer = scene.Layers[layerIndex];
            if (ReferenceEquals(layer, active)) break;
            if (!scene.IsLayerVisible(layer)) continue;
            if (layer.IsAdjustment)
            {
                if (EffectPasses.AdjustmentPass(scene, layerIndex, CurrentFrameIndex, _cache) is { } adj)
                {
                    passes.Add(adj);
                }
                continue;
            }
            if (ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } frame) continue;
            // A smudge or blur samples what it visibly sits on, so the
            // backdrop is shaped exactly as the composite is.
            var shapes = LayerShapes.For(scene, layerIndex, CurrentFrameIndex);
            if (shapes is { Count: 0 }) continue;
            passes.Add(new RenderPass(
                _cache.Get(frame, scene.Width, scene.Height, celIndex: CurrentFrameIndex),
                null, layer.Opacity, SceneRenderer.ToSkia(layer.BlendMode),
                Shapes: LayerShapes.Resolve(shapes, _cache, scene.Width, scene.Height, CurrentFrameIndex),
                Effect: EffectPasses.SelfFilter(layer, CurrentFrameIndex),
                Style: EffectPasses.SelfStyle(layer, CurrentFrameIndex)));
        }
        if (passes.Count == 0) return null;
        using var below = SceneRenderer.Compose(
            scene.Width, scene.Height, passes, SKColors.Transparent);
        return SKBitmap.FromImage(below);
    }

    /// <summary>How often the live tip was drawn, refused, and how far behind the pass was (B322).</summary>
    internal int LiveTipDrawn { get; private set; }

    /// <summary>Publishes where the pass was too far behind to draw a tip within the budget.</summary>
    internal int LiveTipTooFarBehind { get; private set; }

    /// <summary>
    /// Publishes where no live pass had run at all, so a tip was neither needed
    /// nor possible (B322).
    /// </summary>
    /// <remarks>
    /// <b>Counted because its absence was read as a result.</b> The first capture
    /// of the fifth attempt printed no tip line, and the tip line's absence is
    /// produced equally by "the fix did nothing" and "this brush never had the
    /// bug". The owner had drawn with an Airbrush, which takes no post-process
    /// pass — every dab was already on screen and there was nothing to fix — and
    /// the report could not say so. A diagnostic that is silent in two different
    /// situations reports neither.
    /// </remarks>
    internal int LiveTipNoPass { get; private set; }

    /// <summary>Every outstanding-dab count seen, so the report can say where the budget should sit.</summary>
    internal Services.Tally LiveTipOutstanding { get; } = new();

    /// <summary>
    /// What restamping the tip actually costs, so the budget is set from a
    /// measurement rather than from caution (B322).
    /// </summary>
    /// <remarks>
    /// <b>The budget's whole job is to keep this bounded, and until it was timed
    /// nobody could say what value did that.</b> 128 was picked to be obviously
    /// safe and turned the fix off during exactly the fast strokes it exists
    /// for — the owner reported no preview at all mid-stroke while slow strokes
    /// showed it throughout, which is this refusal seen from the pen. Read
    /// against the outstanding count's own spread, this says how far the budget
    /// can move before the stamp stops being free.
    /// </remarks>
    internal Services.Tally LiveTipStampMs { get; } = new();

    /// <summary>
    /// Dabs added between one publish and the next, which is what a tip that
    /// ACCUMULATED would have to stamp (B322, attempt 6).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one number that says whether the fast-stroke case is reachable.</b>
    /// The tip is rebuilt from scratch every publish, so it stamps the whole
    /// outstanding run each time — 783 dabs at the p90, 16.8 us each, 13 ms a
    /// publish against a 20.7 ms cycle. That is why the budget refuses fast
    /// strokes and why the preview vanishes in them.
    /// </para>
    /// <para>
    /// A tip that kept what it had and stamped only what arrived since would
    /// pay <em>this</em> instead, and the total over a stroke would be
    /// proportional to its dabs rather than to the sum of every outstanding
    /// run. If this median is small while the outstanding median is not, the
    /// sixth attempt is worth building. If they are the same, it is not, and
    /// the fast case needs a different idea entirely.
    /// </para>
    /// <para>
    /// Recorded on <b>every</b> publish of a live stroke, including the ones the
    /// budget refuses — those are precisely the fast strokes the question is
    /// about, and measuring only the publishes that drew a tip would sample the
    /// slow ones and answer confidently about the wrong case.
    /// </para>
    /// </remarks>
    internal Services.Tally LiveTipNewDabs { get; } = new();

    private int _lastPublishDabs = -1;

    /// <summary>Publishes that added to the tip rather than rebuilding it (B322 attempt 6).</summary>
    internal int LiveTipAdded { get; private set; }

    /// <summary>Publishes that had to rebuild it because the pass had moved.</summary>
    internal int LiveTipRebuilt { get; private set; }

    /// <summary>Dabs stamped by an addition, and by a rebuild — the two costs, kept apart.</summary>
    internal Services.Tally LiveTipDabsAdded { get; } = new();

    /// <summary>Dabs stamped by a rebuild.</summary>
    internal Services.Tally LiveTipDabsRebuilt { get; } = new();

    /// <summary>
    /// Dabs the tip stamped per publish, whichever path it took — the divisor
    /// for anything per-dab, and the number to compare against the outstanding
    /// run (B322 attempt 6).
    /// </summary>
    /// <remarks>
    /// <b>Because the report kept dividing by the wrong thing.</b> When the tip
    /// was rebuilt every publish, dabs stamped WAS the outstanding run and the
    /// report divided by that. Attempt 6 stamps a fraction of it, and the
    /// divisor was not changed with the mechanism — so a per-dab cost came out
    /// 3.3x high and a verdict compared how OFTEN each path ran instead of what
    /// each cost, and announced a saving of 3.3x as "attempt 6 has not paid".
    /// </remarks>
    internal Services.Tally LiveTipDabsStamped { get; } = new();

    /// <summary>
    /// The dabs stamped since the last completed pass, drawn raw so the tip of
    /// the mark is on screen while the pass catches up (B322).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What it may cost is decided before anything is stamped</b>, by
    /// <see cref="Rendering.LiveTipPlan"/>, and that separation is the whole
    /// lesson of the fourth attempt: the rule "draw what is outstanding" is
    /// unbounded and self-amplifying, and it was buried in the code that acted
    /// on it where no test could reach it.
    /// </para>
    /// <para>
    /// <b>Restamped rather than copied.</b> Copying the region the new dabs
    /// occupy out of the shared scratch brings the older dabs overlapping it
    /// forward too, and those are the pixels the pass has already finished —
    /// hard seams between processed and raw, which is the third attempt that
    /// reached the owner and was called worse than the bug.
    /// </para>
    /// <para>
    /// <b>Per publish, not per pointer event.</b> The per-event path carries the
    /// settled-prefix cut and the tail lend-and-take-back; this is only ever read
    /// when a frame is built.
    /// </para>
    /// </remarks>
    private SKBitmap? BuildLiveTip()
    {
        if (_strokeBuilder.Current is not { } stroke || _live.Dabs is not { Count: > 0 } dabs)
        {
            // Between strokes: forget where the last one had got to, or the
            // first publish of the next would report its whole dab list as new.
            _lastPublishDabs = -1;
            _live.TipUsed = null;
            return null;
        }

        // Before every early return below, because the refused publishes are the
        // fast strokes and they are what attempt 6 needs to know about.
        if (_lastPublishDabs >= 0 && dabs.Count >= _lastPublishDabs)
        {
            LiveTipNewDabs.Add(dabs.Count - _lastPublishDabs);
        }

        _lastPublishDabs = dabs.Count;

        if (_live.PostScratch is null) { _live.TipUsed = null; return null; }

        // Q168, 2026-08-27: the effects whose raw tip breaks live-matches-committed
        // keep today's behaviour. The owner's call over a recommendation to show
        // the tip everywhere and compare after the pass had landed.
        if (BrushEngine.LiveTipWouldDivergeTooFar(stroke.Brush)) { _live.TipUsed = null; return null; }

        // **PostStampedDabs, not PostStampedCount.** The older field is points on
        // one path and dabs on the other, so subtracting it from a dab count
        // answers a question nobody asked — and answered it as "the whole
        // stroke", which is what the fourth attempt then restamped every
        // publish. See LivePaintSession.PostStampedDabs and B329.
        // The cost of a dab, measured on this brush rather than assumed: the
        // budget is a time and this is what converts it into dabs. Zero until
        // something has been stamped, which LiveTipPlan reads as "be generous".
        var perDabMs = LiveTipDabsStamped.MedianMs > 0
            ? LiveTipStampMs.MedianMs / LiveTipDabsStamped.MedianMs
            : 0;
        var (range, planStampFrom, why, outstanding) = Rendering.LiveTipPlan.For(
            _live.PostStampedDabs, dabs.Count, _live.TipFrom, _live.TipStampedTo, perDabMs);
        if (outstanding > 0) LiveTipOutstanding.Add(outstanding);
        if (why == Rendering.LiveTipPlan.Skip.TooFarBehind) LiveTipTooFarBehind++;
        if (why == Rendering.LiveTipPlan.Skip.NoPassYet) LiveTipNoPass++;
        if (range is not { } plan)
        {
            _live.ResetTip();
            return null;
        }

        var info = new SKImageInfo(
            Scene.Width, Scene.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        // **Add what arrived, rebuild only when the pass moved** (attempt 6).
        // The tip keeps its contents between publishes, so an ordinary publish
        // stamps the difference rather than the whole outstanding run. A pass
        // completing is the one thing that invalidates what is already there —
        // those dabs are in the processed body now, and leaving them in the tip
        // would draw raw ink over finished pixels, which is the artifact three
        // earlier attempts produced.
        // The plan already decided this — it had to, because the budget is now
        // about what THIS publish stamps and that depends on whether the buffer
        // can be added to. Keeping the decision in one place is what stops the
        // two from disagreeing about which dabs the buffer holds.
        var canAdd = planStampFrom > plan.From && _live.TipScratch is not null;

        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var stampedFrom = canAdd ? planStampFrom : plan.From;
        var canvas = canAdd ? _live.ContinueTip() : _live.BeginTip(info.Width, info.Height);
        if (canvas is null) return null;
        try
        {
            if (stampedFrom < plan.To)
            {
                BrushEngine.StampDabRange(canvas, stroke, dabs, stampedFrom, plan.To);
                canvas.Flush();
            }
        }
        finally
        {
            canvas.Dispose();
        }

        LiveTipStampMs.Add(
            (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

        // Counted apart, because the saving this attempt exists for is entirely
        // in how often each happens — and the report's prediction of it (1.21 ms
        // against 5.11) assumed every publish was an addition and ignored these.
        var stampedNow = plan.To - stampedFrom;
        if (canAdd) { LiveTipAdded++; LiveTipDabsAdded.Add(stampedNow); }
        else { LiveTipRebuilt++; LiveTipDabsRebuilt.Add(stampedNow); }
        LiveTipDabsStamped.Add(stampedNow);

        _live.TipFrom = plan.From;
        _live.TipStampedTo = plan.To;

        // From plan.From and not from stampedFrom: the tip holds everything back
        // to the pass's position, so the rectangle drawn from it has to cover
        // all of that. Bounding it to this publish's addition alone would clip
        // the older part of the tip off the screen every publish — a mark that
        // flickers down to its newest dabs, which is worse than the bug.
        _live.TipUsed = BrushEngine.RangeBounds(dabs, plan.From, stroke.Brush, info);
        if (_live.TipUsed is null) return null;

        LiveTipDrawn++;
        return _live.TipScratch;
    }
}
