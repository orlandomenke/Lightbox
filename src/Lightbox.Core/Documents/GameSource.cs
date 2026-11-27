using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>
/// Where a document's frames came from in a game project, and exactly where
/// each one goes back to (Q300, docs/DESIGN-unity-link.md). Null, and absent from
/// the file, on every document not opened from a game clip.
/// </summary>
/// <remarks>
/// <para>
/// This is the record write-back inverts, so it holds numbers rather than
/// conventions: each frame's cell in the texture (top-left pixels, the way this
/// document measures), the integer offset the cell was placed at on the canvas,
/// and a hash of the cell's pixels as they were read. A write-back that finds the
/// hash changed on disk stops instead of overwriting work it never saw.
/// </para>
/// <para>
/// The canvas size it was built for is kept too. Every offset is a document
/// coordinate, and a resize or crop moves them all; write-back refuses a canvas
/// that no longer matches rather than landing every frame shifted.
/// </para>
/// </remarks>
public sealed class GameSource
{
    /// <summary>
    /// Keys a newer build wrote that this one does not know, carried through
    /// untouched so a save here does not drop them (Q230).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The engine whose files these are. "unity" is the only one so far.</summary>
    public string Engine { get; set; } = "unity";

    /// <summary>The clip, relative to the game project's root, forward slashes.</summary>
    public string Clip { get; set; } = "";

    /// <summary>The canvas width the offsets were measured on.</summary>
    public int CanvasWidth { get; set; }

    /// <summary>The canvas height the offsets were measured on.</summary>
    public int CanvasHeight { get; set; }

    /// <summary>One per renderer the clip drives.</summary>
    public List<GameSourceTrack> Tracks { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public GameSource Clone()
    {
        var copy = (GameSource)MemberwiseClone();
        copy.Tracks = Tracks.Select(t => t.Clone()).ToList();
        return copy;
    }
}

/// <summary>One renderer's frames, and the layers they were placed on.</summary>
public sealed class GameSourceTrack
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The renderer's path below the animated object; empty for the object itself.</summary>
    public string Path { get; set; } = "";

    /// <summary>The renderer's component class (212 is a sprite renderer).</summary>
    public int ClassId { get; set; } = 212;

    /// <summary>The locked layer holding what the game shows today.</summary>
    public string PlaceholderLayerId { get; set; } = "";

    /// <summary>The layer the artist draws on, whose drawings are written back.</summary>
    public string? DrawingLayerId { get; set; }

    /// <summary>Each exposure, in timeline order.</summary>
    public List<GameSourceFrame> Frames { get; set; } = [];

    public GameSourceTrack Clone()
    {
        var copy = (GameSourceTrack)MemberwiseClone();
        copy.Frames = Frames.Select(f => f.Clone()).ToList();
        return copy;
    }
}

/// <summary>One exposure: when it shows, which sprite it is, and where that sprite lives.</summary>
public sealed class GameSourceFrame
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The first timeline frame it shows on, from 0.</summary>
    public int Start { get; set; }

    /// <summary>How many frames it holds for.</summary>
    public int Length { get; set; } = 1;

    /// <summary>The texture's guid. Null for a blank frame or a sprite that could not be found.</summary>
    public string? Texture { get; set; }

    /// <summary>The sprite's id inside the texture.</summary>
    public long Sprite { get; set; }

    /// <summary>The sprite's name, for messages.</summary>
    public string? Name { get; set; }

    /// <summary>The texture's file, relative to the project root, when it was found.</summary>
    public string? TexturePath { get; set; }

    /// <summary>The cell in the texture: x, top, width, height — top-left pixels.</summary>
    public int[]? Cell { get; set; }

    /// <summary>The sprite's pivot, normalised as Unity stores it (y up from the cell's bottom).</summary>
    public double[]? Pivot { get; set; }

    /// <summary>Where the cell's top-left was placed on the canvas.</summary>
    public int[]? CanvasOffset { get; set; }

    /// <summary>SHA-256 of the cell's pixels as read, lower-case hex.</summary>
    public string? SourceHash { get; set; }

    public GameSourceFrame Clone()
    {
        var copy = (GameSourceFrame)MemberwiseClone();
        copy.Cell = (int[]?)Cell?.Clone();
        copy.Pivot = (double[]?)Pivot?.Clone();
        copy.CanvasOffset = (int[]?)CanvasOffset?.Clone();
        return copy;
    }
}
