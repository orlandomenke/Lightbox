using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Where the artist was in a document: the frame and layer they were on, which
/// reference they had selected, how the canvas was framed, and what they had
/// selected.
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

    /// <summary>
    /// The selection outline, remembered while another tab is active, or null
    /// when nothing is selected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B171.</b> This was the one piece of per-document state living in the
    /// view model instead of here, and the symptom was not "the ants are still
    /// up" — it was <em>the new document will not paint</em>, because a
    /// selection clips every stroke and this one described a canvas that was no
    /// longer on screen. Contours are in document coordinates, so carrying them
    /// to a differently sized document is not merely stale, it is nonsense.
    /// </para>
    /// <para>
    /// Remembered rather than discarded, for the reason <see cref="View"/> is:
    /// a selection is work, and coming back to a tab to find it gone is the
    /// same loss as coming back to find the framing reset. The leak and the
    /// memory are the same fix — state that belongs to a document cannot be
    /// carried to another one once it has somewhere of its own to live.
    /// </para>
    /// </remarks>
    public List<List<StrokePoint>>? Selection;
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
