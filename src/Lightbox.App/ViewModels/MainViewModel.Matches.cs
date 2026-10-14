using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>One piece of a key in the Match lines overlay: its points, which key (0 for A, 1 for B), the match it is in, and whether it is in the match being made.</summary>
/// <param name="Solid">The artist's word on the line's match - solid object or bending - or null where the engine decides.</param>
public sealed record LineMatchLine(IReadOnlyList<StrokePoint> Points, int Key, int? Match, bool Pending, bool? Solid = null);

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

    /// <summary>
    /// The gap being matched, held by what it is rather than where it sat: the
    /// document's editor, the layer, and the ids of its two drawings. A cel
    /// inserted before it moves it along the sheet without moving the match onto
    /// another drawing, and another document ends the mode (the adversary, Q271).
    /// </summary>
    private (object Editor, string Layer, string A, string B)? _matchHeld;

    /// <summary>Set while a match is being saved, so the save's own document change does not close the open match.</summary>
    private bool _savingMatches;

    /// <summary>Where the held gap is now — layer, and the cels of its two drawings — or null when it is gone.</summary>
    private (string Layer, int A, int B)? CurrentGap()
    {
        if (_matchHeld is not { } held || !ReferenceEquals(held.Editor, _editor)) return null;
        var layer = Scene.Layers.FirstOrDefault(l => l.Id == held.Layer);
        if (layer is null) return null;
        int a = -1, b = -1;
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (layer.Cels[i].Frame?.Id == held.A && a < 0) a = i;
            else if (layer.Cels[i].Frame?.Id == held.B && a >= 0) { b = i; break; }
        }
        return a >= 0 && b > a ? (held.Layer, a, b) : null;
    }

    /// <summary>
    /// The document changed under the mode: an undo or a redo closes the open
    /// match (what it was opened against may be gone), and a gap that no longer
    /// exists — its drawings deleted, another document — ends the mode.
    /// </summary>
    private void LineMatchDocumentChanged()
    {
        if (!_savingMatches) _pending = null;
        if (CurrentGap() is null)
        {
            LineMatchMode = false;
            return;
        }
        RefreshLineMatch();
    }

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
        _matchHeld = null;
        if (value && MatchGapAt(CurrentFrameIndex) is not { } gap)
        {
            AiStatus = "Match lines needs a key before and after the playhead, with a gap between them.";
            LineMatchMode = false;
            return;
        }
        if (value && MatchGapAt(CurrentFrameIndex) is { } found)
        {
            var layer = Scene.Layers.First(l => l.Id == found.Layer);
            _matchHeld = (_editor, found.Layer, layer.Cels[found.A].Frame!.Id, layer.Cels[found.B].Frame!.Id);
        }
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
        if (CurrentGap() is not { } gap || Gap(gap) is not { } frames)
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
            else if (owner >= 0)
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
        if (SameMatches(layer.Cels[gap.A].Frame!.Matches, saved))
        {
            RefreshLineMatch();
            return;
        }
        // Only what the engine drew and nobody changed is redrawn: an inbetween
        // the artist drew or touched up, or one the AI drew, differs from what
        // the engine gives for the matches as they were, and is kept (W4).
        var asItWas = Redrawn(layer, gap.A, i => layer.Cels[i].Frame!.Matches).ToDictionary(r => r.Cel, r => r.Frame);
        var all = Redrawn(layer, gap.A, i => i == gap.A ? saved : layer.Cels[i].Frame!.Matches);
        var replacements = all
            .Where(r => asItWas.TryGetValue(r.Cel, out var engine) && EngineDrawn(layer.Cels[r.Cel].Frame!, engine))
            .ToList();
        var kept = all.Count - replacements.Count;
        AiStatus = kept == 0
            ? ""
            : $"{Plural(kept, "inbetween")} you changed {(kept == 1 ? "was" : "were")} left as {(kept == 1 ? "it is" : "they are")} - Redraw inbetweens replaces them too.";
        _savingMatches = true;
        try
        {
            _editor.Perform(doc =>
            {
                var target = doc.Scene.Layers.First(l => l.Id == gap.Layer);
                target.Cels[gap.A].Frame!.Matches = saved?.Select(m => m.Clone()).ToList();
                foreach (var (cel, frame) in replacements) target.Cels[cel].Frame = frame;
            }, "Match lines");
        }
        finally
        {
            _savingMatches = false;
        }
        RefreshLineMatch();
    }

    /// <summary>Two match lists that say the same thing, line for line.</summary>
    private static bool SameMatches(IReadOnlyList<LineMatch>? a, IReadOnlyList<LineMatch>? b)
    {
        if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
        for (var i = 0; i < (a?.Count ?? 0); i++)
        {
            if (!a![i].A.SequenceEqual(b![i].A) || !a[i].B.SequenceEqual(b[i].B) || a[i].Solid != b[i].Solid) return false;
        }
        return true;
    }

    /// <summary>
    /// Whether <paramref name="current"/> is exactly what the engine drew -
    /// <paramref name="engine"/>, the engine's drawing for the same cel - with
    /// nothing added: same strokes, same points, no pixels, no AI marking.
    /// </summary>
    private static bool EngineDrawn(Frame current, Frame engine)
    {
        if (current.PngBase64 is not null || current.Ai is not null || current.Placements is { Count: > 0 }) return false;
        if (current.Strokes.Count != engine.Strokes.Count) return false;
        for (var i = 0; i < current.Strokes.Count; i++)
        {
            var (p, q) = (current.Strokes[i].Points, engine.Strokes[i].Points);
            if (p.Count != q.Count) return false;
            for (var k = 0; k < p.Count; k++)
            {
                if (Math.Abs(p[k].X - q[k].X) > 1e-3 || Math.Abs(p[k].Y - q[k].Y) > 1e-3) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// A right-click while matching: the match of the line under it switches
    /// between one solid object and lines that bend (Q271). A line in no match
    /// is left alone.
    /// </summary>
    public void LineMatchToggleSolid(double x, double y, double reach)
    {
        if (CurrentGap() is not { } gap || Gap(gap) is not { } frames)
        {
            LineMatchMode = false;
            return;
        }
        var point = new StrokePoint(x, y, 0);
        var (key, piece) = Nearest(_piecesA, point, reach) is { } a ? (0, a) : (1, Nearest(_piecesB, point, reach));
        if (piece is null) return;
        var matches = (frames.A.Matches ?? []).Select(m => m.Clone()).ToList();
        var reference = new PieceRef(piece.Source, piece.Ordinal);
        var owner = matches.FindIndex(m => (key == 0 ? m.A : m.B).Contains(reference));
        if (owner < 0) return;
        var match = matches[owner];
        var solid = match.Solid ?? MatchedInbetween.SolidByDefault(Side(_piecesA, match.A), Side(_piecesB, match.B));
        match.Solid = !solid;
        var pending = _pending;
        SaveMatches(gap, matches);
        _pending = pending;
    }

    private static List<Stroke> Side(List<LinePiece> pieces, List<PieceRef> refs) =>
        [.. pieces.Where(p => refs.Contains(new PieceRef(p.Source, p.Ordinal))).Select(p => p.Stroke)];

    /// <summary>Rebuild the pieces and the overlay from the document; clears both outside the mode.</summary>
    private void RefreshLineMatch()
    {
        if (!LineMatchMode || CurrentGap() is not { } gap || Gap(gap) is not { } frames)
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
        bool? SolidOf(LinePiece p, int key) => MatchOf(p, key) is { } i ? matches[i].Solid : null;
        bool Pending(LinePiece p, int key) =>
            _pending is not null && (key == 0 ? _pending.A : _pending.B).Contains(new PieceRef(p.Source, p.Ordinal));
        LineMatchView = new LineMatchOverlay(
        [
            .. _piecesA.Select(p => new LineMatchLine(p.Stroke.Points, 0, MatchOf(p, 0), Pending(p, 0), SolidOf(p, 0))),
            .. _piecesB.Select(p => new LineMatchLine(p.Stroke.Points, 1, MatchOf(p, 1), Pending(p, 1), SolidOf(p, 1))),
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
