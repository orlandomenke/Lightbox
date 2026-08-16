using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;

namespace Lightbox.App.Views;

/// <summary>
/// The reference board: a wall of reference beside the easel, arranged by hand.
/// </summary>
/// <remarks>
/// <para>
/// <b>What replaced the single-view window</b> (Q87). Q69 shipped one window per
/// reference view — live, and correct as far as it went. An artist works from
/// several references at once, so that meant several windows, each framing one
/// picture and none of them arrangeable against the others. This is the same
/// promise with the arrangement as the feature: every sheet in scope pinned up
/// automatically, imported pictures beside them, laid out to fit and remembered
/// between sessions.
/// </para>
/// <para>
/// <b>Live where it can be.</b> A tile that names a reference view re-renders on
/// the edit funnel, and skips the work when the picture did not change — the same
/// compare-and-skip the old window and the taped-on-canvas strip do, because a
/// window that rebuilds a bitmap on every keystroke anywhere flickers.
/// </para>
/// <para>
/// <b>The pointer, and only the pointer, lives here.</b> Everything the board
/// <em>is</em> — which tiles, where, in what order, and where that is written —
/// is <see cref="ReferenceBoardViewModel"/>, so it can be tested without one.
/// </para>
/// </remarks>
public sealed class ReferenceBoardWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Canvas _surface = new();
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TranslateTransform _pan = new();
    private readonly Dictionary<string, (byte[] Bytes, Bitmap Picture)> _pictures = new(StringComparer.Ordinal);
    private readonly Button _sheetsButton = new() { Content = "Sheets ▾", Classes = { "tertiary" } };

    /// <summary>Where a tile grab counts as the resize corner rather than a move.</summary>
    private const double CornerGrip = 16;

    private BoardTile? _dragging;
    private Point _grabbedAt;
    private double _grabbedX;
    private double _grabbedY;
    private double _grabbedWidth;
    private bool _resizing;
    private bool _panning;
    private Point _panFrom;

    public ReferenceBoardViewModel BoardModel { get; }

    /// <summary>The tiles' host — the thing tests measure and the pointer moves over.</summary>
    public Canvas Surface => _surface;

    /// <summary>
    /// The rebindable commands (Q87), or null in a test that does not care.
    /// </summary>
    private readonly Services.ShortcutMap? _shortcuts;

    /// <summary>The tile last touched — what a keyed reorder acts on.</summary>
    private BoardTile? _touched;

    public ReferenceBoardWindow(MainViewModel vm, Services.ShortcutMap? shortcuts = null)
    {
        _vm = vm;
        _shortcuts = shortcuts;
        BoardModel = new ReferenceBoardViewModel(vm);

        Title = "Reference board";
        ShowInTaskbar = false;
        Width = 900;
        Height = 640;

        _surface.Background = Brushes.Transparent;
        _surface.RenderTransform = new TransformGroup { Children = { _zoom, _pan } };
        _surface.RenderTransformOrigin = RelativePoint.TopLeft;

        var board = new Border
        {
            // Its own ground rather than the window's: reference is judged against
            // a neutral field, and a wall the colour of the chrome around it makes
            // every pale drawing on it read as lighter than it is.
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x20, 0x24)),
            ClipToBounds = true,
            Child = _surface,
        };
        Grid.SetRow(board, 1);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(Toolbar());
        layout.Children.Add(board);
        Content = layout;

        board.PointerPressed += OnPointerPressed;
        board.PointerMoved += OnPointerMoved;
        board.PointerReleased += OnPointerReleased;
        board.PointerWheelChanged += OnWheel;

        KeyDown += OnKeyDown;
        // On the window and on the surface both. AllowDrop is inherited, but the
        // board is the control a drop is actually over and the one whose
        // coordinates decide where the picture lands — saying so here is what
        // makes that independent of how inheritance happens to resolve.
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(board, true);
        DragDrop.SetAllowDrop(_surface, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        BoardModel.Changed += Redraw;
        _vm.DocumentEdited += OnDocumentEdited;
        Closed += (_, _) =>
        {
            _vm.DocumentEdited -= OnDocumentEdited;
            BoardModel.Changed -= Redraw;
            foreach (var (_, picture) in _pictures.Values) picture.Dispose();
            _pictures.Clear();
        };

        // Laid out against the window's own size rather than the surface's, which
        // has not been measured yet when the window is built.
        BoardModel.AutoLoad(Width, Height - 44);
        Redraw();
    }

    private Control Toolbar()
    {
        var arrange = new Button { Content = "Auto-arrange", Classes = { "tertiary" } };
        arrange.Click += (_, _) => ArrangeAndShow();

        var fit = new Button { Content = "Fit", Classes = { "tertiary" } };
        fit.Click += (_, _) => FitToView();

        var add = new Button { Content = "Add image…", Classes = { "tertiary" } };
        add.Click += async (_, _) => await AddImageAsync();

        _sheetsButton.Click += (_, _) => ShowSheetsMenu();

        var bar = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 6),
            Children = { arrange, fit, add, _sheetsButton },
        };
        Grid.SetRow(bar, 0);
        return bar;
    }

    private double BoardWidth => _surface.Bounds.Width > 1 ? _surface.Bounds.Width : Width;

    private double BoardHeight => _surface.Bounds.Height > 1 ? _surface.Bounds.Height : Height - 44;

    // ---- the board is bigger than the window ----------------------------------------

    /// <summary>
    /// A point on screen as a point on the board.
    /// </summary>
    /// <remarks>
    /// The board is not the window: it is panned and zoomed under it, and extends
    /// as far in every direction as an artist drags something. Every placement
    /// therefore goes through this rather than using window coordinates, or a
    /// picture dropped while panned would land wherever the wall happened to be
    /// scrolled to.
    /// </remarks>
    private Point ToBoard(Point onScreen) =>
        new((onScreen.X - _pan.X) / _zoom.ScaleX, (onScreen.Y - _pan.Y) / _zoom.ScaleY);

    /// <summary>The middle of what is on screen, in board coordinates.</summary>
    private Point ViewCentre => ToBoard(new Point(BoardWidth / 2, BoardHeight / 2));

    /// <summary>Tidy the wall, then look at it — the two halves of one button.</summary>
    /// <remarks>
    /// Arranging alone would lay the tiles out at the board's origin while the
    /// artist was looking somewhere else entirely, so the tidy-up would appear to
    /// have emptied the board.
    /// </remarks>
    private void ArrangeAndShow()
    {
        BoardModel.Arrange(BoardWidth, BoardHeight);
        FitToView();
    }

    /// <summary>
    /// Zoom and pan so everything on the board is on screen — the way back when a
    /// picture has been dragged off into the distance.
    /// </summary>
    public void FitToView()
    {
        if (BoardModel.Tiles.Count == 0)
        {
            _zoom.ScaleX = _zoom.ScaleY = 1;
            _pan.X = _pan.Y = 0;
            return;
        }

        var left = BoardModel.Tiles.Min(t => t.X);
        var top = BoardModel.Tiles.Min(t => t.Y);
        var right = BoardModel.Tiles.Max(t => t.X + t.Width);
        var bottom = BoardModel.Tiles.Max(t => t.Y + t.Height);

        const double margin = 24;
        var scale = Math.Min(
            (BoardWidth - margin * 2) / Math.Max(1, right - left),
            (BoardHeight - margin * 2) / Math.Max(1, bottom - top));
        // Never magnified: a wall of two small pictures blown up to fill the
        // window would be showing them at a size nobody chose.
        scale = Math.Clamp(scale, 0.05, 1);

        _zoom.ScaleX = _zoom.ScaleY = scale;
        _pan.X = (BoardWidth - (right - left) * scale) / 2 - left * scale;
        _pan.Y = (BoardHeight - (bottom - top) * scale) / 2 - top * scale;
    }

    // ---- the sheets menu ---------------------------------------------------------

    /// <summary>
    /// The sheets in scope that are not on the wall — how a reference taken down,
    /// or one belonging to a folder the artist has just filed work under, gets
    /// back up without retyping anything.
    /// </summary>
    private void ShowSheetsMenu()
    {
        var menu = new ContextMenu();
        var unpinned = BoardModel.UnpinnedViews;
        if (unpinned.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Every sheet is on the board", IsEnabled = false });
        }
        else
        {
            foreach (var (sheet, view) in unpinned)
            {
                var item = new MenuItem { Header = $"{sheet.Name} · {view.Name}" };
                item.Click += (_, _) => BoardModel.Pin(sheet, view);
                menu.Items.Add(item);
            }
        }
        menu.PlacementTarget = _sheetsButton;
        menu.Open(_sheetsButton);
    }

    // ---- drawing the wall ---------------------------------------------------------

    /// <summary>The control showing each tile, so a move can nudge one rather than rebuild all.</summary>
    private readonly Dictionary<string, Border> _frames = new(StringComparer.Ordinal);

    /// <summary>Rebuild the tiles. Cheap: a wall is tens of controls, not thousands.</summary>
    /// <remarks>
    /// <b>Pictures are not re-fetched here.</b> Rendering a view tile costs a
    /// compose, a PNG encode and a base64 — 52 ms for one 960×540 view, which is
    /// what B31 measured and cached — so doing it per rebuild would put that cost
    /// on every add, every reorder and every tidy-up. The cache answers unless
    /// something says the picture moved, which only <see cref="OnDocumentEdited"/>
    /// does.
    /// </remarks>
    private void Redraw()
    {
        _surface.Children.Clear();
        _frames.Clear();
        foreach (var tile in BoardModel.Tiles)
        {
            if (PictureFor(tile, refresh: false) is not { } picture) continue;
            var image = new Image
            {
                Source = picture,
                Stretch = Stretch.Fill,
                Width = tile.Width,
                Height = tile.Height,
                Tag = tile,
            };
            var frame = new Border
            {
                Width = tile.Width,
                Height = tile.Height,
                Child = image,
                Tag = tile,
                ContextMenu = MenuFor(tile),
            };
            Canvas.SetLeft(frame, tile.X);
            Canvas.SetTop(frame, tile.Y);
            _surface.Children.Add(frame);
            _frames[tile.Id] = frame;
        }
        ForgetPicturesOfTilesNoLongerUp();
    }

    /// <summary>
    /// Put one tile where its record says it is, without touching the rest.
    /// </summary>
    /// <remarks>
    /// What a drag runs, once per pointer event. The alternative — rebuilding the
    /// wall — is work proportional to how much is pinned up, in a live path, which
    /// is the charter's definition of a performance regression whatever the
    /// numbers happen to be today.
    /// </remarks>
    private void Reposition(BoardTile tile)
    {
        if (!_frames.TryGetValue(tile.Id, out var frame)) return;
        Canvas.SetLeft(frame, tile.X);
        Canvas.SetTop(frame, tile.Y);
        frame.Width = tile.Width;
        frame.Height = tile.Height;
        if (frame.Child is Image image)
        {
            image.Width = tile.Width;
            image.Height = tile.Height;
        }
    }

    /// <summary>Drop the bitmaps of tiles that have left the wall.</summary>
    private void ForgetPicturesOfTilesNoLongerUp()
    {
        var up = BoardModel.Tiles.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in _pictures.Keys.Where(id => !up.Contains(id)).ToList())
        {
            _pictures[id].Picture.Dispose();
            _pictures.Remove(id);
        }
    }

    private ContextMenu MenuFor(BoardTile tile)
    {
        var front = new MenuItem { Header = "Bring to front" };
        front.Click += (_, _) => BoardModel.BringToFront(tile);
        var back = new MenuItem { Header = "Send to back" };
        back.Click += (_, _) => BoardModel.SendToBack(tile);
        var remove = new MenuItem { Header = "Take off the board" };
        remove.Click += (_, _) => BoardModel.Remove(tile);

        var menu = new ContextMenu();
        menu.Items.Add(front);
        menu.Items.Add(back);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        return menu;
    }

    /// <summary>
    /// The tile's bitmap, decoded once and kept until its bytes change.
    /// </summary>
    /// <remarks>
    /// The comparison is what stops the churn: a view tile is re-rendered on every
    /// document edit, and most edits do not change that view at all. Replacing the
    /// bitmap anyway would flicker the whole wall on every stroke.
    /// </remarks>
    /// <param name="refresh">
    /// Whether to ask the model for the picture again. False answers from the
    /// cache when there is an entry, which is what keeps a rebuild cheap.
    /// </param>
    private Bitmap? PictureFor(BoardTile tile, bool refresh)
    {
        var known = _pictures.TryGetValue(tile.Id, out var cached);
        if (known && !refresh) return cached.Picture;

        var bytes = BoardModel.PixelsFor(tile);
        if (bytes is null)
        {
            if (known)
            {
                cached.Picture.Dispose();
                _pictures.Remove(tile.Id);
            }
            return null;
        }
        if (known && cached.Bytes.AsSpan().SequenceEqual(bytes)) return cached.Picture;

        using var stream = new MemoryStream(bytes);
        var picture = new Bitmap(stream);
        if (known) cached.Picture.Dispose();
        _pictures[tile.Id] = (bytes, picture);
        return picture;
    }

    /// <summary>
    /// A sheet was edited somewhere: re-render the tiles that show one, and
    /// rebuild only if a picture actually changed.
    /// </summary>
    /// <remarks>
    /// The compare is what stops the churn: the funnel fires for every edit
    /// anywhere, and most of them change no reference view at all. Rebuilding
    /// regardless would flicker the whole wall on every stroke.
    /// </remarks>
    private void OnDocumentEdited()
    {
        var moved = false;
        foreach (var tile in BoardModel.Tiles.Where(t => t.IsView))
        {
            var before = _pictures.TryGetValue(tile.Id, out var cached) ? cached.Picture : null;
            if (!ReferenceEquals(PictureFor(tile, refresh: true), before)) moved = true;
        }
        if (moved) Redraw();
    }

    // ---- the pointer -----------------------------------------------------------------

    /// <summary>
    /// Press: on a tile, raise it and start moving — near its bottom-right corner,
    /// resize instead. Anywhere else, pan the wall.
    /// </summary>
    /// <remarks>
    /// <b>Raising on press rather than on a menu</b> (Q87): reaching for a
    /// reference is how you say you want to look at it, and the alternative made
    /// the commonest action on a board take two gestures. Send to back stays on
    /// the menu, where a rarer and less reversible-looking action belongs.
    /// </remarks>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_surface);
        if (!point.Properties.IsLeftButtonPressed)
        {
            if (point.Properties.IsMiddleButtonPressed)
            {
                _panning = true;
                _panFrom = e.GetPosition(this);
            }
            return;
        }

        if (TileAt(point.Position) is not { } tile)
        {
            _panning = true;
            _panFrom = e.GetPosition(this);
            return;
        }

        _dragging = tile;
        _touched = tile;
        _grabbedAt = point.Position;
        _grabbedX = tile.X;
        _grabbedY = tile.Y;
        _grabbedWidth = tile.Width;
        _resizing = point.Position.X > tile.X + tile.Width - CornerGrip
                    && point.Position.Y > tile.Y + tile.Height - CornerGrip;
        BoardModel.BringToFront(tile);
        e.Pointer.Capture((Control?)sender);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_panning)
        {
            var now = e.GetPosition(this);
            _pan.X += now.X - _panFrom.X;
            _pan.Y += now.Y - _panFrom.Y;
            _panFrom = now;
            return;
        }
        if (_dragging is not { } tile) return;

        var at = e.GetPosition(_surface);
        if (_resizing)
        {
            tile.Width = Math.Max(32, _grabbedWidth + (at.X - _grabbedAt.X));
            tile.Height = Math.Max(24, tile.Width / tile.Aspect);
        }
        else
        {
            tile.X = _grabbedX + (at.X - _grabbedAt.X);
            tile.Y = _grabbedY + (at.Y - _grabbedAt.Y);
        }
        // One control moves; nothing is re-rendered and nothing is rebuilt.
        Reposition(tile);
    }

    /// <summary>
    /// Release: the arrangement is written once, at the end of the gesture rather
    /// than on every pointer event — the same rule the drawing side follows.
    /// </summary>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _panning = false;
        if (_dragging is not { } tile) return;
        _dragging = null;
        e.Pointer.Capture(null);
        if (_resizing) BoardModel.ResizeTo(tile, tile.Width);
        else BoardModel.MoveTo(tile, tile.X, tile.Y);
        _resizing = false;
    }

    /// <summary>
    /// The board's rebindable commands. Reordering acts on the tile last
    /// touched, which is the one the artist just reached for.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_shortcuts?.IdFor(e, Services.ShortcutScope.Board) is not { } id) return;
        switch (id)
        {
            case "reference.arrange":
                BoardModel.Arrange(BoardWidth, BoardHeight);
                break;
            case "reference.front" when _touched is { } front:
                BoardModel.BringToFront(front);
                break;
            case "reference.back" when _touched is { } back:
                BoardModel.SendToBack(back);
                break;
            case "reference.fit":
                FitToView();
                break;
            case "reference.remove" when _touched is { } gone:
                BoardModel.Remove(gone);
                _touched = null;
                break;
            case "reference.paste":
                _ = PasteImageAsync();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>The topmost tile under a board point, or null.</summary>
    public BoardTile? TileAt(Point at)
    {
        for (var i = BoardModel.Tiles.Count - 1; i >= 0; i--)
        {
            var tile = BoardModel.Tiles[i];
            if (at.X >= tile.X && at.X <= tile.X + tile.Width
                && at.Y >= tile.Y && at.Y <= tile.Y + tile.Height)
            {
                return tile;
            }
        }
        return null;
    }

    /// <summary>Wheel zooms the wall, around the pointer.</summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var before = _zoom.ScaleX;
        var after = Math.Clamp(before * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1), 0.1, 8);
        if (Math.Abs(after - before) < 0.0001) return;

        // Keep the point under the pointer where it is, which is what makes a
        // zoom feel like moving a camera rather than rescaling a page.
        var at = e.GetPosition((Visual?)sender ?? this);
        _pan.X = at.X - (at.X - _pan.X) * (after / before);
        _pan.Y = at.Y - (at.Y - _pan.Y) * (after / before);
        _zoom.ScaleX = _zoom.ScaleY = after;
        e.Handled = true;
    }

    // ---- bringing pictures in ----------------------------------------------------------

    private async Task AddImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add reference images",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        // Into the middle of what the artist is looking at, which is the picker's
        // equivalent of "where you dropped it".
        var centre = ViewCentre;
        var step = 0;
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path) continue;
            BoardModel.AddImageFile(path, (centre.X + step * 24, centre.Y + step * 24));
            step++;
        }
    }

    /// <summary>
    /// Paste a picture from the clipboard — the other half of how reference
    /// arrives, and the one a screenshot takes.
    /// </summary>
    private async Task PasteImageAsync()
    {
        if (Clipboard is not { } clipboard) return;
        var centre = ViewCentre;
        try
        {
            if (await clipboard.TryGetDataAsync() is not { } data) return;

            // A copied *file* first: that is what a file manager and most
            // browsers put on the clipboard, and it keeps the picture's name.
            if (await data.TryGetValuesAsync(DataFormat.File) is { } files)
            {
                var step = 0;
                foreach (var file in files)
                {
                    if (file.TryGetLocalPath() is not { } path) continue;
                    if (BoardModel.AddImageFile(path, (centre.X + step * 24, centre.Y + step * 24)) is not null)
                    {
                        step++;
                    }
                }
                if (step > 0) return;
            }

            foreach (var format in ClipboardImageFormats)
            {
                if (await data.TryGetValueAsync(format) is not { Length: > 0 } bytes) continue;
                if (BoardModel.AddImageBytes("Pasted", bytes, (centre.X, centre.Y)) is not null) return;
            }
            _vm.AiStatus = "There is no picture on the clipboard to pin up.";
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("reference-board-paste", ex);
        }
    }

    /// <summary>What a copied picture arrives as, best first.</summary>
    private static readonly DataFormat<byte[]>[] ClipboardImageFormats =
    [
        DataFormat.CreateBytesPlatformFormat("image/png"),
        DataFormat.CreateBytesPlatformFormat("image/jpeg"),
        DataFormat.CreateBytesPlatformFormat("image/bmp"),
    ];

    /// <summary>Formats a picture dragged out of a browser can arrive in.</summary>
    private static readonly DataFormat<string> UriListFormat =
        DataFormat.CreateStringPlatformFormat("text/uri-list");

    private static readonly DataFormat<string> HtmlFormat =
        DataFormat.CreateStringPlatformFormat("text/html");

    /// <summary>The image files in a drag, in the order they came.</summary>
    private static List<string> DroppedFiles(DragEventArgs e)
    {
        var paths = new List<string>();
        if (e.DataTransfer?.TryGetFiles() is not { } items) return paths;
        foreach (var item in items)
        {
            if (item.TryGetLocalPath() is { } path
                && Lightbox.Core.Projects.ProjectBoards.ImageExtensions.Contains(Path.GetExtension(path)))
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    /// <summary>
    /// The browser counterpart: a picture dragged off a web page carries URIs
    /// rather than files. Anything carrying actual files is not a web drag,
    /// whatever else rides along — the same reading the canvas drop uses.
    /// </summary>
    private static IReadOnlyList<Uri> DroppedWebImages(DragEventArgs e)
    {
        if (e.DataTransfer is not { } data || data.Contains(DataFormat.File)) return [];
        return Services.WebImageDrop.ImageUris(
            data.TryGetValue(UriListFormat),
            data.TryGetText(),
            data.TryGetValue(HtmlFormat));
    }

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = DroppedFiles(e).Count > 0 || DroppedWebImages(e).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    /// <summary>Pictures dropped on the wall are pinned up, in the order they came.</summary>
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        // Where the pointer let go, on the board — not below everything already
        // up, which is off the bottom of the window on any wall that has been
        // arranged to fill it (B204).
        var at = ToBoard(e.GetPosition(_surface.Parent as Visual ?? this));

        var files = DroppedFiles(e);
        if (files.Count > 0)
        {
            e.Handled = true;
            // Several files fan out from the drop rather than stacking exactly on
            // top of each other, so a folder dropped at once is legible.
            var step = 0;
            foreach (var path in files)
            {
                BoardModel.AddImageFile(path, (at.X + step * 24, at.Y + step * 24));
                step++;
            }
            return;
        }

        var uris = DroppedWebImages(e);
        if (uris.Count == 0) return;
        e.Handled = true;
        // One tile per drop, not one per candidate: the candidates are the same
        // picture described three ways, so pinning them all would put up
        // duplicates. Same reasoning as the canvas's web drop.
        foreach (var uri in uris)
        {
            var bytes = await Services.WebImageDrop.FetchAsync(uri);
            if (bytes is null) continue;
            if (BoardModel.AddImageBytes(Services.WebImageDrop.NameFor(uri), bytes, (at.X, at.Y)) is not null)
            {
                return;
            }
        }
        _vm.AiStatus = "That drop did not contain an image Lightbox could read.";
    }
}
