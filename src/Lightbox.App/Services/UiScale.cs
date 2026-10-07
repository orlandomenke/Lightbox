namespace Lightbox.App.Services;

/// <summary>
/// How big the chrome is drawn: one factor for every panel, bar and menu, and
/// never for the canvas (Q200).
/// </summary>
/// <remarks>
/// <para>
/// <b>A layout transform, not a second density scale.</b> The sizes in
/// <c>Density.axaml</c> and the hundreds of literals in the views are all
/// written at 100%, and they stay that way: a region that scales is wrapped in
/// a <see cref="Lightbox.App.Controls.ScaledChrome"/>, which lays its content
/// out at 100% and draws it at this factor. Text is drawn as text at the new
/// size, so it stays sharp; nothing is resampled.
/// </para>
/// <para>
/// <b>The canvas is never inside one.</b> A transformed canvas would be
/// resampled — softer and slower — and at a factor like 125% its origin would
/// land between device pixels. It already has a zoom that does this job, on the
/// view-only side of invariant 5. <c>UiScaleTests</c> holds that line.
/// </para>
/// <para>
/// Static because it is: one person, one screen, one factor, read by controls
/// that are built long before any view model reaches them (a floating panel's
/// window, a popup).
/// </para>
/// </remarks>
public static class UiScale
{
    public const double Min = 0.75;
    public const double Max = 2.0;

    /// <summary>The setting moves in 5% steps — finer is invisible and only costs relayouts.</summary>
    public const double Step = 0.05;

    private static double _current = 1.0;

    /// <summary>The factor every <see cref="Lightbox.App.Controls.ScaledChrome"/> draws at.</summary>
    public static double Current
    {
        get => _current;
        set
        {
            var next = Normalise(value);
            if (next == _current) return;
            var old = _current;
            _current = next;
            foreach (var follower in Live()) follower.OnUiScaleChanged(old, next);
        }
    }

    /// <summary>
    /// Who is told when the factor changes — held <b>weakly</b>.
    /// </summary>
    /// <remarks>
    /// Not a static event, and that is B281's lesson rather than a taste: a
    /// static invocation list holds every subscriber's window alive, and the
    /// test suite builds thousands of windows it never closes. A follower that
    /// is collected simply drops out of the list.
    /// </remarks>
    private static readonly List<WeakReference<IFollowsUiScale>> Followers = [];

    public static void Follow(IFollowsUiScale follower)
    {
        if (Live().Contains(follower)) return;
        Followers.Add(new WeakReference<IFollowsUiScale>(follower));
    }

    public static void Unfollow(IFollowsUiScale follower) =>
        Followers.RemoveAll(w => !w.TryGetTarget(out var f) || ReferenceEquals(f, follower));

    /// <summary>The followers still alive, pruning the rest as it goes.</summary>
    private static List<IFollowsUiScale> Live()
    {
        var live = new List<IFollowsUiScale>(Followers.Count);
        Followers.RemoveAll(w =>
        {
            if (!w.TryGetTarget(out var f)) return true;
            live.Add(f);
            return false;
        });
        return live;
    }

    /// <summary>
    /// Clamp to the range and snap to the step. A settings file is input like
    /// any other: a hand-edited 40 or a NaN must come out as something the
    /// chrome can be drawn at, not as a window nobody can read.
    /// </summary>
    public static double Normalise(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return 1.0;
        var clamped = Math.Clamp(value, Min, Max);
        // Rounded to two places after snapping, so 100% is exactly 1.0 and
        // the "is it scaled at all" test can compare rather than tolerate.
        return Math.Round(Math.Round(clamped / Step) * Step, 2);
    }
}

/// <summary>Something that redraws or resizes when the interface scale changes.</summary>
public interface IFollowsUiScale
{
    void OnUiScaleChanged(double old, double now);
}
