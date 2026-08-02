using Lightbox.Core.Documents;

namespace Lightbox.App.Input;

/// <summary>
/// Accumulates pointer samples into a Stroke during a drag. Drops samples
/// closer than a small epsilon so slow mouse moves don't produce thousands
/// of coincident points.
/// </summary>
public sealed class StrokeBuilder
{
    private const double MinSampleDist = 0.75;

    private Stroke? _stroke;

    public bool IsActive => _stroke is not null;

    public Stroke? Current => _stroke;

    /// <param name="swatchId">
    /// The palette swatch the artist is painting with, or null for a literal
    /// colour. <paramref name="color"/> is recorded either way, so a stroke
    /// whose swatch is later deleted keeps the colour it was drawn in.
    /// </param>
    public void Begin(
        ToolKind tool, string color, BrushSettings brush, double x, double y, double pressure,
        string? swatchId = null)
    {
        _stroke = new Stroke
        {
            Tool = tool,
            Color = color,
            SwatchId = swatchId,
            Brush = brush.Clone(),
            Points = [new StrokePoint(x, y, pressure)],
        };
    }

    public void Add(double x, double y, double pressure)
    {
        if (_stroke is null) return;
        var last = _stroke.Points[^1];
        var dx = x - last.X;
        var dy = y - last.Y;
        if (dx * dx + dy * dy < MinSampleDist * MinSampleDist) return;
        _stroke.Points.Add(new StrokePoint(x, y, pressure));
    }

    /// <summary>Finish and return the stroke (null if it was never started).</summary>
    public Stroke? End()
    {
        var s = _stroke;
        _stroke = null;
        return s;
    }

    public void Cancel() => _stroke = null;
}
