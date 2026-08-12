using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>What a warmed frame is wanted as.</summary>
public enum WarmProduct
{
    /// <summary>A document-sized bitmap, for <see cref="FrameBitmapCache"/>.</summary>
    Bitmap,

    /// <summary>A sparse tile store and its pyramid, for <see cref="TileFrameCache"/>.</summary>
    Tiles,
}

/// <summary>One frame the playhead is about to need.</summary>
public readonly record struct WarmRequest(
    Frame Frame, int Width, int Height, int CelIndex, WarmProduct Want, int Level = 0)
{
    /// <summary>
    /// What this job would produce, as a string — so two requests for the same
    /// pixels can be recognised as one.
    /// </summary>
    /// <remarks>
    /// Mirrors how each cache keys its own entries: tiles by frame alone, since
    /// a tile store is the frame's ink at document coordinates; bitmaps by size
    /// and cel index too, because the same frame is cached separately at each
    /// size, and a frame that places a symbol draws differently at each
    /// exposure.
    /// </remarks>
    public string Key => Want == WarmProduct.Tiles
        ? $"t:{Frame.Id}"
        : $"b:{Frame.Id}@{Width}x{Height}#{CelIndex}";
}

/// <summary>
/// Pixels rendered off the UI thread, waiting to be taken into a cache. The
/// holder owns them until a cache says it took them.
/// </summary>
public sealed class Warmed : IDisposable
{
    public required WarmRequest Request { get; init; }

    public SKBitmap? Bitmap { get; init; }

    public TileStore? Store { get; init; }

    public TilePyramid? Pyramid { get; init; }

    internal int Generation { get; init; }

    public void Dispose()
    {
        Bitmap?.Dispose();
        // Pyramid first: its level 0 IS the store, which it does not own, so
        // disposing the store first would leave the pyramid walking freed tiles.
        Pyramid?.Dispose();
        Store?.Dispose();
    }
}

/// <summary>
/// Rasterizes the frames playback is about to show, on a background thread, so
/// the tick that shows them finds pixels instead of making them.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this exists for: the first pass through a scene rasterizes on
/// the UI thread, one frame per tick.</b> Every playback measurement in
/// <c>BUGS.md</c> is taken on the second pass, which excludes it by
/// construction — so the cost an artist meets first was the one nothing was
/// watching. It is not small: a frame that misses the cache is a full replay of
/// its stroke record, and it lands *inside* the timer tick, between the
/// playhead moving and the frame being published. Nothing else on the UI thread
/// runs while it does.
/// </para>
/// <para>
/// <b>Safe off-thread because rendering is a pure function of the record.</b>
/// Invariant 2 forbids randomness anywhere in it — every dab dynamic is seeded
/// from geometry through <c>Hash01</c> — so the same strokes produce the same
/// pixels whichever thread replays them, in whatever order, at whatever time.
/// That is the property that makes this a scheduling change and not a rendering
/// one: nothing here can alter a pixel, only when it is computed. If a future
/// change made a render depend on shared mutable state, this class would break
/// loudly and correctly, and the fix would belong to that change.
/// </para>
/// <para>
/// <b>One worker, not a pool.</b> Parallel rasterization was measured and
/// rejected for this shape of work (B29: layer-stack caching is flat across 1–24
/// layers but breaks even only around three, and a miss is worse than doing
/// nothing at all). A second raster thread also competes with the UI thread for
/// the cores that have to composite and present — buying frame N+2 by delaying
/// frame N is the trade this class exists to refuse.
/// </para>
/// <para>
/// <b>No dispatcher, on purpose.</b> Finished work is queued and taken by the UI
/// thread at the top of the next publish, rather than posted to it. Playback
/// publishes every frame, so results land exactly when they are wanted; and a
/// queue drained by the consumer cannot storm the dispatcher the way a poster
/// can when the worker is faster than the timer. It also makes the whole class
/// testable without a dispatcher, which the headless suite does not have in any
/// form that would tell the truth.
/// </para>
/// <para>
/// <b>Superseded work is thrown away rather than finished.</b> A generation
/// counter is bumped by <see cref="Flush"/>, which the view model calls from the
/// same funnel that invalidates the caches. A result computed against a document
/// that has since changed is disposed on arrival — never installed. Speculative
/// work is allowed to be wasted; it is never allowed to be wrong.
/// </para>
/// <para>
/// <b>An artist drawing while the sequence plays is the one real race, and it is
/// closed twice.</b> Committing a stroke appends to the very
/// <c>List&lt;Stroke&gt;</c> the worker may be walking, so the walk can throw —
/// which is caught here and costs one unwarmed frame, nothing more. Whichever
/// side wins, the commit bumps the generation, so a result rendered from either
/// version of the record is discarded rather than installed. The failure mode is
/// a frame rendered on the tick the way it always was, which is the correct
/// thing for a speculative render to degrade into.
/// </para>
/// </remarks>
public sealed class FramePrewarmer : IDisposable
{
    /// <summary>
    /// How many playhead positions ahead to warm.
    /// </summary>
    /// <remarks>
    /// Small on purpose. One is not enough to cover a tick that arrives while
    /// the worker is still on the previous frame; many would have the worker
    /// racing ahead of the playhead, filling the cache with frames that are not
    /// wanted yet and — under the byte budget — leaving no room for the ones
    /// that are. Three positions is roughly a quarter of a second at 12 fps,
    /// which is the horizon over which the guess is still reliably right.
    /// </remarks>
    public const int Lookahead = 3;

    private readonly Lock _gate = new();
    private readonly Queue<WarmRequest> _pending = new();
    private readonly Queue<Warmed> _ready = new();

    private int _generation;
    private bool _running;
    private bool _disposed;

    /// <summary>The key of the job the worker is on, so it is not asked for twice.</summary>
    private string? _inFlight;

    /// <summary>Frames rasterized off-thread and offered to the caches.</summary>
    public int Rendered { get; private set; }

    /// <summary>Frames rasterized whose document changed before they arrived.</summary>
    public int Superseded { get; private set; }

    /// <summary>Frames a cache actually took.</summary>
    public int Installed { get; private set; }

    /// <summary>
    /// Frames finished but not taken — and the number that says whether this is
    /// working.
    /// </summary>
    /// <remarks>
    /// The ordinary reason a cache refuses a warm is that it already holds the
    /// frame, which means the tick got there first: the worker is slower than
    /// the frame period and is rendering, on a second core, exactly what the UI
    /// thread is rendering anyway. A few of those are the cost of a prediction.
    /// A run where refusals dominate installs is prewarming losing its race, and
    /// it is the one thing here that would be worth acting on rather than
    /// tuning — so it is counted rather than inferred.
    /// </remarks>
    public int Refused { get; private set; }

    /// <summary>Frames whose rasterization threw. Never fatal — the tick renders it.</summary>
    public int Failed { get; private set; }

    /// <summary>Whether anything is queued or in flight.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate) return _running || _pending.Count > 0;
        }
    }

    /// <summary>
    /// Ask for these frames, in this order, replacing anything still queued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replacing rather than appending is the point: the queue is a guess about
    /// where the playhead is going, and the moment it moves, the old guess is
    /// worth less than the new one. A queue that accumulated would spend the
    /// worker on frames the playhead has already passed.
    /// </para>
    /// <para>
    /// <b>What is already in hand is not requeued, and that is not a
    /// micro-optimisation.</b> A caller decides what to warm by asking the
    /// caches what they hold — and a frame the worker has already rendered is
    /// not in a cache yet; it is sitting in the finished queue waiting for the
    /// next publish to take it. Without this, every publish between a render
    /// and its installation asks for that frame again, and the worker renders
    /// it again. Measured on a two-layer, four-frame document: six renders
    /// where three were wanted, half of them thrown away on arrival because the
    /// cache had meanwhile been given the first copy.
    /// </para>
    /// </remarks>
    public void Request(IReadOnlyList<WarmRequest> jobs)
    {
        lock (_gate)
        {
            if (_disposed) return;

            var accountedFor = new HashSet<string>();
            if (_inFlight is { } busy) accountedFor.Add(busy);
            foreach (var done in _ready) accountedFor.Add(done.Request.Key);

            _pending.Clear();
            foreach (var job in jobs)
            {
                if (accountedFor.Add(job.Key)) _pending.Enqueue(job);
            }
            if (_running || _pending.Count == 0) return;
            _running = true;
        }
        Task.Run(Work);
    }

    /// <summary>
    /// Take everything finished since the last call. The installer returns true
    /// if it took ownership; anything it refuses is disposed here.
    /// </summary>
    /// <returns>How many were installed.</returns>
    public int Drain(Func<Warmed, bool> install)
    {
        var took = 0;
        while (true)
        {
            Warmed warmed;
            lock (_gate)
            {
                if (_ready.Count == 0)
                {
                    Installed += took;
                    return took;
                }
                warmed = _ready.Dequeue();
            }

            var taken = false;
            try
            {
                taken = install(warmed);
            }
            finally
            {
                if (!taken)
                {
                    warmed.Dispose();
                    Refused++;
                }
            }
            if (taken) took++;
        }
    }

    /// <summary>
    /// Abandon queued and finished work: the document it was rendered from is no
    /// longer the document.
    /// </summary>
    public void Flush()
    {
        List<Warmed> drop;
        lock (_gate)
        {
            _generation++;
            _pending.Clear();
            // Whatever is rendering will be discarded on arrival by the
            // generation check, so it no longer accounts for anything: a caller
            // asking for that frame again after the flush wants a fresh render,
            // not the one being thrown away.
            _inFlight = null;
            drop = [.. _ready];
            _ready.Clear();
        }
        foreach (var warmed in drop) warmed.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        Flush();
    }

    /// <summary>
    /// The frames the playhead will visit next, by the same arithmetic
    /// <c>MainViewModel.StepPlayback</c> uses.
    /// </summary>
    /// <remarks>
    /// Restated rather than shared because <c>StepPlayback</c> also stops at the
    /// end of a non-looping range, resynchronises audio and moves the playhead —
    /// none of which a prediction may do. <c>ThePredictionAgreesWithStepPlayback</c>
    /// drives the real one and compares, so the restatement cannot drift
    /// silently.
    /// </remarks>
    public static IReadOnlyList<int> Upcoming(
        int current, int direction, int start, int end, bool loop, int count)
    {
        var result = new List<int>(Math.Max(0, count));
        var at = current;
        for (var i = 0; i < count; i++)
        {
            var next = at + direction;
            if (next > end || next < start)
            {
                // A range that does not loop simply ends; there is nothing
                // further to warm and guessing past it would warm frames the
                // artist is not about to see.
                if (!loop) break;
                next = next > end ? start : end;
            }
            at = next;
            result.Add(at);
        }
        return result;
    }

    private void Work()
    {
        while (true)
        {
            WarmRequest job;
            int generation;
            lock (_gate)
            {
                if (_disposed || _pending.Count == 0)
                {
                    _running = false;
                    _inFlight = null;
                    return;
                }
                job = _pending.Dequeue();
                generation = _generation;
                _inFlight = job.Key;
            }

            Warmed? made;
            try
            {
                made = Render(job, generation);
            }
            catch
            {
                // A prewarm that throws must never take the application down or
                // stop the queue: the frame simply stays uncached and the tick
                // renders it the way it always did.
                made = null;
            }

            lock (_gate)
            {
                _inFlight = null;
                if (made is null)
                {
                    Failed++;
                    continue;
                }
                if (_disposed || made.Generation != _generation)
                {
                    made.Dispose();
                    Superseded++;
                    continue;
                }
                _ready.Enqueue(made);
                Rendered++;
            }
        }
    }

    private static Warmed Render(WarmRequest job, int generation)
    {
        if (job.Want == WarmProduct.Tiles)
        {
            var (store, pyramid) = TileFrameCache.RenderDetached(
                job.Frame, job.Width, job.Height, job.Level);
            return new Warmed
            {
                Request = job,
                Store = store,
                Pyramid = pyramid,
                Generation = generation,
            };
        }

        return new Warmed
        {
            Request = job,
            Bitmap = FrameBitmapCache.RenderDetached(
                job.Frame, job.Width, job.Height, celIndex: job.CelIndex),
            Generation = generation,
        };
    }

    /// <summary>
    /// Block until the queue drains. For tests, which need the worker's output
    /// to exist before they can assert anything about it.
    /// </summary>
    internal bool WaitForIdle(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (!IsBusy) return true;
            Thread.Sleep(1);
        }
        return !IsBusy;
    }
}
