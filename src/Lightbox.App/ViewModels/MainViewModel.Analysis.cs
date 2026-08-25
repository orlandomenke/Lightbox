using CommunityToolkit.Mvvm.Input;
using Lightbox.App.Rendering;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The analysers riding the motion trail (Q134): the spacing assistant's
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

    /// <summary>The walk cycle read as prose — loop, contacts, bob, slide.</summary>
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

    /// <summary>Judge spacing and the jump arc as the camera sees them (Q137).</summary>
    public bool AnalyseThroughCamera
    {
        get => Settings.Trail.ThroughCamera;
        set
        {
            if (Settings.Trail.ThroughCamera == value) return;
            Settings.Trail.ThroughCamera = value;
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
        // Through the camera only where it means something (Q137): spacing
        // and the jump arc ask how the motion READS, which a camera changes;
        // the walk checks are physical facts a pan cannot alter.
        var through = Settings.Trail.ThroughCamera && HasCamera;
        _spacingTargets = layer is not null && Settings.Trail.SpacingGhosts
            ? SpacingAssistant.TargetsForRun(Doc.Scene, layer, CurrentFrameIndex, TweenEasing, through)
            : null;
        _jumpFit = layer is not null && Settings.Trail.JumpArc
            ? JumpArcAnalyser.FitRun(Doc.Scene, layer, CurrentFrameIndex, through)
            : null;
        _walkReport = layer is not null && Settings.Trail.WalkReport
            ? WalkCycleAnalyser.Analyse(Doc.Scene, layer, CurrentFrameIndex)
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
                // Q137: through the camera the verdict is about the read, not
                // the world — correct gravity can fail to read as an arc under
                // a camera move, and that is information rather than an error.
                var seen = jump.ThroughCamera ? " as the camera sees it" : "";
                if (jump.DepthMotion)
                {
                    lines.Add("Jump arc: the subject changes size across this run — " +
                              "reading it as depth motion, the flat fit does not apply.");
                }
                else if (!jump.Ballistic)
                {
                    lines.Add($"Jump arc: this run does not read as ballistic{seen} — no apex to fall from.");
                }
                else
                {
                    var off = jump.Deviations.Where(d => d.OffArc).ToList();
                    if (off.Count > 0)
                    {
                        var frames = string.Join(", ", off.Select(d => d.Index + 1));
                        lines.Add($"Jump arc: frame{(off.Count == 1 ? "" : "s")} {frames} " +
                                  $"sit{(off.Count == 1 ? "s" : "")} off the arc{seen} (past {jump.Tolerance:0.#} px).");
                    }
                }
            }

            if (_walkTooFew)
            {
                lines.Add($"Walk: fewer than {WalkCycleAnalyser.MinDrawings} drawings with ink — nothing to read yet.");
            }
            else if (_walkReport is { } report)
            {
                // The tag's name in front when a tag scoped the read, so
                // "walk" and "settle" on one layer cannot be confused.
                var scope = report.RangeName is { } tagged ? $" “{tagged}”" : "";
                if (report.Findings.Count == 0)
                {
                    lines.Add($"Walk{scope}: {report.Drawings} drawings, contacts at " +
                              $"{string.Join(", ", report.ContactFrames.Select(f => f + 1))} — nothing to flag.");
                }
                else
                {
                    lines.AddRange(report.Findings.Select(f => $"Walk{scope}: {f.Message}"));
                }
            }

            return string.Join("\n", lines);
        }
    }

    public bool HasAnalysisReadout => AnalysisReadout.Length > 0;

    /// <summary>
    /// Move the playhead's drawing to where the intended spacing wants it —
    /// the assistant's one click (Q134). A whole-drawing translate through
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
        // A clip region lives on the document, content-hashed and shareable
        // between strokes, so it cannot travel with one drawing — moving the
        // ink and leaving the mask desyncs the two silently. Refused whole,
        // the baseline's rule.
        if (FrameTranslate.HasClippedStrokes(frame))
        {
            AiStatus = "This drawing has selection-clipped strokes whose clip cannot move with them — nudge refused rather than desyncing the mask.";
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

    /// <summary>
    /// Read the layer's footfalls and mark them (Q135): named "contact"
    /// markers at every detected footfall start, one undo step, on request
    /// only. Frames that already carry a marker are left alone — a marker is
    /// the artist's, whatever it says, and detection must not overwrite it.
    /// The jump arc analyser trims its fit at these markers from then on.
    /// </summary>
    [RelayCommand]
    public void DetectContacts()
    {
        if (ActiveLayer is not { } layer)
        {
            AiStatus = "No layer to read contacts from.";
            return;
        }
        if (ContactFrames.Detect(Doc.Scene, layer) is not { } reading || reading.Starts.Count == 0)
        {
            AiStatus = $"No contacts read — the lowest ink never settles, or fewer than " +
                       $"{ContactFrames.MinDrawings} drawings have ink.";
            return;
        }

        var unmarked = reading.Starts.Where(f => MarkerAt(f) is null).ToList();
        var frames = string.Join(", ", reading.Starts.Select(f => f + 1));
        if (unmarked.Count == 0)
        {
            AiStatus = $"Contacts read at frame{(reading.Starts.Count == 1 ? "" : "s")} {frames} — all already marked.";
            return;
        }

        _editor.Perform(doc =>
        {
            foreach (var frame in unmarked)
            {
                doc.Scene.Markers.Add(new FrameMarker { Frame = frame, Label = ContactFrames.MarkerLabel });
            }
            doc.Scene.Markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        }, label: "Detect contacts");

        RefreshMotionTrail();   // the jump arc reads the markers just written
        AiStatus = $"Marked {unmarked.Count} contact{(unmarked.Count == 1 ? "" : "s")} " +
                   $"(footfalls at frame{(reading.Starts.Count == 1 ? "" : "s")} {frames}).";
    }
}
