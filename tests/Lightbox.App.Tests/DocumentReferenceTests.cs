using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;
using SkiaSharp;

namespace Lightbox.App.Tests;

/// <summary>
/// Another document as reference (Q284–Q286): the run cycle drawn against by
/// the jump beside it. Read, never written; flattened one picture per frame,
/// visible layers without the background, stacked in a column; laid against
/// the timeline frame for frame; re-flattened when the source file is newer.
/// </summary>
public sealed class DocumentReferenceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "lightbox-docref-" + Guid.NewGuid().ToString("N"));

    public DocumentReferenceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A run of <paramref name="frames"/> drawings on white paper: frame i is a
    /// red dab that travels right by 40 px a frame, so a reference that loses
    /// the travel — or the order — shows it. A hidden layer carries a green dab
    /// that must never appear.
    /// </summary>
    private string Source(int frames = 3, string name = "run.lightbox.json", int width = 320, int height = 180)
    {
        var doc = DocumentFactory.CreateDoc(width, height, 24, paperColor: "#ffffff");
        doc.Scene.FrameCount = frames;
        var paint = doc.Scene.Layers.Single(l => !l.IsBackground);
        paint.Cels = [];
        for (var i = 0; i < frames; i++)
        {
            var frame = new Frame();
            frame.Strokes.Add(Dab(60 + i * 40, 90, "#ff0000"));
            paint.Cels.Add(new Cel { Frame = frame });
        }
        var hidden = new Layer
        {
            Name = "Hidden",
            Kind = LayerKind.Painted,
            Visible = false,
            Cels = [new Cel { Frame = new Frame { Strokes = [Dab(200, 40, "#00ff00")] } }],
        };
        doc.Scene.Layers.Add(hidden);
        var path = Path.Combine(_dir, name);
        DocJson.Save(doc, path);
        return path;
    }

    private static Stroke Dab(double x, double y, string color)
    {
        var stroke = new Stroke { Color = color };
        stroke.Brush.Size = 16;
        for (var p = 0; p < 6; p++) stroke.Points.Add(new StrokePoint(x + p, y, 1.0));
        return stroke;
    }

    private MainViewModel SavedVm(int width = 320, int height = 180)
    {
        var vm = new MainViewModel();
        vm.NewDocument(new NewDocumentSettings("jump", width, height, 11, 72, Scene.DefaultBackgroundColor, false));
        vm.ActiveTab!.FilePath = Path.Combine(_dir, "jump.lightbox.json");
        return vm;
    }

    private static SKColor At(SKBitmap sheet, ReferenceCell cell, int x, int y) =>
        sheet.GetPixel(cell.X + x, cell.Y + y);

    // ---- the flatten -----------------------------------------------------------

    [Fact]
    public void EachFrameFlattensToOneCellOfAColumn_SameWindowEveryFrame()
    {
        var path = Source(frames: 3);

        using var flat = DocumentReference.Flatten(path, out var error);

        Assert.True(flat is not null, error);
        Assert.Equal(3, flat!.Cells.Count);
        // A column: one above the next, every window the same size, so the
        // runner's travel survives being stepped through (windows, not crops).
        var first = flat.Cells[0];
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(0, flat.Cells[i].X);
            Assert.Equal(i * first.Height, flat.Cells[i].Y);
            Assert.Equal(first.Width, flat.Cells[i].Width);
            Assert.Equal(first.Height, flat.Cells[i].Height);
        }
        Assert.Equal(first.Width, flat.Sheet.Width);
        Assert.Equal(first.Height * 3, flat.Sheet.Height);
    }

    [Fact]
    public void TheFlattenKeepsTheTravel_AndRegistersOnTheSourceCanvas()
    {
        var path = Source(frames: 3);

        using var flat = DocumentReference.Flatten(path, out _)!;

        // Frame i's dab sits at source x = 60 + 40i (+2 for the six points).
        // In sheet pixels that is (x - origin) * scale, inside cell i.
        for (var i = 0; i < 3; i++)
        {
            var cell = flat.Cells[i];
            var sx = (int)Math.Round((62 + i * 40 - flat.OriginX) * flat.Scale);
            var sy = (int)Math.Round((90 - flat.OriginY) * flat.Scale);
            var px = At(flat.Sheet, cell, sx, sy);
            Assert.True(px.Red > 200 && px.Green < 80 && px.Alpha > 200,
                $"frame {i}: expected the red dab at sheet ({sx},{sy}) of cell {i}, got {px}");
        }
    }

    [Fact]
    public void ThePaperAndHiddenLayersAreLeftOut()
    {
        var path = Source(frames: 2);

        using var flat = DocumentReference.Flatten(path, out _)!;

        // Without the background a corner of the window holds nothing: the
        // reference must not cover the paper it is taped over (Q285).
        var corner = flat.Sheet.GetPixel(0, 0);
        Assert.True(corner.Alpha == 0, $"the paper came through: {corner}");
        // And nothing green anywhere — the hidden layer stays hidden.
        for (var y = 0; y < flat.Sheet.Height; y++)
        {
            for (var x = 0; x < flat.Sheet.Width; x++)
            {
                var p = flat.Sheet.GetPixel(x, y);
                Assert.False(p.Green > 150 && p.Red < 100 && p.Alpha > 100, $"hidden layer drawn at ({x},{y})");
            }
        }
    }

    [Fact]
    public void ALongRunWrapsIntoAnotherColumn_AtFullSize()
    {
        // A drawing the height of a tall canvas, ten frames of it: one column
        // would be past the side limit, so it wraps rather than shrinking —
        // shrinking cost the owner's 31-frame run a third of its resolution.
        var doc = DocumentFactory.CreateDoc(200, 2000, 24);
        doc.Scene.FrameCount = 10;
        var paint = doc.Scene.Layers[0];
        paint.Cels = [];
        for (var i = 0; i < 10; i++)
        {
            var line = new Stroke { Color = "#ff0000" };
            line.Brush.Size = 8;
            for (var y = 10; y <= 1990; y += 20) line.Points.Add(new StrokePoint(50 + i * 10, y, 1.0));
            paint.Cels.Add(new Cel { Frame = new Frame { Strokes = [line] } });
        }
        var path = Path.Combine(_dir, "tall.lightbox.json");
        DocJson.Save(doc, path);

        using var flat = DocumentReference.Flatten(path, out var error)!;

        Assert.True(flat is not null, error);
        Assert.Equal(1.0, flat.Scale);
        Assert.True(flat.Sheet.Height <= DocumentReference.MaxSide, $"sheet is {flat.Sheet.Height} tall");
        var cell = flat.Cells[0];
        var perColumn = DocumentReference.MaxSide / cell.Height;
        Assert.True(perColumn < 10, "the test needs a run that cannot fit one column");
        // Top to bottom, then the next column: frame perColumn starts column two.
        Assert.Equal((cell.Width, 0), (flat.Cells[perColumn].X, flat.Cells[perColumn].Y));
        Assert.Equal((0, cell.Height), (flat.Cells[1].X, flat.Cells[1].Y));
        Assert.Equal(cell.Width * 2, flat.Sheet.Width);
    }

    [Fact]
    public void AFileThatIsNotADocumentFlattensToNothing_WithAReason()
    {
        var path = Path.Combine(_dir, "notes.lightbox.json");
        File.WriteAllText(path, "not a document");

        Assert.Null(DocumentReference.Flatten(path, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Null(DocumentReference.Flatten(Path.Combine(_dir, "gone.lightbox.json"), out error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    // ---- onto the canvas, frame for frame --------------------------------------

    [AvaloniaFact]
    public async Task ReferencingADocument_LaysItsFramesFromThePlayhead_AndKeepsTheLink()
    {
        var path = Source(frames: 3);
        var vm = SavedVm();
        vm.CurrentFrameIndex = 0;

        var (strip, error) = await vm.ImportDocumentReferenceAsync(path);

        Assert.True(strip is not null, error);
        Assert.Equal(3, strip!.Cells.Count);
        Assert.Equal(0, strip.FirstAssignedSlot());
        Assert.Equal(2, strip.LastAssignedSlot());
        Assert.True(vm.Doc.Scene.FrameCount >= 3);
        // Relative, because the run lives beside the jump — the project can
        // move as a whole.
        Assert.Equal("run.lightbox.json", strip.Document!.Path);
        Assert.Equal(File.GetLastWriteTimeUtc(path).Ticks, strip.Document!.Stamp);
        Assert.NotEmpty(strip.Png);
        Assert.NotNull(Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id));
        Assert.False(strip.Pinned);
    }

    [AvaloniaFact]
    public async Task OnASameSizedCanvas_TheReferenceSitsWhereTheSourceDrewIt()
    {
        var path = Source(frames: 2);
        var vm = SavedVm();

        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        using var flat = DocumentReference.Flatten(path, out _)!;

        // Registration, not a fit-and-centre: the run's feet land on the
        // jump's ground line because both canvases are the same shape.
        Assert.Equal(flat.OriginX, strip.OffsetX, 3);
        Assert.Equal(flat.OriginY, strip.OffsetY, 3);
        Assert.Equal(1 / flat.Scale, strip.Scale, 3);
    }

    [AvaloniaFact]
    public async Task TheSourceFileIsNeverWritten()
    {
        var path = Source(frames: 3);
        var before = File.ReadAllBytes(path);
        var stamp = File.GetLastWriteTimeUtc(path);
        var vm = SavedVm();

        await vm.ImportDocumentReferenceAsync(path);
        await vm.RefreshDocumentReferencesAsync();

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
    }

    [AvaloniaFact]
    public async Task ADocumentCannotReferenceItself()
    {
        var vm = SavedVm();
        var self = vm.ActiveTab!.FilePath!;
        DocJson.Save(vm.Doc, self);

        var (refused, error) = await vm.ImportDocumentReferenceAsync(self);
        Assert.Null(refused);
        Assert.Contains("itself", error);
        Assert.Null(vm.Doc.Scene.References);
    }

    // ---- live (Q284) -----------------------------------------------------------

    [AvaloniaFact]
    public async Task ANewerSourceIsReflattened_AnUnchangedOneIsNot()
    {
        var path = Source(frames: 2);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        var first = strip.Png;

        // Unchanged: the stamp gate answers without rendering anything.
        Assert.Equal(0, await vm.RefreshDocumentReferencesAsync());
        Assert.Same(first, strip.Png);

        // The artist adds a drawing to the run and saves.
        Source(frames: 4);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(1, await vm.RefreshDocumentReferencesAsync());
        Assert.NotEqual(first, strip.Png);
        Assert.Equal(4, strip.Cells.Count);
        // A grown run lays its new drawings on after the old ones, from where
        // the reference already started.
        Assert.Equal(0, strip.FirstAssignedSlot());
        Assert.Equal(3, strip.LastAssignedSlot());
        Assert.Equal(File.GetLastWriteTimeUtc(path).Ticks, strip.Document!.Stamp);
    }

    [AvaloniaFact]
    public async Task ARefreshKeepsTheArtistsAlignment()
    {
        var path = Source(frames: 2);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        strip.Cells[1].Dx = 7;
        strip.Cells[1].Dy = -3;
        strip.Opacity = 0.8;

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        await vm.RefreshDocumentReferencesAsync();

        Assert.Equal(7, strip.Cells[1].Dx);
        Assert.Equal(-3, strip.Cells[1].Dy);
        Assert.Equal(0.8, strip.Opacity);
    }

    [AvaloniaFact]
    public async Task ARunRedrawnWiderStaysRegistered()
    {
        // The window is the union of what the run draws, so a new drawing
        // further left moves the window's origin. The reference must move its
        // offset by the same amount, or every frame jumps sideways on refresh.
        var path = Source(frames: 2);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        var before = (strip.OffsetX, strip.Document!.OriginX);

        var doc = DocJson.Load(path);
        doc.Scene.Layers.Single(l => l.Name == "Paint").Cels[0].Frame!.Strokes.Add(Dab(20, 90, "#ff0000"));
        DocJson.Save(doc, path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        await vm.RefreshDocumentReferencesAsync();

        var moved = strip.Document!.OriginX - before.OriginX;
        Assert.True(moved < -20, $"the window should have grown left, origin moved {moved}");
        Assert.Equal(before.OffsetX + moved, strip.OffsetX, 3);
        Assert.Equal(strip.Document.OriginX, strip.OffsetX, 3);
    }

    [AvaloniaFact]
    public async Task AMissingSourceLeavesTheLastFramesStanding()
    {
        var path = Source(frames: 2);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        var last = strip.Png;

        File.Delete(path);

        Assert.Equal(0, await vm.RefreshDocumentReferencesAsync());
        Assert.Same(last, strip.Png);
        Assert.Equal(2, strip.Cells.Count);
    }

    // ---- onto the board --------------------------------------------------------

    [AvaloniaFact]
    public async Task ADocumentOnTheBoard_IsAColumnTileThatRemembersItsSource()
    {
        var path = Source(frames: 3);
        var board = new ReferenceBoardViewModel(SavedVm());

        var (tile, error) = await board.AddDocumentAsync(path);

        Assert.True(tile is not null, error);
        Assert.Equal(path, tile!.Origin);
        Assert.True(tile.IsDocument);
        Assert.Equal(File.GetLastWriteTimeUtc(path).Ticks, tile.DocumentStamp);
        // The tile is the column's shape: three windows stacked, so three
        // times as tall per unit of width as one frame would be.
        var pixels = board.PixelsFor(tile);
        Assert.NotNull(pixels);
        using var sheet = SKBitmap.Decode(pixels);
        using var flat = DocumentReference.Flatten(path, out _)!;
        Assert.Equal(flat.Cells[0].Height * 3, sheet.Height);
        Assert.Equal(sheet.Height / (double)sheet.Width, tile.Height / tile.Width, 2);
    }

    [AvaloniaFact]
    public async Task ANewerSourceRefreshesItsBoardTile()
    {
        var path = Source(frames: 2);
        var board = new ReferenceBoardViewModel(SavedVm());
        var tile = (await board.AddDocumentAsync(path)).Tile!;
        var before = board.PixelsFor(tile);

        Source(frames: 4);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(1, await board.RefreshDocumentTilesAsync());
        Assert.NotEqual(before, board.PixelsFor(tile));
        Assert.Equal(File.GetLastWriteTimeUtc(path).Ticks, tile.DocumentStamp);
        Assert.Equal(0, await board.RefreshDocumentTilesAsync());
    }

    [AvaloniaFact]
    public async Task LayingADocumentTileOntoTheTimeline_UsesItsFrames_NotAGuess()
    {
        // The slicer reads drawings out of a picture; a document already knows
        // where its frames are, so the tile lays out exactly that many, linked.
        var path = Source(frames: 3);
        var vm = SavedVm();
        var board = new ReferenceBoardViewModel(vm);
        var tile = (await board.AddDocumentAsync(path)).Tile!;

        var strip = await vm.LayBoardTileOntoTimelineAsync(tile, board.PixelsFor(tile));

        Assert.NotNull(strip);
        Assert.Equal(3, strip!.Cells.Count);
        Assert.Equal("run.lightbox.json", strip.Document!.Path);
    }

    // ---- what a refresh keeps (adversarial review) ------------------------------

    [AvaloniaFact]
    public async Task HandRetimingSurvivesTheSourceShrinkingAndGrowingBack()
    {
        // The artist holds drawing 2 for three frames. The run loses a drawing
        // and gets it back: the hold must come back with it, because a cleared
        // slot is timing nobody can undo.
        var path = Source(frames: 3);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        strip.Slots = [0, 1, 2, 2, 2];

        Source(frames: 2);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        await vm.RefreshDocumentReferencesAsync();
        Assert.Equal([0, 1, 2, 2, 2], strip.Slots);
        Assert.Null(strip.CellAt(2));

        Source(frames: 3);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(2));
        await vm.RefreshDocumentReferencesAsync();
        Assert.Equal([0, 1, 2, 2, 2], strip.Slots);
        Assert.NotNull(strip.CellAt(4));
    }

    [AvaloniaFact]
    public async Task AHandRetimedRunThatGrowsKeepsItsTiming()
    {
        var path = Source(frames: 2);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        strip.Slots = [1, 0];

        Source(frames: 3);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        await vm.RefreshDocumentReferencesAsync();

        // Not re-laid from the first slot: the artist's order stands.
        Assert.Equal([1, 0], strip.Slots);
        Assert.Equal(3, strip.Cells.Count);
    }

    [AvaloniaFact]
    public async Task APivotIsCarriedAsAPlaceInsideItsWindow()
    {
        var path = Source(frames: 2);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        var cell = strip.Cells[1];
        cell.PivotX = cell.X + 10;
        cell.PivotY = cell.Y + 7;

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        await vm.RefreshDocumentReferencesAsync();

        var after = strip.Cells[1];
        Assert.Equal(after.X + 10, after.PivotX!.Value, 3);
        Assert.Equal(after.Y + 7, after.PivotY!.Value, 3);
    }

    [Fact]
    public void AChangeInShrinkKeepsThePictureTheSameSizeInTheDocument()
    {
        // Doc pixels per source pixel is what must hold: a column flattened at
        // half size is drawn at twice the scale, so the run does not halve.
        var strip = new ReferenceStrip
        {
            Cells = [new ReferenceCell { Width = 100, Height = 50 }],
            Scale = 1,
            OffsetX = 30,
            OffsetY = 40,
            Document = new DocumentSource { Path = "run.lightbox.json", OriginX = 30, OriginY = 40, Scale = 1 },
        };
        using var flat = new FlattenedDocument
        {
            Sheet = new SKBitmap(50, 25),
            Cells = [new ReferenceCell { Width = 50, Height = 25 }],
            OriginX = 30,
            OriginY = 40,
            Scale = 0.5,
        };

        MainViewModel.Reflatten(strip, flat);

        Assert.Equal(2, strip.Scale, 6);
        Assert.Equal((30.0, 40.0), (strip.OffsetX, strip.OffsetY));
        Assert.Equal(0.5, strip.Document!.Scale);
    }

    [AvaloniaFact]
    public async Task AStripTakenDownMidFlattenIsNotRegisteredAgain()
    {
        var path = Source(frames: 2);
        var vm = SavedVm();
        var strip = (await vm.ImportDocumentReferenceAsync(path)).Strip!;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        // Started, not awaited: the flatten is off the UI thread, and the
        // removal below happens before its continuation can run.
        var refresh = vm.RefreshDocumentReferencesAsync();
        vm.Doc.Scene.References!.Remove(strip);
        Lightbox.Raster.ReferenceStripRegistry.Forget(strip.Id);

        Assert.Equal(0, await refresh);
        Assert.Null(Lightbox.Raster.ReferenceStripRegistry.Resolve(strip.Id));
    }

    // ---- what is never followed or written (sensitivity review) ----------------

    [AvaloniaFact]
    public async Task ANetworkShareIsNeverFollowed()
    {
        var vm = SavedVm();

        var (strip, error) = await vm.ImportDocumentReferenceAsync(@"\\nohost\share\run.lightbox.json");
        Assert.Null(strip);
        Assert.Contains("network share", error);
        Assert.Null(vm.ResolveReferencePath(@"\\nohost\share\run.lightbox.json"));
        Assert.Null(vm.ResolveReferencePath("//nohost/share/run.lightbox.json"));
        Assert.False(DocumentReference.IsLocal(@"\\?\C:\run.lightbox.json"));
    }

    [AvaloniaFact]
    public void ARelativePathNeverClimbsOutOfThisDocumentsFolder()
    {
        var vm = SavedVm();

        Assert.Null(vm.ResolveReferencePath(Path.Combine("..", "OtherShow", "hero.lightbox.json")));
        Assert.Equal(Path.Combine(_dir, "run.lightbox.json"), vm.ResolveReferencePath("run.lightbox.json"));
        Assert.Equal(Path.Combine(_dir, "cycles", "run.lightbox.json"),
            vm.ResolveReferencePath(Path.Combine("cycles", "run.lightbox.json")));
    }

    [Fact]
    public void ASourceWithTooManyFramesIsRefusedBeforeAnyRender()
    {
        var doc = DocumentFactory.CreateDoc(64, 64, 24);
        doc.Scene.FrameCount = 100_000;
        var path = Path.Combine(_dir, "endless.lightbox.json");
        DocJson.Save(doc, path);
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.Null(DocumentReference.Flatten(path, out var error));

        Assert.Contains("frames", error);
        // Refused, not rendered: a hundred thousand renders would take minutes.
        Assert.True(clock.ElapsedMilliseconds < 5000, $"took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ACanvasTooLargeToFlattenIsRefused()
    {
        var doc = DocumentFactory.CreateDoc(10_000, 10_000, 24);
        var path = Path.Combine(_dir, "mural.lightbox.json");
        DocJson.Save(doc, path);

        Assert.Null(DocumentReference.Flatten(path, out var error));
        Assert.Contains("too large", error);
    }

    [AvaloniaFact]
    public async Task ABoardTilePointedAtADrawing_NeverOverwritesIt()
    {
        // A board file is data: a received project can aim a document tile's
        // path at the artist's drawing. A refresh must not write there.
        var vm = SavedVm();
        var root = Path.Combine(_dir, "project");
        vm.ProjectDocker.Project = Lightbox.Core.Projects.ProjectIo.Create("Demo", root);
        var hero = Path.Combine(root, "scenes", "hero.lightbox.json");
        Directory.CreateDirectory(Path.GetDirectoryName(hero)!);
        DocJson.Save(DocumentFactory.CreateDoc(64, 64, 24), hero);
        var before = File.ReadAllBytes(hero);
        var source = Source(frames: 2);
        var board = new ReferenceBoardViewModel(vm);
        var tile = new BoardTile { Name = "run", Path = "scenes/hero.lightbox.json", Origin = source, DocumentStamp = 0 };
        board.Board.Tiles.Add(tile);

        Assert.Equal(1, await board.RefreshDocumentTilesAsync());

        Assert.Equal(before, File.ReadAllBytes(hero));
        // The picture went where pictures go, and the tile follows it there.
        Assert.StartsWith(Lightbox.Core.Projects.ProjectBoards.ImagesDir + "/", tile.Path);
        Assert.EndsWith(".png", tile.Path);
        Assert.NotNull(board.PixelsFor(tile));
    }

    [AvaloniaFact]
    public async Task ABoardTilesOwnPictureIsRewrittenInPlace()
    {
        var vm = SavedVm();
        vm.ProjectDocker.Project = Lightbox.Core.Projects.ProjectIo.Create("Demo", Path.Combine(_dir, "project"));
        var source = Source(frames: 2);
        var board = new ReferenceBoardViewModel(vm);
        var tile = (await board.AddDocumentAsync(source)).Tile!;
        var path = tile.Path;
        Assert.StartsWith(Lightbox.Core.Projects.ProjectBoards.ImagesDir + "/", path);

        Source(frames: 4);
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(1, await board.RefreshDocumentTilesAsync());

        Assert.Equal(path, tile.Path);
        using var sheet = SKBitmap.Decode(board.PixelsFor(tile));
        using var flat = DocumentReference.Flatten(source, out _)!;
        Assert.Equal(flat.Sheet.Height, sheet.Height);
    }
}
