using Lightbox.App.Controls;
using Lightbox.App.Rendering;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>Part of the MainWindow code-behind — see MainWindow.axaml.cs.</summary>
/// <remarks>
/// The volume checker's plumbing: the view model measures, and this pushes the
/// readings onto the two surfaces that show them — the balance arc on the
/// canvas and the volume band on the timeline. Push rather than bind, for the
/// rig overlay's reason (B58): both are flattened snapshots for a render
/// thread, not values an artist edits.
/// </remarks>
public partial class MainWindow
{
    private void InitialiseVolumeCheck() =>
        _vm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(MainViewModel.VolumeRows):
                    RefreshVolumeSurfaces();
                    break;
                // The playhead moved: the readings stand, only the emphasised
                // dot changes — so only the dots are rebuilt, and the timeline
                // band is not touched at all. During playback this fires per
                // frame shown, which is why it must not do the full refresh.
                case nameof(MainViewModel.CurrentFrameIndex) when _vm.VolumeCheck:
                    RefreshBalanceEmphasis();
                    break;
                // The layer is the artist's statement of what "the character"
                // is, so standing on a different one re-asks the question.
                case nameof(MainViewModel.ActiveLayerIndex) when _vm.VolumeCheck:
                    _vm.RecomputeVolumeCheck();
                    break;
            }
        };

    /// <summary>The frame the dots were last emphasised for, to skip no-ops.</summary>
    private int _balanceCurrentFrame = -1;

    private void RefreshVolumeSurfaces()
    {
        var rows = _vm.VolumeRows;
        if (rows.Count == 0)
        {
            // Absent, not blank: no band on the timeline, no dots on the
            // canvas, nothing measured or paid for.
            Canvas.BalanceDots = null;
            TimelineTrackView.VolumeBars = null;
            _balanceCurrentFrame = -1;
            return;
        }

        PushBalanceDots(rows);

        // Normalised against the largest frame, so the skyline uses the whole
        // band whatever the character's absolute size.
        var max = 0.0;
        foreach (var row in rows) if (row.HasInk && row.Area > max) max = row.Area;
        var bars = new TrackView.VolumeBar[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            bars[i] = new TrackView.VolumeBar(
                rows[i].HasInk && max > 0 ? rows[i].Area / max : -1, rows[i].Flagged);
        }
        TimelineTrackView.VolumeBars = bars;
    }

    private void RefreshBalanceEmphasis()
    {
        var rows = _vm.VolumeRows;
        if (rows.Count == 0 || _vm.CurrentFrameIndex == _balanceCurrentFrame) return;
        PushBalanceDots(rows);
    }

    private void PushBalanceDots(IReadOnlyList<MainViewModel.VolumeRow> rows)
    {
        var current = _vm.CurrentFrameIndex;
        var dots = new List<BalanceDot>(rows.Count);
        foreach (var row in rows)
        {
            if (!row.HasInk) continue;
            dots.Add(new BalanceDot(
                (float)row.CentroidX, (float)row.CentroidY,
                Current: row.Frame == current, row.Flagged));
        }
        Canvas.BalanceDots = dots.Count > 0 ? dots : null;
        _balanceCurrentFrame = current;
    }
}
