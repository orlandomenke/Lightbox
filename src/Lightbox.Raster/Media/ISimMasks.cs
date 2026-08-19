using Lightbox.Core.Documents;

namespace Lightbox.Raster.Media;

/// <summary>
/// Where a painted emitter emits, at the element's grid resolution.
/// </summary>
/// <remarks>
/// <para>
/// An interface rather than a document reference on <see cref="SimBaker"/>,
/// because the baker has no business knowing what a layer is. It takes fields
/// and gives back strokes; resolving a mask layer to coverage is somebody
/// else's job, and keeping it out means the whole bake stays testable with an
/// array and no document at all.
/// </para>
/// <para>
/// It is also the seam the obstacle will arrive through, which is the other half
/// of Q125 and its own branch.
/// </para>
/// </remarks>
public interface ISimMasks
{
    /// <summary>
    /// Coverage in 0..1 per cell, row-major and <c>width × height</c> long, or
    /// null when this emitter has no mask on this frame — a layer that was
    /// deleted, or a frame it draws nothing on.
    /// </summary>
    float[]? For(Emitter emitter, int frame);
}
