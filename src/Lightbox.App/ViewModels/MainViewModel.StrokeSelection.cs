using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// Picking whole strokes — the black-arrow Select tool's document side.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately separate from the pixel selection.</b> The marquee, lasso and
/// wand select an <em>area</em>, which becomes a clip region a stroke is painted
/// under. This selects <em>lines</em>, which are records you can then move,
/// recolour or delete. Q48 chose two tools that look different and do visibly
/// different things over one tool whose click means two things depending on what
/// happens to be underneath it — the ambiguity that makes Krita's shape
/// selection feel unpredictable.
/// </para>
/// <para>
/// Everything here is drivable without a window, which is the point: synthetic
/// pointer input through Xvfb is unreliable in this environment, so a gesture
/// that exists only in an event handler is one nothing can check.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>The strokes a pick can reach: the keyed frame at or before the playhead.</summary>
    /// <remarks>
    /// <see cref="PaintTarget"/> rather than <c>PaintTargetOrKey</c> — picking
    /// must never bring a cel into existence. Clicking about is how an artist
    /// looks around, and looking around should not author anything.
    /// </remarks>
    private List<Stroke> PickableStrokes() =>
        PaintTarget() is { } frame ? StrokesOf(frame) : [];

    /// <summary>Every selected stroke that still exists on the current frame.</summary>
    public IReadOnlyList<Stroke> SelectedStrokes
    {
        get
        {
            var ids = Selection.SelectedStrokeIds;
            if (ids.Count == 0) return [];
            return [.. PickableStrokes().Where(s => ids.Contains(s.Id))];
        }
    }

    /// <summary>How many lines are selected, or an empty string when none are.</summary>
    public string StrokeSelectionSummary => Selection.SelectedStrokeIds.Count switch
    {
        0 => "",
        1 => "1 line selected",
        var n => $"{n} lines selected",
    };

    /// <summary>
    /// Pick the stroke under a point. Returns true if something was selected.
    /// </summary>
    /// <param name="tolerance">
    /// Extra grab distance in document units — the caller derives it from the
    /// zoom, so a thin line stays as easy to hit when zoomed out.
    /// </param>
    /// <remarks>
    /// <b>Clicking nothing clears the selection</b>, which is what every tool
    /// with an object selection does and what a hand expects: click away to let
    /// go.
    /// </remarks>
    public bool PickStrokeAt(double x, double y, double tolerance, bool shift = false, bool alt = false)
    {
        var strokes = PickableStrokes();
        var hit = strokes.Count == 0
            ? null
            : StrokePicker.TopmostAt(strokes, IndexOf(strokes), x, y, tolerance);

        // Isolation locks everything else, and this is where that is true rather
        // than merely drawn. A click that landed on another line inside a session
        // does nothing at all — not "selects it and looks odd", nothing — which
        // is the promise the mode makes and the reason it is safe to reach into
        // geometry while it is on.
        if (hit is { } isolated && !RespondsToPicking(strokes[isolated].Id)) return false;

        if (hit is not { } position)
        {
            // Only an unmodified click means "let go". Shift-clicking empty
            // canvas while building a selection is a miss, not a reset.
            if (!shift && !alt) ClearStrokeSelection();
            return false;
        }

        // The layer gate is checked here rather than before the pick, so a click
        // on empty canvas over a locked layer stays silent. You are told the
        // layer is locked when you actually reach for something on it.
        if (!CanEdit(ActiveLayer, "select lines on it")) return false;

        // No OnStrokeSelectionChanged() here: the manager raises its own event
        // and the constructor routes it to that method (B147). Calling it as
        // well would notify twice, and — worse — would make this the pattern,
        // which is exactly how the paths that never called it got missed.
        Selection.SelectStrokeWithModifiers(strokes[position].Id, shift, alt);
        return true;
    }

    /// <summary>
    /// Pick every stroke a marquee touched. Returns how many are now selected.
    /// </summary>
    public int PickStrokesIn(SKRect region, bool add = false)
    {
        var strokes = PickableStrokes();
        if (strokes.Count == 0)
        {
            if (!add) ClearStrokeSelection();
            return 0;
        }

        var caught = StrokePicker.Within(strokes, IndexOf(strokes), region);
        if (caught.Count > 0 && !CanEdit(ActiveLayer, "select lines on it")) return 0;

        Selection.SelectStrokes(caught.Select(i => strokes[i].Id), add);
        return Selection.SelectedStrokeIds.Count;
    }

    /// <summary>Let go of every selected line.</summary>
    public void ClearStrokeSelection()
    {
        if (Selection.SelectedStrokeIds.Count == 0) return;
        Selection.SelectStrokes([]);
    }

    /// <summary>
    /// Drop selected ids that the current frame no longer holds.
    /// </summary>
    /// <remarks>
    /// Ids are stable but not eternal: undo, a delete, or stepping to another
    /// frame can all leave a selection naming lines that are not here. A
    /// selection that reports a count and draws nothing reads as the tool having
    /// broken, so it is pruned rather than left.
    /// </remarks>
    public void PruneStrokeSelection()
    {
        if (Selection.SelectedStrokeIds.Count == 0) return;
        Selection.RetainStrokes(PickableStrokes().Select(s => s.Id).ToHashSet());
    }

    /// <summary>
    /// The outlines of what is selected, for the canvas to trace.
    /// </summary>
    /// <remarks>
    /// Raised on a selection change rather than polled, so the canvas repaints
    /// once per change and never per pointer move. A fill is closed — it is a
    /// contour and its last point joins its first — and everything else is open.
    /// </remarks>
    public event Action<IReadOnlyList<(IReadOnlyList<StrokePoint> Points, bool Closed)>?>? SelectedLinesChanged;

    private void OnStrokeSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedStrokes));
        OnPropertyChanged(nameof(StrokeSelectionSummary));
        OnPropertyChanged(nameof(HasStrokeSelection));

        var selected = SelectedStrokes;
        SelectedLinesChanged?.Invoke(selected.Count == 0
            ? null
            : [.. selected.Select(s =>
                ((IReadOnlyList<StrokePoint>)s.Points, s.Tool == ToolKind.Fill))]);
    }

    /// <summary>Whether any line is selected — for enabling the actions that need one.</summary>
    public bool HasStrokeSelection => Selection.SelectedStrokeIds.Count > 0;

    /// <summary>
    /// A spatial index over the frame's strokes, rebuilt per pick.
    /// </summary>
    /// <remarks>
    /// <b>Per pick is cheap; per pointer move would not be.</b> Building the
    /// index walks every stroke's points once, which is nothing against a click
    /// and is exactly the shape invariant 6 forbids in a move handler. When a
    /// hover highlight arrives it wants a cached index invalidated on the
    /// document's revision, not this.
    /// </remarks>
    private StrokeIndex IndexOf(IReadOnlyList<Stroke> strokes) => StrokeIndex.Of(strokes);
}
