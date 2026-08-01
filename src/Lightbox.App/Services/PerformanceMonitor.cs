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

    /// <summary>Forget the timings — call when the document changes size.</summary>
    public void Reset()
    {
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
        Advice = NeedsAttention ? AdviceForDocument() : "";
    }

    private string AdviceForDocument()
    {
        var megapixels = _width * (double)_height / 1_000_000;
        // Presenting the canvas costs more than composing it: the document is
        // being rescaled to the window on every frame, which no amount of
        // editing efficiency can offset.
        if (FrameMs > PublishMs * 2 && FrameMs > FrameBudgetMs)
        {
            return $"Displaying a {megapixels:0.#} MP canvas costs {FrameMs:0} ms a frame — " +
                   "lower Canvas quality while drawing, or zoom in to work on part of it.";
        }
        if (_layerCount > 6)
        {
            return $"{_layerCount} layers at {megapixels:0.#} MP — merging finished layers frees the most.";
        }
        if (megapixels > 8)
        {
            return $"{megapixels:0.#} MP canvas is large for this machine — work at a smaller size and scale up on export.";
        }
        if (_bytes > 600L * 1024 * 1024)
        {
            return $"{_bytes / (1024.0 * 1024 * 1024):0.0} GB of frames cached — close other documents or shorten the scene.";
        }
        if (_drawingCount > 200)
        {
            return $"{_drawingCount} drawings in memory — closing other documents frees room.";
        }
        return "Merging layers or reducing the canvas size will help.";
    }
}
