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

    private static RenderReport.Facts Facts(
        bool onGpu = false, bool gpuFailed = false, string backend = "CPU (software)") =>
        new(backend, backend != "GPU", onGpu, gpuFailed, 8192, 1920, 1080, 1.0, "Full", 1.0);

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
}
