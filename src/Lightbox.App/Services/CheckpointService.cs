using Avalonia.Threading;
using Lightbox.Core.Documents;
using Lightbox.Raster;

namespace Lightbox.App.Services;

/// <summary>
/// Renders raster checkpoints for the drawings big enough to want one, off the
/// thread the artist is drawing on (B30).
/// </summary>
/// <remarks>
/// <para>
/// <b>Saving must not stall — that is the constraint this was designed against
/// (Q60).</b> Rendering a checkpoint costs exactly what opening the painting
/// costs, which is the whole point and also the reason it cannot happen on the
/// UI thread: a Ctrl+S that froze for a hundred seconds would have moved the
/// wait rather than removed it. So the save writes the record and returns, and
/// the pixels arrive later.
/// </para>
/// <para>
/// <b>The consequence, stated rather than hidden: quit straight after the first
/// save of a big painting and there is no checkpoint in the file.</b> It is
/// harmless by construction — a missing checkpoint is a slow open, never a
/// wrong one — and it is self-correcting, because the render is attached to the
/// document in memory and the next save of any kind writes it out. What it is
/// not is invisible, which is why it is written here and in the manual.
/// </para>
/// <para>
/// <b>Three rules hold this together.</b> The plan is made on the UI thread, so
/// the worker never reads a stroke list the artist is appending to. Only one
/// render is in flight, so a burst of saves does not start a queue of
/// hundred-second jobs. And the result is re-checked against the document
/// before it is attached, because the artist has been painting the whole time
/// it was rendering.
/// </para>
/// </remarks>
public sealed class CheckpointService(Func<Doc?> document, Action<Action>? post = null)
{
    private readonly Action<Action> _post = post ?? (action => Dispatcher.UIThread.Post(action));
    private Task _work = Task.CompletedTask;

    /// <summary>
    /// Whether checkpoints are taken at all.
    /// </summary>
    /// <remarks>
    /// The artist's switch, and it exists for one reason: the pixels live in the
    /// document, so a checkpointed painting is mostly checkpoint by weight.
    /// Photoshop made the same call for the same reason — its flattened
    /// composite is written only when <em>Maximize File Compatibility</em> is on.
    /// Off means <em>absent</em>: <see cref="Clear"/> takes the existing ones out
    /// rather than leaving megabytes behind a switch that says no.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>The render in flight, or a completed task. For tests.</summary>
    public Task InFlight => _work;

    /// <summary>
    /// The most a document may spend on stored renderings, in bytes of base64.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Charter O4 — caches are sized in bytes, never in item count — pointed at
    /// the one document shape that breaks the stroke threshold's reasoning.</b>
    /// The threshold assumes big drawings are few, which holds for a painting and
    /// for an ordinary sequence. It does not hold for a two-hundred-cel
    /// <em>painted</em> sequence, where every drawing qualifies and every drawing
    /// wants a full-canvas image: at 1080p that is hundreds of megabytes, in the
    /// file and resident in memory, from a feature nobody asked to turn on.
    /// </para>
    /// <para>
    /// 64 MB is roughly twenty full-canvas 1080p renderings — far more cels than
    /// any painting has, and a hard stop on the pathological case. Past it the
    /// remaining drawings simply replay, which is what they did before this
    /// existed, so the degradation is back to the old behaviour rather than into
    /// anything new.
    /// </para>
    /// <para>
    /// <b>Spent largest-drawing-first</b>, because what a checkpoint saves is
    /// proportional to the strokes it covers. Handing the budget out in document
    /// order would let a shelf of 250-stroke cels crowd out the 8 000-stroke
    /// painting that is the whole reason for the feature.
    /// </para>
    /// <para>
    /// <b>The budget is checked before a render, never after, so one always
    /// lands.</b> That is not an off-by-one: a single 8K painting's rendering can
    /// exceed any budget worth setting, and refusing it would starve exactly the
    /// document this feature exists for while a shelf of small ones sailed
    /// through. The overshoot is bounded by one drawing, and the largest drawing
    /// is the one that earns it.
    /// </para>
    /// </remarks>
    public static long ByteBudget { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Take checkpoints for whatever in the open document wants one.
    /// </summary>
    /// <remarks>
    /// Call after a save. Cheap and idempotent on everything that does not
    /// qualify: a sequence's cels fail the stroke threshold on a count, and a
    /// painting whose checkpoint is already current plans nothing.
    /// </remarks>
    public void Request()
    {
        if (!Enabled || !_work.IsCompleted) return;
        if (document() is not { } doc) return;

        var plans = new List<FrameCheckpoints.CheckpointPlan>();
        var alreadySpent = 0L;
        foreach (var frame in Drawings(doc))
        {
            if (FrameCheckpoints.Plan(doc, frame) is { } plan) plans.Add(plan);
            // What the document is already carrying counts against the budget,
            // or a save would top it up by the whole budget every time.
            else if (frame.Checkpoint is { } held) alreadySpent += held.PixelsBase64.Length;
        }
        if (plans.Count == 0) return;

        // Biggest drawing first — see `ByteBudget`. Ties keep document order,
        // which `List.Sort` would not: an unstable sort would hand the budget to
        // a different set of equal-sized cels on every save, so the file would
        // churn megabytes for no reason.
        var ordered = plans.OrderByDescending(p => p.Strokes.Count).ToList();

        _work = Task.Run(() =>
        {
            var rendered = new List<(FrameCheckpoints.CheckpointPlan Plan, StrokeCheckpoint Made)>();
            var spent = alreadySpent;
            foreach (var plan in ordered)
            {
                if (spent >= ByteBudget) break;
                // A checkpoint that will not render is a checkpoint the document
                // does without. Nothing here may take the application down: it is
                // speculative work on a copy, and its whole promise is that
                // failing costs a replay.
                try
                {
                    if (FrameCheckpoints.Render(plan) is { } made)
                    {
                        rendered.Add((plan, made));
                        spent += made.PixelsBase64.Length;
                    }
                }
                catch (Exception e) when (e is OutOfMemoryException or InvalidOperationException)
                {
                }
            }
            if (rendered.Count > 0) _post(() => Attach(doc, rendered));
        });
    }

    /// <summary>
    /// Take every checkpoint out of a document.
    /// </summary>
    /// <remarks>
    /// What turning the setting off means, and what makes "optional" mean absent
    /// rather than merely unused. Changes no pixel by construction — the strokes
    /// are the document — which is the property
    /// <c>ACheckpointedRenderIsBitIdenticalToAReplay</c> exists to keep true.
    /// </remarks>
    public static void Clear(Doc doc)
    {
        foreach (var frame in Drawings(doc)) frame.Checkpoint = null;
    }

    /// <summary>
    /// Attach what was rendered, to the frames that still want it.
    /// </summary>
    /// <remarks>
    /// <b>Re-checked rather than assumed.</b> The artist kept painting while the
    /// worker ran, and most of what they can have done is harmless — appending
    /// strokes leaves a prefix's fingerprint exactly as it was, which is the
    /// property that makes checkpointing worth doing at all. Editing a covered
    /// stroke is not harmless, and this is where that is caught: the render is
    /// dropped, and the next save plans a fresh one.
    /// </remarks>
    private static void Attach(
        Doc doc, List<(FrameCheckpoints.CheckpointPlan Plan, StrokeCheckpoint Made)> rendered)
    {
        var byId = new Dictionary<string, Frame>();
        foreach (var frame in Drawings(doc)) byId[frame.Id] = frame;

        foreach (var (plan, made) in rendered)
        {
            if (!byId.TryGetValue(plan.FrameId, out var frame)) continue;
            if (!Lightbox.Core.Serialization.CheckpointFingerprint.Matches(doc, frame, made)) continue;
            frame.Checkpoint = made;
        }
    }

    /// <summary>Every drawing in the document, once each.</summary>
    private static IEnumerable<Frame> Drawings(Doc doc)
    {
        foreach (var layer in doc.Scene.Layers)
            foreach (var cel in layer.Cels)
                if (cel.Frame is { } frame) yield return frame;
    }
}
