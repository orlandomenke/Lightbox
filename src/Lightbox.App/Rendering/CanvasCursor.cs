using Avalonia.Input;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Rendering;

/// <summary>What the pointer should look like over the canvas.</summary>
/// <remarks>
/// Named rather than typed as an Avalonia <c>Cursor</c> so the mapping stays a
/// pure function — the control turns a name into a platform cursor, and the
/// decision is testable with no window, which is the shape
/// <see cref="RigOverlay.CursorFor"/> already uses.
/// </remarks>
public enum CanvasCursorKind
{
    /// <summary>Nothing special: the arrow the platform gives you.</summary>
    Default,

    /// <summary>A brush or eraser, which draw their own size ring as well.</summary>
    Paint,

    /// <summary>Picking a colour out of the artwork.</summary>
    Pick,

    /// <summary>Pouring, or dragging a gradient.</summary>
    Fill,

    /// <summary>Dragging pixels or a record around.</summary>
    Move,

    /// <summary>Placing points: the pen, and the shape tools.</summary>
    Precise,

    /// <summary>Picking records or points — the two arrows.</summary>
    PickRecords,

    /// <summary>
    /// This drag will turn something rather than move it — posing a bone, or
    /// re-aiming one in bind mode.
    /// </summary>
    Rotate,

    /// <summary>
    /// The tool would do nothing here, and the pointer says so before the
    /// artist finds out by trying.
    /// </summary>
    Forbidden,

    /// <summary>
    /// This drag resizes something along an axis, and the axis is carried
    /// beside the kind — see <c>CanvasCursorChoice</c>.
    /// </summary>
    /// <remarks>
    /// <b>B241: the one kind that is not self-contained.</b> Every other value
    /// here names a cursor outright; a resize names a family of them, because
    /// the gizmo rotates and the arrow has to lie along the handle's real
    /// direction on screen. The angle travels with it rather than being folded
    /// into eight enum members, which is what lets the cursor be drawn at 30°
    /// for a box turned 30° (Q107).
    /// </remarks>
    Resize,

    /// <summary>The canvas itself is being dragged: the hand holding it.</summary>
    Grab,
}

/// <summary>
/// What the pointer should be right now — a kind, and an angle when the kind
/// needs one.
/// </summary>
/// <remarks>
/// <b>The decision, separated from the drawing (B241).</b> Rendering a cursor
/// needs a render surface, which a headless run has not got, so a test that
/// asked for the <c>Cursor</c> object would be asserting against the
/// catch-and-fall-back rather than against the choice. This is the choice: pure
/// data, comparable, and the thing worth pinning. <c>PointerCursors</c> turns it
/// into pixels.
/// </remarks>
/// <param name="Angle">
/// Degrees clockwise from screen-right, for <see cref="CanvasCursorKind.Resize"/>.
/// Meaningless and zero for every other kind.
/// </param>
public readonly record struct CanvasCursorChoice(CanvasCursorKind Kind, double Angle = 0)
{
    public static implicit operator CanvasCursorChoice(CanvasCursorKind kind) => new(kind);

    /// <summary>A resize along a screen direction.</summary>
    public static CanvasCursorChoice ResizeAt(double degrees) =>
        // Normalised here rather than at each call site, so two directions that
        // are the same axis compare equal — a double arrow at 200° IS the one
        // at 20°, and a test should not have to know which the caller produced.
        new(CanvasCursorKind.Resize, ((degrees % 180) + 180) % 180);
}

/// <summary>
/// What is under the pointer, as far as "can this tool act here" is concerned.
/// </summary>
/// <remarks>
/// <para>
/// A record of plain booleans rather than the document itself, so the mapping
/// below cannot reach for anything else and cannot be tested only against a
/// fully built scene. Every field is a question the app can actually answer at
/// pointer-move time.
/// </para>
/// <para>
/// <b>Every field is named for the obstacle, so <c>default</c> is an ordinary
/// paintable place.</b> That is a correctness requirement rather than a style
/// choice: a record struct's primary-constructor defaults do not run for
/// <c>new CanvasTarget()</c> or for any zero-initialised field, so a
/// <c>LayerVisible = true</c> would silently arrive as <c>false</c> and refuse
/// every tool everywhere. Written the other way round the zero value is the
/// permissive one, and there is nothing to get wrong.
/// </para>
/// </remarks>
public readonly record struct CanvasTarget(
    bool LayerHidden = false,
    bool LayerLocked = false,
    bool AlphaLocked = false,
    bool NothingUnderPointer = false,
    bool OutsideSelection = false);

/// <summary>
/// One place that decides what the pointer shows, and why.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal is the half that matters.</b> Painting on a hidden layer, a
/// locked layer, an alpha-locked layer with nothing under the brush, or outside
/// a selection all currently do nothing and say nothing — and silence is
/// indistinguishable from a broken application. This turns "it is not working"
/// into "it will not do that here", which an artist can act on. B2's lesson as
/// a rule: <em>even refusing would be better than nothing.</em>
/// </para>
/// <para>
/// <b>Why one function rather than a branch in the canvas control.</b> The
/// cursor and the status line have to agree, and so does whatever eventually
/// decides to drop the pointer event. Three places deciding independently is
/// three chances to say a stroke will land and then swallow it — the worst
/// version of this bug, because the pointer promised.
/// </para>
/// <para>
/// Pure, so <c>CanvasCursorTests</c> needs no window: tool in, booleans in,
/// answer out.
/// </para>
/// </remarks>
public static class CanvasCursor
{
    /// <summary>
    /// Whether a tool puts marks on the active layer, and so cares whether that
    /// layer will accept them.
    /// </summary>
    /// <remarks>
    /// The picker is the interesting exclusion: it reads pixels rather than
    /// writing them, so a locked layer does not stop it. The two arrows, the
    /// move tool and the select tools act on records and regions rather than on
    /// the layer's pixels, so they do not consult it either.
    /// </remarks>
    public static bool Paints(ToolId tool) => tool switch
    {
        ToolId.Brush or ToolId.Eraser or ToolId.Fill or ToolId.Gradient
            or ToolId.Shape or ToolId.Pen => true,
        _ => false,
    };

    /// <summary>
    /// The tool a gesture will actually use, once the held keys are counted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ctrl over a paint or fill tool is a held eyedropper</b> — the canvas
    /// says so in as many words and acts on it before it looks at the tool at
    /// all: <em>"the colour you want is almost always already on the canvas, and
    /// reaching for a tool to fetch it breaks the stroke you were about to
    /// make."</em> So the pointer has to say so too, or the one modifier an
    /// artist holds mid-stroke is the one the cursor lies about.
    /// </para>
    /// <para>
    /// <b>Only this one.</b> Shift and Alt change what several tools <em>do</em>
    /// — a fill inverts, a wand adds or subtracts, a move constrains — but not
    /// which <em>kind</em> of action it is, and a cursor that changed for each
    /// would be flicker rather than information. Modelling more than the canvas
    /// actually branches on would be inventing behaviour, which is worse than
    /// showing none.
    /// </para>
    /// </remarks>
    public static ToolId Effective(ToolId tool, KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) && HeldPickerApplies(tool)
            ? ToolId.Picker
            : tool;

    /// <summary>
    /// The tools the canvas lets Ctrl turn into an eyedropper — paint and fill,
    /// matching <c>CanvasToolMode.Paint or CanvasToolMode.Fill</c> exactly.
    /// </summary>
    private static bool HeldPickerApplies(ToolId tool) => tool switch
    {
        ToolId.Brush or ToolId.Eraser or ToolId.Fill => true,
        _ => false,
    };

    /// <summary>The cursor for a tool over a place, with the keys held.</summary>
    /// <remarks>
    /// The modifiers are resolved into the effective tool <em>first</em>, which
    /// is what makes the refusals follow: holding Ctrl over a locked layer stops
    /// forbidding, because picking a colour off a locked layer is allowed and
    /// always was.
    /// </remarks>
    public static CanvasCursorKind For(
        ToolId tool, CanvasTarget target, KeyModifiers modifiers = KeyModifiers.None)
    {
        var effective = Effective(tool, modifiers);
        return Refusal(effective, target) is not null
            ? CanvasCursorKind.Forbidden
            : Armed(effective);
    }

    /// <summary>The cursor a tool wears when it can act.</summary>
    public static CanvasCursorKind Armed(ToolId tool) => tool switch
    {
        ToolId.Brush or ToolId.Eraser => CanvasCursorKind.Paint,
        ToolId.Picker => CanvasCursorKind.Pick,
        ToolId.Fill or ToolId.Gradient => CanvasCursorKind.Fill,
        ToolId.Move => CanvasCursorKind.Move,
        ToolId.Pen or ToolId.Shape or ToolId.Select or ToolId.Width => CanvasCursorKind.Precise,
        // Bones are records picked and dragged, the arrows' kind of act.
        ToolId.Arrow or ToolId.DirectSelect or ToolId.Bone => CanvasCursorKind.PickRecords,
        _ => CanvasCursorKind.Default,
    };

    /// <summary>
    /// What a press with the Bone tool would do where the pointer is, said as
    /// a cursor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The Bone tool is the one tool whose gesture changes under the
    /// pointer</b>, and it was reported exactly that way: "I am able to move
    /// and rotate them, but it is not clear when that happens." Everything
    /// else in the rail does one thing wherever you press, so a per-tool
    /// cursor was enough; here the same press moves a joint, turns a limb or
    /// starts a new bone depending on a few pixels and a mode.
    /// </para>
    /// <para>
    /// Pure, and separate from <see cref="Armed"/>, because it answers a
    /// different question — not "what tool is in hand" but "what would happen
    /// here". <c>ArmatureOverlay.Hit</c> supplies the grab, so the cursor and
    /// the gesture read the same hit-test and cannot disagree.
    /// </para>
    /// </remarks>
    /// <param name="posing">Posing rotates whatever it grabs; binding edits the skeleton.</param>
    /// <param name="weightPainting">The weight brush is armed, and it is a brush like any other.</param>
    public static CanvasCursorKind ForBone(BoneGrab grab, bool posing, bool weightPainting = false)
    {
        // The brush wins outright: while it is armed a press paints weights
        // wherever it lands, bone or no bone, so the pointer must be the ring
        // the artist is about to paint with rather than a promise about bones.
        if (weightPainting) return CanvasCursorKind.Paint;

        return grab switch
        {
            // Nothing under the pointer: a drag here starts a new bone, which
            // is a placing gesture like the pen's.
            BoneGrab.None => posing ? CanvasCursorKind.PickRecords : CanvasCursorKind.Precise,
            // Posing turns whatever it takes hold of.
            _ when posing => CanvasCursorKind.Rotate,
            // Binding: the shaft and the joint move the bone, the tip re-aims it.
            BoneGrab.Tip => CanvasCursorKind.Rotate,
            _ => CanvasCursorKind.Move,
        };
    }

    /// <summary>
    /// Why this tool will do nothing here, or null when it will do something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The order is the message.</b> A hidden, locked layer is reported as
    /// hidden, because that is the one the artist has to fix first and because
    /// unlocking it would still leave nothing visible. Reporting the innermost
    /// reason — "nothing under the brush" on a hidden layer — is technically
    /// true and useless.
    /// </para>
    /// <para>
    /// Written as a sentence an artist reads rather than a code a developer
    /// looks up: this string is meant for the status line beside the cursor.
    /// </para>
    /// </remarks>
    public static string? Refusal(
        ToolId tool, CanvasTarget target, KeyModifiers modifiers = KeyModifiers.None)
    {
        tool = Effective(tool, modifiers);
        if (!Paints(tool)) return null;

        if (target.LayerHidden) return "This layer is hidden — turn its eye back on to draw here.";
        if (target.LayerLocked) return "This layer is locked.";
        if (target.OutsideSelection) return "Outside the selection.";
        if (target.AlphaLocked && target.NothingUnderPointer)
        {
            return "Alpha lock is on, and there is no paint here to draw on.";
        }
        return null;
    }
}
