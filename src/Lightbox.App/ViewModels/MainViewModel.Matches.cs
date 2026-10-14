using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The artist's line matches (Q271): which lines of a key are which lines of
/// the next, and redrawing a run's inbetweens from them.
/// </summary>
public partial class MainViewModel
{
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
    /// <b>Times.</b> A charted run (Q58) puts its inbetweens on the chart's
    /// rungs, so the rungs are reused, in order, when there is one for each
    /// inbetween; otherwise each inbetween's time is where its cel sits in the
    /// run, through the bar's easing — what ＋ Inbetween gave it.
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
        var run = ExposureSheet.RunAt(layer, CurrentFrameIndex);
        if (run.Count < 3) return;

        var aIndex = run[0];
        var span = (double)(run[^1] - aIndex);
        var targets = run.Where(i => layer.Cels[i].Frame!.Role == FrameRole.Inbetween).ToList();
        if (targets.Count == 0) return;
        var stops = run
            .Where(i => layer.Cels[i].Frame!.Role != FrameRole.Inbetween)
            .Select(i => new Inbetweener.RunStop(
                (i - aIndex) / span, StrokesOf(layer.Cels[i].Frame!), layer.Cels[i].Frame!.Matches))
            .ToList();
        if (stops.Count < 2) return;

        var chart = layer.Cels[aIndex].Frame!.Chart;
        var rungs = chart?
            .Where(rung => !stops.Any(s => Math.Abs(s.Time - rung) <= RungEpsilon))
            .Order()
            .ToList();
        var charted = rungs is not null && rungs.Count == targets.Count;
        var times = charted ? rungs! : [.. targets.Select(i => (i - aIndex) / span)];
        var drawn = Inbetweener.InbetweenRun(stops, times, charted ? Easing.Linear : TweenEasing);

        var layerId = layer.Id;
        var replacements = targets
            .Select((cel, k) => (cel, Frame: NewFrameFor(layer, drawn[k], FrameRole.Inbetween)))
            .ToList();
        _editor.Perform(doc =>
        {
            var target = doc.Scene.Layers.First(l => l.Id == layerId);
            foreach (var (cel, frame) in replacements) target.Cels[cel].Frame = frame;
        }, "Redraw inbetweens");
    }
}
