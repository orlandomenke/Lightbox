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

    // ---- held letter keys (B176) -------------------------------------------------
    // The same borrow-and-restore, triggered by a key rather than a flag. A
    // modifier is a state the whole window can consult (ApplyHeldModifiers reads
    // KeyModifiers on every event); a letter held down is not — it needs the
    // press and the release delivered as calls, and it must survive key repeat,
    // which re-fires the press for as long as the key is down.

    /// <summary>The tool a held letter key will give back, or null.</summary>
    private ToolId? _momentaryFrom;

    /// <summary>When the hold began, from <see cref="MomentaryClock"/>.</summary>
    private long _momentaryHeldSince;

    /// <summary>
    /// The eraser's own size while a hold has it wearing the brush's, or null
    /// when no borrow is dressing the eraser up.
    /// </summary>
    private double? _eraserSizeBeforeBorrow;

    /// <summary>What the borrow set the eraser's size to, so a mid-hold scrub is tellable from it.</summary>
    private double _eraserSizeWhileBorrowed;

    /// <summary>Whether a letter key is currently standing in for a tool.</summary>
    public bool IsHoldingMomentaryTool => _momentaryFrom is not null;

    /// <summary>
    /// Milliseconds under which a press-and-release is a tap, and latches.
    /// </summary>
    /// <remarks>
    /// One key, both behaviours, split by duration — Photoshop's spring-loaded
    /// rule, so the muscle memory transfers: tap E and you have chosen the
    /// eraser; hold it, scrub, and let go, and you never left the brush. 300 ms
    /// is Photoshop's own threshold for the same decision.
    /// </remarks>
    public const int MomentaryTapMilliseconds = 300;

    /// <summary>Test seam: the clock the tap/hold split reads.</summary>
    public Func<long> MomentaryClock { get; set; } = () => Environment.TickCount64;

    /// <summary>
    /// A momentary key went down: borrow its tool. Idempotent under key repeat.
    /// </summary>
    public void BeginMomentaryTool(ToolId tool)
    {
        // Key repeat re-fires the press for as long as the key is down; the
        // first press is the one that started the clock.
        if (_momentaryFrom is not null) return;
        if (ActiveTool == tool) return;

        _momentaryFrom = ActiveTool;
        _momentaryHeldSince = MomentaryClock();
        BorrowEraserSize(tool);
        SetToolWithoutSideEffects(tool);
    }

    /// <summary>
    /// A held E rubs out at the size of the mark being corrected: the borrowed
    /// eraser takes the brush's size, and the release puts the eraser's own
    /// back. A tap is a choice of the eraser, so it keeps the size it was last
    /// deliberately given — this is only for the borrow.
    /// </summary>
    /// <remarks>
    /// Before the tool switch, so the notifications the switch fires read the
    /// borrowed size — the cursor ring and the size slider must not show the
    /// old number for a frame. The press cannot yet know whether it is a tap,
    /// so the size is borrowed unconditionally and every way out of the hold
    /// returns it; a tap lasting under <see cref="MomentaryTapMilliseconds"/>
    /// leaves no time to draw at the wrong size.
    /// </remarks>
    private void BorrowEraserSize(ToolId tool)
    {
        if (tool != ToolId.Eraser) return;
        _eraserSizeBeforeBorrow = _brushes.Eraser.Size;
        _eraserSizeWhileBorrowed = _brushes.Brush.Size;
        _brushes.Eraser.Size = _brushes.Brush.Size;
    }

    /// <summary>
    /// Put the eraser's own size back when a borrow ends, however it ends.
    /// </summary>
    /// <remarks>
    /// A size scrubbed mid-hold is a decision about the eraser and is kept as
    /// its new size; only the mirrored brush size is undone. The persist keeps
    /// <c>brushes.json</c> honest when a mid-hold stroke saved the borrowed
    /// size as the eraser's last.
    /// </remarks>
    private void ReturnEraserSize()
    {
        if (_eraserSizeBeforeBorrow is not { } size) return;
        _eraserSizeBeforeBorrow = null;
        if (_brushes.Eraser.Size != _eraserSizeWhileBorrowed) return;
        _brushes.Eraser.Size = size;
        PersistBrushState();
    }

    /// <summary>
    /// The key came back up: a hold restores the interrupted tool, a tap keeps
    /// the new one — the latch the key always meant.
    /// </summary>
    public void EndMomentaryTool(ToolId tool)
    {
        if (_momentaryFrom is not { } owner) return;
        _momentaryFrom = null;

        // Something else moved the tool mid-hold (a click on the rail is a
        // decision); the release has no tool left to restore — but a size the
        // borrow dressed the eraser in still comes off.
        if (ActiveTool != tool) { ReturnEraserSize(); return; }

        if (MomentaryClock() - _momentaryHeldSince >= MomentaryTapMilliseconds)
        {
            ReturnEraserSize();
            SetToolWithoutSideEffects(owner);
            return;
        }

        // A tap is the deliberate switch this key performed before it could be
        // held, so the switch's consequences happen now — they were suppressed
        // at the press because a press cannot know yet which one it is. The
        // borrowed size comes off for the same reason: a chosen eraser is the
        // eraser as last configured, and the tool stays put, so the bound
        // sliders and the cursor ring have to be told by hand.
        ReturnEraserSize();
        NotifyBrushProperties();
        LeaveToolStateBehind(tool);
    }

    /// <summary>
    /// The window lost focus mid-hold and will never see the key-up: restore.
    /// </summary>
    /// <remarks>
    /// The same reason <see cref="ApplyHeldModifiers"/> is called on
    /// <c>Deactivated</c> — a momentary tool that does not survive alt-tabbing
    /// away is a stuck mode.
    /// </remarks>
    public void CancelMomentaryTool()
    {
        if (_momentaryFrom is not { } owner) return;
        _momentaryFrom = null;
        ReturnEraserSize();
        SetToolWithoutSideEffects(owner);
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
