namespace Lightbox.App.ViewModels;

/// <summary>
/// Where the artist was in a document: the frame and layer they were on, which
/// reference they had selected, and how the canvas was framed.
/// </summary>
/// <remarks>
/// <para>
/// <b>B67.</b> Kept on the <see cref="DocumentTab"/> and swapped on every tab
/// change, so coming back to a drawing finds it as it was left rather than
/// wherever the last document happened to leave the shared controls.
/// </para>
/// <para>
/// <b>What is deliberately not here is the more interesting half.</b> The brush
/// is absent because <b>Q9 answered it and answered it against per-document</b>:
/// the store is <c>ProjectManifest.Brush</c>, chosen per project type, and with
/// no project open the effective scope is Global. Per-document was the original
/// proposal and was rejected the same day, on the grounds that it "fixes the
/// page you already drew and leaves the next one starting from whatever you
/// last used elsewhere". Adding a brush field here would reinstate the rejected
/// design under a second name, which is the failure mode
/// <c>BlendOrNormal</c> is remembered for.
/// </para>
/// <para>
/// The active <em>tool</em> is absent for a different reason, and a weaker one:
/// a tool is a mode of the hand rather than a property of the drawing, and
/// every comparable application keeps it global. If that turns out to be wrong
/// it is one field and one line in <c>Adopt</c>.
/// </para>
/// </remarks>
public sealed class DocumentScopedState
{
    /// <summary>The playhead, remembered while another tab is active.</summary>
    public int FrameIndex;

    /// <summary>The selected layer.</summary>
    public int LayerIndex;

    /// <summary>
    /// Which reference strip was selected.
    /// </summary>
    /// <remarks>
    /// Shared, this was worse than untidy: the index is bounds-checked against
    /// the active document's strips, so a document holding one reference showed
    /// <em>none</em> after visiting a document where strip 2 was selected. The
    /// reference was there and invisible.
    /// </remarks>
    public int ReferenceIndex;

    /// <summary>
    /// How the canvas was framed, or null for a document nobody has framed yet.
    /// </summary>
    /// <remarks>
    /// Null rather than a default value, so "never looked at" and "looked at
    /// and left at 100%" stay distinguishable — the first opens fitted, the
    /// second is restored verbatim.
    /// </remarks>
    public CanvasViewState? View;
}

/// <summary>The canvas view transform, as a value that can be put down and picked up.</summary>
/// <remarks>
/// Invariant 5 holds: this is view-only. It lives on a
/// <see cref="DocumentTab"/> — a view model — never on the <c>Doc</c>, and
/// nothing serializes it. Reopening a file next session opens it fitted, which
/// is the honest behaviour for state the record does not carry.
/// </remarks>
public readonly record struct CanvasViewState(
    double Zoom,
    double RotationDeg,
    bool Mirrored,
    double PanX,
    double PanY);
