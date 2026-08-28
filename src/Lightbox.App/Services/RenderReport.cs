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
        (int Repaired, int Dropped)? FrameEdits = null,
        IReadOnlyList<string>? FrameDropCallers = null,
        (int Slow, int SlowWithMiss)? SlowBuilds = null,
        (int Audited, int WithLoss, double WorstLossPercent, double LastLossPercent)? InkAudit = null,
        DateTime LaunchedAt = default,
        IReadOnlyList<(DateTime Began, double ToFirstInkMs, double LastedMs, int Points, int Dabs)>?
            StrokeLog = null,
        (double RestoreMs, double SettledMs, double BackupMs, double TailMs, double TailMpx,
            double TailMpxP90, double ColourMs, double FootprintMs,
            double FootprintScale)? StampParts = null,
        IReadOnlyList<(double Ms, double AtSeconds, int Points, int Dabs, long Misses, double DescribeMs)>?
            SlowBuildLog = null,
        (double LostMs, double SessionMs)? StallCensus = null,
        IReadOnlyList<(double Ms, double AtSeconds, int Points, int Outstanding, bool TipRefused,
            bool Missed, long EventsInGap, double StampMs)>? PreviewGaps = null,
        IReadOnlyList<(string FrameId, int Width, int Height, double Scale, int Cel, string Why)>?
            FrameCacheMisses = null,
        (int Frames, int Layers, int Strokes, double Fps)? Scene = null,
        (long Requested, long Delivered)? AnimationFrames = null,
        double RenderMedianMs = 0,
        (int Hits, int Misses, long Bytes)? TextureResidency = null,
        bool GpuCompositeOptedIn = false,
        int FramesReused = 0,
        (int Hits, int Misses, int Evictions, long Bytes, long Budget)? FlattenCache = null,
        (long Frames, long Flattens)? AwaitingUnpin = null,
        (int Frames, int Flattens)? Pinned = null,
        (long Bytes, long Budget)? TileStore = null,
        Rendering.StrokeToScreen.Stats? StrokeWait = null,
        (int Passes, double TotalMs, double WorstMs,
         int Waits, double WaitTotalMs, double WaitWorstMs,
         long Pixels, long MarkPixels,
         int WorstW, int WorstH, long WorstMarkPixels, int WorstTail)? LivePost = null,
        (int Drawn, int TooFarBehind, int NoPass,
         double OutstandingMedian, double OutstandingWorst,
         double OutstandingP90, double OutstandingP99,
         double StampMedianMs, double StampWorstMs,
         double NewDabsMedian, double NewDabsP90, double NewDabsWorst,
         int Added, int Rebuilt,
         double DabsAddedMedian, double DabsRebuiltMedian, double DabsStampedMedian,
         double MarginalMs, double TipScale)? LiveTip = null,
        (double SettledMedian, double SettledP90,
         double ProvisionalMedian, double ProvisionalP90, double ProvisionalWorst,
         long Events, int WholeMarkEvents,
         double BandAtQueue, double BandAtStart, long Bands)? StampShape = null,
        (int Deferrals, int ByPresent, int ByTimer, int HoldsTimed,
         double HeldTotalMs, double HeldWorstMs,
         double LateTotalMs, double LateWorstMs, int ByEvent)? Dam = null,
        (double CycleMedianMs, double CycleMeanMs, long Cycles,
         double ReleaseToPublishMedianMs, double ReleaseToPublishMeanMs,
         double EventIntervalMedianMs, long Events)? Cycle = null,
        (int Count, double TotalMs, double WorstMs, double MedianMs, bool MeanDistorted)? Compose = null,
        (double DescribeMs, double ComposeMs, double HandoffMs)? BuildPhases = null,
        (double TotalMs, double DescribeMs, double ComposeMs, double HandoffMs, long Misses,
            double AtSeconds, int StrokePoints, int StrokeDabs)? WorstBuild = null,
        Rendering.PublishTally? PublishesByCaller = null);

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
            sb.AppendLine("  Tiles are used while the sequence is PLAYING. A report written");
            sb.AppendLine("  without playing says nothing about them.");
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

    /// <summary>
    /// What this present path cannot go below, and whether it is already there
    /// (B321).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The arithmetic has no free parameters, which is the only reason it is
    /// allowed to reach a verdict.</b> Its inputs are the refresh rate, which
    /// comes from the operating system and not from anything Lightbox measures,
    /// and the draw, which is measured. Everything else is counting: a frame
    /// published at no particular moment waits on average half a refresh for the
    /// visual pass that hands it over, and the compositor picks that hand-over
    /// up on the following refresh rather than the current one, so the floor is
    /// one and a half refreshes plus whatever the draw costs.
    /// </para>
    /// <para>
    /// <b>Why a floor is worth printing at all.</b> Six wrong hypotheses were
    /// recorded against this bug in two days, four of them confident, and every
    /// one of them was a number that looked large next to nothing. A latency
    /// with no floor beside it always looks like a defect. This line is what
    /// lets a reader tell "slow" from "as fast as this architecture goes", and
    /// it is deliberately printed whether the answer flatters the code or not.
    /// </para>
    /// </remarks>
    private static void AppendPresentFloor(StringBuilder sb, Rendering.PresentLatency.Stats stats)
    {
        if (Rendering.DisplayCadence.PeriodMs is not { } period || period <= 0)
        {
            sb.AppendLine("  screen refresh rate unknown, so no floor is computed here —");
            sb.AppendLine("  every number above is a duration with nothing to compare it to.");
            return;
        }

        var hz = Rendering.DisplayCadence.Hz ?? 0;
        var draw = stats.Queued > 0 ? stats.KeyedDrawMeanMs : stats.DrawMeanMs;
        var floor = (period * 1.5) + draw;
        var measured = stats.MedianMs > 0 ? stats.MedianMs : stats.MeanMs;

        sb.AppendLine(
            $"  the screen refreshes every {period:0.##} ms ({hz} Hz)");
        sb.AppendLine(
            $"  floor for this path       {floor:0.##} ms   (half a refresh for the visual pass,");
        sb.AppendLine(
            $"                            one for the pick-up, plus the {draw:0.##} ms draw)");
        sb.AppendLine(
            $"  measured (median)         {measured:0.##} ms   = {measured / period:0.##} refreshes");

        // A tenth of a refresh of slack: below that the two are the same number
        // measured twice, and claiming a difference would be reading noise.
        var slack = period * 0.1;
        var atFloor = measured <= floor + slack;

        // **The tenth percentile, and emphatically not the minimum.** This test
        // has now been wrong twice in one day in two opposite directions, and
        // the second time is the instructive one: reading the single smallest
        // sample, it called a wait whose p10 is 13.67 ms and p90 16.94 ms — 80%
        // of it inside a 3.3 ms band under one refresh — "not a gate", on the
        // strength of one 1.61 ms outlier below the tenth percentile. A gate
        // with a rare escape is still a gate for every frame that does not take
        // it, and the artist watches the typical frame.
        //
        // The clauses below were also once independent, so a capture could be
        // told in one breath that it sat at an unimprovable floor and that its
        // pick-up was a queue with something in it. Both printed together on
        // the owner's first capture. A section that can contradict itself is
        // worse than one that says less.
        var gateLike = stats.Queued == 0 || stats.QueueP10Ms > stats.QueueMedianMs * 0.5;

        if (atFloor && gateLike)
        {
            sb.AppendLine("  >> AT THE FLOOR. The frame is not slow to produce — it is waiting");
            sb.AppendLine("     for the screen, twice, and the work between the waits is small");
            sb.AppendLine("     enough to fit inside them. Making anything here faster buys");
            sb.AppendLine("     nothing; only removing a hop, or presenting without waiting for");
            sb.AppendLine("     the refresh, would move it. See B321.");
        }
        else if (atFloor)
        {
            sb.AppendLine("  >> THE TYPICAL FRAME IS AT THE FLOOR, AND IT IS NOT A GATE.");
            sb.AppendLine("     The median pick-up is about a refresh, but a TENTH of frames are");
            sb.AppendLine("     picked up in well under half of one — so this is not the screen");
            sb.AppendLine("     holding every frame, it is a race that most of them lose. Winning");
            sb.AppendLine("     it more often would take about a refresh off. See B321, which");
            sb.AppendLine("     closed on the opposite reading and would reopen on this one.");
        }
        else
        {
            sb.AppendLine(
                $"  >> {measured - floor:0.##} ms ABOVE the floor, so some of this is a cost rather than a");
            sb.AppendLine("     cadence. The phases above say which one is carrying it.");
            if (!gateLike)
            {
                sb.AppendLine("     The wait to be picked up DOES get short sometimes, so the render");
                sb.AppendLine("     thread is being kept busy rather than held. That is a queue and");
                sb.AppendLine("     it has something in it worth finding.");
            }
        }

        if (atFloor && gateLike && stats.Queued > 0)
        {
            sb.AppendLine("     The wait to be picked up never gets short — nine frames in ten");
            sb.AppendLine("     sit within a whisker of the typical one — so the render thread is");
            sb.AppendLine("     not busy, it is being held. That is a gate, not a queue, and the");
            sb.AppendLine("     stray fast one below is an outlier rather than a way through.");
        }

        sb.AppendLine();
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
    /// takes the tiled compositor. A report showing thousands of tiled
    /// layer passes and a few dozen GPU composites is that, and it is not
    /// something a reader should have to infer.
    /// </para>
    /// </remarks>
    private static void AppendCompositor(StringBuilder sb, Facts facts)
    {
        var gpu = Rendering.GpuComposite.GpuComposites;
        var cpu = Rendering.GpuComposite.CpuComposites;

        // What was asked for, and — on Automatic — what this machine answered.
        // Printed whichever way it went, because "the processor is blending" has
        // three different causes and only one of them is a setting.
        var mode = Rendering.GpuComposite.Mode;
        sb.AppendLine($"compositing asked for     {mode}");
        if (Rendering.GpuComposite.AutoProbe is { } probe)
        {
            sb.AppendLine($"  probe: {probe.Describe()}");
        }
        else if (mode == Rendering.GpuComposeMode.Auto)
        {
            sb.AppendLine("  probe: has not run this session — nothing has been drawn yet.");
        }

        if (!facts.GpuCompositeOptedIn)
        {
            sb.AppendLine("compositing               CPU raster");
            sb.AppendLine("  Configure > Performance > Composite layers on the GPU.");
            return;
        }

        // Both counts are of the DEFERRED route only — the one B125 moved into the
        // draw op. The tiled and ring compositors build their own surfaces and
        // never reach this helper, so they are invisible here. Saying "0 on the
        // processor" without that is the third false line this file has had: it
        // reads as "no CPU compositing happened" when in truth all of it did.
        sb.AppendLine(
            $"compositing               of the publishes that could use the card: {gpu} did, {cpu} fell back");
        sb.AppendLine("  counted since the toggle was last switched on, not since launch (B184).");
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
        // A fallback with no refusal behind it is a different thing entirely, and
        // "fell back" reads like a failure either way. Say which one it was: with
        // both refusal tallies at zero, the card was never asked — those publishes
        // ran on a lease that had no graphics context, which is ordinary.
        if (cpu > 0
            && Rendering.GpuComposite.RefusedAllocations == 0
            && Rendering.GpuComposite.RefusedTooLarge == 0)
        {
            sb.AppendLine("  none of those were refusals — the card was never asked, because those");
            sb.AppendLine("  publishes drew on a lease with no graphics context. Nothing is failing.");
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
        // B179's fix at work: retired GPU frames are freed on the render
        // thread, where the context is, instead of parked by a UI-thread
        // dispose. Printed whenever the machinery has been exercised, so the
        // capture that checks the fix can see it running — a large and growing
        // "waiting" against a still count of freed is the line to distrust.
        if (Rendering.GpuImageReaper.Reaped > 0 || Rendering.GpuImageReaper.PendingCount > 0)
        {
            sb.AppendLine($"gpu frames reaped         {Rendering.GpuImageReaper.Reaped} freed "
                          + $"on the render thread, {Rendering.GpuImageReaper.PendingCount} waiting");
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

    /// <summary>
    /// The composite cache (B167 phase 7), which is the one saving that pays
    /// with GPU compositing switched off.
    /// </summary>
    /// <remarks>
    /// <b>The hit rate is the number, and it has a predicted shape.</b> Lap one
    /// of a loop can only miss; lap two should hit nearly everything, because
    /// the blend is byte-identical to the one a second ago. So a rate near zero
    /// after a minute of playback does not mean "the cache is small" — it means
    /// the key is changing when the picture is not, and the epoch is the first
    /// thing to suspect.
    /// </remarks>
    private static void AppendComposeCache(StringBuilder sb)
    {
        var cache = Rendering.ComposeCacheHost.Shared;
        var asked = cache.Hits + cache.Misses;
        sb.AppendLine("-- composite cache (B167 phase 7) ----------------------------");
        if (asked == 0)
        {
            sb.AppendLine("nothing asked for a cached composite this session.");
            sb.AppendLine("  Only a playing document keys its publishes, and only when no");
            sb.AppendLine("  stroke is in flight — so this reads zero unless a scene was played.");
            sb.AppendLine();
            return;
        }

        var rate = 100.0 * cache.Hits / asked;
        sb.AppendLine($"frames served             {cache.Hits} of {asked} ({rate:F0}%)");
        sb.AppendLine(
            $"resident                  {cache.Count} composite(s), "
            + $"{cache.CachedBytes / (1024 * 1024)} MB of {cache.BudgetBytes / (1024 * 1024)} MB");
        sb.AppendLine($"dropped for the budget    {cache.Evictions}");
        if (rate < 10 && asked > 60)
        {
            sb.AppendLine("  !! a rate this low after real playback is a key that changes when");
            sb.AppendLine("     the picture does not. Suspect the render epoch first — anything");
            sb.AppendLine("     bumping it per frame makes every lap a first lap.");
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
        // B321: that wait, split where the UI thread hands the draw to the
        // compositor. Two explanations, opposite fixes, and one number cannot
        // choose between them.
        if (stats.Enqueued > 0)
        {
            var after = stats.MeanMs - stats.ToEnqueueMeanMs;
            sb.AppendLine(
                $"    waiting for a visual pass  {stats.ToEnqueueMeanMs:0.##} ms   median {stats.ToEnqueueMedianMs:0.##} ms   worst {stats.ToEnqueueWorstMs:0.##} ms");
            sb.AppendLine(
                $"    then in the compositor     {after:0.##} ms");
            // B321's third split, and the one the floor verdict turns on: the
            // compositor's half is a wait to be picked up plus a draw, and until
            // this line existed the difference between them was a subtraction
            // with nothing to attribute it to.
            if (stats.Queued > 0)
            {
                sb.AppendLine(
                    $"      waiting to be picked up  {stats.QueueMeanMs:0.##} ms   median {stats.QueueMedianMs:0.##} ms   best {stats.QueueBestMs:0.##} ms");
                sb.AppendLine(
                    $"        its spread             p10 {stats.QueueP10Ms:0.##} ms   p90 {stats.QueueP90Ms:0.##} ms");
                sb.AppendLine(
                    $"      then drawing             {stats.KeyedDrawMeanMs:0.##} ms   (this frame's own draw)");
            }

            if (stats.Draws > 0)
            {
                sb.AppendLine(
                    $"      of which drawing         {stats.DrawMeanMs:0.##} ms   worst {stats.DrawWorstMs:0.##} ms   (every draw, cursor repaints included)");
            }

            AppendPresentFloor(sb, stats);

            if (stats.MeanMs > 25)
            {
                // Three phases, so three verdicts. The draw is checked first
                // because it is the only one of them that is WORK — the other
                // two are a frame waiting its turn, and waiting is a cadence to
                // be understood rather than a cost to be optimised away.
                if (stats.Draws > 0 && after > stats.ToEnqueueMeanMs && stats.DrawMeanMs < after / 2)
                {
                    sb.AppendLine("  >> The DRAW is a small part of the compositor's half, so the");
                    sb.AppendLine("     frame is not slow to paint — it is waiting its turn on the");
                    sb.AppendLine("     render thread. That is a cadence (vsync, queue depth), and");
                    sb.AppendLine("     making the composite faster would not move it.");
                }
                else if (after > stats.ToEnqueueMeanMs)
                {
                    sb.AppendLine("  >> Most of the wait is AFTER the hand-over and the draw itself");
                    sb.AppendLine("     accounts for it, so the compositor really is slow to paint —");
                    sb.AppendLine("     the CPU composite (B125), not the dispatcher.");
                }
                else
                {
                    sb.AppendLine("  >> Most of the wait is BEFORE the hand-over, so the compositor is");
                    sb.AppendLine("     not the problem: the canvas is not being asked to paint. That");
                    sb.AppendLine("     is scheduling (B150), and the fix is a different one entirely.");
                }
            }
        }
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
    /// Who published while the clock was running, busiest first (B178).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The section B178's entry asked for by name: a per-caller publish
    /// tally, not another change to the publish path.</b> Its capture showed
    /// 757 publishes against 339 ticks — about 2.2 per tick — and the surplus
    /// is the backlog behind the 176 ms mean wait and the frames replaced
    /// unseen. A loop that presents at ~26 Hz against a playhead at ~10 Hz
    /// should never build a backlog; one does because something publishes
    /// when the playhead has not moved, and this table names it.
    /// </para>
    /// <para>
    /// The verdict compares publishes against ticks rather than judging any
    /// caller by its name, because the tick's own publishes are the baseline
    /// — one per advance — and everything above that line is the surplus,
    /// whoever made it.
    /// </para>
    /// </remarks>
    private static void AppendPublishTally(StringBuilder sb, Facts facts)
    {
        sb.AppendLine("-- who publishes during playback (B178) ----------------------");
        if (facts.PublishesByCaller is not { Total: > 0 } tally)
        {
            sb.AppendLine("publishes while playing   none yet — this needs the scene PLAYED first");
            sb.AppendLine();
            return;
        }

        var rows = tally.Snapshot();
        sb.AppendLine($"publishes while playing   {tally.Total}, from {rows.Count} caller(s)");
        foreach (var (caller, count) in rows)
        {
            sb.AppendLine($"  {caller,-24}{count}");
        }

        sb.AppendLine();
        if (facts.Pacing is { Ticks: > 0 } pacing)
        {
            var perTick = (double)tally.Total / pacing.Ticks;
            sb.AppendLine(perTick <= 1.15
                ? $"  >> {perTick:0.##} publish(es) per tick — the playhead accounts for the"
                  + "\n     publishing, so a backlog here is not being fed by a surplus."
                : $"  >> {perTick:0.##} publishes per tick. One per advance is the playhead's own;"
                  + "\n     the rest are the surplus B178 is about, and the biggest caller above"
                  + "\n     that is not the tick is where to look first.");
        }
        else
        {
            sb.AppendLine("  >> No tick data in this capture, so the table cannot be judged");
            sb.AppendLine("     against the playhead. Play the scene and write the report again.");
        }
        sb.AppendLine();
    }

    /// <summary>
    /// How long the ink takes to follow the pen, segment by segment (B189).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The section the "drawing lags" reports have been missing.</b> Every
    /// engine budget passes headless while the hand still feels the ink trail —
    /// because the lag lives in the waits <em>between</em> the pieces, and only
    /// a real dispatcher under real input has those. So the chain is measured in
    /// the shipped build and priced here: event → stamp → publish → drawn.
    /// </para>
    /// <para>
    /// <b>The verdict names the fattest segment</b>, because the three point at
    /// three different fixes: a fat stamp is engine or medium cost, a fat wait
    /// to publish is the dispatcher queue, and a fat wait to be drawn is the
    /// present path — B178's shape, showing up under drawing instead of
    /// playback. Naming the wrong one costs a round trip to the one machine
    /// that can measure this, which is why the report does the arithmetic.
    /// </para>
    /// </remarks>
    private static void AppendStrokeLatency(StringBuilder sb, Facts facts)
    {
        sb.AppendLine("-- pen to screen while drawing (B189) ------------------------");
        if (facts.StrokeWait is not { Events: > 0 } s)
        {
            sb.AppendLine("pointer events stamped    none yet — DRAW a few strokes, then write this again");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"pointer events stamped    {s.Events}");
        // **B332/B322: ink that was stamped and is not on screen.** Every other
        // number in this file measures WHEN something happened; the owner
        // reports ink DISAPPEARING, which no timer can see. `live tip drawn 505
        // of 505` with zero stalls sat beside "the first dabs are visible but
        // stop and disappear soon thereafter", and both were true.
        if (facts.InkAudit is { Audited: > 0 } ink)
        {
            sb.AppendLine(
                $"ink audit                 {ink.Audited} publishes checked,"
                + $" {ink.WithLoss} with ink missing");
            sb.AppendLine(
                $"  worst missing           {ink.WorstLossPercent:0.#}% of the stamped ink"
                + $"   (last {ink.LastLossPercent:0.#}%)");
            if (ink.WorstLossPercent > 1)
            {
                sb.AppendLine(
                    "  >> INK THAT WAS STAMPED IS NOT ON SCREEN. Not late — absent. A pass");
                sb.AppendLine(
                    "     writes only the band it processed and then records every dab below");
                sb.AppendLine(
                    "     it as done, so a dab whose pixels no band ever wrote is in neither");
                sb.AppendLine(
                    "     the body nor the tip. That is the mark vanishing while you draw.");
            }
            else
            {
                sb.AppendLine(
                    "  >> Everything stamped is on screen, so what the artist sees is LATE");
                sb.AppendLine(
                    "     ink rather than absent ink. Read PEN -> SCREEN, not this.");
            }
        }
        // **Wall clock, so a screen recording can be read beside this file.**
        // Every other time here is measured from launch and a video has no idea
        // when the application started. The owner recorded a session and could
        // not tell which stroke on screen was which row in the report.
        if (facts.StrokeLog is { Count: > 0 } strokes)
        {
            sb.AppendLine("  each stroke, by the clock:");
            sb.AppendLine(
                "      began        following      lasted    points     dabs");
            foreach (var st in strokes)
            {
                var firstInk = st.ToFirstInkMs < 0 ? "  never" : $"{st.ToFirstInkMs,6:0} ms";
                sb.AppendLine(
                    $"    {st.Began:HH:mm:ss.fff}   {firstInk}   {st.LastedMs,7:0} ms"
                    + $" {st.Points,9} {st.Dabs,8}");
            }

            sb.AppendLine(
                "  >> Line these up with a screen recording: began is when the pen went");
            sb.AppendLine(
                "     down, following is how long before the mark started FOLLOWING it.");
            sb.AppendLine(
                "     The opening dab is published by BeginStroke itself and says nothing.");
        }
        // B330: which of the numbers below cannot be quoted as costs. Each phase
        // is a latency distribution with stalls in it, and until these carried a
        // median the only honest reading was "some of this is a stall and you
        // cannot tell which line". The frame build had this warning already; the
        // chain an artist's lag is actually read from did not.
        var distorted = new List<string>();
        if (s.Stamp.MeanIsDistorted) distorted.Add("stamping");
        if (s.WaitToPublish.MeanIsDistorted) distorted.Add("event -> publish");
        if (s.WaitToDraw.MeanIsDistorted) distorted.Add("publish -> drawn");
        if (s.PenToScreen.MeanIsDistorted) distorted.Add("PEN -> SCREEN");
        if (s.TipToScreen.MeanIsDistorted) distorted.Add("TIP -> SCREEN");
        if (distorted.Count > 0)
        {
            sb.AppendLine(
                $"  !! a stall is doing the talking in: {string.Join(", ", distorted)}.");
            sb.AppendLine(
                "     Their means are more than twice their medians, so read the MEDIAN as");
            sb.AppendLine(
                "     the cost and the WORST as the thing to chase. Quoting one of those");
            sb.AppendLine(
                "     means is quoting an event rather than a cost.");
        }
        // B321: the UI thread's own half. Everything else in this section is a
        // wait; this is the work that happens before any of it.
        if (facts.Compose is { Count: > 0 } comp)
        {
            sb.AppendLine(
                $"building each frame      mean {comp.TotalMs / comp.Count,7:0.##} ms   median {comp.MedianMs,7:0.##} ms   worst {comp.WorstMs,7:0.##} ms   (UI thread, before the publish)");
            // Said out loud rather than left to whoever notices the two columns
            // disagree. A mean over a latency distribution with stalls in it
            // describes no frame that ever happened, and one taken on this
            // machine put 5.4 ms on a 3.2 ms build from a single 2-second
            // stall — which the report then explained confidently and wrongly.
            if (comp.MeanDistorted)
            {
                sb.AppendLine(
                    "  !! the mean is more than twice the median, so a stall is doing the");
                sb.AppendLine(
                    "     talking rather than the typical frame. Read the MEDIAN as the cost");
                sb.AppendLine(
                    "     and the WORST as the thing to chase; the phase split below is");
                sb.AppendLine(
                    "     built from means and inherits the same distortion.");
            }
            // Split, because the one number stopped naming its own cause once
            // it became the largest item in the chain. The three are different
            // fixes and the report should say which one is being asked for.
            if (facts.BuildPhases is { } ph)
            {
                var n = comp.Count;
                sb.AppendLine(
                    $"    describing it         mean {ph.DescribeMs / n,7:0.##} ms   (pass list, stack fold, cel fetches)");
                sb.AppendLine(
                    $"    compositing it        mean {ph.ComposeMs / n,7:0.##} ms   (the CPU blend, on this thread)");
                sb.AppendLine(
                    $"    handing it over       mean {ph.HandoffMs / n,7:0.##} ms   (snapshot swap and retire)");
                // Derived rather than measured, so the four ALWAYS sum to the
                // whole and a cost cannot hide between two stamps. The first
                // version of this split printed three numbers adding to 2.52 ms
                // of a 22.63 ms build and said nothing about the other 20 —
                // which was the frame capture, recording from inside the window
                // that was timing it.
                var rest = comp.TotalMs - ph.DescribeMs - ph.ComposeMs - ph.HandoffMs;
                sb.AppendLine(
                    $"    everything else       mean {rest / n,7:0.##} ms   (whatever the three above do not cover)");

                // **B332: the same split for the ONE build that stalled.** Every
                // number above is a mean over every build, and the distribution
                // here is two milliseconds typical against several SECONDS worst.
                // A mean over a thousand fast builds and one catastrophic one
                // describes the thousand. Four captures blamed "describing it"
                // for a six-second stall on exactly that evidence, which could
                // not have told a slow phase from one slow frame.
                if (facts.WorstBuild is { TotalMs: > 0 } w)
                {
                    sb.AppendLine("");
                    if (facts.SlowBuilds is { } slow)
                    {
                        // Printed at zero as well: "no line" and "no slow builds"
                        // are the same silence, and this is the line that says
                        // whether the stalling is one freeze or continuous.
                        sb.AppendLine(
                            $"  stalls (over {ViewModels.MainViewModel.StallMs:0} ms)      {slow.Slow}"
                            + $"   of which a cache miss was inside: {slow.SlowWithMiss}");
                        if (facts.StallCensus is { SessionMs: > 0 } census)
                        {
                            // **The number the artist would recognise.** Everything
                            // else here is a rate; this is how much of their time
                            // the application spent not responding.
                            sb.AppendLine(
                                $"    time lost to them     {census.LostMs / 1000:0.##} s of"
                                + $" {census.SessionMs / 1000:0.#} s drawing"
                                + $"   ({100 * census.LostMs / census.SessionMs:0.#}%)");
                        }
                        if (slow.Slow == 0)
                        {
                            sb.AppendLine(
                                "  >> NOT ONE build stalled, so whatever is being felt is not the frame");
                            sb.AppendLine(
                                "     build at all. Read the chain below instead.");
                        }
                        else if (slow.SlowWithMiss < slow.Slow)
                        {
                            sb.AppendLine(
                                $"  >> {slow.Slow - slow.SlowWithMiss} slow builds had NO cache miss in them, so B332 is not the");
                            sb.AppendLine(
                                "     whole story and fixing the cache will not stop the jumping.");
                        }
                        else
                        {
                            sb.AppendLine(
                                "  >> EVERY stall in the BUILD had a cache miss in it, so B332 accounts");
                            sb.AppendLine(
                                "     for the freezing. It does NOT account for lag: this counter only");
                            sb.AppendLine(
                                "     sees the frame build, and a chain that is late everywhere else");
                            sb.AppendLine(
                                "     shows up as one stall here and a slow median below. Read");
                            sb.AppendLine(
                                "     'already drawn, still held' before concluding the cache is all.");
                        }
                    }

                    // **All of them, not just the worst.** Two populations look
                    // identical in a single sample and obvious in a list: stalls
                    // at zero points are something between strokes, stalls at
                    // deep point counts are a cost that grows with the mark, and
                    // the artist reports both.
                    if (facts.SlowBuildLog is { Count: > 0 } log)
                    {
                        sb.AppendLine("    every stall, in order:");
                        sb.AppendLine(
                            "         at        ms   describing    points     dabs   misses");
                        foreach (var b in log)
                        {
                            sb.AppendLine(
                                $"    {b.AtSeconds,7:0.#} s {b.Ms,9:0} {b.DescribeMs,12:0}"
                                + $" {b.Points,9} {b.Dabs,8} {b.Misses,8}");
                        }

                        // **What the artist actually saw stop.** A build census
                        // counts frames that took too long to make; this counts
                        // moments the mark stopped moving under the pen. The two
                        // sets barely overlap, and the owner reported several
                        // stalls in a session whose build census found one.
                        if (facts.PreviewGaps is { Count: > 0 } gaps)
                        {
                            sb.AppendLine("");
                            sb.AppendLine(
                                $"  the preview stopped {gaps.Count} times while the pen was down:");
                            sb.AppendLine(
                                "     clock            at    gap ms   points  outstanding  events  stamping   why");
                            foreach (var g in gaps)
                            {
                                var why = g.TipRefused
                                    ? (g.Missed ? "tip refused + cache miss" : "TIP REFUSED (frame was on time)")
                                    : g.Missed ? "cache miss" : "publish gapped";
                                sb.AppendLine(
                                    $"  {facts.LaunchedAt.AddSeconds(g.AtSeconds):HH:mm:ss.fff}"
                                    + $" {g.AtSeconds,7:0.#} s {g.Ms,9:0} {g.Points,8} {g.Outstanding,12}"
                                    + $" {g.EventsInGap,7} {g.StampMs,9:0}   {why}");
                            }

                            // **Ours or the pen's.** A gap with no events in it is
                            // not the application being slow — nothing arrived to
                            // draw. The pen-interval tally cannot say this: it
                            // drops anything over 250 ms as a pause, which is
                            // exactly the silence being complained about.
                            // **What the thread was DOING, not what it produced.**
                            // A gap with input in it is ours; the next question is
                            // whether the time went on stamping the mark or on
                            // something else entirely, and a mean times a count
                            // cannot answer that — which is how this entry got the
                            // per-dab cost wrong three times.
                            var busiest = gaps
                                .Where(g => g.EventsInGap > 0 && g.Ms > 200)
                                .OrderByDescending(g => g.Ms)
                                .FirstOrDefault();
                            if (busiest.Ms > 0)
                            {
                                var share = 100 * busiest.StampMs / busiest.Ms;
                                sb.AppendLine(
                                    $"  >> The worst gap the app owns: {busiest.Ms:0} ms with"
                                    + $" {busiest.EventsInGap} events in it, of which"
                                    + $" {busiest.StampMs:0} ms ({share:0}%) was stamping.");
                                sb.AppendLine(share > 60
                                    ? "     The mark itself is the cost, so this is B189's ground."
                                    : "     Stamping is NOT most of it, so the thread was busy with");
                                if (share <= 60)
                                {
                                    sb.AppendLine(
                                        "     something else while the pen waited — look at the pass, the");
                                    sb.AppendLine(
                                        "     dam and the compositor before touching the brush engine.");
                                }
                            }

                            var starved = gaps.Count(g => g.EventsInGap == 0);
                            if (starved > 0)
                            {
                                sb.AppendLine(
                                    $"  >> {starved} of {gaps.Count} had NO pointer event arrive during them at all.");
                                sb.AppendLine(
                                    "     Those are not slow frames and not a refused tip: nothing came");
                                sb.AppendLine(
                                    "     in to draw. The pen, the driver or the OS held the input, and");
                                sb.AppendLine(
                                    "     the pen-interval median above cannot see it because it drops");
                                sb.AppendLine(
                                    "     anything over 250 ms as an artist's pause. See B255.");
                            }

                            var refused = gaps.Count(g => g.TipRefused);
                            sb.AppendLine(
                                $"  >> {refused} of {gaps.Count} were the tip being REFUSED, not a slow frame.");
                            if (refused > 0)
                            {
                                sb.AppendLine(
                                    "     Those frames arrived on time carrying a mark that had stopped");
                                sb.AppendLine(
                                    "     growing, which no timer on the build can see. That is B322,");
                                sb.AppendLine(
                                    "     and it is why a build census counts one stall in a session");
                                sb.AppendLine(
                                    "     the artist experienced as stalling repeatedly.");
                            }
                        }

                        var midStroke = log.Count(b => b.Points > 0);
                        var withMiss = log.Count(b => b.Misses > 0);
                        sb.AppendLine(
                            $"  >> {midStroke} of {log.Count} landed mid-stroke, {withMiss} of {log.Count} had a cache miss.");
                        if (midStroke > 0 && withMiss < log.Count)
                        {
                            sb.AppendLine(
                                "     Stalls with a stroke in flight and NO miss are a second fault, and");
                            sb.AppendLine(
                                "     they are the ones that would feel like lag rather than a freeze.");
                        }
                    }

                    sb.AppendLine(
                        $"  the WORST build alone   {w.TotalMs,8:0.##} ms   — where that one frame went:");
                    sb.AppendLine(
                        $"    it happened at        {w.AtSeconds,8:0.#} s into the session"
                        + $"   with {w.StrokePoints} points and {w.StrokeDabs} dabs under the pen");
                    if (w.StrokePoints == 0)
                    {
                        sb.AppendLine(
                            "  >> NO stroke was in flight, so this is not a cost that grows with the");
                        sb.AppendLine(
                            "     mark. Look at what happens between strokes: opening, committing,");
                        sb.AppendLine(
                            "     clearing, or the first publish of a frame nothing had rendered.");
                    }
                    else
                    {
                        sb.AppendLine(
                            $"  >> A stroke WAS in flight at {w.StrokePoints} points, so the stall lands");
                        sb.AppendLine(
                            "     mid-mark. Compare that count against the stroke you felt it on: if");
                        sb.AppendLine(
                            "     it is early, the trigger is the stroke STARTING rather than its");
                        sb.AppendLine(
                            "     length; if it is deep, the cost grows with the mark.");
                    }
                    var wRest = w.TotalMs - w.DescribeMs - w.ComposeMs - w.HandoffMs;
                    sb.AppendLine(
                        $"    describing it         {w.DescribeMs,8:0.##} ms   ({w.DescribeMs / w.TotalMs * 100:0}%)"
                        + $"   frame-cache misses in it: {w.Misses}");
                    sb.AppendLine(
                        $"    compositing it        {w.ComposeMs,8:0.##} ms   ({w.ComposeMs / w.TotalMs * 100:0}%)");
                    sb.AppendLine(
                        $"    handing it over       {w.HandoffMs,8:0.##} ms   ({w.HandoffMs / w.TotalMs * 100:0}%)");
                    sb.AppendLine(
                        $"    everything else       {wRest,8:0.##} ms   ({wRest / w.TotalMs * 100:0}%)");

                    // The verdict is written as a test of B332 rather than a
                    // description, so a capture can refute it.
                    var describeShare = w.DescribeMs / Math.Max(1e-9, w.TotalMs);
                    if (w.Misses > 0 && describeShare > 0.5)
                    {
                        sb.AppendLine(
                            "  >> B332 CONFIRMED by this capture: the worst build spent its time");
                        sb.AppendLine(
                            "     DESCRIBING the frame and a frame-cache miss happened inside it.");
                        sb.AppendLine(
                            "     FrameBitmapCache.Get renders a missed frame synchronously on the");
                        sb.AppendLine(
                            "     calling thread, which here is the UI thread mid-stroke. That is the");
                        sb.AppendLine(
                            "     jump. RenderDetached already exists to do it off-thread.");
                    }
                    else if (w.Misses == 0 && describeShare > 0.5)
                    {
                        sb.AppendLine(
                            "  >> B332 is HALF right: the stall is in describing the frame, but no");
                        sb.AppendLine(
                            "     cache miss happened inside it — so it is the pass list, the stack");
                        sb.AppendLine(
                            "     fold or a cel fetch that did not miss. Look there, not at the cache.");
                    }
                    else
                    {
                        sb.AppendLine(
                            "  >> B332 is REFUTED by this capture: the worst build did not spend its");
                        sb.AppendLine(
                            "     time describing the frame. Read the split above and chase the phase");
                        sb.AppendLine(
                            "     it actually names. Do not fix the cache on the strength of a mean.");
                    }
                }
                var describe = ph.DescribeMs / Math.Max(1e-9, comp.TotalMs);
                var compose = ph.ComposeMs / Math.Max(1e-9, comp.TotalMs);
                var other = rest / Math.Max(1e-9, comp.TotalMs);
                // A share is only worth a verdict when the whole is worth
                // attacking. The first version of this judged on share alone
                // and printed "the CPU composite is 83% of the build — that is
                // B125 stage 6's ground" about a build of 3.49 ms: it was
                // recommending an architectural project to win 2.88 ms. A
                // phase can be almost all of something negligible.
                // Judged on the MEDIAN: whether the build is worth attacking is a
                // question about the typical frame, and the mean answers a
                // different one whenever the session contains a stall.
                var typical = comp.MedianMs > 0 ? comp.MedianMs : comp.TotalMs / comp.Count;
                if (typical < MaterialBuildMs)
                {
                    sb.AppendLine(
                        $"  >> A typical build is {typical:0.##} ms, so none of this is worth");
                    sb.AppendLine(
                        "     attacking however the share falls. The time is elsewhere — read");
                    sb.AppendLine(
                        "     publish -> drawn and the dam below.");
                }
                else if (other >= 0.5)
                {
                    sb.AppendLine(
                        $"  >> {other * 100:0}% of the build is in NEITHER describing, compositing nor");
                    sb.AppendLine(
                        "     handing over. Something between those steps is the cost, and no");
                    sb.AppendLine(
                        "     fix aimed at any of the three would touch it. Split it further");
                    sb.AppendLine(
                        "     before acting — the phases here exist because the single number");
                    sb.AppendLine(
                        "     above it stopped naming its own cause.");
                }
                else
                if (compose >= 0.5)
                {
                    sb.AppendLine(
                        $"  >> The CPU composite is {compose * 100:0}% of the build, and it runs on the");
                    sb.AppendLine(
                        "     UI thread — the same thread the pen is delivering into. That is");
                    sb.AppendLine(
                        "     B125 stage 6's ground: the ring route never reaches the card.");
                }
                else if (describe >= 0.5)
                {
                    sb.AppendLine(
                        $"  >> Describing the frame is {describe * 100:0}% of the build, so the cost is in");
                    sb.AppendLine(
                        "     the pass list, the stack fold or the cel fetches rather than in any");
                    sb.AppendLine(
                        "     blending. Moving the composite would not move this.");
                }
                else
                {
                    sb.AppendLine(
                        "  >> No single phase of the build dominates, so the build is broadly");
                    sb.AppendLine(
                        "     costly rather than blocked on one step. Read the three above.");
                }
            }
        }

        sb.AppendLine($"  stamping the dabs       median {s.Stamp.MedianMs,7:0.##} ms   mean {s.Stamp.MeanMs,7:0.##} ms   worst {s.Stamp.WorstMs,7:0.##} ms");
        // **B189: what an event's stamp is MADE of.** The dab count is bounded —
        // B321 pinned the provisional tail at 2.0 events at every pen speed — but
        // an event also performs three bitmap copies over the tail RECTANGLE,
        // which spans the distance the pen covered in those two events. That is
        // area work that grows with speed while the dab count does not, and it
        // is invisible to every dab-based number above.
        if (facts.StampParts is { } sp)
        {
            var whole = sp.RestoreMs + sp.SettledMs + sp.BackupMs + sp.TailMs;
            sb.AppendLine(
                $"    restoring the tail    median {sp.RestoreMs,7:0.##} ms   (copy back what was on loan)");
            sb.AppendLine(
                $"    stamping the settled  median {sp.SettledMs,7:0.##} ms   (dabs that stopped moving)");
            sb.AppendLine(
                $"    backing up the tail   median {sp.BackupMs,7:0.##} ms   (copy out, for the next event)");
            sb.AppendLine(
                $"    stamping the tail     median {sp.TailMs,7:0.##} ms   (the dabs on loan)");
            sb.AppendLine(
                $"    the tail rectangle    median {sp.TailMpx,7:0.###} Mpx   p90 {sp.TailMpxP90,7:0.###} Mpx");
            // **Every dab is walked twice.** Colour into the scratch, footprint
            // into the coverage buffer, both document-sized. Split because the
            // per-dab figure above has always included both.
            sb.AppendLine(
                $"    of which colour       median {sp.ColourMs,7:0.##} ms   and footprint {sp.FootprintMs,7:0.##} ms"
                + $"   (the same dabs, walked twice)");
            sb.AppendLine(
                $"    the footprint went    into {Rendering.LiveFootprintScale.Describe(sp.FootprintScale)}");
            var twoWalks = sp.ColourMs + sp.FootprintMs;
            if (twoWalks > 0 && sp.FootprintMs > 0)
            {
                var share = sp.FootprintMs / twoWalks * 100;
                sb.AppendLine(
                    $"  >> The footprint is {share:0}% of the dab work. It is a running maximum kept");
                sb.AppendLine(
                    share > 35
                        ? "     so a soft brush can be capped to it, and at this share it is the"
                        : "     so a soft brush can be capped to it, and at this share it is NOT");
                sb.AppendLine(
                    share > 35
                        ? "     lever for B189 rather than the brush or the canvas size."
                        : "     where B189's cost is. Look at the colour stamp instead.");
            }
            if (whole > 0)
            {
                var copies = (sp.RestoreMs + sp.BackupMs) / whole * 100;
                sb.AppendLine(
                    $"  >> {copies:0}% of an event's stamp is COPYING the tail rectangle, not stamping dabs.");
                sb.AppendLine(copies > 40
                    ? "     That cost is an AREA and the rectangle spans two events of pen travel,"
                    : "     The dabs are the cost here, so the tail copies are not the lever.");
                if (copies > 40)
                {
                    sb.AppendLine(
                        "     so it grows with SPEED while the dab count does not. That is why a");
                    sb.AppendLine(
                        "     long fast stroke stalls and a short one does not, and no dab-based");
                    sb.AppendLine(
                        "     number in this file can see it. B189.");
                }
            }
        }
        // **What that one number is made of** (B322 attempt 6). A mean cannot
        // show that a cost is proportional to something, which is exactly the
        // blindness that let the fourth attempt restamp the whole stroke per
        // publish with every test green. The settled half is stamped once; the
        // provisional half is re-stamped every event, and the comment beside it
        // in the paint path has suspected since it was written that the tail
        // grows with pen speed without anyone measuring it.
        if (facts.StampShape is { Events: > 8 } shape)
        {
            // What this shape does not cover, said before it is read.
            if (shape.WholeMarkEvents > 0)
            {
                var total = shape.Events + shape.WholeMarkEvents;
                sb.AppendLine(
                    $"    (the split below covers {shape.Events} of {total} events — {shape.WholeMarkEvents} took the");
                sb.AppendLine(
                    "     whole-mark route, which stamps its silhouette in one piece and has");
                sb.AppendLine(
                    "     no settled/provisional split to report)");
            }

            sb.AppendLine(
                $"    settled per event     median {shape.SettledMedian,7:0.#}   p90 {shape.SettledP90,7:0.#}   dabs (stamped once)");
            sb.AppendLine(
                $"    provisional per event median {shape.ProvisionalMedian,7:0.#}   p90 {shape.ProvisionalP90,7:0.#}"
                + $"   worst {shape.ProvisionalWorst,7:0.#}   dabs (re-stamped EVERY event)");

            var perEvent = shape.SettledMedian + shape.ProvisionalMedian;
            if (perEvent > 0)
            {
                // **Median over median, which B330 made possible.** This line
                // used to refuse to divide at all, because the only figure the
                // stamp carried was a mean and a mean over a median drags every
                // stall into a number printed as precision. It has a median now,
                // so the division is between like statistics — with the caveat
                // below, which is real and not a formality.
                sb.AppendLine(
                    $"  >> A typical event stamps {perEvent:0.#} dabs at {s.Stamp.MedianMs:0.##} ms,"
                    + $" so about {s.Stamp.MedianMs / perEvent * 1000:0.#} us a dab.");
                sb.AppendLine(
                    "     A ratio of medians is not the median of the ratio: it is the right");
                sb.AppendLine(
                    "     order of magnitude for sizing a budget and the wrong thing to quote");
                sb.AppendLine(
                    "     as a per-dab cost to three figures.");
            }

            // **The tail is judged against the event's OWN dabs, not against its
            // own spread** — and the first version of this got that wrong. It
            // compared the provisional median with the provisional p90, saw 5.6x,
            // and concluded the stamp was unbounded. Both numbers rise together
            // on a fast stroke because a fast event simply contains more dabs;
            // the question is whether the tail is growing *relative to* the work,
            // which is the ratio below. B321 had already settled this — the tail
            // is 2.0 events at every pen speed (`ProvisionalTailTests`) and its
            // large dab counts "are just what a fast stroke is" — and the wrong
            // verdict nearly sent a session to re-derive a ruled-out result.
            if (shape.SettledMedian > 0 && shape.ProvisionalMedian > 0)
            {
                var atMedian = shape.ProvisionalMedian / shape.SettledMedian;
                var atP90 = shape.SettledP90 > 0 ? shape.ProvisionalP90 / shape.SettledP90 : atMedian;
                sb.AppendLine(
                    $"    tail per settled dab  {atMedian:0.##}x at the median, {atP90:0.##}x at the p90");

                if (atP90 > atMedian * 1.5)
                {
                    sb.AppendLine(
                        "  >> The tail grows RELATIVE to the event's own dabs on a fast stroke, so");
                    sb.AppendLine(
                        "     the lending policy is the cost and not merely the dab count. That is");
                    sb.AppendLine(
                        "     a real regression against ProvisionalTailTests — check it first.");
                }
                else
                {
                    sb.AppendLine(
                        "  >> The tail holds its ratio to the event's own dabs, so it is behaving");
                    sb.AppendLine(
                        "     exactly as designed and the stamp is bounded by what the event");
                    sb.AppendLine(
                        "     contains. A fast event holds more dabs; that is what a fast stroke");
                    sb.AppendLine(
                        "     IS, and it is ruled out as a fault in B321. Do not re-derive it.");
                }
            }
        }
        var perPublish = s.Publishes == 0 ? 0 : (double)s.Events / s.Publishes;
        sb.AppendLine($"publishes carrying ink    {s.Publishes}  ({perPublish:0.#} events per publish)");
        if (facts.Dam is { Deferrals: > 0 } dam)
        {
            // B328: over the holds that were TIMED, not over some subset of the
            // ways a deferral can end. Divided by `ByPresent + ByTimer` this
            // read 67.3 ms beside a worst of 47.16 — an impossibility that sat
            // in three captures before anyone noticed it was one.
            var held = dam.HoldsTimed == 0 ? 0 : dam.HeldTotalMs / dam.HoldsTimed;
            sb.AppendLine(
                $"  publish held back       {dam.Deferrals} times   mean {held:0.##} ms   worst {dam.HeldWorstMs:0.##} ms");
            // **The impossibility, said out loud** (B328). An average above the
            // largest single sample cannot happen, so if it is printed the
            // accounting is broken and every conclusion drawn from the pair is
            // void. It sat in three captures unremarked and was quoted as a
            // finding before anyone noticed it could not be true. A reader
            // should not have to do this subtraction themselves.
            if (dam.HoldsTimed > 0 && held > dam.HeldWorstMs)
            {
                sb.AppendLine("  !! that mean is ABOVE the worst, which is impossible — the total");
                sb.AppendLine("     and the count it is divided by have drifted apart. Treat both");
                sb.AppendLine("     as void and fix the accounting before reading anything into");
                sb.AppendLine("     them. See B328.");
            }
            sb.AppendLine(
                $"    released by the screen  {dam.ByPresent}      by the 250 ms backstop  {dam.ByTimer}"
                + (dam.ByEvent > 0 ? $"      by a pointer event asking  {dam.ByEvent}" : ""));
            // The half of a deferral that is not pacing. Waiting for a canvas
            // that has not drawn yet is the dam doing its job; waiting after it
            // has drawn is overhead, and the two are not separable from the
            // hold alone — which is why the hold sat at 54.98 ms beside a
            // `publish -> drawn` of 30.67 with nothing to say about the gap.
            if (dam.Deferrals > 0 && dam.LateTotalMs > 0)
            {
                var late = dam.LateTotalMs / dam.Deferrals;
                var share = dam.LateTotalMs / Math.Max(1e-9, dam.HeldTotalMs);
                sb.AppendLine(
                    $"    already drawn, still held  mean {late,7:0.##} ms   worst {dam.LateWorstMs,7:0.##} ms   ({share * 100:0}% of the hold)");
                if (share >= 0.25)
                {
                    sb.AppendLine(
                        "  >> That share is not pacing, it is the dam finding out late. The");
                    sb.AppendLine(
                        "     frame was on screen and the publish went on waiting — either the");
                    sb.AppendLine(
                        "     release notification is queued behind the artist's own pointer");
                    sb.AppendLine(
                        "     events, or nothing asked again until the next one arrived.");
                }
            }
            if (dam.ByTimer > dam.ByPresent)
            {
                sb.AppendLine("  !! the BACKSTOP is pacing the canvas, not the screen. That timer");
                sb.AppendLine("     exists for a window that has stopped presenting at all, so a");
                sb.AppendLine("     drawing canvas reaching it means the present notification is");
                sb.AppendLine("     not arriving or not matching the frame it waited on — and the");
                sb.AppendLine("     update rate is then a constant nobody chose as one.");
            }
        }
        if (s.Publishes > 0)
        {
            if (facts.Cycle is { Cycles: > 4 } cyc)
            {
                // The whole loop, so every part above is a share of something
                // rather than a number on its own. Median, because the cycle is
                // a latency distribution and one stall moves its mean without
                // moving any cycle that actually happened.
                sb.AppendLine(
                    $"  publish -> publish      median {cyc.CycleMedianMs,7:0.##} ms   mean {cyc.CycleMeanMs,7:0.##} ms   ({cyc.Cycles} cycles — the whole loop)");
                sb.AppendLine(
                    $"    frames allowed in flight  {ViewModels.PublishState.DefaultInFlightDepth}   (LIGHTBOX_INFLIGHT overrides; 2 is the default)");
                sb.AppendLine(
                    $"    dam let go -> publish  median {cyc.ReleaseToPublishMedianMs,7:0.##} ms   mean {cyc.ReleaseToPublishMeanMs,7:0.##} ms");
                if (cyc.Events > 4)
                {
                    sb.AppendLine(
                        $"    the pen delivers every  median {cyc.EventIntervalMedianMs,7:0.##} ms   ({cyc.Events} intervals, pauses excluded)");
                }
                if (cyc.CycleMedianMs > 0 && cyc.EventIntervalMedianMs > 0)
                {
                    var eventsPerCycle = cyc.CycleMedianMs / cyc.EventIntervalMedianMs;
                    sb.AppendLine(
                        $"  >> A cycle is {eventsPerCycle:0.#} pen events long. THAT is the chunkiness — how");
                    sb.AppendLine(
                        "     much ink arrives at once — and it is set by the cycle rather than by");
                    sb.AppendLine(
                        "     any of the latencies above it.");
                }
            }
            sb.AppendLine($"  event -> publish        median {s.WaitToPublish.MedianMs,7:0.##} ms   mean {s.WaitToPublish.MeanMs,7:0.##} ms   worst {s.WaitToPublish.WorstMs,7:0.##} ms");
            sb.AppendLine($"    newest event          median {s.TipToPublish.MedianMs,7:0.##} ms   mean {s.TipToPublish.MeanMs,7:0.##} ms   worst {s.TipToPublish.WorstMs,7:0.##} ms");
        }
        // B322: how often the newest dabs reached the screen, and how far behind
        // the pass was when they did not. **The budget is a guess until this line
        // is read** — nothing recorded how many dabs are typically outstanding,
        // and the fourth attempt's "about nine events" assumption survived all
        // the way to a person's machine precisely because no capture could
        // contradict it.
        // **Printed even when it is all zeroes**, which is the correction the
        // first real capture forced. A missing line is produced equally by "the
        // tip was never drawn" and "this brush never needed one", and the owner
        // had drawn with a brush that takes no post-process pass at all. The
        // report said nothing and the silence was read as a result.
        if (facts.LiveTip is { } tip)
        {
            var considered = tip.Drawn + tip.TooFarBehind;
            if (considered == 0)
            {
                sb.AppendLine(
                    $"live tip                  not applicable — no post-process pass ran in {tip.NoPass} publishes");
                sb.AppendLine(
                    "  This brush has no live effect, so every dab was already on screen and");
                sb.AppendLine(
                    "  B322 cannot arise. To exercise it, draw with granulation or a wet edge.");
            }
            else
            {
                sb.AppendLine(
                    $"live tip drawn            {tip.Drawn} of {considered}   too far behind {tip.TooFarBehind}"
                    + $"   (budget {Rendering.LiveTipPlan.MaxMs} ms a publish)");
                // **Which arm ran.** Two builds that differ only in the tip's
                // resolution produce reports that are otherwise identical, and a
                // capture that cannot say which one it is describes neither.
                sb.AppendLine(
                    $"  stamped at              {Rendering.LiveTipScale.Describe(tip.TipScale)}");
                sb.AppendLine(
                    $"  dabs outstanding        median {tip.OutstandingMedian,7:0.#}   p90 {tip.OutstandingP90,7:0.#}"
                    + $"   p99 {tip.OutstandingP99,7:0.#}   worst {tip.OutstandingWorst,7:0.#}");
                // What the budget is protecting, timed. Without this the only
                // way to choose it is caution, and caution set it to a value
                // that refused the strokes the fix exists for.
                sb.AppendLine(
                    $"  restamping the tip cost median {tip.StampMedianMs,7:0.##} ms   worst {tip.StampWorstMs,7:0.##} ms");
                if (tip.StampMedianMs > 0 && tip.OutstandingMedian > 0)
                {
                    // **Divided by what was STAMPED, not by what was outstanding.**
                    // Those were the same number while the tip was rebuilt every
                    // publish; attempt 6 stamps a fraction of the outstanding run,
                    // and leaving the old divisor in place overstated the per-dab
                    // cost by exactly the saving — 27.9 us reported against 91.5
                    // actual, on a capture where the saving was 3.3x.
                    // The MARGINAL cost, measured over the stamp alone. The
                    // average over the whole operation carries the fixed setup,
                    // and dividing that by a small dab count reported 58 us a
                    // dab on a brush whose dabs cost 5.45 — which bought a
                    // budget of 51 dabs and refused half the publishes.
                    var perDab = tip.MarginalMs;
                    sb.AppendLine(
                        $"  >> About {perDab * 1000:0.##} us a dab (marginal), so {Rendering.LiveTipPlan.MaxMs} ms"
                        + $" allows {Rendering.LiveTipPlan.Allowance(perDab)} dabs a publish.");
                    sb.AppendLine(
                        "     Set the budget against that, not against caution: refusing a publish");
                    sb.AppendLine(
                        "     turns the fix off during exactly the fast strokes it exists for.");

                    // B322 attempt 6: what the same tip would cost if it kept
                    // what it had instead of being rebuilt every publish.
                    sb.AppendLine(
                        $"  new dabs per publish    median {tip.NewDabsMedian,7:0.#}   p90 {tip.NewDabsP90,7:0.#}"
                        + $"   worst {tip.NewDabsWorst,7:0.#}");
                    if (tip.NewDabsP90 > 0)
                    {
                        var rebuilt = perDab * tip.OutstandingP90;
                        var accumulated = perDab * tip.NewDabsP90;
                        sb.AppendLine(
                            $"  >> At the p90 a REBUILT tip would stamp {tip.OutstandingP90:0} dabs ({rebuilt:0.##} ms);"
                            + $" an accumulated one");
                        sb.AppendLine(
                            $"     {tip.NewDabsP90:0} ({accumulated:0.##} ms) — the prediction attempt 6 was built on.");

                        // **What it actually did**, which that prediction could not say: it
                        // assumed every publish was an addition and ignored the rebuilds a
                        // completed pass forces. The saving is entirely in their ratio.
                        var decisions = tip.Added + tip.Rebuilt;
                        if (decisions > 0)
                        {
                            sb.AppendLine(
                                $"  added to the tip          {tip.Added} of {decisions}   rebuilt {tip.Rebuilt}"
                                + $"   (a completed pass forces a rebuild)");
                            sb.AppendLine(
                                $"    dabs stamped            adding median {tip.DabsAddedMedian,6:0.#}"
                                + $"   rebuilding median {tip.DabsRebuiltMedian,6:0.#}");
                            // **Judged by what each path COSTS, not by how often it runs.**
                            // The first version compared the counts, saw 53 rebuilds against
                            // 47 additions, and announced "attempt 6 has not paid" on a
                            // capture where it stamped 17.6 dabs a publish against the 57.5
                            // a rebuilt tip would have — a 3.3x saving reported as a failure.
                            // A rebuild is cheap precisely because it happens when the pass
                            // has just reset the outstanding run.
                            if (tip.DabsStampedMedian > 0 && tip.OutstandingMedian > 0)
                            {
                                var saving = tip.OutstandingMedian / tip.DabsStampedMedian;
                                sb.AppendLine(
                                    $"    dabs stamped a publish  {tip.DabsStampedMedian,6:0.#} against {tip.OutstandingMedian,6:0.#}"
                                    + $" outstanding — {saving:0.#}x");
                                if (saving < 1.2)
                                {
                                    sb.AppendLine("  >> The tip is stamping about as much as a rebuilt one would,");
                                    sb.AppendLine("     so keeping it between publishes has bought nothing here.");
                                }
                                else
                                {
                                    sb.AppendLine(
                                        $"  >> Keeping the tip saves {saving:0.#}x the stamping a rebuild would cost,");
                                    sb.AppendLine("     whichever path a publish takes. A rebuild is cheap because it");
                                    sb.AppendLine("     happens exactly when the pass has just reset the outstanding run.");
                                }
                            }
                            else
                            {
                                sb.AppendLine(
                                    $"  >> {tip.Added * 100.0 / decisions:0}% of publishes only added, so the tip survives");
                                sb.AppendLine("     between passes and the saving is real. The gap between the two");
                                sb.AppendLine("     medians above is what attempt 6 bought.");
                            }
                        }
                    }
                }
            }

            if (considered > 8 && tip.TooFarBehind > tip.Drawn)
            {
                sb.AppendLine("  >> The budget refused MORE publishes than it served, so the tip is");
                sb.AppendLine("     mostly not being drawn and B322 is only half fixed here. Raise it");
                sb.AppendLine("     only if the median above is close to it — if the median is many");
                sb.AppendLine("     times the budget then the pass is not keeping up at all, and no");
                sb.AppendLine("     tip size fixes that.");
            }
        }

        if (s.Drawn > 0)
        {
            sb.AppendLine($"  publish -> drawn        median {s.WaitToDraw.MedianMs,7:0.##} ms   mean {s.WaitToDraw.MeanMs,7:0.##} ms   worst {s.WaitToDraw.WorstMs,7:0.##} ms");
            sb.AppendLine($"  PEN -> SCREEN           median {s.PenToScreen.MedianMs,7:0.##} ms   mean {s.PenToScreen.MeanMs,7:0.##} ms   worst {s.PenToScreen.WorstMs,7:0.##} ms"
                          + $"   ({s.Drawn} drawn, {s.Superseded} replaced first)");
            sb.AppendLine($"  TIP -> SCREEN           median {s.TipToScreen.MedianMs,7:0.##} ms   mean {s.TipToScreen.MeanMs,7:0.##} ms   worst {s.TipToScreen.WorstMs,7:0.##} ms");
            // B189's second capture is why these are two numbers: the oldest
            // anchor grew from 4.7 to 11.4 events of coalescing when the
            // publish pacing landed, and read as MORE lag while the tip's was
            // falling. The gap between the two lines is coalescing depth; the
            // TIP line alone is how far the freshest ink runs behind the hand.
            var gap = s.PenToScreen.MeanMs - s.TipToScreen.MeanMs;
            if (gap > s.TipToScreen.MeanMs && s.TipToScreen.MeanMs > 0)
            {
                sb.AppendLine($"    ({gap:0.#} ms of the PEN line is coalescing depth, not staleness —");
                sb.AppendLine("     publishes are batching well rather than falling behind)");
            }
        }
        else
        {
            sb.AppendLine("  nothing drawn yet — the chain has no end to measure. Draw for longer.");
        }

        // The wet-media pass runs on a worker since B189's second capture
        // measured it blocking the UI thread for six seconds in one session —
        // so its cost no longer adds to the chain above, but a slow pass still
        // shows: the true wet look settles that far behind the pen tip. Named
        // even at zero: a capture that never exercised a wet brush should say
        // so, or its clean bill covers only dry ones.
        if (facts.LivePost is { Passes: > 0 } wet)
        {
            var mean = wet.TotalMs / wet.Passes;
            sb.AppendLine($"live medium passes        {wet.Passes}   mean {mean:0.##} ms   worst {wet.WorstMs:0.##} ms   (wet brushes only, OFF the UI thread)");

            // B313's follow-up: a pass being cheap is only half the story. It
            // has to actually RUN, and it has to be reading a band rather than
            // the whole mark. These two separate the faults — a long wait with
            // a small rect is the pass being starved at Background priority
            // (B312's lesson in another file), a short wait with a mark-sized
            // rect is the band never engaging.
            if (wet.Waits > 0)
            {
                var waitMean = wet.WaitTotalMs / wet.Waits;
                sb.AppendLine(
                    $"  queued -> started       mean {waitMean:0.##} ms   worst {wet.WaitWorstMs:0.##} ms   over {wet.Waits} passes");
                if (waitMean > 20)
                {
                    // The text this replaces said the pass was "posted at
                    // Background priority" -- true when written and false since
                    // B314 moved it to Input, which is worse than saying
                    // nothing: a reader would chase a fix that already landed.
                    // A long wait now means a busy dispatcher, not starvation.
                    // B331: the band at both ends of that wait, which is the pair
                // that says which way the loop runs. Printed next to the wait
                // because the two are only meaningful together.
                sb.AppendLine("  !! a pass spends longer waiting to start than running. Since");
                    sb.AppendLine("     B314 it is posted at Input priority, so this is not");
                    sb.AppendLine("     starvation — it is the UI thread being busy with something");
                    sb.AppendLine("     else when the pass comes due.");
                }
            }

            if (wet.MarkPixels > 0)
            {
                var share = 100.0 * wet.Pixels / wet.MarkPixels;
                sb.AppendLine(
                    $"  of the mark re-processed  {share:0.#}%   (band-local passes read only what moved)");
                // **B331: the band at both ends of the wait.** `PostPending` grows
                // until a pass consumes it, and the pass waits behind the artist's
                // own events to be dispatched. Whether the band is large because the
                // pass was late, or the pass was late because the band was large, is
                // the question B331 refuses to answer by inference — these two say it.
                // **One sample is enough, and requiring more was a mistake.** This
                // asked for five before it would print, and a starved pass — the
                // whole subject of B331 — produces FEW passes by definition. The
                // owner's capture of 14:58 had four, so the measurement built to
                // diagnose the pathology stayed silent in the worst example of it
                // anyone had produced. A threshold that hides the case of interest
                // is not caution.
                if (facts.StampShape is { Bands: > 0 } bands && bands.BandAtQueue >= 0)
                {
                    var grew = bands.BandAtQueue > 0 ? bands.BandAtStart / bands.BandAtQueue : 0;
                    sb.AppendLine(
                        $"  the band when queued    {bands.BandAtQueue / 1e6,7:0.##} Mpx"
                        + $"   when it started {bands.BandAtStart / 1e6,7:0.##} Mpx   ({grew:0.#}x)");
                    if (bands.Bands < 5)
                    {
                        sb.AppendLine(
                            $"     (only {bands.Bands} pass(es) — few passes is itself the symptom, so this");
                        sb.AppendLine(
                            "      is reported rather than withheld; read it as a direction, not a rate)");
                    }

                    if (grew >= 2)
                    {
                        sb.AppendLine("  >> The band GREW while the pass waited to start, so the wait is");
                        sb.AppendLine("     what makes it large and the pass being slow is the consequence.");
                        sb.AppendLine("     Dispatch it sooner, or stop the band accruing while it waits —");
                        sb.AppendLine("     making the pass itself faster treats the wrong end.");
                    }
                    else if (share >= 25)
                    {
                        sb.AppendLine("  >> The band was already this large when the pass was asked for, so");
                        sb.AppendLine("     the wait is not what made it big. Whatever is dirtying that much");
                        sb.AppendLine("     of the mark per event is the cause, and the pass is its victim.");
                    }
                    else
                    {
                        // **Says nothing alarming about a healthy capture**, which
                        // the first version did: it announced that something was
                        // dirtying "that much of the mark" over a band of 2.2%.
                        // A verdict that fires whatever the numbers say is not a
                        // verdict, and this section has had three of those.
                        sb.AppendLine("  >> The band is small and did not grow while the pass waited, so");
                        sb.AppendLine("     nothing here is wrong. B331's pathology is a band that reaches");
                        sb.AppendLine("     most of the mark; this capture is not showing it.");
                    }
                }
                if (share > 50)
                {
                    sb.AppendLine("  !! the pass is reading most of the mark every time, so it grows");
                    sb.AppendLine("     with the stroke. Either the band is not engaging or something");
                    sb.AppendLine("     is forcing whole-mark passes — a brush setting changing");
                    sb.AppendLine("     mid-stroke does that deliberately.");
                }
            }

            // The slowest pass's own geometry. A mean band beside a worst cost
            // cannot say whether the expensive pass was expensive because it
            // was big — these two lines can, and they disagree loudly when the
            // cost is somewhere other than the area.
            if (wet.WorstW > 0 && wet.WorstMarkPixels > 0)
            {
                var worstArea = (long)wet.WorstW * wet.WorstH;
                var worstShare = 100.0 * worstArea / wet.WorstMarkPixels;
                sb.AppendLine(
                    $"  the slowest pass ran over {wet.WorstW}x{wet.WorstH} px — {worstShare:0.#}% of the mark");
                if (worstShare < 10 && wet.WorstMs > 100)
                {
                    sb.AppendLine("  !! that pass was slow WITHOUT being big, so its cost is not the");
                    sb.AppendLine("     area it covered. Look at what the pass does per pixel, or at");
                    sb.AppendLine("     what the worker was competing with for a core.");
                }
            }

            if (wet.WorstTail > 0)
            {
                sb.AppendLine(
                    $"  longest provisional tail  {wet.WorstTail} dabs re-stamped per event");
            }

            if (mean > 33)
            {
                sb.AppendLine("  !! a pass this slow no longer blocks input, but the simulated look");
                sb.AppendLine("     — the rim, the pooling — settles this far behind the pen tip,");
                sb.AppendLine("     showing plain dabs until it lands. This cost is the medium");
                sb.AppendLine("     simulation — smaller brush, smaller canvas, or a preset without");
                sb.AppendLine("     a simulated medium to confirm the difference.");
            }
        }
        else
        {
            sb.AppendLine("live medium passes        none — no wet-media brush was used in this capture,");
            sb.AppendLine("  so these numbers only speak for dry brushes.");
        }

        if (s.Drawn > 0)
        {
            sb.AppendLine();
            // The same guard PresentLatency's split learned the hard way: a
            // verdict off a handful of frames reads exactly like one that
            // means something.
            const int Enough = 12;
            if (s.Drawn < Enough)
            {
                sb.AppendLine($"  >> Too few drawn frames to mean anything (under {Enough}). One stall");
                sb.AppendLine("     moves a mean this small by tens of milliseconds — draw a few long");
                sb.AppendLine("     strokes and write the report again.");
            }
            else if (s.PenToScreen.MeanMs < 20)
            {
                sb.AppendLine("  >> The pipeline is keeping up: ink reaches the screen about a frame");
                sb.AppendLine("     after the event reaches the app. If drawing still feels laggy, the");
                sb.AppendLine("     time is being spent BEFORE the event arrives — tablet driver, OS");
                sb.AppendLine("     queue, display latency — or the lag is unevenness rather than");
                sb.AppendLine("     delay, which these means cannot show but the worsts hint at.");
            }
            else
            {
                var stamp = s.Stamp.MeanMs;
                var toPublish = Math.Max(0, s.WaitToPublish.MeanMs - stamp);
                var toDraw = s.WaitToDraw.MeanMs;
                if (toDraw >= toPublish && toDraw >= stamp)
                {
                    sb.AppendLine("  >> The wait is AFTER the publish: frames carrying fresh ink sit in");
                    sb.AppendLine("     the present queue before anything draws them. That is B178's");
                    sb.AppendLine("     shape showing up under drawing, and the fix is the present path —");
                    sb.AppendLine("     not the brush engine, whose stamp cost is above and small.");
                }
                else if (stamp >= toPublish)
                {
                    sb.AppendLine("  >> The stamp itself is the fat segment, so this IS the cost of the");
                    sb.AppendLine("     mark — brush size, medium, or canvas size. Compare a plain Soft");
                    sb.AppendLine("     Round on a small canvas: if that collapses the number, the cost");
                    sb.AppendLine("     scales with the brush and the wet-media line above says how much.");
                }
                else
                {
                    sb.AppendLine("  >> The wait is BETWEEN stamp and publish: the snapshot is queueing");
                    sb.AppendLine("     behind other dispatcher work. The stamp is cheap and the draw is");
                    sb.AppendLine("     prompt, so look at what else runs on the UI thread mid-stroke.");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("  Measured from the event REACHING the app. Queueing in the OS or the");
        sb.AppendLine("  tablet driver before that is invisible here, so the true pen-to-screen");
        sb.AppendLine("  is this number plus a floor nothing in this file can see.");
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
            // **B332: where the misses come from.** A miss renders the whole
            // frame synchronously on the calling thread — 797 ms measured on a
            // lighter frame than the owner's, and 100% of a 3.3 second stall in
            // the capture that confirmed it. So the question is not how many
            // misses there are but what CAUSED them, and the leading candidate
            // is a committed stroke the incremental repaint could not patch.
            if (facts.FrameEdits is { } edits)
            {
                var edited = edits.Repaired + edits.Dropped;
                sb.AppendLine(
                    $"  edits repaired in place {edits.Repaired} of {edited}   dropped whole {edits.Dropped}"
                    + $"   (a drop is what makes the next lookup miss)");
                if (edits.Dropped > 0)
                {
                    sb.AppendLine(
                        "  >> Every DROP costs a full frame render on the UI thread the next time");
                    sb.AppendLine(
                        "     anything asks for this frame — and the next publish always does, so");
                    sb.AppendLine(
                        "     there is no window to warm it in. Widening the repaint (B327) is the");
                    if (facts.FrameDropCallers is { Count: > 0 } who)
                    {
                        sb.AppendLine($"     dropped by: {string.Join(", ", who)}");
                    }

                    sb.AppendLine(
                        "     fix that changes nothing visible; warming after the fact is not.");
                }
                else if (edited > 0)
                {
                    sb.AppendLine(
                        "  >> Every edit was patched in place, so the misses above came from");
                    sb.AppendLine(
                        "     somewhere else. B332's drop path is NOT the cause here.");
                }
                else
                {
                    sb.AppendLine(
                        "  >> NO edit went through the invalidate path at all this session, so");
                    sb.AppendLine(
                        "     committing a stroke is not what caused the misses. Printed at zero");
                    sb.AppendLine(
                        "     on purpose: a missing line is produced equally by \"nothing happened\"");
                    sb.AppendLine(
                        "     and \"the counter is not wired\", and a capture cannot tell them apart.");
                }

                // What the misses actually WERE. Five misses and a stall is a
                // fact about cost; this is the fact about cause, and the three
                // causes need three different fixes.
                if (facts.FrameCacheMisses is { Count: > 0 } misses)
                {
                    sb.AppendLine($"  the last {misses.Count} misses, and what each was for:");
                    foreach (var m in misses)
                    {
                        sb.AppendLine(
                            $"    frame {m.FrameId[..Math.Min(8, m.FrameId.Length)]}"
                            + $"  {m.Width}x{m.Height}@{m.Scale:0.###}  cel {m.Cel}   {m.Why}");
                    }

                    var sizes = misses.Select(m => $"{m.Width}x{m.Height}@{m.Scale:0.###}").Distinct().Count();
                    if (sizes > 1)
                    {
                        sb.AppendLine(
                            $"  >> {sizes} DIFFERENT sizes or scales among them, so the cache is being");
                        sb.AppendLine(
                            "     asked for the same drawing at keys it does not hold. Each new key is");
                        sb.AppendLine(
                            "     a full render on the calling thread. That is the fix to chase.");
                    }
                }
            }
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

    /// <summary>
    /// Below this, the frame build is not where a latency problem lives and the
    /// report says so rather than naming whichever phase happens to be biggest.
    /// </summary>
    /// <remarks>
    /// Five milliseconds is about a third of a 60 Hz frame: small enough that
    /// removing all of it could not change how drawing feels, which is the
    /// question this section is being read to answer.
    /// </remarks>
    private const double MaterialBuildMs = 5.0;

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
        // Which composition path the window ran under, so two reports taken to
        // compare them can be told apart without anybody having to remember
        // which one they set the variable for.
        sb.AppendLine($"composition               {Program.CompositionChoice}");
        // Was "compositing is on the CPU either way", which stopped being true
        // when B167 phase 4 put the tiled composite on the card. Derived from the
        // toggle rather than asserted, so it cannot go stale the same way twice.
        sb.AppendLine(facts.GpuCompositeOptedIn
            ? "  (the final blit — see the compositing line below for where blending happened)"
            : "  (this is the FINAL BLIT only — compositing is on the CPU, which is the default)");
        sb.AppendLine($"durable frame (B122)      {DurableFrameState(facts)}");
        // **A launch-time fact, printed at launch** (B322). The live-tip arm is
        // fixed for the process by an environment variable, and until this line
        // existed the only place it appeared was the live-tip block — which needs
        // a stroke on a brush with an effect before it says anything. Two
        // captures were taken as an A/B, both on the default arm, and nothing in
        // the file said so until after the drawing was done. A setting chosen
        // before the window opens belongs beside the other two that are.
        sb.AppendLine(
            $"live tip stamped at       {(Rendering.LiveTipScale.PreviewScale ? "preview resolution" : "document resolution")}"
            + $"   ({Rendering.LiveTipScale.Variable}"
            + $"{(Rendering.LiveTipScale.PreviewScale ? "=preview" : " unset — the default")})");
        // The same launch-time fact for the OTHER buffer that follows the
        // compose scale (B189). Default on, which is the opposite of the tip's
        // default, so the line says which rather than leaving it to be inferred
        // from the absence of a variable.
        sb.AppendLine(
            "live footprint at         "
            + (Rendering.LiveFootprintScale.FollowsPreview
                ? "preview resolution   (the default — set "
                    + Rendering.LiveFootprintScale.Variable + "=full to pin it)"
                : "document resolution   (" + Rendering.LiveFootprintScale.Variable + "=full)"));
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
        AppendPublishTally(sb, facts);
        AppendStrokeLatency(sb, facts);
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
        AppendComposeCache(sb);
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
