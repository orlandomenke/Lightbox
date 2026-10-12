using Lightbox.App.Services;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// Buffering before play: playback phase 4 of docs/DESIGN-playback-first-loop.md,
/// Q214's first decision ("buffer, then play").
/// </remarks>
public partial class MainViewModel
{
    private bool _isBufferingPlay;
    private double _playBufferProgress;
    private int _bufferDirection = 1;
    private int _bufferTotal;
    private int _bufferLeft;

    /// <summary>Set while a press of play is starting playback and must not buffer again.</summary>
    private bool _playWithoutBuffering;

    /// <summary>
    /// Play was pressed on a loop that is not ready: the playhead holds while
    /// the range is prepared, and playback starts at full speed once it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Q214: timing on screen is always true. Starting the clock on a cold loop
    /// rendered each drawing on the tick that reached it, so the first loop
    /// came late and uneven — on the lab's 30-layer document, frames never
    /// arrived at all. The owner (2026-10-11): a steady loop matters more than
    /// how soon it starts.
    /// </para>
    /// <para>
    /// The first cut of the design note's rule: play when every drawing in the
    /// range is held. Pressing play again, starting a stroke or stopping
    /// cancels. A range that cannot be held — larger than the budget — plays as
    /// it did before rather than waiting for ever.
    /// </para>
    /// </remarks>
    public bool IsBufferingPlay
    {
        get => _isBufferingPlay;
        private set
        {
            if (_isBufferingPlay == value) return;
            _isBufferingPlay = value;
            OnPropertyChanged();
        }
    }

    /// <summary>How much of the range was ready, 0 to 1, while buffering.</summary>
    public double PlayBufferProgress
    {
        get => _playBufferProgress;
        private set
        {
            if (Math.Abs(_playBufferProgress - value) < 0.0005) return;
            _playBufferProgress = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The drawings of the playback range not yet prepared, in the order the
    /// loop will play them — exactly what the playback warm would ask for.
    /// </summary>
    private List<WarmRequest> RangeStillToPrepare()
    {
        var tileNative = Scene.Camera is null
            && _publish.Viewport is { Width: > 0, Height: > 0 }
            && !Lightbox.Raster.EffectPasses.AnyLive(Scene);
        return PlaybackWarmJobs(PlaybackRangeInPlayOrder(), tileNative, ComposeScale);
    }

    /// <summary>
    /// Start buffering instead of playing, when the range is not ready. False
    /// means play now: the range is ready, or there is no way to deliver work
    /// done off this thread (the headless suite, which keeps the old path).
    /// </summary>
    private bool BufferBeforePlaying(int direction)
    {
        if (_playWithoutBuffering || ThumbnailWorker.Post is null || PaperHasMoved) return false;
        var jobs = RangeStillToPrepare();
        _prepareBlocked = false;
        var frames = FramesToStartLeft(direction);
        if (jobs.Count == 0 && frames == 0) return false;

        _bufferDirection = direction;
        // Two halves of one bar: the drawings as tiles, then the frames a play
        // shows first, finished (prepared playback frames).
        _bufferTotal = jobs.Count + Math.Max(frames, 1);
        _bufferLeft = jobs.Count;
        PlayBufferProgress = 0;
        IsBufferingPlay = true;
        _prewarm.Changed ??= OnPrewarmChanged;
        // Nothing is being shown, so every core but one is on the range.
        if (jobs.Count > 0) _prewarm.Request(jobs, FramePrewarmer.IdleWorkers);
        else PrepareForPlay();
        return true;
    }

    /// <summary>
    /// After a drain: how far the range has come, and play once it is ready.
    /// True while buffering, so the drain does not also start an idle warm that
    /// would replace the range's queue.
    /// </summary>
    /// <param name="refused">This drain took nothing in and a cache refused something: the range does not fit.</param>
    private bool ContinueBuffering(bool refused)
    {
        if (!IsBufferingPlay) return false;
        var left = RangeStillToPrepare();

        // The tiles are in: now the frames themselves, finished, so the first
        // lap is shown rather than built on its ticks. A frame that cannot be
        // prepared (not the tiled route, a camera) leaves the rest to the ticks.
        var frames = left.Count == 0 && !_prepareBlocked ? FramesToStartLeft(_bufferDirection) : 0;
        PlayBufferProgress = _bufferTotal == 0 ? 1
            : Math.Clamp(1 - (left.Count + frames) / (double)_bufferTotal, 0, 1);
        if (left.Count == 0 && frames > 0)
        {
            PrepareForPlay();
            return true;
        }

        // Ready; or the caches refuse what is left; or the workers finished a
        // round and nothing more came in — each of the last two means waiting
        // longer would not make it ready.
        var stuck = !_prewarm.IsBusy && left.Count >= _bufferLeft;
        if (left.Count == 0 || (refused && !_prewarm.IsBusy) || stuck)
        {
            IsBufferingPlay = false;
            _playWithoutBuffering = true;
            try
            {
                StartPlayback(_bufferDirection);
            }
            finally
            {
                _playWithoutBuffering = false;
            }
            return true;
        }

        // A round finished with more to do (an edit flushed it, or the queue
        // was superseded): ask again for what is left.
        if (!_prewarm.IsBusy) _prewarm.Request(left, FramePrewarmer.IdleWorkers);
        _bufferLeft = left.Count;
        return true;
    }

    /// <summary>Stop buffering without playing — play pressed again, a stroke, a stop.</summary>
    private void CancelBuffering()
    {
        if (!IsBufferingPlay) return;
        IsBufferingPlay = false;
        PlayBufferProgress = 0;
        StopPreparingFrames();
        // What is in hand finishes and is kept; nothing more is started.
        _prewarm.Request([], 1);
    }
}
