using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.App.Input;
using Lightbox.App.Rendering;
using Lightbox.App.Services;
using Lightbox.Core.Documents;
using Lightbox.Core.Geometry;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Projects;
using Lightbox.Core.Serialization;
using Lightbox.Core.Timeline;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>The AI assistance surface: inbetweening, subject reading, and what the bar binds to.</summary>
/// <remarks>
/// Split out of <c>MainViewModel.cs</c> under Q75, which was 12,749 lines across 61
/// sections. Every field this file uses is either declared here — meaning no other
/// section touches it — or in the shared-state block at the top of
/// <c>MainViewModel.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainViewModel
{
    // ---- AI -----------------------------------------------------------------

    private CancellationTokenSource? _aiCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseAi))]
    private bool _aiBusy;

    [ObservableProperty]
    private string _aiStatus = "";

    public bool IsAiAvailable => _ai.IsAvailable;

    public bool CanUseAi => IsAiAvailable && !AiBusy;

    /// <summary>
    /// Whether AI assistance is switched on at all. The AI bar binds its
    /// visibility here rather than its enabled state: a studio that turns AI
    /// off wants it gone, not greyed, and a permanently disabled row is a
    /// worse answer than an absent one — the camera's rule again.
    /// </summary>
    public bool AiEnabled => _ai.Enabled;

    public string AiUnavailableHint => IsAiAvailable
        ? ""
        : "Choose an AI provider in Edit ▸ Configure ▸ AI — Claude, GPT, OpenRouter, a local model "
          + "through Ollama, any OpenAI-compatible endpoint, or an agent of your own over MCP. "
          + "No provider at all? Drive Lightbox from an MCP client instead — see the README.";

    /// <summary>Which provider is in use.</summary>
    public string AiProviderLabel => _ai.ProviderLabel;

    /// <summary>
    /// Rebuild the artist from what is stored. Called after the Configure
    /// window changes the connection, so a provider picked at 3pm is the one
    /// that draws at 3.01 without a restart.
    /// </summary>
    public void ReloadAiProvider()
    {
        _ai.Reload();
        OnPropertyChanged(nameof(IsAiAvailable));
        OnPropertyChanged(nameof(CanUseAi));
        OnPropertyChanged(nameof(AiEnabled));
        OnPropertyChanged(nameof(AiUnavailableHint));
        OnPropertyChanged(nameof(AiProviderLabel));
    }

    [RelayCommand]
    private void CancelAi() => _aiCts?.Cancel();

    /// <summary>
    /// The chosen model draws the inbetweens between the key at/before the
    /// playhead and the next key. Same insertion path as the deterministic
    /// engine — only the frame producer differs.
    /// </summary>
    [RelayCommand]
    private async Task AiInbetweenAsync()
    {
        if (_ai.Artist is null || AiBusy) return;
        var layer = ActiveLayer;
        // The AI paths are held to the same layer rules as the artist's own
        // hand: a hidden or locked layer refuses both. This guard used to live
        // only on the prompt-drawing command, so removing that would have left
        // the in-app AI able to write where a brush cannot.
        if (!CanEdit(layer, "insert inbetweens on it")) return;
        var aIndex = ExposureSheet.KeyIndexAtOrBefore(layer, CurrentFrameIndex);
        if (aIndex < 0) return;
        var bIndex = ExposureSheet.NextKeyIndex(layer, aIndex);
        if (bIndex < 0)
        {
            AiStatus = "Needs a second keyframe after the current one.";
            return;
        }

        // Worked out before the request goes, reported after it comes back: the
        // status line in between belongs to progress, and a warning written into
        // it now would be overwritten before anybody read it.
        var unseen = AiEnabled
            ? UnseenByTheModel(layer.Cels[aIndex].Frame!, layer.Cels[bIndex].Frame!)
            : null;

        // The extreme's timing chart is the ts when it has one (Q58): both
        // producers of inbetweens read the same ladder, so accepting the AI's
        // frames or the deterministic ones lands the same timing. A chart's
        // rungs are already eased — the artist placed them — so the easing
        // sent alongside is Linear rather than the bar's.
        var chart = layer.Cels[aIndex].Frame!.Chart;
        var ts = chart is { Count: > 0 }
            ? chart.ToList()
            : Enumerable.Range(1, TweenCount)
                .Select(k => (double)k / (TweenCount + 1))
                .ToList();
        // Send the effective drawings — erased strokes must not leak into
        // the model's input any more than into the deterministic tweens.
        var request = new InbetweenRequest(
            new SceneInfo(Scene.Width, Scene.Height, Scene.Fps),
            StrokeRecordCleaner.EffectiveStrokes(StrokesOf(layer.Cels[aIndex].Frame!)),
            StrokeRecordCleaner.EffectiveStrokes(StrokesOf(layer.Cels[bIndex].Frame!)),
            ts,
            chart is { Count: > 0 } ? Easing.Linear : TweenEasing,
            CollectReferenceImages(),
            TaxonomyForActiveDocument());

        var result = await RunAiAsync(
            $"{AiProviderLabel} is drawing {ts.Count} inbetween(s)…",
            ct => _ai.Artist.GenerateInbetweensAsync(request, ct));
        if (result is null) return;

        // The model proposes; Lightbox disposes. Every frame is verified
        // against the keys before it can reach the document, and a frame that
        // fails is refused rather than repaired or swapped for the
        // deterministic answer — per Q32, the AI never inserts a frame it
        // cannot defend, and the deterministic engine stays its own command.
        var ordered = result.OrderBy(f => f.T).ToList();
        var candidates = ordered
            .Select(f => new CandidateInbetween(f.T, f.Strokes))
            .ToList();
        // Judged against the easing the request carried — under a timing
        // chart that is Linear, and refusing a frame for sitting exactly on
        // its rung would be the verifier arguing with the artist.
        var judgement = InbetweenVerifier.Verify(
            request.KeyframeA, request.KeyframeB, candidates, request.Easing);

        // Refusal is per frame: the ones that passed are inserted, each at its
        // own t's slot — a null keeps the slot a hold, so partial acceptance
        // never shifts a surviving frame onto somebody else's timing.
        var provenance = new AiProvenance(AiProviderLabel, _ai.ModelLabel);
        var slots = new List<Frame?>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            if (!judgement.Frames[i].Accepted)
            {
                slots.Add(null);
                continue;
            }
            var frame = NewFrameFor(layer, ordered[i].Strokes, FrameRole.Inbetween);
            frame.Ai = provenance;
            slots.Add(frame);
        }

        var refused = judgement.Frames
            .Select((f, i) => (Judgement: f, Slot: i))
            .Where(x => !x.Judgement.Accepted)
            .Select(x => $"frame {x.Slot + 1} of {candidates.Count} was refused: {x.Judgement.Refusal}")
            .ToList();

        var accepted = judgement.AcceptedCount;
        if (accepted == 0)
        {
            // A refusal and a silent no-op are different outcomes: the document
            // is untouched, and the status says which t and why.
            AiStatus = $"Nothing was inserted — {string.Join(" ", refused)}";
            return;
        }

        _editor.InsertInbetweens(layer.Id, aIndex, slots);
        CurrentFrameIndex = Math.Min(aIndex + 1, Scene.FrameCount - 1);
        var inserted = unseen is null
            ? $"Inserted {accepted} AI inbetween(s)."
            : $"Inserted {accepted} AI inbetween(s) — drawn lines only, {unseen} not tweened.";
        AiStatus = refused.Count == 0 ? inserted : $"{inserted} {string.Join(" ", refused)}";
    }

    /// <summary>
    /// What the two keys hold that an inbetween cannot carry across, or null when
    /// they hold nothing but strokes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inbetweening — the model's and the deterministic engine's alike — works
    /// from the stroke record. An imported pixel baseline and a symbol placement
    /// are neither of them strokes, so a key holding one produces a tween with
    /// that part of the drawing missing. That is not new behaviour, but it is
    /// newly <em>reachable</em>: before the two frame classes merged, a layer
    /// created as Vector had nowhere to put either, and the two that could hold
    /// them were rarer.
    /// </para>
    /// <para>
    /// <b>This asks the frame what it holds, deliberately, rather than asking the
    /// layer what kind it is.</b> Keying it on <c>Layer.Kind</c> would be the
    /// obvious shortcut and would fire on every document that exists: every
    /// pre-merge layer is <c>LayerKind.Painted</c>, including the hand-drawn ones
    /// that have no baseline at all. A warning that appears on every old file
    /// teaches an artist to ignore warnings.
    /// </para>
    /// <para>
    /// Only worth saying when there is an AI to say it about, which is why the
    /// caller gates it on <c>AiEnabled</c> — a studio that switched AI off does
    /// not need to hear what the model would have missed. Q52.
    /// </para>
    /// </remarks>
    private static string? UnseenByTheModel(Frame a, Frame b) =>
        (a.HasBaseline || b.HasBaseline, a.HasPlacements || b.HasPlacements) switch
        {
            (true, true) => "imported pixels and placed symbols",
            (true, false) => "imported pixels",
            (false, true) => "placed symbols",
            _ => null,
        };

    /// <summary>
    /// What the project knows about the subject this document belongs to, or
    /// null when there is no project, no subject above it, or nothing read yet.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Walks up the folder tree rather than searching a list of
    /// characters, so a drawing two folders below Knight is still Knight's — the
    /// old model could not express that at all.
    /// <para>
    /// Null is the ordinary answer and costs nothing: a request with no
    /// taxonomy is byte-for-byte the request Lightbox sent before this feature
    /// existed. Optional means absent here too.
    /// </para>
    /// </remarks>
    private SubjectTaxonomy? TaxonomyForActiveDocument() =>
        ProjectDocker.Project is { } project && SaveTargetTab?.Source is { } source
            ? project.ReadingFor(source)?.Taxonomy
            : null;

    /// <summary>
    /// Read what the selected character is, from the sheets drawn of it, and
    /// keep the answer on the character.
    /// </summary>
    /// <remarks>
    /// Once per character rather than once per frame — the whole economic
    /// argument for storing it. A 24-frame cycle pays for this once, and the
    /// next animation of the same character pays nothing.
    ///
    /// It refuses to overwrite a reading somebody has edited. A guess is a
    /// default, never an override of something a person stated, and a re-read
    /// that silently discarded an artist's corrections would teach them not to
    /// make any.
    /// </remarks>
    [RelayCommand]
    private async Task AiReadSubjectAsync()
    {
        if (_ai.Artist is null || AiBusy) return;
        if (ProjectDocker.Project is null)
        {
            AiStatus = "Reading a subject needs a project — that is where a character lives.";
            return;
        }
        // B114. A folder, not a `Character` — and the folder need not already be
        // one, because reading it is what makes it one. Selecting an ordinary
        // folder full of a character's drawings and asking to read it is the
        // whole gesture; the old model needed the character to exist first.
        if (ProjectDocker.TargetFolder is not { } character)
        {
            AiStatus = "Select a folder in the Project panel first — that is what gets read.";
            return;
        }
        if (character.Taxonomy is { Reviewed: true })
        {
            AiStatus = $"“{character.Name}” has a reading you edited. Clear it first to read again.";
            return;
        }
        if (CollectReferenceImages() is not { Count: > 0 } sheets)
        {
            AiStatus = "No character sheet to read — draw one, or make a layer on it visible.";
            return;
        }

        var taxonomy = await RunAiAsync(
            $"{AiProviderLabel} is reading “{character.Name}”…",
            ct => _ai.Artist.ReadSubjectAsync(new SubjectRequest(character.Name, sheets), ct));
        if (taxonomy is null) return;

        character.Taxonomy = taxonomy;
        ProjectDocker.MarkManifestChanged();
        AiStatus = $"Read “{character.Name}”: {taxonomy.Kind}, "
                 + $"{Plural(taxonomy.Parts.Count, "part")}. Edit it and it will not be overwritten.";
    }

    private static string Plural(int n, string noun) => $"{n} {noun}{(n == 1 ? "" : "s")}";

    /// <summary>Shared busy/cancel/error plumbing for AI calls; null on failure.</summary>
    private async Task<T?> RunAiAsync<T>(string busyMessage, Func<CancellationToken, Task<AiResult<T>>> call)
        where T : class
    {
        _aiCts = new CancellationTokenSource();
        AiBusy = true;
        AiStatus = busyMessage;
        try
        {
            var result = await call(_aiCts.Token);
            if (result.Outcome == AiOutcome.Success) return result.Value;
            AiStatus = result.Message ?? "AI request failed.";
            return null;
        }
        finally
        {
            AiBusy = false;
            _aiCts.Dispose();
            _aiCts = null;
        }
    }
}
