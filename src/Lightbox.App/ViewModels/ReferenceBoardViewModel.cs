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

    private ProjectFolder? Scope =>
        Project is { } project ? ProjectBoards.ScopeOf(project.Manifest, Source) : null;

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
    public BoardTile? Pin(ReferenceSheet sheet, ReferenceView view)
    {
        if (Board.TileForView(view.Id) is not null) return null;
        var (width, height) = BoardLayout.StartingSize(view.Width, view.Height);
        var (x, y) = BoardLayout.NextFreeSpot(Board.Tiles);
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
    public BoardTile? AddImageFile(string path)
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
            if (Project is { } project && ProjectBoards.ImportImage(project, path) is { } relative)
            {
                return AddTile(name, decoded.Width, decoded.Height, tile =>
                {
                    tile.Path = relative;
                    tile.Origin = path;
                });
            }
            var png = Lightbox.Raster.PngCodec.Encode(decoded);
            return AddTile(name, decoded.Width, decoded.Height, tile =>
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
    public BoardTile? AddImageBytes(string name, byte[] bytes)
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
            return AddTile(name, decoded.Width, decoded.Height, tile => tile.Path = relative);
        }
        return AddTile(name, decoded.Width, decoded.Height, tile => tile.Png = png);
    }

    private BoardTile AddTile(string name, int pixelWidth, int pixelHeight, Action<BoardTile> source)
    {
        var (width, height) = BoardLayout.StartingSize(pixelWidth, pixelHeight);
        var (x, y) = BoardLayout.NextFreeSpot(Board.Tiles);
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
