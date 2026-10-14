using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lightbox.Core.Documents;

/// <summary>
/// One piece of one stroke: <paramref name="Stroke"/> is the stroke's id, and
/// <paramref name="Piece"/> counts the pieces it is cut into for matching, from
/// its start (<c>LinePieces.Cut</c>). A stroke nothing meets is one piece, 0.
/// </summary>
/// <remarks>A value, so it is compared by what it names; a struct for the reason a point is one — a fixed shape, no room for a newer build's keys.</remarks>
public readonly record struct PieceRef(string Stroke, int Piece);

/// <summary>
/// The artist's word that these lines of key A and these lines of key B are
/// the same part of the drawing (Q271): which, not how — the engine works out
/// the order and the direction.
/// </summary>
/// <remarks>
/// Kept on key A for the gap to the next key, the way the timing chart is. It
/// names pieces by stroke id rather than by position, so redrawing one line
/// leaves every other match standing; a match naming a line that is gone is
/// dropped when it is read, never guessed at. Either side may be empty: a line
/// that appears, or one that goes.
/// </remarks>
public sealed class LineMatch
{
    /// <summary>
    /// Keys a newer build wrote that this one does not know, carried through
    /// untouched (Q230). Null unless a file supplied some; never edited, so a
    /// clone may share it.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }

    public List<PieceRef> A { get; set; } = [];

    public List<PieceRef> B { get; set; } = [];

    /// <summary>
    /// The artist's word on whether this part is one solid object — a shoe, a
    /// cuff — that moves, turns and scales as a whole (true), or lines that bend
    /// (false). Null, and no key in the file, leaves it to the engine, which
    /// treats a part of many small lines as solid (Q271).
    /// </summary>
    public bool? Solid { get; set; }

    public LineMatch Clone() => new() { Unknown = Unknown, A = [.. A], B = [.. B], Solid = Solid };
}
