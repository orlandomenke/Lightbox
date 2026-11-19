using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Rendering;
using Lightbox.Core.Cleanup;
using Lightbox.Core.Documents;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The AI line review (Q296–Q299): a model reads an inbetween beside its keys,
/// names the mistakes the inbetweener made, and the engine's fixes go up as a
/// cleanup preview, applied or cancelled like any other.
/// </summary>
/// <remarks>
/// <para>
/// <b>Optional, all the way down.</b> Nothing is sent unless the artist asks,
/// or turns on the opt-in after inserting (off by default, Q299); without a
/// provider the command says so and does nothing; a provider that cannot
/// review says so too. What comes back writes nothing: it is a preview.
/// </para>
/// <para>
/// <b>Inbetweens only.</b> A key or breakdown is what a review judges by
/// (Q298), not what it fixes — so it is never sent to be fixed, by the button,
/// the opt-in or an agent.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>Whether each inbetween ＋ Inbetween makes is sent for an AI line review (Q299). Off unless turned on.</summary>
    public bool ReviewNewInbetweensWithAi
    {
        get => Settings.ReviewNewInbetweensWithAi;
        set
        {
            if (Settings.ReviewNewInbetweensWithAi == value) return;
            Settings.ReviewNewInbetweensWithAi = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>What a review looks at: the drawing, its sheet, and its strokes as they were when it was read.</summary>
    internal sealed record ReviewTarget(Layer Layer, Frame Frame, ReviewSheet Sheet, List<Stroke> Before);

    /// <summary>
    /// The inbetween at a frame and the keys either side of it, numbered — or
    /// a sentence saying why there is nothing to review.
    /// </summary>
    internal (ReviewTarget? Target, string? Why) ReviewTargetAt(Layer layer, int index)
    {
        if (ExposureSheet.ExposedFrame(layer, index) is not { } frame) return (null, "There is no drawing here to review.");
        if (frame.Role != FrameRole.Inbetween)
            return (null, "AI review is for inbetweens — a key or a breakdown is what a review judges by, not what it fixes.");
        var at = -1;
        for (var i = 0; i < layer.Cels.Count; i++) if (ReferenceEquals(layer.Cels[i].Frame, frame)) { at = i; break; }
        var references = CleanupReferences.Around(layer, at);
        if (references.Count < 2) return (null, "An AI review needs a key or breakdown on both sides of the inbetween.");
        var sheet = ReviewSheet.For(frame.Strokes, references[0], references[1]);
        if (sheet.Lines.Count == 0) return (null, "This drawing has no lines the review can fix.");
        return (new ReviewTarget(layer, frame, sheet, [.. frame.Strokes.Select(s => s.Clone(newId: false))]), null);
    }

    /// <summary>Ask the configured AI to review the inbetween under the playhead.</summary>
    [RelayCommand]
    private async Task ReviewLinesWithAiAsync()
    {
        if (_ai.Artist is null)
        {
            AiStatus = "AI review needs a provider — choose one in Configure ▸ AI.";
            return;
        }
        if (AiBusy || ActiveLayer is not { } layer || !CanEdit(layer, "review its lines")) return;
        var (target, why) = ReviewTargetAt(layer, CurrentFrameIndex);
        if (target is null)
        {
            AiStatus = why!;
            return;
        }
        await ReviewAsync([target], byAgent: false);
    }

    /// <summary>
    /// Send each target for review in turn and put every fix that came back up
    /// as one preview. Returns how many drawings it would change.
    /// </summary>
    internal async Task<int> ReviewAsync(IReadOnlyList<ReviewTarget> targets, bool byAgent)
    {
        if (_ai.Artist is not { } artist || targets.Count == 0) return 0;
        var outcomes = new List<(ReviewTarget Target, ReviewOutcome Outcome)>();
        foreach (var target in targets)
        {
            var request = new LineReviewRequest(target.Sheet.Listing(), LineReviewPicture.Png(target.Sheet));
            var fixes = await RunAiAsync(
                $"{AiProviderLabel} is reviewing the lines{(targets.Count > 1 ? $" ({outcomes.Count + 1} of {targets.Count})" : "")}…",
                ct => artist.ReviewLinesAsync(request, ct));
            if (fixes is null) return 0;   // RunAiAsync has said why
            outcomes.Add((target, LineReview.Apply(target.Before, target.Sheet, fixes)));
        }
        return ProposeReview(outcomes, byAgent);
    }

    /// <summary>
    /// Put a review's fixes up as a preview. A drawing that changed while the
    /// review was out is caught by Apply, which compares against the strokes
    /// the review read.
    /// </summary>
    internal int ProposeReview(IReadOnlyList<(ReviewTarget Target, ReviewOutcome Outcome)> outcomes, bool byAgent)
    {
        var changed = outcomes.Where(o => o.Outcome.Result.Changed).ToList();
        var refused = outcomes.Sum(o => o.Outcome.Refused.Count);
        if (changed.Count == 0)
        {
            AiStatus = refused == 0
                ? "The AI review found no mistakes against the keys."
                : $"The AI review named {Plural(refused, "fix", "fixes")} and none could be made: {outcomes.SelectMany(o => o.Outcome.Refused).First().Why}";
            return 0;
        }
        if (IsCleanupPendingFromArtist && byAgent) return 0;
        CancelCleanup();

        var layer = changed[0].Target.Layer;
        var drawings = changed
            .Select(o => (o.Target.Frame.Id, o.Target.Before, o.Outcome.Result.Strokes.Select(s => s.Clone(newId: false)).ToList()))
            .ToList();
        var reasons = changed.SelectMany(o => o.Outcome.Made)
            .Select(f => $"L{f.Line}: {f.Reason} ({string.Join(", ", f.Cites)})")
            .Distinct()
            .Take(3)
            .ToList();
        var because = " AI review — " + string.Join(" ", reasons)
            + (refused > 0 ? $" {Plural(refused, "named fix was", "named fixes were")} not made." : "");
        InstallPendingCleanup(layer, drawings, [.. changed.SelectMany(o => o.Outcome.Result.Changes)], byAgent, because);
        return drawings.Count;
    }

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    /// <summary>
    /// After ＋ Inbetween, when the artist has asked for it (Q299): send the
    /// new inbetweens for review, and leave what comes back as a preview. Never
    /// replaces a preview the artist already has open.
    /// </summary>
    private async Task ReviewInsertedIfAskedAsync(string layerId, IReadOnlyList<string> frameIds)
    {
        if (!ReviewNewInbetweensWithAi || _ai.Artist is null || AiBusy || IsCleanupPending || frameIds.Count == 0) return;
        if (Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer) return;
        var targets = new List<ReviewTarget>();
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (layer.Cels[i].Frame is not { } frame || !frameIds.Contains(frame.Id)) continue;
            if (ReviewTargetAt(layer, i).Target is { } target) targets.Add(target);
        }
        try
        {
            await ReviewAsync(targets, byAgent: false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Nobody awaits this: a failure is said, not thrown into the void.
            AiStatus = $"The AI review after inserting failed: {e.Message}";
        }
    }
}
