using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// <para>
/// The volume and balance checker: per-frame ink area (the 0th moment of the
/// active layer's ink) and its centroid (the 1st), with frames flagged when
/// their volume drifts past the tolerance. A <b>checker</b>, not a guide — it
/// reads the drawing and reports, touches no input, reaches no pixel.
/// </para>
/// <para>
/// Every field this section uses is declared here; no other section touches
/// them.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>
    /// One frame's reading. Area is in document pixels of ink, alpha-weighted;
    /// the centroid is in document coordinates, or meaningless when
    /// <paramref name="HasInk"/> is false.
    /// </summary>
    public readonly record struct VolumeRow(
        int Frame, double Area, double CentroidX, double CentroidY, bool HasInk, bool Flagged);

    /// <summary>
    /// Whether the checker is on. A working state like onion skin — how you
    /// are looking this minute, never saved with the document.
    /// </summary>
    [ObservableProperty]
    private bool _volumeCheck;

    partial void OnVolumeCheckChanged(bool value)
    {
        if (value) RecomputeVolumeCheck();
        else ClearVolumeCheck();
    }

    private IReadOnlyList<VolumeRow> _volumeRows = [];

    /// <summary>
    /// The readings, one per timeline frame — empty while the checker is off,
    /// so the surfaces that draw it are absent rather than blank.
    /// </summary>
    public IReadOnlyList<VolumeRow> VolumeRows => _volumeRows;

    /// <summary>
    /// Longest side of the measuring render, in pixels.
    /// </summary>
    /// <remarks>
    /// The checker's question is "did the mass move", not "what is the exact
    /// pixel", and drift ratios are scale-free — so a capped render answers it
    /// at a fraction of a full-resolution pass over every frame. Deterministic
    /// all the same: the same document always measures the same numbers.
    /// </remarks>
    private const int MeasureCap = 512;

    private bool _volumeCheckQueued;

    /// <summary>
    /// Recompute after the current burst of work, once.
    /// </summary>
    /// <remarks>
    /// Called from <c>OnDocumentChanged</c>, which fires per committed edit —
    /// never per pointer event, so invariant 6 is not in play. Posted at
    /// background priority so the stroke's own publish always paints first;
    /// the checker lagging an edit by a beat is the right trade for never
    /// adding to pen-lift latency.
    /// </remarks>
    internal void ScheduleVolumeCheck()
    {
        if (!VolumeCheck || _volumeCheckQueued) return;
        _volumeCheckQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _volumeCheckQueued = false;
            if (VolumeCheck) RecomputeVolumeCheck();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// Measure every frame of the active layer now.
    /// </summary>
    /// <remarks>
    /// The active layer, deliberately: "the character" is not "the ink", and
    /// effects, props or a second character would pollute the moments — so
    /// attribution is the artist's statement (the layer they are standing on)
    /// rather than an inference. Holds resolve to the same drawing and are
    /// measured once, so a scene on 2s costs half its frame count.
    /// </remarks>
    public void RecomputeVolumeCheck()
    {
        if (!VolumeCheck || ActiveLayer is not { } layer || Scene.FrameCount <= 0)
        {
            ClearVolumeCheck();
            return;
        }

        var scene = Scene;
        var scale = Math.Min(1.0, (double)MeasureCap / Math.Max(1, Math.Max(scene.Width, scene.Height)));
        var rows = new VolumeRow[scene.FrameCount];
        var measured = new Dictionary<string, InkMoments>();

        for (var i = 0; i < scene.FrameCount; i++)
        {
            var frame = ExposureSheet.ExposedFrame(layer, i);
            InkMoments m = default;
            if (frame is not null && !measured.TryGetValue(frame.Id, out m))
            {
                using var bmp = FrameRasterizer.Rasterize(
                    frame.Strokes, scene.Width, scene.Height, scale,
                    new SKPointI(scene.Left, scene.Top));
                m = InkMoments.Measure(bmp);
                measured[frame.Id] = m;
            }
            rows[i] = m.HasInk
                ? new VolumeRow(
                    i, m.Area / (scale * scale), m.CentroidX / scale, m.CentroidY / scale,
                    HasInk: true, Flagged: false)
                : new VolumeRow(i, 0, 0, 0, HasInk: false, Flagged: false);
        }

        // Drift is judged against the shot's median, not its first frame: the
        // median is what most of the shot agrees the character's mass is, so
        // one bad drawing flags itself instead of flagging everything else.
        var inked = rows.Where(r => r.HasInk).Select(r => r.Area).OrderBy(a => a).ToList();
        if (inked.Count > 0)
        {
            var median = inked.Count % 2 == 1
                ? inked[inked.Count / 2]
                : (inked[inked.Count / 2 - 1] + inked[inked.Count / 2]) / 2;
            var tolerance = Math.Max(0, Settings.VolumeTolerance);
            if (median > 0)
            {
                for (var i = 0; i < rows.Length; i++)
                {
                    if (!rows[i].HasInk) continue;
                    var drift = Math.Abs(rows[i].Area - median) / median;
                    if (drift > tolerance) rows[i] = rows[i] with { Flagged = true };
                }
            }
        }

        _volumeRows = rows;
        OnPropertyChanged(nameof(VolumeRows));
    }

    private void ClearVolumeCheck()
    {
        if (_volumeRows.Count == 0) return;
        _volumeRows = [];
        OnPropertyChanged(nameof(VolumeRows));
    }
}
