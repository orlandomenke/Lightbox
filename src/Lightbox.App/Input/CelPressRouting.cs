using Lightbox.App.ViewModels;

namespace Lightbox.App.Input;

/// <summary>What a left press on an X-sheet cel is the start of.</summary>
public enum CelPress
{
    /// <summary>Nothing: a click, if it turns out to be one.</summary>
    None,

    /// <summary>A drag from here carries the drawing along its row (<see cref="CelDragGesture"/>).</summary>
    Move,

    /// <summary>A drag from here sweeps a block into the selection (<see cref="CelBlockSelectGesture"/>).</summary>
    Select,
}

/// <summary>
/// Which of the two drags a press on a cel arms. Exactly one, so they can never
/// both claim it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cel decides, not a modifier.</b> A drawing is something to pick up,
/// so a drag from one carries it; an empty cel has nothing to carry, so a drag
/// from one sweeps a block. Q207 had it the other way about — the plain drag
/// always selected and the move needed Alt — which made retiming by hand, the
/// thing an exposure sheet is for, a two-handed gesture.
/// </para>
/// <para>
/// What that costs is a block that <em>starts</em> on a drawing: it is made
/// with Shift+click, or swept from an empty cel. A press-and-hold timer would
/// have kept both on every cel and was not taken, because a held press is how
/// a pen right-clicks (B8) and the timer would have fought the context menu.
/// </para>
/// <para>
/// Alt still arms the move on a drawing, so the hand that learned Q207's
/// gesture is not punished; on an empty cel it arms nothing, as before.
/// </para>
/// </remarks>
public static class CelPressRouting
{
    public static CelPress For(FrameCell cell, bool alt)
    {
        // The hatch past the scene's end: nothing to carry, no cel to start from.
        if (cell.IsVirtual) return CelPress.None;
        if (cell.IsKeyed) return CelPress.Move;
        return alt ? CelPress.None : CelPress.Select;
    }
}
