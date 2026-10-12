using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using SkiaSharp;

namespace Lightbox.Raster;

/// <summary>
/// <see cref="BrushEngine.WalkDabs(Stroke, IncrementalDensify?)"/> for a stroke
/// that is still being drawn: the dabs no new point can reach are kept, and
/// only the end of the stroke is walked again.
/// </summary>
/// <remarks>
/// <para>
/// <b>The walk was the last thing a pointer event did to the whole stroke.</b>
/// The live path asked for every dab of the mark on every event, and from the
/// structural settle rule on (<see cref="BrushEngine.SettledPrefix(IReadOnlyList{BrushEngine.Dab}, IReadOnlyList{BrushEngine.Dab}, int, out int)"/>)
/// asked twice, then compared the answers dab by dab from the first. On a
/// stroke of 3000 points that was 5.9 ms an event against 2.9 ms at 500,
/// measured, and a pen sends an event every 4 to 8 ms: past some length the
/// thread spends its time finding out where the dabs are and not showing
/// them. The owner's report was a line that stopped appearing part way and
/// came back whole at pen-up, at any speed. Work proportional to the mark on a
/// per-event path is what invariant 6 forbids, and
/// <c>DabWalkGrowsWithTheStrokeTests</c> had said in August that the fix was
/// "an incremental dab list beside the incremental densify". This is it.
/// </para>
/// <para>
/// <b>Why a prefix can be kept.</b> The walk is a running sum along the
/// densified path: what it has become after a given densified point depends on
/// the path up to there and on nothing after. <see cref="IncrementalDensify"/>
/// already knows the first span a call had to recompute, so the walk's state is
/// saved at the end of every span and a call picks up from the last span that
/// did not change. Same arithmetic in the same order, so the answer is the
/// reference walk's to the bit - <c>LiveDabWalkTests</c> compares them over
/// every prefix of strokes whose tails are rewritten the way smoothing rewrites
/// them.
/// </para>
/// <para>
/// <b>It also says what changed</b>, which the callers were finding out by
/// comparing the whole list with the last one: <see cref="Kept"/> leading dabs
/// are the very same as last time, and <see cref="Replaced"/> is what stood
/// after them. A settle rule then compares the tail and not the mark.
/// </para>
/// <para>
/// The list returned is this object's own and is changed in place by the next
/// call. A posed stroke is walked whole, as before: its walk needs no densify
/// and has no span to resume from.
/// </para>
/// </remarks>
public sealed class LiveDabWalk
{
    private readonly IncrementalDensify _densify = new();
    private readonly List<BrushEngine.Dab> _dabs = [];
    private readonly List<BrushEngine.Dab> _replaced = [];

    /// <summary>The walk as it stood after each densified span, so a call can resume there.</summary>
    private readonly List<State> _afterSpan = [];

    private BrushSettings? _brush;

    private struct State
    {
        public double Acc;
        public double Step;
        public int Fidelity;
        public StrokePoint Prev;
        public int Dabs;
        public double Heading;
        public double Travelled;
    }

    /// <summary>How many leading dabs the last call left exactly as they were.</summary>
    public int Kept { get; private set; }

    /// <summary>The dabs that stood after <see cref="Kept"/> before the last call.</summary>
    public IReadOnlyList<BrushEngine.Dab> Replaced => _replaced;

    /// <summary>
    /// How many densified points the last call walked - the work it did, in a
    /// unit a test can bound without a clock.
    /// </summary>
    public int LastWalked { get; private set; }

    /// <summary>Forget the stroke: the next call walks from its first point.</summary>
    public void Reset()
    {
        _densify.Reset();
        _dabs.Clear();
        _replaced.Clear();
        _afterSpan.Clear();
        _brush = null;
        Kept = 0;
        LastWalked = 0;
    }

    /// <summary>Every dab of the stroke so far, in order.</summary>
    public List<BrushEngine.Dab> Of(Stroke stroke)
    {
        _replaced.Clear();

        // A different brush is a different walk of the same points.
        if (!ReferenceEquals(stroke.Brush, _brush))
        {
            StartOver();
            _brush = stroke.Brush;
        }

        if (stroke.RestPoints is { Count: > 1 } rest && rest.Count == stroke.Points.Count)
        {
            StartOver();
            _dabs.AddRange(BrushEngine.WalkDabs(stroke));
            LastWalked = stroke.Points.Count;
            return _dabs;
        }

        var brush = stroke.Brush;
        var pts = _densify.Of(stroke.Points);
        var spans = _densify.SpanCount;
        var firstSpan = Math.Min(spans == 0 ? 0 : _densify.LastFirstSpan, _afterSpan.Count);
        var subdivides = BrushEngine.SubdividesForFidelity(brush);
        var turns = BrushEngine.CarriesAHeading(brush);

        State s;
        int from;
        if (firstSpan == 0)
        {
            StartOver(keepDensify: true);
            var first = pts[0];
            var firstPressure = Math.Max(first.Pressure, BrushEngine.MinPressure);
            var (step, fidelity) = BrushEngine.StepFor(brush, firstPressure, subdivides);
            s = new State { Step = step, Fidelity = fidelity, Prev = first, Heading = double.NaN };
            _dabs.Add(new BrushEngine.Dab(
                new SKPoint((float)first.X, (float)first.Y), firstPressure, double.NaN, BrushEngine.LoadAt(0, brush))
            {
                Fidelity = fidelity,
            });
            from = 1;
        }
        else
        {
            s = _afterSpan[firstSpan - 1];
            from = _densify.SpanEnd(firstSpan - 1);
            _afterSpan.RemoveRange(firstSpan, _afterSpan.Count - firstSpan);
            for (var i = s.Dabs; i < _dabs.Count; i++) _replaced.Add(_dabs[i]);
            _dabs.RemoveRange(s.Dabs, _dabs.Count - s.Dabs);
            Kept = s.Dabs;
        }

        LastWalked = pts.Count - from;
        if (spans == 0)
        {
            Advance(pts, from, pts.Count, ref s, brush, subdivides, turns);
            return _dabs;
        }

        for (var span = firstSpan; span < spans; span++)
        {
            var end = _densify.SpanEnd(span);
            Advance(pts, from, end, ref s, brush, subdivides, turns);
            s.Dabs = _dabs.Count;
            _afterSpan.Add(s);
            from = end;
        }
        return _dabs;
    }

    /// <summary>Every dab so far becomes one that was replaced, and nothing is kept.</summary>
    private void StartOver(bool keepDensify = false)
    {
        _replaced.AddRange(_dabs);
        _dabs.Clear();
        _afterSpan.Clear();
        if (!keepDensify) _densify.Reset();
        Kept = 0;
    }

    /// <summary>
    /// The reference walk's loop over densified points <paramref name="from"/>
    /// up to <paramref name="to"/>, continued from <paramref name="s"/>. Kept
    /// line for line with <c>SampledDabPositions</c> and the body of
    /// <c>WalkDabs</c>: the two must agree to the bit, and a test holds them to it.
    /// </summary>
    private void Advance(
        IReadOnlyList<StrokePoint> pts, int from, int to, ref State s,
        BrushSettings brush, bool subdivides, bool turns)
    {
        for (var i = from; i < to; i++)
        {
            var cur = pts[i];
            var d = GeometryOps.Dist(s.Prev, cur);
            while (d > 0 && s.Acc + d >= s.Step)
            {
                var t = (s.Step - s.Acc) / d;
                var np = GeometryOps.LerpPoint(s.Prev, cur, t);
                var pressure = Math.Max(np.Pressure, BrushEngine.MinPressure);
                d -= s.Step - s.Acc;
                s.Acc = 0;
                s.Prev = np;
                (s.Step, s.Fidelity) = BrushEngine.StepFor(brush, pressure, subdivides);

                var pos = new SKPoint((float)np.X, (float)np.Y);
                var last = _dabs[^1].Pos;
                float dx = pos.X - last.X, dy = pos.Y - last.Y;
                var moved = Math.Sqrt(dx * dx + dy * dy);
                s.Travelled += moved;
                if (turns && moved > 0.01) s.Heading = Math.Atan2(dy, dx) * 180 / Math.PI;
                _dabs.Add(new BrushEngine.Dab(pos, pressure, s.Heading, BrushEngine.LoadAt(s.Travelled, brush))
                {
                    Fidelity = s.Fidelity,
                });
            }
            s.Acc += d;
            s.Prev = cur;
        }
    }
}
