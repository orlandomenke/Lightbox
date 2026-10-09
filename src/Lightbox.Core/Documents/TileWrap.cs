using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>
/// The tile a stroke was painted to wrap around: a rectangle in document
/// coordinates whose left edge continues on its right and whose top continues
/// on its bottom.
/// </summary>
/// <remarks>
/// <para>
/// <b>Wrap is one more kind of symmetry copy</b> (Q192). A mark that leaves the
/// tile on one side is stamped again translated by the tile's width or height,
/// so it comes in on the other — and because the copy is made by translating
/// the <em>canvas</em> rather than the geometry, <c>Hash01</c> seeds every dab
/// from the coordinates the artist drew and the copy carries identical grain,
/// scatter and jitter. The seam is invisible by construction, not by blending.
/// </para>
/// <para>
/// <b>It lives on the stroke, not on the scene</b>, for the reason
/// <see cref="SymmetryAxis"/> does: it reaches pixels (invariant 4), so a mark
/// carries the tiling it was drawn under and turning the mode off later never
/// changes art already made. The rectangle is stored whole rather than taken
/// from the scene at render time, so a canvas resized afterwards still renders
/// the old marks about the tile they were drawn for.
/// </para>
/// <para>
/// <b>Absent unless used.</b> <c>Stroke.Wrap</c> is nullable and the serializer
/// ignores nulls, so a document painted without tiling grows no <c>wrap</c>
/// key; every derived member here is <c>[JsonIgnore]</c> so none reintroduces
/// one under another name.
/// </para>
/// <para>
/// Rectangular only. A brick stagger — the horizontal neighbour carrying a
/// half-tile vertical shift — is one extra field when it is wanted, and the
/// offsets below are the one place it would go.
/// </para>
/// </remarks>
public sealed class TileWrap
{
    /// <summary>
    /// Keys a newer build wrote that this one does not know, carried through
    /// untouched so a save here does not drop them (Q230). Null unless a file
    /// supplied some; never edited, so a clone may share it.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    /// <summary>The tile's left edge, in document coordinates.</summary>
    public double Left { get; set; }

    /// <summary>The tile's top edge, in document coordinates.</summary>
    public double Top { get; set; }

    /// <summary>The tile's width; a copy comes in this far along from where a mark left.</summary>
    public double Width { get; set; }

    /// <summary>The tile's height.</summary>
    public double Height { get; set; }

    /// <summary>The tile a scene's whole page makes — what a stroke painted with tiling on records.</summary>
    public static TileWrap OfScene(Scene scene) => new()
    {
        Left = scene.Left, Top = scene.Top, Width = scene.Width, Height = scene.Height,
    };

    /// <summary>
    /// True when the tile cannot wrap anything, so a caller skips it entirely.
    /// </summary>
    [JsonIgnore]
    public bool IsIdentity => !(Width > 0) || !(Height > 0) || !double.IsFinite(Width) || !double.IsFinite(Height);

    /// <summary>
    /// The translations a wrapped mark is stamped under besides where it was
    /// drawn: the tile's eight neighbours.
    /// </summary>
    /// <remarks>
    /// All eight, always. Which of them actually touch the page is decided
    /// where every copy is already bounded and clipped — <c>SegmentBounds</c>
    /// returns null for a copy that misses the document and the stamp returns
    /// at once — so a stroke in the middle of the tile costs eight rectangle
    /// tests and nothing else, and a stroke at a corner costs the three copies
    /// that land. Culling here instead would have to agree with that clipping
    /// to the pixel, and two answers to "does this copy land" is the bug
    /// charter O5 describes.
    /// </remarks>
    public (double Dx, double Dy)[] Offsets() => OffsetsFor(null);

    /// <summary>
    /// The translations for a mark whose points are <paramref name="points"/>:
    /// every tile the mark reaches into, plus one, so a stroke that travels
    /// more than a page away from the tile still comes back onto it.
    /// </summary>
    /// <remarks>
    /// A stroke begun in the left neighbour of the tiled preview and dragged
    /// across to the right one is recorded from about <c>x = 0</c> to past
    /// <c>2W</c>. The fixed 3×3 ring has no copy that brings the part beyond
    /// <c>2W</c> back, so it rendered nowhere while the preview showed it
    /// would. The range here comes from the points alone — never from the
    /// page, so the clipping still owns "does this copy land" — and is one
    /// tile generous each way for a brush's reach. A mark inside the tile
    /// gets exactly the eight neighbours.
    /// </remarks>
    public (double Dx, double Dy)[] OffsetsFor(IReadOnlyList<StrokePoint>? points)
    {
        if (IsIdentity) return [];
        var kxMin = -1;
        var kxMax = 1;
        var kyMin = -1;
        var kyMax = 1;
        if (points is { Count: > 0 })
        {
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            kxMin = Math.Min(-1, Tile(minX, Left, Width) - 1);
            kxMax = Math.Max(1, Tile(maxX, Left, Width) + 1);
            kyMin = Math.Min(-1, Tile(minY, Top, Height) - 1);
            kyMax = Math.Max(1, Tile(maxY, Top, Height) + 1);
        }

        var offsets = new List<(double, double)>((kxMax - kxMin + 1) * (kyMax - kyMin + 1) - 1);
        for (var ky = kyMin; ky <= kyMax; ky++)
        {
            for (var kx = kxMin; kx <= kxMax; kx++)
            {
                if (kx == 0 && ky == 0) continue;
                // Tile k's content comes onto the page translated by −k tiles.
                offsets.Add((-kx * Width, -ky * Height));
            }
        }
        return offsets.ToArray();
    }

    /// <summary>Which tile along one axis a coordinate falls in; 0 is the page, bounded so a wild coordinate cannot ask for millions.</summary>
    private static int Tile(double v, double origin, double size)
    {
        var k = Math.Floor((v - origin) / size);
        if (!double.IsFinite(k)) return 0;
        return (int)Math.Clamp(k, -MaxTilesAway, MaxTilesAway);
    }

    /// <summary>How far from the page a stroke's copies are followed, in tiles. The preview is one tile wide; this is room past it.</summary>
    public const int MaxTilesAway = 4;

    /// <summary>A copy, so editing a tile never reaches a stroke already painted.</summary>
    public TileWrap Clone() => new() { Unknown = Unknown, Left = Left, Top = Top, Width = Width, Height = Height };
}
