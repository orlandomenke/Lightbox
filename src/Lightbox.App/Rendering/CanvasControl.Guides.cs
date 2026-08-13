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
        var over = GuideDragEnabled && GuideAt(view) is not null;
        if (over == _overGuide) return;
        _overGuide = over;
        Cursor = over ? PointerCursors.Move : PointerCursors.For(PointerIntent);
    }
}
