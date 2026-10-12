using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Playback from finished frames: a playing frame whose composite the cache
/// already holds is shown from it without being built (prepared playback
/// frames, step 1; docs/DESIGN-playback-first-loop.md).
/// </remarks>
public partial class MainViewModel
{
    /// <summary>
    /// The composite cache playback is served from: the process's own
    /// (<see cref="ComposeCacheHost.Shared"/>, which the canvas fills), or a
    /// test's.
    /// </summary>
    internal ComposeCache PlaybackComposites { get; set; } = ComposeCacheHost.Shared;

    /// <summary>Playing frames shown straight from the cache, without being built — for tests and the lab.</summary>
    internal int PlaybackFramesFromCache { get; private set; }

    /// <summary>
    /// Where each cached playing frame sits — the document size and the rectangle
    /// its image covers — recorded when it was published, since the route that
    /// decides it is only known after building the frame, which a hit skips.
    /// </summary>
    private readonly Dictionary<ComposeKey, (int Width, int Height, SKRectI? Covers)> _playbackPlacement = [];

    /// <summary>Remember where a keyed playing frame sits, for a later hit.</summary>
    private void NotePlaybackPlacement(ComposeKey key, int width, int height, SKRectI? covers)
    {
        // Bounded: the keys of a long session, not the cache's contents. Losing
        // them only costs a frame built the ordinary way.
        if (_playbackPlacement.Count > 4096) _playbackPlacement.Clear();
        _playbackPlacement[key] = (width, height, covers);
    }

    /// <summary>
    /// Show this playing frame from the cache if its composite is there: true
    /// when it was published, so the caller builds nothing.
    /// </summary>
    private bool TryShowPlayingFrameFromCache(FrameFingerprint fingerprint, Lightbox.Core.Documents.Scene scene)
    {
        if (!IsPlaying || _strokeBuilder.IsActive || _live.Composite is not null || _publish.AnythingDirty) return false;
        if (SnapshotChanged is not { } handler) return false;

        var key = new ComposeKey(fingerprint, _publish.RenderEpoch, _editor.Revision, StackStamp(scene, CurrentFrameIndex));
        if (!_playbackPlacement.TryGetValue(key, out var where)) return false;
        if (PlaybackComposites.Acquire(key) is not { } image) return false;

        var snapshot = RenderSnapshot.Borrowing(
            PlaybackComposites, image, key, where.Width, where.Height, _publish.NextSequence(), where.Covers);
        _publish.LastPublishClip = null;
        _publish.LastPublished = fingerprint;
        PlaybackFramesFromCache++;
        handler(snapshot);
        return true;
    }
}
