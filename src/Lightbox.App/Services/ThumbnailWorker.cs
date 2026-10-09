using System.Collections.Concurrent;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// Renders the thumbnail-sized source of a drawing off the UI thread, and hands
/// it back to be installed in the frame cache.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Every thumbnail that was not cached was replayed from its strokes
/// on the UI thread, one drawing after another: opening the owner-shaped
/// document (64 drawings) froze for 5.9 s in <c>RefreshThumbnails</c> alone,
/// measured by the performance lab (Q209). A thumbnail is a view of the record,
/// not the record, so it may arrive a moment after the timeline appears — the
/// cell shows its previous picture, or none, until it does.
/// </para>
/// <para>
/// <b>Safe off the UI thread for the prewarmer's reasons</b>
/// (<see cref="FramePrewarmer"/>): rendering is a pure function of the stroke
/// record (<see cref="FrameBitmapCache.RenderDetached"/>), and anything that
/// changes a drawing bumps its generation through the same funnel that
/// invalidates its render. A result computed from a record that has since
/// changed is disposed on arrival, never installed — it would otherwise reach
/// the canvas too, through the cache they share. A walk that trips over a list
/// being appended to is caught and costs one thumbnail.
/// </para>
/// <para>
/// <b>Off unless the app turns it on</b> (<see cref="Post"/>). The headless test
/// suite keeps the synchronous path every existing thumbnail test was written
/// against; the background path has tests of its own.
/// </para>
/// </remarks>
public sealed class ThumbnailWorker : IDisposable
{
    /// <summary>
    /// How a finished render gets back to the UI thread, or null to render
    /// synchronously as before. Set once by the app at startup, through a window's
    /// own dispatcher — never the ambient static, from a worker thread (B93).
    /// </summary>
    public static Action<Action>? Post { get; set; }

    /// <summary>A drawing's thumbnail source, rendered and waiting to be installed.</summary>
    public sealed record Made(Frame Frame, int Width, int Height, double Scale, int Cel, SKBitmap Bitmap);

    private sealed record Job(
        Frame Frame, int Width, int Height, double Scale, int Cel, long Generation, SKPointI Origin);

    private readonly Action<Action> _post;
    private readonly Func<Made, bool> _install;
    private readonly Action _stale;
    private readonly BlockingCollection<Job> _queue = new();
    private readonly Dictionary<string, long> _generation = [];
    // Which generation each in-flight id was asked for, so a late result for an
    // older request cannot clear the guard of a newer one.
    private readonly Dictionary<string, long> _pending = [];
    private readonly Thread _thread;
    private long _flushes;
    private bool _disposed;

    /// <param name="post">Back to the UI thread.</param>
    /// <param name="install">
    /// On the UI thread: put a current render in place and refresh what shows
    /// it. Returns whether it was taken; a refused bitmap is disposed here.
    /// </param>
    /// <param name="stale">
    /// On the UI thread, when a result was thrown away: the drawing still needs a
    /// thumbnail, and nothing else would ask for it again until the next edit.
    /// </param>
    public ThumbnailWorker(Action<Action> post, Func<Made, bool> install, Action? stale = null)
    {
        _post = post;
        _install = install;
        _stale = stale ?? (() => { });
        _thread = new Thread(Work) { IsBackground = true, Name = "thumbnails", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>How many renders were thrown away for describing a record that had changed. For tests.</summary>
    public int Discarded { get; private set; }

    /// <summary>Ask for a drawing's source, unless one is already on its way. UI thread.</summary>
    /// <param name="origin">
    /// The paper's corner (B409), read by the caller on the thread that owns
    /// the document: this renders on a worker with no document in hand.
    /// </param>
    public void Request(Frame frame, int width, int height, double scale, int cel, SKPointI origin = default)
    {
        if (_disposed) return;
        var generation = GenerationOf(frame.Id);
        if (_pending.TryGetValue(frame.Id, out var asked) && asked == generation) return;
        _pending[frame.Id] = generation;
        _queue.Add(new Job(frame, width, height, scale, cel, generation, origin));
    }

    /// <summary>This drawing changed: anything rendered from before is stale. UI thread.</summary>
    public void Invalidate(string frameId)
    {
        _generation[frameId] = GenerationOf(frameId) + 1;
    }

    /// <summary>Every drawing changed. UI thread.</summary>
    public void Flush()
    {
        _flushes++;
        _generation.Clear();
    }

    // The flush count is folded in so a result begun before a Flush can never
    // match a generation counted from zero again after it.
    private long GenerationOf(string frameId) =>
        (_flushes << 32) + (_generation.TryGetValue(frameId, out var g) ? g : 0);

    private void Work()
    {
        foreach (var job in _queue.GetConsumingEnumerable())
        {
            // Everything is caught, per job: an exception that escaped here would
            // end the loop, the thread would die silently, and every thumbnail
            // asked for afterwards would never come (B397's review). A failed
            // render arrives as nothing, which re-asks.
            SKBitmap? bmp = null;
            try
            {
                bmp = FrameBitmapCache.RenderDetached(
                    job.Frame, job.Width, job.Height, job.Scale, job.Cel, job.Origin);
            }
            catch (Exception)
            {
                // Most likely the record changed under the walk.
            }
            var made = bmp;
            try
            {
                _post(() => Arrive(job, made));
            }
            catch (Exception)
            {
                made?.Dispose(); // the dispatcher is gone: the app is closing
                return;
            }
        }
    }

    private void Arrive(Job job, SKBitmap? bmp)
    {
        if (_pending.TryGetValue(job.Frame.Id, out var asked) && asked == job.Generation)
        {
            _pending.Remove(job.Frame.Id);
        }
        if (_disposed)
        {
            bmp?.Dispose();
            return;
        }
        if (bmp is null || job.Generation != GenerationOf(job.Frame.Id))
        {
            if (bmp is not null) Discarded++;
            bmp?.Dispose();
            // Thrown away, but the cell still wants a picture: without this it
            // stayed blank until the next edit happened to refresh thumbnails.
            _stale();
            return;
        }
        if (!_install(new Made(job.Frame, job.Width, job.Height, job.Scale, job.Cel, bmp))) bmp.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
    }
}
