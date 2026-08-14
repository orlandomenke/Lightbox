using Avalonia;
using Avalonia.Input;

namespace Lightbox.App.Rendering;

/// <summary>Part of CanvasControl — see CanvasControl.cs.</summary>
/// <remarks>
/// The guide-grab section: picking a guide up under the pointer, telling a
/// move from a height-scale resize, and the hover cursor that is the whole
/// affordance. Split out under the monolith ratchet — every field it uses is
/// declared here, so no other section touches this state.
/// </remarks>
public sealed partial class CanvasControl
{
    /// <summary>
    /// One guide, flattened for the renderer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A snapshot rather than the <c>Guide</c> itself: the renderer runs on
    /// another thread and must never read a document object the UI thread may
    /// be halfway through editing. Same reason the reference boxes are copied.
    /// </para>
    /// <para>
    /// <see cref="GuidePainter.Emphasis"/> is not pushed with the rest: the
    /// window knows what the guides <i>are</i>, and this control knows what the
    /// pointer is over, so it is stamped on at draw time by
    /// <see cref="EmphasisedGuides"/> rather than making a hover round-trip
    /// through the view model to repaint.
    /// </para>
    /// </remarks>
    public readonly record struct GuideLine(
        string Id, int Kind, float X, float Y, float Spacing, IReadOnlyList<double> Angles,
        string? Label = null, int Divisions = 0,
        GuidePainter.Emphasis Emphasis = GuidePainter.Emphasis.None);

    /// <summary>
    /// Whether a guide under the pointer can be picked up and moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Move tool's job, and only its job — <c>MainWindow.RefreshGuideGrab</c>
    /// is what sets it. Grabbing a guide and drawing along one are the same
    /// gesture in the same place, so something has to say which was meant, and
    /// a tool says it plainly: it is visible in the palette, and it is still
    /// the answer with the rulers down, which is most of the time. Hiding or
    /// locking the guides overrides it, because both mean "leave the rig
    /// alone" whatever tool is in hand.
    /// </para>
    /// <para>
    /// The cursor is one half of the affordance and the rig's own emphasis is
    /// the other: while this is on, every guide is drawn a shade up from
    /// scenery, the one under the pointer brighter still, and the selected one
    /// brightest — see <see cref="EmphasisedGuides"/>. No handles float over
    /// the artwork, which is the thing this design exists to avoid; the guides
    /// simply say that they are reachable right now.
    /// </para>
    /// </remarks>
    public bool GuideDragEnabled
    {
        get => _guideDragEnabled;
        set
        {
            if (_guideDragEnabled == value) return;
            _guideDragEnabled = value;
            // The whole rig changes emphasis on this, so the switch has to
            // repaint — picking up the Move tool is what makes it visible.
            InvalidateVisual();
        }
    }

    private bool _guideDragEnabled;

    /// <summary>
    /// The guides to draw, or null for none — in which case nothing
    /// guide-related is drawn at all.
    /// </summary>
    public IReadOnlyList<GuideLine>? Guides
    {
        get => _guides;
        set
        {
            _guides = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<GuideLine>? _guides;

    /// <summary>
    /// A guide being pulled out of a ruler, not yet part of the document.
    /// </summary>
    /// <remarks>
    /// Chrome, not data: it exists for the length of one drag and is never
    /// written anywhere. Drawing it is the whole point of the gesture — a
    /// guide you cannot see until you let go is one you place twice.
    /// </remarks>
    public GuideLine? DraftGuide
    {
        get => _draftGuide;
        set
        {
            _draftGuide = value;
            InvalidateVisual();
        }
    }

    private GuideLine? _draftGuide;

    /// <summary>
    /// The volume checker's centre-of-mass arc, or null while it is off — in
    /// which case nothing balance-related is drawn or paid for.
    /// </summary>
    /// <remarks>
    /// Pushed from the window like <see cref="Guides"/> and <c>RigMarks</c>: a
    /// flattened snapshot for the render thread, never a document object the
    /// UI thread may be halfway through editing.
    /// </remarks>
    public IReadOnlyList<BalanceDot>? BalanceDots
    {
        get => _balanceDots;
        set
        {
            _balanceDots = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<BalanceDot>? _balanceDots;

    /// <summary>A guide was dragged, by a delta in document pixels.</summary>
    public event Action<string, double, double>? GuideMoved;

    /// <summary>A guide drag finished, so the move can be closed off.</summary>
    public event Action? GuideDragEnded;

    /// <summary>
    /// A height scale's top was dragged, by a vertical delta in document
    /// pixels. The view model turns it into a unit-height change.
    /// </summary>
    public event Action<string, double>? GuideResized;

    /// <summary>A height-scale resize finished, so it can become one undo step.</summary>
    public event Action? GuideResizeEnded;

    private string? _guideDrag;

    private bool _guideResizing;

    private (double X, double Y) _guideDragLast;

    /// <summary>How close, in screen pixels, counts as being on a guide.</summary>
    private const double GuideGrabPixels = 6;

    /// <summary>
    /// The guide under a view-space point, or null.
    /// </summary>
    /// <remarks>
    /// A line is grabbed anywhere along it, and a height scale anywhere along
    /// its post; everything else — grids, isometric axes, vanishing points —
    /// is grabbed at its anchor. Letting a grid be grabbed on any of its lines
    /// would mean a grid covers the whole canvas in grab targets and nothing
    /// else could ever be picked up.
    /// </remarks>
    private GuideLine? GuideAt(Point view)
    {
        if (_guides is not { Count: > 0 } guides) return null;
        var scale = FitScale() * _zoom;
        if (scale <= 0) return null;
        var reach = GuideGrabPixels / scale;
        var (x, y) = ViewToDoc(view);

        GuideLine? best = null;
        var bestDistance = reach;
        foreach (var guide in guides)
        {
            double distance;
            if (guide.Kind == (int)GuideKindLine && guide.Angles.Count > 0)
            {
                var radians = guide.Angles[0] * Math.PI / 180;
                distance = Math.Abs(
                    -Math.Sin(radians) * (x - guide.X) + Math.Cos(radians) * (y - guide.Y));
            }
            else if (guide.Kind == GuideKindHeightScale)
            {
                // Grabbed anywhere along its post, like a line — the post is
                // the thing on screen, and an anchor-only grab would mean
                // reaching for its feet every time.
                var top = guide.Y - guide.Spacing * Math.Max(1, guide.Divisions);
                var dy = Math.Clamp(y, top, guide.Y) - y;
                distance = Math.Sqrt((x - guide.X) * (x - guide.X) + dy * dy);
            }
            else
            {
                distance = Math.Sqrt(
                    (x - guide.X) * (x - guide.X) + (y - guide.Y) * (y - guide.Y));
            }
            if (distance > bestDistance) continue;
            bestDistance = distance;
            best = guide;
        }
        return best;
    }

    /// <summary>
    /// Whether a grab on this guide is the top of a height scale — a resize,
    /// not a move.
    /// </summary>
    /// <remarks>
    /// The top rung is the handle because it is what the gesture means: "this
    /// character is this tall". The divisions follow, since a head count is a
    /// proportion and resizing the character does not change how many heads
    /// they are.
    /// </remarks>
    private bool GrabsHeightScaleTop(GuideLine guide, Point view)
    {
        if (guide.Kind != GuideKindHeightScale) return false;
        var scale = FitScale() * _zoom;
        if (scale <= 0) return false;
        var (x, y) = ViewToDoc(view);
        var top = guide.Y - guide.Spacing * Math.Max(1, guide.Divisions);
        var reach = GuideGrabPixels / scale;
        return Math.Abs(y - top) <= reach && Math.Abs(x - guide.X) <= reach * 2;
    }

    private const int GuideKindLine = 0;
    private const int GuideKindHeightScale = 4;

    private bool _overGuide;

    private void UpdateGuideHoverCursor(Point view)
    {
        var hit = GuideDragEnabled ? GuideAt(view) : null;
        var over = hit is not null;
        var id = hit?.Id;
        if (id != _hoverGuideId)
        {
            _hoverGuideId = id;
            InvalidateVisual();
        }
        if (over == _overGuide) return;
        _overGuide = over;
        Cursor = over ? PointerCursors.Move : PointerCursors.For(PointerIntent);
    }

    /// <summary>The guide the pointer is on, or null. Chrome only; never pushed anywhere.</summary>
    private string? _hoverGuideId;

    /// <summary>
    /// The guide the tool options are pointed at, or null.
    /// </summary>
    /// <remarks>
    /// Pushed down from the window rather than owned here, for the reason every
    /// other piece of guide state is: the document's selection outlives this
    /// control, survives an undo, and is what the options bar binds to.
    /// </remarks>
    public string? SelectedGuideId
    {
        get => _selectedGuideId;
        set
        {
            if (_selectedGuideId == value) return;
            _selectedGuideId = value;
            InvalidateVisual();
        }
    }

    private string? _selectedGuideId;

    /// <summary>
    /// A guide was clicked, or the click landed on nothing and cleared the
    /// selection.
    /// </summary>
    public event Action<string?>? GuideSelected;

    /// <summary>Raise <see cref="GuideSelected"/>, from the press handler in CanvasControl.cs.</summary>
    private void SelectGuide(string? id) => GuideSelected?.Invoke(id);

    /// <summary>
    /// The guides to draw, each stamped with how prominently.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is lit unless a guide can actually be picked up — with a brush
    /// in hand, or the guides locked or hidden, the rig is scenery again and
    /// this returns the pushed list untouched, allocating nothing. That is also
    /// what stops the highlight from being a distraction while drawing: it
    /// appears with the tool that can use it.
    /// </para>
    /// <para>
    /// A fresh list rather than a mutation, because <c>_guides</c> is the
    /// snapshot the render thread reads — see <see cref="GuideLine"/>.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<GuideLine>? EmphasisedGuides()
    {
        if (!GuideDragEnabled || _guides is not { Count: > 0 } guides) return _guides;
        var lit = new List<GuideLine>(guides.Count);
        foreach (var guide in guides)
        {
            lit.Add(guide with
            {
                Emphasis =
                    guide.Id == _selectedGuideId ? GuidePainter.Emphasis.Selected
                    : guide.Id == _hoverGuideId ? GuidePainter.Emphasis.Hover
                    : GuidePainter.Emphasis.Grabbable,
            });
        }
        return lit;
    }
}
