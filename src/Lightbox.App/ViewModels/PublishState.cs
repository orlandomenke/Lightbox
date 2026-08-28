using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// What has changed since the last publish, and what the last publish was.
/// </summary>
/// <remarks>
/// <para>
/// The second half of Tier 0 in <c>docs/DESIGN-mainviewmodel-decomposition.md</c>,
/// and <b>not</b> the "render orchestrator" that document originally called for. That
/// plan was written from a partial reading and is wrong; the reasons are worth keeping
/// because they are an argument about where a boundary belongs, not a detail.
/// </para>
/// <para>
/// <b>Why not an orchestrator.</b> <c>PublishSnapshot</c> reads roughly fifteen pieces
/// of view-model state — the scene, the playhead, the compose scale, the camera
/// transform, whether it is playing, the light table, the onion settings, the active
/// layer, the playback range, and the whole live-edit tuple. An orchestrator would
/// have to be handed all of that per call or hold a reference back to the view model.
/// The second is a second view model with circular coupling. The first allocates a
/// request per publish, and <b>the code next door already refuses that trade</b>: the
/// transform-split delegate is cached in a field rather than written as a lambda
/// precisely because "a lambda capturing <c>this</c> allocates a closure and a
/// delegate on every publish, and a publish happens per pointer event while drawing".
/// A path that avoids one closure allocation should not gain a record allocation and a
/// layer of indirection.
/// </para>
/// <para>
/// <b>What was actually unowned.</b> The document also claimed this cluster's state was
/// already owned by six collaborators — <c>ComposeRing</c>, <c>FrameBitmapCache</c>,
/// <c>TileFlattenCache</c>, <c>LayerStackBake</c>, <c>FramePrewarmer</c>,
/// <c>TileFallbackTally</c>. True of the <i>caches</i>, and false of the bookkeeping:
/// the dirty region, the viewport, the publish sequence and the on-screen fingerprint
/// were seven raw fields belonging to nothing. Those are what moved. The sequencing
/// stays in <c>PublishSnapshot</c>, where it can read the view model directly and
/// allocate nothing.
/// </para>
/// <para>
/// <b>The one thing this buys that a field could not.</b> <see cref="TakeDirty"/>.
/// Reading the dirty region and clearing it is three statements that must happen
/// together, and both halves of getting it wrong are silent: clear without reading and
/// the next publish repaints nothing that changed, read without clearing and every
/// later publish inherits a region it already painted. Invariant 6 — painting is
/// bounded work — rests on this one method being right.
/// </para>
/// </remarks>
sealed class PublishState
{
    /// <summary>
    /// The document region the next publish is limited to, when it is limited at all.
    /// </summary>
    /// <remarks>
    /// Only meaningful while <see cref="WholeCanvasDirty"/> is false. Read it through
    /// <see cref="TakeDirty"/> rather than directly, so the reset cannot be forgotten.
    /// </remarks>
    internal SKRectI? PendingDirty { get; private set; }

    /// <summary>The next publish repaints everything. The safe default, hence the initial value.</summary>
    internal bool WholeCanvasDirty { get; private set; } = true;

    /// <summary>Anything at all to repaint — the reuse guard's question (B165).</summary>
    internal bool AnythingDirty => WholeCanvasDirty || PendingDirty is not null;

    /// <summary>The rectangle of the document the canvas last asked to see.</summary>
    internal SKRectI? Viewport { get; set; }

    /// <summary>What the frame currently on screen was composed for (B165).</summary>
    internal FrameFingerprint? LastPublished { get; set; }

    /// <summary>
    /// How many times the document's rendered content has been invalidated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The half <see cref="FrameFingerprint"/> cannot carry (B167 phase 7).</b>
    /// That record answers "would this frame compose to what the *previous*
    /// publish composed", which is a question about two adjacent moments and is
    /// exactly right for the single-slot reuse check it was built for. A cache
    /// asks about two moments a lap of the loop apart, and between them an
    /// artist can have drawn — so the key needs something the fingerprint has no
    /// reason to hold.
    /// </para>
    /// <para>
    /// Bumped in <c>MainViewModel.InvalidateFrameRender</c> and
    /// <c>ClearFrameRenders</c>, which the view model's own comment already
    /// calls "the only thing standing between an under-the-bake edit and stale
    /// art on screen". Anything that mutates a frame goes through there, so a
    /// new render path that forgot to bump this would also have forgotten to
    /// invalidate the frame cache — a failure that is loud rather than silent.
    /// </para>
    /// </remarks>
    internal int RenderEpoch { get; private set; }

    /// <summary>Note that something rendered has changed.</summary>
    /// <remarks>
    /// <b>It does not reach into the composite cache, and that is deliberate.</b>
    /// The first version cleared it here, reasoning that giving the memory back
    /// at once beat waiting for the budget to notice. Two things are wrong with
    /// that. A stale entry can never be hit — its epoch is gone — so it is
    /// never touched again and LRU evicts it before anything live; the budget
    /// already bounds it. And this is a per-document operation on a
    /// per-document object, called on every edit, so reaching a process-wide
    /// lock from here made every parallel test contend on one mutex for
    /// nothing.
    /// </remarks>
    internal void BumpRenderEpoch() => RenderEpoch++;

    /// <summary>
    /// The document region the last publish actually recomposited (null = the whole
    /// canvas). What the artist feels as a stutter is this rect growing, so tests assert
    /// on it rather than on wall-clock, which is unusable on a shared runner.
    /// </summary>
    internal SKRectI? LastPublishClip { get; set; }

    /// <summary>
    /// Playback frames that composed to the pixels already on screen and were therefore
    /// not composed at all.
    /// </summary>
    internal int FramesReused { get; set; }

    /// <summary>
    /// Frames allowed in flight before a publish is held. Two, measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One frame in flight caps the update rate at one publish per round
    /// trip, however fast everything else becomes.</b> On the owner's machine
    /// that was twenty-eight publishes a second from a tablet delivering two
    /// hundred. The pipeline is already about two vsyncs deep in latency, so a
    /// second frame costs no more waiting — it is what is being paid for
    /// anyway — and lets a publish go out each vsync instead of each round trip.
    /// </para>
    /// <para>
    /// Measured as an A/B on 2026-08-26, same build, same brush:
    /// </para>
    /// <list type="bullet">
    /// <item>the publish cycle <b>35.44 ms → 17.16 ms</b>, which is one vsync</item>
    /// <item>ink arriving <b>9.5 pen events at a time → 4.1</b></item>
    /// <item><c>TIP -&gt; SCREEN</c> 46.11 → <b>35.87 ms</b>, and <c>PEN -&gt; SCREEN</c> 90.26 → <b>49.98</b></item>
    /// <item>the owner's verdict, which is the one that counts: <i>"in general
    ///   it feels fluid… way less than when we started"</i> — the first change
    ///   of that day to alter what they felt rather than only what was measured</item>
    /// </list>
    /// <para>
    /// <b>The cost is real and is B189's:</b> a frame composed and then replaced
    /// before anything drew it. That went from 1.9% to <b>15.6%</b>. B189 chose
    /// a depth of one when the rate was <b>48.7%</b> — 935 of 1921 — and when a
    /// publish cost about 27 ms of UI thread. A build now costs 1.7 ms, so the
    /// waste is a third the rate at a sixteenth the unit price: roughly 440 ms
    /// of UI thread across a session of 7,595 events.
    /// </para>
    /// <para>
    /// <c>LIGHTBOX_INFLIGHT</c> still overrides, because that is how this was
    /// settled and how it would be re-settled. Deliberately NOT a Configure
    /// setting: an artist cannot judge frames-in-flight, and a preference
    /// nobody can evaluate is a worse answer than a measurement.
    /// <c>replaced before drawing</c> in the render report is the number that
    /// would reverse this.
    /// </para>
    /// </remarks>
    internal int InFlightDepth { get; set; } = DefaultInFlightDepth;

    /// <summary>
    /// What the environment asked for, read once. Settable per instance above so
    /// a test can drive both depths in one process — a static readonly is fixed
    /// by whichever test touched the class first, which is not a seam at all.
    /// </summary>
    internal static readonly int DefaultInFlightDepth =
        int.TryParse(Environment.GetEnvironmentVariable("LIGHTBOX_INFLIGHT"), out var d)
        && d >= 1 && d <= 4 ? d : 2;

    private long _sequence;

    /// <summary>The number stamped on the next snapshot, so the canvas can order them.</summary>
    internal long NextSequence() => ++_sequence;

    /// <summary>The newest sequence number published.</summary>
    internal long Sequence => _sequence;

    // ---- pacing: has the canvas caught up? (B189, PR222) ---------------------
    //
    // These four arrived with the publish pacing and belong here rather than beside
    // it, for the reason the class exists: they are read together with Sequence and
    // are meaningless apart from it. CanvasIsBehind compares three of them at once,
    // and a publish deferred without recording when it was deferred is a publish that
    // waits forever — the failure PR222's adversarial pass found in its own first
    // draft, where the dam was only ever checked and never released.
    //
    // The dispatcher half stays on the view model: arming the one-shot timer and
    // re-entering RequestSnapshot are things this object must not know about, the same
    // line Q77 drew for RequestSnapshot itself.

    /// <summary>Newest seq the canvas has reported drawn. UI thread.</summary>
    internal long PresentedSeq { get; private set; }

    /// <summary>
    /// Asks the canvas what it has actually drawn, rather than what it has got
    /// round to saying (B321).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null in every headless test and wherever no canvas is attached, which is
    /// the same condition <see cref="CanvasIsBehind"/> already treats as "no
    /// consumer to pace to" — so the pacing behaves exactly as it did before
    /// this existed unless a real canvas is on the other end.
    /// </para>
    /// <para>
    /// A delegate rather than a reference to the control: this object is in the
    /// view-model layer and knowing about <c>CanvasControl</c> would invert the
    /// dependency the decomposition drew. The window supplies it, as it does the
    /// present event.
    /// </para>
    /// </remarks>
    internal Func<long>? RenderedSeqProbe { get; set; }

    /// <summary>
    /// Take the canvas's own high-water mark, if it is ahead of what we were
    /// told. Monotonic: a probe that answered lower would be a lost draw rather
    /// than a rewind.
    /// </summary>
    private void AdoptRenderedSeq()
    {
        if (RenderedSeqProbe?.Invoke() is not { } drawn) return;
        if (drawn > PresentedSeq) PresentedSeq = drawn;
    }

    /// <summary>A coalesced publish is waiting for the canvas to catch up.</summary>
    internal bool WaitingForPresent { get; set; }

    /// <summary>When the newest publish left, for the liveness dam.</summary>
    internal long LastPublishTicks { get; set; }

    /// <summary>A one-shot dam timer is already pending, so do not arm a second.</summary>
    internal bool DamArmed { get; set; }

    /// <summary>Has the canvas not yet drawn the newest published frame?</summary>
    /// <remarks>
    /// False when nothing has ever been presented — headless, no canvas wired, or the
    /// first frames of a session. <b>Pacing needs a consumer to pace to</b>, and a
    /// pacing check that answered true here would dam every publish of B73's entire
    /// suite, which never presents at all.
    /// </remarks>
    internal bool CanvasIsBehind(double damMs)
    {
        // B321: ask before judging. The canvas may have drawn the frame this
        // dam is waiting on and simply not have been given a dispatcher turn to
        // say so — mid-stroke that turn queues behind the artist's own pointer
        // events, which is precisely when the pacing is deciding.
        AdoptRenderedSeq();
        if (PresentedSeq == 0) return false;
        // How many frames may be in flight at once. One is what B189 chose when
        // a publish cost ~27 ms of UI thread and half of them were replaced
        // before anything drew them — waste worth preventing. It also caps the
        // update rate at 1 / (publish -> drawn) however fast everything else
        // gets, and the owner's capture of 2026-08-26 shows exactly that: a
        // cycle of 35.44 ms against a `publish -> drawn` of 31.19, with the
        // dam's own overhead down to 0.15 ms and a pen delivering every 5.06.
        // Twenty-eight publishes a second from a tablet offering two hundred.
        //
        // The pipeline is ALREADY about two vsyncs deep in latency, so a depth
        // of two costs no more waiting — it is what is being paid for anyway —
        // and lets a publish go out each vsync instead of each round trip. A
        // build now costs 1.25 ms rather than the 27 that made waste expensive.
        if (_sequence - PresentedSeq < InFlightDepth) return false;
        return (System.Diagnostics.Stopwatch.GetTimestamp() - LastPublishTicks)
            * 1000.0 / System.Diagnostics.Stopwatch.Frequency < damMs;
    }

    /// <summary>
    /// Would the in-flight depth have held this publish, asked without any of
    /// the deferral bookkeeping (B178)?
    /// </summary>
    /// <remarks>
    /// <para>
    /// For attribution only, and deliberately partial: it asks the depth
    /// question and <em>not</em> the liveness one. A request refused because a
    /// publish is already posted has not started a deferral, so there is no
    /// 250 ms clock running for it to be measured against — and answering
    /// "the dam would have let it through" on the strength of an expired
    /// backstop would credit the dispatcher with a request the pacing had
    /// genuinely queued.
    /// </para>
    /// <para>
    /// <see cref="AdoptRenderedSeq"/> first, for the reason
    /// <see cref="CanvasIsBehind"/> does: the canvas may have drawn the frame
    /// and not yet had a dispatcher turn to say so, and counting that as the
    /// pacing holding would be the very misattribution B321 fixed.
    /// </para>
    /// </remarks>
    internal bool WouldHoldAnyway()
    {
        AdoptRenderedSeq();
        if (PresentedSeq == 0) return false;
        return _sequence - PresentedSeq >= InFlightDepth;
    }

    /// <summary>
    /// Record that the canvas drew a frame. True when a deferred publish is now due.
    /// </summary>
    /// <remarks>
    /// The clearing of <see cref="WaitingForPresent"/> happens here rather than at the
    /// call site so that "a deferral was released" and "the flag is down" cannot come
    /// apart — two releases for one deferral would put a second frame in flight, which
    /// is the thing the pacing exists to prevent.
    /// </remarks>
    internal bool NotePresented(long seq)
    {
        if (seq > PresentedSeq) PresentedSeq = seq;
        if (!WaitingForPresent || PresentedSeq < _sequence) return false;
        WaitingForPresent = false;
        return true;
    }

    /// <summary>Take a pending deferral, if there is one — for the dam timer's release.</summary>
    internal bool TakeDeferral()
    {
        if (!WaitingForPresent) return false;
        WaitingForPresent = false;
        return true;
    }

    /// <summary>
    /// Document pixels every dirty region must grow by, asked at mark time —
    /// the reach of the document's live effect stacks (a blur reads
    /// neighbours, so the region a stroke dirties is wider than the stroke;
    /// the brush-reach rule of invariant 6, applied to effects). Null, and
    /// free, on the ordinary document; set once by the view model, which owns
    /// the answer. A provider rather than a number because a keyed radius
    /// changes per frame, and a stale number here is a one-frame smear at the
    /// edge of the dirty region that nobody ever traces back.
    /// </summary>
    internal Func<int>? DirtyInflationOf { get; set; }

    /// <summary>
    /// Limit the next publish to a document region. Only safe when nothing outside the
    /// region can change; every other edit path must leave the default (whole-canvas)
    /// invalidation alone, or stale pixels linger.
    /// </summary>
    internal void MarkDirty(SKRectI region)
    {
        if (WholeCanvasDirty) return;
        var inflate = DirtyInflationOf?.Invoke() ?? 0;
        if (inflate > 0) region.Inflate(inflate, inflate);
        if (PendingDirty is { } existing)
        {
            existing.Union(region);
            PendingDirty = existing;
        }
        else
        {
            PendingDirty = region;
        }
    }

    /// <summary>The next publish repaints everything (the safe default).</summary>
    /// <remarks>
    /// B165: and forget what is on screen, so the next publish composes.
    /// <para>
    /// The reuse guard already refuses while anything is dirty, so that part is belt and
    /// braces — but it is the cheap half of a pair whose expensive half is stale art. The
    /// guard rests on an existing contract: anything that changes pixels must already mark
    /// the canvas dirty, or it would not reach the screen today either. This makes that
    /// dependency explicit instead of implicit, so a future invalidation path that forgets
    /// to mark dirty fails loudly at the canvas rather than quietly here.
    /// </para>
    /// </remarks>
    internal void InvalidateWholeCanvas()
    {
        WholeCanvasDirty = true;
        PendingDirty = null;
        LastPublished = null;
    }

    /// <summary>
    /// Repaint everything on the publish already in progress, without forgetting the
    /// frame on screen.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not <see cref="InvalidateWholeCanvas"/>, and the difference is
    /// one line.</b> This exists for the fold transition mid-publish, where folded and
    /// unfolded pixels can differ by an LSB and a dirty-region patch must never mix the
    /// two on one surface. At that point the fingerprint has already been read by the
    /// reuse guard and is about to be overwritten by this same publish, so clearing it
    /// would be equivalent — today. It is kept separate because "equivalent today"
    /// depends on there being no early return between the two, which is a property of
    /// <c>PublishSnapshot</c>'s body rather than of this class, and the failure if it
    /// ever stops holding is a lost frame reuse that nothing measures.
    /// </remarks>
    internal void RepaintEverythingThisPublish() => WholeCanvasDirty = true;

    /// <summary>
    /// What this publish must repaint — null meaning everything — and reset for the next.
    /// </summary>
    /// <remarks>
    /// One method rather than three statements at the call site, because the three have
    /// to happen together and both ways of splitting them fail silently. See the class
    /// remarks; invariant 6 rests on this.
    /// </remarks>
    internal SKRectI? TakeDirty()
    {
        var dirty = WholeCanvasDirty ? null : PendingDirty;
        PendingDirty = null;
        WholeCanvasDirty = false;
        return dirty;
    }
}
