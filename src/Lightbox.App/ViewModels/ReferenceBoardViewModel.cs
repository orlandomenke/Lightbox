using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
using SkiaSharp;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The reference board behind its window: what is pinned up, where it sits, and
/// where the arrangement is kept.
/// </summary>
/// <remarks>
/// <para>
/// <b>All the board's decisions live here rather than in the window</b>, so the
/// arrangement, the auto-load and the z-order can be tested without a pointer.
/// The window's job is the pointer and nothing else.
/// </para>
/// <para>
/// <b>Two storages, one record</b> (Q87). A document in a project reads and
/// writes the board filed on its scope (<see cref="ProjectBoards"/>) — one wall
/// per subject, shared by every animation under it, which is the arrangement
/// persisting rather than being rebuilt every session. A loose document keeps
/// its own in <see cref="Doc.ReferenceBoard"/> with its pictures embedded,
/// because there is no project directory to copy an import into.
/// </para>
/// <para>
/// <b>A sheet view is named, never copied.</b> Its tile carries no pixels: the
/// picture comes from <see cref="MainViewModel.RenderReferenceViewPng(ReferenceView)"/>,
/// the same definition of "the view as a picture" the AI payloads and the taped
/// strip use, so an edit in the sheet's tab shows on the wall without a reimport.
/// Only files an artist brought in from outside are stored as pixels, since
/// there is nothing live behind them.
/// </para>
/// </remarks>
public sealed class ReferenceBoardViewModel
{
    private readonly MainViewModel _vm;

    public ReferenceBoardViewModel(MainViewModel vm)
    {
        _vm = vm;
        Board = LoadBoard();
    }

    /// <summary>The record: tiles back to front. See <see cref="ReferenceBoard"/>.</summary>
    public ReferenceBoard Board { get; private set; }

    /// <summary>Something on the wall moved, arrived or left.</summary>
    public event Action? Changed;

    public IReadOnlyList<BoardTile> Tiles => Board.Tiles;

    /// <summary>The project this board belongs to, or null for a loose document.</summary>
    private Project? Project => _vm.ProjectDocker.Project;

    /// <summary>The document whose scope owns the board, or null.</summary>
    private DocumentRef? Source => (_vm.SaveTargetTab ?? _vm.ActiveTab)?.Source;

    /// <summary>
    /// The folder whose wall is on screen — the nearest at or above this
    /// document that has one — and where a change to it is written.
    /// </summary>
    /// <remarks>
    /// Read and write are the same scope on purpose (B361). Writing to the
    /// document's own folder while reading an inherited wall would make the
    /// first pin appear to erase everything else on it.
    /// </remarks>
    private ProjectFolder? Scope =>
        Project is { } project ? ProjectBoards.OwnerOf(project, Source) : null;

    /// <summary>What the wall on screen belongs to, for the board's own toolbar.</summary>
    public string OwnerLabel => Project is null
        ? "This document"
        : Scope?.Name ?? "The whole project";

    /// <summary>Whether re-filing is on offer at all — a loose document has one wall.</summary>
    public bool CanChooseOwner => Project is not null;

    /// <summary>
    /// Every folder this document could hang its wall on, nearest first, ending
    /// with the project-wide wall.
    /// </summary>
    public IReadOnlyList<(string Label, ProjectFolder? Folder, bool IsCurrent)> OwnerChoices
    {
        get
        {
            if (Project is not { } project) return [];
            var current = Scope;
            var choices = new List<(string, ProjectFolder?, bool)>();
            foreach (var folder in ProjectBoards.OwnerChoicesFor(project.Manifest, Source))
            {
                choices.Add((folder?.Name ?? "The whole project", folder, folder?.Id == current?.Id));
            }
            return choices;
        }
    }

    /// <summary>
    /// Hang this wall on <paramref name="folder"/> instead.
    /// </summary>
    /// <remarks>
    /// The pictures come with it, which is the whole point: an artist splitting
    /// a shared wall into one per character is separating what they have, not
    /// throwing it away and starting again. Moving it further up deletes the
    /// walls in between, or the nearer one would keep winning and the choice
    /// would look ignored (B361).
    /// </remarks>
    public void SetOwner(ProjectFolder? folder)
    {
        if (Project is not { } project) return;
        ProjectBoards.SetOwner(project, Source, folder, Board);
        Board = LoadBoard();
        Changed?.Invoke();
    }

    private ReferenceBoard LoadBoard()
    {
        if (Project is { } project) return ProjectBoards.Load(project, Scope);
        return (_vm.SaveTargetTab ?? _vm.ActiveTab)?.Doc.ReferenceBoard
               ?? _vm.Doc.ReferenceBoard
               ?? new ReferenceBoard();
    }

    // ---- what could be pinned up ------------------------------------------------

    /// <summary>
    /// Every reference view this document can consult, sheet by sheet — what the
    /// board auto-loads and what the sheets menu offers.
    /// </summary>
    public IReadOnlyList<(ReferenceSheet Sheet, ReferenceView View)> AvailableViews =>
        [.. _vm.ReferenceSheetsView.SelectMany(s => s.Views.Select(v => (s, v)))];

    /// <summary>The views not on the wall yet — the sheets menu.</summary>
    public IReadOnlyList<(ReferenceSheet Sheet, ReferenceView View)> UnpinnedViews =>
        [.. AvailableViews.Where(p => Board.TileForView(p.View.Id) is null)];

    // ---- filling the wall ---------------------------------------------------------

    /// <summary>
    /// Pin up every reference sheet in scope, and lay the wall out when it had no
    /// arrangement of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>New views are added; nothing already placed is moved.</b> Opening the
    /// board must not undo the arrangement an artist made last week — that is the
    /// whole reason the file exists — so the tidy-up only runs when every tile is
    /// new, which is to say when there was no board.
    /// </para>
    /// <para>
    /// <b>A tile whose view has gone is dropped.</b> A sheet deleted from the
    /// project leaves a tile that can never render again; keeping it would put a
    /// permanent hole in the wall.
    /// </para>
    /// </remarks>
    public void AutoLoad(double viewWidth, double viewHeight)
    {
        _ = RefreshDocumentTilesAsync();
        var available = AvailableViews;
        var known = available.Select(p => p.View.Id).ToHashSet(StringComparer.Ordinal);
        var wasEmpty = Board.Tiles.Count == 0;

        // Views that have gone: only view tiles are checked, since an imported
        // image answers to nothing in the document.
        Board.Tiles.RemoveAll(t => t.IsView && !known.Contains(t.ViewId!));

        foreach (var (sheet, view) in available)
        {
            if (Board.TileForView(view.Id) is { } existing)
            {
                existing.Name = TileName(sheet, view);
                continue;
            }
            var (width, height) = BoardLayout.StartingSize(view.Width, view.Height);
            var (x, y) = BoardLayout.NextFreeSpot(Board.Tiles);
            Board.Tiles.Add(new BoardTile
            {
                Name = TileName(sheet, view),
                SheetId = sheet.Id,
                ViewId = view.Id,
                X = x,
                Y = y,
                Width = width,
                Height = height,
            });
        }

        if (wasEmpty) BoardLayout.Arrange(Board.Tiles, viewWidth, viewHeight);
        Save();
        Changed?.Invoke();
    }

    private static string TileName(ReferenceSheet sheet, ReferenceView view) =>
        $"{sheet.Name} · {view.Name}";

    /// <summary>Pin one view up, wherever there is room. Null when it is already up.</summary>
    /// <inheritdoc cref="AddImageFile(string, ValueTuple{double,double}?)" path="/param[@name='at']"/>
    public BoardTile? Pin(ReferenceSheet sheet, ReferenceView view, (double X, double Y)? at = null)
    {
        if (Board.TileForView(view.Id) is not null) return null;
        var (width, height) = BoardLayout.StartingSize(view.Width, view.Height);
        var (x, y) = at is { } spot
            ? (spot.X - width / 2, spot.Y - height / 2)
            : BoardLayout.NextFreeSpot(Board.Tiles);
        var tile = new BoardTile
        {
            Name = TileName(sheet, view),
            SheetId = sheet.Id,
            ViewId = view.Id,
            X = x,
            Y = y,
            Width = width,
            Height = height,
        };
        Board.Tiles.Add(tile);
        Save();
        Changed?.Invoke();
        return tile;
    }

    // ---- images from outside --------------------------------------------------------

    /// <summary>
    /// Pin up an image file from disk. Null when the file is not an image.
    /// </summary>
    /// <remarks>
    /// In a project the file is <em>copied in</em> and the tile points at the copy;
    /// on a loose document it is embedded as PNG, because there is nowhere to copy
    /// it to. Either way the board never points at where the artist happened to
    /// keep the original — a reference that breaks when a downloads folder is
    /// emptied breaks silently, and you find out while drawing against a blank.
    /// </remarks>
    /// <param name="at">
    /// Where on the board to put it, in board coordinates — where it was dropped,
    /// or the middle of what the artist is looking at. Null falls back to below
    /// everything already up.
    /// </param>
    /// <remarks>
    /// <b>The placement is the caller's, and that is the whole of B245.</b> Every
    /// import used to land under the bottom-most tile, which on a wall that had
    /// been auto-arranged to fill the window is <em>below the visible area</em>:
    /// the picture arrived, the file was copied, nothing appeared, and the only
    /// way to find it was to pan down. A picture goes where you put it.
    /// </remarks>
    public BoardTile? AddImageFile(string path, (double X, double Y)? at = null)
    {
        SKBitmap? decoded;
        try
        {
            decoded = SKBitmap.Decode(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        if (decoded is null) return null;
        using (decoded)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var png = Lightbox.Raster.PngCodec.Encode(decoded);
            if (Project is { } project)
            {
                // By path first, which keeps the original file's own format; by
                // its decoded bytes when the name says nothing (B282), so a
                // browser's nameless temporary is still *copied into* the
                // project rather than quietly embedded in the board file.
                var relative = ProjectBoards.ImportImage(project, path)
                    ?? ProjectBoards.ImportImage(
                        project, Convert.FromBase64String(png), $"{ProjectIo.Slug(name)}.png");
                if (relative is not null)
                {
                    return AddTile(name, decoded.Width, decoded.Height, at, tile =>
                    {
                        tile.Path = relative;
                        tile.Origin = path;
                    });
                }
            }
            return AddTile(name, decoded.Width, decoded.Height, at, tile =>
            {
                tile.Png = png;
                tile.Origin = path;
            });
        }
    }

    /// <summary>
    /// Pin up bytes that came from somewhere with no file — a picture dragged out
    /// of a browser. Null when the bytes are not an image.
    /// </summary>
    /// <inheritdoc cref="AddImageFile(string, ValueTuple{double,double}?)" path="/param[@name='at']"/>
    public BoardTile? AddImageBytes(string name, byte[] bytes, (double X, double Y)? at = null)
    {
        // Through a codec rather than Decode(byte[]), for the reason
        // ImportReferenceImageBytes gives: the byte[] overload throws on bytes no
        // codec recognises, where this returns null for "not a picture".
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) return null;
        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null) return null;

        var png = Lightbox.Raster.PngCodec.Encode(decoded);
        if (Project is { } project
            && ProjectBoards.ImportImage(project, Convert.FromBase64String(png), $"{name}.png") is { } relative)
        {
            return AddTile(name, decoded.Width, decoded.Height, at, tile => tile.Path = relative);
        }
        return AddTile(name, decoded.Width, decoded.Height, at, tile => tile.Png = png);
    }

    /// <summary>
    /// Pin up another Lightbox document as a column of its frames, flattened
    /// (Q284–Q286). The source is read and never written; the tile remembers
    /// it in <see cref="BoardTile.Origin"/> and refreshes when it is newer.
    /// The tile, or null with a reason. The flatten runs off the UI thread.
    /// </summary>
    /// <inheritdoc cref="AddImageFile(string, ValueTuple{double,double}?)" path="/param[@name='at']"/>
    public async Task<(BoardTile? Tile, string? Error)> AddDocumentAsync(string path, (double X, double Y)? at = null)
    {
        var full = Path.GetFullPath(path);
        var (flat, error) = await Task.Run(() => MainViewModel.FlattenQuietly(full));
        if (flat is null) return (null, error);
        using (flat)
        {
            var name = MainViewModel.DocumentName(full);
            var png = Lightbox.Raster.PngCodec.Encode(flat.Sheet);
            if (Project is { } project
                && ProjectBoards.ImportImage(project, Convert.FromBase64String(png), $"{ProjectIo.Slug(name)}.png") is { } relative)
            {
                return (AddTile(name, flat.Sheet.Width, flat.Sheet.Height, at, tile =>
                {
                    tile.Path = relative;
                    tile.Origin = full;
                    tile.DocumentStamp = flat.Stamp;
                }), null);
            }
            return (AddTile(name, flat.Sheet.Width, flat.Sheet.Height, at, tile =>
            {
                tile.Png = png;
                tile.Origin = full;
                tile.DocumentStamp = flat.Stamp;
            }), null);
        }
    }

    /// <summary>Tiles being flattened right now, so a second trigger does not start a second render.</summary>
    private readonly HashSet<string> _flatteningTiles = new(StringComparer.Ordinal);

    /// <summary>
    /// Re-flatten every document tile whose source is newer than its picture.
    /// Returns how many changed; a source that has gone leaves its picture up.
    /// </summary>
    /// <remarks>
    /// The picture is rewritten in place — the same copied file, or the same
    /// embedded PNG — so the tile keeps its id, its place on the wall and any
    /// projection of it. The width stays where the artist dragged it and the
    /// height follows the picture, which grows when the run gains a drawing.
    /// </remarks>
    public async Task<int> RefreshDocumentTilesAsync()
    {
        var stale = Board.Tiles
            .Where(t => t.IsDocument && !_flatteningTiles.Contains(t.Id)
                && Services.DocumentReference.IsLocal(t.Origin!) && File.Exists(t.Origin)
                && Services.DocumentReference.StampOf(t.Origin!) != t.DocumentStamp)
            .ToList();
        if (stale.Count == 0) return 0;

        foreach (var tile in stale) _flatteningTiles.Add(tile.Id);
        List<Services.FlattenedDocument?> flats;
        try
        {
            flats = await Task.Run(() => stale.Select(t => MainViewModel.FlattenQuietly(t.Origin!).Flat).ToList());
        }
        finally
        {
            foreach (var tile in stale) _flatteningTiles.Remove(tile.Id);
        }

        var changed = 0;
        for (var i = 0; i < stale.Count; i++)
        {
            if (flats[i] is not { } flat) continue;
            using (flat)
            {
                var tile = stale[i];
                var png = Lightbox.Raster.PngCodec.Encode(flat.Sheet);
                if (tile.IsFile)
                {
                    if (Project is not { } project || !RewriteDocumentPicture(project, tile, png)) continue;
                }
                else
                {
                    tile.Png = png;
                }
                tile.DocumentStamp = flat.Stamp;
                if (flat.Sheet.Width > 0) tile.Height = tile.Width * flat.Sheet.Height / flat.Sheet.Width;
                changed++;
            }
        }
        if (changed > 0)
        {
            Save();
            Changed?.Invoke();
        }
        return changed;
    }

    /// <summary>
    /// Put a document tile's new picture where its old one was — only if that
    /// is a PNG in the project's references folder, which is the only kind of
    /// file this feature ever writes. Anything else gets a fresh copy there and
    /// the tile is pointed at it.
    /// </summary>
    /// <remarks>
    /// <b>A board file is data, not a licence to write.</b> Its tile paths are
    /// plain JSON: a received project or a hand edit can point a document
    /// tile's <see cref="BoardTile.Path"/> at a drawing, the manifest, a PSD —
    /// and a refresh that wrote where the tile said would replace that file
    /// with a picture, with no prompt and nothing to undo. Written beside and
    /// moved over, so a crash mid-write cannot leave a truncated copy where the
    /// last good picture was.
    /// </remarks>
    private static bool RewriteDocumentPicture(Project project, BoardTile tile, string png)
    {
        var bytes = Convert.FromBase64String(png);
        var target = ProjectBoards.ResolveImage(project, tile);
        var folder = ProjectIo.ResolveInProject(project, ProjectBoards.ImagesDir);
        var ours = target is not null && folder is not null
            && string.Equals(Path.GetExtension(target), ".png", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                Path.GetFullPath(Path.GetDirectoryName(target)!).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        if (!ours)
        {
            if (ProjectBoards.ImportImage(project, bytes, $"{ProjectIo.Slug(tile.Name)}.png") is not { } fresh) return false;
            tile.Path = fresh;
            return true;
        }
        var temp = target + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, target!, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            return false;
        }
    }

    private BoardTile AddTile(
        string name, int pixelWidth, int pixelHeight, (double X, double Y)? at, Action<BoardTile> source)
    {
        var (width, height) = BoardLayout.StartingSize(pixelWidth, pixelHeight);
        // Centred on the point it was put at, the way anything dragged onto
        // anything lands under the cursor rather than beside it.
        var (x, y) = at is { } spot
            ? (spot.X - width / 2, spot.Y - height / 2)
            : BoardLayout.NextFreeSpot(Board.Tiles);
        var tile = new BoardTile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Reference" : name,
            X = x,
            Y = y,
            Width = width,
            Height = height,
        };
        source(tile);
        Board.Tiles.Add(tile);
        Save();
        Changed?.Invoke();
        return tile;
    }

    // ---- arranging ------------------------------------------------------------------

    /// <summary>Lay every tile out neatly in the space there is.</summary>
    public void Arrange(double viewWidth, double viewHeight)
    {
        BoardLayout.Arrange(Board.Tiles, viewWidth, viewHeight);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Move a tile — the drag, recorded.</summary>
    public void MoveTo(BoardTile tile, double x, double y)
    {
        tile.X = x;
        tile.Y = y;
        Save();
        Changed?.Invoke();
    }

    /// <summary>Resize a tile, keeping its aspect — the corner drag, recorded.</summary>
    public void ResizeTo(BoardTile tile, double width)
    {
        var aspect = tile.Aspect;
        tile.Width = Math.Max(32, width);
        tile.Height = Math.Max(24, tile.Width / aspect);
        Save();
        Changed?.Invoke();
    }

    public void BringToFront(BoardTile tile)
    {
        if (!Board.BringToFront(tile)) return;
        Save();
        Changed?.Invoke();
    }

    public void SendToBack(BoardTile tile)
    {
        if (!Board.SendToBack(tile)) return;
        Save();
        Changed?.Invoke();
    }

    /// <summary>
    /// Take a tile off the wall. The copied file stays in the project — deleting
    /// an image another board may be showing is a separate, said-out-loud choice.
    /// </summary>
    public void Remove(BoardTile tile)
    {
        if (!Board.Tiles.Remove(tile)) return;
        Save();
        Changed?.Invoke();
    }

    // ---- pictures ----------------------------------------------------------------

    /// <summary>
    /// The tile's picture as PNG bytes, or null when it cannot be rendered — a
    /// view that has gone, or a copied file somebody deleted from the project.
    /// </summary>
    public byte[]? PixelsFor(BoardTile tile)
    {
        if (tile.IsView)
        {
            if (FindView(tile.ViewId!) is not { } view) return null;
            try
            {
                return Convert.FromBase64String(_vm.RenderReferenceViewPng(view));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
        if (tile.IsEmbedded) return Convert.FromBase64String(tile.Png!);
        if (Project is { } project && ProjectBoards.ResolveImage(project, tile) is { } path && File.Exists(path))
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>The live view behind a view tile, or null when it has gone.</summary>
    public ReferenceView? FindView(string viewId) =>
        _vm.ReferenceSheetsView.SelectMany(s => s.Views).FirstOrDefault(v => v.Id == viewId);

    // ---- keeping it -----------------------------------------------------------------

    /// <summary>
    /// Write the arrangement where this document's board is kept.
    /// </summary>
    /// <remarks>
    /// Written on every change rather than on close: the board is small, the
    /// write is atomic, and an arrangement lost to a crash is exactly the friction
    /// this feature exists to remove.
    /// </remarks>
    public void Save()
    {
        if (Project is { } project)
        {
            ProjectBoards.Save(project, Scope, Board);
            return;
        }
        // Loose document: the board travels inside it, and stays absent while
        // the wall is empty — the ordinary "optional means absent" rule.
        var doc = (_vm.SaveTargetTab ?? _vm.ActiveTab)?.Doc ?? _vm.Doc;
        doc.ReferenceBoard = Board.Tiles.Count == 0 ? null : Board;
    }
}
