using CommunityToolkit.Mvvm.Input;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The analysers riding the motion trail (Q133): the spacing assistant's
/// ghost targets and one-click nudge, the jump arc fit, and the walk cycle
/// readout. The geometry goes to the canvas inside the trail's own snapshot;
/// the words go to <see cref="AnalysisReadout"/> in the onion bar's flyout.
/// Everything here reads the record; only the nudge writes it, as one
/// undoable whole-drawing move.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Ghost ticks where the intended spacing wants each inbetween.</summary>
    public bool SpacingGhosts
    {
        get => Settings.Trail.SpacingGhosts;
        set
        {
            if (Settings.Trail.SpacingGhosts == value) return;
            Settings.Trail.SpacingGhosts = value;
            OnPropertyChanged();
            AfterTrailChange();
        }
    }

    /// <summary>The gravity parabola fitted to the playhead's run.</summary>
    public bool JumpArcOverlay
    {
        get => Settings.Trail.JumpArc;
        set
        {
            if (Settings.Trail.JumpArc == value) return;
            Settings.Trail.JumpArc = value;
            OnPropertyChanged();
            AfterTrailChange();
        }
    }

    /// <summary>The walk cycle read as prose — loop, contacts, bob.</summary>
    public bool WalkReport
    {
        get => Settings.Trail.WalkReport;
        set
        {
            if (Settings.Trail.WalkReport == value) return;
            Settings.Trail.WalkReport = value;
            OnPropertyChanged();
            AfterTrailChange();
        }
    }

    /// <summary>
    /// The analysers' latest results, computed ONCE per trail refresh and read
    /// by both consumers — the overlay snapshot and the readout. The readout
    /// getter used to re-run every analyser the overlay had just run, which
    /// doubled a whole-record walk on every playhead move (leak-hunter,
    /// 2026-08-20); a refresh now computes here and everything else reads.
    /// </summary>
    private IReadOnlyList<SpacingTarget>? _spacingTargets;

    private JumpArcFit? _jumpFit;

    private WalkCycleReport? _walkReport;

    private bool _walkTooFew;

    /// <summary>
    /// Run the switched-on analysers against the record, or clear everything
    /// when <paramref name="layer"/> is null (trail off, playing, no layer).
    /// </summary>
    internal void RecomputeAnalysis(Layer? layer)
    {
        _spacingTargets = layer is not null && Settings.Trail.SpacingGhosts
            ? SpacingAssistant.TargetsForRun(Doc.Scene, layer, CurrentFrameIndex, TweenEasing)
            : null;
        _jumpFit = layer is not null && Settings.Trail.JumpArc
            ? JumpArcAnalyser.FitRun(Doc.Scene, layer, CurrentFrameIndex)
            : null;
        _walkReport = layer is not null && Settings.Trail.WalkReport
            ? WalkCycleAnalyser.Analyse(Doc.Scene, layer)
            : null;
        _walkTooFew = layer is not null && Settings.Trail.WalkReport && _walkReport is null;
    }

    /// <summary>
    /// The trail's ticks plus whatever analysis is switched on, or null when
    /// nothing would draw. The analysers only speak while the trail is on —
    /// they annotate its ticks, and marks with no ticks to sit beside would
    /// be chrome nobody asked to learn.
    /// </summary>
    internal TrailOverlay? BuildTrailOverlay(IReadOnlyList<TrailPoint> points)
    {
        var ticks = points.Count > 1 ? points : null;
        if (ticks is null && _spacingTargets is not { Count: > 0 } && _jumpFit is null) return null;
        return new TrailOverlay(ticks, _spacingTargets, _jumpFit);
    }

    /// <summary>
    /// What the switched-on analysers have to say, one line each, empty when
    /// they are off or content. Composed from the results the last refresh
    /// computed, so it follows the playhead and every edit at no second walk.
    /// </summary>
    public string AnalysisReadout
    {
        get
        {
            var lines = new List<string>();

            if (_spacingTargets is { } targets)
            {
                var misses = targets.Where(t => t.Misses).ToList();
                if (misses.Count > 0)
                {
                    var worst = misses.MaxBy(t => t.Deviation);
                    lines.Add($"Spacing: {misses.Count} drawing{(misses.Count == 1 ? "" : "s")} off the ease — " +
                              $"worst is frame {worst.Index + 1}, {worst.Deviation:0.#} px out.");
                }
            }

            if (_jumpFit is { } jump)
            {
                if (!jump.Ballistic)
                {
                    lines.Add("Jump arc: this run does not read as ballistic — no apex to fall from.");
                }
                else
                {
                    var off = jump.Deviations.Where(d => d.OffArc).ToList();
                    if (off.Count > 0)
                    {
                        var frames = string.Join(", ", off.Select(d => d.Index + 1));
                        lines.Add($"Jump arc: frame{(off.Count == 1 ? "" : "s")} {frames} " +
                                  $"sit{(off.Count == 1 ? "s" : "")} off the arc (past {jump.Tolerance:0.#} px).");
                    }
                }
            }

            if (_walkTooFew)
            {
                lines.Add($"Walk: fewer than {WalkCycleAnalyser.MinDrawings} drawings with ink — nothing to read yet.");
            }
            else if (_walkReport is { } report)
            {
                if (report.Findings.Count == 0)
                {
                    lines.Add($"Walk: {report.Drawings} drawings, contacts at " +
                              $"{string.Join(", ", report.ContactFrames.Select(f => f + 1))} — nothing to flag.");
                }
                else
                {
                    lines.AddRange(report.Findings.Select(f => $"Walk: {f.Message}"));
                }
            }

            return string.Join("\n", lines);
        }
    }

    public bool HasAnalysisReadout => AnalysisReadout.Length > 0;

    /// <summary>
    /// Move the playhead's drawing to where the intended spacing wants it —
    /// the assistant's one click (Q133). A whole-drawing translate through
    /// <see cref="FrameTranslate"/>, one undo step, exact on undo because the
    /// revert restores the snapshotted coordinates rather than subtracting.
    /// </summary>
    [RelayCommand]
    public void NudgeToSpacing()
    {
        if (ActiveLayer is not { } layer
            || ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is not { } frame)
        {
            AiStatus = "No drawing under the playhead to nudge.";
            return;
        }

        var targets = SpacingAssistant.TargetsForRun(
            Doc.Scene, layer, CurrentFrameIndex, TweenEasing);
        var target = targets.FirstOrDefault(t => t.FrameId == frame.Id);
        if (target.FrameId != frame.Id)
        {
            AiStatus = "This drawing is an extreme or has no spacing target — nothing to nudge to.";
            return;
        }
        if (frame.HasBaseline)
        {
            AiStatus = "This drawing has a pixel baseline, which cannot be moved — nudge refused rather than tearing it.";
            return;
        }

        var dx = target.TargetX - target.X;
        var dy = target.TargetY - target.Y;
        // A baked raster's offset is whole pixels; a fractional move would put
        // it out of register with its own stroke, so the nudge rounds.
        if (FrameTranslate.HasBakedRaster(frame))
        {
            dx = Math.Round(dx);
            dy = Math.Round(dy);
        }
        if (dx == 0 && dy == 0)
        {
            AiStatus = "This drawing is already on its spacing.";
            return;
        }

        // Snapshotted whole, and re-cloned on every revert: a redo translates
        // the live objects in place, so handing back the snapshot's own lists
        // would let one round of undo/redo corrupt the next.
        var frameId = frame.Id;
        var before = frame.Clone();
        _editor.PerformDelta(
            apply: doc =>
            {
                if (FrameIn(doc, frameId) is { } f) FrameTranslate.Apply(f, dx, dy);
            },
            revert: doc =>
            {
                if (FrameIn(doc, frameId) is not { } f) return;
                var restored = before.Clone();
                f.Strokes = restored.Strokes;
                f.Placements = restored.Placements;
                f.Anchors = restored.Anchors;
                f.Shapes = restored.Shapes;
            },
            affectedFrameId: frameId,
            label: "Nudge to spacing");

        AfterStrokeEdit(frameId);
        RefreshMotionTrail();
        AiStatus = $"Nudged frame {target.Index + 1} by {dx:0.#}, {dy:0.#} onto its spacing.";
    }
}
