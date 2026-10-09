using System.Text.Json.Serialization;
using System.Text.Json;
namespace Lightbox.Core.Documents;

/// <summary>
/// The sprite's origin, in document coordinates. See <see cref="Scene.Pivot"/>
/// for why it exists and why it is optional.
/// </summary>
public sealed class Pivot
{
    /// <summary>
    /// Keys a newer build wrote that this one does not know, carried through
    /// untouched so a save here does not drop them (Q230). Null unless a file
    /// supplied some; never edited, so a clone may share it.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>Feet centre: where a standing character is usually anchored.</summary>
    public static Pivot BottomCentre(int sceneWidth, int sceneHeight) =>
        new() { X = sceneWidth / 2.0, Y = sceneHeight };

    /// <summary>A copy holding no reference in common with this one.</summary>
    public Pivot Clone() => (Pivot)MemberwiseClone();
}
