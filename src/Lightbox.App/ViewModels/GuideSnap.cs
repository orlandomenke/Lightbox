using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Whether the stroke in progress has committed to a guide, and which one.
/// </summary>
/// <remarks>
/// <para>
/// Tier 1 of <c>docs/DESIGN-mainviewmodel-decomposition.md</c>. Three fields that
/// were loose on <see cref="MainViewModel"/> — the locked guide, the anchor the
/// heading is measured from, and whether the choice has been made — and the state
/// machine over them.
/// </para>
/// <para>
/// <b>Decide once, then hold.</b> That is the whole rule, and it exists because a
/// hand wobbles. A stroke that re-chooses its guide mid-way kinks visibly at the
/// moment it changes its mind, so the choice is made from a committed direction and
/// is never revisited. <see cref="Snapper"/> supplies the geometry; what lives here
/// is the part with memory, which is why it is a class and not another static.
/// </para>
/// <para>
/// <b>The anchor is the unsnapped start, except when it is not.</b> The heading is
/// measured from where the hand actually began, so snapping the anchor first would
/// measure from a point nobody visited — hence <see cref="Begin"/> taking the raw
/// point. But when the start itself lands on a guide the anchor moves to the snapped
/// point (<see cref="Anchor"/>), and when the stroke already has a direction, from a
/// shift-click joining the previous stroke, the decision is closed before it opens
/// (<see cref="HoldDirection"/>). Three entry points because there are three ways a
/// stroke can start, not because the surface wanted widening.
/// </para>
/// <para>
/// The tolerance and the on/off switch stay on the view model: they are
/// <c>[ObservableProperty]</c> bindings the XAML reads in two windows, so they cannot
/// move without changing every binding path that names them. This class is handed
/// them per call instead.
/// </para>
/// </remarks>
sealed class GuideSnap
{
    private Guide? _locked;
    private (double X, double Y) _anchor;
    private bool _decided;

    /// <summary>The guide this stroke committed to, if it has committed to one.</summary>
    internal Guide? Locked => _locked;

    /// <summary>
    /// Start a stroke at the raw, unsnapped point, forgetting any previous lock.
    /// </summary>
    internal void Begin(double x, double y)
    {
        _locked = null;
        _decided = false;
        _anchor = (x, y);
    }

    /// <summary>Move the anchor, for a start point that was itself snapped onto a guide.</summary>
    internal void Anchor(double x, double y) => _anchor = (x, y);

    /// <summary>
    /// The stroke arrives with a direction already — a shift-click continuing the last
    /// one — so no guide may re-aim it.
    /// </summary>
    internal void HoldDirection(double x, double y)
    {
        _anchor = (x, y);
        _decided = true;
    }

    /// <summary>Put a raw point where the guides say it belongs.</summary>
    /// <remarks>
    /// Called after stabilisation, not before. Snapping first and smoothing after would
    /// drag the point back off the guide, which is the wrong way round — the wobble is
    /// what you want removed, the guide is what you want obeyed.
    /// </remarks>
    internal (double X, double Y) Apply(
        IReadOnlyList<Guide> guides, double x, double y, double tolerance)
    {
        // Locked already: hold the line, and stop reconsidering.
        if (_locked is { } locked)
        {
            return Snapper.Along(locked, _anchor.X, _anchor.Y, x, y);
        }
        if (!_decided)
        {
            if (Snapper.Lock(guides, _anchor.X, _anchor.Y, x, y) is { } found)
            {
                _locked = found;
                _decided = true;
                return Snapper.Along(found, _anchor.X, _anchor.Y, x, y);
            }
            // Far enough to have meant something, and it matched nothing: this is a
            // freehand stroke, and asking again every event would only let a late
            // wobble grab it.
            var dx = x - _anchor.X;
            var dy = y - _anchor.Y;
            if (Math.Sqrt(dx * dx + dy * dy) >= Snapper.LockDistance) _decided = true;
        }
        return Snapper.Point(guides, x, y, tolerance);
    }
}
