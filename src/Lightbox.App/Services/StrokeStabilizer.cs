using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.Services;

/// <summary>
/// Input smoothing for hand-drawn strokes. Live modes (EMA, pulled string)
/// filter positions as they stream in — what's recorded is the smoothed
/// path; post modes (Laplacian, moving average, Savitzky–Golay) run once on
/// the finished stroke. Either way the FILTERED points are the stroke
/// record, so replay and inbetweens see exactly what was drawn.
/// </summary>
public sealed class StrokeStabilizer
{
    /// <summary>
    /// The settings this filter is running with. Replaced at the start of every
    /// stroke from whichever brush is in hand, so a brush that carries its own
    /// stabilisation gets it without anything else having to know.
    /// </summary>
    public BrushStabilisation Settings { get; set; } = new();

    public SmoothingMode Mode
    {
        get => Settings.Mode;
        set => Settings.Mode = value;
    }

    /// <summary>Window size for moving average / Savitzky–Golay (odd, 3–25).</summary>
    public int Window
    {
        get => Settings.Window;
        set => Settings.Window = value;
    }

    /// <summary>EMA smoothing strength 0–0.95 (higher = smoother, laggier).</summary>
    public double Strength
    {
        get => Settings.Strength;
        set => Settings.Strength = value;
    }

    /// <summary>Pulled-string radius in document pixels.</summary>
    public double LazyRadius
    {
        get => Settings.LazyRadius;
        set => Settings.LazyRadius = value;
    }

    private double _x;
    private double _y;
    private bool _active;

    /// <summary>The smoothed brush position while a live mode is filtering (gizmo anchor).</summary>
    public (double X, double Y)? BrushPosition =>
        _active && Mode is SmoothingMode.Ema or SmoothingMode.PulledString ? (_x, _y) : null;

    public void Begin(double x, double y)
    {
        _x = x;
        _y = y;
        _active = true;
    }

    public void End() => _active = false;

    /// <summary>Filter one live cursor sample into a brush position.</summary>
    public (double X, double Y) FilterLive(double x, double y)
    {
        if (!_active) return (x, y);
        switch (Mode)
        {
            case SmoothingMode.Ema:
            {
                var follow = 1 - Math.Clamp(Strength, 0, 0.95);
                _x += (x - _x) * follow;
                _y += (y - _y) * follow;
                return (_x, _y);
            }
            case SmoothingMode.PulledString:
            {
                var dx = x - _x;
                var dy = y - _y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist <= LazyRadius) return (_x, _y); // inside the dead zone: the string is slack
                var pull = (dist - LazyRadius) / dist;
                _x += dx * pull;
                _y += dy * pull;
                return (_x, _y);
            }
            default:
                _x = x;
                _y = y;
                return (x, y);
        }
    }

    /// <summary>Run the post filter (if any) on the finished stroke's points.</summary>
    public List<StrokePoint> PostProcess(List<StrokePoint> points) => Mode switch
    {
        SmoothingMode.Laplacian when points.Count >= 3 => GeometryOps.Smooth(points, 1),
        SmoothingMode.MovingAverage => StrokeFilters.MovingAverage(points, Window),
        SmoothingMode.SavitzkyGolay => StrokeFilters.SavitzkyGolay(points, Window),
        _ => points,
    };
}
