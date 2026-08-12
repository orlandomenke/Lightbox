using SkiaSharp;

namespace Lightbox.App.Rendering;

/// <summary>
/// Whether a composite should go to a GPU surface on <em>this</em> machine.
/// </summary>
/// <remarks>
/// <para>
/// <b>B125 stage 4's policy, and it is a policy rather than a constant because
/// of the owner's question:</b> testing on one graphics card is fine, but this
/// has to run better on every computer with one. So the compositor is chosen on
/// the machine it is running on, and the CPU path stays first-class rather than
/// legacy.
/// </para>
/// <para>
/// <b>Off by default, and that is not timidity — it is what stage 4 is for.</b>
/// Stage 4 uploads every layer every frame, which is the worst case by
/// construction and may well be *slower* than the CPU path on integrated
/// graphics, where the upload competes with the CPU for the same memory bus.
/// The point of the stage is to measure that, on real hardware this repository
/// will never own. Defaulting it on would be shipping an unmeasured change to
/// the drawing path, and B130 is what that looks like.
/// </para>
/// <para>
/// <b>The refusals are the interesting part</b>, because each is a way one
/// machine's measurement would not have generalised:
/// </para>
/// <list type="bullet">
/// <item><b>No context.</b> Headless, software rendering, a lease that could not
/// give one. Nothing to compose onto.</item>
/// <item><b>Bigger than the texture limit.</b> Hardware, not driver: a 4K
/// document needs 4096 and 8K needs 8192, and older parts cap below that. This
/// is the one that would turn "slower" into "will not open", so it is a refusal
/// rather than an attempt with a fallback.</item>
/// <item><b>Allocation failure.</b> Asked for and refused at runtime — fall back
/// and say so, because "the GPU path did not help" and "the GPU path never ran"
/// are indistinguishable otherwise.</item>
/// </list>
/// </remarks>
internal static class GpuComposite
{
    /// <summary>
    /// The opt-in. An environment variable rather than a setting, deliberately:
    /// this is a measurement instrument for stage 4, and nobody should find it
    /// by accident. Same reasoning, and the same precedent, as
    /// <c>LIGHTBOX_DURABLE_FRAME</c> after B130.
    /// </summary>
    internal const string OptInVariable = "LIGHTBOX_GPU_COMPOSITE";

    /// <summary>Times the GPU surface was asked for and refused.</summary>
    internal static int RefusedAllocations { get; private set; }

    /// <summary>Times a document was too large for the context's textures.</summary>
    internal static int RefusedTooLarge { get; private set; }

    /// <summary>Composites that ran on a GPU surface.</summary>
    internal static int GpuComposites { get; private set; }

    /// <summary>Composites that ran on a CPU surface.</summary>
    internal static int CpuComposites { get; private set; }

    internal static void ResetCounters()
    {
        RefusedAllocations = 0;
        RefusedTooLarge = 0;
        GpuComposites = 0;
        CpuComposites = 0;
    }

    /// <summary>
    /// Pretend a composite happened, for tests that need the counter set.
    /// </summary>
    /// <remarks>
    /// <b>A seam rather than a contrivance:</b> incrementing
    /// <see cref="GpuComposites"/> for real needs a <see cref="GRContext"/>, and
    /// there is none in this repository — the same constraint that made the
    /// report the only place the GPU could be observed at all. The report's own
    /// wording now depends on this counter, so the wording has to be testable
    /// without one.
    /// </remarks>
    internal static void CountCompositeForTests(bool onGpu, int times = 1)
    {
        if (onGpu) GpuComposites += times;
        else CpuComposites += times;
    }

    /// <summary>
    /// Ask Skia to account for the compose surface, instead of opting out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B179's discriminator, and it exists to be switched rather than
    /// shipped.</b> The leak is established to be in the GPU compositing path —
    /// working set 894 MB with the toggle off against 12 GB with it on — and two
    /// candidates remain inside it. This separates them in one playback: memory
    /// flat with this on means the unbudgeted surfaces were the cause; still
    /// climbing means they are exonerated and the residency cache is next.
    /// </para>
    /// <para>
    /// <b>Default off, because <c>budgeted: false</c> is not an accident.</b>
    /// <see cref="CreateSurface"/> asks for it so that Skia cannot recycle a
    /// surface underneath a snapshot that is still being read — B130's failure
    /// mode, a use-after-free in native code with an empty crash log. Turning
    /// this on trades that guarantee for the measurement, which is a fine trade
    /// for one capture and not a fix.
    /// </para>
    /// </remarks>
    internal const string BudgetedVariable = "LIGHTBOX_GPU_BUDGETED";

    internal static bool Budgeted =>
        Environment.GetEnvironmentVariable(BudgetedVariable) is "1" or "true" or "TRUE";

    /// <summary>
    /// B179's second discriminator: keep GPU compositing on and take residency
    /// out, so the two remaining candidates can be told apart.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bisect established the leak is in the GPU path, and two things in it
    /// hold GPU memory: the compose surfaces and the layer textures. Switching
    /// one off at a time separates them — the same move that worked on the path
    /// itself, and a measurement nobody can argue with beats a third round of
    /// instrumentation.
    /// </para>
    /// <para>
    /// <b>Here rather than on the canvas, and that is not tidiness.</b> It lived
    /// on <c>CanvasControl</c> for one commit and every render-report test went
    /// red: reading a member off an Avalonia control forces its static
    /// initialisation, which registers styled properties and cannot run in a
    /// headless test. A switch the report has to read belongs on a type the
    /// report can touch.
    /// </para>
    /// </remarks>
    internal const string NoResidencyVariable = "LIGHTBOX_NO_RESIDENCY";

    internal static bool ResidencyDisabled =>
        Environment.GetEnvironmentVariable(NoResidencyVariable) is "1" or "true" or "TRUE";

    private static bool? _override;

    private static bool ForcedByEnvironment =>
        Environment.GetEnvironmentVariable(OptInVariable) is "1" or "true" or "TRUE";

    /// <summary>
    /// Whether this session composites on the GPU.
    /// </summary>
    /// <remarks>
    /// <b>The setting is the switch and the environment variable is an override,
    /// not the other way round.</b> It began as an environment variable on the
    /// B130 precedent — nobody should find an unmeasured GPU path by accident —
    /// and became a setting when the person who has to run the measurement asked
    /// for one, which is the right reason to move it. The variable stays for
    /// headless and scripted runs, where there is no window to tick a box in.
    /// <para>
    /// Read fresh rather than cached: the toggle takes effect on the next frame,
    /// because a restart between "switch it on" and "see the number" is exactly
    /// the friction that stops a measurement from happening.
    /// </para>
    /// </remarks>
    internal static bool OptedIn =>
        _override ?? (ForcedByEnvironment || SettingEnabled);

    /// <summary>
    /// Mirrors <c>AppSettings.GpuCompositing</c>, written when settings load and
    /// when the toggle moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mirror rather than a read, because this is consulted from the render
    /// thread inside the draw op and the settings object lives on the view model.
    /// A bool written on the UI thread and read on the render thread is the one
    /// shape of sharing that needs no synchronisation — a torn read of a bool
    /// does not exist, and being one frame stale after a toggle is invisible.
    /// </para>
    /// <para>
    /// <b>Moving it clears the counters, and that is B184 rather than tidiness.</b>
    /// Nothing else in the application ever calls <see cref="ResetCounters"/>, so
    /// the tallies ran for the life of the process — and every composite made
    /// while the toggle was <em>off</em> landed in <see cref="CpuComposites"/>,
    /// which the report then prints as a fallback. A capture taken after a
    /// bisect therefore read <c>160 did, 207 fell back</c> when nothing had
    /// fallen back at all: the 207 were the previous playback, taken with the
    /// path switched off. That is the exact reading a discriminating experiment
    /// cannot survive, and it happened on the one bug the experiment was for.
    /// </para>
    /// <para>
    /// The counts are only ever printed for the mode that is running, so
    /// clearing them on the transition loses nothing and makes the line mean
    /// what it says.
    /// </para>
    /// </remarks>
    internal static bool SettingEnabled
    {
        get => _settingEnabled;
        set
        {
            if (_settingEnabled == value) return;
            _settingEnabled = value;
            ResetCounters();
        }
    }

    private static bool _settingEnabled;

    /// <summary>For tests: force the opt-in on or off, or null to read the real answer.</summary>
    internal static bool? OptInOverride
    {
        get => _override;
        set => _override = value;
    }

    /// <summary>
    /// Whether a surface this size should be GPU-backed, given this context.
    /// </summary>
    /// <remarks>
    /// Pure and side-effect free apart from the too-large tally, so the policy
    /// can be asserted on without a graphics context — which is the only way it
    /// gets tested at all in this repository.
    /// </remarks>
    internal static bool Wants(GRContext? gpu, SKImageInfo info)
    {
        if (!OptedIn || gpu is null) return false;
        if (info.Width <= 0 || info.Height <= 0) return false;

        var limit = gpu.MaxTextureSize;
        if (limit > 0 && (info.Width > limit || info.Height > limit))
        {
            RefusedTooLarge++;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Create the surface the policy asks for, falling back to CPU rather than
    /// failing. A slow frame beats no frame; a missing frame is a black canvas.
    /// </summary>
    internal static SKSurface CreateSurface(GRContext? gpu, SKImageInfo info, out bool gpuBacked)
    {
        if (Wants(gpu, info))
        {
            // Unbudgeted, matching PresentedFrame: a budgeted surface can be
            // recycled underneath a snapshot that is still being read.
            //
            // B179 can flip it for one capture (see Budgeted): unbudgeted means
            // Skia neither counts these against its cache nor purges them, so
            // they are invisible to its accounting and to ours at once — which
            // is exactly the shape of the memory that is missing.
            var surface = SKSurface.Create(gpu, Budgeted, info);
            if (surface is not null)
            {
                gpuBacked = true;
                GpuComposites++;
                return surface;
            }
            RefusedAllocations++;
        }

        gpuBacked = false;
        CpuComposites++;
        return SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create render surface");
    }
}
