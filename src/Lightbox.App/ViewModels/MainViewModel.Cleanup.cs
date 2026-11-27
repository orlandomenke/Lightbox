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
    /// <remarks>
    /// <b>Light by default</b> (art-director review): on a drawing of the
    /// artist's own the cleaner should take the least it can, and Medium
    /// already irons out small deliberate shapes on a sketch. For this session
    /// only, like the inbetween count and easing beside it on the bar.
    /// </remarks>
    [ObservableProperty]
    private CleanupStrength _cleanupStrength = CleanupStrength.Light;

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
        IReadOnlyList<(string FrameId, List<Stroke> Before, List<Stroke> After)> Drawings,
        bool ByAgent, string? Label = null);

    private PendingCleanup? _pendingCleanup;

    /// <summary>The drawing the current overlay was built for.</summary>
    private string? _cleanupViewFrameId;

    /// <summary>The pending cleanup described as done, for the status once it is.</summary>
    private string _appliedSummary = "";

    /// <summary>Whether the pending preview is one the artist asked for, which an agent must not replace.</summary>
    public bool IsCleanupPendingFromArtist => _pendingCleanup is { ByAgent: false };

    /// <summary>What the most recent preview proposed, stroke by stroke, for the MCP surface's answer.</summary>
    public IReadOnlyList<CleanupChange> LastCleanupChanges { get; private set; } = [];

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
        var targets = CleanupTargets(layer, guided, out var skipped);
        if (targets.Count == 0)
        {
            AiStatus = $"This drawing is a {skipped} — your own. Pick its cel on the timeline to clean it toward its neighbours.";
            return 0;
        }
        return PreviewCleanup(layer, targets, guided, CleanupStrength);
    }

    /// <summary>The same, for named drawings of a named layer — the MCP surface's way in.</summary>
    public int PreviewCleanup(Layer layer, IReadOnlyList<int> indices, bool guided, CleanupStrength strength, bool byAgent = false)
    {
        CancelCleanup();
        if (!CanEdit(layer, "clean its lines")) return 0;

        var drawings = new List<(string, List<Stroke>, List<Stroke>)>();
        var changes = new List<CleanupChange>();
        var seen = new HashSet<string>();
        var unguided = 0;
        var tooLarge = 0;
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
                result = LineCleaner.Clean(frame.Strokes, strength);
            }
            if (result.TooLarge) tooLarge++;
            if (!result.Changed) continue;
            drawings.Add((frame.Id, [.. frame.Strokes.Select(s => s.Clone(newId: false))], [.. result.Strokes.Select(s => s.Clone(newId: false))]));
            changes.AddRange(result.Changes);
        }

        if (drawings.Count == 0)
        {
            AiStatus = tooLarge > 0
                ? $"This drawing has more lines than the cleaner works on at once (over {LineCleaner.MaxLines} lines or {LineCleaner.MaxPoints:N0} points) — nothing was changed."
                : guided && unguided > 0
                    ? "There is no hand-drawn key or breakdown either side of this drawing to clean toward."
                    : "The lines are already clean — nothing to change.";
            return 0;
        }

        InstallPendingCleanup(layer, drawings, changes, byAgent);
        return drawings.Count;
    }

    /// <summary>
    /// Put a proposal up for Apply or Cancel — the one way any cleanup, the
    /// cleaner's or a review's, reaches the canvas.
    /// </summary>
    /// <param name="because">A sentence on why, said after what would change; the review's reasons go here.</param>
    private void InstallPendingCleanup(
        Layer layer, List<(string, List<Stroke>, List<Stroke>)> drawings, List<CleanupChange> changes, bool byAgent,
        string because = "", string? label = null)
    {
        _pendingCleanup = new PendingCleanup(_editor, layer.Id, drawings, byAgent, label);
        LastCleanupChanges = changes;
        CleanupSummary = Describe(changes, drawings.Count, preview: true) + because + " Apply, or Cancel to leave the drawing as it is.";
        _appliedSummary = Describe(changes, drawings.Count, preview: false);
        AiStatus = CleanupSummary;
        _cleanupViewFrameId = null;
        CleanupView = null;
        RefreshCleanupView();
        OnPropertyChanged(nameof(IsCleanupPending));
        OnPropertyChanged(nameof(IsCleanupPendingFromArtist));
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
            label: (pending.Label ?? "Clean lines") + (drawings.Count == 1 ? "" : $" on {drawings.Count} drawings") + (pending.ByAgent ? " (agent)" : ""));

        foreach (var (frameId, _, _) in drawings) AfterStrokeEdit(frameId);
        var said = _appliedSummary;
        ClearPendingCleanup();
        AiStatus = said + " Undo puts it back.";
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
        _cleanupViewFrameId = null;
        CleanupView = null;
        CleanupSummary = "";
        RefreshCleanupView();
        OnPropertyChanged(nameof(IsCleanupPending));
        OnPropertyChanged(nameof(IsCleanupPendingFromArtist));
    }

    /// <summary>
    /// The overlay for the drawing under the playhead: the pending cleanup's
    /// changed lines in their new places and its removed marks where they are.
    /// </summary>
    public void RefreshCleanupView()
    {
        // Held drawings and playback move the playhead without changing which
        // drawing is shown; the overlay, and the paths built from it, stay.
        var shownId = _pendingCleanup is { } p0
            && Scene.Layers.FirstOrDefault(l => l.Id == p0.LayerId) is { } l0
            && ExposureSheet.ExposedFrame(l0, CurrentFrameIndex) is { } f0 ? f0.Id : null;
        if (CleanupView is not null && shownId == _cleanupViewFrameId) return;
        _cleanupViewFrameId = shownId;
        CleanupOverlay? view = null;
        if (_pendingCleanup is { } pending
            && Scene.Layers.FirstOrDefault(l => l.Id == pending.LayerId) is { } layer
            && ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is { } shown
            && pending.Drawings.FirstOrDefault(d => d.FrameId == shown.Id) is { FrameId: not null } drawing)
        {
            var after = new Dictionary<string, Stroke>();
            foreach (var s in drawing.After) after.TryAdd(s.Id, s);
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

    /// <summary>
    /// The drawings a cleanup reaches: the cels picked on this layer, or the
    /// one under the playhead. Toward keys, the one under the playhead is
    /// skipped when it is a key or a breakdown (Q294) — those are cleaned
    /// toward their neighbours only when the artist picks them.
    /// </summary>
    private List<int> CleanupTargets(Layer layer, bool guided, out string skipped)
    {
        skipped = "";
        var layerIndex = Scene.Layers.IndexOf(layer);
        var picked = CelSelection
            .Where(c => c.Layer == layerIndex && ExposureSheet.FrameAtExactIndex(layer, c.Index) is not null)
            .Select(c => c.Index)
            .Order()
            .ToList();
        if (picked.Count > 0) return picked;
        if (guided && ExposureSheet.ExposedFrame(layer, CurrentFrameIndex) is { Role: not FrameRole.Inbetween } own)
        {
            skipped = own.Role == FrameRole.Breakdown ? "breakdown" : "key";
            return [];
        }
        return [CurrentFrameIndex];
    }

    private static int FrameIndexOf(Layer layer, Frame frame)
    {
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (ReferenceEquals(layer.Cels[i].Frame, frame)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Whether a drawing is still the one a preview was made from — every
    /// stroke whole, not only its points: a line recoloured, rebound to a bone
    /// or given another brush after the preview would otherwise be silently
    /// put back by Apply (adversary review).
    /// </summary>
    private static bool Unchanged(Layer layer, string frameId, List<Stroke> before)
    {
        var frame = layer.Cels.Select(c => c.Frame).FirstOrDefault(f => f?.Id == frameId);
        return frame is not null && CleanupFingerprint(frame.Strokes) == CleanupFingerprint(before);
    }

    private static string CleanupFingerprint(List<Stroke> strokes) =>
        System.Text.Json.JsonSerializer.Serialize(strokes, Lightbox.Core.Serialization.DocJson.Compact);

    /// <summary>
    /// "Would smooth 6 lines, trim 2 overshoots …; nothing moves more than 2 px."
    /// before the cleanup is written, and "Smoothed 6 lines …" after: a preview
    /// in the past tense would contradict "nothing changes until you apply it".
    /// Numbers in the invariant culture, because the same sentence is an MCP
    /// reply an agent parses.
    /// </summary>
    internal static string Describe(IReadOnlyList<CleanupChange> changes, int drawings, bool preview)
    {
        var parts = new List<string>();
        void Add(CleanupKind kind, string verb, string noun, string nouns)
        {
            var n = changes.Where(c => c.Kind == kind).Select(c => c.StrokeId).Distinct().Count();
            if (n > 0) parts.Add($"{verb} {n} {(n == 1 ? noun : nouns)}");
        }
        Add(CleanupKind.Smoothed, preview ? "smooth" : "smoothed", "line", "lines");
        Add(CleanupKind.Trimmed, preview ? "trim" : "trimmed", "overshoot", "overshoots");
        Add(CleanupKind.Joined, preview ? "close" : "closed", "gap", "gaps");
        Add(CleanupKind.Removed, preview ? "remove" : "removed", "speck", "specks");
        var moved = changes.Where(c => c.Kind != CleanupKind.Removed).Select(c => c.Moved).DefaultIfEmpty(0).Max();
        var sentence = (preview ? "Would " : "") + string.Join(", ", parts);
        sentence = char.ToUpperInvariant(sentence[0]) + sentence[1..];
        if (drawings > 1) sentence += $" on {drawings} drawings";
        var px = moved.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return preview ? $"{sentence}; nothing moves more than {px} px." : $"{sentence}; nothing moved more than {px} px.";
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
        if (references.Count == 0) return drawn;
        // One measurement of the two keys serves every inbetween of the gap.
        if (_guideFor is not { } cached || !cached.Refs.SequenceEqual(references, ReferenceEqualityComparer.Instance))
        {
            _guideFor = (references, LineCleaner.Measure(references));
        }
        return _guideFor.Value.Guide is { } guide ? LineCleaner.CleanGuided(drawn, guide).Strokes : drawn;
    }

    /// <summary>The last references measured for the opt-in, and what was measured; held by identity, never compared by content.</summary>
    private (List<IReadOnlyList<Stroke>> Refs, LineCleaner.CleanupGuide? Guide)? _guideFor;
}
