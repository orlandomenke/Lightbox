using Avalonia;
using Avalonia.Input;

namespace Lightbox.App.Rendering;

/// <summary>
/// What the pointer looks like, decided in one place (B241).
/// </summary>
/// <remarks>
/// <para>
/// <b>The cursor is the whole affordance on this canvas.</b> Nothing floats
/// over the drawing — no handles bar the gizmo's, no row of buttons — so the
/// pointer changing shape is the only thing that says a guide can be picked up,
/// that this corner scales and that one turns. That was true of guides alone;
/// eleven other grabbable things showed whatever the tool happened to be
/// wearing, which meant the gizmo — six distinct gestures inside one box — said
/// nothing at all.
/// </para>
/// <para>
/// <b>One method, asked from everywhere that could change the answer.</b> The
/// owner's two rules are structural rather than decorative: the cursor must
/// hold <em>during</em> a gesture as well as on hover, and a held modifier must
/// show what it would do. Both fall out of asking the live gestures first and
/// the hover second — a drag cannot be overwritten by a hover test that runs
/// later, because the hover test never runs while a drag is live.
/// </para>
/// <para>
/// <b>The choice is separated from the bitmap.</b> <see cref="CanvasCursorNow"/>
/// returns a <see cref="CanvasCursorChoice"/> — pure data — and
/// <see cref="CursorFor"/> is the only thing that turns one into pixels. That is
/// what makes the decision testable: rendering wants a surface a headless run
/// has not got, so a test that asked for the <c>Cursor</c> would be asserting
/// against <c>PointerCursors</c>' fallback rather than against the answer.
/// </para>
/// <para>
/// <b>Angles are measured in view space, not document space.</b> The view can
/// be zoomed, rotated and mirrored (invariant 5), so a handle's direction on
/// screen is not its direction in the record. Every angle here is taken from
/// two points put through <see cref="DocToView"/>, which makes it correct under
/// all three without knowing about any of them.
/// </para>
/// </remarks>
public sealed partial class CanvasControl
{
    /// <summary>
    /// Set the cursor to what a press would do at this point.
    /// </summary>
    /// <remarks>
    /// Assigning <see cref="Avalonia.Controls.Control.Cursor"/> when it has not
    /// changed is cheap, and comparing first would mean holding a copy that can
    /// go stale — the mistake B147 records one overlay along.
    /// </remarks>
    private void RefreshCanvasCursor(Point view, KeyModifiers modifiers)
    {
        _cursorAt = view;
        _cursorModifiers = modifiers;
        Cursor = CursorFor(CanvasCursorNow(view, modifiers));
    }

    /// <summary>
    /// Re-ask at the last place the pointer was, because something other than
    /// the pointer changed.
    /// </summary>
    /// <remarks>
    /// <b>This is what makes a held key show its own cursor.</b> The artist's
    /// hand is still: they press Ctrl and the answer changes without a single
    /// pointer event to hang it on. The same is true of picking up a tool, of
    /// the gizmo appearing, and of a guide being locked while the pointer sits
    /// over it. With the pointer off the canvas there is nothing to re-ask
    /// about, and the caller's own fallback stands.
    /// </remarks>
    internal void RefreshCanvasCursor()
    {
        if (_cursorAt is { } at) Cursor = CursorFor(CanvasCursorNow(at, _cursorModifiers));
    }

    /// <summary>Where the pointer was when the cursor was last decided.</summary>
    private Point? _cursorAt;

    private KeyModifiers _cursorModifiers;

    /// <summary>The platform cursor for a choice.</summary>
    /// <remarks>
    /// The whole of the rendering half, and deliberately trivial: everything
    /// that could be got wrong is upstream in the choice, where a test can see
    /// it.
    /// </remarks>
    private static Cursor CursorFor(CanvasCursorChoice choice) => choice.Kind switch
    {
        CanvasCursorKind.Resize => PointerCursors.ResizeAt(choice.Angle),
        CanvasCursorKind.Grab => PointerCursors.Grab,
        CanvasCursorKind.Rotate => PointerCursors.Turn,
        // The crosshair with its +/−: the select family saying whether this
        // press adds to or carves from the selection. Paint's badge rides the
        // brush ring instead — its platform cursor is hidden.
        CanvasCursorKind.Precise when choice.Badge is not CursorBadge.None =>
            PointerCursors.PreciseBadged(choice.Badge),
        _ => PointerCursors.For(choice.Kind),
    };

    /// <summary>
    /// The cursor for right now: the gesture in flight, else what is under the
    /// pointer, else the tool's own intent.
    /// </summary>
    /// <remarks>
    /// The order is the feature. A gesture in flight is what the artist is
    /// <em>doing</em>, and it outranks whatever the pointer has since wandered
    /// over — a scale that turned into a move cursor halfway through the drag
    /// would be the pointer contradicting the thing it is describing.
    /// </remarks>
    internal CanvasCursorChoice CanvasCursorNow(Point view, KeyModifiers modifiers)
    {
        // ---- a gesture in flight ------------------------------------------------
        if (_panning) return CanvasCursorKind.Grab;
        if (_txActive && _txDrag != TxDrag.None) return GizmoCursor(_txDrag, _txHandle);
        if (_guideDrag is not null)
        {
            return _guideResizing ? GuideResizeCursor() : CanvasCursorKind.Move;
        }
        if (_movingGuides || _movingRefBoxes || _movingAnchors || _movingShapes || _movingContent)
        {
            return CanvasCursorKind.Move;
        }
        if (_cameraDrag != CameraDrag.None) return CameraCursor(_cameraDrag);
        if (_gridGesture == GridGesture.Resize) return RefBoxResizeCursor(_gridLeft, _gridTop);
        if (_gridGesture == GridGesture.Move) return CanvasCursorKind.Move;
        if (_lineDragFrom is not null) return CanvasCursorKind.Move;

        // ---- what a press here would grab ---------------------------------------
        var (docX, docY) = ViewToDoc(view);

        // The gizmo owns the canvas while it is up — every press goes to the
        // handles whatever the toolbar says — so it is asked before any tool.
        if (_txActive)
        {
            var (kind, handle) = TxHitTest(docX, docY);
            return GizmoCursor(kind, handle);
        }

        if (GuideDragEnabled && GuideAt(view) is { } guide)
        {
            return GrabsHeightScaleTop(guide, view) ? GuideResizeCursor() : CanvasCursorKind.Move;
        }

        // The camera's handles are deliberately small targets, and the press
        // handler asks for them before the reference boxes — so this does too,
        // or the cursor would promise a box resize the press reads as a camera.
        if (_cameraFrame is { Length: 4 }
            && CameraHandleAt(docX, docY, (float)(FitScale() * _zoom))
                is var camera and not CameraDrag.None)
        {
            return CameraCursor(camera);
        }

        if (ReferenceBoxes is not null && RefBoxCornerAt(docX, docY) is { } refCorner)
        {
            return RefBoxResizeCursor(refCorner.Left, refCorner.Top);
        }

        // The same promise for a projected reference the Arrow has hold of
        // (Q108): a corner scales it, inside it moves. A locked one offers
        // neither, and saying nothing is how the cursor tells the truth about
        // that — B241's rule, that the pointer names the gesture it would get.
        if (ReferenceSheetBox is { } sheet && !ReferenceSheetLocked)
        {
            var reach = 6 / Math.Max(0.01, _zoom * FitScale());
            if (CornerOf(sheet, docX, docY, reach) is { } sheetCorner)
            {
                return RefBoxResizeCursor(sheetCorner.Left, sheetCorner.Top);
            }
            if (docX >= sheet.X && docX <= sheet.X + sheet.W
                && docY >= sheet.Y && docY <= sheet.Y + sheet.H)
            {
                return CanvasCursorKind.Move;
            }
        }

        // ---- the tool's own intent ----------------------------------------------
        return new CanvasCursorChoice(PointerIntent, Badge: PointerBadge);
    }

    /// <summary>The cursor for one part of the transform gizmo.</summary>
    /// <remarks>
    /// <b>Six gestures live inside one box and none of them was legible.</b>
    /// Krita's layout — pivot, corners, edges, inside moves, outside turns — is
    /// only discoverable if the pointer says which is which, because the box
    /// draws the same handle for scale and for perspective.
    /// </remarks>
    private CanvasCursorChoice GizmoCursor(TxDrag kind, int handle) => kind switch
    {
        TxDrag.Move => CanvasCursorKind.Move,
        TxDrag.Rotate => CanvasCursorKind.Rotate,
        // The pivot is a place, not a direction — it is dropped rather than
        // dragged along an axis, so it takes the four-way rather than an arrow.
        TxDrag.Pivot => CanvasCursorKind.Move,
        // A perspective corner goes wherever it is put; there is no axis to
        // point along, which is exactly what distinguishes it from a scale.
        TxDrag.Quad => CanvasCursorKind.Move,
        TxDrag.ScaleCorner => CanvasCursorChoice.ResizeAt(CornerAngle(handle)),
        TxDrag.ScaleEdge => CanvasCursorChoice.ResizeAt(EdgeAngle(handle)),
        _ => PointerIntent,
    };

    /// <summary>
    /// The screen angle a corner handle scales along: its own diagonal.
    /// </summary>
    /// <remarks>
    /// Taken from the opposite corner through this one rather than from the
    /// pivot, because the pivot is draggable — moving it off centre would swing
    /// every corner cursor while the box itself had not moved at all.
    /// </remarks>
    private double CornerAngle(int handle)
    {
        var corners = TxCorners();
        var here = corners[handle & 3];
        var across = corners[(handle + 2) & 3];
        return ViewAngle(across.X, across.Y, here.X, here.Y);
    }

    /// <summary>
    /// The screen angle an edge handle scales along: the edge's normal.
    /// </summary>
    /// <remarks>
    /// An edge grows the box perpendicular to itself, so the arrow lies across
    /// the edge rather than along it. Pointing it along the edge is the version
    /// that looks right in a screenshot and is wrong under the hand.
    /// </remarks>
    private double EdgeAngle(int handle)
    {
        var corners = TxCorners();
        var a = corners[handle & 3];
        var b = corners[(handle + 1) & 3];
        return ViewAngle(a.X, a.Y, b.X, b.Y) + 90;
    }

    /// <summary>
    /// The angle of a document-space direction once the view has had it.
    /// </summary>
    /// <remarks>
    /// Both ends go through <see cref="DocToView"/> and the angle is taken from
    /// the result, so zoom, rotation and mirror are all accounted for without
    /// this having to know they exist. A mirrored view reverses the sense of the
    /// angle, which is correct: the handle really does drag the other way.
    /// </remarks>
    private double ViewAngle(double fromX, double fromY, double toX, double toY)
    {
        var a = DocToView(fromX, fromY);
        var b = DocToView(toX, toY);
        return Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
    }

    /// <summary>
    /// A height scale's top rung resizes it, and resizes it vertically.
    /// </summary>
    /// <remarks>
    /// <b>The one guide grab that is not a move</b>, and it wore the move cursor
    /// like the rest — so the gesture that says "this character is this tall"
    /// was indistinguishable from sliding the chart sideways. Measured through
    /// the view like everything else here, so it is still right on a rotated
    /// canvas.
    /// </remarks>
    private CanvasCursorChoice GuideResizeCursor() =>
        CanvasCursorChoice.ResizeAt(ViewAngle(0, 0, 0, 1));

    /// <summary>A reference box corner, as the direction that corner drags along.</summary>
    private CanvasCursorChoice RefBoxResizeCursor(bool left, bool top) =>
        // The diagonal through the box: a top-left corner and a bottom-right
        // one drag along the same line, which is why two booleans give one of
        // two answers rather than four.
        CanvasCursorChoice.ResizeAt(ViewAngle(0, 0, left == top ? 1 : -1, 1));

    /// <summary>The reference-box corner under a document point, or null.</summary>
    /// <remarks>
    /// The same reach and the same corners-before-insides order
    /// <see cref="BeginGridGesture"/> uses, so the cursor cannot promise a
    /// resize the press then reads as a move.
    /// </remarks>
    private (bool Left, bool Top)? RefBoxCornerAt(double x, double y)
    {
        var boxes = ReferenceBoxes ?? [];
        var reach = 6 / Math.Max(0.01, _zoom * FitScale());
        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            if (!boxes[i].Selected) continue;
            var corner = CornerOf(boxes[i], x, y, reach);
            if (corner is not null) return corner;
        }
        return null;
    }

    /// <summary>The cursor for one of the camera frame's three grips.</summary>
    /// <remarks>
    /// The camera is the one gizmo whose handles were already distinct on
    /// screen and still said nothing under the hand. Zoom is a resize along the
    /// frame's own diagonal, so it is measured rather than assumed — a rolled
    /// camera drags its zoom grip on the roll, not on the screen diagonal.
    /// </remarks>
    private CanvasCursorChoice CameraCursor(CameraDrag grip) => grip switch
    {
        CameraDrag.Pan => CanvasCursorKind.Move,
        CameraDrag.Rotate => CanvasCursorKind.Rotate,
        CameraDrag.Zoom when _cameraFrame is { Length: 4 } frame =>
            CanvasCursorChoice.ResizeAt(ViewAngle(
                frame[0].X, frame[0].Y, frame[2].X, frame[2].Y)),
        _ => CanvasCursorKind.Move,
    };
    /// <summary>
    /// Whatever the press started, the cursor becomes that gesture's and stays
    /// there until it ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wrapped around the handler rather than appended to it, because the body
    /// returns from a dozen places — one per thing that can be grabbed — and
    /// that is exactly why the refresh cannot be a line at each of them. The
    /// one it was forgotten at would be the one gesture with the wrong pointer,
    /// and nothing would say so.
    /// </para>
    /// <para>
    /// <b>Both wrappers live here rather than beside the handlers they wrap</b>,
    /// which is where they were first written: they exist for the cursor and
    /// nothing else, so this is the file that has to be read to understand them.
    /// The bodies stay in <c>CanvasControl.cs</c> with the rest of the input
    /// handling, which is what they are about.
    /// </para>
    /// </remarks>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        try { OnPointerPressedCore(e); }
        finally { RefreshCanvasCursor(e.GetPosition(this), e.KeyModifiers); }
    }

    /// <summary>The same, for the release that ends a gesture.</summary>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        try { OnPointerReleasedCore(e); }
        finally { RefreshCanvasCursor(e.GetPosition(this), e.KeyModifiers); }
    }

}
