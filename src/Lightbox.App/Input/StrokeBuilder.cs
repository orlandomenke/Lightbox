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

    /// <summary>Whether this stroke keeps the tilt and speed the pen reports.</summary>
    /// <remarks>
    /// <b>Decided once, at <see cref="Begin"/>, from the brush this stroke was
    /// started with.</b> Deciding per sample would let the answer change under
    /// a stroke already in flight — switching brushes mid-drag is a real
    /// gesture — and half a stroke carrying tilt is worse than none of it: a
    /// curve would read the recorded half and the neutral default for the
    /// rest, and the mark would step.
    /// </remarks>
    private bool _keepTilt;

    private bool _keepSpeed;

    public bool IsActive => _stroke is not null;

    public Stroke? Current => _stroke;

    /// <param name="swatchId">
    /// The palette swatch the artist is painting with, or null for a literal
    /// colour. <paramref name="color"/> is recorded either way, so a stroke
    /// whose swatch is later deleted keeps the colour it was drawn in.
    /// </param>
    /// <param name="alwaysRecordAxes">
    /// Keep the pen's axes even when this brush reads none of them — the
    /// artist's override, so a stroke can be given a tilt curve later and
    /// actually respond to it. Ordinarily the brush decides
    /// (<see cref="PenAxisUse"/>) and an unused axis is never written.
    /// </param>
    public void Begin(
        ToolKind tool, string color, BrushSettings brush, double x, double y, double pressure,
        string? swatchId = null, bool alwaysRecordAxes = false)
    {
        _keepTilt = alwaysRecordAxes || PenAxisUse.NeedsTilt(brush);
        _keepSpeed = alwaysRecordAxes || PenAxisUse.NeedsSpeed(brush);
        _stroke = new Stroke
        {
            Tool = tool,
            Color = color,
            SwatchId = swatchId,
            Brush = brush.Clone(),
            Points = [new StrokePoint(x, y, pressure)],
        };
    }

    public void Add(double x, double y, double pressure) =>
        Add(x, y, pressure, null, null, null);

    /// <summary>
    /// Add a sample carrying the pen axes, keeping the ones this stroke wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The axes ride the point rather than a parallel list, so the distance
    /// filter above cannot drop a position while keeping its tilt — the
    /// alignment trap Q112 rejected parallel arrays to avoid.
    /// </para>
    /// <para>
    /// <b>The caller always supplies them and this decides what is kept.</b>
    /// Reading two properties off a pointer point is free; writing them into
    /// every point of every document is not — 113 bytes a point, measured, and
    /// 1.70× the saved file. So the control reports what the pen said and the
    /// record keeps what the brush can use.
    /// </para>
    /// </remarks>
    public void Add(double x, double y, double pressure, double? tiltX, double? tiltY, double? speed)
    {
        if (_stroke is null) return;
        var last = _stroke.Points[^1];
        var dx = x - last.X;
        var dy = y - last.Y;
        if (dx * dx + dy * dy < MinSampleDist * MinSampleDist) return;
        _stroke.Points.Add(_keepTilt || _keepSpeed
            ? new StrokePoint(
                x, y, pressure,
                _keepTilt ? tiltX : null,
                _keepTilt ? tiltY : null,
                _keepSpeed ? speed : null)
            : new StrokePoint(x, y, pressure));
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
