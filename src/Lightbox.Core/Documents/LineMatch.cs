namespace Lightbox.Core.Documents;

/// <summary>
/// One piece of one stroke: <paramref name="Stroke"/> is the stroke's id, and
/// <paramref name="Piece"/> counts the pieces it is cut into for matching, from
/// its start (<c>LinePieces.Cut</c>). A stroke nothing meets is one piece, 0.
/// </summary>
public sealed record PieceRef(string Stroke, int Piece);

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
    public List<PieceRef> A { get; set; } = [];

    public List<PieceRef> B { get; set; } = [];

    public LineMatch Clone() => new() { A = [.. A], B = [.. B] };
}
