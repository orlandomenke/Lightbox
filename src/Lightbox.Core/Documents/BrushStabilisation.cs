namespace Lightbox.Core.Documents;

/// <summary>The stabilizer algorithms (raster input smoothing).</summary>
/// <remarks>
/// In Core rather than beside the filter that runs it, because a brush now
/// carries which one it wants and a brush is part of the document. The filter
/// itself stays in the app — it is stateful, it runs against live pointer
/// input, and none of that belongs in the record.
/// </remarks>
public enum SmoothingMode
{
    Off,

    /// <summary>Neighbor averaging of the finished stroke — the classic light touch-up.</summary>
    Laplacian,

    /// <summary>Centered moving average over a window — steady, rounds corners.</summary>
    MovingAverage,

    /// <summary>Exponential moving average, applied live — smooth with minimal lag.</summary>
    Ema,

    /// <summary>Lazy mouse: the brush trails the cursor on a string of fixed length.</summary>
    PulledString,

    /// <summary>Savitzky–Golay: polynomial fit that smooths without flattening peaks.</summary>
    SavitzkyGolay,
}

/// <summary>
/// How much a brush steadies the hand that is holding it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is per brush.</b> An inking brush wants heavy lazy-mouse so a
/// long confident line comes out clean; a pencil wants none, because the shake
/// <em>is</em> the texture and smoothing it makes roughs look dead. Those are
/// two different tools, and asking somebody to reach for the stabilizer control
/// every time they switch between them is asking them to remember a setting
/// instead of picking a brush. That is most of what stabilisation settings are
/// for, and one global value cannot express it.
/// </para>
/// <para>
/// <b>It is not a pixel setting, and invariant 4 does not apply.</b> Smoothing
/// filters the pointer samples <em>before</em> they become the stroke's points,
/// so what lands in the record is the smoothed path. Changing a brush's
/// stabilisation cannot alter a mark already drawn — there is nothing left to
/// re-run. That is why it can live on the brush without being cloned into every
/// stroke: the stroke already carries the result.
/// </para>
/// </remarks>
public sealed class BrushStabilisation
{
    public SmoothingMode Mode { get; set; } = SmoothingMode.Laplacian;

    /// <summary>Window size for moving average / Savitzky–Golay (odd, 3–25).</summary>
    public int Window { get; set; } = 7;

    /// <summary>EMA smoothing strength 0–0.95 (higher = smoother, laggier).</summary>
    public double Strength { get; set; } = 0.5;

    /// <summary>Pulled-string radius in document pixels.</summary>
    public double LazyRadius { get; set; } = 24;

    public BrushStabilisation Clone() => (BrushStabilisation)MemberwiseClone();
}
