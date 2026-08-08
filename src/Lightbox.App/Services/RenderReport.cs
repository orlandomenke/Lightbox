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
        Rendering.TileFallbackTally? TileFallbacks = null);

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
        sb.AppendLine($"  (this is the FINAL BLIT only — compositing is on the CPU either way)");
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
        sb.AppendLine("compositing               CPU raster (always — see B125)");
        sb.AppendLine();

        AppendTilePath(sb, facts.TileFallbacks);

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
