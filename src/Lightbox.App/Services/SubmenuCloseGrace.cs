using Avalonia;
using Avalonia.Controls;

namespace Lightbox.App.Services;

/// <summary>
/// Stops a submenu closing the instant it opened (B255).
/// </summary>
/// <remarks>
/// <para>
/// <b>The measurement this exists for.</b> On the reporter's Huion, the
/// driver's phantom mouse trades the pointer with the pen about forty times a
/// second, and Avalonia's <c>MenuItem</c> hover logic answers each handoff by
/// opening or closing a submenu. The fourth trace counted <b>2,763 popup opens
/// in 45 seconds — 61 a second, shortest life 0 ms</b> — and the UI thread
/// blocked for 29% of the session, with the popup rate before each stall over
/// 400 ms sitting at a median of 101/s.
/// </para>
/// <para>
/// <b>Why this rather than more of the last fix.</b> <c>OverlayPopups</c> made
/// each cycle about twenty times cheaper and took the worst freeze from 18.9 s
/// to 0.85 s — a real win, and the wrong axis. The rate then rose sixfold to
/// fill the space it freed, because nothing had touched *how often* the menu
/// was asked to change its mind. This caps the rate instead: at a 250 ms floor
/// a submenu can cycle four times a second rather than sixty.
/// </para>
/// <para>
/// <b>What it deliberately does not do.</b> It never forces a submenu *open*
/// and it never keeps one open against a real dismissal — a click elsewhere, a
/// key, the menu itself closing. It only refuses the close that arrives within
/// <see cref="GraceMs"/> of the open, which is the half of the cycle no artist
/// ever asked for. A menu that lingers a quarter of a second longer than the
/// pointer is not something anybody notices; one that strobes sixty times a
/// second is.
/// </para>
/// <para>
/// <b>The recursion guard is the whole risk.</b> Writing to
/// <see cref="MenuItem.IsSubMenuOpenProperty"/> from inside its own change
/// handler re-enters this method, so <see cref="_reopening"/> makes the
/// re-entrant write a no-op. Without it the property fights itself and the
/// thrash gets worse rather than better, which is the failure mode this class
/// is meant to end.
/// </para>
/// </remarks>
internal static class SubmenuCloseGrace
{
    /// <summary>
    /// How long a submenu is held open after opening.
    /// </summary>
    /// <remarks>
    /// Long enough to swallow a churn cycle whole — the measured handoff is
    /// sub-millisecond and the observed submenu lives were as short as 0 ms —
    /// and short enough that a deliberate move off the menu still feels
    /// immediate. Anything under about 200 ms leaves the rate high enough to
    /// keep the thread busy; much over 300 ms starts to feel sticky.
    /// </remarks>
    internal const double GraceMs = 250;

    private static readonly Dictionary<MenuItem, DateTime> OpenedAt = new();
    private static bool _reopening;
    private static bool _installed;
    private static long _refused;

    /// <summary>How many closes were refused, for the trace's report.</summary>
    internal static long Refused => Interlocked.Read(ref _refused);

    /// <summary>Whether the grace is watching menus at all.</summary>
    internal static bool Installed => _installed;

    /// <summary>
    /// Watch every menu item in the process, once.
    /// </summary>
    /// <remarks>
    /// A class handler rather than a style or a per-item wiring, for the reason
    /// <c>InputTrace</c> uses one: it covers menu items that do not exist yet,
    /// including the ones a docker builds at runtime, and there is no
    /// registration for a new menu to forget.
    /// </remarks>
    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            MenuItem.IsSubMenuOpenProperty.Changed.AddClassHandler<MenuItem>(
                (item, args) => OnSubMenuOpenChanged(item, args.GetNewValue<bool>()));
        }
        catch
        {
            // A menu that cannot be guarded still works; it just thrashes.
            _installed = false;
        }
    }

    /// <summary>Decide whether this close is too soon after the open.</summary>
    /// <param name="msSinceOpened">
    /// Milliseconds since this submenu opened, or null if it was not seen
    /// opening — in which case there is nothing to protect and the close
    /// stands.
    /// </param>
    internal static bool IsTooSoon(double? msSinceOpened) =>
        msSinceOpened is { } since && since < GraceMs;

    private static void OnSubMenuOpenChanged(MenuItem item, bool open)
    {
        if (_reopening) return;

        if (open)
        {
            lock (OpenedAt) OpenedAt[item] = DateTime.UtcNow;
            return;
        }

        double? since;
        lock (OpenedAt)
        {
            since = OpenedAt.TryGetValue(item, out var at)
                ? (DateTime.UtcNow - at).TotalMilliseconds
                : null;
        }

        if (!IsTooSoon(since))
        {
            lock (OpenedAt) OpenedAt.Remove(item);
            return;
        }

        // Put it back. The guard makes this write invisible to this handler, so
        // the property settles open rather than oscillating.
        Interlocked.Increment(ref _refused);
        _reopening = true;
        try { item.IsSubMenuOpen = true; }
        catch { /* a menu torn down mid-flight is not worth a crash */ }
        finally { _reopening = false; }
    }

    /// <summary>Test seam: forget every remembered open and the count.</summary>
    internal static void ResetForTests()
    {
        lock (OpenedAt) OpenedAt.Clear();
        Interlocked.Exchange(ref _refused, 0);
        _reopening = false;
    }

    /// <summary>Test seam: pretend this item opened <paramref name="msAgo"/> ago.</summary>
    internal static void NoteOpenedForTests(MenuItem item, double msAgo)
    {
        lock (OpenedAt) OpenedAt[item] = DateTime.UtcNow.AddMilliseconds(-msAgo);
    }
}
