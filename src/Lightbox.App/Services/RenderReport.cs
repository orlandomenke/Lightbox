using System.Diagnostics;
using System.Text;
using Lightbox.App.Rendering;
using SkiaSharp;

namespace Lightbox.App.Services;

/// <summary>
/// What the renderer actually did on this machine, written where the crash
/// reports go.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The development container has no GPU context, so the
/// suite cannot answer the questions that decide whether the drawing path is
/// fast: is the durable presentation frame really on the GPU, does patching it
/// beat replacing it, and where does the time go at 4K. Those were reasoned
/// about and explicitly marked unmeasured in B122. This moves the measurement to
/// the one machine that can take it — the artist's — and writes the answer to a
/// file rather than leaving it to be described.
/// </para>
/// <para>
/// <b>The line it is most likely to earn its keep on</b> is
/// <see cref="PresentedFrame.GpuSurfaceRequestFailed"/>. A GPU surface can fail
/// to allocate and the fallback to a raster one is deliberately silent, so the
/// entire saving can be absent while the status strip still says "GPU". "It
/// barely improved" and "it never ran" look the same from outside; one line here
/// tells them apart.
/// </para>
/// <para>
/// <b>Nothing here throws</b>, for the reason <see cref="DiagnosticLog"/> gives:
/// a report that can break the application is worse than no report. Every path
/// swallows and callers assume a write may simply not have happened.
/// </para>
/// </remarks>
internal static class RenderReport
{
    /// <summary>The startup report, written once per run when the backend is known.</summary>
    private const string StartupFile = "render-startup.txt";

    /// <summary>Guards the one-per-run promise, and concurrent writes.</summary>
    private static readonly object Gate = new();
    private static bool _startupWritten;

    /// <summary>
    /// Facts the canvas can only know once it has drawn, gathered by whoever
    /// owns them so this class reaches into nothing.
    /// </summary>
    /// <param name="Backend">"GPU" or "CPU (software)" — Avalonia's presentation backend.</param>
    /// <param name="MaxTextureSize">The context's limit, or null when unknown.</param>
    internal readonly record struct Facts(
        string Backend,
        bool? SoftwareRendering,
        bool PresentedFrameOnGpu,
        bool GpuSurfaceRequestFailed,
        int? MaxTextureSize,
        int DocWidth,
        int DocHeight,
        double DisplayScale,
        string CanvasQuality,
        double ComposeScale,
        bool DurableFrameEnabled = false,
        bool DurableFrameHasPresented = false,
        Rendering.TileFallbackTally? TileFallbacks = null,
        Services.FramePrewarmer? Prewarm = null,
        Services.PlaybackClock.Pacing? Pacing = null,
        Rendering.PresentLatency.Stats? PresentWait = null,
        IReadOnlyList<TickProfile.PhaseStats>? TickPhases = null,
        int TickCount = 0,
        (long Hits, long Misses, long Evictions, long Bytes, long Budget)? FrameCache = null,
        (int Frames, int Layers, int Strokes, double Fps)? Scene = null,
        (long Requested, long Delivered)? AnimationFrames = null,
        double RenderMedianMs = 0,
        (int Hits, int Misses, long Bytes)? TextureResidency = null,
        bool GpuCompositeOptedIn = false,
        int FramesReused = 0,
        (int Hits, int Misses, int Evictions, long Bytes, long Budget)? FlattenCache = null,
        (long Frames, long Flattens)? AwaitingUnpin = null,
        (int Frames, int Flattens)? Pinned = null,
        (long Bytes, long Budget)? TileStore = null);

    /// <summary>
    /// Whether playback got the tile path, and what stopped it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The most actionable section in the file, and the one a slow-playback
    /// report is usually about.</b> B144's tile path measured 145 → 14 ms a
    /// frame at 1080p and 334 → 39 ms at 4K. A frame the tiles cannot say pays
    /// the old cost instead — and until this section existed it did so in
    /// silence, so "my animation stutters" pointed at the machine when the
    /// answer was a property of the document.
    /// </para>
    /// <para>
    /// The reasons are named in the artist's terms rather than the code's,
    /// because every one of them is something they can act on: remove the
    /// camera, flatten fewer frames, keep smudge off the animated layers.
    /// </para>
    /// </remarks>
    private static void AppendTilePath(StringBuilder sb, Rendering.TileFallbackTally? tally)
    {
        sb.AppendLine("-- the tile path (B144) --------------------------------------");
        if (tally is null || tally.Considered == 0)
        {
            sb.AppendLine("frames offered            none yet — play the scene, then write this again");
            sb.AppendLine("  Tiles are used while the sequence is PLAYING (and on an unbounded");
            sb.AppendLine("  canvas). A report written without playing says nothing about them.");
            sb.AppendLine();
            return;
        }

        // Passes, not frames: the gate is asked once per layer per publish, so a
        // three-layer document reports three for every frame an artist saw. The
        // label carries that or the percentage below is read as a share of the
        // sequence.
        var tiledShare = 100.0 * tally.Tiled / tally.Considered;
        sb.AppendLine($"layer passes offered      {tally.Considered}");
        sb.AppendLine($"drawn from tiles          {tally.Tiled} ({tiledShare:0.#}%)");

        foreach (var (reason, count) in tally.NonZero())
        {
            sb.AppendLine(
                $"  fell back {count,8}    {Rendering.TileFallback.Explain(reason)}");
        }

        var dominant = tally.Dominant;
        if (dominant == Rendering.TileFallbackReason.None)
        {
            sb.AppendLine("  every pass tiled — playback is not falling back, so slowness here is");
            sb.AppendLine("  compositing or cache size rather than the tile path (B125, B144).");
            sb.AppendLine();
            return;
        }

        var fellShare = 100.0 * tally.FallbackShare;
        sb.AppendLine();
        sb.AppendLine($"  >> {fellShare:0.#}% of passes fell back, most because "
            + $"{Rendering.TileFallback.Explain(dominant)}.");
        sb.AppendLine("     A pass that falls back pays a full-frame bitmap — roughly 137 ms at");
        sb.AppendLine("     1080p against an 83 ms budget, where a tiled one costs about 14 ms.");
        sb.AppendLine("     This is a property of the DOCUMENT, not of the graphics card.");

        sb.AppendLine();
    }

    /// <summary>
    /// Whether rasterizing ahead of the playhead is winning its race.
    /// </summary>
    /// <remarks>
    /// <b>One number decides it, and it is the ratio rather than either half.</b>
    /// A warm is refused, almost always, because the tick got to the frame
    /// first — which means the worker is slower than the frame period and is
    /// rendering, on a second core, exactly what the UI thread renders anyway.
    /// So "installed 40, refused 2" is the feature working and "installed 2,
    /// refused 40" is a machine on which it cannot, and the two are
    /// indistinguishable from a total. Printed rather than derived by whoever
    /// reads the report, because a report that needs arithmetic to be understood
    /// gets read wrong (see the durable-frame line above, which cost exactly
    /// that).
    /// </remarks>
    private static void AppendPrewarm(StringBuilder sb, FramePrewarmer? prewarm)
    {
        sb.AppendLine("-- rasterizing ahead of the playhead (B148) -------------------");
        if (prewarm is null || prewarm.Rendered == 0)
        {
            sb.AppendLine("frames warmed             none yet — warming only happens while PLAYING");
            sb.AppendLine();
            return;
        }

        var offered = prewarm.Installed + prewarm.Refused;
        sb.AppendLine($"rendered off-thread       {prewarm.Rendered}");
        sb.AppendLine($"  taken into a cache      {prewarm.Installed}");
        sb.AppendLine($"  arrived too late        {prewarm.Refused}");
        sb.AppendLine($"  document changed first  {prewarm.Superseded}");
        if (prewarm.Failed > 0)
        {
            sb.AppendLine($"  failed to render        {prewarm.Failed}  (rendered on the tick instead)");
        }

        if (offered == 0)
        {
            sb.AppendLine();
            return;
        }

        var won = 100.0 * prewarm.Installed / offered;
        sb.AppendLine();
        sb.AppendLine($"  >> {won:0.#}% of warmed frames were ready before the playhead reached them.");
        sb.AppendLine(won >= 50
            ? "     Rasterization is off the tick, which is what it is for."
            : "     Most warms arrived after the tick had already drawn the frame, so a\n"
              + "     second core is repeating the UI thread's work. That means a frame\n"
              + "     costs more to rasterize than the scene's frame period allows.");

        sb.AppendLine();
    }

    /// <summary>
    /// Whether the frame clock was actually delivered on time (B150).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The number that separates the two halves of "playback stutters".</b>
    /// Every other section here measures how long a frame took to *make*. This
    /// one measures whether the tick that asked for it arrived when it was due —
    /// a completely different failure, on a completely different axis, and the
    /// only one that can make a near-empty scene stutter on a fast machine.
    /// </para>
    /// <para>
    /// It is printed rather than judged from inside, because
    /// <see cref="PlaybackClock.Pace"/> is built to absorb lateness: it shortens
    /// the next interval and drops frames to stay in time, so a clock delivered
    /// 40 ms late and one delivered on the nose leave the playhead in the same
    /// place. The lateness is the only place the difference exists.
    /// </para>
    /// </remarks>
    private static void AppendPacing(StringBuilder sb, PlaybackClock.Pacing? pacing)
    {
        sb.AppendLine("-- was the frame clock on time (B150) ------------------------");
        // Printed whether or not the clock has run, and printed always rather
        // than only when overridden: a run must never be attributable to a
        // setting it did not have, and "no line" is indistinguishable from "the
        // line I expected" when somebody is comparing two reports.
        // Ask the environment whether it was overridden, rather than inferring it
        // from the band that came out. This compared against Render and went on
        // doing so after B156-B164 moved the shipped default to Input — so every
        // ordinary run was told a variable was set that nobody had set, and the
        // owner went looking for how to unset it. Second lie in this file of the
        // same shape as "compositing CPU raster (always)": a claim that was true
        // when written and became false when the thing it described changed.
        var overridden = Environment.GetEnvironmentVariable("LIGHTBOX_CLOCK_PRIORITY");
        sb.AppendLine($"clock priority            {PlaybackClock.Priority}"
            + (string.IsNullOrWhiteSpace(overridden)
                ? "   (the shipped default)"
                : $"   (LIGHTBOX_CLOCK_PRIORITY={overridden.Trim()} — this is NOT the shipped default)"));
        if (pacing is not { Ticks: > 0 } stats)
        {
            sb.AppendLine("frame clock               not run yet — this needs the scene PLAYED first");
            sb.AppendLine();
            return;
        }

        var lateShare = 100.0 * stats.LateTicks / stats.Ticks;
        sb.AppendLine($"ticks that advanced       {stats.Ticks}");
        sb.AppendLine($"  arrived late            {stats.LateTicks}  ({lateShare:0.#}%)");
        sb.AppendLine($"  mean lateness           {stats.MeanLatenessMs:0.##} ms");
        sb.AppendLine($"  worst lateness          {stats.WorstLatenessMs:0.##} ms");
        sb.AppendLine($"frames dropped to keep up {stats.DroppedFrames}");
        if (stats.Resyncs > 0)
        {
            sb.AppendLine($"  gave up and re-based    {stats.Resyncs}  (a stall longer than {PlaybackClock.MaxCatchUpFrames} frames)");
        }

        sb.AppendLine();
        // A millisecond or two of lateness is the operating system's scheduling
        // quantum and is not a defect. What matters is whether it is a large
        // fraction of the frame period, which is what makes the period wander —
        // and a wandering period is what the eye reads as stutter, even at a
        // frame rate that averages out correctly.
        sb.AppendLine(stats.MeanLatenessMs < 4
            ? "  >> The clock is being delivered on time. If playback still looks uneven,\n"
              + "     the cost is in making the frames — see the two sections above."
            : "  >> The clock is being delivered LATE, so the frame period is wandering\n"
              + "     whatever the frames cost to draw. Worth capturing this twice: once\n"
              + "     with the pointer still and once while moving it. A large difference\n"
              + "     means the tick is competing with input rather than with rendering.");

        sb.AppendLine();
    }

    /// <summary>
    /// How long a published frame waited before anything drew it (B150).
    /// </summary>
    /// <remarks>
    /// <b>The section that tells the two candidate causes apart.</b> The one
    /// above says whether the tick arrived on time; this says whether the frame
    /// it asked for reached the screen. Even publishes with uneven presents mean
    /// the clock is innocent and something downstream is only being pumped when
    /// the dispatcher happens to wake — which is what "smooth while I move the
    /// mouse, stuttery when I stop" looks like from inside the application.
    /// </remarks>
    /// <summary>
    /// The wait to be drawn, split by what arrived while the frame was waiting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the section that answers the report the last three fixes could
    /// not.</b> Playback is smooth while the pointer moves over the canvas,
    /// stuttery when it is still, and — the fact that kills every theory built
    /// so far — no better when the pointer moves over a docker instead.
    /// </para>
    /// <para>
    /// "Something wakes the dispatcher" predicts the docker helps. It does not.
    /// The one thing the canvas does that a docker cannot is invalidate the
    /// canvas, from its own pointer handler. So these three rows are the
    /// experiment: if <c>quiet</c> is slow and <c>on the canvas</c> is fast
    /// while <c>elsewhere</c> stays slow, the frame is waiting for an invalidate
    /// it should never have needed, and that is the bug — stated as a
    /// measurement rather than as a theory about dispatcher priorities.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Drawing the finished frame to the screen, which the tick breakdown above
    /// cannot see because it happens outside the tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B161. This number has been measured all along and printed only when the
    /// durable frame was on — which B130 turned off by default</b>, so in every
    /// report from a real machine it was collected and thrown away. The section
    /// that carried it is guarded on <c>Presents &gt; 0</c>, a counter only the
    /// durable path increments, so the whole block vanished along with it.
    /// </para>
    /// <para>
    /// <b>It is not a minor omission, and <c>PerformanceMonitor.FrameMs</c> says
    /// so in its own summary</b>: <i>"on a large document it is usually the larger
    /// of the two … reporting headroom from compositing alone said smooth while
    /// the canvas ran at 34 fps."</i> That is precisely what this report has been
    /// doing — showing the composite, which is inside the tick, and staying silent
    /// about the draw, which is not. Four captures showed a tick comfortably
    /// inside its budget alongside a clock 200 ms late, and the arithmetic could
    /// not be closed because half of it was missing.
    /// </para>
    /// </remarks>
    private static void AppendWorkOutsideTheTick(StringBuilder sb, Facts facts, double tickMs)
    {
        if (facts.RenderMedianMs <= 0) return;

        var render = facts.RenderMedianMs;
        sb.AppendLine();
        sb.AppendLine($"  drawing it to the screen  {render,7:0.##} ms/frame   << OUTSIDE the tick above");
        sb.AppendLine($"  {"tick + draw",-22} {tickMs + render,7:0.##} ms   — this is what the budget has to cover");

        if (facts.Scene is not { Fps: > 0 } scene) return;

        var period = 1000.0 / scene.Fps;
        var share = 100.0 * (tickMs + render) / period;
        sb.AppendLine();
        sb.AppendLine($"  >> {share:0}% of the {period:0.#} ms budget at {scene.Fps:0.##} fps.");
        if (share >= 90)
        {
            sb.AppendLine("     There is no headroom. The clock cannot be on time however it is");
            sb.AppendLine("     prioritised, and the dropped frames above are the consequence —");
            sb.AppendLine("     whichever of the two numbers is the larger is the one to attack.");
        }
        else if (share >= 60)
        {
            sb.AppendLine("     Tight. Any hitch spends the remainder, which is what an uneven");
            sb.AppendLine("     period looks like from a chair even though the average fits.");
        }
        else
        {
            sb.AppendLine("     Comfortable. If the clock is still late with this much headroom,");
            sb.AppendLine("     the time is going somewhere neither of these two numbers covers.");
        }
    }

    private static void AppendPresentWaitByInput(
        StringBuilder sb, Rendering.PresentLatency.Stats stats,
        (long Requested, long Delivered)? animationFrames)
    {
        if (stats.ByCohort is not { Count: 3 } cohorts) return;

        sb.AppendLine();
        sb.AppendLine("  the same wait, split by what arrived while the frame waited:");
        foreach (var c in cohorts)
        {
            var label = c.Which switch
            {
                Rendering.PresentLatency.Cohort.Quiet => "nothing (pointer still)",
                Rendering.PresentLatency.Cohort.InputElsewhere => "input, not on canvas",
                _ => "input ON THE CANVAS",
            };
            sb.AppendLine(c.Count == 0
                ? $"    {label,-24} none"
                : $"    {label,-24} {c.Count,4} frames   mean {c.MeanMs,7:0.##} ms   worst {c.WorstMs,7:0.##} ms");
        }

        var quiet = cohorts[(int)Rendering.PresentLatency.Cohort.Quiet];
        var elsewhere = cohorts[(int)Rendering.PresentLatency.Cohort.InputElsewhere];
        var canvas = cohorts[(int)Rendering.PresentLatency.Cohort.InputOnCanvas];

        sb.AppendLine();
        if (quiet.Count == 0 && canvas.Count > 0)
        {
            sb.AppendLine("  >> Every frame was drawn with the pointer moving over the canvas, so");
            sb.AppendLine("     this says nothing yet. Play the scene again with the pointer STILL");
            sb.AppendLine("     and off the canvas, then write the report — that is the case the");
            sb.AppendLine("     rows above exist to price.");
            return;
        }
        // A cohort this small is one stall, not a trend. Learned from a capture
        // that announced "any input helps" off FOUR quiet frames whose mean was
        // dragged to 73 ms by a single 163 ms outlier, while the three captures
        // beside it — with 31, 52 and 53 quiet frames — all said the opposite.
        // A verdict that turns on a handful of frames is worse than no verdict,
        // because it reads exactly like the ones that mean something.
        const int Enough = 12;

        if (quiet.Count is > 0 and < Enough || canvas.Count is > 0 and < Enough)
        {
            sb.AppendLine($"  >> Too few frames in one of these rows to mean anything (under {Enough}).");
            sb.AppendLine("     One stall moves a mean this small by tens of milliseconds. Play for");
            sb.AppendLine("     longer in each condition before reading anything into the split.");
            return;
        }

        if (quiet.Count == 0)
        {
            sb.AppendLine("  >> No frame was drawn with input quiet, so there is nothing to compare.");
            return;
        }
        if (canvas.Count == 0)
        {
            sb.AppendLine("  >> No frame was drawn while the pointer moved over the canvas, so the");
            sb.AppendLine("     comparison this section exists for is not in this capture. Play once");
            sb.AppendLine("     with the pointer moving over the canvas and write the report again.");
            return;
        }

        // A frame of a 60 Hz screen, the same threshold the section above uses.
        var canvasHelps = quiet.MeanMs > canvas.MeanMs * 2 && quiet.MeanMs > 17;
        var elsewhereHelps = elsewhere.Count > 0 && quiet.MeanMs > elsewhere.MeanMs * 2;

        if (canvasHelps && !elsewhereHelps)
        {
            // This used to assert B164's answer as CONFIRMED — "only its own
            // pointer handler invalidates the canvas". B164 was then FIXED
            // (`KeepPresenting` re-arms a compositor frame for the whole of
            // playback), and this text was never re-checked. On 2026-08-12 it
            // printed "CONFIRMED, and this is the fault" in a report whose own
            // wake-up counter read 676 asked, 676 arrived — the loop running
            // exactly as intended. A diagnosis that survives its own fix sends
            // the next reader to re-fix something that works.
            //
            // So the observation is kept and the causal claim is now conditional
            // on the counter that can refute it.
            var loopRunning = animationFrames is { } af
                && af.Requested > 0 && af.Delivered >= af.Requested;

            sb.AppendLine("  >> Frames wait far longer when the pointer is still than when it moves");
            sb.AppendLine("     over the canvas, and input elsewhere does not help.");
            if (loopRunning)
            {
                sb.AppendLine("     But every compositor frame asked for ARRIVED (see the wake-ups line),");
                sb.AppendLine("     so the compositor is not asleep and B164's answer does not apply —");
                sb.AppendLine("     that one was fixed by keeping a frame permanently on request. This");
                sb.AppendLine("     is B178: the wait is real, the wake is working, and where the time");
                sb.AppendLine("     accumulates is not yet known. Instrument it before changing the");
                sb.AppendLine("     present path.");
            }
            else
            {
                sb.AppendLine("     Compositor frames were asked for and did NOT all arrive, so the");
                sb.AppendLine("     publish's own invalidate is not producing a render while the");
                sb.AppendLine("     pointer handler's identical call is. Fix the publish path; do not");
                sb.AppendLine("     add another way to poke the compositor.");
            }
        }
        else if (canvasHelps && elsewhereHelps)
        {
            sb.AppendLine("  >> Any input helps, not just the canvas — so the loop is genuinely idle");
            sb.AppendLine("     between frames and the wake is the problem rather than the invalidate.");
            sb.AppendLine("     That is the opposite conclusion from the row above it, and it points");
            sb.AppendLine("     at the clock's dispatcher priority rather than at the canvas.");
        }
        else
        {
            sb.AppendLine("  >> Input makes no difference to how long a frame waits, so whatever is");
            sb.AppendLine("     uneven about playback is NOT the frame reaching the screen. Read the");
            sb.AppendLine("     tick breakdown below: a phase that costs most of the frame period");
            sb.AppendLine("     makes playback uneven with every number in this section healthy.");
        }
    }

    /// <summary>
    /// Whether layer rasters are staying on the GPU between frames (B125 stage 5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one number stage 5 exists to move, and it can only be read here.</b>
    /// A texture is reused exactly when a layer shows the same drawing as the
    /// previous frame. B165 measured that share from the exposure sheet — 26% of
    /// layer draws at two layers, 51% at six, 59% at ten — so the hit rate below
    /// should land near that for the document being played. Well under it means
    /// something is invalidating textures that did not change; well over it means
    /// the playhead is not moving.
    /// </para>
    /// <para>
    /// Absent unless GPU compositing was switched on, because there is nothing to
    /// report otherwise and a row of zeroes reads like a failure rather than an
    /// unused feature.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Where layer compositing actually happened this session.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This line said "CPU raster (always — see B125)" and became false the day
    /// stage 4 landed.</b> It was printed unconditionally while the very next
    /// section of the same report showed resident GPU textures — which is exactly
    /// the failure B125's entry already records about the status bar reading "GPU"
    /// and meaning only that Avalonia can blit. A hardcoded claim about a thing
    /// that has since become configurable is worse than no line at all: it is
    /// read, believed and acted on.
    /// </para>
    /// <para>
    /// So it counts instead of asserting. The counts also answer the question the
    /// first real reports raised and nothing in the file could: <b>the GPU path
    /// only runs on the culled route, and playback does not take it</b> — playback
    /// takes the tiled unbounded compositor. A report showing thousands of tiled
    /// layer passes and a few dozen GPU composites is that, and it is not
    /// something a reader should have to infer.
    /// </para>
    /// </remarks>
    private static void AppendCompositor(StringBuilder sb, Facts facts)
    {
        var gpu = Rendering.GpuComposite.GpuComposites;
        var cpu = Rendering.GpuComposite.CpuComposites;

        if (!facts.GpuCompositeOptedIn)
        {
            sb.AppendLine("compositing               CPU raster (GPU compositing is off)");
            sb.AppendLine("  Configure > Performance > Use the graphics card to blend layers.");
            return;
        }

        // Both counts are of the DEFERRED route only — the one B125 moved into the
        // draw op. The tiled and ring compositors build their own surfaces and
        // never reach this helper, so they are invisible here. Saying "0 on the
        // processor" without that is the third false line this file has had: it
        // reads as "no CPU compositing happened" when in truth all of it did.
        sb.AppendLine(
            $"compositing               of the publishes that could use the card: {gpu} did, {cpu} fell back");
        if (gpu + cpu == 0)
        {
            sb.AppendLine("  !! no publish even reached that path, so EVERY frame was composited");
            sb.AppendLine("     on the processor by the tiled or full-document compositor.");
            sb.AppendLine("     Only the culled route goes to the card today, and it needs the view");
            sb.AppendLine("     zoomed in past the document edges on a whole-canvas publish — a");
            sb.AppendLine("     fit-to-window view never takes it, and neither does playback.");
            return;
        }
        if (gpu == 0)
        {
            sb.AppendLine("  !! reached the path and fell back every time — see the refusals below.");
            return;
        }
        if (Rendering.GpuComposite.RefusedAllocations > 0)
        {
            sb.AppendLine(
                $"  !! {Rendering.GpuComposite.RefusedAllocations} GPU surface(s) refused and fell back to the processor.");
        }
        if (Rendering.GpuComposite.RefusedTooLarge > 0)
        {
            sb.AppendLine(
                $"  !! {Rendering.GpuComposite.RefusedTooLarge} composite(s) were larger than this card's textures.");
        }
    }

    /// <summary>
    /// What the process is holding, against what the caches admit to holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B179: a machine reached 12 GB and crashed while every budget line in
    /// this report read comfortably inside its limit.</b> That is not a
    /// contradiction, it is a gap — the report only ever printed the pools it
    /// knows about, so a leak anywhere else was invisible by construction, and
    /// the four reassuring lines above actively pointed away from it.
    /// </para>
    /// <para>
    /// <b>The number that makes this section worth having is the difference.</b>
    /// Each cache's own figure is already printed elsewhere; what was missing is
    /// the total beside them, so "the caches are fine" can be distinguished from
    /// "the caches are fine and the process is not". Only the second is a leak in
    /// something this report can see.
    /// </para>
    /// <para>
    /// <b>And two pools were tracked but never printed:</b> the bytes each cache
    /// has evicted and cannot free because a published snapshot is still reading
    /// them. They are excluded from <c>CachedBytes</c> on purpose — they are not
    /// cache contents — and are <em>unbounded</em>, which makes them the first
    /// place to look rather than a footnote.
    /// </para>
    /// </remarks>
    private static void AppendMemory(StringBuilder sb, Facts facts)
    {
        sb.AppendLine("-- what memory is held (B179) --------------------------------");

        long working = 0, managed = 0;
        try
        {
            using var self = Process.GetCurrentProcess();
            working = self.WorkingSet64;
        }
        catch { /* a diagnostic must never be the thing that fails */ }
        try { managed = GC.GetTotalMemory(forceFullCollection: false); }
        catch { /* as above */ }

        static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0} MB";

        var frames = facts.FrameCache?.Bytes ?? 0;
        var flats = facts.FlattenCache?.Bytes ?? 0;
        var textures = facts.TextureResidency?.Bytes ?? 0;
        var tiles = facts.TileStore?.Bytes ?? 0;
        var waiting = (facts.AwaitingUnpin?.Frames ?? 0) + (facts.AwaitingUnpin?.Flattens ?? 0);
        var accounted = frames + flats + textures + tiles + waiting;

        if (working > 0) sb.AppendLine($"process working set       {Mb(working)}");
        sb.AppendLine($"  managed heap            {Mb(managed)}");
        sb.AppendLine($"accounted for by caches   {Mb(accounted)}");
        sb.AppendLine($"  rendered frames         {Mb(frames)}");
        sb.AppendLine($"  flattened tiles         {Mb(flats)}");
        sb.AppendLine($"  layer textures          {Mb(textures)}");
        // Budgeted since the derived-budgets change and never printed anywhere:
        // the tile path section counts passes, not bytes.
        sb.AppendLine($"  tiles                   {Mb(tiles)}");

        // The pools that were tracked and never printed, and the reason this
        // section exists. Named even at zero: an absent line reads as "nothing
        // was wrong" when it means "nothing was looked at".
        if (facts.AwaitingUnpin is { } held)
        {
            sb.AppendLine($"  evicted, still in use   {Mb(waiting)}"
                          + $"   (frames {Mb(held.Frames)}, flattens {Mb(held.Flattens)})");
        }
        if (facts.Pinned is { } pins)
        {
            sb.AppendLine($"pinned by live snapshots  {pins.Frames} frame bitmap(s), "
                          + $"{pins.Flattens} flatten(s)");
        }

        // Skia's own two caches (B179's second narrowing). Neither is a budget
        // this application owns, and both purge to a limit rather than on
        // dispose — so a texture handed back is purgeable, not freed, and the
        // cache sitting at its ceiling is design rather than a leak. Printed
        // because the first capture found 2362 MB outside everything above, and
        // "outside everything we track" is only useful once the list is long
        // enough to be worth trusting.
        // Which diagnostic mode this capture was taken in. Printed here rather
        // than left to the person who ran it, because this session has already
        // watched two reports get read against each other as though they came
        // from the same build — and a discriminator whose state is invisible
        // turns a decisive experiment back into an argument.
        if (Rendering.GpuComposite.Budgeted || Rendering.GpuComposite.ResidencyDisabled)
        {
            sb.AppendLine("diagnostic mode           NOT an ordinary capture (B179)");
            if (Rendering.GpuComposite.Budgeted)
                sb.AppendLine("  compose surfaces are BUDGETED — Skia accounts for and may purge them");
            if (Rendering.GpuComposite.ResidencyDisabled)
                sb.AppendLine("  layer residency is OFF — every layer is uploaded again per frame");
        }

        var skiaCpu = Rendering.SkiaMemory.Cpu;
        sb.AppendLine($"skia's own caches");
        sb.AppendLine(Rendering.SkiaMemory.Gpu is { } skiaGpu
            ? $"  gpu resources           {Mb(skiaGpu.Used)} of {Mb(skiaGpu.Limit)}"
            : "  gpu resources           not measured — no frame has drawn on a GPU lease yet");
        sb.AppendLine($"  cpu images and glyphs   {Mb(skiaCpu.Used)} of {Mb(skiaCpu.Limit)}");

        if (working > 0 && accounted > 0)
        {
            var skiaHeld = (Rendering.SkiaMemory.Gpu?.Used ?? 0) + skiaCpu.Used;
            var unaccounted = working - accounted - skiaHeld;
            sb.AppendLine();
            if (skiaHeld > 0)
            {
                sb.AppendLine($"  >> Skia is holding {Mb(skiaHeld)} on its own account, on top of the");
                sb.AppendLine($"     {Mb(accounted)} above.");
            }
            sb.AppendLine($"  >> {Mb(unaccounted)} is NOT in any cache this report tracks.");
            // A managed process carries a runtime, an Avalonia tree and Skia's own
            // context; a few hundred megabytes over is ordinary and says nothing.
            if (unaccounted > 2L * 1024 * 1024 * 1024)
            {
                sb.AppendLine("     That is more than two gigabytes outside the budgets, which no");
                sb.AppendLine("     amount of ordinary overhead explains. The caches being inside");
                sb.AppendLine("     their limits is therefore NOT evidence that memory is healthy —");
                sb.AppendLine("     it means the growth is somewhere these budgets do not reach.");
                sb.AppendLine("     Check 'evicted, still in use' first: it is unbounded by design,");
                sb.AppendLine("     and it only grows when snapshots are not being released.");
            }
        }

        if (facts.Pinned is { Frames: > 64 } or { Flattens: > 64 })
        {
            sb.AppendLine("  !! a large number of bitmaps are pinned. A pin is dropped when a");
            sb.AppendLine("     published snapshot is released, so a count that climbs means");
            sb.AppendLine("     snapshots are being held — and a pinned bitmap is one eviction");
            sb.AppendLine("     cannot free, whatever the budget says.");
        }

        sb.AppendLine();
    }

    private static void AppendTextureResidency(StringBuilder sb, Facts facts)
    {
        if (!facts.GpuCompositeOptedIn) return;

        sb.AppendLine("-- resident layer textures (B125 stage 5) --------------------");
        if (facts.TextureResidency is not { } r || r.Hits + r.Misses == 0)
        {
            sb.AppendLine("no layer textures were asked for.");
            sb.AppendLine();
            // This said "nothing composited through it — the culled route is the
            // only one that goes to the GPU today", which was written before
            // B167 phases 3b and 4 and was still printed on 2026-08-12 directly
            // under a line reporting 310 publishes ON the card. Two lines of one
            // report contradicting each other is worse than either being absent,
            // because a reader has no way to tell which one is stale.
            //
            // So it now says what it can actually see, and asks the other counter
            // rather than asserting a route.
            if (Rendering.GpuComposite.GpuComposites > 0)
            {
                sb.AppendLine($"  But {Rendering.GpuComposite.GpuComposites} publish(es) DID composite on the card, so this is not");
                sb.AppendLine("  \"the GPU is unused\" — it is the blend running on the card while every");
                sb.AppendLine("  layer is uploaded again for it. Residency is what stops the re-upload,");
                sb.AppendLine("  so a zero here next to a non-zero above is a wiring fault, not a");
                sb.AppendLine("  setting. That exact pair is what found one on 2026-08-12.");
            }
            else
            {
                sb.AppendLine("  GPU compositing is switched on and nothing has composited through it");
                sb.AppendLine("  yet. Playback takes the tiled route and drawing takes the ring, so a");
                sb.AppendLine("  report written from a still canvas can legitimately show this — play");
                sb.AppendLine("  a range for a few seconds and write it again.");
            }
            sb.AppendLine();
            return;
        }

        var total = r.Hits + r.Misses;
        var rate = (double)r.Hits / total;
        sb.AppendLine($"uploads avoided           {r.Hits} of {total} layer draws ({rate * 100:0.0}%)");
        sb.AppendLine($"resident                  {r.Bytes / (1024.0 * 1024.0):0.0} MB");
        sb.AppendLine();
        sb.AppendLine("  This counts only layer draws that went through the GPU path at all —");
        sb.AppendLine("  the culled route. Compare it with the tile path's layer-pass count");
        sb.AppendLine("  above: if that is thousands and this is dozens, almost nothing is");
        sb.AppendLine("  being composited on the card, and the hit rate here is about a handful");
        sb.AppendLine("  of publishes rather than about playback.");
        sb.AppendLine();
    }

    private static void AppendPresentWait(StringBuilder sb, Facts facts)
    {
        var wait = facts.PresentWait;
        sb.AppendLine("-- did the frames reach the screen (B150) --------------------");
        if (wait is not { Presented: > 0 } stats)
        {
            sb.AppendLine("frames drawn              none yet — this needs the scene PLAYED first");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"frames published and drawn {stats.Presented}");
        sb.AppendLine($"  mean wait to be drawn    {stats.MeanMs:0.##} ms");
        sb.AppendLine($"  worst wait               {stats.WorstMs:0.##} ms");
        sb.AppendLine($"replaced before drawing    {stats.Superseded}");

        AppendPresentWaitByInput(sb, stats, facts.AnimationFrames);

        sb.AppendLine();
        sb.AppendLine(
            wait is { } w && facts.AnimationFrames is { } frames
                ? $"compositor wake-ups       asked {frames.Requested}, arrived {frames.Delivered}"
                  + (frames.Requested > 0 && frames.Delivered < frames.Requested * 0.9
                      ? "   << the compositor is NOT waking on request"
                      : "")
                : "compositor wake-ups       not measured");
        sb.AppendLine();
        // The threshold is a frame of a 60 Hz screen. Below it the compositor is
        // picking work up on its next tick, which is as fast as anything can go;
        // well above it, frames are being published and then sitting.
        sb.AppendLine(stats.MeanMs < 17
            ? "  >> Frames are reaching the screen promptly. Combined with the section\n"
              + "     above, that rules the front end out: if playback still looks uneven,\n"
              + "     the cost is in making the frames rather than in showing them."
            : "  >> Frames are being published and then WAITING. That is a different\n"
              + "     fault from a late clock and needs a different fix. Capture this\n"
              + "     twice — pointer still, then pointer moving. If the wait collapses\n"
              + "     while the pointer moves, the compositor is only being woken by\n"
              + "     input, and playback is riding on that rather than on its own clock.");

        sb.AppendLine();
    }

    /// <summary>
    /// Where a playback tick's time actually went.
    /// </summary>
    /// <remarks>
    /// <b>The section that turns a localisation into a diagnosis.</b> The two
    /// above establish that the clock is late and that frames reach the screen
    /// promptly, which narrows the cost to the tick handler and stops. This says
    /// which part of the handler, in milliseconds, on the machine that has the
    /// problem — which is the step that B152 had to guess at and could not prove.
    /// </remarks>
    private static void AppendTickBreakdown(StringBuilder sb, Facts facts)
    {
        sb.AppendLine("-- where the tick's time went --------------------------------");

        if (facts.Scene is { } shape)
        {
            // Printed even when nothing played: how many frames and layers there
            // are decides whether the cache below can hold the scene, and it is
            // the fact the first symptomatic report was missing.
            sb.AppendLine($"scene                     {shape.Frames} frames, {shape.Layers} layers, {shape.Strokes} strokes");
            // The number every other line in this section is meant to be read
            // against, and it was missing: the tick breakdown below says "compare
            // that with the frame period for this scene's fps" and then did not
            // print the fps or the period, so the one comparison the report asks
            // for was the one it made you do from memory.
            if (shape.Fps > 0)
            {
                sb.AppendLine($"playing at                {shape.Fps:0.##} fps"
                              + $"  — a frame every {1000.0 / shape.Fps:0.#} ms, and that is the budget below");
            }
        }

        if (facts.FrameCache is { } cache)
        {
            var lookups = cache.Hits + cache.Misses;
            var missShare = lookups == 0 ? 0 : 100.0 * cache.Misses / lookups;
            sb.AppendLine($"frame cache               {cache.Bytes / (1024 * 1024)} MB held of {cache.Budget / (1024 * 1024)} MB");
            sb.AppendLine($"  served from memory      {cache.Hits}");
            sb.AppendLine($"  had to render           {cache.Misses}  ({missShare:0.#}%)");
            sb.AppendLine($"  thrown out              {cache.Evictions}");
        }

        // B167 phase 2. Printed beside the frame cache because they answer the
        // same question about different work, and printed even when it never hits
        // — a cache reading 0% is a finding, and an absent line reads as a cache
        // nobody measured. That distinction is what the tile-flatten phase line
        // above was missing for a whole round of reports.
        if (facts.FlattenCache is { } flats && flats.Hits + flats.Misses > 0)
        {
            var lookups = flats.Hits + flats.Misses;
            var hitShare = 100.0 * flats.Hits / lookups;
            sb.AppendLine($"flattened tiles           {flats.Bytes / (1024 * 1024)} MB held of {flats.Budget / (1024 * 1024)} MB");
            sb.AppendLine($"  reused a flatten        {flats.Hits}  ({hitShare:0.#}%)");
            sb.AppendLine($"  had to flatten          {flats.Misses}");
            sb.AppendLine($"  thrown out              {flats.Evictions}");
        }

        if (facts.TickPhases is not { Count: > 0 } phases || facts.TickCount == 0)
        {
            sb.AppendLine("tick breakdown            none yet — this needs the scene PLAYED first");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"playback ticks            {facts.TickCount}");
        if (facts.FramesReused > 0)
        {
            sb.AppendLine($"  frames not composited   {facts.FramesReused}  (B165: the playhead moved but no");
            sb.AppendLine("                          visible layer changed drawing, so the picture was reused)");
        }
        var total = 0.0;
        foreach (var phase in phases)
        {
            // A nested phase is measured INSIDE another one, so adding it to the
            // total would count its time twice and make ALL PHASES exceed the
            // tick it is meant to describe. Printed indented, under the phase it
            // breaks down, and left out of the sum (B167 phase 1).
            var nested = phase.Phase is TickProfile.Phase.TileFlatten;
            if (phase.Calls == 0)
            {
                if (nested) continue;
                // Named rather than omitted: a phase that ran zero times is a
                // finding — it is what B152's fix looks like from here — and an
                // absent line reads as a phase nobody measured.
                sb.AppendLine($"  {phase.Phase,-22} never ran");
                continue;
            }
            if (!nested) total += phase.TotalMs;
            var label = nested ? "  of it, " + phase.Phase : phase.Phase.ToString();
            // A nested phase runs once per PASS, not once per tick, so "816 of
            // 300 ticks" reads as nonsense and its worst — which is per call —
            // sits below its mean, which is per tick. Both were true and the line
            // said neither. Own units, said out loud.
            var counts = nested
                ? $"({phase.Calls} calls, worst one {phase.WorstMs:0.##} ms)"
                : $"worst {phase.WorstMs,7:0.##} ms   ({phase.Calls} of {facts.TickCount} ticks)";
            sb.AppendLine($"  {label,-22} {phase.TotalMs / facts.TickCount,7:0.##} ms/tick   {counts}");
        }
        sb.AppendLine($"  {"ALL PHASES",-22} {total / facts.TickCount,7:0.##} ms/tick");
        // Phase 1's whole product: which half of the tiled composite costs the
        // frame. Said here rather than left to subtraction, because the reader
        // who needs it is deciding what to optimise next.
        if (phases.FirstOrDefault(p => p.Phase == TickProfile.Phase.TileFlatten) is { Calls: > 0 } flat
            && phases.FirstOrDefault(p => p.Phase == TickProfile.Phase.Compose) is { TotalMs: > 0 } comp)
        {
            var share = flat.TotalMs / comp.TotalMs;
            sb.AppendLine();
            sb.AppendLine($"  >> Flattening tiles is {share * 100:0}% of Compose, blending is the rest.");
            sb.AppendLine(share > 0.6
                ? "     Flattening dominates: caching the flattened bitmap (B167 phase 2) is\n"
                  + "     the win, and moving the blend to the card would barely register."
                : share < 0.3
                    ? "     Blending dominates: getting the composite onto the card (B167\n"
                      + "     phases 3-4) is the win, and caching the flatten would barely register."
                    : "     Neither dominates, so both halves of B167 are worth having and\n"
                      + "     neither alone will fix playback.");
        }

        AppendWorkOutsideTheTick(sb, facts, total / facts.TickCount);

        sb.AppendLine();
        var worstPhase = phases.Where(p => p.Calls > 0).OrderByDescending(p => p.TotalMs).FirstOrDefault();
        if (worstPhase.Calls > 0)
        {
            sb.AppendLine(
                $"  >> The tick spends most of itself in {worstPhase.Phase}"
                + $" ({worstPhase.TotalMs / facts.TickCount:0.##} ms/tick).");
            sb.AppendLine("     Compare that with the frame period for this scene's fps: anything");
            sb.AppendLine("     approaching it makes the clock late whatever else is true, and the");
            sb.AppendLine("     lateness above is the consequence rather than a separate fault.");
        }

        sb.AppendLine();
    }

    /// <summary>
    /// Why the durable frame is or is not on the GPU, in words rather than a
    /// boolean.
    /// </summary>
    /// <remarks>
    /// <b>The first report from a real machine was misread because of this line,
    /// and the misreading was the report's fault.</b> It printed
    /// <c>durable frame on GPU: no</c> on a build where B130 had switched the
    /// durable frame off entirely — so "no" was true and meant nothing like what
    /// it appeared to mean. Four states share that one boolean: switched off, on
    /// but not yet used, working, and fell back. Only the last is a problem, and a
    /// diagnostic that cannot separate "not in use" from "failed" is worse than
    /// silence, because it invites exactly the wrong conclusion.
    /// </remarks>
    private static string DurableFrameState(Facts f)
    {
        if (!f.DurableFrameEnabled)
        {
            return "off — B130 disabled it by default; set LIGHTBOX_DURABLE_FRAME=1 to measure it";
        }
        if (f.GpuSurfaceRequestFailed)
        {
            return "NO — a GPU surface was asked for and refused, so it fell back to CPU";
        }
        if (!f.DurableFrameHasPresented)
        {
            return "on, but nothing has been presented yet — take an on-demand report after drawing";
        }
        return f.PresentedFrameOnGpu ? "yes" : "no — running on a CPU surface";
    }

    /// <summary>
    /// Write the startup report, once. Returns the file, or null if nothing was
    /// written — including because it has already been written this run.
    /// </summary>
    internal static string? WriteStartup(Facts facts)
    {
        lock (Gate)
        {
            if (_startupWritten) return null;
            _startupWritten = true;
        }
        return Write(StartupFile, Compose("Startup", facts, null, null));
    }

    /// <summary>
    /// Write a full report: the same facts, plus what has been measured since the
    /// application started, plus an upload probe if one was run.
    /// </summary>
    internal static string? WriteOnDemand(Facts facts, Totals? totals, Probe? probe)
    {
        // Timestamped rather than overwritten: these get taken one after another
        // while changing a setting, and the comparison IS the measurement.
        var name = $"render-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        return Write(name, Compose("On demand", facts, totals, probe));
    }

    /// <summary>What the presentation frame has done so far this run.</summary>
    internal readonly record struct Totals(
        long Presents,
        long FullPresents,
        long FreePresents,
        long PatchedPixels,
        long PixelsIfAlwaysFull,
        double PublishMedianMs,
        double FrameMedianMs);

    /// <summary>
    /// The B122 claim, timed on real hardware: a full-frame present against a
    /// dab-sized one, same surface, same backend.
    /// </summary>
    internal readonly record struct Probe(
        int Width, int Height, int PatchWidth, int PatchHeight,
        double FullMsMedian, double PatchMsMedian, bool WasGpuBacked);

    /// <summary>
    /// Time a full present against a patch present at the given size. Runs only
    /// when a report is asked for, never at startup — startup cost is guarded by
    /// its own tests and a splash-screen contract.
    /// </summary>
    /// <param name="gpu">
    /// The lease's context, or null. A null context still produces a valid
    /// comparison; it simply measures the CPU path, and the report says so.
    /// </param>
    internal static Probe? RunUploadProbe(GRContext? gpu, int width, int height, int iterations = 12)
    {
        try
        {
            if (width <= 0 || height <= 0) return null;
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var sourceSurface = SKSurface.Create(info);
            if (sourceSurface is null) return null;
            sourceSurface.Canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Black };
            sourceSurface.Canvas.DrawRect(new SKRect(0, 0, width / 2f, height / 2f), paint);
            sourceSurface.Canvas.Flush();
            using var source = sourceSurface.Snapshot();

            // A dab, at the size B121 measured one at.
            var patch = new SKRectI(width / 2, height / 2, Math.Min(width, width / 2 + 44), Math.Min(height, height / 2 + 28));
            if (patch.Width <= 0 || patch.Height <= 0) return null;

            using var frame = new PresentedFrame();

            // Warm up: the first present allocates, and an allocation timed as a
            // present is the attribution error this repo keeps a design note about.
            frame.Present(gpu, source, null);
            frame.Present(gpu, source, patch);

            var full = Median(iterations, () =>
            {
                frame.ForceFull();
                frame.Present(gpu, source, null);
            });
            var patched = Median(iterations, () => frame.Present(gpu, source, patch, long.MinValue));

            return new Probe(
                width, height, patch.Width, patch.Height, full, patched, frame.IsGpuBacked);
        }
        catch
        {
            return null;
        }
    }

    private static double Median(int iterations, Action body)
    {
        var samples = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            body();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        return samples[samples.Count / 2];
    }

    private static string Compose(string kind, Facts facts, Totals? totals, Probe? probe)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Lightbox render report ({kind})");
        sb.AppendLine($"written   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"build     {DiagnosticLog.Build}");
        sb.AppendLine($"os        {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")})");
        sb.AppendLine($"cpus      {Environment.ProcessorCount}");
        sb.AppendLine();

        sb.AppendLine("-- where the work happens ------------------------------------");
        sb.AppendLine($"presentation backend      {facts.Backend}");
        // Was "compositing is on the CPU either way", which stopped being true
        // when B167 phase 4 put the tiled composite on the card. Derived from the
        // toggle rather than asserted, so it cannot go stale the same way twice.
        sb.AppendLine(facts.GpuCompositeOptedIn
            ? "  (the final blit — see the compositing line below for where blending happened)"
            : "  (this is the FINAL BLIT only — compositing is on the CPU, which is the default)");
        sb.AppendLine($"durable frame (B122)      {DurableFrameState(facts)}");
        if (facts.GpuSurfaceRequestFailed)
        {
            sb.AppendLine("  !! a GPU surface was asked for and could not be created, so the");
            sb.AppendLine("     frame fell back to CPU raster. The upload saving is ABSENT on");
            sb.AppendLine("     this machine even though the backend above says GPU. This is");
            sb.AppendLine("     the line to report — see B122 and B125.");
        }
        sb.AppendLine($"max texture size          {facts.MaxTextureSize?.ToString() ?? "unknown"}");
        // The comparison rather than the two numbers, because the number alone
        // invites the guess. The first real report showed 16384 against a 960-wide
        // surface, which settles the question it was raised about — a texture limit
        // cannot be why a 4K canvas feels slow — and saying so here means nobody
        // has to work it out twice.
        if (facts.MaxTextureSize is { } limit && limit > 0)
        {
            var widest = Math.Max(
                (int)Math.Ceiling(facts.DocWidth * facts.ComposeScale),
                (int)Math.Ceiling(facts.DocHeight * facts.ComposeScale));
            sb.AppendLine(widest > limit
                ? $"  !! the compose surface's {widest} px exceeds it — a GPU surface cannot be made"
                : $"  the compose surface's widest side is {widest} px, so the limit is not a factor");
        }
        AppendCompositor(sb, facts);
        sb.AppendLine();

        AppendTilePath(sb, facts.TileFallbacks);
        AppendPrewarm(sb, facts.Prewarm);
        AppendPacing(sb, facts.Pacing);
        AppendPresentWait(sb, facts);
        AppendTickBreakdown(sb, facts);

        sb.AppendLine("-- what is being drawn ---------------------------------------");
        sb.AppendLine($"document                  {facts.DocWidth} x {facts.DocHeight}");
        sb.AppendLine($"display scale             {facts.DisplayScale:0.###}");
        sb.AppendLine($"canvas quality            {facts.CanvasQuality}");
        sb.AppendLine($"compose scale             {facts.ComposeScale:0.###}");
        var surfaceW = (int)Math.Ceiling(facts.DocWidth * facts.ComposeScale);
        var surfaceH = (int)Math.Ceiling(facts.DocHeight * facts.ComposeScale);
        sb.AppendLine($"compose surface           {surfaceW} x {surfaceH}"
                      + $"  ({surfaceW * (long)surfaceH * 4 / 1024.0 / 1024.0:0.0} MB per full frame)");
        sb.AppendLine();

        // Say when there is nothing to say. A startup report has no measurements
        // by construction — it is written on the first frame — and a section that
        // is simply missing reads as "nothing was wrong" rather than "nothing was
        // looked at".
        if (kind == "Startup")
        {
            sb.AppendLine("-- measured ---------------------------------------------------");
            sb.AppendLine("Nothing yet: this is the startup report, written on the first frame");
            sb.AppendLine("before anything has been drawn. For timings, counters and the upload");
            sb.AppendLine("probe, draw for a while and then use Help > Write a render report.");
            sb.AppendLine();
        }

        if (totals is { } t && t.Presents > 0)
        {
            sb.AppendLine("-- measured, this session ------------------------------------");
            sb.AppendLine($"presents                  {t.Presents}  ({t.FullPresents} full, "
                          + $"{t.Presents - t.FullPresents} patched)");
            sb.AppendLine($"repaints that copied none {t.FreePresents}   (cursor moves — these should dominate)");
            sb.AppendLine($"pixels copied             {t.PatchedPixels:N0}");
            sb.AppendLine($"  if always full          {t.PixelsIfAlwaysFull:N0}");
            if (t.PixelsIfAlwaysFull > 0)
            {
                sb.AppendLine($"  saved                   {100.0 - 100.0 * t.PatchedPixels / t.PixelsIfAlwaysFull:0.0}%"
                              + $"  ({t.PixelsIfAlwaysFull / (double)Math.Max(1, t.PatchedPixels):0.0}x less)");
            }
            sb.AppendLine($"publish                   median {t.PublishMedianMs:0.00} ms");
            sb.AppendLine($"frame                     median {t.FrameMedianMs:0.00} ms"
                          + "   (16.7 ms is 60 fps)");
            sb.AppendLine();
        }

        AppendTextureResidency(sb, facts);
        AppendMemory(sb, facts);

        if (probe is { } p)
        {
            sb.AppendLine("-- upload probe (B122, timed here rather than inferred) ------");
            sb.AppendLine($"surface                   {p.Width} x {p.Height}, GPU-backed {Yes(p.WasGpuBacked)}");
            sb.AppendLine($"full present              {p.FullMsMedian:0.000} ms  (median)");
            sb.AppendLine($"patch present {p.PatchWidth,3} x {p.PatchHeight,-3}   {p.PatchMsMedian:0.000} ms  (median)");
            if (p.PatchMsMedian > 0)
            {
                sb.AppendLine($"speedup                   {p.FullMsMedian / p.PatchMsMedian:0.0}x");
            }
            sb.AppendLine();
            sb.AppendLine("  A speedup near 1x on a GPU-backed surface means the patch is not");
            sb.AppendLine("  the thing costing time, and the remaining cost is the CPU");
            sb.AppendLine("  composite (B125) rather than the transfer.");
            sb.AppendLine();
            // Said here because it is the obvious and wrong assumption: that a
            // disabled feature cannot be measured. The probe builds its own frame
            // against the real context, so this number is about the hardware
            // whether or not the paint path is using it.
            sb.AppendLine("  This probe runs against the real graphics context regardless of");
            sb.AppendLine("  whether the durable frame is switched on above — it builds its own.");
            sb.AppendLine("  So these two numbers are the GPU measurement, and they are valid");
            sb.AppendLine("  even when the line above says \"off\".");
            sb.AppendLine();
        }

        sb.AppendLine("Nothing in this file is sent anywhere. It exists to be read or");
        sb.AppendLine("attached to a report.");
        return sb.ToString();

        static string Yes(bool b) => b ? "yes" : "no";
    }

    private static string? Write(string name, string body)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticLog.Directory);
            var path = Path.Combine(DiagnosticLog.Directory, name);
            File.WriteAllText(path, body);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Tests only: let a second startup report be written.</summary>
    internal static void ResetForTests()
    {
        lock (Gate) _startupWritten = false;
    }
}
