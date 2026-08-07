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
        double ComposeScale);

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
        sb.AppendLine($"durable frame on GPU      {Yes(facts.PresentedFrameOnGpu)}");
        if (facts.GpuSurfaceRequestFailed)
        {
            sb.AppendLine("  !! a GPU surface was asked for and could not be created, so the");
            sb.AppendLine("     frame fell back to CPU raster. The upload saving is ABSENT on");
            sb.AppendLine("     this machine even though the backend above says GPU. This is");
            sb.AppendLine("     the line to report — see B122 and B125.");
        }
        sb.AppendLine($"max texture size          {facts.MaxTextureSize?.ToString() ?? "unknown"}");
        sb.AppendLine("compositing               CPU raster (always — see B125)");
        sb.AppendLine();

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
