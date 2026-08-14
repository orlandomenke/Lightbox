namespace Lightbox.Core.Documents;

/// <summary>
/// A wall of reference, arranged by hand: the pinboard beside the easel rather
/// than a viewer for one drawing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The board is an arrangement, not a copy of the art.</b> A tile pointing at
/// a reference view holds no pixels — it names the view and is rendered from it,
/// so a sheet edited in its tab changes on the board without anything being
/// re-imported. That is what keeps the board from becoming a second, stale copy
/// of every sheet in the project (the B133 failure, in a new place).
/// </para>
/// <para>
/// <b>Z-order is the list order, back to front.</b> There is no <c>Z</c> key,
/// because two tiles could then claim the same number and the file would not say
/// which is in front. Bringing a tile forward moves it to the end of the list,
/// which is one operation with one meaning and survives a round trip exactly.
/// </para>
/// <para>
/// <b>Where a board lives</b> is <see cref="Projects.ProjectBoards"/>' business:
/// in a project it is a sidecar filed on the scope that owns the references, and
/// a loose document keeps its own in <see cref="Doc.ReferenceBoard"/>. The record
/// is the same either way — only the storage differs, and only because a loose
/// file has no project directory to copy an image into.
/// </para>
/// </remarks>
public sealed class ReferenceBoard
{
    public string Id { get; set; } = Ids.NewId("board");

    /// <summary>Tiles, back to front. See the class remarks on z-order.</summary>
    public List<BoardTile> Tiles { get; set; } = [];

    /// <summary>A copy holding no reference in common with this one.</summary>
    public ReferenceBoard Clone()
    {
        var copy = (ReferenceBoard)MemberwiseClone();
        copy.Tiles = Tiles.Select(t => t.Clone()).ToList();
        return copy;
    }

    /// <summary>The tile for a reference view, or null when it is not pinned up.</summary>
    public BoardTile? TileForView(string viewId) =>
        Tiles.FirstOrDefault(t => t.ViewId == viewId);

    /// <summary>Put a tile in front of every other one.</summary>
    /// <returns>Whether anything moved — it is already in front, or not here.</returns>
    public bool BringToFront(BoardTile tile)
    {
        var at = Tiles.IndexOf(tile);
        if (at < 0 || at == Tiles.Count - 1) return false;
        Tiles.RemoveAt(at);
        Tiles.Add(tile);
        return true;
    }

    /// <summary>Put a tile behind every other one.</summary>
    public bool SendToBack(BoardTile tile)
    {
        var at = Tiles.IndexOf(tile);
        if (at <= 0) return false;
        Tiles.RemoveAt(at);
        Tiles.Insert(0, tile);
        return true;
    }
}

/// <summary>
/// One image on the board: where it sits, how big it is, and where its picture
/// comes from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three sources, exactly one of them set.</b> A tile is either a reference
/// view (<see cref="SheetId"/> and <see cref="ViewId"/>), a file copied into the
/// project (<see cref="Path"/>), or — only on a document with no project behind
/// it — an embedded picture (<see cref="Png"/>). <see cref="IsView"/> and the
/// two beside it are how to ask; every unused key is null and writes nothing.
/// </para>
/// <para>
/// <b>An imported image is copied, never linked.</b> A path into somebody's
/// downloads folder is a reference that breaks the moment the file moves, and it
/// breaks silently — you find out when you are drawing against a blank. The copy
/// lives in the project beside the art it is reference for; <see cref="Origin"/>
/// remembers where it came from, for the artist rather than for the loader.
/// </para>
/// </remarks>
public sealed class BoardTile
{
    public string Id { get; set; } = Ids.NewId("tile");

    /// <summary>What the board labels it — the view's name, or the file's.</summary>
    public string Name { get; set; } = "";

    /// <summary>The sheet a view tile belongs to; null on an image tile.</summary>
    public string? SheetId { get; set; }

    /// <summary>The view a view tile shows; null on an image tile.</summary>
    public string? ViewId { get; set; }

    /// <summary>Project-relative path of a copied image; null otherwise.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// The picture as base64 PNG — the loose-document fallback only, where there
    /// is no project directory to copy a file into.
    /// </summary>
    public string? Png { get; set; }

    /// <summary>Where an imported image came from, kept so the artist can tell.</summary>
    public string? Origin { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 320;

    public double Height { get; set; } = 240;

    /// <summary>A tile rendered from a live reference view.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsView => ViewId is { Length: > 0 };

    /// <summary>A tile showing a file copied into the project.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFile => Path is { Length: > 0 };

    /// <summary>A tile carrying its own pixels — a loose document's import.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmbedded => Png is { Length: > 0 };

    /// <summary>Width over height, never zero — what the arranger preserves.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double Aspect => Height > 0 && Width > 0 ? Width / Height : 1.0;

    public BoardTile Clone() => (BoardTile)MemberwiseClone();
}
