using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Serialization;

namespace Lightbox.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    private Services.IpcServer? _ipc;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.SnapshotChanged += snapshot => Canvas.UpdateSnapshot(snapshot);
        Canvas.PaintStarted += _vm.BeginStroke;
        Canvas.PaintMoved += _vm.MoveStrokeBatch;
        Canvas.PaintEnded += _vm.EndStroke;

        // Tool-aware canvas input (fill clicks, selection shapes) + ants overlay.
        Canvas.FillClicked += _vm.FillAt;
        Canvas.SelectionShapeDrawn += _vm.ApplySelectionShape;
        Canvas.PolygonVertexAdded += _vm.AddPolygonVertex;
        Canvas.PolygonCompleted += _vm.CompletePolygon;
        _vm.SelectionChanged += () => Canvas.SetSelectionOverlay(_vm.SelectionContours, _vm.PolygonInProgress);
        SyncCanvasToolMode();

        // The toggle button eats pointer events, so hook the hold-to-open
        // variant flyout with tunneling handlers.
        SelectToolButton.AddHandler(PointerPressedEvent, OnSelectToolPressed, RoutingStrategies.Tunnel);
        SelectToolButton.AddHandler(PointerReleasedEvent, OnSelectToolReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Dock geometry (side, collapse, min sizes) is a view concern the VM
        // only expresses as booleans.
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.SidebarOnRight)
                or nameof(MainViewModel.SidebarVisible)
                or nameof(MainViewModel.ShowTimeline))
            {
                ApplyDockLayout();
            }
            if (args.PropertyName is nameof(MainViewModel.ActiveTool)
                or nameof(MainViewModel.ActiveSelectVariant))
            {
                SyncCanvasToolMode();
            }
        };
        ApplyDockLayout();

        // If canvas input ever fails, say so in the status bar instead of dying silently.
        Canvas.CanvasError += message => _vm.AiStatus = message;

        Canvas.ViewChanged += () =>
        {
            ZoomLabel.Content = $"{Canvas.ZoomPercent:0}%";
            MirrorButton.Background = Canvas.IsMirrored
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a6ea9"))
                : Avalonia.Media.Brushes.Transparent;
        };

        KeyDown += OnKeyDown;
        Loaded += (_, _) =>
        {
            _vm.PublishSnapshot();
            // MCP bridge endpoint (Lightbox.Mcp connects here).
            _ipc ??= new Services.IpcServer(new Services.IpcDocumentApi(_vm));
        };
        Closed += async (_, _) =>
        {
            if (_ipc is not null) await _ipc.DisposeAsync();
        };
    }

    // ---- docker geometry -----------------------------------------------------

    private GridLength _timelineHeight = new(280, GridUnitType.Pixel);
    private GridLength _sidebarWidth = new(300, GridUnitType.Pixel);
    private int _sidebarColumn = 4;

    /// <summary>
    /// Keep the grid in step with the VM's docker booleans: collapse rows and
    /// columns for hidden dockers (remembering their dragged size), and move
    /// the sidebar between the left and right column.
    /// </summary>
    private void ApplyDockLayout()
    {
        var rows = RootGrid.RowDefinitions;
        if (rows[2].Height.IsAbsolute && rows[2].Height.Value > 20) _timelineHeight = rows[2].Height;
        if (_vm.ShowTimeline)
        {
            rows[2].MinHeight = 180;
            rows[2].Height = _timelineHeight;
        }
        else
        {
            rows[2].MinHeight = 0;
            rows[2].Height = GridLength.Auto;
        }

        // Work-area columns: 0 toolbar, 1 splitter, 2 + 4 canvas/sidebar
        // (whichever side the sidebar is on), 3 the splitter between them.
        var cols = WorkArea.ColumnDefinitions;
        if (cols[_sidebarColumn].Width.IsAbsolute && cols[_sidebarColumn].Width.Value > 20)
            _sidebarWidth = cols[_sidebarColumn].Width;
        _sidebarColumn = _vm.SidebarOnRight ? 4 : 2;
        var canvasColumn = _vm.SidebarOnRight ? 2 : 4;
        Grid.SetColumn(Sidebar, _sidebarColumn);
        Grid.SetColumn(CanvasHost, canvasColumn);
        cols[canvasColumn].Width = new GridLength(1, GridUnitType.Star);
        cols[canvasColumn].MinWidth = 240;
        if (_vm.SidebarVisible)
        {
            cols[_sidebarColumn].MinWidth = 240;
            cols[_sidebarColumn].Width = _sidebarWidth;
        }
        else
        {
            cols[_sidebarColumn].MinWidth = 0;
            cols[_sidebarColumn].Width = GridLength.Auto;
        }
    }

    /// <summary>Clicking anywhere on a layer-docker row makes that layer active.</summary>
    private void OnLayerRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is LayerRow row)
            _vm.ActivateLayerCommand.Execute(row);
    }

    // ---- layer rename (double-click, both dockers) ---------------------------

    private void OnLayerNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LayerRow row) return;
        row.IsRenaming = true;
        if ((sender as Control)?.Parent is Panel panel)
        {
            var box = panel.Children.OfType<TextBox>().FirstOrDefault();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                box?.Focus();
                box?.SelectAll();
            });
        }
        e.Handled = true;
    }

    private void OnLayerNameLostFocus(object? sender, RoutedEventArgs e)
    {
        // The LostFocus binding has already committed the text by now.
        if ((sender as Control)?.DataContext is LayerRow row) row.IsRenaming = false;
    }

    private void OnLayerNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not LayerRow row) return;
        switch (e.Key)
        {
            case Key.Enter:
                row.Name = box.Text ?? ""; // commit through the row's write-through
                row.IsRenaming = false;
                e.Handled = true;
                break;
            case Key.Escape:
                box.Text = row.Name; // revert, so the LostFocus commit is a no-op
                row.IsRenaming = false;
                e.Handled = true;
                break;
        }
    }

    // ---- timeline cell context menu -----------------------------------------

    private static FrameCell? CellOf(object? sender) => (sender as Control)?.DataContext as FrameCell;

    private void OnInsertKeyframe(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.InsertFrameAt(cell, FrameRole.Key);
    }

    private void OnInsertBreakdown(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.InsertFrameAt(cell, FrameRole.Breakdown);
    }

    private void OnInsertInbetweenFrame(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.InsertFrameAt(cell, FrameRole.Inbetween);
    }

    private void OnSetStartFrame(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.SetPlaybackStart(cell);
    }

    private void OnSetEndFrame(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.SetPlaybackEnd(cell);
    }

    private void OnClearPlaybackRange(object? sender, RoutedEventArgs e) => _vm.ClearPlaybackRange();

    // ---- character sheets -----------------------------------------------------

    private void OnAddReferenceSheet(object? sender, RoutedEventArgs e) => _vm.AddReferenceSheet();

    private void OnAddReferenceView(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Lightbox.Core.Documents.ReferenceSheet sheet)
            _vm.AddReferenceView(sheet);
    }

    private void OnOpenReferenceView(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Lightbox.Core.Documents.ReferenceView view)
            _vm.OpenReferenceView(view);
    }

    private void OnReferenceRenamed(object? sender, RoutedEventArgs e) => _vm.MarkReferenceEdited();

    // ---- brush presets --------------------------------------------------------

    private void OnSavePresetClicked(object? sender, RoutedEventArgs e)
    {
        var preset = _vm.SaveCurrentAsPreset(PresetNameBox.Text ?? "");
        PresetNameBox.Text = "";
        _vm.AiStatus = $"Saved brush preset “{preset.Name}”.";
    }

    private async void OnImportBrushesClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import brushes",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Brush files")
                {
                    Patterns = ["*.abr", "*.gbr", "*.gih", "*.kpp"],
                },
            ],
        });
        if (files.Count == 0) return;

        var payloads = new List<(string, byte[])>();
        foreach (var file in files)
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            payloads.Add((file.Name, memory.ToArray()));
        }
        _vm.ImportBrushFiles(payloads);
    }

    // ---- toolbar ---------------------------------------------------------------

    /// <summary>Map the VM's tool + selection variant onto the canvas input mode.</summary>
    private void SyncCanvasToolMode()
    {
        Canvas.ToolMode = _vm.ActiveTool switch
        {
            ToolId.Fill => Rendering.CanvasControl.CanvasToolMode.Fill,
            ToolId.Select => _vm.ActiveSelectVariant switch
            {
                SelectVariant.Polygon => Rendering.CanvasControl.CanvasToolMode.SelectPolygon,
                SelectVariant.Box => Rendering.CanvasControl.CanvasToolMode.SelectRect,
                SelectVariant.Ellipse => Rendering.CanvasControl.CanvasToolMode.SelectEllipse,
                _ => Rendering.CanvasControl.CanvasToolMode.SelectFreehand,
            },
            _ => Rendering.CanvasControl.CanvasToolMode.Paint,
        };
    }

    /// <summary>
    /// Toolbar width decides its shape: two icon columns when narrow, one
    /// full-width column when widened, icon + tool name when wider still.
    /// </summary>
    private void OnToolbarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        ToolButtons.ItemWidth = width < 96 ? 34 : Math.Max(40, width - 14);
        Toolbar.Classes.Set("labels", width >= 150);
    }

    // Press-and-hold a tool button to list its variants (like Photoshop/Krita).
    private Avalonia.Threading.DispatcherTimer? _holdTimer;
    private bool _variantFlyoutOpened;

    private void OnSelectToolPressed(object? sender, PointerPressedEventArgs e)
    {
        _variantFlyoutOpened = false;
        if (!e.GetCurrentPoint(SelectToolButton).Properties.IsLeftButtonPressed) return;
        _holdTimer?.Stop();
        _holdTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            _variantFlyoutOpened = true;
            SelectToolButton.ContextFlyout?.ShowAt(SelectToolButton);
        };
        _holdTimer.Start();
    }

    private void OnSelectToolReleased(object? sender, PointerReleasedEventArgs e)
    {
        _holdTimer?.Stop();
        // The hold already opened the variant list — don't also register a click.
        if (_variantFlyoutOpened) e.Handled = true;
    }

    private void OnVariantChosen(object? sender, RoutedEventArgs e) =>
        SelectToolButton.ContextFlyout?.Hide();

    // ---- canvas view tools (view-only: never touch the document) -------------

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Canvas.ZoomIn();

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Canvas.ZoomOut();

    private void OnRotateCw(object? sender, RoutedEventArgs e) => Canvas.RotateBy(15);

    private void OnRotateCcw(object? sender, RoutedEventArgs e) => Canvas.RotateBy(-15);

    private void OnToggleMirror(object? sender, RoutedEventArgs e) => Canvas.ToggleMirror();

    private void OnResetView(object? sender, RoutedEventArgs e) => Canvas.ResetView();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Don't hijack keys while the user is typing (layer rename, color hex, AI prompt).
        if (e.Source is TextBox) return;
        switch (e)
        {
            case { Key: Key.Space }:
                _vm.TogglePlaybackCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.Z, KeyModifiers: KeyModifiers.Control }:
                _vm.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.Y, KeyModifiers: KeyModifiers.Control }:
                _vm.RedoCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.Left }:
                _vm.CurrentFrameIndex = Math.Max(0, _vm.CurrentFrameIndex - 1);
                e.Handled = true;
                break;
            case { Key: Key.Right }:
                _vm.CurrentFrameIndex = Math.Min(_vm.Doc.Scene.FrameCount - 1, _vm.CurrentFrameIndex + 1);
                e.Handled = true;
                break;
            case { Key: Key.B, KeyModifiers: KeyModifiers.None }:
                _vm.ActiveTool = ToolId.Brush; // back to the last-configured brush
                e.Handled = true;
                break;
            case { Key: Key.E, KeyModifiers: KeyModifiers.None }:
                _vm.ActiveTool = ToolId.Eraser;
                e.Handled = true;
                break;
            case { Key: Key.F, KeyModifiers: KeyModifiers.None }:
                _vm.ActiveTool = ToolId.Fill;
                e.Handled = true;
                break;
            case { Key: Key.S, KeyModifiers: KeyModifiers.None }:
                _vm.SelectToolCommand.Execute(ToolId.Select); // again = next variant
                e.Handled = true;
                break;
            case { Key: Key.A, KeyModifiers: KeyModifiers.Control }:
                _vm.SelectAllCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.D, KeyModifiers: KeyModifiers.Control }:
                _vm.DeselectCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.I, KeyModifiers: KeyModifiers.Control | KeyModifiers.Shift }:
                _vm.InvertSelectionCommand.Execute(null);
                e.Handled = true;
                break;
            case { Key: Key.Escape }:
                _vm.CancelPolygon();
                break;
            case { Key: Key.M, KeyModifiers: KeyModifiers.None }:
                Canvas.ToggleMirror();
                e.Handled = true;
                break;
            case { Key: Key.D0 or Key.NumPad0, KeyModifiers: KeyModifiers.None }:
                Canvas.ResetView();
                e.Handled = true;
                break;
        }
    }

    private static readonly FilePickerFileType LightboxFileType = new("Lightbox document")
    {
        Patterns = ["*.lightbox.json"],
    };

    private async void OnNewClicked(object? sender, RoutedEventArgs e)
    {
        var settings = await new NewDocumentDialog().ShowDialog<NewDocumentSettings?>(this);
        if (settings is null) return;
        _vm.NewDocument(settings);
    }

    private async void OnCloseTabClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not DocumentTab tab) return;
        if (tab.IsDirty && !await ConfirmDiscardAsync(tab.Title)) return;
        _vm.CloseTab(tab);
    }

    private async Task<bool> ConfirmDiscardAsync(string title)
    {
        var result = false;
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var discard = new Button { Content = "Discard changes", MinWidth = 120 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        discard.Click += (_, _) => { result = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"“{title}” has unsaved changes. Close it anyway?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { discard, cancel },
                },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save animation",
            SuggestedFileName = $"{_vm.ActiveTab?.Title ?? "untitled"}.lightbox.json",
            FileTypeChoices = [LightboxFileType],
        });
        if (file is null) return;
        await using (var stream = await file.OpenWriteAsync())
        await using (var writer = new StreamWriter(stream))
        {
            await writer.WriteAsync(_vm.SerializeDocument());
        }
        _vm.NotifySaved(file.TryGetLocalPath() ?? file.Name);
    }

    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export PNG sequence to folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var dir = folders[0].TryGetLocalPath();
        if (dir is null) return;
        var written = await Task.Run(() => Services.SequenceExporter.ExportPngSequence(_vm.Doc, dir));
        _vm.AiStatus = $"Exported {written.Count} PNG frame(s).";
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open animation",
            AllowMultiple = false,
            FileTypeFilter = [LightboxFileType],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        _vm.OpenDocumentTab(DocJson.Deserialize(json), files[0].TryGetLocalPath());
    }
}
