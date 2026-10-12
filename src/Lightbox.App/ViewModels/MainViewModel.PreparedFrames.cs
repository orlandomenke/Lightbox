using Lightbox.App.Rendering;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Prepared playback frames: frames ahead of the playhead are built on the UI
/// thread between refreshes, blended on a worker, and offered to the composite
/// cache under the key the tick asks with — so the first lap is shown from
/// finished composites as the second already is (docs/DESIGN-playback-first-loop.md).
/// </remarks>
public partial class MainViewModel
{
    /// <summary>How many frames ahead of the playhead are kept prepared while playing: a second at 24 fps.</summary>
    internal const int PrepareAhead = 24;

    /// <summary>Whether a frame is being prepared now — for tests that wait on it.</summary>
    internal bool IsPreparingFrames => _preparing;

    /// <summary>Frames prepared ahead and offered to the cache — for tests and the lab.</summary>
    internal int FramesPrepared { get; private set; }

    private bool _preparing;
    private int _prepareGeneration;

    /// <summary>A frame built for preparing: its blend still to do, and what to let go of after.</summary>
    private sealed record PlaybackFrameWork(
        ComposeKey Key, DeferredCompose Work, int Width, int Height, SKRectI? Covers, Action Release);

    /// <summary>The scale a composite is made at, at <paramref name="quality"/> and the current zoom.</summary>
    private double ScaleFor(CanvasQuality quality) => WorthScaling(quality switch
    {
        CanvasQuality.Full => Math.Clamp(_displayScale * 2.0, 0.125, 1.0),
        CanvasQuality.Half => Math.Clamp(_displayScale * 0.5, 0.125, 1.0),
        _ => Math.Clamp(_displayScale, 0.125, 1.0),
    });

    /// <summary>
    /// The scale a playing frame is made at — playback has a quality of its own,
    /// so a frame prepared while buffering, before the clock runs, must be made at
    /// the scale the tick will ask for, not the paused canvas's.
    /// </summary>
    private double PlayingScale => ScaleFor(PlaybackQuality ?? CanvasQuality);

    /// <summary>The key the tick will ask for frame <paramref name="index"/> with, as things stand.</summary>
    private ComposeKey PlaybackKeyFor(int index, Lightbox.Core.Documents.Scene scene) =>
        new(new FrameFingerprint(
                index, PlayingScale, _publish.Viewport,
                CameraViewTransform(PlayingScale),
                UnchangedLayerRun.VisibleLayers(scene)),
            _publish.RenderEpoch, _editor.Revision, StackStamp(scene, index));

    /// <summary>
    /// Build frame <paramref name="index"/> as a playing tick would, without
    /// publishing it: the same pass state, the same live edits, the same folds
    /// and the same tile flattening — so its blend is the tick's, byte for byte.
    /// Null when it cannot be prepared: not the tiled playback route, its tiles
    /// not all warm (building would rasterise on this thread), or already held.
    /// </summary>
    /// <param name="coldTiles">Build even with tiles missing, rendering them here as a tick would — for tests only.</param>
    private PlaybackFrameWork? BuildPlaybackFrame(int index, bool coldTiles = false)
    {
        var scene = Scene;
        if (index < 0 || index >= scene.FrameCount) return null;
        if (_strokeBuilder.IsActive || _live.Composite is not null || PaperHasMoved) return null;
        if (_publish.Viewport is not { Width: > 0, Height: > 0 } viewport) return null;
        // A camera maps the view itself; the tiled route is not what plays.
        var renderScale = PlayingScale;
        if (CameraViewTransform(renderScale) is not null) return null;
        // Every drawing at this frame held as tiles, or the flattening below
        // would render the missing ones here, on the UI thread.
        if (!coldTiles && PlaybackWarmJobs([index], tileNativeDoc: true, renderScale).Count > 0) return null;

        var key = PlaybackKeyFor(index, scene);
        if (PlaybackComposites.Holds(key)) return null;

        var passState = new ScenePassBuilder.State(
            index,
            scene.Layers.Count > 0 ? ActiveLayer.Id : null,
            IsPlaying: true, IsLightTable,
            HaveViewport: true,
            Onion,
            false,
            ThroughCamera: ViewThroughCamera,
            GhostsLater: false);
        var built = ScenePassBuilder.Describe(scene, passState, _cache, _tileFallbacks, LiveEditNow());
        if (!built.TileNative) return null;

        // The folds as the playing tick asks them: the stack fold declines while
        // playing, and the held run's prefix is the whole range's, so asking for
        // a frame not on screen serves the bake the tick serves.
        var passes = _stackBake.Fold(
            built, scene.Width, scene.Height, hold: true,
            _materializePass ??= MaterializePass, out _);
        var heldRun = UnchangedLayerRun.HeldPrefix(scene, EffectiveStartFrame, EffectiveEndFrame);
        passes = _stackBake.FoldHeldRun(passes, heldRun, scene.Width, scene.Height, out _);
        if (WornOverlayFor(scene, index) is { } worn) passes.Add(new RenderPass(worn, null, 1.0));

        var plan = ComposePlan.For(scene.Width, scene.Height, null, viewport, null, true, renderScale);
        if (plan.Route != ComposeRoute.Tiled) return null;

        var owned = new List<SKBitmap>();
        passes = FlattenTilePasses(passes, scene, viewport, renderScale, owned);
        var work = new DeferredCompose(
            passes, SceneRenderer.BackgroundOf(scene), renderScale, plan.Info, viewport, Tiled: true);
        return new PlaybackFrameWork(
            key, work, (int)plan.ViewWidth, (int)plan.ViewHeight, plan.ImageCovers, ReleaseFor(passes, owned));
    }

    /// <summary>Blend a built frame (any thread) — a CPU raster, as prepared frames are.</summary>
    private static SKImage? Blend(PlaybackFrameWork frame)
    {
        try
        {
            return frame.Work.Compose(null, null, out _);
        }
        catch
        {
            return null; // the tick composes it the ordinary way
        }
    }

    /// <summary>Give the cache a blended frame and let go of what it was built from (UI thread).</summary>
    private void Offer(PlaybackFrameWork frame, SKImage? image)
    {
        frame.Release();
        if (image is null) return;
        NotePlaybackPlacement(frame.Key, frame.Width, frame.Height, frame.Covers);
        if (PlaybackComposites.Offer(frame.Key, image, (long)image.Width * image.Height * 4)) FramesPrepared++;
        else image.Dispose();
    }

    /// <summary>Prepare frame <paramref name="index"/> here and now, on this thread — for tests.</summary>
    internal bool PrepareFrameNow(int index)
    {
        if (BuildPlaybackFrame(index, coldTiles: true) is not { } frame) return false;
        var image = Blend(frame);
        Offer(frame, image);
        return image is not null;
    }

    /// <summary>Set when a frame could not be prepared: buffering stops waiting for frames and plays.</summary>
    private bool _prepareBlocked;

    /// <summary>The frames a play would show first, in order: the playhead's, then up to a second on.</summary>
    private IEnumerable<int> FramesFromThePlayhead(int direction, bool includeCurrent)
    {
        int start = EffectiveStartFrame, end = EffectiveEndFrame;
        var length = end - start + 1;
        if (length < 1) yield break;
        var index = Math.Clamp(CurrentFrameIndex, start, end);
        if (includeCurrent) yield return index;
        for (var step = 0; step < Math.Min(PrepareAhead, length - 1); step++)
        {
            index += direction >= 0 ? 1 : -1;
            if (index > end) index = start;
            if (index < start) index = end;
            yield return index;
        }
    }

    /// <summary>The next frame within reach of the playhead that is not prepared, in play order; -1 if none.</summary>
    private int NextFrameToPrepare(bool includeCurrent = false)
    {
        var scene = Scene;
        foreach (var index in FramesFromThePlayhead(_playDirection, includeCurrent))
        {
            if (!PlaybackComposites.Holds(PlaybackKeyFor(index, scene))) return index;
        }
        return -1;
    }

    /// <summary>How many of the first frames a play would show are not prepared yet.</summary>
    private int FramesToStartLeft(int direction)
    {
        if (Services.ThumbnailWorker.Post is null) return 0;
        var scene = Scene;
        return FramesFromThePlayhead(direction, includeCurrent: true)
            .Count(index => !PlaybackComposites.Holds(PlaybackKeyFor(index, scene)));
    }

    /// <summary>
    /// Keep the frames ahead of the playhead prepared, one at a time: built at
    /// Background priority — behind every refresh and every input — blended on a
    /// worker, offered back here, then the next. Called after each playing
    /// publish; a chain already running is left to run.
    /// </summary>
    private void PrepareAheadWhilePlaying()
    {
        if (_preparing || !(IsPlaying || IsBufferingPlay)) return;
        if (Services.ThumbnailWorker.Post is not { } post) return;
        var generation = _prepareGeneration;
        _preparing = true;
        var later = Services.ThumbnailWorker.PostAtRest ?? post;
        later(() => PrepareNext(generation, post, later));
    }

    private void PrepareNext(int generation, Action<Action> post, Action<Action> later)
    {
        if (generation != _prepareGeneration || !(IsPlaying || IsBufferingPlay))
        {
            _preparing = false;
            return;
        }
        var index = NextFrameToPrepare(includeCurrent: IsBufferingPlay);
        if (index < 0)
        {
            _preparing = false;
            return;
        }
        if (BuildPlaybackFrame(index) is not { } frame)
        {
            // Cannot be prepared here: buffering stops waiting for frames.
            _preparing = false;
            _prepareBlocked = true;
            ContinueBuffering(refused: false);
            return;
        }
        Task.Run(() =>
        {
            var image = Blend(frame);
            post(() =>
            {
                Offer(frame, image);
                if (generation != _prepareGeneration || !(IsPlaying || IsBufferingPlay))
                {
                    _preparing = false;
                    return;
                }
                if (IsBufferingPlay && FramesToStartLeft(_bufferDirection) == 0)
                {
                    // Ready: buffering starts the clock, and the chain goes on
                    // ahead of the playhead from there.
                    _preparing = false;
                    ContinueBuffering(refused: false);
                    if (!IsPlaying) return;
                    _preparing = true;
                }
                later(() => PrepareNext(generation, post, later));
            });
        });
    }

    /// <summary>Stop preparing: playback stopped, or what would be prepared changed.</summary>
    private void StopPreparingFrames()
    {
        _prepareGeneration++;
        _preparing = false;
    }
}
