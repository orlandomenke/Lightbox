namespace Lightbox.Core.Documents;

/// <summary>
/// The sprite's origin, in document coordinates. See <see cref="Scene.Pivot"/>
/// for why it exists and why it is optional.
/// </summary>
public sealed class Pivot
{
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Feet centre: where a standing character is usually anchored.</summary>
    public static Pivot BottomCentre(int sceneWidth, int sceneHeight) =>
        new() { X = sceneWidth / 2.0, Y = sceneHeight };
}
