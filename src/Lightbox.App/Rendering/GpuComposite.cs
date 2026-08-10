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

    internal static void ResetCounters()
    {
        RefusedAllocations = 0;
        RefusedTooLarge = 0;
    }

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
    /// A mirror rather than a read, because this is consulted from the render
    /// thread inside the draw op and the settings object lives on the view model.
    /// A bool written on the UI thread and read on the render thread is the one
    /// shape of sharing that needs no synchronisation — a torn read of a bool
    /// does not exist, and being one frame stale after a toggle is invisible.
    /// </remarks>
    internal static bool SettingEnabled { get; set; }

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
            var surface = SKSurface.Create(gpu, false, info);
            if (surface is not null)
            {
                gpuBacked = true;
                return surface;
            }
            RefusedAllocations++;
        }

        gpuBacked = false;
        return SKSurface.Create(info)
            ?? throw new InvalidOperationException("Failed to create render surface");
    }
}
