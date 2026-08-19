namespace Lightbox.App.ViewModels;

/// <summary>
/// What a press on the crop rectangle has taken hold of.
/// </summary>
/// <remarks>
/// <see cref="Inside"/> moves the whole frame; the eight named parts resize it;
/// <see cref="None"/> means the press landed outside, which starts a fresh
/// rectangle rather than doing nothing.
/// </remarks>
public enum CropHandle
{
    None,
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
    Inside,
}

/// <summary>
/// The crop rectangle being dragged: where it is, what the pointer has hold of,
/// and what each kind of drag does to it. Document coordinates throughout.
/// </summary>
/// <remarks>
/// <para>
/// <b>A class of its own, with no view and no document in it</b>, for the reason
/// <c>TransformSession</c> gives: this is a gesture whose state has to be raised
/// together and dropped together, and the arithmetic is the part that goes
/// wrong. Handle picking, edge dragging, the clamp and the aspect lock are all
/// decided here, where a test can drive them without a window — the canvas
/// draws what this says and the view model applies it, and neither has a copy of
/// the rules.
/// </para>
/// <para>
/// <b>It never touches the document.</b> Applying a crop is
/// <c>CanvasResize.CropTo</c> through the view model, exactly as the two menu
/// commands do (Q126) — this only decides the rectangle. That is what keeps the
/// tool a third caller rather than a second implementation, and it is why the
/// menu commands did not have to be dug out of a tool when the tool arrived.
/// </para>
/// </remarks>
public sealed class CropSession
{
    /// <summary>
    /// The smallest crop anyone can drag to. One pixel each way is the floor
    /// <c>CanvasResize.CropTo</c> enforces; this stops the drag short of it so
    /// the rectangle on screen is always a rectangle the Apply would accept.
    /// </summary>
    private const double MinSize = 1;

    private double _paperLeft, _paperTop, _paperRight, _paperBottom;

    // Where the rectangle was when the current drag began, and where the
    // pointer was — a move is expressed as a delta from the press rather than
    // from the last move, so a drag that leaves the paper and comes back lands
    // where the hand is instead of accumulating the clamped remainder.
    private double _grabX, _grabY;
    private double _startLeft, _startTop, _startRight, _startBottom;

    public double Left { get; private set; }
    public double Top { get; private set; }
    public double Right { get; private set; }
    public double Bottom { get; private set; }

    public double Width => Right - Left;
    public double Height => Bottom - Top;

    /// <summary>What the current drag has hold of; <see cref="CropHandle.None"/> between drags.</summary>
    public CropHandle Holding { get; private set; }

    /// <summary>Whether a drag is in progress.</summary>
    public bool Dragging { get; private set; }

    /// <summary>
    /// Whether the rectangle is anything other than the whole page — i.e.
    /// whether applying it would do something.
    /// </summary>
    public bool WouldChangeAnything =>
        RoundedLeft != (int)Math.Round(_paperLeft)
        || RoundedTop != (int)Math.Round(_paperTop)
        || RoundedWidth != (int)Math.Round(_paperRight - _paperLeft)
        || RoundedHeight != (int)Math.Round(_paperBottom - _paperTop);

    /// <summary>
    /// The rectangle as whole pixels, which is the only form the document can
    /// take.
    /// </summary>
    /// <remarks>
    /// Rounded rather than truncated, and the width is taken from the rounded
    /// edges rather than rounded itself — otherwise a rectangle from 10.6 to
    /// 20.4 reports left 11 and width 10, which is a right edge of 21 that
    /// nobody dragged to. Deriving both from the same two roundings is what
    /// makes the applied crop the rectangle the artist could see.
    /// </remarks>
    public int RoundedLeft => (int)Math.Round(Left);

    /// <inheritdoc cref="RoundedLeft"/>
    public int RoundedTop => (int)Math.Round(Top);

    /// <inheritdoc cref="RoundedLeft"/>
    public int RoundedWidth => Math.Max(1, (int)Math.Round(Right) - RoundedLeft);

    /// <inheritdoc cref="RoundedLeft"/>
    public int RoundedHeight => Math.Max(1, (int)Math.Round(Bottom) - RoundedTop);

    /// <summary>
    /// Point the session at a page, and open it on the whole of that page.
    /// </summary>
    /// <remarks>
    /// The whole page rather than nothing, because a crop tool that starts
    /// empty makes the first gesture mandatory and gives the artist nothing to
    /// judge against. Opening on the page means the handles are there to pull
    /// in immediately, which is what every application that has this tool does.
    /// </remarks>
    public void Begin(double paperLeft, double paperTop, double width, double height)
    {
        _paperLeft = paperLeft;
        _paperTop = paperTop;
        _paperRight = paperLeft + width;
        _paperBottom = paperTop + height;
        Reset();
    }

    /// <summary>Back to the whole page, abandoning whatever was dragged.</summary>
    public void Reset()
    {
        Left = _paperLeft;
        Top = _paperTop;
        Right = _paperRight;
        Bottom = _paperBottom;
        Holding = CropHandle.None;
        Dragging = false;
    }

    /// <summary>
    /// What is under the pointer, in document coordinates.
    /// </summary>
    /// <param name="tolerance">
    /// How near an edge counts as being on it, in document units. The caller
    /// scales this by the zoom, so a handle stays the same size on screen —
    /// a fixed document tolerance would be ungrabbable zoomed out and would
    /// swallow the whole rectangle zoomed in.
    /// </param>
    public CropHandle HandleAt(double x, double y, double tolerance)
    {
        var nearLeft = Math.Abs(x - Left) <= tolerance;
        var nearRight = Math.Abs(x - Right) <= tolerance;
        var nearTop = Math.Abs(y - Top) <= tolerance;
        var nearBottom = Math.Abs(y - Bottom) <= tolerance;

        // Within the span of the edge, not merely on its infinite line —
        // otherwise the left edge is grabbable from anywhere above or below
        // the rectangle, which reads as the tool grabbing at nothing.
        var inRowSpan = y >= Top - tolerance && y <= Bottom + tolerance;
        var inColSpan = x >= Left - tolerance && x <= Right + tolerance;

        // Corners first: a corner is also two edges, and the corner is what a
        // hand aiming at a corner means.
        if (nearLeft && nearTop) return CropHandle.TopLeft;
        if (nearRight && nearTop) return CropHandle.TopRight;
        if (nearLeft && nearBottom) return CropHandle.BottomLeft;
        if (nearRight && nearBottom) return CropHandle.BottomRight;

        if (nearLeft && inRowSpan) return CropHandle.Left;
        if (nearRight && inRowSpan) return CropHandle.Right;
        if (nearTop && inColSpan) return CropHandle.Top;
        if (nearBottom && inColSpan) return CropHandle.Bottom;

        if (x > Left && x < Right && y > Top && y < Bottom) return CropHandle.Inside;
        return CropHandle.None;
    }

    /// <summary>
    /// Take hold. A press on a handle resizes, a press inside moves, and a
    /// press outside starts a new rectangle from that corner.
    /// </summary>
    /// <remarks>
    /// The press-outside case is why this returns nothing and cannot fail: a
    /// crop tool where clicking off the rectangle does nothing is a tool that
    /// looks broken the first time somebody wants to re-frame from scratch.
    /// Photoshop's behaviour, and the one an artist arrives with.
    /// </remarks>
    public void Press(double x, double y, double tolerance)
    {
        Holding = HandleAt(x, y, tolerance);
        Dragging = true;
        _grabX = x;
        _grabY = y;

        // A drag inside the frame moves it — but only when there is somewhere
        // to move to. While the frame still covers the whole page, moving it is
        // a no-op by construction (every direction is immediately clamped), and
        // what the hand means by dragging across the canvas is obviously "frame
        // it here". So the opening state treats an inside press as a fresh
        // rectangle, and the frame only becomes draggable once it is smaller
        // than the paper it sits on.
        if (Holding == CropHandle.Inside && !WouldChangeAnything) Holding = CropHandle.None;

        if (Holding == CropHandle.None)
        {
            // A fresh rectangle: it has no size yet, and the corner opposite
            // the pointer is the one that was just pressed. Reported as
            // BottomRight so the first Move drags the far corner, which is
            // what dragging out a box means.
            var px = Math.Clamp(x, _paperLeft, _paperRight);
            var py = Math.Clamp(y, _paperTop, _paperBottom);
            Left = Right = px;
            Top = Bottom = py;
            Holding = CropHandle.BottomRight;
        }

        _startLeft = Left;
        _startTop = Top;
        _startRight = Right;
        _startBottom = Bottom;
    }

    /// <summary>
    /// Drag. <paramref name="constrain"/> is Shift: it holds the rectangle's
    /// aspect ratio on a corner, which is the same thing Shift means on every
    /// other tool here.
    /// </summary>
    public void Move(double x, double y, bool constrain = false)
    {
        if (!Dragging) return;

        if (Holding == CropHandle.Inside)
        {
            MoveWholeRect(x, y);
            return;
        }

        double left = _startLeft, top = _startTop, right = _startRight, bottom = _startBottom;

        if (Holding is CropHandle.TopLeft or CropHandle.Left or CropHandle.BottomLeft) left = x;
        if (Holding is CropHandle.TopRight or CropHandle.Right or CropHandle.BottomRight) right = x;
        if (Holding is CropHandle.TopLeft or CropHandle.Top or CropHandle.TopRight) top = y;
        if (Holding is CropHandle.BottomLeft or CropHandle.Bottom or CropHandle.BottomRight) bottom = y;

        left = Math.Clamp(left, _paperLeft, _paperRight);
        right = Math.Clamp(right, _paperLeft, _paperRight);
        top = Math.Clamp(top, _paperTop, _paperBottom);
        bottom = Math.Clamp(bottom, _paperTop, _paperBottom);

        // Normalised here rather than on release, so dragging a handle past the
        // opposite edge flips the rectangle under the hand the way it does in
        // every other box-drag — instead of inverting silently and coming back
        // as a rectangle nobody drew. Holding deliberately keeps naming the
        // corner that was pressed: it is what the aspect lock anchors against,
        // and re-labelling it mid-drag would move the anchor under the hand.
        if (left > right) (left, right) = (right, left);
        if (top > bottom) (top, bottom) = (bottom, top);

        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;

        if (constrain && IsCorner(Holding)) ApplyAspectLock();
    }

    /// <summary>Let go. The rectangle keeps whatever it was dragged to.</summary>
    public void Release()
    {
        Dragging = false;
        Holding = CropHandle.None;
        // A press with no drag behind it leaves a rectangle of no size, which
        // is not something anybody meant to make and which Apply would refuse.
        // Falling back to the whole page says "nothing is cropped" rather than
        // leaving an invisible zero-width frame the artist has to find.
        if (Width < MinSize || Height < MinSize) Reset();
    }

    private void MoveWholeRect(double x, double y)
    {
        var w = _startRight - _startLeft;
        var h = _startBottom - _startTop;

        // Clamped as a whole rather than per edge: shifting each edge on its
        // own and clamping both would squash the rectangle against the paper's
        // edge instead of stopping it there.
        var left = Math.Clamp(_startLeft + (x - _grabX), _paperLeft, _paperRight - w);
        var top = Math.Clamp(_startTop + (y - _grabY), _paperTop, _paperBottom - h);

        Left = left;
        Top = top;
        Right = left + w;
        Bottom = top + h;
    }

    private static bool IsCorner(CropHandle handle) =>
        handle is CropHandle.TopLeft or CropHandle.TopRight
            or CropHandle.BottomLeft or CropHandle.BottomRight;

    /// <summary>
    /// Hold the ratio the rectangle had when the drag began, pulling the
    /// dragged corner back to whichever axis moved least.
    /// </summary>
    /// <remarks>
    /// Shrinking to the smaller axis rather than growing to the larger, because
    /// growing can leave the paper and would then be clamped — which is the
    /// aspect lock silently not holding, at the exact moment the artist is
    /// watching the numbers.
    /// </remarks>
    private void ApplyAspectLock()
    {
        var startW = _startRight - _startLeft;
        var startH = _startBottom - _startTop;
        if (startW < MinSize || startH < MinSize) return;

        var ratio = startW / startH;
        var w = Width;
        var h = Height;
        if (w < MinSize || h < MinSize) return;

        if (w / h > ratio) w = h * ratio; else h = w / ratio;

        // The corner the hand is on is the one that moves; the opposite corner
        // is the anchor and stays exactly where it was. Naming them for the
        // edge that moves rather than for the anchor, because that is what the
        // two assignments below actually do.
        var movesLeftEdge = Holding is CropHandle.TopLeft or CropHandle.BottomLeft;
        var movesTopEdge = Holding is CropHandle.TopLeft or CropHandle.TopRight;

        if (movesLeftEdge) Left = Right - w; else Right = Left + w;
        if (movesTopEdge) Top = Bottom - h; else Bottom = Top + h;
    }
}
