using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
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
        Canvas.PaintStarted += _vm.BeginStroke;  // (x, y, pressure, alt-erases)
        Canvas.PaintMoved += _vm.MoveStrokeBatch;
        Canvas.PaintEnded += _vm.EndStroke;

        // Tool-aware canvas input (fill clicks, selection shapes) + ants overlay.
        Canvas.FillClicked += _vm.FillAt;
        Canvas.WandClicked += _vm.WandSelectAt;
        Canvas.SelectionShapeDrawn += _vm.ApplySelectionShape;
        Canvas.PolygonVertexAdded += _vm.AddPolygonVertex;
        Canvas.PolygonCompleted += _vm.CompletePolygon;
        _vm.SelectionChanged += () => Canvas.SetSelectionOverlay(_vm.SelectionContours, _vm.PolygonInProgress);
        _vm.LazyBrushMoved += (x, y) => Canvas.SetLazyAnchor(x, y);
        _vm.LazyBrushCleared += () => Canvas.SetLazyAnchor(null, null);
        Canvas.BrushResizeRequested += size => _vm.BrushSize = size;
        Canvas.InputDiagnostic += text => _vm.PenDiagnostic = text;
        // The canvas is the only place that knows how much of the document is
        // actually visible, and how long presenting a frame took.
        Canvas.DisplayScaleChanged += scale => _vm.SetDisplayScale(scale);
        Canvas.FrameRendered += ms => _vm.RecordFrameTime(ms);
        Canvas.CursorPressureChanged += (pressure, penDown) => _vm.SetCursorPressure(pressure, penDown);

        // Transform session: the VM owns the frames, the canvas owns the gizmo.
        _vm.TransformBegun += (minX, minY, maxX, maxY) =>
        {
            Canvas.BeginTransformGizmo(minX, minY, maxX, maxY);
            Canvas.ToolMode = Rendering.CanvasControl.CanvasToolMode.Transform;
        };
        _vm.TransformEnded += () =>
        {
            Canvas.EndTransformGizmo();
            TransformPerspectiveToggle.IsChecked = false; // gizmo resets per session
            SyncCanvasToolMode();
        };
        Canvas.TransformMenuRequested += ShowTransformMenu;
        SyncCanvasToolMode();

        // The camera frame is view-only chrome, so it crosses to the canvas the
        // same way the gizmos do. Null when there is no camera, which is what
        // keeps a sprite document free of camera UI.
        _vm.CameraChanged += () => Canvas.CameraFrame = _vm.CameraFrameCorners;
        Canvas.CameraFrame = _vm.CameraFrameCorners;

        LayersDocker.PointerEntered += (_, _) => _pointerInLayersDocker = true;
        LayersDocker.PointerExited += (_, _) => _pointerInLayersDocker = false;
        TimelineDocker.PointerEntered += (_, _) => _pointerInTimeline = true;
        TimelineDocker.PointerExited += (_, _) => _pointerInTimeline = false;
        Canvas.PickClicked += _vm.PickColorAt;
        Canvas.GradientDragStarted += _vm.BeginGradient;
        Canvas.GradientDragMoved += _vm.MoveGradient;
        Canvas.GradientDragEnded += _vm.EndGradient;
        Canvas.GradientDragCancelled += _vm.CancelGradient;
        _vm.GradientAxisChanged += Canvas.SetGradientAxis;

        // The toggle button eats pointer events, so hook the hold-to-open
        // variant flyout with tunneling handlers.
        SelectToolButton.AddHandler(PointerPressedEvent, OnSelectToolPressed, RoutingStrategies.Tunnel);
        SelectToolButton.AddHandler(PointerReleasedEvent, OnSelectToolReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Timeline cel interactions that need modifiers or drag (buttons eat
        // plain pointer events): Shift+click range select, drag-a-cel drop.
        AddHandler(PointerPressedEvent, OnTimelinePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnCelDragOver);
        AddHandler(DragDrop.DropEvent, OnCelDrop);
        DragDrop.SetAllowDrop(Canvas, true);
        Canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasColorDragOver);
        Canvas.AddHandler(DragDrop.DropEvent, OnCanvasColorDrop);

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
            if (args.PropertyName is nameof(MainViewModel.ColorDockerVisible)
                or nameof(MainViewModel.SheetsDockerVisible))
            {
                ApplySidebarLayout();
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

        _shortcuts.Load();
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
        // The splitter turns the canvas row absolute while dragging; it must
        // go back to star or a reopened timeline lands outside the window.
        rows[0].Height = new GridLength(1, GridUnitType.Star);

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

    /// <summary>
    /// Collapse/restore the sidebar rows of the closable Color and Character
    /// sheets dockers, remembering their dragged sizes.
    /// </summary>
    private GridLength _colorRowHeight = new(1.3, GridUnitType.Star);
    private GridLength _sheetsRowHeight = new(0.7, GridUnitType.Star);

    private void ApplySidebarLayout()
    {
        var rows = SidebarGrid.RowDefinitions;
        ApplyDockerRow(rows[2], _vm.ColorDockerVisible, 160, ref _colorRowHeight);
        ApplyDockerRow(rows[4], _vm.SheetsDockerVisible, 80, ref _sheetsRowHeight);
    }

    private static void ApplyDockerRow(RowDefinition row, bool visible, double minHeight, ref GridLength saved)
    {
        if (visible)
        {
            row.MinHeight = minHeight;
            row.Height = saved;
        }
        else
        {
            if (!row.Height.IsAuto) saved = row.Height;
            row.MinHeight = 0;
            row.Height = GridLength.Auto;
        }
    }

    // Shortcut contexts follow the pointer: the same key can mean different
    // things over the canvas, the timeline, or the Layers docker.
    private bool _pointerInLayersDocker;
    private bool _pointerInTimeline;

    private Services.ShortcutContext CurrentShortcutContext() =>
        _pointerInLayersDocker ? Services.ShortcutContext.LayersDocker
        : _pointerInTimeline ? Services.ShortcutContext.Timeline
        : Services.ShortcutContext.Canvas;

    /// <summary>Clicking anywhere on a layer-docker row makes that layer active.</summary>
    private void OnLayerRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LayerRow row) return;
        _vm.ActivateLayerCommand.Execute(row);
        // Pull keyboard focus off menus/sliders so the arrow-key layer walk
        // (and Delete/Backspace) reaches the window's shortcut handler.
        (sender as Control)?.Focus();
    }

    /// <summary>
    /// Ctrl+click a layer thumbnail selects the layer's visible pixels
    /// (Shift adds, Alt subtracts); a plain click falls through to the row
    /// and activates the layer.
    /// </summary>
    private void OnLayerThumbPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if ((sender as Control)?.DataContext is not LayerRow row) return;
        _vm.SelectLayerAlpha(
            row,
            add: e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            subtract: e.KeyModifiers.HasFlag(KeyModifiers.Alt));
        e.Handled = true;
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

    // ---- layer folder rename / collapse ---------------------------------------

    private void OnGroupCollapseClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is GroupRow row) row.Collapsed = !row.Collapsed;
    }

    private void OnGroupNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not GroupRow row) return;
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

    private void OnGroupNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is GroupRow row) row.IsRenaming = false;
    }

    private void OnGroupNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not GroupRow row) return;
        switch (e.Key)
        {
            case Key.Enter:
                row.Name = box.Text ?? "";
                row.IsRenaming = false;
                e.Handled = true;
                break;
            case Key.Escape:
                box.Text = row.Name;
                row.IsRenaming = false;
                e.Handled = true;
                break;
        }
    }

    // ---- layer docker context menus (menu items inherit the row's DataContext) ----

    private static LayerRow? LayerRowOf(object? sender) =>
        (sender as Control)?.DataContext as LayerRow;

    private static GroupRow? GroupRowOf(object? sender) =>
        (sender as Control)?.DataContext as GroupRow;

    private void OnLayerMenuRename(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) row.IsRenaming = true;
    }

    private void OnLayerMenuMoveUp(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.MoveLayerUpCommand.Execute(row);
    }

    private void OnLayerMenuMoveDown(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.MoveLayerDownCommand.Execute(row);
    }

    private void OnLayerMenuNewFolder(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is not { } row) return;
        _vm.ActivateLayerCommand.Execute(row);
        _vm.CreateLayerFolderCommand.Execute(null);
    }

    private void OnLayerMenuRemoveFromFolder(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.RemoveLayerFromGroupCommand.Execute(row);
    }

    private void OnLayerMenuSelectAlpha(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SelectLayerAlpha(row, add: false, subtract: false);
    }

    private void OnLayerMenuBlank(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.ClearLayerContent(row.Layer);
    }

    private void OnLayerMenuDelete(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.DeleteLayer(row.Layer);
    }

    private void OnGroupMenuRename(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) row.IsRenaming = true;
    }

    private void OnGroupColorClicked(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row && (sender as Control)?.Tag is string hex)
            row.Color = hex;
    }

    private void OnGroupMenuCollapse(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) row.Collapsed = !row.Collapsed;
    }

    private void OnGroupMenuAddActive(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) _vm.AddActiveLayerToGroupCommand.Execute(row);
    }

    private void OnGroupMenuDissolve(object? sender, RoutedEventArgs e)
    {
        if (GroupRowOf(sender) is { } row) _vm.DissolveGroupCommand.Execute(row);
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

    // ---- exposure editing + cel clipboard (context menu) ----------------------

    private void OnExtendExposure(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.ExtendExposureAt(cell);
    }

    private void OnReduceExposure(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.ReduceExposureAt(cell);
    }

    private void OnClearCel(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.ClearCelAt(cell);
    }

    private void OnCopyCel(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.CopyCel(cell);
    }

    private void OnCutCel(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.CutCel(cell);
    }

    private void OnPasteCel(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.PasteCel(cell);
    }

    // ---- multi-cel range selection (Shift+click) --------------------------------

    private static FrameCell? CellUnder(object? source) =>
        (source as Control)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext as FrameCell;

    private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CellUnder(e.Source) is not { } cell) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _vm.RangeSelectTo(cell);
            e.Handled = true; // don't also fire the cell's click (which clears the range)
            return;
        }
        // Remember the press so a later move can turn it into a cel drag.
        if (cell.IsKeyed && !cell.IsVirtual)
        {
            _celDragCandidate = cell;
            _celDragPress = e;
            _celDragStart = e.GetPosition(this);
        }
    }

    // ---- drag a cel along its row ------------------------------------------------

    private static readonly DataFormat<FrameCell> CelDragFormat =
        DataFormat.CreateInProcessFormat<FrameCell>("lightbox-cel");

    private FrameCell? _celDragCandidate;
    private PointerPressedEventArgs? _celDragPress;
    private Avalonia.Point _celDragStart;
    private bool _celDragging;

    private async void OnCellPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_celDragging || _celDragCandidate is not { } cell || _celDragPress is not { } press) return;
        if (sender is not Button button || !ReferenceEquals(button.DataContext, cell)) return;
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _celDragCandidate = null;
            _celDragPress = null;
            return;
        }
        var delta = point.Position - _celDragStart;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;

        _celDragging = true;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(CelDragFormat, cell));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            _celDragging = false;
            _celDragCandidate = null;
            _celDragPress = null;
        }
    }

    private static FrameCell? DraggedCelOf(DragEventArgs e) =>
        e.DataTransfer is { } transfer ? transfer.TryGetValue(CelDragFormat) : null;

    private void OnCelDragOver(object? sender, DragEventArgs e)
    {
        if (DraggedCelOf(e) is not { } source || CellUnder(e.Source) is not { } target
            || target.LayerIndex != source.LayerIndex)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? DragDropEffects.Copy
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnCelDrop(object? sender, DragEventArgs e)
    {
        if (DraggedCelOf(e) is not { } source || CellUnder(e.Source) is not { } target) return;
        _vm.MoveCel(source, target, copy: e.KeyModifiers.HasFlag(KeyModifiers.Control));
        e.Handled = true;
    }

    // ---- frame markers -------------------------------------------------------------

    private async void OnEditMarker(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is not { } cell) return;
        var existing = _vm.MarkerAt(cell.Index);

        var dialog = new Window
        {
            Title = $"Marker on frame {cell.Index + 1}",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var labelBox = new TextBox { Text = existing?.Label ?? "", PlaceholderText = "Label (e.g. “walk starts”)" };
        var chosenColor = existing?.Color ?? "#e0a030";
        var swatches = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        foreach (var hex in new[] { "#e0a030", "#e05555", "#4caf50", "#4a6ea9", "#b05ac9", "#20b2aa" })
        {
            // Plain buttons with a white ring on the chosen one — a checked
            // ToggleButton's theme background would hide the swatch color.
            var swatch = new Button
            {
                Width = 30,
                Height = 24,
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex)),
                BorderThickness = new Avalonia.Thickness(2),
                BorderBrush = hex == chosenColor ? Avalonia.Media.Brushes.White : Avalonia.Media.Brushes.Transparent,
            };
            swatch.Click += (_, _) =>
            {
                chosenColor = hex;
                foreach (var other in swatches.Children.OfType<Button>())
                {
                    other.BorderBrush = Avalonia.Media.Brushes.Transparent;
                }
                swatch.BorderBrush = Avalonia.Media.Brushes.White;
            };
            swatches.Children.Add(swatch);
        }
        var ok = new Button { Content = "Save marker", MinWidth = 110, IsDefault = true };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        var save = false;
        ok.Click += (_, _) => { save = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(14),
            Spacing = 10,
            Children =
            {
                labelBox,
                swatches,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { ok, cancel },
                },
            },
        };
        await dialog.ShowDialog(this);
        if (save) _vm.SetMarkerAt(cell.Index, labelBox.Text ?? "", chosenColor);
    }

    private void OnRemoveMarker(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.RemoveMarkerAt(cell.Index);
    }

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

    // ---- palette ---------------------------------------------------------------

    private static readonly FilePickerFileType GplFileType = new("GIMP palette")
    {
        Patterns = ["*.gpl"],
    };

    private async void OnImportPaletteClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import palette",
            AllowMultiple = false,
            FileTypeFilter = [GplFileType],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        _vm.PaletteDocker.ImportGpl(path);
    }

    private async void OnExportPaletteClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.PaletteDocker.SelectedPalette is not { } palette) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export palette",
            SuggestedFileName = $"{palette.Name}.gpl",
            FileTypeChoices = [GplFileType],
        });
        if (file?.TryGetLocalPath() is not { } path) return;
        _vm.PaletteDocker.ExportGpl(path);
    }

    // ---- toolbar ---------------------------------------------------------------

    /// <summary>Map the VM's tool + selection variant onto the canvas input mode.</summary>
    private void SyncCanvasToolMode()
    {
        Canvas.ToolMode = _vm.ActiveTool switch
        {
            ToolId.Fill => Rendering.CanvasControl.CanvasToolMode.Fill,
            ToolId.Picker => Rendering.CanvasControl.CanvasToolMode.Pick,
            ToolId.Gradient => Rendering.CanvasControl.CanvasToolMode.Gradient,
            ToolId.Select => _vm.ActiveSelectVariant switch
            {
                SelectVariant.Polygon => Rendering.CanvasControl.CanvasToolMode.SelectPolygon,
                SelectVariant.Box => Rendering.CanvasControl.CanvasToolMode.SelectRect,
                SelectVariant.Ellipse => Rendering.CanvasControl.CanvasToolMode.SelectEllipse,
                SelectVariant.Wand => Rendering.CanvasControl.CanvasToolMode.SelectWand,
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

    /// <summary>Brush-parameter flyout: categories on the left, one page visible at a time.</summary>
    private void OnBrushCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BrushPageGeneral is null) return; // template not built yet
        var index = BrushCategoryList.SelectedIndex;
        BrushPageGeneral.IsVisible = index == 0;
        BrushPageEffects.IsVisible = index == 1;
        BrushPageMedium.IsVisible = index == 2;
        BrushPagePressure.IsVisible = index == 3;
        BrushPagePresets.IsVisible = index == 4;
    }

    // ---- drag a colour onto the canvas to fill --------------------------------

    private static readonly DataFormat<string> ColorDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-color");

    /// <summary>
    /// Start dragging the current colour. Dropping it on the canvas fills
    /// there — the shortest path from "I chose this colour" to "that shape is
    /// that colour", without visiting the tool bar on the way.
    /// </summary>
    private async void OnColorSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ColorDragFormat, _vm.ColorHex));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("color-drag", ex);
        }
    }

    private static string? DraggedColorOf(DragEventArgs e) =>
        e.DataTransfer is { } transfer ? transfer.TryGetValue(ColorDragFormat) : null;

    private void OnCanvasColorDragOver(object? sender, DragEventArgs e)
    {
        if (DraggedColorOf(e) is null) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnCanvasColorDrop(object? sender, DragEventArgs e)
    {
        if (DraggedColorOf(e) is not { } hex) return;

        var (x, y) = Canvas.ViewToDoc(e.GetPosition(Canvas));
        _vm.DropColorAt(hex, x, y);
        e.Handled = true;
    }

    // ---- canvas view tools (view-only: never touch the document) -------------

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Canvas.ZoomIn();

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Canvas.ZoomOut();

    private void OnRotateCw(object? sender, RoutedEventArgs e) => Canvas.RotateBy(15);

    private void OnRotateCcw(object? sender, RoutedEventArgs e) => Canvas.RotateBy(-15);

    private void OnToggleMirror(object? sender, RoutedEventArgs e) => Canvas.ToggleMirror();

    private void OnResetView(object? sender, RoutedEventArgs e) => Canvas.ResetView();

    private readonly Services.ShortcutMap _shortcuts = new();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Don't hijack keys while the user is typing (layer rename, color hex, AI prompt).
        if (e.Source is TextBox) return;

        // An active transform session owns Enter/Escape outright.
        if (_vm.TransformActive)
        {
            if (e.Key == Key.Enter)
            {
                CommitTransformFromGizmo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                _vm.CancelTransform();
                e.Handled = true;
                return;
            }
        }

        switch (_shortcuts.IdFor(e, CurrentShortcutContext()))
        {
            case "canvas.transform":
                if (!_vm.TransformActive) _vm.BeginTransform();
                break;
            case "canvas.pickColor":
                _vm.ActiveTool = ToolId.Picker;
                break;
            case "timeline.insertKey":
                _vm.InsertKeyframeAtPlayhead();
                break;
            case "timeline.playPause":
                _vm.TogglePlaybackCommand.Execute(null);
                break;
            case "canvas.undo":
                _vm.UndoCommand.Execute(null);
                break;
            case "canvas.redo":
                _vm.RedoCommand.Execute(null);
                break;
            case "timeline.prevFrame":
                _vm.CurrentFrameIndex = Math.Max(0, _vm.CurrentFrameIndex - 1);
                break;
            case "timeline.nextFrame":
                _vm.CurrentFrameIndex = Math.Min(_vm.Doc.Scene.FrameCount - 1, _vm.CurrentFrameIndex + 1);
                break;
            case "tool.brush":
                _vm.ActiveTool = ToolId.Brush; // back to the last-configured brush
                break;
            case "tool.eraser":
                _vm.ActiveTool = ToolId.Eraser;
                break;
            case "tool.fill":
                _vm.ActiveTool = ToolId.Fill;
                break;
            case "tool.gradient":
                _vm.ActiveTool = ToolId.Gradient;
                break;
            case "tool.select":
                _vm.SelectToolCommand.Execute(ToolId.Select); // again = next variant
                break;
            case "select.all":
                _vm.SelectAllCommand.Execute(null);
                break;
            case "timeline.copyCel":
                _vm.CopyCurrentCel();
                break;
            case "timeline.cutCel":
                _vm.CutCurrentCel();
                break;
            case "timeline.pasteCel":
                _vm.PasteCurrentCel();
                break;
            case "select.none":
                _vm.DeselectCommand.Execute(null);
                break;
            case "select.invert":
                _vm.InvertSelectionCommand.Execute(null);
                break;
            case "select.cancel":
                _vm.CancelPolygon();
                _vm.CancelGradient();
                return; // leave Escape unhandled so open flyouts still close
            case "docker.deleteLayer":
                _vm.DeleteActiveLayerCommand.Execute(null);
                break;
            case "docker.clearLayer":
                _vm.ClearActiveLayerCommand.Execute(null);
                break;
            // Flipping: hop between key drawings without leaving the pen.
            case "timeline.prevKey":
                _vm.PreviousKeyframeCommand.Execute(null);
                break;
            case "timeline.nextKey":
                _vm.NextKeyframeCommand.Execute(null);
                break;
            case "canvas.nudgeLeft":
                _vm.NudgeSelection(-1, 0);
                break;
            case "canvas.nudgeRight":
                _vm.NudgeSelection(1, 0);
                break;
            case "canvas.nudgeUp":
                _vm.NudgeSelection(0, -1);
                break;
            case "canvas.nudgeDown":
                _vm.NudgeSelection(0, 1);
                break;
            // Layer walking: rows show topmost first, so "above" is a higher scene index.
            case "docker.layerAbove":
                _vm.ActiveLayerIndex = Math.Min(_vm.Doc.Scene.Layers.Count - 1, _vm.ActiveLayerIndex + 1);
                break;
            case "docker.layerBelow":
                _vm.ActiveLayerIndex = Math.Max(0, _vm.ActiveLayerIndex - 1);
                break;
            case "canvas.mirror":
                Canvas.ToggleMirror();
                break;
            case "canvas.resetView":
                Canvas.ResetView();
                break;
            default:
                return; // unbound or context-gated: not ours
        }
        e.Handled = true;
    }

    private async void OnConfigureClicked(object? sender, RoutedEventArgs e) =>
        await new ConfigureWindow(_shortcuts, _vm).ShowDialog(this);

    // ---- transform session (window side) --------------------------------------

    /// <summary>Read the gizmo and commit through the matching VM path.</summary>
    private void CommitTransformFromGizmo()
    {
        if (Canvas.TransformIsIdentity)
        {
            _vm.CancelTransform(); // nothing changed — don't record an undo step
            return;
        }
        if (Canvas.TransformIsPerspectiveResult)
        {
            var (src, dst) = Canvas.TransformQuadResult;
            _vm.CommitTransformPerspective(src, dst);
        }
        else
        {
            var (px, py, sx, sy, angle, dx, dy) = Canvas.TransformAffineResult;
            _vm.CommitTransformAffine(px, py, sx, sy, angle, dx, dy);
        }
    }

    private void OnTransformPerspectiveToggled(object? sender, RoutedEventArgs e) =>
        Canvas.TransformPerspective = TransformPerspectiveToggle.IsChecked == true;

    private void OnTransformMirrorH(object? sender, RoutedEventArgs e) =>
        Canvas.MirrorTransformGizmo(horizontal: true);

    private void OnTransformMirrorV(object? sender, RoutedEventArgs e) =>
        Canvas.MirrorTransformGizmo(horizontal: false);

    private void OnTransformReset(object? sender, RoutedEventArgs e) => Canvas.ResetTransformGizmo();

    private void OnTransformApply(object? sender, RoutedEventArgs e) => CommitTransformFromGizmo();

    private void OnTransformCancel(object? sender, RoutedEventArgs e) => _vm.CancelTransform();

    /// <summary>Right-click on the canvas during a transform: the options menu.</summary>
    private void ShowTransformMenu(Avalonia.Point viewPos)
    {
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        var menu = new ContextMenu
        {
            ItemsSource = new Control[]
            {
                Item("Apply transform (Enter)", CommitTransformFromGizmo),
                Item("Cancel (Esc)", _vm.CancelTransform),
                new Separator(),
                Item("Mirror horizontally", () => Canvas.MirrorTransformGizmo(horizontal: true)),
                Item("Mirror vertically", () => Canvas.MirrorTransformGizmo(horizontal: false)),
                new Separator(),
                Item(Canvas.TransformPerspective ? "Box mode (affine)" : "Perspective mode (free corners)",
                    () =>
                    {
                        Canvas.TransformPerspective = !Canvas.TransformPerspective;
                        TransformPerspectiveToggle.IsChecked = Canvas.TransformPerspective;
                    }),
                Item("Reset transform", Canvas.ResetTransformGizmo),
            },
            Placement = PlacementMode.Pointer,
        };
        menu.Open(Canvas);
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
