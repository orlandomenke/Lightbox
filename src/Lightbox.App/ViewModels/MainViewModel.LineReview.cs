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

    /// <summary>
    /// The review the opt-in started after the last ＋ Inbetween, or a finished
    /// task — what nobody else awaits, held so it can be (tests, and a later
    /// insert that should not overlap it).
    /// </summary>
    public Task ReviewAfterInsert { get; private set; } = Task.CompletedTask;

    /// <summary>What a review looks at: the drawing, its sheet, and its strokes as they were when it was read.</summary>
    /// <param name="Editor">The document's editor when the review was read — the preview belongs to it, not to whichever tab is open when the answer comes.</param>
    internal sealed record ReviewTarget(Layer Layer, Frame Frame, ReviewSheet Sheet, List<Stroke> Before, object Editor);

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
        if (sheet.TooLarge)
            return (null, $"These drawings have more lines than a review reads at once (over {LineCleaner.MaxLines} lines or {LineCleaner.MaxPoints:N0} points) — nothing was sent.");
        return (new ReviewTarget(layer, frame, sheet, [.. frame.Strokes.Select(s => s.Clone(newId: false))], _editor), null);
    }

    /// <summary>Ask the configured AI to review the inbetween under the playhead.</summary>
    [RelayCommand]
    private async Task ReviewLinesWithAiAsync()
    {
        if (_ai.Artist is null)
        {
            AiStatus = AiUnavailableHint;
            return;
        }
        if (AiBusy)
        {
            AiStatus = "The AI is busy with another request — try again when it has answered.";
            return;
        }
        if (ActiveLayer is not { } layer || !CanEdit(layer, "review its lines")) return;
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
    internal async Task<int> ReviewAsync(IReadOnlyList<ReviewTarget> targets, bool byAgent, bool automatic = false)
    {
        if (_ai.Artist is not { } artist || targets.Count == 0) return 0;
        var outcomes = new List<(ReviewTarget Target, ReviewOutcome Outcome)>();
        var failed = 0;
        foreach (var target in targets)
        {
            // The listing and the picture are built off the thread the artist
            // draws on (perf review), and the picture only for a provider that
            // looks at it.
            var takesImages = artist.TakesImages;
            var request = await Task.Run(() => new LineReviewRequest(
                target.Sheet.Listing(keyNeighbours: false),
                takesImages ? LineReviewPicture.Png(target.Sheet) : null,
                takesImages ? null : target.Sheet.Listing(keyNeighbours: true)));
            var fixes = await RunAiAsync(
                $"{AiProviderLabel} is reviewing the lines{(targets.Count > 1 ? $" ({outcomes.Count + failed + 1} of {targets.Count})" : "")}…",
                ct => artist.ReviewLinesAsync(request, ct));
            // One failure keeps the reviews already paid for (ai-engineer).
            if (fixes is null) { failed++; continue; }
            outcomes.Add((target, LineReview.Apply(target.Before, target.Sheet, fixes)));
        }
        var why = AiStatus;
        var count = outcomes.Count == 0 ? 0 : ProposeReview(outcomes, byAgent, automatic);
        if (failed > 0) AiStatus = $"{AiStatus} {Plural(failed, "review", "reviews")} failed: {why}";
        return count;
    }

    /// <summary>
    /// Put a review's fixes up as a preview. A drawing that changed while the
    /// review was out is caught by Apply, which compares against the strokes
    /// the review read.
    /// </summary>
    internal int ProposeReview(IReadOnlyList<(ReviewTarget Target, ReviewOutcome Outcome)> outcomes, bool byAgent, bool automatic = false)
    {
        // A review that came back to another document, or — for one nobody
        // pressed a button for — to a canvas where a preview is now open, is
        // not put up: the artist's preview is theirs (every reviewer flagged it).
        if (outcomes.Any(o => !ReferenceEquals(o.Target.Editor, _editor)))
        {
            AiStatus = "The AI review came back after you switched documents, so it was not shown.";
            return 0;
        }
        if (automatic && IsCleanupPending)
        {
            AiStatus = "The AI review of the new inbetweens finished while a cleanup preview was open, so it was not shown — run Review lines with AI… when you are ready.";
            return 0;
        }
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
        // In drawing terms, as the prompt asks: the artist sees no line numbers.
        var all = changed.SelectMany(o => o.Outcome.Made)
            .Select(f => Sentence(f.Reason))
            .Where(r => r.Length > 0)
            .Distinct()
            .ToList();
        var because = " AI review: " + string.Join("; ", all.Take(3)) + (all.Count > 3 ? $"; and {all.Count - 3} more" : "") + "."
            + (refused > 0 ? $" {Plural(refused, "named fix was", "named fixes were")} not made." : "");
        InstallPendingCleanup(layer, drawings, [.. changed.SelectMany(o => o.Outcome.Result.Changes)], byAgent, because, label: "AI line review");
        return drawings.Count;
    }

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    /// <summary>A model's reason, trimmed to a clause: no trailing full stop, at most 80 characters.</summary>
    private static string Sentence(string reason)
    {
        var r = reason.Trim().TrimEnd('.');
        return r.Length <= 80 ? r : r[..79].TrimEnd() + "…";
    }

    /// <summary>
    /// After ＋ Inbetween, when the artist has asked for it (Q299): send the
    /// new inbetweens for review, and leave what comes back as a preview. Never
    /// replaces a preview the artist already has open.
    /// </summary>
    private async Task ReviewInsertedIfAskedAsync(string layerId, IReadOnlyList<string> frameIds)
    {
        if (!ReviewNewInbetweensWithAi || frameIds.Count == 0) return;
        if (_ai.Artist is null)
        {
            AiStatus = "Review new inbetweens with AI is on, but no AI provider is chosen — nothing was sent.";
            return;
        }
        if (AiBusy || IsCleanupPending) return;
        if (Scene.Layers.FirstOrDefault(l => l.Id == layerId) is not { } layer || !Scene.IsLayerEditable(layer)) return;
        var targets = new List<ReviewTarget>();
        for (var i = 0; i < layer.Cels.Count; i++)
        {
            if (layer.Cels[i].Frame is not { } frame || !frameIds.Contains(frame.Id)) continue;
            if (ReviewTargetAt(layer, i).Target is { } target) targets.Add(target);
        }
        try
        {
            await ReviewAsync(targets, byAgent: false, automatic: true);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Nobody awaits this: a failure is said, not thrown into the void.
            AiStatus = $"The AI review after inserting failed: {e.Message}";
        }
    }
}
