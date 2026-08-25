using Lightbox.App.Services;

namespace Lightbox.App.Rendering;

/// <summary>
/// The session's one composite cache, reachable from the draw op.
/// </summary>
/// <remarks>
/// <para>
/// <b>Static for the same reason <see cref="GpuImageReaper"/> and
/// <c>GpuComposite</c> are.</b> The cache is filled inside the draw op, on the
/// render thread, where there is no route back to the view model and no way to
/// hand a control a new field without the render pipeline learning about it.
/// Both of those types settled this question already; a third answer would be a
/// third thing to keep in step.
/// </para>
/// <para>
/// <b>It is also what keeps the wiring out of <c>CanvasControl.cs</c>.</b> That
/// file is on the monolith ratchet with no headroom, and threading a cache
/// through the draw op's constructor would have cost it a field, a parameter and
/// an argument. The ratchet asking "does this really belong in the hub" got the
/// right answer twice in one day.
/// </para>
/// <para>
/// <b>The budget is a share of the machine, not a constant.</b> A compose
/// surface is view-sized rather than document-sized, so a 4K document in a
/// 1080p window costs about the window — roughly 8 MB a frame, and a 24-frame
/// loop about 200 MB. A 240-frame scene would be 2 GB, which is why this is a
/// window around the playhead inside a budget and never "the animation".
/// </para>
/// </remarks>
internal static class ComposeCacheHost
{
    /// <summary>A twelfth of the machine, floored at 128 MB and capped at 1 GB.</summary>
    /// <remarks>
    /// The floor is where a loop stops fitting at all and the feature stops
    /// being worth having rather than merely being smaller; the ceiling is where
    /// a cache is larger than any working set it could serve.
    /// </remarks>
    private static readonly long Budget = MemoryBudget.Share(
        fraction: 1.0 / 12, floorBytes: 128L * 1024 * 1024, ceilingBytes: 1024L * 1024 * 1024);

    internal static ComposeCache Shared { get; private set; } = new(Budget);

    /// <summary>
    /// Everything cached is stale — a drawing changed, or the document did.
    /// </summary>
    /// <remarks>
    /// Called on the UI thread from the same funnel that bumps the epoch. The
    /// epoch alone would already make every entry unreachable, so this is about
    /// giving the memory back promptly rather than about correctness; a GPU
    /// image still goes to the reaper rather than being freed here (B179).
    /// </remarks>
    internal static void Invalidate() => Shared.Clear();

    /// <summary>For tests: a cache nobody else has touched.</summary>
    internal static void ResetForTests() => Shared = new ComposeCache(Budget);
}
