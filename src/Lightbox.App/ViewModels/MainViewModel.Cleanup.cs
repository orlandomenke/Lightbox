using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>One line of a cleanup preview: where it would be, and what was done to it.</summary>
public sealed record CleanupLine(IReadOnlyList<StrokePoint> Points, CleanupKind Kind);

/// <summary>
/// What a cleanup preview shows on the canvas: the lines it would change, in
/// their new places, and the marks it would remove, where they are now.
/// </summary>
public sealed record CleanupOverlay(IReadOnlyList<CleanupLine> Lines);

/// <summary>
/// The line cleaner (Q292–Q295) as the artist reaches it: a preview of a
/// cleanup, applied as one undo step or cancelled, and the opt-in that cleans
/// generated inbetweens as they arrive.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing is written until Apply (Q292).</b> The preview holds a proposal
/// and the canvas draws it as chrome — the changed lines in their new places
/// over the drawing as it is — so the artist compares the two on the page.
/// A preview made against a drawing that has since changed is not applied:
/// it would write a cleanup of something that is no longer there.
/// </para>
/// <para>
/// <b>Which drawings (Q294).</b> The drawings picked on the active layer's
/// timeline, or the drawing under the playhead. A key is cleaned only when the
/// artist runs a command on it; the one thing that runs by itself is the
/// opt-in, and it reaches only drawings the inbetweener has just made.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>How hard Clean lines smooths. Toward keys measures its own.</summary>
    [ObservableProperty]
    private CleanupStrength _cleanupStrength = CleanupStrength.Medium;

    public IReadOnlyList<CleanupStrength> CleanupStrengths { get; } =
        [CleanupStrength.Light, CleanupStrength.Medium, CleanupStrength.Strong];

    /// <summary>
    /// Whether ＋ Inbetween and Redraw inbetweens clean each drawing they make
    /// toward the keys either side, in the same undo step (Q294). Off unless
    /// the artist turns it on.
    /// </summary>
    public bool CleanNewInbetweens
    {
        get => Settings.CleanNewInbetweens;
        set
        {
            if (Settings.CleanNewInbetweens == value) return;
            Settings.CleanNewInbetweens = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>The preview the canvas draws; null when there is none.</summary>
    public CleanupOverlay? CleanupView { get; private set; }

    /// <summary>Raised whenever <see cref="CleanupView"/> changes, for the window to push to the canvas.</summary>
    public event Action<CleanupOverlay?>? CleanupChanged;

    /// <summary>Whether a cleanup is waiting for Apply or Cancel.</summary>
    public bool IsCleanupPending => _pendingCleanup is not null;

    /// <summary>What the pending cleanup would do, in a sentence, for the bar that offers Apply.</summary>
    [ObservableProperty]
    private string _cleanupSummary = "";

    /// <summary>
    /// A proposal: for each drawing, the strokes as they were when it was made
    /// and as the cleanup would leave them. Held by ids, never by the live
    /// objects, for the reason every delta step is.
    /// </summary>
    private sealed record PendingCleanup(
        object Editor, string LayerId,
        IReadOnlyList<(string FrameId, List<Stroke> Before, List<Stroke> After)> Drawings);

    private PendingCleanup? _pendingCleanup;

    /// <summary>Preview a cleanup of the picked drawings, or the one under the playhead, on their own.</summary>
    [RelayCommand]
    private void CleanLines() => PreviewCleanup(guided: false);

    /// <summary>Preview a cleanup of the picked drawings toward the hand-drawn frames around each.</summary>
    [RelayCommand]
    private void CleanLinesTowardKeys() => PreviewCleanup(guided: true);

    /// <summary>
    /// Work out a cleanup and show it, writing nothing. Returns how many
    /// drawings it would change, for the tests and the MCP surface.
    /// </summary>
    public int PreviewCleanup(bool guided)
    {
        CancelCleanup();
        var layer = ActiveLayer;
        if (layer is null) return 0;
        if (!CanEdit(layer, "clean its lines")) return 0;

        var indices = CleanupTargets(layer);
        var drawings = new List<(string, List<Stroke>, List<Stroke>)>();
        var changes = new List<CleanupChange>();
        var seen = new HashSet<string>();
        var unguided = 0;
        foreach (var index in indices)
        {
            if (ExposureSheet.ExposedFrame(layer, index) is not { } frame || !seen.Add(frame.Id)) continue;
            CleanupResult result;
            if (guided)
            {
                var references = CleanupReferences.Around(layer, FrameIndexOf(layer, frame));
                if (references.Count == 0)
                {
                    unguided++;
                    continue;
                }
                result = LineCleaner.CleanGuided(frame.Strokes, references);
            }
            else
            {
                result = LineCleaner.Clean(frame.Strokes, CleanupStrength);
            }
            if (!result.Changed) continue;
            drawings.Add((frame.Id, [.. frame.Strokes.Select(s => s.Clone(newId: false))], [.. result.Strokes.Select(s => s.Clone(newId: false))]));
            changes.AddRange(result.Changes);
        }

        if (drawings.Count == 0)
        {
            AiStatus = guided && unguided > 0
                ? "There is no hand-drawn key or breakdown either side of this drawing to clean toward."
                : "The lines are already clean — nothing to change.";
            return 0;
        }

        _pendingCleanup = new PendingCleanup(_editor, layer.Id, drawings);
        CleanupSummary = Describe(changes, drawings.Count) + " Apply, or Cancel to leave the drawing as it is.";
        AiStatus = CleanupSummary;
        RefreshCleanupView();
        OnPropertyChanged(nameof(IsCleanupPending));
        return drawings.Count;
    }

    [RelayCommand]
    private void ApplyCleanup() => TryApplyCleanup();

    /// <summary>Write the pending cleanup as one undo step. Returns whether it did.</summary>
    public bool TryApplyCleanup()
    {
        if (_pendingCleanup is not { } pending) return false;
        var layer = Scene.Layers.FirstOrDefault(l => l.Id == pending.LayerId);
        // Made against another document, or a drawing that has changed since:
        // what it would write is a cleanup of something no longer there.
        if (!ReferenceEquals(pending.Editor, _editor) || layer is null
            || pending.Drawings.Any(d => !Unchanged(layer, d.FrameId, d.Before)))
        {
            CancelCleanup();
            AiStatus = "The drawing changed after the preview, so the cleanup was not applied — run it again.";
            return false;
        }
        if (!CanEdit(layer, "clean its lines")) return false;

        var drawings = pending.Drawings;
        _editor.PerformDelta(
            apply: doc =>
            {
                foreach (var (frameId, _, after) in drawings)
                {
                    if (FrameIn(doc, frameId) is { } f) f.Strokes = [.. after.Select(s => s.Clone(newId: false))];
                }
            },
            revert: doc =>
            {
                foreach (var (frameId, before, _) in drawings)
                {
                    if (FrameIn(doc, frameId) is { } f) f.Strokes = [.. before.Select(s => s.Clone(newId: false))];
                }
            },
            affectedFrameId: drawings.Count == 1 ? drawings[0].FrameId : null,
            label: drawings.Count == 1 ? "Clean lines" : $"Clean lines on {drawings.Count} drawings");

        foreach (var (frameId, _, _) in drawings) AfterStrokeEdit(frameId);
        var said = CleanupSummary;
        ClearPendingCleanup();
        AiStatus = "Cleaned. " + said.Replace(" Apply, or Cancel to leave the drawing as it is.", "") + " Ctrl+Z puts it back.";
        return true;
    }

    /// <summary>Drop the pending cleanup; the drawing was never touched.</summary>
    [RelayCommand]
    public void CancelCleanup()
    {
        if (_pendingCleanup is null) return;
        ClearPendingCleanup();
    }

    private void ClearPendingCleanup()
    {
        _pendingCleanup = null;
        CleanupSummary = "";
        RefreshCleanupView();
        OnPropertyChanged(nameof(IsCleanupPending));
    }

    /// <summary>
    /// The overlay for the drawing under the playhead: the pending cleanup's
    /// changed lines in their new places and its removed marks where they are.
    /// </summary>
    public void RefreshCleanupView()
    {
        CleanupOverlay? view = null;
        if (_pendingCleanup is { } pending
            && Scene.Layers.FirstOrDefault(l => l.Id == pending.LayerId) is { } layer
            && ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is { } shown
            && pending.Drawings.FirstOrDefault(d => d.FrameId == shown.Id) is { FrameId: not null } drawing)
        {
            var after = drawing.After.ToDictionary(s => s.Id);
            var lines = new List<CleanupLine>();
            foreach (var before in drawing.Before)
            {
                if (!after.TryGetValue(before.Id, out var now))
                {
                    if (LineCleaner.Cleanable(before)) lines.Add(new CleanupLine(before.Points, CleanupKind.Removed));
                    continue;
                }
                if (ReferenceEquals(now.Points, before.Points) || now.Points.SequenceEqual(before.Points)) continue;
                var kind = now.Points.Count != before.Points.Count ? (now.Points.Count < before.Points.Count ? CleanupKind.Trimmed : CleanupKind.Joined) : CleanupKind.Smoothed;
                lines.Add(new CleanupLine(now.Points, kind));
            }
            view = new CleanupOverlay(lines);
        }
        CleanupView = view;
        CleanupChanged?.Invoke(view);
    }

    /// <summary>The drawings a cleanup reaches: the cels picked on this layer, or the one under the playhead.</summary>
    private List<int> CleanupTargets(Layer layer)
    {
        var layerIndex = Scene.Layers.IndexOf(layer);
        var picked = CelSelection
            .Where(c => c.Layer == layerIndex && ExposureSheet.FrameAtExactIndex(layer, c.Index) is not null)
            .Select(c => c.Index)
            .Order()
            .ToList();
        return picked.Count > 0 ? picked : [CurrentFrameIndex];
    }

    private static int FrameIndexOf(Layer layer, Frame frame)
    {
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (ReferenceEquals(layer.Cels[i].Frame, frame)) return i;
        }
        return -1;
    }

    /// <summary>Whether a drawing's strokes are still the ones a preview was made from.</summary>
    private static bool Unchanged(Layer layer, string frameId, List<Stroke> before)
    {
        var frame = layer.Cels.Select(c => c.Frame).FirstOrDefault(f => f?.Id == frameId);
        if (frame is null || frame.Strokes.Count != before.Count) return false;
        for (var i = 0; i < before.Count; i++)
        {
            var now = frame.Strokes[i];
            if (now.Id != before[i].Id || !now.Points.SequenceEqual(before[i].Points)) return false;
        }
        return true;
    }

    /// <summary>"Smoothed 6 lines, trimmed 2 ends, removed 1 speck on 2 drawings."</summary>
    internal static string Describe(IReadOnlyList<CleanupChange> changes, int drawings)
    {
        var parts = new List<string>();
        void Add(CleanupKind kind, string one, string many)
        {
            var n = changes.Where(c => c.Kind == kind).Select(c => c.StrokeId).Distinct().Count();
            if (n > 0) parts.Add($"{(n == 1 ? one : string.Format(many, n))}");
        }
        Add(CleanupKind.Smoothed, "smoothed 1 line", "smoothed {0} lines");
        Add(CleanupKind.Trimmed, "trimmed 1 overshoot", "trimmed {0} overshoots");
        Add(CleanupKind.Joined, "closed 1 gap", "closed {0} gaps");
        Add(CleanupKind.Removed, "removed 1 speck", "removed {0} specks");
        var moved = changes.Where(c => c.Kind != CleanupKind.Removed).Select(c => c.Moved).DefaultIfEmpty(0).Max();
        var sentence = string.Join(", ", parts);
        sentence = char.ToUpperInvariant(sentence[0]) + sentence[1..];
        if (drawings > 1) sentence += $" on {drawings} drawings";
        return $"{sentence}; nothing moved more than {moved:0.#} px.";
    }

    /// <summary>
    /// A generated inbetween, cleaned toward the drawings either side of it
    /// when the artist has asked for that (Q294) — otherwise exactly as made.
    /// </summary>
    private List<Stroke> CleanedIfAsked(List<Stroke> drawn, double time, IReadOnlyList<Inbetweener.RunStop> stops)
    {
        if (!CleanNewInbetweens || stops.Count == 0) return drawn;
        var before = stops.LastOrDefault(s => s.Time <= time);
        var after = stops.FirstOrDefault(s => s.Time >= time);
        var references = new List<IReadOnlyList<Stroke>>();
        if (before.Strokes is not null) references.Add(before.Strokes);
        if (after.Strokes is not null && !ReferenceEquals(after.Strokes, before.Strokes)) references.Add(after.Strokes);
        return references.Count == 0 ? drawn : LineCleaner.CleanGuided(drawn, references).Strokes;
    }
}
