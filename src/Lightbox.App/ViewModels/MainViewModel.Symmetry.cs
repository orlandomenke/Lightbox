using Lightbox.App.Rendering;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// The symmetry <em>controls</em>: the toggle, the order and mirror fields, and
/// the gizmo's drags. <c>ActiveSymmetry</c> itself — the axis the next stroke is
/// cloned from — stays in <c>MainViewModel.Painting.cs</c> beside
/// <c>BeginStroke</c>, which is the one place that reads it. Every field this
/// file uses is declared here or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- symmetry controls ----------------------------------------------------

    /// <summary>The most rotational copies the fields offer — the record's own ceiling, so the field and the engine agree.</summary>
    public const int MaxSymmetryOrder = SymmetryAxis.MaxOrder;

    /// <summary>
    /// Fired when the axis appears, disappears, moves or turns — the canvas
    /// redraws its gizmo from this, the way it redraws the camera frame from
    /// <see cref="CameraChanged"/>.
    /// </summary>
    public event Action? SymmetryChanged;

    /// <summary>What the canvas was last told, so a document edit that left the axis alone costs no repaint.</summary>
    private SymmetryAxis? _symmetryShown;

    /// <summary>The axis as it was when a gizmo drag began, for the one undo step the drag becomes.</summary>
    private SymmetryAxis? _symmetryDragBefore;

    /// <summary>
    /// Whether the artist has asked for symmetry with the toggle, as distinct
    /// from what is in effect. While this is set, <see cref="ActiveSymmetry"/>
    /// is kept equal to the scene's axis — through tab switches, document
    /// replacement and every undo — and reads as off only when the current
    /// scene has no axis. While it is clear, the view model leaves
    /// <see cref="ActiveSymmetry"/> alone, which is what lets a test set an
    /// axis directly without the scene knowing.
    /// </summary>
    private bool _symmetryWanted;

    /// <summary>
    /// Whether the next stroke is painted under the scene's axis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Session state, like snapping is for guides.</b> The axis itself lives
    /// on the scene (<see cref="Scene.Symmetry"/>) so a reopened document finds
    /// it where it was left; whether it is <em>in use</em> is not saved, so
    /// reopening never starts reflecting marks the artist has not asked to
    /// reflect. Turning this on with no axis on the scene places one at the
    /// centre of the page, vertical, as the plain left/right mirror character
    /// design reaches for first — and that placement is an undo step, because
    /// it is a change to the document.
    /// </para>
    /// <para>
    /// Turning it off leaves the axis on the scene. The strokes already made
    /// keep their own copy of it (Q15), so nothing on the canvas changes.
    /// </para>
    /// </remarks>
    public bool SymmetryEnabled
    {
        get => ActiveSymmetry is not null;
        set
        {
            _symmetryWanted = value;
            if (!value)
            {
                ActiveSymmetry = null;
                return;
            }

            if (Scene.Symmetry is null)
            {
                var axis = new SymmetryAxis
                {
                    CenterX = Scene.Left + Scene.Width / 2.0,
                    CenterY = Scene.Top + Scene.Height / 2.0,
                    AngleDeg = 90,
                    Order = 1,
                    Mirror = true,
                };
                // The delta closes over the axis it places and nothing else;
                // undoing it finds the scene through the document it is handed,
                // so a snapshot undo in between cannot leave it writing into a
                // scene that is no longer the document's.
                _editor.PerformDelta(
                    d => d.Scene.Symmetry = axis, d => d.Scene.Symmetry = null, label: "Place symmetry axis");
            }

            ResyncSymmetry();
        }
    }

    /// <summary>How many rotational copies, the drawn mark included. 1 is no turning.</summary>
    public int SymmetryOrder
    {
        get => Scene.Symmetry?.Order ?? 1;
        set => EditSymmetry(a => a.Order = Math.Clamp(value, 1, MaxSymmetryOrder), "Symmetry order");
    }

    /// <summary>Whether each copy is reflected as well as turned.</summary>
    public bool SymmetryMirror
    {
        get => Scene.Symmetry?.Mirror ?? true;
        set => EditSymmetry(a => a.Mirror = value, "Symmetry mirror");
    }

    /// <summary>The axis angle in degrees; 90 is the vertical mirror line.</summary>
    public double SymmetryAngleDeg
    {
        get => Scene.Symmetry?.AngleDeg ?? 90;
        set => EditSymmetry(a => a.AngleDeg = Normalise(value), "Symmetry angle");
    }

    /// <summary>
    /// The copies the current axis makes, for the bar's label — "×2" beside
    /// the toggle says what the next stroke will cost without opening anything.
    /// </summary>
    public int SymmetryCopyCount => ActiveSymmetry?.CopyCount ?? 1;

    /// <summary>
    /// Apply one edit to the scene's axis as one undo step, in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In place, so <see cref="ActiveSymmetry"/> keeps pointing at the same
    /// object and the next stroke sees the edit; the before and after are
    /// copied in and out rather than the object swapped. A stroke already
    /// begun is unaffected either way — it took its clone at
    /// <c>BeginStroke</c>.
    /// </para>
    /// <para>
    /// <b>The step finds the axis through the document it is handed, never
    /// through a captured reference.</b> A snapshot step (adding a layer, say)
    /// undoes by swapping the whole <c>Doc</c> for a clone, and the clone's
    /// axis is a different object; a delta that had captured the old one would
    /// then write its undo into an orphan while the live axis stayed put. The
    /// adversary found exactly that sequence before this landed.
    /// </para>
    /// </remarks>
    private void EditSymmetry(Action<SymmetryAxis> edit, string label)
    {
        if (Scene.Symmetry is not { } axis) return;
        var before = axis.Clone();
        var after = axis.Clone();
        edit(after);
        if (SameAxis(before, after)) return;
        _editor.PerformDelta(d => AssignOnScene(d, after), d => AssignOnScene(d, before), label: label);
        ResyncSymmetry();
    }

    // ---- the gizmo's drags ----------------------------------------------------
    //
    // A drag mutates the scene's axis directly on every pointer event and
    // becomes ONE undo step when the pointer lifts — the weight brush's shape:
    // the apply is idempotent because the record already holds the after-state.
    // Per-event steps would make Ctrl+Z walk back through forty pointer moves.

    /// <summary>A gizmo drag is starting: remember where the axis was.</summary>
    public void BeginSymmetryDrag() => _symmetryDragBefore = Scene.Symmetry?.Clone();

    /// <summary>The centre handle moved by a document-space delta.</summary>
    public void DragSymmetryCentreBy(double dx, double dy)
    {
        if (Scene.Symmetry is not { } axis) return;
        axis.CenterX += dx;
        axis.CenterY += dy;
        NotifySymmetry();
    }

    /// <summary>
    /// The rotate handle is under document point (<paramref name="x"/>, <paramref name="y"/>);
    /// with <paramref name="snap"/>, the angle lands on the nearest 15°.
    /// </summary>
    public void DragSymmetryAngleTowards(double x, double y, bool snap)
    {
        if (Scene.Symmetry is not { } axis) return;
        axis.AngleDeg = SymmetryAxisGizmo.AngleTowards(axis, x, y, snap ? 15 : 0);
        NotifySymmetry();
    }

    /// <summary>
    /// The drag ended: one undo step from where it began to where it is.
    /// </summary>
    /// <remarks>
    /// Also what the canvas calls when it loses the pointer mid-drag — the
    /// moves already made are on the record, so they get their step rather
    /// than becoming an edit nothing can undo.
    /// </remarks>
    public void EndSymmetryDrag()
    {
        var before = _symmetryDragBefore;
        _symmetryDragBefore = null;
        if (before is null || Scene.Symmetry is not { } axis) return;
        var after = axis.Clone();
        if (SameAxis(before, after)) return;
        _editor.PerformDelta(d => AssignOnScene(d, after), d => AssignOnScene(d, before), label: "Move symmetry axis");
        ResyncSymmetry();
    }

    // ---- keeping the view honest ----------------------------------------------

    /// <summary>
    /// Make <see cref="ActiveSymmetry"/> agree with the current scene, and tell
    /// the canvas if anything it shows has moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One rule, applied after every document change and every attach: while
    /// the artist wants symmetry, the axis in use <em>is</em> the scene's axis
    /// — whatever object that is now. That covers a tab switch (the arriving
    /// document's axis, or off when it has none), <c>ReplaceDocument</c>, an
    /// undo of the placement (off, and a redo puts it back in use), and a
    /// snapshot undo of something unrelated, which swaps the scene for a clone
    /// whose axis is a different object with the same numbers. An earlier
    /// version compared references and read that last case as "the axis was
    /// undone", switching symmetry off under the artist for undoing a layer.
    /// </para>
    /// <para>
    /// Cheap on the common path: a stroke commit raises <c>Changed</c> too, and
    /// this is a reference compare and, at most, five numbers against what the
    /// canvas was last shown. While the toggle is off nothing is compared at
    /// all, and a dormant axis on the scene costs no repaint per commit.
    /// </para>
    /// </remarks>
    private void ResyncSymmetry()
    {
        if (!_symmetryWanted) return;
        var next = Scene.Symmetry;
        if (!ReferenceEquals(next, ActiveSymmetry))
        {
            ActiveSymmetry = next; // notifies through OnActiveSymmetryChanged
            return;
        }
        if (!SameAxis(_symmetryShown, next)) NotifySymmetry();
    }

    partial void OnActiveSymmetryChanged(SymmetryAxis? value) => NotifySymmetry();

    private void NotifySymmetry()
    {
        _symmetryShown = ActiveSymmetry?.Clone();
        OnPropertyChanged(nameof(SymmetryEnabled));
        OnPropertyChanged(nameof(SymmetryOrder));
        OnPropertyChanged(nameof(SymmetryMirror));
        OnPropertyChanged(nameof(SymmetryAngleDeg));
        OnPropertyChanged(nameof(SymmetryCopyCount));
        SymmetryChanged?.Invoke();
    }

    /// <summary>Copy <paramref name="source"/>'s numbers onto whatever axis <paramref name="doc"/>'s scene holds now.</summary>
    private static void AssignOnScene(Doc doc, SymmetryAxis source)
    {
        if (doc.Scene.Symmetry is not { } target) return;
        target.CenterX = source.CenterX;
        target.CenterY = source.CenterY;
        target.AngleDeg = source.AngleDeg;
        target.Order = source.Order;
        target.Mirror = source.Mirror;
    }

    private static bool SameAxis(SymmetryAxis? a, SymmetryAxis? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.CenterX == b.CenterX && a.CenterY == b.CenterY && a.AngleDeg == b.AngleDeg
               && a.Order == b.Order && a.Mirror == b.Mirror;
    }

    private static double Normalise(double deg)
    {
        if (!double.IsFinite(deg)) return 90;
        deg %= 360.0;
        return deg < 0 ? deg + 360.0 : deg;
    }
}
