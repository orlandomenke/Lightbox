using System.Diagnostics;
using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// What one machine's graphics hardware said when asked to blend, measured
/// against the same blend on the processor.
/// </summary>
/// <param name="HadContext">False when there was no graphics context to ask.</param>
/// <param name="SurfaceRefused">
/// True when a context existed and would not give a surface of the probe's
/// size — a driver refusal, which is a "no" rather than a slow "yes".
/// </param>
/// <param name="GpuMs">Fastest run on the card, including the submit that waits for it.</param>
/// <param name="CpuMs">Fastest run of the identical work on a raster surface.</param>
/// <param name="Side">The square the probe blended, in pixels.</param>
/// <param name="Layers">How many passes it blended per run.</param>
internal readonly record struct GpuProbeResult(
    bool HadContext,
    bool SurfaceRefused,
    double GpuMs,
    double CpuMs,
    int Side,
    int Layers)
{
    /// <summary>How many times faster the card was. Zero when there is no answer.</summary>
    internal double Speedup => GpuMs > 0 && CpuMs > 0 ? CpuMs / GpuMs : 0;

    /// <summary>One line for the render report, and the same line for the log.</summary>
    internal string Describe() =>
        !HadContext ? "no graphics context — the compositor stays on the processor"
        : SurfaceRefused ? "the driver refused a surface — the compositor stays on the processor"
        : GpuMs <= 0 || CpuMs <= 0 ? "the probe returned no timing — the compositor stays on the processor"
        : $"{Side}x{Side}, {Layers} layers: card {GpuMs:F2} ms against processor {CpuMs:F2} ms "
          + $"= {Speedup:F2}x ({(GpuComposeProbe.Decide(this) ? "using the card" : "staying on the processor")})";
}

/// <summary>
/// Decides, on the machine it is running on, whether this session should
/// composite on the graphics card.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is <c>DESIGN-gpu-compositing.md</c>'s "probe, then choose", built.</b>
/// The note's own argument for it is that a measurement on one graphics card
/// settles how urgent residency is and settles nothing whatever about whether
/// the GPU path should ship — because the ways it can go wrong do not
/// generalise. One of them is a trap rather than a risk:
/// </para>
/// <para>
/// <b>A software rasteriser reports as a GPU.</b> <c>llvmpipe</c> and
/// <c>swiftshader</c> hand Skia a real GL context that it accepts, so
/// <see cref="CanvasControl.GraphicsBackend"/> says "GPU" while every pixel is
/// drawn on the processor anyway — slower than the path it replaced, with the
/// status bar claiming otherwise. No vendor list catches that and no build flag
/// catches it. Timing the two against each other does, because a software
/// rasteriser cannot be faster than the raster backend it *is*.
/// </para>
/// <para>
/// <b>What this can and cannot say.</b> It can say that blending N passes into a
/// surface of this size is faster on the card than on the processor, on this
/// machine, today. It cannot say the win holds at 4K with ten layers, and it
/// cannot say anything about upload bandwidth once textures are resident —
/// that is what a render report from a real session is for and this does not
/// replace it. It is a gate against the machine where the answer is *no*, not
/// a measurement of how good *yes* is.
/// </para>
/// <para>
/// <b>The two timings are deliberately not symmetric, and that is the whole
/// point of the GPU one.</b> Both clear a surface and blend the same passes into
/// it; the card's run then calls <see cref="GRContext.Submit"/> synchronously,
/// because without it the number is how long it took to *queue* the work. A
/// probe that skipped the submit would report the card as impossibly fast on
/// every machine, including the ones it exists to refuse.
/// </para>
/// </remarks>
internal static class GpuComposeProbe
{
    /// <summary>
    /// How much faster the card has to be before it is worth using.
    /// </summary>
    /// <remarks>
    /// <b>Not 1.0, and the margin is doing work.</b> A software rasteriser lands
    /// near parity and noise alone can put it either side of it, so a 1.0 gate
    /// would switch the compositor on and off between sessions on the same
    /// machine. A card that cannot beat the processor by half again at this size
    /// is also a card whose win will not survive residency's memory cost — on
    /// integrated graphics that VRAM is the same memory the processor is
    /// competing for. Refusing is cheap here: the CPU path is what export
    /// already uses, so it cannot rot.
    /// </remarks>
    internal const double RequiredSpeedup = 1.5;

    /// <summary>The square the probe blends. Big enough to measure, small enough to pay once.</summary>
    internal const int Side = 1024;

    /// <summary>Passes per run — a background, a character, an overlay.</summary>
    internal const int Layers = 3;

    /// <summary>Runs per backend; the fastest is taken, per the charter's argument.</summary>
    private const int Iterations = 3;

    private static bool _ran;

    /// <summary>
    /// Ask this machine, once per session, and record the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Called from inside the draw op, because that is where the context
    /// is.</b> The <c>GRContext</c> lives in the render thread's lease and
    /// nowhere else — the same fact that made GPU compositing an inversion
    /// rather than a substitution (<c>DESIGN-gpu-compositing.md</c>'s first
    /// crux), and the same reason the render report's upload probe has to be
    /// queued rather than called.
    /// </para>
    /// <para>
    /// <b>Not through <c>CanvasControl.RunWithGpuContext</c>, deliberately.</b>
    /// That is a single slot with one existing owner (the render report), and
    /// two things posting to one slot is a queued job silently dropped. This
    /// runs inline on the first frame instead, which is also the earliest it
    /// can possibly run.
    /// </para>
    /// <para>
    /// <b>And it lives here rather than on the canvas, which is not
    /// tidiness.</b> <c>CanvasControl.cs</c> is one of the two files the
    /// monolith ratchet holds at a fixed size, and it earned that by absorbing
    /// exactly this kind of passenger. The rule the ratchet encodes is that a
    /// thing the canvas merely *calls* belongs with the thing it calls.
    /// </para>
    /// <para>
    /// It costs one frame at startup and only ever runs when the mode is
    /// Automatic — an artist who has chosen On or Off is not asking to be
    /// measured, and measuring them anyway would spend the frame for nothing.
    /// </para>
    /// </remarks>
    internal static void RunOnce(GRContext? gpu)
    {
        if (_ran) return;
        if (GpuComposite.Mode != GpuComposeMode.Auto) return;
        _ran = true;
        var result = Run(gpu);
        GpuComposite.NoteProbe(result);
        Services.DiagnosticLog.WriteNote("gpu-compose-probe", result.Describe());
    }

    /// <summary>Test seam: let the probe run again.</summary>
    internal static void ForgetForTests()
    {
        _ran = false;
        GpuComposite.ForgetProbeForTests();
    }

    /// <summary>
    /// The verdict, as a pure function of the measurement.
    /// </summary>
    /// <remarks>
    /// Separated from the measuring so it can be asserted on without a graphics
    /// context, which is the only kind of test this repository can write about
    /// any of this — the same constraint that made <c>GpuCompositeTests</c>
    /// assert the policy and not the speed.
    /// </remarks>
    internal static bool Decide(in GpuProbeResult result) =>
        result.HadContext
        && !result.SurfaceRefused
        && result.GpuMs > 0
        && result.CpuMs > 0
        && result.CpuMs / result.GpuMs >= RequiredSpeedup;

    /// <summary>
    /// Blend the same passes on the card and on the processor and time both.
    /// Never throws: a diagnostic that can take the compositor down is worse
    /// than no diagnostic.
    /// </summary>
    internal static GpuProbeResult Run(GRContext? gpu)
    {
        if (gpu is null) return new GpuProbeResult(false, false, 0, 0, Side, Layers);
        try
        {
            var info = new SKImageInfo(Side, Side, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var source = MakeSource(info);
            if (source is null) return new GpuProbeResult(true, true, 0, 0, Side, Layers);

            // Unbudgeted, matching GpuComposite.CreateSurface: the probe should
            // measure the surface compositing will actually ask for, not a
            // cheaper one. B179's hazard does not reach here — that was a GPU
            // image freed on the UI thread, where a release only parks the
            // resource on the context's deferred queue. This runs inside the
            // draw op, so every `using` below frees on the context's own thread,
            // and nothing it allocates is ever handed anywhere.
            using var onGpu = SKSurface.Create(gpu, false, info);
            if (onGpu is null) return new GpuProbeResult(true, true, 0, 0, Side, Layers);
            using var onCpu = SKSurface.Create(info);
            if (onCpu is null) return new GpuProbeResult(true, true, 0, 0, Side, Layers);

            // Warm both: the first draw into either surface allocates, and an
            // allocation timed as work is the attribution error this repository
            // keeps a design note about.
            Blend(onGpu, source, gpu);
            Blend(onCpu, source, null);

            var gpuMs = Fastest(() => Blend(onGpu, source, gpu));
            var cpuMs = Fastest(() => Blend(onCpu, source, null));
            return new GpuProbeResult(true, false, gpuMs, cpuMs, Side, Layers);
        }
        catch
        {
            // A probe that faulted is a probe that answered "no". Anything else
            // would let a driver quirk decide the compositor by exception.
            return new GpuProbeResult(true, true, 0, 0, Side, Layers);
        }
    }

    /// <summary>Something with edges and transparency, so the blend has work to do.</summary>
    private static SKImage? MakeSource(SKImageInfo info)
    {
        using var surface = SKSurface.Create(info);
        if (surface is null) return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = new SKColor(40, 110, 220, 190), IsAntialias = true };
        for (var i = 0; i < 8; i++)
        {
            canvas.DrawCircle(
                info.Width * (0.1f + 0.1f * i), info.Height * 0.5f, info.Width * 0.18f, paint);
        }
        canvas.Flush();
        return surface.Snapshot();
    }

    /// <summary>
    /// One run: clear, blend every pass, and wait for it to actually be done.
    /// </summary>
    private static void Blend(SKSurface surface, SKImage source, GRContext? gpu)
    {
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xf2, 0xf0, 0xea));
        using var paint = new SKPaint { BlendMode = SKBlendMode.SrcOver };
        for (var i = 0; i < Layers; i++)
        {
            paint.Color = SKColors.White.WithAlpha((byte)(255 - i * 40));
            canvas.DrawImage(source, 0, 0, paint);
        }
        canvas.Flush();
        // The submit is what makes the card's number mean something — see the
        // remarks on this class. Synchronous on purpose.
        gpu?.Submit(true);
    }

    private static double Fastest(Action body)
    {
        var best = double.MaxValue;
        for (var i = 0; i < Iterations; i++)
        {
            var started = Stopwatch.GetTimestamp();
            body();
            var ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            if (ms < best) best = ms;
        }
        return best is double.MaxValue ? 0 : best;
    }
}
