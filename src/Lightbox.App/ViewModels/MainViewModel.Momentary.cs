using Avalonia.Input;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Hold a modifier, borrow another tool; let go, and the one you had comes back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gesture an artist reaches for without thinking</b>: the colour you
/// want is almost always already on the canvas, and going to fetch it breaks the
/// stroke you were about to make. Photoshop, Krita and Procreate all do this,
/// and every one of them restores the tool on release rather than leaving you
/// somewhere new.
/// </para>
/// <para>
/// <b>A table rather than a special case, because Ctrl was already half-built
/// and invisible.</b> The canvas has picked a colour on a Ctrl-click since the
/// beginning — but only on the press, only while painting or filling, with no
/// cursor change and nothing switching back, so it worked and looked like
/// nothing. This makes it the actual tool, which is what makes it legible: the
/// rail highlights, the cursor changes, and letting go puts it all back.
/// </para>
/// <para>
/// <b>What is deliberately NOT in the table, and why the table is a table.</b>
/// Switching tools has consequences — <c>OnActiveToolChanged</c> finishes a pen
/// path, ends an isolation session and drops the line selection. Borrowing a
/// tool must never do those, so a tool with modal state in flight cannot be
/// borrowed <em>from</em>: holding Ctrl mid-path would otherwise commit the path
/// you were still drawing. The pen, both arrows and the width tool are absent
/// for that reason rather than by oversight. Move is absent for a different one:
/// Ctrl already means "the whole layer" while dragging with it, and a modifier
/// cannot mean two things on one tool.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The tool to come back to, or null when nothing is borrowed.</summary>
    private ToolId? _borrowedFrom;

    /// <summary>Whether a modifier is currently standing in for a tool.</summary>
    public bool IsBorrowingTool => _borrowedFrom is not null;

    /// <summary>
    /// The tool a held modifier stands in for, or null when it stands in for
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Pure and static so the whole scheme is one readable table that a test can
    /// hold still. Exact equality on the modifiers rather than
    /// <c>HasFlag</c>: Ctrl+Shift is a different gesture from Ctrl, and treating
    /// it as Ctrl would fire the eyedropper in the middle of a constrained
    /// drag.
    /// </remarks>
    public static ToolId? BorrowedFor(ToolId active, KeyModifiers held) => (active, held) switch
    {
        (ToolId.Brush or ToolId.Eraser or ToolId.Fill or ToolId.Shape or ToolId.Gradient,
            KeyModifiers.Control) => ToolId.Picker,
        _ => null,
    };

    /// <summary>
    /// Bring the tool into line with the modifiers currently held down.
    /// </summary>
    /// <remarks>
    /// Called from both key down and key up with the whole modifier set, rather
    /// than from two handlers that each know half the story. A press and a
    /// release are the same question — <em>what should the tool be now</em> —
    /// and answering it in one place is what stops a modifier getting stuck
    /// down when a key-up goes missing (an alt-tab away and back is enough).
    /// </remarks>
    public void ApplyHeldModifiers(KeyModifiers held)
    {
        if (_borrowedFrom is { } owner)
        {
            // Still the same borrow: nothing to do, and returning early keeps
            // this idempotent under key repeat, which fires continuously.
            if (BorrowedFor(owner, held) == ActiveTool) return;

            _borrowedFrom = null;
            SetToolWithoutSideEffects(owner);
        }

        if (BorrowedFor(ActiveTool, held) is { } borrowed)
        {
            _borrowedFrom = ActiveTool;
            SetToolWithoutSideEffects(borrowed);
        }
    }

    /// <summary>
    /// Switch the tool for a borrow, skipping what a deliberate switch does.
    /// </summary>
    /// <remarks>
    /// <b>A borrow is not a decision.</b> An artist holding Ctrl has not chosen
    /// to leave the brush, so the things that make leaving a tool meaningful —
    /// dropping the line selection, finishing a pen path, ending isolation —
    /// must not happen and must not happen again on the way back. The table
    /// above keeps every borrowable tool free of that state anyway; this is the
    /// belt to its braces, and it is what makes adding a row to the table safe
    /// rather than a thing to reason about each time.
    /// </remarks>
    private void SetToolWithoutSideEffects(ToolId tool)
    {
        if (ActiveTool == tool) return;
        _suppressToolSideEffects = true;
        try
        {
            ActiveTool = tool;
        }
        finally
        {
            _suppressToolSideEffects = false;
        }
    }

    private bool _suppressToolSideEffects;
}
