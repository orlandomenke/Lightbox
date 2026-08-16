using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// The rig and armature overlays' surface on the canvas control: the chrome
/// pushed from the window, the gesture events, and their state. A partial
/// rather than more of <c>CanvasControl.cs</c>, because that file is on the
/// monolith ratchet — new work goes beside it, and the rig block moved here
/// with the bones to pay for the lines the gestures still add inline.
/// </summary>
public sealed partial class CanvasControl
{
    /// <summary>
    /// The bones the armature overlay draws, or null. Pushed from the window
    /// like <see cref="RigMarks"/>, for the same reason: a flattened snapshot,
    /// not a value an artist edits.
    /// </summary>
    public IReadOnlyList<BoneChrome>? BoneChromes
    {
        get => _boneChromes;
        set
        {
            _boneChromes = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<BoneChrome>? _boneChromes;

    /// <summary>The weight heat view under the bones, or null.</summary>
    public IReadOnlyList<HeatPoint>? HeatPoints
    {
        get => _heatPoints;
        set
        {
            _heatPoints = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<HeatPoint>? _heatPoints;

    /// <summary>A press landed with the Bone tool, in document space — x, y, scale.</summary>
    /// <remarks>Void like <see cref="RigPressed"/>: the window owns the decision and answers with <see cref="BeginBoneDrag"/>.</remarks>
    public event Action<double, double, double>? BonePressed;

    /// <summary>
    /// A bone gesture finished: the bone (null means the drag started on empty
    /// canvas and creates one), the grab, the press point and the release point.
    /// On release with the endpoints, not per move, for <see cref="RigDragged"/>'s
    /// reason — one editor step per gesture.
    /// </summary>
    public event Action<string?, BoneGrab, double, double, double, double, bool>? BoneGestureEnded;

    /// <summary>
    /// The same gesture per pointer move, for the live preview — chrome only,
    /// no editor step. The release still lands everything through
    /// <see cref="BoneGestureEnded"/>; a drag the view model never previews
    /// still commits exactly as it always did.
    /// </summary>
    public event Action<string?, BoneGrab, double, double, double, double, bool>? BoneDragged;

    /// <summary>
    /// The gesture died without a release — pointer capture lost — so the
    /// preview must be dropped without committing anything.
    /// </summary>
    public event Action? BoneGestureCancelled;

    /// <summary>Shift was held on the press, so a tip drag grows a child rather than re-aiming.</summary>
    private bool _boneExtruding;

    private string? _boneDragId;
    private BoneGrab _boneDragGrab;
    private (double X, double Y) _boneGestureStart;
    private bool _boneGestureActive;

    /// <summary>
    /// Document pixels per screen pixel, for hit-tests that use screen-sized
    /// targets. Exposed from the partial so the ratcheted file gains nothing.
    /// </summary>
    public double ViewScale => FitScale() * _zoom;

    /// <summary>The window's answer to <see cref="BonePressed"/>: this press grabbed a bone.</summary>
    public void BeginBoneDrag(string id, BoneGrab grab)
    {
        _boneDragId = id;
        _boneDragGrab = grab;
    }

    /// <summary>
    /// The rig marks to draw, or null for none — in which case no rig furniture
    /// is drawn at all.
    /// </summary>
    /// <remarks>
    /// <b>B58.</b> The one thing that was missing: <c>MainViewModel.RigMarks</c>
    /// existed, was resolved through holds, and nothing ever asked for it. A plain
    /// property with <c>InvalidateVisual</c> in the setter rather than a
    /// <c>StyledProperty</c>, following <see cref="Guides"/> — the list is pushed
    /// from the window when the view model says it changed, not bound, because it
    /// is a flattened snapshot rather than a value an artist edits.
    /// <para>
    /// Absent rather than empty when the mode is off: <c>RigMarks</c> returns an
    /// empty list when <c>RigEditMode</c> is false, so nothing here needs to know
    /// about the mode at all.
    /// </para>
    /// </remarks>
    public IReadOnlyList<RigMark>? RigMarks
    {
        get => _rigMarks;
        set
        {
            _rigMarks = value;
            InvalidateVisual();
        }
    }

    private IReadOnlyList<RigMark>? _rigMarks;

    /// <summary>
    /// Whether a press should edit the rig instead of drawing.
    /// </summary>
    /// <remarks>
    /// A mode, for the reason <c>MainViewModel.RigEditMode</c> gives: Shift, Ctrl
    /// and Alt are already spoken for on the canvas, and a fourth meaning for one
    /// of them is a chord nobody finds and everybody triggers by accident.
    /// </remarks>
    public bool RigEditMode
    {
        get => _rigEditMode;
        set
        {
            _rigEditMode = value;
            InvalidateVisual();
        }
    }

    private bool _rigEditMode;


    /// <summary>A press landed on the canvas in rig edit mode, in document space.</summary>
    /// <remarks>
    /// Void, like every other event here, so the window owns the decision. The
    /// control then learns the answer through <see cref="BeginRigDrag"/> — the
    /// alternative was a <c>Func</c> returning a hit, which would put the view
    /// model's shape into the control's signature.
    /// </remarks>
    public event Action<double, double, double>? RigPressed;

    /// <summary>A rig drag finished: the mark, the corner, and the total delta.</summary>
    /// <remarks>
    /// On release with the whole delta, not per pointer move, because
    /// <c>MainViewModel.DragRig</c> is one editor step per call — a long drag
    /// reported per event would be a hundred undo entries.
    /// </remarks>
    public event Action<string, RigCorner, double, double>? RigDragged;

    /// <summary>An empty-canvas press in rig edit mode, for whatever the window adds there.</summary>
    public event Action<double, double>? RigEmptyPressed;

    private string? _rigDragId;
    private RigCorner _rigDragCorner;
    private (double X, double Y) _rigDragStart;

    /// <summary>
    /// Start dragging a mark. Called by the window once it knows what was hit.
    /// </summary>
    public void BeginRigDrag(string id, RigCorner corner)
    {
        _rigDragId = id;
        _rigDragCorner = corner;
    }

    /// <summary>
    /// Whether a Bone-tool press paints weights instead of working bones.
    /// Pushed from the window like <see cref="RigEditMode"/>.
    /// </summary>
    public bool WeightPainting { get; set; }

    /// <summary>A weight-brush stroke began at this document point.</summary>
    public event Action<double, double, double>? WeightStrokeStarted;

    /// <summary>One weight dab: position and pressure, per pointer move.</summary>
    public event Action<double, double, double>? WeightDabbed;

    /// <summary>The weight-brush stroke ended; the window lands the one undo step.</summary>
    public event Action? WeightStrokeEnded;

    private bool _weightStrokeActive;

    /// <summary>The Bone tool's press: a weight-brush stroke or a bone gesture.</summary>
    private void BeginBoneGesture(double x, double y, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (WeightPainting)
        {
            _weightStrokeActive = true;
            WeightStrokeStarted?.Invoke(x, y, PressureOf(e.GetCurrentPoint(this).Properties.Pressure));
        }
        else
        {
            _boneDragId = null;
            _boneDragGrab = BoneGrab.None;
            _boneGestureStart = (x, y);
            _boneGestureActive = true;
            // Blender's E, as a drag: Shift on a tip pulls a connected child
            // out of it. Read at the press, because the key can be let go
            // mid-drag and the gesture is decided by how it started.
            _boneExtruding = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);
            // The window answers with BeginBoneDrag if the press hit a bone.
            BonePressed?.Invoke(x, y, FitScale() * _zoom);
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <summary>
    /// Streams weight dabs while the brush is down, and reports every other
    /// bone drag for the live preview. True when the move was consumed.
    /// </summary>
    private bool ContinueBoneGesture(Avalonia.Input.PointerEventArgs e)
    {
        if (_weightStrokeActive)
        {
            var (wx, wy) = ViewToDoc(e.GetPosition(this));
            WeightDabbed?.Invoke(wx, wy, PressureOf(e.GetCurrentPoint(this).Properties.Pressure));
            e.Handled = true;
            return true;
        }
        if (!_boneGestureActive) return false;
        var (x, y) = ViewToDoc(e.GetPosition(this));
        BoneDragged?.Invoke(
            _boneDragId, _boneDragGrab, _boneGestureStart.X, _boneGestureStart.Y, x, y, _boneExtruding);
        e.Handled = true;
        return true;
    }

    /// <summary>
    /// A bone gesture ends the same way a rig drag does: on release, with the
    /// endpoints, one editor step. The window decides what the gesture meant —
    /// create, joint move, re-aim, pose, or the weight stroke's single undo
    /// step — from the grab and the mode. True when the release was consumed.
    /// </summary>
    private bool EndBoneGesture(Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_weightStrokeActive)
        {
            _weightStrokeActive = false;
            e.Pointer.Capture(null);
            WeightStrokeEnded?.Invoke();
            e.Handled = true;
            return true;
        }
        if (!_boneGestureActive) return false;

        var (bx, by) = ViewToDoc(e.GetPosition(this));
        _boneGestureActive = false;
        var boneId = _boneDragId;
        var boneGrab = _boneDragGrab;
        _boneDragId = null;
        e.Pointer.Capture(null);
        BoneGestureEnded?.Invoke(
            boneId, boneGrab, _boneGestureStart.X, _boneGestureStart.Y, bx, by, _boneExtruding);
        e.Handled = true;
        return true;
    }

    /// <summary>A mouse reports 0 or 0.5 depending on platform; either way a full-strength dab.</summary>
    private static double PressureOf(float raw) => raw <= 0 ? 1.0 : raw;

    // ---- the whole-line hover, and the point snapper (B216, B217) ------------------
    //
    // Both are surfaces pushed in from the window rather than decisions this
    // control makes, which is what this partial is for — and CanvasControl.cs is
    // on the ratchet, so new surface goes beside it rather than into it.

    /// <summary>
    /// Put a document point where the guides say it belongs, or leave it.
    /// </summary>
    /// <remarks>
    /// <b>B216.</b> The canvas owns the marquee rubber band, so unlike the
    /// view model's own tools it cannot reach <c>SnappedPoint</c> directly. It
    /// is handed the function instead — the same delegation every other
    /// decision this control makes already uses — which keeps `Snapper`, the
    /// guides and the on/off switch on the view model's side and leaves this
    /// with the geometry it already had.
    ///
    /// <para>
    /// Identity until something sets it, so a control with no snapper attached
    /// behaves exactly as it did.
    /// </para>
    /// </remarks>
    private Func<double, double, (double X, double Y)> _snapPoint = static (x, y) => (x, y);

    public void SetPointSnapper(Func<double, double, (double X, double Y)>? snap) =>
        _snapPoint = snap ?? ((x, y) => (x, y));

    /// <summary>Snap a document point through <see cref="SetPointSnapper"/>.</summary>
    private (double X, double Y) Snapped(double x, double y) => _snapPoint(x, y);

    private IReadOnlyList<SelectedLine>? _hoveredLines;

    /// <summary>
    /// The line the pointer is over, drawn as a dimmer version of a selection.
    /// </summary>
    /// <remarks>
    /// <b>B217.</b> The white arrow has previewed the geometry it would reach
    /// into since the vector work landed, and the two other tools that take
    /// hold of a whole line — the Arrow and the Width tool — showed nothing at
    /// all. So the one tool that reaches into a line told you what you were
    /// about to get, and the two that act on all of it did not.
    ///
    /// <para>
    /// It is the <em>selection</em> outline at lower alpha rather than a
    /// shape of its own, on purpose: a hover means "this is the thing the click
    /// will take", so it should look like a dimmer version of what the click
    /// produces. Anything else has to be learned separately.
    /// </para>
    /// <para>
    /// Built once per line, not per pointer move. The view model answers
    /// whether the hovered line actually changed, so the common case — the
    /// pointer travelling along a line it is already previewing — costs a hit
    /// test and no rebuild, which is what keeps this out of invariant 6's way.
    /// </para>
    /// </remarks>
    public void SetHoveredLine((IReadOnlyList<Core.Documents.StrokePoint> Points, bool Closed)? line)
    {
        if (line is not { } hovered || hovered.Points.Count < 2)
        {
            if (_hoveredLines is null) return;
            _hoveredLines = null;
            InvalidateVisual();
            return;
        }

        var points = new SKPoint[hovered.Points.Count];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new SKPoint((float)hovered.Points[i].X, (float)hovered.Points[i].Y);
        }
        _hoveredLines = [new SelectedLine(points, hovered.Closed)];
        InvalidateVisual();
    }

    /// <summary>Ask what line is under the pointer; returns whether it moved.</summary>
    private Func<double, double, double, bool>? _hoverLine;

    public void SetLineHover(Func<double, double, double, bool>? hover) => _hoverLine = hover;

    // ---- the picked lines being moved (B223) ---------------------------------------
    //
    // The drag used to report one delta on release and the drawing arrived
    // after the fact. It is a transform session now — the same one the Move
    // tool and Ctrl+T use — so it needs a beginning and a middle as well as an
    // end. Declared here rather than in CanvasControl.cs, which is on the
    // ratchet.

    /// <summary>Whether a line-move session is live for the current drag.</summary>
    /// <remarks>
    /// The view model answers whether it actually opened one — a press on a
    /// line whose layer is locked does not — so the moves and the release only
    /// talk to a session that exists.
    /// </remarks>
    private bool _lineMoveLive;

    /// <summary>A drag on the picked lines began. Returns whether a session opened.</summary>
    public event Func<double, double, bool>? SelectedLinesMoveStarted;

    /// <summary>The drag moved, in document coordinates. The flag is Shift: hold one axis.</summary>
    public event Action<double, double, bool>? SelectedLinesMoved;

    /// <summary>The drag finished and should become one undo step.</summary>
    public event Action? SelectedLinesMoveEnded;

    /// <summary>The press turned out to be a click; throw the session away.</summary>
    public event Action? SelectedLinesMoveCancelled;
}
