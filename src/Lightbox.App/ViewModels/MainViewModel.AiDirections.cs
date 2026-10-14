using CommunityToolkit.Mvvm.Input;
using Lightbox.Ai;
using Lightbox.Core.Documents;
using Lightbox.Core.Inbetween;
using Lightbox.Core.Timeline;

namespace Lightbox.App.ViewModels;

/// <summary>What the direction popup is being asked for.</summary>
public enum DirectionAsk
{
    /// <summary>A note to go with a new AI inbetween.</summary>
    Inbetween,

    /// <summary>What should change about AI frames already drawn.</summary>
    Revise,
}

/// <summary>Part of MainViewModel — see MainViewModel.cs.</summary>
/// <remarks>
/// <para>
/// Q261: directions for the AI inbetweener. The AI Inbetween button stays one
/// click; a second way in asks for a note first, and <b>Revise</b> redraws AI
/// frames already on the timeline with the artist's note. Both go through
/// <see cref="InbetweenRepair"/> like every AI request, so the verifier still
/// decides what lands — a note changes what is asked, never what is accepted.
/// </para>
/// <para>
/// Kept out of <c>MainViewModel.Ai.cs</c> because the one thing the two
/// commands share with it is the request shape; the bounds, the selection and
/// the in-place replacement are theirs alone.
/// </para>
/// </remarks>
public partial class MainViewModel
{
    /// <summary>
    /// Asks the artist for a note. Null is a cancel, which is not the same as an
    /// empty answer. Set by the window; a test sets it to a scripted answer.
    /// </summary>
    public Func<DirectionAsk, Task<string?>>? AskDirection { get; set; }

    /// <summary>
    /// The longest note sent or kept. A note is stored on every frame it
    /// produced, so an essay pasted by accident would be written dozens of
    /// times over; a direction is a sentence or two.
    /// </summary>
    public const int MaxDirectionLength = 500;

    /// <summary>A note trimmed and capped, or null when there is nothing in it.</summary>
    private static string? Direction(string? note)
    {
        if (string.IsNullOrWhiteSpace(note)) return null;
        note = note.Trim();
        return note.Length <= MaxDirectionLength ? note : note[..MaxDirectionLength];
    }

    /// <summary>
    /// AI Inbetween with a note first — the arrow beside the button, a
    /// Shift+click on it, or its shortcut. An empty note is the plain button.
    /// </summary>
    [RelayCommand]
    private async Task AiInbetweenWithDirectionAsync()
    {
        if (_ai.Artist is null || AiBusy || AskDirection is null) return;
        var note = await AskDirection(DirectionAsk.Inbetween);
        if (note is null) return;
        await InbetweenWithAiAsync(Direction(note));
    }

    /// <summary>
    /// Redraw the AI frames between the drawings around the playhead with the
    /// artist's note — or, when some of them are selected on the timeline, only
    /// those. One undo step; a frame the verifier refuses keeps its old drawing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The bounds are the nearest drawings the artist made, not the nearest
    /// keys by role.</b> An AI frame is what is being replaced, so it can never
    /// be an end; anything else the artist drew — a key, a breakdown, a frame
    /// they redrew by hand — is something the revision has to sit between,
    /// which is also what the verifier will judge it against.
    /// </para>
    /// <para>
    /// The t of each frame is its place between the bounds — or the key's own
    /// timing chart when it has one rung per slot, because that is what the
    /// first ask used.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task AiReviseAsync()
    {
        if (_ai.Artist is null || AiBusy) return;
        var layer = ActiveLayer;
        if (!CanEdit(layer, "revise its frames")) return;

        var (a, b) = ArtistDrawnBounds(layer, CurrentFrameIndex);
        if (a < 0 || b < 0)
        {
            AiStatus = "Revise works between two drawings you made — put the playhead before the last one.";
            return;
        }
        var aiFrames = Enumerable.Range(a + 1, b - a - 1).Where(i => layer.Cels[i].Frame?.Ai is not null).ToList();
        if (aiFrames.Count == 0)
        {
            AiStatus = "There are no AI frames between these drawings to revise.";
            return;
        }

        var layerIndex = ActiveLayerIndex;
        var selected = CelSelection.Where(c => c.Layer == layerIndex).Select(c => c.Index).ToHashSet();
        var redraw = aiFrames.Where(selected.Contains).ToList();
        if (redraw.Count == 0) redraw = aiFrames;

        if (AskDirection is null) return;
        var answer = await AskDirection(DirectionAsk.Revise);
        if (answer is null) return;
        if (Direction(answer) is not { } note)
        {
            AiStatus = "Revise needs a note saying what should change.";
            return;
        }

        var keyA = layer.Cels[a].Frame!;
        var keyB = layer.Cels[b].Frame!;
        var chart = keyA.Chart is { } c && c.Count == b - a - 1 ? c : null;
        double T(int i) => chart is not null ? chart[i - a - 1] : (double)(i - a) / (b - a);
        AcceptedFrame Drawn(int i) =>
            new(T(i), StrokeRecordCleaner.EffectiveStrokes(layer.Cels[i].Frame!.Strokes));

        // Only the frames either side of each redrawn one go along as context:
        // smoothness is judged against neighbours, and every extra frame is the
        // expensive kind of payload (strokes are what tokens are made of).
        var staying = aiFrames.Except(redraw)
            .Where(i => redraw.Any(r => Neighbours(aiFrames, redraw, r).Contains(i)))
            .Select(Drawn)
            .ToList();
        var request = new InbetweenRequest(
            new SceneInfo(Scene.Width, Scene.Height, Scene.Fps),
            StrokeRecordCleaner.EffectiveStrokes(keyA.Strokes),
            StrokeRecordCleaner.EffectiveStrokes(keyB.Strokes),
            redraw.Select(T).ToList(),
            chart is not null ? Easing.Linear : TweenEasing,
            CollectReferenceImages(),
            TaxonomyForActiveDocument(),
            Direction: note,
            Earlier: redraw.Select(Drawn).ToList(),
            Fixed: staying.Count > 0 ? staying : null);

        // What the write-back expects to find when the answer arrives. The
        // timeline is not locked while a request is out, so a frame inserted or
        // deleted meanwhile would move every index after it.
        var expected = Enumerable.Range(a, b - a + 1)
            .ToDictionary(i => i, i => layer.Cels[i].Frame?.Id);

        var run = await RunAiAsync(
            $"{AiProviderLabel} is revising {Plural(redraw.Count, "frame")} between frames {a + 1} and {b + 1}…",
            ct => InbetweenRepair.RunAsync(
                _ai.Artist,
                request,
                ct,
                (attempt, count) => AiStatus =
                    $"{AiProviderLabel} is redrawing {count} refused frame(s) — attempt {attempt}…"));
        if (run is null) return;

        if (!Scene.Layers.Contains(layer)
            || expected.Any(e => e.Key >= layer.Cels.Count || layer.Cels[e.Key].Frame?.Id != e.Value))
        {
            AiStatus = "The timeline changed while the model was working, so nothing was written. Revise again.";
            return;
        }

        // Only the cels being replaced are written — a frame nobody asked to
        // change is left exactly as it is, marking, anchors and all. One step,
        // so one undo.
        var replacements = new Dictionary<int, Frame>();
        var refused = new List<string>();
        for (var k = 0; k < redraw.Count; k++)
        {
            var result = run.Frames[k];
            if (!result.Accepted)
            {
                refused.Add($"frame {redraw[k] + 1} was refused and kept as it was: {result.Refusal}");
                continue;
            }
            var frame = NewFrameFor(layer, [.. result.Strokes!], FrameRole.Inbetween);
            frame.Ai = new AiProvenance(
                AiProviderLabel, _ai.ModelLabel, result.Attempts > 1 ? result.Attempts : null, note);
            replacements[redraw[k]] = frame;
        }

        if (replacements.Count > 0)
        {
            var layerId = layer.Id;
            _editor.Perform(doc =>
            {
                var target = doc.Scene.Layers.First(l => l.Id == layerId);
                foreach (var (index, frame) in replacements) target.Cels[index].Frame = frame;
            }, "Revise AI frames");
        }
        var replaced = replacements.Count;
        var said = replaced == 0
            ? $"Nothing was revised after {Plural(run.Calls, "attempt")}."
            : $"Revised {Plural(replaced, "frame")}.";
        AiStatus = refused.Count == 0 ? said : $"{said} {string.Join(" ", refused)}";
    }

    /// <summary>
    /// The unselected AI frames either side of <paramref name="frame"/>: the
    /// nearest one before it and the nearest one after it that are not being
    /// redrawn themselves.
    /// </summary>
    private static IEnumerable<int> Neighbours(List<int> aiFrames, List<int> redraw, int frame)
    {
        var before = aiFrames.Where(i => i < frame && !redraw.Contains(i)).DefaultIfEmpty(-1).Max();
        var after = aiFrames.Where(i => i > frame && !redraw.Contains(i)).DefaultIfEmpty(-1).Min();
        if (before >= 0) yield return before;
        if (after >= 0) yield return after;
    }

    /// <summary>
    /// The nearest drawings at or before, and after, <paramref name="index"/>
    /// that were not made by the AI; -1 where there is none.
    /// </summary>
    private static (int A, int B) ArtistDrawnBounds(Layer layer, int index)
    {
        static bool ArtistDrawn(Cel cel) => cel.Frame is { Ai: null };
        var a = -1;
        for (var k = Math.Min(index, layer.Cels.Count - 1); k >= 0; k--)
        {
            if (ArtistDrawn(layer.Cels[k])) { a = k; break; }
        }
        if (a < 0) return (-1, -1);
        var b = -1;
        for (var k = a + 1; k < layer.Cels.Count; k++)
        {
            if (ArtistDrawn(layer.Cels[k])) { b = k; break; }
        }
        return (a, b);
    }
}
