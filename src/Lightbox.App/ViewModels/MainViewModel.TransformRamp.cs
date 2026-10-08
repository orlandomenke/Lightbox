using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Timeline;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>What each <see cref="RampEase"/> is called on the Transform page.</summary>
public static class RampEaseText
{
    public static string Label(RampEase ease) => ease switch
    {
        RampEase.EaseIn => "Ease in",
        RampEase.EaseOut => "Ease out",
        RampEase.EaseInOut => "Ease in and out",
        _ => "Even",
    };

    public static readonly Avalonia.Data.Converters.IValueConverter Converter =
        new Avalonia.Data.Converters.FuncValueConverter<RampEase, string>(Label);
}

/// <summary>
/// A transform that ramps over the frames (Q216): none of it on the first
/// drawing of the range, all of it on the last, eased in between.
/// </summary>
/// <remarks>
/// <para>
/// <b>The box is the end state.</b> What the artist drags is the transform the
/// last drawing gets; the drawing at the playhead shows its own share of it and
/// each onion ghost shows its own, so the ramp is visible in the ghosts while
/// the box is being dragged. That was the owner's pick over opening the session
/// on the range's last drawing, which would have moved the playhead unasked.
/// </para>
/// <para>
/// <b>A share belongs to where a drawing starts showing</b>, so holds keep
/// their timing — a cycle on 2s moves on 2s. A drawing exposed at more than
/// one place is <b>split into copies</b> at commit, one per place, because one
/// drawing cannot stand in two positions; that is what lets a looping cycle
/// become one that travels (Q216, the owner's pick). Drawings outside the range
/// never change: a drawing that is also exposed outside it is copied at every
/// place inside it.
/// </para>
/// <para>
/// Box mode only — perspective, bands and the cage apply in full — and whole
/// drawings only: a ramp under a selection would be a different feature.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Ramp the open transform over the frames. Ends with the session.</summary>
    [ObservableProperty]
    private bool _rampOverFrames;

    [ObservableProperty]
    private RampEase _rampOverFramesEase = RampEase.Linear;

    public IReadOnlyList<RampEase> RampEaseChoices { get; } = Enum.GetValues<RampEase>();

    /// <summary>Whether the open session can ramp at all — the ramp control shows only then.</summary>
    public bool RampAvailable => TransformActive && _transform.Filter is null && RampRange() is not null;

    /// <summary>The gizmo's box as parts, while it is a box; null in the other modes.</summary>
    private AffineParts? _rampBox;

    /// <summary>The box one gizmo change ago — the repaint covers where the ramp was too.</summary>
    private AffineParts? _rampBoxBefore;

    /// <summary>Each start position's share, for the range the session holds.</summary>
    private RampPlan? _rampPlan;

    private sealed record RampPlan(Layer Layer, int Start, int End, IReadOnlyDictionary<int, double> Shares);

    /// <summary>Whether the open preview and the commit ramp.</summary>
    private bool RampApplies =>
        RampOverFrames && TransformActive && _transform.Filter is null
        && _rampBox is { Mirrors: false } && RampPlanNow() is not null;

    /// <summary>
    /// The gizmo's decomposed box, handed over beside its matrix on every change;
    /// null when the gizmo is not a box (perspective, bands, cage).
    /// </summary>
    public void SetTransformBox(AffineParts? box)
    {
        _rampBoxBefore = _rampBox;
        _rampBox = box;
        if (RampOverFrames && box is { Mirrors: true })
        {
            AiStatus = "A mirror cannot be ramped — it would pass through a flat line on the way. Turn the ramp off to mirror.";
        }
    }

    partial void OnRampOverFramesChanged(bool value)
    {
        if (value && !RampAvailable)
        {
            RampOverFrames = false;
            return;
        }
        _rampPlan = null;
        RepaintForRamp();
        if (value) SayRampAtPlayhead();
    }

    partial void OnRampOverFramesEaseChanged(RampEase value)
    {
        _rampPlan = null;
        RepaintForRamp();
        if (RampOverFrames) SayRampAtPlayhead();
    }

    /// <summary>
    /// One full repaint for a click on the ramp's controls — the shares change
    /// everywhere at once. A click, never a pointer move, so invariant 6's
    /// bounded-work rule (which is about the drag) is not what this pays.
    /// </summary>
    private void RepaintForRamp()
    {
        if (!TransformActive) return;
        _publish.InvalidateWholeCanvas();
        RequestSnapshot();
    }

    /// <summary>Session over: the ramp and its box go with it, as a gesture's scope does (B403).</summary>
    private void EndRamp()
    {
        _rampBox = null;
        _rampBoxBefore = null;
        _rampPlan = null;
        if (RampOverFrames) RampOverFrames = false;
        OnPropertyChanged(nameof(RampAvailable));
    }

    /// <summary>
    /// The run of cels a ramp covers: every frame of the active layer, or the
    /// marked cels when they are one unbroken run on one layer. Null otherwise.
    /// </summary>
    private (Layer Layer, int Start, int End)? RampRange()
    {
        switch (EffectiveTransformScope)
        {
            case TransformScope.ActiveLayerAllFrames when ActiveLayer is { Cels.Count: > 1 } layer:
                return (layer, 0, layer.Cels.Count - 1);
            case TransformScope.CelRange when CelRange is { } r && r.Layer >= 0 && r.Layer < Scene.Layers.Count
                                              && r.End > r.Start:
                return (Scene.Layers[r.Layer], r.Start, r.End);
            default:
                return null;
        }
    }

    private RampPlan? RampPlanNow()
    {
        if (_rampPlan is { } plan) return plan;
        if (RampRange() is not var (layer, start, end)) return null;
        var positions = new List<int>();
        for (var i = start; i <= end; i++)
        {
            if (StartOf(layer, i, start) is { } p) positions.Add(p);
        }
        if (positions.Count == 0) return null;
        return _rampPlan = new RampPlan(layer, start, end, Lightbox.Core.Geometry.TransformRamp.Shares(positions, RampOverFramesEase));
    }

    /// <summary>
    /// Where the drawing showing at <paramref name="index"/> starts showing,
    /// counted from the range's start — a drawing held into the range from
    /// before it starts there. Null over an empty cel.
    /// </summary>
    private static int? StartOf(Layer layer, int index, int rangeStart)
    {
        var key = ExposureSheet.KeyIndexAtOrBefore(layer, index);
        if (key < 0) return null;
        return Math.Max(key, rangeStart);
    }

    /// <summary>The share of the ramp the drawing showing at this cel gets.</summary>
    private double RampShareAt(Layer layer, int index)
    {
        if (RampPlanNow() is not { } plan || !ReferenceEquals(plan.Layer, layer)) return 1;
        if (StartOf(layer, index, plan.Start) is not { } p || p > plan.End) return 0;
        return plan.Shares.TryGetValue(p, out var share) ? share : 0;
    }

    private static SKMatrix MatrixOf(AffineParts p)
    {
        var m = SKMatrix.CreateTranslation((float)-p.PivotX, (float)-p.PivotY);
        m = m.PostConcat(SKMatrix.CreateScale((float)p.ScaleX, (float)p.ScaleY));
        m = m.PostConcat(SKMatrix.CreateRotation((float)p.Angle));
        return m.PostConcat(SKMatrix.CreateTranslation((float)(p.PivotX + p.OffsetX), (float)(p.PivotY + p.OffsetY)));
    }

    /// <summary>
    /// The preview matrix for one drawing at one cel — the builder's view of
    /// the ramp. Null when the ramp is off, so the single matrix stands.
    /// </summary>
    private SKMatrix? RampPreviewAt(Layer layer, int index) =>
        RampApplies && _rampBox is { } box ? MatrixOf(box.At(RampShareAt(layer, index))) : null;

    private Func<Layer, int, SKMatrix?>? _passRampPreview;

    /// <summary>
    /// The doc-space region a ramped preview step changes, or null when the ramp
    /// is off.
    /// </summary>
    /// <remarks>
    /// The plain rule — old box through the old matrix, unioned with it through
    /// the new — is not enough here: a drawing part-way along a diagonal move,
    /// or part-way round a turn, stands outside both. So the session's bounds go
    /// through every share a drawing is actually given, before and after. One
    /// rectangle map per distinct share, per event: bounded, and never the
    /// whole canvas (invariant 6).
    /// </remarks>
    internal SKRectI? RampDirtyRegion()
    {
        var before = _rampBoxBefore;
        if (!RampApplies || _rampBox is not { } now || RampPlanNow() is not { } plan) return null;
        if (_transform.MovingBounds is not { } bounds) return null;
        var region = bounds;
        foreach (var share in plan.Shares.Values.Distinct())
        {
            region.Union(MatrixOf(now.At(share)).MapRect(bounds));
            if (before is { } b) region.Union(MatrixOf(b.At(share)).MapRect(bounds));
        }
        region.Inflate(2, 2);
        return SKRectI.Ceiling(region);
    }

    private void SayRampAtPlayhead()
    {
        if (RampPlanNow() is not { } plan) return;
        var share = RampShareAt(plan.Layer, CurrentFrameIndex);
        AiStatus = $"Ramp over frames {plan.Start + 1}–{plan.End + 1}: the box is the last drawing; " +
                   $"this one gets {share:P0}.";
    }

    /// <summary>
    /// Apply the ramp: each drawing in the range through its own share of the
    /// box, reused drawings split into copies first, as one undo step.
    /// </summary>
    private void CommitTransformRamp(AffineParts box)
    {
        using var perf = PerfLog.Begin("transform.commit.ramp");
        if (RampPlanNow() is not { } plan) return;
        var layer = plan.Layer;

        // Which drawing each start position transforms, and which of them must
        // become copies. Positions with no share change nothing and are left
        // alone — the first drawing, and one held in from before the range.
        var jobs = new List<(int Position, Frame Original, AffineParts Parts)>();
        foreach (var (position, share) in plan.Shares.OrderBy(kv => kv.Key))
        {
            if (share <= 0) continue;
            if (ExposureSheet.FrameAtExactIndex(layer, position) is not { } frame) continue;
            jobs.Add((position, frame, box.At(share)));
        }
        if (jobs.Count == 0)
        {
            CancelTransform();
            return;
        }
        var copyAt = new HashSet<int>();
        var keptOriginal = new HashSet<string>();
        foreach (var (position, frame, _) in jobs)
        {
            var outside = false;
            for (var k = 0; k < layer.Cels.Count && !outside; k++)
            {
                if ((k < plan.Start || k > plan.End) && ReferenceEquals(layer.Cels[k].Frame, frame)) outside = true;
            }
            // Within the range, the first place a shared drawing stands also
            // counts as a reference — it keeps the original, and a later share
            // of it gets a copy. A place with no share is that first place.
            var firstInRange = Enumerable.Range(plan.Start, plan.End - plan.Start + 1)
                .First(k => ReferenceEquals(layer.Cels[k].Frame, frame));
            var heldByAZeroShare = firstInRange < position;
            if (outside || heldByAZeroShare || !keptOriginal.Add(frame.Id)) copyAt.Add(position);
        }

        // B381: the pose is resolved from the drawings as they are now, before
        // any copy exists — a copy is the same drawing, so it travels the same way.
        var movers = jobs.ConvertAll(j =>
            Skinning.PoseSpaceMover(Doc, j.Original, CurrentFrameIndex, _cache.Rig, j.Parts.Map()));
        var copies = jobs.ConvertAll(j => copyAt.Contains(j.Position) ? KeyedCopyOf(j.Original) : null);

        _transform.ClearPreview();
        _transform.ClearBands();
        foreach (var j in jobs) InvalidateFrameRender(j.Original.Id);

        var travels = new List<ClipTravel>(jobs.Count);
        _confirmingTransform = true;
        try
        {
            _editor.Perform(doc =>
            {
                for (var i = 0; i < jobs.Count; i++)
                {
                    var (position, original, parts) = jobs[i];
                    var target = original;
                    if (copies[i] is { } copy)
                    {
                        layer.Cels[position].Frame = copy;
                        target = copy;
                    }
                    var map = parts.Map();
                    var travel = new ClipTravel(map);
                    travels.Add(travel);
                    TransformOps.TransformFrame(target, map, parts.SizeScale, null, travel.Carry, movers[i]);
                    if (target is Frame { PngBase64.Length: > 0 } painted) ResampleBaseline(painted, MatrixOf(parts));
                }
                foreach (var travel in travels)
                {
                    foreach (var (id, region) in travel.Used) doc.ClipRegions.TryAdd(id, region);
                }
            },
            touchedFrames: [.. jobs.Select(j => j.Original.Id), .. copies.Where(c => c is not null).Select(c => c!.Id)]);
        }
        finally
        {
            _confirmingTransform = false;
        }
        EndTransformSession();
        var made = copyAt.Count;
        AiStatus = $"Ramped {jobs.Count} drawing{(jobs.Count == 1 ? "" : "s")} over frames {plan.Start + 1}–{plan.End + 1}"
                   + (made > 0 ? $", making {made} cop{(made == 1 ? "y" : "ies")} of drawings shown in more than one place." : ".");
    }
}
