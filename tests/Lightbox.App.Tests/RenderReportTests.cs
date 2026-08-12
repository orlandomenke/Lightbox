using Lightbox.App.Rendering;
using Lightbox.App.Services;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// The render report: what it says, where it writes, and that it cannot break the
/// application.
/// </summary>
/// <remarks>
/// <para>
/// <b>The point of the feature is to measure what this suite cannot.</b> There is
/// no GPU context here, so nothing below proves anything about uploads — that is
/// the report's job, on the artist's machine. What is testable, and what these
/// cover, is everything around the measurement: the counters add up, the file
/// lands in the diagnostics folder, the probe returns a comparison rather than
/// throwing, and the one line that matters most — a silent fall back to CPU — is
/// actually reported rather than swallowed.
/// </para>
/// <para>
/// Written with <c>DiagnosticLog.DirectoryOverride</c>, the seam the crash log
/// already provides, so no test writes near a real installation.
/// </para>
/// </remarks>
public class RenderReportTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-render-report-" + Guid.NewGuid().ToString("N"));

    public RenderReportTests Setup()
    {
        Directory.CreateDirectory(_dir);
        DiagnosticLog.DirectoryOverride = _dir;
        RenderReport.ResetForTests();
        return this;
    }

    public void Dispose()
    {
        DiagnosticLog.DirectoryOverride = null;
        RenderReport.ResetForTests();
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp dir is not worth failing over */ }
    }

    // Internal rather than private: PresentWaitByInputTests builds the same
    // report to read one section of it, and a second copy of this list of
    // defaults would drift from this one on the first field added.
    internal static RenderReport.Facts Facts(
        bool onGpu = false,
        bool gpuFailed = false,
        string backend = "CPU (software)",
        bool durableEnabled = true,
        bool hasPresented = true,
        int? maxTexture = 8192,
        int docWidth = 1920,
        int docHeight = 1080,
        PlaybackClock.Pacing? pacing = null,
        Lightbox.App.Rendering.PresentLatency.Stats? presentWait = null,
        IReadOnlyList<TickProfile.PhaseStats>? tickPhases = null,
        int tickCount = 0,
        (long Hits, long Misses, long Evictions, long Bytes, long Budget)? frameCache = null,
        (int Frames, int Layers, int Strokes, double Fps)? scene = null,
        (long Requested, long Delivered)? animationFrames = null,
        double renderMedianMs = 0,
        bool gpuCompositeOptedIn = false,
        (int Hits, int Misses, long Bytes)? textureResidency = null,
        (long Frames, long Flattens)? awaitingUnpin = null,
        (int Frames, int Flattens)? pinned = null) =>
        new(backend, backend != "GPU", onGpu, gpuFailed, maxTexture,
            docWidth, docHeight, 1.0, "Full", 1.0, durableEnabled, hasPresented,
            Pacing: pacing, PresentWait: presentWait,
            TickPhases: tickPhases, TickCount: tickCount, FrameCache: frameCache, Scene: scene,
            AnimationFrames: animationFrames, RenderMedianMs: renderMedianMs,
            TextureResidency: textureResidency, GpuCompositeOptedIn: gpuCompositeOptedIn,
            AwaitingUnpin: awaitingUnpin, Pinned: pinned);

    /// <summary>
    /// The four states behind one boolean, and the reason this test exists: the
    /// first report from a real machine printed "durable frame on GPU: no" on a
    /// build where B130 had switched the frame off entirely. True, and nothing
    /// like what it appeared to mean. A diagnostic that cannot separate "not in
    /// use" from "failed" invites exactly the wrong conclusion.
    /// </summary>
    [Fact]
    public void TheReportSaysWhyTheDurableFrameIsNotOnTheGpu()
    {
        Setup();

        string Line(RenderReport.Facts f)
        {
            RenderReport.ResetForTests();
            var path = RenderReport.WriteStartup(f);
            var line = File.ReadAllLines(path!).First(l => l.Contains("durable frame"));
            File.Delete(path!);
            output.WriteLine(line);
            return line;
        }

        var off = Line(Facts(durableEnabled: false, backend: "GPU"));
        var unused = Line(Facts(durableEnabled: true, hasPresented: false, backend: "GPU"));
        var failed = Line(Facts(durableEnabled: true, gpuFailed: true, backend: "GPU"));
        var working = Line(Facts(durableEnabled: true, onGpu: true, backend: "GPU"));

        // Each must be distinguishable from the others, and only one may alarm.
        Assert.Contains("off", off);
        Assert.Contains("LIGHTBOX_DURABLE_FRAME", off);
        Assert.Contains("nothing has been presented yet", unused);
        Assert.Contains("refused", failed);
        Assert.Contains("yes", working);

        // The alarming one must not read like the other three.
        Assert.DoesNotContain("refused", off);
        Assert.DoesNotContain("refused", unused);
        Assert.DoesNotContain("refused", working);
    }

    /// <summary>
    /// The texture limit is only meaningful next to the surface it has to hold, so
    /// the report states the comparison rather than leaving it to be worked out.
    /// </summary>
    [Theory]
    [InlineData(16384, 3840, 2160, false)]   // a 4K canvas is nowhere near a real limit
    [InlineData(2048, 3840, 2160, true)]     // a limit a 4K canvas would actually exceed
    public void TheTextureLimitIsComparedAgainstTheSurfaceThatMustFit(
        int limit, int w, int h, bool shouldWarn)
    {
        Setup();
        var path = RenderReport.WriteStartup(
            Facts(backend: "GPU", maxTexture: limit, docWidth: w, docHeight: h));
        var text = File.ReadAllText(path!);
        var line = text.Split('\n').First(l => l.Contains("compose surface's"));
        output.WriteLine(line.Trim());

        if (shouldWarn) Assert.Contains("exceeds it", line);
        else Assert.Contains("not a factor", line);
    }

    /// <summary>
    /// A startup report has no measurements by construction. Saying so beats an
    /// absent section, which reads as "nothing was wrong" instead of "nothing was
    /// looked at".
    /// </summary>
    [Fact]
    public void AStartupReportSaysItHasNoMeasurementsYet()
    {
        Setup();
        var text = File.ReadAllText(RenderReport.WriteStartup(Facts())!);
        output.WriteLine(text);
        Assert.Contains("Nothing yet", text);
        Assert.Contains("Write a render report", text);
    }

    [Fact]
    public void TheStartupReportLandsInTheDiagnosticsFolderAndNamesTheBuild()
    {
        Setup();
        var path = RenderReport.WriteStartup(Facts());

        Assert.NotNull(path);
        Assert.Equal(_dir, Path.GetDirectoryName(path));
        var text = File.ReadAllText(path!);
        output.WriteLine(text);

        Assert.Contains("Lightbox render report", text);
        Assert.Contains(DiagnosticLog.Build, text);
        Assert.Contains("1920 x 1080", text);
        Assert.Contains("presentation backend", text);
    }

    /// <summary>
    /// <b>The section that separates the two halves of "playback stutters"
    /// (B150).</b> Every other measurement here is how long a frame took to
    /// *make*; this one is whether the tick that asked for it arrived when it
    /// was due. They are different axes, and only the second can make a
    /// near-empty scene stutter on a fast machine — so the report has to be able
    /// to tell an artist which one they are looking at.
    /// </summary>
    [Fact]
    public void TheReportSaysWhetherTheFrameClockWasDeliveredOnTime()
    {
        Setup();

        string Section(PlaybackClock.Pacing? pacing)
        {
            RenderReport.ResetForTests();
            var text = File.ReadAllText(RenderReport.WriteStartup(Facts(pacing: pacing))!);
            var start = text.IndexOf("was the frame clock on time", StringComparison.Ordinal);
            Assert.True(start >= 0, "the pacing section is missing from the report");
            return text[start..];
        }

        var never = Section(null);
        var onTime = Section(new PlaybackClock.Pacing(120, 3, 0, 0, 0.4, 2.1));
        var late = Section(new PlaybackClock.Pacing(120, 110, 14, 2, 31.5, 92.0));
        output.WriteLine(late);

        // Not run is distinct from run-and-fine, for the reason the durable-frame
        // line exists: an absent measurement reads as "nothing was wrong".
        Assert.Contains("PLAYED", never);

        Assert.Contains("delivered on time", onTime);
        Assert.DoesNotContain("LATE", onTime);

        Assert.Contains("LATE", late);
        Assert.Contains("31.5", late);
        Assert.Contains("92", late);
        Assert.Contains("14", late);
        // The one instruction that turns the number into a diagnosis, because the
        // difference between the two conditions is the finding.
        Assert.Contains("moving it", late);
    }

    /// <summary>
    /// <b>The section that tells B150's two candidate causes apart.</b> The
    /// pacing section says whether the tick arrived on time; this says whether
    /// the frame it asked for reached the screen. A report carrying only the
    /// first reads clean on a machine where the second is the problem, which is
    /// the failure this pair exists to remove.
    /// </summary>
    [Fact]
    public void TheReportSaysWhetherFramesReachedTheScreenOrSatWaiting()
    {
        Setup();

        string Section(Lightbox.App.Rendering.PresentLatency.Stats? wait)
        {
            RenderReport.ResetForTests();
            var text = File.ReadAllText(RenderReport.WriteStartup(Facts(presentWait: wait))!);
            var start = text.IndexOf("did the frames reach the screen", StringComparison.Ordinal);
            Assert.True(start >= 0, "the present-wait section is missing from the report");
            return text[start..];
        }

        var never = Section(null);
        var prompt = Section(new Lightbox.App.Rendering.PresentLatency.Stats(240, 1, 3.2, 11.0));
        var waiting = Section(new Lightbox.App.Rendering.PresentLatency.Stats(240, 40, 48.5, 130.0));
        output.WriteLine(waiting);

        Assert.Contains("PLAYED", never);
        Assert.Contains("promptly", prompt);
        Assert.DoesNotContain("WAITING", prompt);

        Assert.Contains("WAITING", waiting);
        Assert.Contains("48.5", waiting);
        Assert.Contains("40", waiting);
        // The instruction that turns the number into a diagnosis.
        Assert.Contains("pointer moving", waiting);
    }

    /// <summary>
    /// A run must never be attributable to a setting it did not have. The
    /// override exists to be typed at a command prompt while chasing a stutter,
    /// so which band was actually used has to be in the file that gets sent back
    /// — and printed always, because an absent line is indistinguishable from
    /// the expected one when two reports are being compared.
    /// </summary>
    [Fact]
    public void TheReportNamesTheClockPriorityItActuallyRanAt()
    {
        Setup();
        var text = File.ReadAllText(RenderReport.WriteStartup(Facts())!);

        Assert.Contains("clock priority", text);
        Assert.Contains(PlaybackClock.Priority.ToString(), text);
    }

    /// <summary>
    /// <b>The section that turns a localisation into a diagnosis.</b> The two
    /// before it establish that the clock is late and the frames arrive
    /// promptly, which narrows the cost to the tick handler and then stops. This
    /// has to name the phase, in milliseconds, on the machine that has the
    /// problem — the step B152 had to guess at and could not prove.
    /// </summary>
    [Fact]
    public void TheReportSaysWhichPartOfTheTickSpentTheTime()
    {
        Setup();

        var phases = new List<TickProfile.PhaseStats>
        {
            new(TickProfile.Phase.Thumbnails, 0, 0, 0),
            new(TickProfile.Phase.Highlights, 120, 24, 0.6),
            new(TickProfile.Phase.Bookkeeping, 120, 12, 0.4),
            new(TickProfile.Phase.Audio, 120, 6, 0.3),
            // Deliberately lopsided: the whole point of splitting Publish
            // (B157) is that the report can name WHICH half spent the tick.
            new(TickProfile.Phase.Compose, 120, 600, 9.2),
            new(TickProfile.Phase.Handoff, 120, 1200, 22.0),
        };

        var text = File.ReadAllText(RenderReport.WriteStartup(Facts(
            tickPhases: phases,
            tickCount: 120,
            frameCache: (Hits: 300, Misses: 900, Evictions: 850, Bytes: 500L * 1024 * 1024, Budget: 512L * 1024 * 1024),
            scene: (Frames: 90, Layers: 3, Strokes: 4200, Fps: 12.0)))!);
        var section = text[text.IndexOf("where the tick's time went", StringComparison.Ordinal)..];
        output.WriteLine(section);

        // The scene's shape, because it decides whether the cache can hold it —
        // and it is the fact the first symptomatic report was missing.
        Assert.Contains("90 frames, 3 layers", section);

        // A miss is a full frame replayed from its record, so the ratio is the
        // number that matters rather than the raw counts.
        Assert.Contains("75%", section);
        Assert.Contains("thrown out              850", section);

        // A phase that never ran is named rather than omitted: that is what
        // B152's fix looks like from here, and a missing line reads as a phase
        // nobody measured.
        Assert.Contains("Thumbnails", section);
        Assert.Contains("never ran", section);

        // And the dominant phase is called out, so the reader does not have to
        // do the arithmetic that the durable-frame line once cost us.
        Assert.Contains("most of itself in Handoff", section);
        Assert.Contains("10 ms/tick", section);
        // And the half that did NOT spend it is still on the page, because
        // "compositing is cheap here" is half the finding.
        Assert.Contains("Compose", section);
    }

    /// <summary>
    /// <b>The two numbers that decide between the last two explanations for
    /// playback riding on pointer movement (B153).</b> If wake-ups asked and
    /// arrived track each other, the compositor is ticking and the fault is
    /// upstream of it. If requests climb and callbacks do not, the compositor is
    /// genuinely asleep when input is quiet — a different fix, and worth knowing
    /// before building one.
    /// </summary>
    [Fact]
    public void TheReportSaysWhetherTheCompositorIsWakingWhenAsked()
    {
        Setup();

        string Line((long Requested, long Delivered)? frames)
        {
            RenderReport.ResetForTests();
            var text = File.ReadAllText(RenderReport.WriteStartup(Facts(
                presentWait: new Lightbox.App.Rendering.PresentLatency.Stats(200, 10, 40, 120),
                animationFrames: frames))!);
            var line = text.Split('\n').First(l => l.Contains("compositor wake-ups"));
            output.WriteLine(line.Trim());
            return line;
        }

        var healthy = Line((Requested: 300, Delivered: 298));
        var asleep = Line((Requested: 300, Delivered: 12));
        var absent = Line(null);

        Assert.DoesNotContain("NOT waking", healthy);
        Assert.Contains("NOT waking", asleep);
        // Not measured is distinct from measured-and-fine, for the reason the
        // durable-frame line exists: an absent number reads as "nothing wrong".
        Assert.Contains("not measured", absent);
    }

    /// <summary>Once per run, so a report is not rewritten on every repaint.</summary>
    [Fact]
    public void TheStartupReportIsWrittenOnlyOnce()
    {
        Setup();
        Assert.NotNull(RenderReport.WriteStartup(Facts()));
        Assert.Null(RenderReport.WriteStartup(Facts()));
        Assert.Single(Directory.GetFiles(_dir, "render-startup*.txt"));
    }

    /// <summary>
    /// The line the whole feature is for: a GPU surface that could not be created
    /// has to be shouted about, because the fallback is silent and the status
    /// strip still says "GPU".
    /// </summary>
    [Fact]
    public void ASilentFallbackToCpuIsReportedLoudly()
    {
        Setup();
        var path = RenderReport.WriteStartup(Facts(onGpu: false, gpuFailed: true, backend: "GPU"));
        var text = File.ReadAllText(path!);
        output.WriteLine(text);

        Assert.Contains("could not be created", text);
        Assert.Contains("ABSENT", text);

        // And the healthy case must NOT say it, or the warning means nothing.
        RenderReport.ResetForTests();
        File.Delete(path!);
        var healthy = File.ReadAllText(RenderReport.WriteStartup(Facts(onGpu: true, backend: "GPU"))!);
        Assert.DoesNotContain("ABSENT", healthy);
    }

    [Fact]
    public void TheOnDemandReportCarriesTheSessionTotalsAndTheSaving()
    {
        Setup();
        var totals = new RenderReport.Totals(
            Presents: 100, FullPresents: 4, FreePresents: 900,
            PatchedPixels: 1_000_000, PixelsIfAlwaysFull: 100_000_000,
            PublishMedianMs: 0.42, FrameMedianMs: 3.1);

        var path = RenderReport.WriteOnDemand(Facts(), totals, null);
        var text = File.ReadAllText(path!);
        output.WriteLine(text);

        Assert.Contains("99.0%", text);      // 1M of 100M copied
        Assert.Contains("100.0x less", text);
        Assert.Contains("900", text);        // the free repaints
        Assert.Contains("0.42 ms", text);
    }

    /// <summary>
    /// The probe has to come back with two numbers on the CPU path too — a
    /// diagnostic that only works on the hardware being diagnosed is no use for
    /// checking the diagnostic.
    /// </summary>
    [Fact]
    public void TheUploadProbeComparesTwoPresentsWithoutAGpu()
    {
        Setup();
        var probe = RenderReport.RunUploadProbe(gpu: null, width: 640, height: 480, iterations: 4);

        Assert.NotNull(probe);
        var p = probe!.Value;
        output.WriteLine($"full {p.FullMsMedian:0.000} ms, patch {p.PatchWidth}x{p.PatchHeight} "
                         + $"{p.PatchMsMedian:0.000} ms, gpu {p.WasGpuBacked}");

        Assert.False(p.WasGpuBacked);
        Assert.Equal(640, p.Width);
        Assert.True(p.FullMsMedian > 0, "the full present was not timed");
        Assert.True(p.PatchMsMedian >= 0, "the patch present was not timed");
        Assert.True(p.PatchWidth <= 44 && p.PatchHeight <= 28, "the probe's patch is not dab-sized");
    }

    /// <summary>A degenerate size must return null rather than throw.</summary>
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-4, -4)]
    public void TheProbeRefusesADegenerateSurfaceInsteadOfThrowing(int w, int h)
    {
        Setup();
        Assert.Null(RenderReport.RunUploadProbe(null, w, h));
    }

    /// <summary>
    /// A report that can break the application is worse than no report — the rule
    /// <c>DiagnosticLog</c> is built on, checked here for the same reason.
    /// </summary>
    [Fact]
    public void AnUnwritableFolderIsSurvivedRatherThanThrown()
    {
        Setup();
        // A path that cannot be a directory, because a file of that name exists.
        var blocker = Path.Combine(_dir, "blocked");
        File.WriteAllText(blocker, "not a directory");
        DiagnosticLog.DirectoryOverride = Path.Combine(blocker, "logs");

        var path = RenderReport.WriteStartup(Facts());
        output.WriteLine($"returned {(path is null ? "null" : path)}");
        Assert.Null(path);
    }

    /// <summary>
    /// The counters the report reads must describe what the frame did — this is
    /// the arithmetic behind the saving it claims.
    /// </summary>
    [Fact]
    public void ThePresentedFrameCountsFullAgainstPatchedAndFreeRepaints()
    {
        using var frame = new PresentedFrame();
        using var surface = SKSurface.Create(new SKImageInfo(200, 100, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Flush();
        using var image = surface.Snapshot();

        frame.Present(null, image, null, seq: 1);            // full
        frame.Present(null, image, new SKRectI(0, 0, 10, 10), seq: 2);  // patched
        frame.Present(null, image, new SKRectI(0, 0, 10, 10), seq: 2);  // free — same publish
        frame.Present(null, image, new SKRectI(0, 0, 10, 10), seq: 2);  // free

        output.WriteLine($"presents {frame.Presents}, full {frame.FullPresents}, free {frame.FreePresents}, "
                         + $"patched {frame.TotalPatchedPixels}, if-full {frame.TotalPixelsIfAlwaysFull}");

        Assert.Equal(2, frame.Presents);
        Assert.Equal(1, frame.FullPresents);
        Assert.Equal(2, frame.FreePresents);
        // 200x100 full, plus a patch grown to the clipped 10x10.
        Assert.Equal(200L * 100 + 100, frame.TotalPatchedPixels);
        Assert.Equal(2 * 200L * 100, frame.TotalPixelsIfAlwaysFull);
        Assert.False(frame.GpuSurfaceRequestFailed);
    }

    /// <summary>
    /// <b>The report must not tell a reader the GPU is unused in a file that also
    /// says publishes composited on it.</b>
    /// </summary>
    /// <remarks>
    /// The 2026-08-12 capture printed
    /// <c>of the publishes that could use the card: 310 did</c> and, three
    /// sections later, <c>The culled route is the only one that goes to the GPU
    /// today</c> — prose written before B167 phases 3b and 4 and never re-checked.
    /// Both lines were in one file, and a reader has no way to tell which is
    /// stale. This is the fifth report line in this project to be accurate when
    /// written and wrong once the thing it described moved, which is why the
    /// residency section now asks the compositing counter rather than asserting a
    /// route.
    /// </remarks>
    [Fact]
    public void TheResidencySectionDoesNotClaimTheGpuIsUnusedWhenItWasUsed()
    {
        Setup();
        Lightbox.App.Rendering.GpuComposite.ResetCounters();
        Lightbox.App.Rendering.GpuComposite.CountCompositeForTests(onGpu: true, times: 310);
        try
        {
            var path = RenderReport.WriteStartup(
                Facts(backend: "GPU", gpuCompositeOptedIn: true, textureResidency: null));
            var text = File.ReadAllText(path!);

            var residency = text[text.IndexOf("resident layer textures", StringComparison.Ordinal)..];
            output.WriteLine(residency);

            Assert.Contains("no layer textures were asked for", residency);
            // The claim that made the file self-contradictory.
            Assert.DoesNotContain("nothing composited through it", residency);
            Assert.DoesNotContain("the only one that goes to the GPU today", residency);
            // And it names the pair as a wiring fault, which is what it was.
            Assert.Contains("310 publish(es) DID composite on the card", residency);
        }
        finally
        {
            Lightbox.App.Rendering.GpuComposite.ResetCounters();
        }
    }

    /// <summary>
    /// With nothing on the card the old wording is still right, and still printed
    /// — a section that only ever hedged would be useless.
    /// </summary>
    [Fact]
    public void WithNothingOnTheCardItStillSaysSo()
    {
        Setup();
        Lightbox.App.Rendering.GpuComposite.ResetCounters();

        var path = RenderReport.WriteStartup(
            Facts(backend: "GPU", gpuCompositeOptedIn: true, textureResidency: null));
        var residency = File.ReadAllText(path!);
        residency = residency[residency.IndexOf("resident layer textures", StringComparison.Ordinal)..];
        output.WriteLine(residency);

        Assert.Contains("nothing has composited through it", residency);
        Assert.DoesNotContain("DID composite on the card", residency);
    }

    /// <summary>
    /// <b>B179's blind spot: every budget line read comfortably while the machine
    /// reached 12 GB and crashed.</b>
    /// </summary>
    /// <remarks>
    /// The report only ever printed the pools it knows about, so a leak anywhere
    /// else was invisible by construction — and worse, the four reassuring lines
    /// actively pointed away from it. "The caches are fine" and "the caches are
    /// fine and the process is not" have to be distinguishable, and only the
    /// second is something this report can help with.
    /// </remarks>
    [Fact]
    public void TheMemorySectionSeparatesTheCachesFromTheProcess()
    {
        Setup();
        var path = RenderReport.WriteStartup(Facts(
            frameCache: (0, 0, 0, 284L * 1024 * 1024, 4008L * 1024 * 1024),
            textureResidency: (0, 0, 93L * 1024 * 1024),
            gpuCompositeOptedIn: true));
        var text = File.ReadAllText(path!);
        var section = text[text.IndexOf("what memory is held", StringComparison.Ordinal)..];
        output.WriteLine(section);

        Assert.Contains("process working set", section);
        Assert.Contains("accounted for by caches", section);
        Assert.Contains("NOT in any cache this report tracks", section);
    }

    /// <summary>
    /// <b>The two pools that were tracked and never printed.</b> They are excluded
    /// from CachedBytes on purpose — they are not cache contents — and they are
    /// unbounded, which makes them the first place to look rather than a
    /// footnote. Named even at zero, because an absent line reads as "nothing was
    /// wrong" when it means "nothing was looked at".
    /// </summary>
    [Fact]
    public void ItNamesTheBytesEvictionCannotFree()
    {
        Setup();
        var path = RenderReport.WriteStartup(Facts(
            frameCache: (0, 0, 0, 100L * 1024 * 1024, 4008L * 1024 * 1024),
            awaitingUnpin: (700L * 1024 * 1024, 1300L * 1024 * 1024),
            pinned: (400, 900)));
        var text = File.ReadAllText(path!);
        var section = text[text.IndexOf("what memory is held", StringComparison.Ordinal)..];
        output.WriteLine(section);

        Assert.Contains("evicted, still in use", section);
        Assert.Contains("2000 MB", section);
        Assert.Contains("pinned by live snapshots", section);
        // A pin count that high is the leak shape, and it has to be called out
        // rather than left as a number to interpret.
        Assert.Contains("a pinned bitmap is one eviction", section);
    }

    /// <summary>
    /// <b>A capture taken in a diagnostic mode has to say so.</b>
    /// </summary>
    /// <remarks>
    /// B179's two discriminators change what the renderer does — budgeted
    /// surfaces let Skia purge them, and residency off uploads every layer per
    /// frame. A report that looked identical either way is how two runs get
    /// compared as though they came from the same build, which this session has
    /// already watched happen twice with stale prose. The mode is a fact about
    /// the capture, so it belongs in the capture.
    /// </remarks>
    [Fact]
    public void ADiagnosticCaptureSaysWhichModeItWasTakenIn()
    {
        Setup();
        var ordinary = File.ReadAllText(RenderReport.WriteStartup(Facts())!);
        Assert.DoesNotContain("NOT an ordinary capture", ordinary);

        Environment.SetEnvironmentVariable(
            Lightbox.App.Rendering.GpuComposite.BudgetedVariable, "1");
        try
        {
            RenderReport.ResetForTests();
            var flagged = File.ReadAllText(RenderReport.WriteStartup(Facts())!);
            output.WriteLine(flagged[flagged.IndexOf("what memory is held", StringComparison.Ordinal)..]);

            Assert.Contains("NOT an ordinary capture", flagged);
            Assert.Contains("compose surfaces are BUDGETED", flagged);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                Lightbox.App.Rendering.GpuComposite.BudgetedVariable, null);
        }
    }
}
