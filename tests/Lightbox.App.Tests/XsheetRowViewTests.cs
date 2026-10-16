using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.App.Views;
using Lightbox.Core.Documents;

namespace Lightbox.App.Tests;

/// <summary>
/// The X-sheet drawn by one control per row instead of a button per cel (Q282):
/// what it costs, what it shows, and that the cel's gestures and menu still
/// land on the cel under the pointer.
/// </summary>
[Collection("BrushState")]
public class XsheetRowViewTests : BrushStateIsolated
{
    private static void Pump() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static (MainWindow Window, MainViewModel Vm) Open(int frames = 6, int layers = 3)
    {
        var window = new MainWindow { Width = 1400, Height = 1000 };
        window.Show();
        Pump();
        var vm = (MainViewModel)window.DataContext!;
        vm.NewDocument(new NewDocumentSettings("Sheet", 400, 300, 12, 72, "#ffffff", false));
        while (vm.Doc.Scene.Layers.Count < layers + 1) vm.AddPaintedLayerCommand.Execute(null);
        for (var i = 1; i < frames; i++) vm.AddFrameCommand.Execute(null);
        vm.Workspace.Activate(Lightbox.App.Docking.DockPanelId.Xsheet);
        Pump();
        return (window, vm);
    }

    private static Control Xsheet(MainWindow window) => window.FindControl<Control>("XsheetDocker")!;

    private static FrameCell Cell(MainViewModel vm, int row, int index) => vm.LayerRows[row].Cells[index];

    private static void Render(MainWindow window)
    {
        Pump();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Pump();
        using var _ = window.CaptureRenderedFrame();
    }

    // ---- what it costs -------------------------------------------------------------

    [AvaloniaFact]
    public void TheSheetHasNoControlPerCel()
    {
        var (window, vm) = Open();

        var sheet = Xsheet(window);
        Assert.DoesNotContain(sheet.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("cel"));
        var rows = sheet.GetVisualDescendants().OfType<XsheetRowView>().Count();
        Assert.Equal(vm.SheetLayerRows.Count(), rows);
    }

    /// <summary>
    /// A long scene costs what is on screen. Today's sheet built a button, a
    /// picture and a context menu for every cel of every row, scrolled into view
    /// or not; the drawn row paints the frames the docker is showing.
    /// </summary>
    [AvaloniaFact]
    public void TheSheetCostsTheCelsOnScreen()
    {
        var (window, vm) = Open(frames: 400, layers: 2);
        Render(window);

        var row = XsheetProbe.RowOf(window, Cell(vm, 0, 0));
        var pitch = row.FrameWidth + row.Gap;
        var visible = (int)Math.Ceiling(window.Bounds.Width / pitch) + 2;
        Assert.InRange(row.LastDrawnCells, 1, visible);
        Assert.True(row.LastDrawnCells < 400, $"drew {row.LastDrawnCells} of 400 cels");

        // And the visual tree does not grow with the scene either.
        var shortSheet = Xsheet(Open(frames: 6, layers: 2).Window).GetVisualDescendants().Count();
        var longSheet = Xsheet(window).GetVisualDescendants().Count();
        Assert.Equal(shortSheet, longSheet);
    }

    /// <summary>
    /// A row scrolled out of the sheet still repaints whenever its cels change —
    /// every frame of playback — so it must paint nothing, not fall back to
    /// painting all of them for want of a viewport.
    /// </summary>
    [AvaloniaFact]
    public void ARowScrolledOutOfTheSheetDrawsNothing()
    {
        var (window, vm) = Open(frames: 200, layers: 30);
        Render(window);

        var rows = Xsheet(window).GetVisualDescendants().OfType<XsheetRowView>().ToList();
        var hidden = rows.Where(r => r.LastDrawnCells == 0).ToList();
        Assert.True(hidden.Count > 0, $"expected some of the {rows.Count} rows to be scrolled out of a {window.Height} px window");
        Assert.All(rows, r => Assert.InRange(r.LastDrawnCells, 0, 199));
    }

    // ---- what a cel shows ----------------------------------------------------------

    [AvaloniaFact]
    public void TheSheetShowsNumbersUnlessAskedOtherwise()
    {
        var (window, vm) = Open();

        Assert.Equal(XsheetCellStyle.Numbers, vm.XsheetCellStyle);
        Assert.Equal(XsheetCellStyle.Numbers, XsheetProbe.RowOf(window, Cell(vm, 0, 0)).CellStyle);
    }

    [AvaloniaFact]
    public void TheCelStyleIsAPreferenceThatSurvivesARestart()
    {
        var (window, vm) = Open();
        vm.XsheetCellStyle = XsheetCellStyle.Dots;
        Pump();

        Assert.Equal(XsheetCellStyle.Dots, XsheetProbe.RowOf(window, Cell(vm, 0, 0)).CellStyle);
        Assert.Equal(XsheetCellStyle.Dots, Lightbox.App.Services.AppSettings.Load().XsheetCellStyle);
    }

    /// <summary>
    /// A style a newer build added reads as the default here, and costs no other
    /// preference: the plain string converter threw on the unknown name, and a
    /// settings file that fails to read is replaced by defaults wholesale.
    /// </summary>
    [Fact]
    public void AStyleFromANewerBuildReadsAsNumbersAndKeepsEverythingElse()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<Lightbox.App.Services.AppSettings>(
            """{"XsheetCellStyle":"Strips","MarkOffSheetKeys":true}""")!;

        Assert.Equal(XsheetCellStyle.Numbers, settings.XsheetCellStyle);
        Assert.True(settings.MarkOffSheetKeys);
    }

    [Fact]
    public void TheStyleIsWrittenByName()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new Lightbox.App.Services.AppSettings { XsheetCellStyle = XsheetCellStyle.Dots });
        Assert.Contains("\"XsheetCellStyle\":\"Dots\"", json);
    }

    /// <summary>
    /// The row remembers the cel last clicked, for Ctrl+C. Given a different
    /// list of cels — a row reused for another layer — it must forget it, or a
    /// copy would reach a cel that is no longer on the row.
    /// </summary>
    [AvaloniaFact]
    public void ARowGivenOtherCelsForgetsTheOneItWasHolding()
    {
        var (window, vm) = Open();
        var cel = Cell(vm, 0, 2);
        var row = XsheetProbe.RowOf(window, cel);
        var at = XsheetProbe.CentreOfCell(window, cel);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();
        Assert.Same(cel, row.FocusedCell);

        row.Cells = vm.LayerRows[1].Cells;
        Pump();

        Assert.Null(row.FocusedCell);
    }

    /// <summary>The choice is on Configure ▸ Timeline as well as the sheet's own bar.</summary>
    [AvaloniaFact]
    public void ConfigureOffersTheCelStyle()
    {
        var vm = new MainViewModel(artist: null);
        var window = new ConfigureWindow(new Lightbox.App.Services.ShortcutMap(), vm);
        window.FindControl<ListBox>("CategoryList")!.SelectedIndex = 4;
        window.Show();
        Pump();
        Assert.True(window.FindControl<ScrollViewer>("TimelinePage")!.IsVisible);
        var box = window.FindControl<ComboBox>("XsheetCellStyleBox")!;
        Assert.Equal(XsheetCellStyle.Numbers, box.SelectedItem);

        box.SelectedItem = XsheetCellStyle.Pictures;

        Assert.Equal(XsheetCellStyle.Pictures, vm.XsheetCellStyle);
        window.Close();
    }

    /// <summary>
    /// Pictures are the expensive style, so only the pictures style pays for
    /// them: numbers and dots render no cel thumbnail at all.
    /// </summary>
    [AvaloniaFact]
    public void OnlyThePicturesStyleRendersCelPictures()
    {
        var (_, vm) = Open();
        vm.RefreshThumbnails();
        Assert.All(vm.LayerRows.SelectMany(r => r.Cells), c => Assert.Null(c.Thumb));

        vm.XsheetCellStyle = XsheetCellStyle.Pictures;
        Pump();

        Assert.Contains(vm.LayerRows.SelectMany(r => r.Cells), c => c.IsKeyed && c.Thumb is not null);
    }

    [AvaloniaFact]
    public void ABlankDrawingIsMarkedBlankUntilSomethingIsDrawnOnIt()
    {
        var (_, vm) = Open();
        var row = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex);
        vm.InsertBlankKeyframeAt(row.Cells[3]);
        vm.RefreshThumbnails();
        Assert.True(row.Cells[3].IsBlank);

        vm.CurrentFrameIndex = 3;
        vm.Doc.Scene.Layers[vm.ActiveLayerIndex].Cels[3].Frame!.Strokes.Add(new Stroke
        {
            Tool = ToolKind.Fill,
            Color = "#000000",
            Points = [new(1, 1, 1), new(9, 1, 1), new(9, 9, 1)],
        });
        vm.RefreshThumbnails();

        Assert.False(row.Cells[3].IsBlank);
    }

    /// <summary>
    /// The kind of drawing is said once. Numbers say it with the number — bold,
    /// ringed, quiet — and dots with the mark, so the breakdown and inbetween
    /// tints are left to the pictures style, where a picture cannot say it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(XsheetCellStyle.Numbers, false)]
    [InlineData(XsheetCellStyle.Dots, false)]
    [InlineData(XsheetCellStyle.Pictures, true)]
    public void OnlyPicturesTintACelByTheKindOfDrawing(XsheetCellStyle style, bool tinted)
    {
        var (window, vm) = Open();
        var row = vm.LayerRows.First(r => r.SceneIndex == vm.ActiveLayerIndex);
        vm.MarkCelAs(row.Cells[2], Lightbox.Core.Documents.FrameRole.Breakdown);
        vm.CurrentFrameIndex = 0;
        vm.XsheetCellStyle = style;
        Pump();
        var view = XsheetProbe.RowOf(window, row.Cells[2]);
        Assert.True(row.Cells[2].IsBreakdown);

        var ground = view.GroundFor(row.Cells[2]);

        // Looked up the way the row looks them up: the cel brush is a theme
        // resource, which a lookup without the theme variant does not find.
        object? Brush(string key) => view.TryFindResource(key, view.ActualThemeVariant, out var b) ? b : null;
        var plain = Brush("CelBrush");
        var breakdown = Brush("CelBreakdownBrush");
        Assert.NotNull(plain);
        Assert.NotNull(breakdown);
        Assert.Same(tinted ? breakdown : plain, ground);
    }

    // ---- the cel's gestures ---------------------------------------------------------

    [AvaloniaFact]
    public void AClickOnACelGoesThere()
    {
        var (window, vm) = Open();
        var at = XsheetProbe.CentreOfCell(window, Cell(vm, 0, 4));

        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Pump();

        Assert.Equal(4, vm.CurrentFrameIndex);
    }

    /// <summary>
    /// A press focuses the row, and the row is the whole scene wide. Asked to
    /// bring all of it into view, the sheet slid sideways between the press and
    /// the release, and the click landed on the next cel — found by the click
    /// tests on a six-frame sheet, which is all it takes to be wider than the
    /// docker.
    /// </summary>
    [AvaloniaFact]
    public void AClickNeverScrollsTheSheet()
    {
        var (window, vm) = Open(frames: 60);
        var row = XsheetProbe.RowOf(window, Cell(vm, 0, 2));
        var scroller = row.FindAncestorOfType<ScrollViewer>()!;
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width, "the row has to be wider than the docker");
        var before = scroller.Offset;
        var at = XsheetProbe.CentreOfCell(window, Cell(vm, 0, 2));

        window.MouseDown(at, MouseButton.Left);
        Pump();
        window.MouseUp(at, MouseButton.Left);
        Pump();

        Assert.Equal(before, scroller.Offset);
        Assert.Equal(2, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void APressThatLeavesTheCelBeforeLettingGoIsNotAClick()
    {
        var (window, vm) = Open();
        vm.CurrentFrameIndex = 0;
        var at = XsheetProbe.CentreOfCell(window, Cell(vm, 0, 4));
        var away = new Point(at.X, at.Y + 400);

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(away);
        window.MouseUp(away, MouseButton.Left);
        Pump();

        Assert.Equal(0, vm.CurrentFrameIndex);
    }

    [AvaloniaFact]
    public void ShiftClickTakesTheRange()
    {
        var (window, vm) = Open();
        var from = XsheetProbe.CentreOfCell(window, Cell(vm, 0, 1));
        var to = XsheetProbe.CentreOfCell(window, Cell(vm, 0, 3));

        window.MouseDown(from, MouseButton.Left);
        window.MouseUp(from, MouseButton.Left);
        window.MouseDown(to, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.Shift);
        Pump();

        Assert.Equal(3, vm.CelSelection.Count);
        Assert.Equal(1, vm.CurrentFrameIndex);
    }

    /// <summary>
    /// Reached by Tab with nothing clicked, the row holds the playhead's cel, so
    /// the keyboard has a cel to act on — Enter goes there, Ctrl+C copies it.
    /// </summary>
    [AvaloniaFact]
    public void TabbingOntoARowHoldsThePlayheadsCel()
    {
        var (window, vm) = Open();
        vm.CurrentFrameIndex = 3;
        Pump();
        var row = XsheetProbe.RowOf(window, Cell(vm, 0, 0));

        row.Focus(NavigationMethod.Tab);
        Pump();

        Assert.Same(Cell(vm, 0, 3), row.FocusedCell);
    }

    /// <summary>
    /// The menu is one per row now, so it has to be told which cel it was
    /// opened on — every item reads the cel off the menu's data context.
    /// </summary>
    [AvaloniaFact]
    public void TheRightClickMenuIsAimedAtTheCelUnderThePointer()
    {
        var (window, vm) = Open();
        var cell = Cell(vm, 1, 5);
        var at = XsheetProbe.CentreOfCell(window, cell);

        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);
        Pump();

        var menu = XsheetProbe.RowOf(window, cell).ContextMenu;
        Assert.NotNull(menu);
        Assert.Same(cell, menu!.DataContext);
    }
}
