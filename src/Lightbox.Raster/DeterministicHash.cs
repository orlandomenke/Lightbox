namespace Lightbox.Raster;

/// <summary>
/// The one alternative to an RNG in this assembly: a pure 0..1 hash of a
/// position, used wherever rendering wants variation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Invariant 2 lives here.</b> Effects seeded from a random source would make
/// a reload, an undo and an AI inbetween disagree about what the same document
/// looks like, so nothing in the render path may draw on one. Everything seeds
/// from geometry instead, through this — which is also what makes "visual
/// variation is wanted, logical randomness is forbidden" a mechanical rule
/// rather than an aspiration: two marks in the same place come out the same,
/// and two marks a pixel apart do not.
/// </para>
/// <para>
/// <b>Why the IEEE-754 bits rather than the number.</b> The float's bits are
/// hashed directly, so the seed changes with the coordinate rather than with any
/// rounding of it. Invariant 7 is the consequence and the reason it is worth
/// stating: doubling a coordinate re-rolls every dynamic seeded from it, so a 2×
/// render must scale the surface and never the geometry.
/// </para>
/// <para>
/// <b>Extracted from <c>BrushEngine</c> unchanged</b> when the fluid solver
/// needed the same primitive for its turbulence. Two copies of the project's
/// determinism primitive is exactly the kind of thing that drifts silently, and
/// a drift here is a document that renders differently depending on which
/// subsystem drew it. <c>BrushEngine.Hash01</c> forwards to this, so the marks
/// it produces are bit-identical to the ones it produced before the move —
/// <c>RuntimeDeterminismTests</c> is what says so.
/// </para>
/// </remarks>
public static class DeterministicHash
{
    /// <summary>
    /// A 0..1 value that depends only on the two coordinates and the salt.
    /// FNV-1a over the operands' bits, finished with an xorshift-multiply so
    /// neighbouring coordinates do not produce neighbouring values.
    /// </summary>
    /// <param name="salt">
    /// Which dynamic is asking. Multiplied by the golden-ratio constant before
    /// it enters the state, so two dynamics reading the same position are
    /// decorrelated rather than merely offset.
    /// </param>
    public static double Unit(float x, float y, uint salt)
    {
        var h = 2166136261u ^ (salt * 0x9E3779B9u);
        h = (h ^ (uint)BitConverter.SingleToInt32Bits(x)) * 16777619u;
        h = (h ^ (uint)BitConverter.SingleToInt32Bits(y)) * 16777619u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (double)0x1000000;
    }
}
