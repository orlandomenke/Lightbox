using CommunityToolkit.Mvvm.ComponentModel;

namespace Lightbox.App.Services;

/// <summary>
/// Watches how long the app actually takes to repaint the canvas and turns
/// that into a headroom figure the artist can act on.
///
/// The number is measured, not guessed: a publish is the work between a
/// pointer event and pixels being ready, so the share of a 60 fps frame it
/// eats is exactly the headroom left for drawing. When it runs out, the
/// advice names the biggest thing in the document worth changing.
/// </summary>
public sealed partial class PerformanceMonitor : ObservableObject
{
    /// <summary>One 60 fps frame. Publishes comfortably under this feel instant.</summary>
    private const double FrameBudgetMs = 16.7;

    private readonly double[] _samples = new double[30];
    private int _count;
    private int _next;

    private readonly double[] _frames = new double[30];
    private int _frameCount;
    private int _frameNext;

    /// <summary>Median milliseconds of the recent canvas repaints.</summary>
    [ObservableProperty]
    private double _publishMs;

    /// <summary>
    /// Median milliseconds the render thread spends putting a frame on screen.
    /// Measured separately because it is not the same work: compositing runs
    /// once per edit, this runs once per displayed frame, and on a large
    /// document it is usually the larger of the two. Reporting headroom from
    /// compositing alone said "smooth" while the canvas ran at 34 fps.
    /// </summary>
    [ObservableProperty]
    private double _frameMs;

    /// <summary>
    /// 100 = repaints are effectively free, 0 = each one blows well past a
    /// frame. Derived from the median so one slow publish doesn't panic.
    /// </summary>
    [ObservableProperty]
    private int _headroomPercent = 100;

    /// <summary>Short label for the info strip ("Smooth", "Heavy"…).</summary>
    [ObservableProperty]
    private string _healthLabel = "Smooth";

    /// <summary>What to do about it, or empty when there's nothing to fix.</summary>
    [ObservableProperty]
    private string _advice = "";

    /// <summary>True once performance is worth the artist's attention.</summary>
    [ObservableProperty]
    private bool _needsAttention;

    /// <summary>
    /// Whether enough has been measured for the numbers to mean anything.
    /// </summary>
    /// <remarks>
    /// The first repaints of a session pay for JIT, the first surfaces and a
    /// cold frame cache, so a headroom figure taken from three samples says
    /// "struggling" on a machine that is fine. Anything that <em>acts</em> on
    /// the measurement has to wait for this; anything that merely displays it
    /// can show the number as it settles.
    /// </remarks>
    public bool HasSettled => _count >= 10 && _frameCount >= 10;

    public void RecordPublish(double milliseconds)
    {
        _samples[_next] = milliseconds;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
        Recompute();
    }

    /// <summary>One completed frame on the render thread, in milliseconds.</summary>
    public void RecordFrame(double milliseconds)
    {
        _frames[_frameNext] = milliseconds;
        _frameNext = (_frameNext + 1) % _frames.Length;
        if (_frameCount < _frames.Length) _frameCount++;
        Recompute();
    }

    /// <summary>
    /// One whole repaint, in the parts the view model times: describing the
    /// frame (where a drawing missing from the cache is rendered), compositing
    /// it, and handing it over — plus how many drawings it had to render and how
    /// many the cache threw out to make room.
    /// </summary>
    public readonly record struct BuildSample(
        double TotalMs, double DescribeMs, double ComposeMs, double HandoffMs,
        long Misses, long Evictions, double AtSeconds);

    /// <summary>A repaint longer than this is a pause the artist sees, not a slow frame.</summary>
    public const double FreezeMs = 100;

    private const double WindowSeconds = 60;
    private const int WindowCap = 600;
    private readonly Queue<BuildSample> _builds = new();

    /// <summary>
    /// The advice reads these, never a guess about the document (the owner's
    /// report, 2026-10-07: "11 layers at 2.1 MP — merging finished layers frees
    /// the most" on a document whose time, measured, went on re-rendering
    /// drawings — which merging would not have touched).
    /// </summary>
    public void RecordBuild(BuildSample sample)
    {
        _builds.Enqueue(sample);
        while (_builds.Count > WindowCap
               || (_builds.Count > 0 && sample.AtSeconds - _builds.Peek().AtSeconds > WindowSeconds))
        {
            _builds.Dequeue();
        }
        FreezesLastMinute = _builds.Count(b => b.TotalMs > FreezeMs);
        Recompute();
    }

    /// <summary>Repaints over <see cref="FreezeMs"/> in the last minute — what "it stalls" counts.</summary>
    [ObservableProperty]
    private int _freezesLastMinute;

    /// <summary>Forget the timings — call when the document changes size.</summary>
    public void Reset()
    {
        _builds.Clear();
        FreezesLastMinute = 0;
        _count = 0;
        _next = 0;
        _frameCount = 0;
        _frameNext = 0;
        PublishMs = 0;
        FrameMs = 0;
        HeadroomPercent = 100;
        HealthLabel = "Smooth";
        Advice = "";
        NeedsAttention = false;
    }

    /// <summary>
    /// Document facts the advice draws on. Called when the document changes,
    /// not per frame.
    /// </summary>
    public void DescribeDocument(int width, int height, int layerCount, int drawingCount, long bytes)
    {
        _width = width;
        _height = height;
        _layerCount = layerCount;
        _drawingCount = drawingCount;
        _bytes = bytes;
        Recompute();
    }

    private int _width = 960;
    private int _height = 540;
    private int _layerCount = 1;
    private int _drawingCount = 1;
    private long _bytes;

    private static double Median(double[] buffer, int count)
    {
        if (count == 0) return 0;
        var window = new double[count];
        Array.Copy(buffer, window, count);
        Array.Sort(window);
        return window[count / 2];
    }

    private void Recompute()
    {
        if (_count == 0 && _frameCount == 0) return;
        PublishMs = Median(_samples, _count);
        FrameMs = Median(_frames, _frameCount);

        // Whichever stage is slower is what the artist feels: compositing runs
        // once per edit, presenting runs once per frame, and either can be the
        // one that drops the stroke behind the pen.
        var worst = Math.Max(PublishMs, FrameMs);

        // A stage at a quarter of the frame budget still leaves plenty of room
        // for input, stamping and the UI, so that counts as full marks; from
        // there it falls off to zero at four frames.
        var ratio = worst / FrameBudgetMs;
        var score = ratio <= 0.25 ? 1.0
            : ratio >= 4 ? 0.0
            : 1.0 - (ratio - 0.25) / (4 - 0.25);
        HeadroomPercent = (int)Math.Round(Math.Clamp(score, 0, 1) * 100);

        (HealthLabel, NeedsAttention) = HeadroomPercent switch
        {
            >= 70 => ("Smooth", false),
            >= 40 => ("Busy", false),
            >= 20 => ("Heavy", true),
            _ => ("Struggling", true),
        };
        // Pauses are worth the artist's attention whatever the median says: the
        // median is what most repaints cost, and a pause is the one that is not.
        if (FreezesLastMinute >= 3) NeedsAttention = true;
        Advice = NeedsAttention ? MeasuredAdvice() : "";
    }

    /// <summary>
    /// What the time went on, from measurements only — and a remedy only where
    /// it is known to help that cause.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It used to guess.</b> More than six layers meant "merge finished
    /// layers", whatever was actually slow — and it was fed the compositing
    /// time alone, so a repaint that spent seconds rendering drawings the cache
    /// did not hold reached it as a few milliseconds of compositing, and the
    /// only fact left to blame was the layer count.
    /// </para>
    /// <para>
    /// Now each cause is named by its share of the slow repaints of the last
    /// minute: <b>displaying</b> (the frame time, rescaling the canvas to the
    /// window), <b>re-rendering drawings</b> (describe time with cache misses in
    /// it), <b>compositing</b> (compose time). A cause under half the time is not
    /// named; the honest answer then is how slow, and where to look.
    /// </para>
    /// </remarks>
    private string MeasuredAdvice()
    {
        var megapixels = _width * (double)_height / 1_000_000;
        var pauses = FreezesLastMinute > 0
            ? $"{FreezesLastMinute} pause{(FreezesLastMinute == 1 ? "" : "s")} over {FreezeMs / 1000:0.#} s in the last minute. "
            : "";

        // Presenting the canvas costs more than composing it: the document is
        // rescaled to the window on every frame, which no amount of editing
        // efficiency can offset. Measured on the render thread.
        if (FrameMs > PublishMs * 2 && FrameMs > FrameBudgetMs)
        {
            return pauses + $"Displaying the {megapixels:0.#} MP canvas costs {FrameMs:0} ms a frame — " +
                   "lower Canvas quality while drawing, or zoom in to work on part of it.";
        }

        var slow = _builds.Where(b => b.TotalMs > FrameBudgetMs).ToList();
        if (slow.Count == 0)
        {
            return pauses + $"Repaints take {PublishMs:0} ms — Help ▸ Write a render report shows where.";
        }
        var total = slow.Sum(b => b.TotalMs);
        var rendering = slow.Where(b => b.Misses > 0).Sum(b => b.DescribeMs);
        var compositing = slow.Sum(b => b.ComposeMs);

        if (rendering >= total / 2)
        {
            var drawings = slow.Sum(b => b.Misses);
            var evicted = _builds.Sum(b => b.Evictions);
            var what = $"{pauses}{rendering / 1000:0.#} s went on rendering {drawings} drawing{(drawings == 1 ? "" : "s")} " +
                       "the cache did not hold";
            // Only a full cache has a remedy: a cold one is the first view of
            // those drawings, which idle time already prepares.
            return evicted > 0
                ? what + " — the frame cache is full; raising it in Configure ▸ Performance keeps them."
                : what + " — the first look at them; pausing lets Lightbox prepare the rest.";
        }

        if (compositing >= total / 2)
        {
            var median = Median(slow.Select(b => b.ComposeMs).ToArray(), slow.Count);
            return pauses + $"Compositing {_layerCount} layers takes {median:0} ms a repaint — " +
                   "merging finished layers or a lower Canvas quality cuts it.";
        }

        return pauses + $"Repaints take {Median(slow.Select(b => b.TotalMs).ToArray(), slow.Count):0} ms when slow, " +
               "with no single cause — Help ▸ Write a render report shows where.";
    }
}
