using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>One piece of a key in the Match lines overlay: its points, which key (0 for A, 1 for B), the match it is in, and whether it is in the match being made.</summary>
public sealed record LineMatchLine(IReadOnlyList<StrokePoint> Points, int Key, int? Match, bool Pending);

/// <summary>What the Match lines mode shows: both keys' pieces, and the onion colours an unmatched piece of each key is drawn in.</summary>
public sealed record LineMatchOverlay(IReadOnlyList<LineMatchLine> Lines, string TintA, string TintB);

/// <summary>
/// The artist's line matches (Q271): which lines of a key are which lines of
/// the next, matched on the canvas, and redrawing a run's inbetweens from them.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Whether the canvas is matching lines between the keys either side of the
    /// playhead instead of drawing. Turning it on where there is no such gap
    /// leaves it off and says why.
    /// </summary>
    [ObservableProperty]
    private bool _lineMatchMode;

    /// <summary>The overlay the canvas draws while matching; null outside the mode.</summary>
    public LineMatchOverlay? LineMatchView { get; private set; }

    /// <summary>Raised whenever <see cref="LineMatchView"/> changes, for the window to push to the canvas.</summary>
    public event Action<LineMatchOverlay?>? LineMatchChanged;

    /// <summary>The gap being matched: its layer, and the cels of its two drawings.</summary>
    private (string Layer, int A, int B)? _matchGap;

    private List<LinePiece> _piecesA = [];
    private List<LinePiece> _piecesB = [];

    /// <summary>
    /// The match being made: lines of A clicked, and the lines of B given so
    /// far. Saved only once it has a line of B — a line of A alone would be
    /// saved as a line that goes, and fade out of the inbetweens mid-click.
    /// </summary>
    private LineMatch? _pending;

    partial void OnLineMatchModeChanged(bool value)
    {
        _pending = null;
        _matchGap = null;
        if (value && MatchGapAt(CurrentFrameIndex) is not { } gap)
        {
            AiStatus = "Match lines needs a key before and after the playhead, with a gap between them.";
            LineMatchMode = false;
            return;
        }
        if (value) _matchGap = MatchGapAt(CurrentFrameIndex);
        RefreshLineMatch();
    }

    /// <summary>
    /// The drawings either side of <paramref name="frameIndex"/> that are not
    /// inbetweens — the keys and breakdowns a gap runs between — on the active
    /// layer, or null when there is no such pair.
    /// </summary>
    private (string Layer, int A, int B)? MatchGapAt(int frameIndex)
    {
        var layer = ActiveLayer;
        bool Drawn(int i) => layer.Cels[i].Frame is { Role: not FrameRole.Inbetween };
        var a = -1;
        for (var i = Math.Min(frameIndex, layer.Cels.Count - 1); i >= 0 && a < 0; i--)
        {
            if (Drawn(i)) a = i;
        }
        if (a < 0) return null;
        for (var i = a + 1; i < layer.Cels.Count; i++)
        {
            if (Drawn(i)) return (layer.Id, a, i);
        }
        return null;
    }

    /// <summary>
    /// A press on the canvas while matching, in document units, with how far
    /// from a line still counts as on it.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>A line of A starts a new match; with Shift it joins the open one.</item>
    /// <item>A line of B joins the open match, which is saved with it.</item>
    /// <item>A line already in a match that is not open leaves that match; a
    /// match left with nothing on one side is dropped.</item>
    /// <item>Empty canvas closes the open match.</item>
    /// </list>
    /// Where a press is near both keys, the open match decides: a line of B is
    /// what an open match is waiting for, and Shift asks for a line of A.
    /// </remarks>
    public void LineMatchPress(double x, double y, bool shift, double reach)
    {
        if (_matchGap is not { } gap || Gap(gap) is not { } frames)
        {
            LineMatchMode = false;
            return;
        }
        var point = new StrokePoint(x, y, 0);
        var hitA = Nearest(_piecesA, point, reach);
        var hitB = Nearest(_piecesB, point, reach);
        var preferB = _pending is not null && !shift;
        var (key, piece) = (hitA, hitB) switch
        {
            ({ } a, { } b) => preferB ? (1, b) : (0, a),
            ({ } a, null) => (0, a),
            (null, { } b) => (1, b),
            _ => (-1, (LinePiece?)null),
        };
        if (piece is null)
        {
            _pending = null;
            RefreshLineMatch();
            return;
        }

        var matches = (frames.A.Matches ?? []).Select(m => m.Clone()).ToList();
        var pendingIndex = _pending is null ? -1 : matches.FindIndex(m => SameMatch(m, _pending));
        var reference = new PieceRef(piece.Source, piece.Ordinal);
        var owner = matches.FindIndex(m => (key == 0 ? m.A : m.B).Contains(reference));

        if (key == 0)
        {
            if (shift && _pending is not null && owner < 0)
            {
                _pending.A.Add(reference);
                if (pendingIndex < 0)
                {
                    RefreshLineMatch();
                    return;
                }
                matches[pendingIndex].A.Add(reference);
            }
            else if (owner >= 0 && owner != pendingIndex)
            {
                matches[owner].A.Remove(reference);
                _pending = null;
            }
            else
            {
                _pending = new LineMatch { A = [reference] };
                RefreshLineMatch();
                return;
            }
        }
        else
        {
            if (_pending is not null)
            {
                if (owner >= 0) matches[owner].B.Remove(reference);
                if (!_pending.B.Contains(reference)) _pending.B.Add(reference);
                if (pendingIndex >= 0) matches[pendingIndex] = _pending.Clone();
                else matches.Add(_pending.Clone());
            }
            else if (owner >= 0)
            {
                matches[owner].B.Remove(reference);
            }
            else
            {
                AiStatus = "Click a line of the first key, then the lines it becomes in the second.";
                return;
            }
        }

        matches.RemoveAll(m => m.A.Count == 0 || m.B.Count == 0);
        SaveMatches(gap, matches);
    }

    /// <summary>The same match by content: its lines of A, which a match is opened by.</summary>
    private static bool SameMatch(LineMatch saved, LineMatch pending) =>
        saved.A.Count == pending.A.Count && saved.A.All(pending.A.Contains);

    /// <summary>The nearest piece within <paramref name="reach"/> of the point, or null.</summary>
    private static LinePiece? Nearest(List<LinePiece> pieces, StrokePoint point, double reach)
    {
        LinePiece? best = null;
        var bestDistance = reach;
        foreach (var piece in pieces)
        {
            var points = piece.Stroke.Points;
            for (var i = 0; i < points.Count; i++)
            {
                var d = i == 0
                    ? GeometryOps.Dist(point, points[0])
                    : GeometryOps.DistToSegment(point, points[i - 1], points[i]);
                if (d > bestDistance) continue;
                (best, bestDistance) = (piece, d);
            }
        }
        return best;
    }

    /// <summary>The gap's two drawings as they are now, or null when the sheet has moved under it.</summary>
    private (Frame A, Frame B)? Gap((string Layer, int A, int B) gap)
    {
        var layer = Scene.Layers.FirstOrDefault(l => l.Id == gap.Layer);
        if (layer is null || gap.B >= layer.Cels.Count) return null;
        return layer.Cels[gap.A].Frame is { } a && layer.Cels[gap.B].Frame is { } b ? (a, b) : null;
    }

    /// <summary>
    /// Save the gap's matches on its first drawing and redraw the run's
    /// inbetweens from them, as one undo step.
    /// </summary>
    private void SaveMatches((string Layer, int A, int B) gap, List<LineMatch> matches)
    {
        var layer = Scene.Layers.First(l => l.Id == gap.Layer);
        var saved = matches.Count == 0 ? null : matches;
        var replacements = Redrawn(layer, gap.A, i => i == gap.A ? saved : layer.Cels[i].Frame!.Matches);
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.First(l => l.Id == gap.Layer);
            target.Cels[gap.A].Frame!.Matches = saved?.Select(m => m.Clone()).ToList();
            foreach (var (cel, frame) in replacements) target.Cels[cel].Frame = frame;
        }, "Match lines");
        RefreshLineMatch();
    }

    /// <summary>Rebuild the pieces and the overlay from the document; clears both outside the mode.</summary>
    private void RefreshLineMatch()
    {
        if (!LineMatchMode || _matchGap is not { } gap || Gap(gap) is not { } frames)
        {
            _piecesA = [];
            _piecesB = [];
            LineMatchView = null;
            LineMatchChanged?.Invoke(null);
            return;
        }
        _piecesA = LinePieces.Cut(StrokeRecordCleaner.EffectiveStrokes(StrokesOf(frames.A)));
        _piecesB = LinePieces.Cut(StrokeRecordCleaner.EffectiveStrokes(StrokesOf(frames.B)));
        var matches = frames.A.Matches ?? [];
        int? MatchOf(LinePiece p, int key)
        {
            var r = new PieceRef(p.Source, p.Ordinal);
            var i = matches.FindIndex(m => (key == 0 ? m.A : m.B).Contains(r));
            return i < 0 ? null : i;
        }
        bool Pending(LinePiece p, int key) =>
            _pending is not null && (key == 0 ? _pending.A : _pending.B).Contains(new PieceRef(p.Source, p.Ordinal));
        LineMatchView = new LineMatchOverlay(
        [
            .. _piecesA.Select(p => new LineMatchLine(p.Stroke.Points, 0, MatchOf(p, 0), Pending(p, 0))),
            .. _piecesB.Select(p => new LineMatchLine(p.Stroke.Points, 1, MatchOf(p, 1), Pending(p, 1))),
        ], Onion.PreviousTint, Onion.NextTint);
        LineMatchChanged?.Invoke(LineMatchView);
    }

    /// <summary>
    /// Redraw the inbetweens of the run under the playhead from its keys,
    /// breakdowns and line matches as they are now, in place and in one undo
    /// step.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it is not ＋ Inbetween again.</b> A run counts every drawing on
    /// the sheet as a stop, inbetweens included, so asking ＋ Inbetween a second
    /// time fills between the inbetweens rather than redrawing them. This takes
    /// the run's keys and breakdowns as the stops and gives each cel that holds
    /// an inbetween a new drawing at the same time.
    /// </para>
    /// <para>
    /// <b>What it overwrites.</b> Every inbetween in the run, including one the
    /// artist has touched up since: nothing yet records a touch-up. Undo brings
    /// it back.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void RedrawInbetweens()
    {
        var layer = ActiveLayer;
        var replacements = Redrawn(layer, CurrentFrameIndex, i => layer.Cels[i].Frame!.Matches);
        if (replacements.Count == 0) return;
        var layerId = layer.Id;
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.First(l => l.Id == layerId);
            foreach (var (cel, frame) in replacements) target.Cels[cel].Frame = frame;
        }, "Redraw inbetweens");
    }

    /// <summary>
    /// New drawings for every inbetween of the run at <paramref name="frameIndex"/>,
    /// with each drawing's matches read through <paramref name="matchesAt"/>.
    /// </summary>
    /// <remarks>
    /// A charted run (Q58) puts its inbetweens on the chart's rungs, so the
    /// rungs are reused, in order, when there is one for each inbetween;
    /// otherwise each inbetween's time is where its cel sits in the run,
    /// through the bar's easing — what ＋ Inbetween gave it.
    /// </remarks>
    private List<(int Cel, Frame Frame)> Redrawn(Layer layer, int frameIndex, Func<int, IReadOnlyList<LineMatch>?> matchesAt)
    {
        var run = ExposureSheet.RunAt(layer, frameIndex);
        if (run.Count < 3) return [];
        var aIndex = run[0];
        var span = (double)(run[^1] - aIndex);
        var targets = run.Where(i => layer.Cels[i].Frame!.Role == FrameRole.Inbetween).ToList();
        var stops = run
            .Where(i => layer.Cels[i].Frame!.Role != FrameRole.Inbetween)
            .Select(i => new Inbetweener.RunStop((i - aIndex) / span, StrokesOf(layer.Cels[i].Frame!), matchesAt(i)))
            .ToList();
        if (targets.Count == 0 || stops.Count < 2) return [];

        var rungs = layer.Cels[aIndex].Frame!.Chart?
            .Where(rung => !stops.Any(s => Math.Abs(s.Time - rung) <= RungEpsilon))
            .Order()
            .ToList();
        var charted = rungs is not null && rungs.Count == targets.Count;
        var times = charted ? rungs! : [.. targets.Select(i => (i - aIndex) / span)];
        var drawn = Inbetweener.InbetweenRun(stops, times, charted ? Easing.Linear : TweenEasing);
        return [.. targets.Select((cel, k) => (cel, NewFrameFor(layer, drawn[k], FrameRole.Inbetween)))];
    }
}
