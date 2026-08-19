using Avalonia.Input;

namespace Lightbox.App.Rendering;

/// <summary>
/// Reads the pen's tilt and estimates the hand's speed, for the two axes a
/// stroke can record beside pressure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separate from <c>CanvasControl</c> because the arithmetic is the part
/// that can be wrong.</b> Reading two properties off a pointer point is not
/// worth testing; deciding what counts as "fast", how quickly that estimate
/// should follow the hand, and what a device reporting nothing should store,
/// all are. Kept here they are testable without a window, a pen, or Windows —
/// none of which this repository has.
/// </para>
/// <para>
/// <b>Speed is computed once and then true forever</b> (Q113). Avalonia
/// delivers no per-point timestamps — verified against the shipped assembly,
/// not the documentation — so it cannot be recomputed at render, and a stroke
/// that stored it keeps drawing the same mark on reload, under undo and
/// through the inbetweener. A better estimator later changes new strokes only.
/// That is invariant 4's rule applied to time.
/// </para>
/// </remarks>
internal sealed class PenAxes
{
    /// <summary>
    /// The screen-space speed, in pixels per millisecond, that records as 1.0.
    /// </summary>
    /// <remarks>
    /// <b>Calibrated against the reporter's own traces rather than picked.</b>
    /// Their Huion delivers 90–207 pointer moves a second; at a brisk drawing
    /// pace that is a few pixels between samples, so a hand crossing roughly a
    /// pixel and a half per millisecond is at the top of the useful range. Fast
    /// enough that ordinary drawing lands mid-scale, rather than pinning at 1
    /// and making the axis useless.
    /// </remarks>
    internal const double FullSpeedPxPerMs = 1.5;

    /// <summary>
    /// How quickly the smoothed speed forgets, in milliseconds.
    /// </summary>
    /// <remarks>
    /// A raw sample-to-sample velocity is far too noisy to drive a mark — at
    /// 200 Hz a single dropped packet reads as a hand that stopped. Thirty
    /// milliseconds is about two frames: long enough to be steady, short
    /// enough that a deliberate flick still registers as one.
    /// </remarks>
    internal const double HalfLifeMs = 30;

    private double _smoothed;
    private bool _started;

    /// <summary>Whether this stroke has seen a device report a nonzero tilt.</summary>
    private bool _sawTilt;

    /// <summary>Begin a stroke: no history, no tilt seen yet.</summary>
    public void Begin()
    {
        _smoothed = 0;
        _started = false;
        _sawTilt = false;
    }

    /// <summary>
    /// The tilt to record for a point, or null when there is none to record.
    /// </summary>
    /// <param name="isPen">Only a pen has a tilt axis at all.</param>
    /// <param name="xTilt">Degrees from vertical along X, as delivered.</param>
    /// <param name="yTilt">Degrees from vertical along Y, as delivered.</param>
    /// <remarks>
    /// <b>A device that reports nothing gives 0,0 — which is also what a
    /// perfectly upright pen gives</b>, and the two must not be confused: one
    /// means "no information" and belongs out of the record, the other means
    /// "upright" and belongs in it. So tilt is recorded only once this stroke
    /// has seen a nonzero value, the same shape <c>_penHasReportedPressure</c>
    /// uses for the same reason. A stroke drawn dead upright by a tilt-capable
    /// pen therefore stores no tilt, which is the safe way round: absent reads
    /// as unknown, and a brush treats unknown as upright anyway.
    /// </remarks>
    public (double? X, double? Y) TiltFor(bool isPen, double xTilt, double yTilt)
    {
        if (!isPen) return (null, null);
        if (xTilt != 0 || yTilt != 0) _sawTilt = true;
        if (!_sawTilt) return (null, null);
        return (Round(xTilt), Round(yTilt));
    }

    /// <summary>One decimal, so the file carries no float noise.</summary>
    private static double Round(double degrees) => Math.Round(Math.Clamp(degrees, -90, 90), 1);

    /// <summary>
    /// The speed to record for a point: smoothed, normalized, clamped 0..1.
    /// </summary>
    /// <param name="dx">Screen-space movement since the previous sample.</param>
    /// <param name="dy">The same, vertically.</param>
    /// <param name="dtMs">
    /// Milliseconds since the previous sample. Non-positive means the samples
    /// share a timestamp — which coalesced points do — and the estimate holds
    /// rather than dividing by zero.
    /// </param>
    /// <remarks>
    /// The first sample of a stroke has no previous point to measure against,
    /// so it records <b>0</b> rather than a guess: a stroke starts from rest,
    /// and inventing a speed for its first dab would put a mark on the paper
    /// that the hand did not make.
    /// </remarks>
    public double SpeedFor(double dx, double dy, double dtMs)
    {
        if (dtMs <= 0) return Math.Clamp(_smoothed, 0, 1);

        var raw = Math.Sqrt(dx * dx + dy * dy) / dtMs / FullSpeedPxPerMs;
        if (!_started)
        {
            _started = true;
            _smoothed = 0;
            return 0;
        }

        // Exponential moving average expressed as a half-life, so the constant
        // above means something in milliseconds rather than in samples — the
        // report rate varies from 90 to 207 a second on the one machine this
        // was calibrated against, and a per-sample weight would mean something
        // different at each.
        var alpha = 1 - Math.Pow(0.5, dtMs / HalfLifeMs);
        _smoothed += alpha * (raw - _smoothed);
        return Math.Round(Math.Clamp(_smoothed, 0, 1), 3);
    }
}
