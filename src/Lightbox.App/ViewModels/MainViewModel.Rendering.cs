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
        // Any publish satisfies a deferred one — the snapshot it hands over is
        // built from the current state, which includes whatever the deferral
        // was waiting to show.
        _publish.WaitingForPresent = false;
        _publish.LastPublishTicks = System.Diagnostics.Stopwatch.GetTimestamp();

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
            Onion);
        var live = new ScenePassBuilder.LiveEdit(
            _live.Composite, _live.Scratch, _live.PostScratch, _live.PostStampedCount,
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
        // the tiled path) sees a shorter list of the same pixels.
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

        var seq = _publish.NextSequence();
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
        _publish.LastPublishClip = usedClip;
        _publish.LastPublished = fingerprint;
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

}
