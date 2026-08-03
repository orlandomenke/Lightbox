using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Core.Projects;
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
        Canvas.PaintStarted += _vm.BeginStroke;  // (x, y, pressure, alt-erases, shift-joins)
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
        // The gizmo is the authority on the shape of the drag; the view model
        // owns the pixels. Feeding the matrix across on every gizmo change is
        // what makes the drawing move with the box instead of after it.
        Canvas.TransformGizmoChanged += () => _vm.PreviewTransform(Canvas.TransformMatrix);
        Canvas.TransformMenuRequested += ShowTransformMenu;
        WireGradientRamp();
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
        Canvas.ShapeDragStarted += _vm.BeginShape;
        Canvas.ShapeDragMoved += _vm.MoveShape;
        Canvas.ShapeDragEnded += _vm.EndShape;
        Canvas.GradientDragCancelled += _vm.CancelGradient;
        _vm.GradientAxisChanged += Canvas.SetGradientAxis;

        Canvas.ReferenceDragged += (dx, dy, wholeSheet) =>
        {
            if (wholeSheet) _vm.NudgeReference(dx, dy);
            else _vm.NudgeReferenceCell(dx, dy);
        };
        Canvas.Bind(
            Rendering.CanvasControl.ReferenceAlignModeProperty,
            new Avalonia.Data.Binding(nameof(ViewModels.MainViewModel.ReferenceAlignMode)) { Source = _vm });

        // The wheel inside the palette panel is the one place a duplicate is
        // taken at face value: somebody working in the palette who asks for a
        // second copy of a colour wants one, usually to file the two under
        // different folders.
        PaletteSwatchField.Picker.AddIntent = ViewModels.PaletteAddIntent.Allow;

        // The grid gizmos. The canvas owns the gesture — which corner, which
        // box, how far — and the view model owns what it means to the document.
        Canvas.ReferenceBoxPicked += index => _vm.SelectedReferenceCell = index;
        Canvas.ReferenceBoxMoved += (index, dx, dy) => _vm.MoveReferenceCell(index, dx, dy);
        Canvas.ReferenceBoxResized += (index, left, top, dx, dy) =>
            _vm.ResizeReferenceCell(index, left, top, dx, dy);
        Canvas.ReferenceBoxDrawn += (x, y, w, h) => _vm.AddReferenceCell(x, y, w, h);
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.ReferenceGridEditMode)
                or nameof(MainViewModel.SelectedReferenceCell)
                or nameof(MainViewModel.ActiveReferenceIndex))
            {
                RefreshReferenceBoxes();
            }
        };
        _vm.ReferenceChanged += RefreshReferenceBoxes;
        _vm.GuidesChanged += RefreshGuides;
        InitialiseRulers();

        // Right-click on the scrub bar: the one thing worth offering there is
        // giving the loop range back, so it is one item rather than a menu.
        Ruler.RangeMenuRequested += (_, _) =>
        {
            var menu = new ContextMenu();
            var reset = new MenuItem { Header = "Reset playback range" };
            reset.Click += (_, _) => Ruler.ResetRange();
            menu.Items.Add(reset);
            menu.Open(Ruler);
        };

        // The toggle button eats pointer events, so hook the hold-to-open
        // variant flyout with tunneling handlers.
        SelectToolButton.AddHandler(PointerPressedEvent, OnSelectToolPressed, RoutingStrategies.Tunnel);
        SelectToolButton.AddHandler(PointerReleasedEvent, OnSelectToolReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        ShapeToolButton.AddHandler(PointerPressedEvent, OnShapeToolPressed, RoutingStrategies.Tunnel);
        ShapeToolButton.AddHandler(PointerReleasedEvent, OnSelectToolReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Timeline cel interactions that need modifiers or drag (buttons eat
        // plain pointer events): Shift+click range select, drag-a-cel drop.
        AddHandler(PointerPressedEvent, OnTimelinePointerPressed, RoutingStrategies.Tunnel);
        // Both disarm the cel drag; see OnTimelineContextRequested for why the
        // context menu has to. Tunnelling so they are seen before anything can
        // mark the event handled.
        AddHandler(ContextRequestedEvent, OnTimelineContextRequested, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTimelinePointerReleased, RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnCelDragOver);
        AddHandler(DragDrop.DropEvent, OnCelDrop);
        DragDrop.SetAllowDrop(Canvas, true);
        Canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasColorDragOver);
        Canvas.AddHandler(DragDrop.DropEvent, OnCanvasColorDrop);
        Canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasSymbolDragOver);
        Canvas.AddHandler(DragDrop.DropEvent, OnCanvasSymbolDrop);

        // Two things move a panel in or out of a strip without the layout
        // changing: a project appearing (the project panel is absent until
        // there is one) and a reference tab taking the focus (the timeline
        // means nothing on one). Both are answered the same way as everything
        // else — rebuild from the layout.
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.HasProject)
                or nameof(MainViewModel.ShowTimeline))
            {
                ApplyDockLayout();
            }
            if (args.PropertyName is nameof(MainViewModel.ActiveTool)
                or nameof(MainViewModel.ActiveSelectVariant))
            {
                SyncCanvasToolMode();
                RefreshGuideGrab();
            }
            // The brush and the eraser keep separate settings, and a preset
            // replaces the lot — so the tip button has to follow the switch or
            // it shows the shape of a brush that is no longer selected.
            if (args.PropertyName is nameof(MainViewModel.BrushTipId)
                or nameof(MainViewModel.ActiveTool)
                or nameof(MainViewModel.BrushSize))
            {
                RefreshTipButton();
            }
            // B and E switch brushes without going near the picker, and a
            // preset applied from a shortcut has to move the button's label.
            if (args.PropertyName is nameof(MainViewModel.SelectedBrushPreset))
            {
                RefreshBrushPickerButton();
                RefreshPresetPage();
            }
        };
        _vm.Workspace.Changed += ApplyDockLayout;
        InitialisePanels();
        InitialiseOverlays();
        ApplyDockLayout();
        // Both buttons show state the view model restored in its constructor,
        // before anything here was subscribed — so the first paint has to be
        // asked for rather than waited for.
        RefreshBrushPickerButton();
        RefreshTipButton();
        // The bars are positioned as a fraction of the canvas, so they have to
        // be replaced whenever the canvas changes size.
        CanvasHost.SizeChanged += (_, _) => ApplyOverlayLayout();

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
        RecentMenu.SubmenuOpened += (_, _) => RefreshRecentMenu();
        ConvertProjectMenu.SubmenuOpened += (_, _) => RefreshConvertMenu();
        TemplatesMenu.SubmenuOpened += (_, _) => RefreshTemplatesMenu();
        Loaded += (_, _) =>
        {
            _vm.PublishSnapshot();
            // MCP bridge endpoint (Lightbox.Mcp connects here).
            _ipc ??= new Services.IpcServer(new Services.IpcDocumentApi(_vm));
            // The backend is only known once a frame has actually been
            // presented, which is after Loaded on every platform.
            _vm.NoteGraphicsBackend();
        };
        Closed += async (_, _) =>
        {
            if (_ipc is not null) await _ipc.DisposeAsync();
        };
    }

    // ---- the workspace -------------------------------------------------------

    /// <summary>
    /// Every panel, once. They are created by the XAML into
    /// <c>PanelPool</c> and moved between strips from there; nothing here ever
    /// builds a panel, so a panel keeps its scroll position, its bindings and
    /// any half-typed value across a drag.
    /// </summary>
    private readonly Dictionary<DockPanelId, Docker> _panels = [];

    private readonly Dictionary<DockPanelId, FloatingPanelWindow> _floating = [];

    private void InitialisePanels()
    {
        foreach (var panel in PanelPool.Children.OfType<Docker>().ToList())
        {
            _panels[panel.PanelId] = panel;
            panel.SwitchTargets ??= WorkspaceViewModel.SwitchTargetsFor(panel.PanelId);
            panel.SwitchRequested += (from, to) => _vm.Workspace.Swap(from.PanelId, to);
            panel.PanelDragStarted += BeginPanelDrag;
        }
        foreach (var strip in Strips())
        {
            strip.Value.Side = strip.Key;
            strip.Value.ExtentsChanged += () => _vm.Workspace.Touch();
        }
    }

    private IEnumerable<KeyValuePair<DockSide, DockStrip>> Strips()
    {
        yield return new(DockSide.Left, LeftStrip);
        yield return new(DockSide.Right, RightStrip);
        yield return new(DockSide.Top, TopStrip);
        yield return new(DockSide.Bottom, BottomStrip);
    }

    /// <summary>
    /// Rebuild every strip from the layout.
    ///
    /// Wholesale rather than incremental on purpose: reparenting seven controls
    /// costs nothing next to a layout pass, and it means the window never has
    /// to work out what changed — only what the layout now says. Docking bugs
    /// that survive this are bugs in the model, which is tested without a
    /// window at all.
    /// </summary>
    private void ApplyDockLayout()
    {
        var layout = _vm.Workspace.Layout;

        foreach (var (side, strip) in Strips())
        {
            var panels = layout.PanelsIn(side).Where(IsPanelUsable).Select(id => _panels[id]).ToList();
            foreach (var panel in panels) Detach(panel);
            strip.Rebuild(panels, layout);
            // The cap comes from the panels actually shown, not from the ones
            // the layout lists: a project panel with no project is not in the
            // strip, so it should not be lifting the strip's ceiling.
            SizeArea(side, layout, DockPanels.CapOf(panels.Select(p => p.PanelId)), panels.Count > 0);
        }

        // Anything not in a strip goes back to the pool, or into its own
        // window. Parking rather than destroying is what makes closing a panel
        // and reopening it a no-op rather than a reset.
        foreach (var (id, panel) in _panels)
        {
            var side = layout.SideOf(id);
            if (side == DockSide.Floating && IsPanelUsable(id)) ShowFloating(id, panel, layout);
            else if (panel.Parent is null) Park(panel);
        }
        foreach (var id in _floating.Keys.ToList())
        {
            if (layout.SideOf(id) != DockSide.Floating || !IsPanelUsable(id)) CloseFloating(id);
        }

        ApplyOverlayLayout();
    }

    // ---- the bars on the canvas -----------------------------------------------

    private IEnumerable<(OverlayId Id, CanvasOverlayBar Bar)> OverlayBars() =>
    [
        (OverlayId.View, ViewBar),
        (OverlayId.Shortcuts, ShortcutBar),
    ];

    private void InitialiseOverlays()
    {
        foreach (var (_, bar) in OverlayBars())
        {
            bar.DragHost = CanvasHost;
            // Live, not just on release. A bar that only jumps when you let go
            // makes you drop it to find out where it would land, which is not
            // a drag — it is a guess followed by an undo.
            bar.Dragging += (b, at) => PositionOverlay(b, EdgeAt(at), AlongAt(at));
            bar.Dropped += (b, at) =>
                _vm.Workspace.PlaceOverlay(b.OverlayId, EdgeAt(at), AlongAt(at));
            bar.CloseRequested += b => _vm.Workspace.SetOverlayVisible(b.OverlayId, false);
            bar.PropertyChanged += (_, e) =>
            {
                if (e.Property == CanvasOverlayBar.CollapsedProperty)
                {
                    _vm.Workspace.SetOverlayCollapsed(bar.OverlayId, bar.Collapsed);
                }
            };
        }
    }

    private CanvasEdge EdgeAt(Point at) =>
        CanvasOverlayLayout.NearestEdge(at.X, at.Y, CanvasHost.Bounds.Width, CanvasHost.Bounds.Height);

    private double AlongAt(Point at) =>
        CanvasOverlayLayout.AlongFor(
            EdgeAt(at), at.X, at.Y, CanvasHost.Bounds.Width, CanvasHost.Bounds.Height);

    /// <summary>Read the workspace and put every bar where it says.</summary>
    private void ApplyOverlayLayout()
    {
        var overlays = _vm.Workspace.Layout.Overlays;
        foreach (var (id, bar) in OverlayBars())
        {
            var placement = overlays.Place(id);
            bar.IsVisible = placement.Visible;
            if (!placement.Visible) continue;
            bar.Collapsed = placement.Collapsed;
            PositionOverlay(bar, placement.Edge, placement.Along);
        }
    }

    /// <summary>
    /// Put one bar on an edge without touching the workspace — the live half
    /// of a drag, and the mechanism the committed placement uses too.
    /// </summary>
    /// <remarks>
    /// Alignment pins the bar to its edge. The other axis is a lopsided
    /// margin: a fraction of the room left over once the bar's own size is
    /// taken out, which is what turns "0.25 along the top" into a position
    /// without the bar ever hanging off the end.
    /// </remarks>
    private void PositionOverlay(CanvasOverlayBar bar, CanvasEdge edge, double along)
    {
        bar.Edge = edge;
        var vertical = CanvasOverlayLayout.IsVertical(edge);
        const double Gap = 8;

        bar.HorizontalAlignment = edge switch
        {
            CanvasEdge.Right => Avalonia.Layout.HorizontalAlignment.Right,
            _ => Avalonia.Layout.HorizontalAlignment.Left,
        };
        bar.VerticalAlignment = edge switch
        {
            CanvasEdge.Bottom => Avalonia.Layout.VerticalAlignment.Bottom,
            _ => Avalonia.Layout.VerticalAlignment.Top,
        };

        var slack = vertical
            ? Math.Max(0, CanvasHost.Bounds.Height - 2 * Gap - bar.Bounds.Height)
            : Math.Max(0, CanvasHost.Bounds.Width - 2 * Gap - bar.Bounds.Width);
        var offset = Gap + Math.Clamp(along, 0, 1) * slack;
        bar.Margin = vertical
            ? new Thickness(Gap, offset, Gap, Gap)
            : new Thickness(offset, Gap, Gap, Gap);
    }

    /// <summary>
    /// Whether a panel makes sense right now, regardless of where the layout
    /// puts it: the project tree needs a project, and the timeline means
    /// nothing on a reference tab.
    /// </summary>
    private bool IsPanelUsable(DockPanelId id) => id switch
    {
        DockPanelId.Project => _vm.HasProject,
        // Symbols are *not* gated. A project symbol needs a project, but the
        // artist's own library does not — it is theirs, and it should be there
        // when they open the app to draw one picture. Placing one into a loose
        // document copies it into Doc.Symbols, so the file still stands alone.
        // The project tree above stays gated, because without a project it has
        // literally nothing to show.
        DockPanelId.Timeline => _vm.ShowTimeline,
        _ => true,
    };

    private static void Detach(Control child)
    {
        switch (child.Parent)
        {
            case Panel panel: panel.Children.Remove(child); break;
            case ContentControl host when ReferenceEquals(host.Content, child): host.Content = null; break;
            case Decorator d when ReferenceEquals(d.Child, child): d.Child = null; break;
        }
    }

    private void Park(Docker panel)
    {
        Detach(panel);
        if (!PanelPool.Children.Contains(panel)) PanelPool.Children.Add(panel);
    }

    /// <summary>
    /// Open or collapse an edge. "Optional means absent, not disabled": an area
    /// with nothing in it takes no width, shows no splitter and costs no
    /// layout, so a workspace that never uses the left edge looks exactly like
    /// one that could not have.
    /// </summary>
    private void SizeArea(DockSide side, DockLayout layout, double? cap, bool occupied)
    {
        var extent = layout.AreaExtents.TryGetValue(side, out var saved) && saved > 40
            ? saved
            : side is DockSide.Left or DockSide.Right ? 300 : 280;

        switch (side)
        {
            case DockSide.Left:
                Collapse(LeftHost, LeftSplitter, occupied);
                SizeColumn(WorkArea.ColumnDefinitions[2], occupied, extent, cap);
                break;
            case DockSide.Right:
                Collapse(RightHost, RightSplitter, occupied);
                SizeColumn(WorkArea.ColumnDefinitions[6], occupied, extent, cap);
                break;
            case DockSide.Top:
                Collapse(TopHost, TopSplitter, occupied);
                SizeRow(RootGrid.RowDefinitions[0], occupied, extent, cap);
                break;
            default:
                Collapse(BottomHost, BottomSplitter, occupied);
                SizeRow(RootGrid.RowDefinitions[4], occupied, extent, cap);
                break;
        }
    }

    private static void Collapse(Control host, Control splitter, bool occupied)
    {
        host.IsVisible = occupied;
        splitter.IsVisible = occupied;
    }

    private static void SizeColumn(ColumnDefinition col, bool occupied, double extent, double? cap)
    {
        if (!occupied)
        {
            col.MinWidth = 0;
            col.MaxWidth = double.PositiveInfinity;
            col.Width = new GridLength(0, GridUnitType.Pixel);
            return;
        }
        col.MinWidth = 180;
        // A capped strip holds only fixed-size controls, so widening it just
        // adds whitespace. Uncapped panels — the layer stack, the project tree
        // — genuinely use the room, and remove the ceiling for the whole strip.
        col.MaxWidth = cap ?? double.PositiveInfinity;
        col.Width = new GridLength(cap is { } c ? Math.Min(extent, c) : extent, GridUnitType.Pixel);
    }

    private static void SizeRow(RowDefinition row, bool occupied, double extent, double? cap)
    {
        if (!occupied)
        {
            row.MinHeight = 0;
            row.MaxHeight = double.PositiveInfinity;
            row.Height = new GridLength(0, GridUnitType.Pixel);
            return;
        }
        row.MinHeight = 120;
        row.MaxHeight = cap ?? double.PositiveInfinity;
        row.Height = new GridLength(cap is { } c ? Math.Min(extent, c) : extent, GridUnitType.Pixel);
    }

    /// <summary>
    /// A one-field modal. Returns null when the user cancels — which is not
    /// the same as an empty string, and the callers rely on the difference.
    /// </summary>
    private async Task<string?> PromptForText(string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, PlaceholderText = label };
        string? answer = null;

        var dialog = new Window
        {
            Title = title,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var ok = new Button { Content = "Save", IsDefault = true, MinWidth = 72 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 72 };
        ok.Click += (_, _) =>
        {
            answer = box.Text ?? "";
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, Opacity = 0.8 },
                box,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 6,
                    Children = { cancel, ok },
                },
            },
        };
        box.Focus();
        await dialog.ShowDialog(this);
        return answer;
    }

    private void OnAutosaveOff(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 0;

    private void OnAutosaveHalfMinute(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 0.5;

    private void OnAutosaveMinute(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 1;

    private void OnAutosaveFiveMinutes(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 5;

    private void OnAutosaveFifteenMinutes(object? sender, RoutedEventArgs e) => _vm.AutosaveMinutes = 15;

    // ---- the gradient ramp editor ------------------------------------------------

    /// <summary>
    /// The ramp draws and reports; every edit it reports lands in the view
    /// model, and therefore in the undo history. A control that mutated the
    /// document directly would make dragging a stop the one change Ctrl+Z
    /// could not reach.
    /// </summary>
    /// <remarks>
    /// Found by name at first use rather than wired at construction: the ramp
    /// lives inside a Flyout, and a flyout's content is not built until it is
    /// first opened.
    /// </remarks>

    private void WireGradientRamp()
    {
        // The panel's copy exists from the start; the toolbar's lives in a
        // flyout, whose content is not built until it is first opened.
        Bind(PanelRampEditor);
        if (GradientPreviewButton.Flyout is Flyout flyout)
        {
            flyout.Opened += (_, _) =>
            {
                if (flyout.Content is Control content
                    && content.FindControl<GradientRamp>("RampEditor") is { } ramp)
                {
                    Bind(ramp);
                }
            };
        }
    }

    private readonly HashSet<GradientRamp> _wiredRamps = [];

    private void Bind(GradientRamp? ramp)
    {
        if (ramp is null || !_wiredRamps.Add(ramp)) return;
        var gradients = _vm.GradientDocker;
        ramp.StopAdded += (track, at) => gradients.AddStopAt(track == RampTrack.Alpha, at);
        ramp.StopMoved += (stop, at) =>
            gradients.MoveStop(stop.Track == RampTrack.Alpha, stop.Index, at);
        ramp.SelectionChanged += stop =>
        {
            if (stop is { } s) gradients.Select(s.Track == RampTrack.Alpha, s.Index);
        };
        ramp.StopRemoved += stop =>
        {
            gradients.RemoveStopAt(stop.Track == RampTrack.Alpha, stop.Index);
            ramp.Selection = null;
        };
    }

    // ---- workspace commands ----------------------------------------------------

    private void OnSaveWorkspace(object? sender, RoutedEventArgs e)
    {
        _vm.Workspace.SaveCurrent();
        _vm.AiStatus = $"Saved workspace “{_vm.Workspace.SelectedName}”.";
    }


    // ---- symbols ------------------------------------------------------------------

    /// <summary>
    /// Turn the current drawing into a symbol.
    /// </summary>
    /// <remarks>
    /// Asks for a name first, because a browser full of "Symbol", "Symbol 2"
    /// and "Symbol 3" is a browser nobody searches — and naming a thing is
    /// cheapest at the moment you decide it is a thing.
    /// </remarks>
    private async void OnMakeSymbol(object? sender, RoutedEventArgs e)
    {
        if (await PromptForText("Make symbol", "Name", "Symbol") is not { } name) return;
        _vm.MakeSymbolFromDrawing(name);
    }

    private void OnDeleteSymbol(object? sender, RoutedEventArgs e)
    {
        if (_vm.SymbolBrowser.Selected is { } row) _vm.DeleteSymbol(row.Model);
    }

    private void OnPromoteSymbol(object? sender, RoutedEventArgs e) => _vm.PromoteSelectedSymbol();

    private void OnUpdateSymbolsFromLibrary(object? sender, RoutedEventArgs e) =>
        _vm.UpdateSymbolsFromLibrary();

    /// <summary>
    /// Report where the selected symbol is placed.
    /// </summary>
    /// <remarks>
    /// A button rather than something the panel shows automatically: the count
    /// comes from reading every document in the project, which is exactly what
    /// the folder layout exists to avoid doing on its own.
    /// </remarks>
    private void OnSymbolUsage(object? sender, RoutedEventArgs e)
    {
        if (_vm.SymbolBrowser.Selected is { } row) _vm.AiStatus = _vm.DescribeUsage(row.Model);
    }

    private void OnAcknowledgeStale(object? sender, RoutedEventArgs e)
    {
        var count = _vm.AcknowledgeOutdatedPlacements();
        if (count > 0)
        {
            _vm.AiStatus = $"Marked {count} placement(s) as seen. Nothing about the drawing changed.";
        }
    }

    /// <summary>Double-click a tile to open the symbol, like an animation row.</summary>
    private void OnSymbolTileOpened(object? sender, RoutedEventArgs e)
    {
        if (_vm.SymbolBrowser.Selected is { } row) _vm.OpenSymbol(row.Model);
    }
    private async void OnSaveWorkspaceAs(object? sender, RoutedEventArgs e)
    {
        if (await PromptForText("Save workspace", "Name", _vm.Workspace.SelectedName) is not { } name) return;
        _vm.Workspace.SaveAs(name);
        _vm.AiStatus = $"Saved workspace “{_vm.Workspace.SelectedName}”.";
    }

    private void OnResetWorkspace(object? sender, RoutedEventArgs e)
    {
        _vm.Workspace.Reset();
        _vm.AiStatus = $"Reset to “{_vm.Workspace.SelectedName}”.";
    }

    private void OnWorkspacePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox picker || picker.SelectedItem is not WorkspaceRow row) return;
        // Back to the placeholder — which shows the current workspace's name —
        // because the list is a set of verbs, not a bound value. Leaving a
        // selection in it would go stale the moment anything is rearranged.
        picker.SelectedItem = null;
        _vm.Workspace.Apply(row.Name);
    }

    private void OnDeleteWorkspace(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string name) return;
        // The bin is inside the row, so the click would also pick the row.
        e.Handled = true;
        _vm.Workspace.Delete(name);
        WorkspacePicker.SelectedItem = null;
        WorkspacePicker.IsDropDownOpen = false;
    }

    // ---- dragging a panel ----------------------------------------------------

    private Docker? _dragging;
    private IPointer? _dragPointer;

    /// <summary>
    /// The window the drag's pointer events arrive in — this one for a docked
    /// panel, the floating window for one being dragged back.
    /// </summary>
    private Window? _dragHost;

    /// <summary>
    /// A header was pulled. From here until the pointer comes up, the window
    /// owns the gesture: it resolves a drop target on every move, shows it, and
    /// commits on release.
    /// </summary>
    /// <remarks>
    /// Pointer capture is taken on the <b>window</b>, not the panel, precisely
    /// because the panel is about to be reparented — capture held by a control
    /// that is being moved between visual trees does not survive the move, and
    /// the drag would end halfway through itself.
    /// </remarks>
    private void BeginPanelDrag(Docker panel, PointerEventArgs e)
    {
        if (_dragging is not null) return;
        _dragging = panel;
        // A floating panel lives in its own window, so its pointer events never
        // reach this one. Capture where the panel actually is and translate to
        // main-window coordinates through the screen — that is what makes a
        // torn-out panel dockable again instead of stranded.
        _dragHost = _floating.Values.FirstOrDefault(w => w.PanelId == panel.PanelId) ?? (Window)this;
        _dragPointer = e.Pointer;
        e.Pointer.Capture(_dragHost);
        _dragHost.AddHandler(PointerMovedEvent, OnPanelDragMoved, RoutingStrategies.Tunnel);
        _dragHost.AddHandler(PointerReleasedEvent, OnPanelDragReleased, RoutingStrategies.Tunnel);
        UpdateDropTarget(e);
    }

    private void OnPanelDragMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null) return;
        UpdateDropTarget(e);
        e.Handled = true;
    }

    private void OnPanelDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging is not { } panel) return;
        var target = ResolveDrop(e);
        // Where the pointer is, in screen space, read before the drag state is
        // torn down — the float fallback below needs it.
        Visual hostVisual = _dragHost ?? this;
        var origin = hostVisual.PointToScreen(e.GetPosition((Visual)(_dragHost ?? this)));
        EndPanelDrag();
        e.Handled = true;

        if (target is { } drop)
        {
            _vm.Workspace.Dock(panel.PanelId, drop.Side, drop.Index);
            return;
        }
        // Let go over nothing: the panel floats. Dropping a panel into empty
        // space and having it snap back would make tearing one out impossible.
        var info = DockPanels.Of(panel.PanelId);
        _vm.Workspace.Float(
            panel.PanelId, origin.X - 40, origin.Y - 10,
            info.MaxExtent ?? 360, Math.Max(240, info.DefaultExtent));
    }

    private void EndPanelDrag()
    {
        if (_dragHost is { } host)
        {
            host.RemoveHandler(PointerMovedEvent, OnPanelDragMoved);
            host.RemoveHandler(PointerReleasedEvent, OnPanelDragReleased);
        }
        _dragPointer?.Capture(null);
        _dragPointer = null;
        _dragHost = null;
        _dragging = null;
        DropIndicator.Show(null);
    }

    private void UpdateDropTarget(PointerEventArgs e) => DropIndicator.Show(ResolveDrop(e));

    private DropTarget? ResolveDrop(PointerEventArgs e)
    {
        if (_dragging is not { } panel) return null;
        if (PointerOverRoot(e) is not { } at) return null;
        return DockZones.Resolve(
            at.X, at.Y, RectOf(RootGrid), CurrentSlots(), panel.PanelId, _vm.Workspace.Layout);
    }

    /// <summary>
    /// The pointer in RootGrid coordinates, however far away the window it is
    /// actually over happens to be. Null when this window is not on screen.
    /// </summary>
    private Point? PointerOverRoot(PointerEventArgs e)
    {
        if (_dragHost is null || ReferenceEquals(_dragHost, this)) return e.GetPosition(RootGrid);
        Visual host = _dragHost;
        var screen = host.PointToScreen(e.GetPosition(_dragHost));
        Visual root = RootGrid;
        return root.PointToClient(screen);
    }

    /// <summary>Where every docked panel currently is, in RootGrid coordinates.</summary>
    private List<PanelSlot> CurrentSlots()
    {
        var slots = new List<PanelSlot>();
        var layout = _vm.Workspace.Layout;
        foreach (var (id, panel) in _panels)
        {
            var side = layout.SideOf(id);
            if (side is DockSide.Hidden or DockSide.Floating) continue;
            Visual visual = panel;
            // TranslatePoint returns null for a panel that is not in this
            // window's tree — parked in the pool, or floating — which is
            // exactly the set that has no slot to report.
            if (!panel.IsVisible) continue;
            if (visual.TranslatePoint(default, RootGrid) is not { } origin) continue;
            slots.Add(new PanelSlot(
                id, side, layout.Place(id).Order,
                new DockRect(origin.X, origin.Y, panel.Bounds.Width, panel.Bounds.Height)));
        }
        return slots;
    }

    private static DockRect RectOf(Control c) => new(0, 0, c.Bounds.Width, c.Bounds.Height);

    // ---- floating panels -------------------------------------------------------

    private void ShowFloating(DockPanelId id, Docker panel, DockLayout layout)
    {
        if (_floating.TryGetValue(id, out var open))
        {
            open.Activate();
            return;
        }
        Detach(panel);
        var window = new FloatingPanelWindow(panel, layout.Place(id));
        window.Dismissed += floated =>
        {
            // The window's own close button closes the panel. Park it first so
            // the panel outlives the window that was showing it.
            if (!_floating.Remove(floated, out var w)) return;
            Park(w.Release());
            _vm.Workspace.SetVisible(floated, false);
        };
        window.Moved += floated =>
        {
            if (!_floating.TryGetValue(floated, out var w)) return;
            var place = _vm.Workspace.Layout.Place(floated);
            place.FloatX = w.Position.X;
            place.FloatY = w.Position.Y;
            place.FloatWidth = w.Width;
            place.FloatHeight = w.Height;
        };
        _floating[id] = window;
        window.Show(this);
    }

    private void CloseFloating(DockPanelId id)
    {
        if (!_floating.Remove(id, out var window)) return;
        Park(window.Release());
        window.Close();
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

    // Three states rather than a checkbox, because "leave it to the export" and
    // "keep this in whatever the export decides" are genuinely different answers and
    // a two-state control cannot say both.
    private void OnLayerMenuExportAuto(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SetLayerExportPin(row.Layer, null);
    }

    private void OnLayerMenuExportNever(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SetLayerExportPin(row.Layer, true);
    }

    private void OnLayerMenuExportAlways(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) _vm.SetLayerExportPin(row.Layer, false);
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

    private void OnRetimeCel(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.ApplyTimingAt(cell);
    }

    private void OnSaveTimingPreset(object? sender, RoutedEventArgs e) => _vm.SaveTimingPreset();

    private void OnDeleteTimingPreset(object? sender, RoutedEventArgs e) => _vm.DeleteSelectedTimingPreset();

    private void OnClearCel(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.ClearCelAt(cell);
    }

    private void OnDeleteCel(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.DeleteCelAt(cell);
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
        _celDrag.Press(cell, e.GetPosition(this), leftButton: true, keyed: cell.IsKeyed && !cell.IsVirtual);
        _celDragPress = _celDrag.Candidate is null ? null : e;
    }

    /// <summary>
    /// A context menu and a cel drag are two readings of the same press, and
    /// only one can win.
    /// </summary>
    /// <remarks>
    /// B8: a pen right-click is a press-and-hold, so the press armed the drag
    /// and the hold opened the menu — then moving towards "Insert frame"
    /// crossed the threshold, started a drag, and the drag seized the pointer
    /// and shut the menu. A mouse right-click never arms it, which is why the
    /// report said a mouse was fine.
    /// </remarks>
    private void OnTimelineContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        _celDrag.Cancel();
        _celDragPress = null;
    }

    /// <summary>
    /// Letting go ends the gesture, whether or not a move ever arrived.
    /// </summary>
    /// <remarks>
    /// The arming press used to be cleared only by a move that found the
    /// button up, so lifting the pen without moving left it armed — and the
    /// next press-and-drag anywhere on that cel would pick up a gesture that
    /// began minutes earlier.
    /// </remarks>
    private void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _celDrag.Cancel();
        _celDragPress = null;
    }

    // ---- drag a cel along its row ------------------------------------------------

    private static readonly DataFormat<FrameCell> CelDragFormat =
        DataFormat.CreateInProcessFormat<FrameCell>("lightbox-cel");

    private readonly Input.CelDragGesture _celDrag = new();
    private PointerPressedEventArgs? _celDragPress;

    private async void OnCellPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_celDragPress is not { } press) return;
        if (sender is not Button button || button.DataContext is not FrameCell cell) return;
        var point = e.GetCurrentPoint(this);
        if (!_celDrag.ShouldStart(cell, point.Position, point.Properties.IsLeftButtonPressed))
        {
            if (_celDrag.Candidate is null) _celDragPress = null;
            return;
        }

        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(CelDragFormat, cell));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            _celDrag.Finished();
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
        var preset = _vm.SaveCurrentAsPreset(PresetNameBox.Text ?? "", SplitTags(PresetTagBox.Text));
        PresetNameBox.Text = "";
        PresetTagBox.Text = "";
        _vm.AiStatus = $"Saved brush preset “{preset.Name}”.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private void OnUpdatePresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset || !_vm.UpdateSelectedPreset()) return;
        _vm.AiStatus = $"Updated “{preset.Name}”.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private void OnRevertPresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset || !_vm.RevertBrushPreset()) return;
        _vm.AiStatus = $"“{preset.Name}” is back to the one that ships with Lightbox.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private void OnApplyTagsClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset) return;
        _vm.SetPresetTags(preset, SplitTags(PresetTagBox.Text));
        RefreshPresetPage();
    }

    private void OnDeletePresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset) return;
        var wasBuiltIn = preset.IsBuiltIn;
        if (!_vm.DeletePreset(preset)) return;
        _vm.AiStatus = wasBuiltIn ? $"“{preset.Name}” reverted." : $"Deleted “{preset.Name}”.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private static List<string> SplitTags(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// The preset page reflects one preset, and which one changes under it —
    /// so it is refreshed rather than bound. Two of the four buttons only make
    /// sense some of the time, and a button that does nothing is worse than an
    /// absent one.
    /// </summary>
    private void RefreshPresetPage()
    {
        if (PresetCurrentName is null) return;

        var preset = _vm.SelectedBrushPreset;
        PresetCurrentName.Text = preset?.Name ?? "No brush chosen";
        UpdatePresetButton.IsEnabled = _vm.CanUpdateBrushPreset;
        RevertPresetButton.IsVisible = preset?.IsBuiltIn == true;
        RevertPresetButton.IsEnabled = _vm.CanRevertBrushPreset;
        DeletePresetButton.IsEnabled = preset is not null;
        DeletePresetButton.Content = preset?.IsBuiltIn == true ? "Revert this brush" : "Delete this brush";
        ApplyTagsButton.IsEnabled = preset is not null;

        PresetModifiedNote.IsVisible = _vm.BrushIsModified;
        PresetModifiedText.Text = _vm.BrushModifiedTip;

        // Only when the artist has not started typing, or refreshing would eat
        // what they were halfway through writing.
        if (!PresetTagBox.IsFocused) PresetTagBox.Text = string.Join(", ", preset?.Tags ?? []);
    }

    // ---- the brush picker -------------------------------------------------------

    private readonly HashSet<string> _brushTagFilter = new(StringComparer.OrdinalIgnoreCase);
    private bool _pickingBrush;

    private void OnBrushPickerOpen(object? sender, RoutedEventArgs e)
    {
        BuildBrushTagChips();
        RefreshBrushPresetList();
    }

    private void BuildBrushTagChips()
    {
        if (BrushTagChips is null) return;

        // Absent until there are tags. An empty strip of chips is a row of
        // nothing that says the feature is broken.
        BrushTagChips.IsVisible = _vm.BrushTagChoices.Count > 0;
        if (!BrushTagChips.IsVisible)
        {
            BrushTagChips.ItemsSource = null;
            return;
        }

        var chips = new List<Control>();
        foreach (var tag in _vm.BrushTagChoices)
        {
            var chip = new ToggleButton
            {
                Content = tag,
                FontSize = 10,
                Padding = new Thickness(6, 1),
                Margin = new Thickness(0, 0, 4, 4),
                IsChecked = _brushTagFilter.Contains(tag),
            };
            chip.IsCheckedChanged += (_, _) =>
            {
                if (chip.IsChecked == true) _brushTagFilter.Add(tag);
                else _brushTagFilter.Remove(tag);
                RefreshBrushPresetList();
            };
            chips.Add(chip);
        }
        BrushTagChips.ItemsSource = chips;
    }

    private void OnBrushFilterChanged(object? sender, TextChangedEventArgs e) => RefreshBrushPresetList();

    /// <summary>Re-run the filter. The rules themselves live in <see cref="BrushFilter"/>.</summary>
    private void RefreshBrushPresetList()
    {
        if (BrushPresetList is null) return;

        var matches = BrushFilter.Apply(_vm.BrushPresetChoices, BrushSearchBox.Text, _brushTagFilter);

        _pickingBrush = true;
        BrushPresetList.ItemsSource = matches;
        BrushPresetList.SelectedItem = matches.FirstOrDefault(p => p.Id == _vm.SelectedBrushPreset?.Id);
        _pickingBrush = false;

        BrushFilterEmpty.IsVisible = matches.Count == 0;
    }

    private void OnBrushPresetPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_pickingBrush || BrushPresetList?.SelectedItem is not BrushPreset preset) return;
        // ApplyPreset rather than the property, so picking the brush you are
        // already on puts it back — which is the obvious way to undo a nudge
        // once there is a dot telling you the brush has been nudged.
        _vm.ApplyPreset(preset);
        RefreshBrushPickerButton();
        RefreshPresetPage();
        if (BrushPickerButton?.Flyout is { } flyout) flyout.Hide();
    }

    private void RefreshBrushPickerButton()
    {
        if (BrushPickerName is null) return;
        BrushPickerName.Text = _vm.SelectedBrushPreset?.Name ?? "Brush";
    }

    private async void OnImportTextureClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Paper texture",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        if (SkiaSharp.SKBitmap.Decode(path) is not { } bitmap) return;

        using (bitmap)
        {
            _vm.ImportBrushTexture(Lightbox.Raster.PngCodec.Encode(bitmap));
        }
        _vm.AiStatus = $"Painting on “{Path.GetFileName(path)}”.";
        RefreshTextureNote(Path.GetFileName(path));
    }

    private void OnClearTextureClicked(object? sender, RoutedEventArgs e)
    {
        _vm.ClearBrushTexture();
        RefreshTextureNote(null);
    }

    /// <summary>
    /// Names the file the paper came from. The document keeps the pixels, so
    /// this is the artist's memory rather than a reference — the same job
    /// <c>BrushTip.Source</c> does.
    /// </summary>
    private void RefreshTextureNote(string? name)
    {
        if (TextureSourceNote is null) return;
        TextureSourceNote.IsVisible = name is not null;
        TextureSourceNote.Text = name is null ? "" : $"Paper: {name}";
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

    private async void OnImportReferenceClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import reference",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;

        // Everything becomes PNG on the way in. The document carries the image
        // itself rather than a path — a reference that broke when the file
        // moved would break silently, and you would not notice until you were
        // drawing against nothing.
        string png;
        try
        {
            using var decoded = SkiaSharp.SKBitmap.Decode(path);
            if (decoded is null) return;
            png = Lightbox.Raster.PngCodec.Encode(decoded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _vm.ImportReference(Path.GetFileNameWithoutExtension(path), png);
        _vm.ReferenceDockerVisible = true;
    }

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
            ToolId.Shape => Rendering.CanvasControl.CanvasToolMode.Shape,
            ToolId.Move => Rendering.CanvasControl.CanvasToolMode.Move,
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

    /// <summary>
    /// Hold a tool button to get its list of variants.
    /// </summary>
    /// <remarks>
    /// One implementation for every tool that has variants, because the
    /// gesture has to be identical: a hold that works on Select and not on
    /// Shape is worse than no hold at all — the artist stops trusting it and
    /// goes to the options bar every time.
    /// </remarks>
    private void HoldToOpen(Control button, PointerPressedEventArgs e)
    {
        _variantFlyoutOpened = false;
        _holdButton = null;
        if (!e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
        _holdTimer?.Stop();
        _holdTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer?.Stop();
            _variantFlyoutOpened = true;
            _holdButton = button;
            button.ContextFlyout?.ShowAt(button);
        };
        _holdTimer.Start();
    }

    private Control? _holdButton;

    private void OnSelectToolPressed(object? sender, PointerPressedEventArgs e) =>
        HoldToOpen(SelectToolButton, e);

    private void OnShapeToolPressed(object? sender, PointerPressedEventArgs e) =>
        HoldToOpen(ShapeToolButton, e);

    private void OnSelectToolReleased(object? sender, PointerReleasedEventArgs e)
    {
        _holdTimer?.Stop();
        // The hold already opened the variant list — don't also register a click.
        if (_variantFlyoutOpened) e.Handled = true;
    }

    /// <summary>
    /// Close the hold-list after a variant has been picked.
    /// </summary>
    /// <remarks>
    /// Posted, not called. <c>Button.OnClick</c> raises Click and only then
    /// runs the command, and closing the flyout from inside the Click handler
    /// tears the button out of the tree first — its <c>{Binding …Command}</c>
    /// loses its DataContext, resolves to null, and the command never runs.
    /// The list closed, nothing changed, and the tool options bar still showed
    /// the old variant, which is precisely what "the dropdown does nothing"
    /// looks like. Letting the click finish first fixes it.
    /// </remarks>
    private void OnVariantChosen(object? sender, RoutedEventArgs e)
    {
        var button = _holdButton ?? SelectToolButton;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => button.ContextFlyout?.Hide());
    }

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

        if (index == 0) RefreshTipButton();
        if (index == 3) BuildPressureCurves();
        if (index == 4) RefreshPresetPage();
    }

    // ---- pressure curves --------------------------------------------------------

    /// <summary>
    /// What each curve drives, and what to call it. Data rather than seven
    /// blocks of near-identical XAML, so adding a dynamic is a row here.
    /// </summary>
    private static readonly (BrushDynamic Target, string Label, string Tip, bool SmudgeOnly)[] PressureRows =
    [
        (BrushDynamic.Size, "Size", "Line thickness", false),
        (BrushDynamic.Flow, "Transparency", "Paint amount per dab — light pressure paints lighter", false),
        (BrushDynamic.Hardness, "Hardness", "Light pressure gives a softer dab edge", false),
        (BrushDynamic.Scatter, "Scatter", "How far dabs are thrown off the path", false),
        (BrushDynamic.Roundness, "Roundness", "Press harder and a flat dab spreads toward circular", false),
        (BrushDynamic.ColorRate, "Colour rate", "How much of the brush's own colour a smudge adds", true),
        (BrushDynamic.SmudgeLength, "Smudge length", "How far a smudge drags what it picked up", true),
    ];

    private bool _buildingCurves;

    /// <summary>
    /// Build the pressure page's rows. Rebuilt when the page is shown rather
    /// than bound, because a curve is edited by dragging rather than by typing
    /// and there is nothing for a two-way binding to carry.
    /// </summary>
    private void BuildPressureCurves()
    {
        if (PressureCurveRows is null) return;

        _buildingCurves = true;
        PressureCurveRows.Children.Clear();

        foreach (var (target, label, tip, smudgeOnly) in PressureRows)
        {
            if (smudgeOnly && !_vm.IsSmudgeBrush) continue;

            var driven = _vm.BrushDrives(target);

            var editor = new CurveEditor
            {
                Curve = _vm.BrushCurve(target),
                IsActive = driven,
                Width = 128,
                Height = 84,
            };
            editor.CurveChanged += curve =>
            {
                if (_buildingCurves) return;
                _vm.SetBrushCurve(target, curve);
            };

            var check = new CheckBox { Content = label, FontSize = 12, IsChecked = driven };
            check.IsCheckedChanged += (_, _) =>
            {
                if (_buildingCurves) return;
                _vm.SetBrushDrives(target, check.IsChecked == true);
                BuildPressureCurves();
            };

            var reset = new Button
            {
                Content = "Reset",
                FontSize = 11,
                IsEnabled = driven,
                [ToolTip.TipProperty] = "Back to a straight line",
            };
            reset.Click += (_, _) =>
            {
                _vm.ResetBrushCurve(target);
                BuildPressureCurves();
            };

            var side = new StackPanel { Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            side.Children.Add(check);
            side.Children.Add(new TextBlock
            {
                Text = tip, FontSize = 10, Opacity = 0.6, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            side.Children.Add(reset);

            var row = new DockPanel { [ToolTip.TipProperty] = tip };
            DockPanel.SetDock(editor, Dock.Right);
            row.Children.Add(editor);
            row.Children.Add(side);
            PressureCurveRows.Children.Add(row);
        }

        _buildingCurves = false;
    }

    // ---- the tip picker ---------------------------------------------------------

    /// <summary>Fill the flyout the moment before it opens, so it is never stale.</summary>
    private void OnTipPickerOpen(object? sender, RoutedEventArgs e)
    {
        if (TipPickerList is null) return;

        var choices = new List<TipChoice> { TipChoice.Round() };
        choices.AddRange(_vm.AvailableTips().Select(TipChoice.For));

        _pickingTip = true;
        TipPickerList.ItemsSource = choices;
        TipPickerList.SelectedItem = choices.FirstOrDefault(c => c.Tip?.Id == _vm.BrushTipId) ?? choices[0];
        _pickingTip = false;
    }

    private bool _pickingTip;

    private void OnTipPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_pickingTip || TipPickerList?.SelectedItem is not TipChoice choice) return;
        _vm.SetBrushTip(choice.Tip);
        RefreshTipButton();
        if (TipPickerButton?.Flyout is { } flyout) flyout.Hide();
    }

    /// <summary>The button shows the tip itself, because that is what it is for.</summary>
    private void RefreshTipButton()
    {
        if (TipPickerName is null) return;

        var chosen = _vm.BrushTipId is { } id
            ? _vm.AvailableTips().FirstOrDefault(t => t.Id == id)
            : null;

        // Named but missing: the tip travelled into the drawing and then left
        // the library. The mark still renders — the raster is in the document —
        // so say so rather than showing "Round", which would be a lie about
        // what the brush is doing.
        TipPickerName.Text = chosen?.Name ?? (_vm.BrushTipId is null ? "Round" : "Custom");
        TipPickerThumb.Source = chosen is null ? null : TipChoice.For(chosen).Thumbnail;
    }

    // ---- drag a symbol onto the canvas to place it -----------------------------

    private static readonly DataFormat<string> SymbolDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-symbol");

    /// <summary>
    /// A tile does two things, told apart by whether the pointer moves: a click
    /// selects it, a drag carries the symbol to the spot you want it. Same
    /// shape as the colour swatch next door, and for the same reason — the drag
    /// API cannot be asked afterwards whether the gesture moved, so the press
    /// is held and the decision made on the first move.
    /// </summary>
    private Point? _tilePress;

    private PointerPressedEventArgs? _tilePressArgs;

    private string? _tileSymbolId;

    private void OnSymbolTilePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SymbolTiles).Properties.IsLeftButtonPressed) return;
        if ((e.Source as Control)?.DataContext is not ViewModels.SymbolRow row) return;
        _tilePress = e.GetPosition(this);
        _tilePressArgs = e;
        _tileSymbolId = row.Model.Id;
        SymbolTiles.PointerMoved += OnSymbolTileMoved;
        SymbolTiles.PointerReleased += OnSymbolTileReleased;
    }

    private async void OnSymbolTileMoved(object? sender, PointerEventArgs e)
    {
        if (_tilePress is not { } start) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
        var press = _tilePressArgs;
        var id = _tileSymbolId;
        EndTileGesture();
        if (press is null || id is null) return;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(SymbolDragFormat, id));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("symbol-drag", ex);
        }
    }

    private void OnSymbolTileReleased(object? sender, PointerReleasedEventArgs e) => EndTileGesture();

    private void EndTileGesture()
    {
        SymbolTiles.PointerMoved -= OnSymbolTileMoved;
        SymbolTiles.PointerReleased -= OnSymbolTileReleased;
        _tilePress = null;
        _tilePressArgs = null;
        _tileSymbolId = null;
    }

    private static string? DraggedSymbolOf(DragEventArgs e) =>
        e.DataTransfer is { } transfer ? transfer.TryGetValue(SymbolDragFormat) : null;

    private void OnCanvasSymbolDragOver(object? sender, DragEventArgs e)
    {
        if (DraggedSymbolOf(e) is null) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnCanvasSymbolDrop(object? sender, DragEventArgs e)
    {
        if (DraggedSymbolOf(e) is not { } id) return;
        var (x, y) = Canvas.ViewToDoc(e.GetPosition(Canvas));
        // Where the pointer is, not the middle of the canvas: the whole point
        // of dragging rather than pressing Place is choosing the spot.
        _vm.PlaceSymbol(id, x, y);
        e.Handled = true;
    }

    // ---- drag a colour onto the canvas to fill --------------------------------

    private static readonly DataFormat<string> ColorDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-color");

    /// <summary>
    /// A colour swatch does two things, told apart by whether the pointer
    /// moves: a click opens its picker, a drag carries the colour to the canvas
    /// to fill with — the shortest path from "I chose this colour" to "that
    /// shape is that colour", without visiting the tool bar on the way.
    /// </summary>
    /// <remarks>
    /// <c>DoDragDropAsync</c> does not return until the gesture is over and
    /// reports no distance, so "was that a drag" cannot be asked afterwards.
    /// The press is therefore held, and the decision made on the first move —
    /// which is the same shape as the panel-header grip, for the same reason.
    /// </remarks>
    private Point? _swatchPress;

    /// <summary>
    /// The press that started the gesture. Held because the drag API wants the
    /// event that began it, and by the time we know this is a drag rather than
    /// a click that event has been and gone.
    /// </summary>
    private PointerPressedEventArgs? _swatchPressArgs;

    /// <summary>The swatch a gesture started on — there are three of them now.</summary>
    private Control? _swatchControl;

    /// <summary>
    /// Press on any colour swatch: a click opens its picker, a drag carries
    /// the colour off to be dropped as a fill.
    /// </summary>
    /// <remarks>
    /// One handler for all of them. It used to be two, and both were dead: the
    /// foreground swatch in the tool bar wired its gesture onto the Color
    /// panel's swatch rather than the one you pressed, and the background one
    /// asked for an attached flyout that had never been attached. Neither did
    /// anything at all.
    /// </remarks>
    private void OnColorSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control swatch) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _swatchPress = e.GetPosition(this);
        _swatchPressArgs = e;
        _swatchControl = swatch;
        swatch.PointerMoved += OnColorSwatchMoved;
        swatch.PointerReleased += OnColorSwatchReleased;
    }

    /// <summary>Which colour a swatch carries — its Tag names the half of the pair.</summary>
    private string ColorOf(Control? swatch) =>
        swatch?.Tag as string == "background" ? _vm.BackgroundColorHex : _vm.ColorHex;

    private async void OnColorSwatchMoved(object? sender, PointerEventArgs e)
    {
        if (_swatchPress is not { } start) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < 4 && Math.Abs(now.Y - start.Y) < 4) return;
        var press = _swatchPressArgs;
        var hex = ColorOf(_swatchControl);
        EndSwatchGesture();
        if (press is null) return;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ColorDragFormat, hex));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("color-drag", ex);
        }
    }

    private void OnColorSwatchReleased(object? sender, PointerReleasedEventArgs e)
    {
        var clicked = _swatchPress is not null ? _swatchControl : null;
        EndSwatchGesture();
        if (clicked is not null) FlyoutBase.ShowAttachedFlyout(clicked);
    }

    private void EndSwatchGesture()
    {
        if (_swatchControl is { } swatch)
        {
            swatch.PointerMoved -= OnColorSwatchMoved;
            swatch.PointerReleased -= OnColorSwatchReleased;
        }
        _swatchPress = null;
        _swatchPressArgs = null;
        _swatchControl = null;
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

    /// <summary>
    /// Hand the canvas the boxes to draw, or null when the mode is off.
    /// </summary>
    /// <remarks>
    /// A snapshot in document coordinates rather than the cells themselves.
    /// The renderer runs on another thread and must never read a document
    /// object the UI thread may be halfway through editing.
    /// </remarks>
    private void RefreshReferenceBoxes()
    {
        if (!_vm.ReferenceGridEditMode || _vm.ActiveReference is not { } strip)
        {
            Canvas.ReferenceBoxes = null;
            return;
        }
        var boxes = new List<Rendering.CanvasControl.ReferenceBox>(strip.Cells.Count);
        for (var i = 0; i < strip.Cells.Count; i++)
        {
            var cell = strip.Cells[i];
            var (x, y, w, h) = _vm.CellRect(strip, cell);
            var (px, py) = cell.Pivot;
            var scale = Math.Max(0.01, strip.Scale);
            boxes.Add(new Rendering.CanvasControl.ReferenceBox(
                (float)x, (float)y, (float)w, (float)h,
                (float)(strip.OffsetX + cell.Dx + px * scale),
                (float)(strip.OffsetY + cell.Dy + py * scale),
                i == _vm.SelectedReferenceCell));
        }
        Canvas.ReferenceBoxes = boxes;
    }

    // ---- the palette hierarchy ------------------------------------------------

    private static readonly DataFormat<string> PaletteNodeDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-palette-node");

    /// <summary>
    /// The row a drag started on, and where. Held for the same reason the
    /// colour swatch holds its press: <c>DoDragDropAsync</c> wants the event
    /// that began the gesture, and by the time we know this is a drag rather
    /// than a click that event has been and gone.
    /// </summary>
    private (PaletteNode Node, Point At, PointerPressedEventArgs Args)? _paletteDrag;

    private static PaletteNode? NodeOf(object? sender) =>
        (sender as Control)?.DataContext as PaletteNode;

    private void OnPaletteNodePressed(object? sender, PointerPressedEventArgs e)
    {
        if (NodeOf(sender) is not { } node) return;
        var point = e.GetCurrentPoint(this).Properties;
        if (point.IsRightButtonPressed)
        {
            _vm.PaletteDocker.SelectedNode = node;
            ShowPaletteNodeMenu((Control)sender!, node);
            e.Handled = true;
            return;
        }
        if (!point.IsLeftButtonPressed || !node.IsDraggable) return;
        _paletteDrag = (node, e.GetPosition(this), e);
    }

    private async void OnPaletteNodeMoved(object? sender, PointerEventArgs e)
    {
        if (_paletteDrag is not { } drag) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - drag.At.X) < 4 && Math.Abs(now.Y - drag.At.Y) < 4) return;
        _paletteDrag = null;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(
                PaletteNodeDragFormat, drag.Node.Palette?.Id ?? drag.Node.Folder!.Id));
            await DragDrop.DoDragDropAsync(drag.Args, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("palette-drag", ex);
        }
    }

    private void OnPaletteNodeReleased(object? sender, PointerReleasedEventArgs e) =>
        _paletteDrag = null;

    /// <summary>The row a drag is carrying, resolved back from its id.</summary>
    private PaletteNode? DraggedNode(DragEventArgs e) =>
        e.DataTransfer?.TryGetValue(PaletteNodeDragFormat) is { } id
            ? FindPaletteNode(_vm.PaletteDocker.Tree, id)
            : null;

    private static PaletteNode? FindPaletteNode(IEnumerable<PaletteNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Palette?.Id == id || node.Folder?.Id == id) return node;
            if (FindPaletteNode(node.Children, id) is { } hit) return hit;
        }
        return null;
    }

    private void OnPaletteNodeDragOver(object? sender, DragEventArgs e)
    {
        // The cursor says no before the drop does. A move that silently does
        // nothing on release reads as a bug in the drag, not as a refusal.
        var onto = NodeOf(sender);
        if (DraggedSwatch(e) is not null)
        {
            e.DragEffects = onto is { IsPalette: true } ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        var source = DraggedNode(e);
        var allowed = source is not null && onto is not null
            && !ReferenceEquals(source, onto) && source.Scope == onto.Scope;
        e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPaletteNodeDrop(object? sender, DragEventArgs e)
    {
        if (DraggedSwatch(e) is { } swatch)
        {
            _vm.PaletteDocker.MoveSwatch(swatch, NodeOf(sender)?.Palette);
            e.Handled = true;
            return;
        }
        if (DraggedNode(e) is not { } source) return;
        _vm.PaletteDocker.Drop(source, NodeOf(sender));
        e.Handled = true;
    }

    // ---- dragging a swatch into another palette --------------------------------

    private static readonly DataFormat<string> SwatchDragFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-swatch");

    private (SwatchRow Row, Point At, PointerPressedEventArgs Args)? _swatchDrag;

    /// <summary>
    /// The swatch a drag is carrying, resolved back from its id.
    /// </summary>
    /// <remarks>
    /// By id rather than by object, so a drop that lands after the grid has
    /// been rebuilt still finds the row it means. Only the palette on screen
    /// is searched — a swatch can only be dragged out of the one you can see.
    /// </remarks>
    private SwatchRow? DraggedSwatch(DragEventArgs e) =>
        e.DataTransfer?.TryGetValue(SwatchDragFormat) is { } id
            ? _vm.PaletteDocker.Swatches.FirstOrDefault(s => s.Id == id)
            : null;

    private void OnSwatchPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SwatchRow row) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _swatchDrag = (row, e.GetPosition(this), e);
    }

    private async void OnSwatchMoved(object? sender, PointerEventArgs e)
    {
        if (_swatchDrag is not { } drag) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - drag.At.X) < 4 && Math.Abs(now.Y - drag.At.Y) < 4) return;
        _swatchDrag = null;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(SwatchDragFormat, drag.Row.Id));
            await DragDrop.DoDragDropAsync(drag.Args, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("swatch-drag", ex);
        }
    }

    private void OnSwatchReleased(object? sender, PointerReleasedEventArgs e) => _swatchDrag = null;

    /// <summary>
    /// The right-click menu, built here rather than declared in the template.
    /// </summary>
    /// <remarks>
    /// A menu declared inside a template lives in a popup, outside the tree it
    /// came from, and the bindings that would reach the docker resolve to
    /// nothing there — the items look right and do nothing at all. Handlers
    /// close over the view model instead, which cannot go quiet.
    /// </remarks>
    private void ShowPaletteNodeMenu(Control anchor, PaletteNode node)
    {
        var docker = _vm.PaletteDocker;
        var assign = new MenuItem { Header = "Assign to" };
        foreach (var target in docker.AssignTargets)
        {
            if (!docker.CanAssign(node, target)) continue;
            var item = new MenuItem { Header = target.Label };
            var to = target;
            item.Click += (_, _) => docker.Assign(node, to);
            assign.Items.Add(item);
        }

        var rename = new MenuItem { Header = "Rename" };
        rename.Click += (_, _) => node.IsRenaming = true;

        var remove = new MenuItem { Header = node.IsFolder ? "Delete folder" : "Delete palette" };
        remove.Click += (_, _) =>
        {
            docker.SelectedNode = node;
            docker.RemovePaletteCommand.Execute(null);
        };

        var menu = new MenuFlyout();
        if (assign.Items.Count > 0) menu.Items.Add(assign);
        menu.Items.Add(rename);
        menu.Items.Add(remove);
        menu.ShowAt(anchor, showAtPointer: true);
    }

    private void OnPaletteNameCommitted(object? sender, RoutedEventArgs e)
    {
        if (NodeOf(sender) is { } node) node.IsRenaming = false;
    }

    private void OnPaletteNameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        if (NodeOf(sender) is { } node) node.IsRenaming = false;
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

        // Grid editing owns Escape: it is a mode, and a mode you cannot leave
        // with the key everybody tries first is a mode you are stuck in.
        if (_vm.ReferenceGridEditMode && e.Key == Key.Escape)
        {
            _vm.ReferenceGridEditMode = false;
            e.Handled = true;
            return;
        }

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
            case "color.swap":
                _vm.SwapColorsCommand.Execute(null);
                break;
            case "color.reset":
                _vm.ResetColorsCommand.Execute(null);
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
            case "canvas.rulers":
                _vm.Workspace.RulersVisible = !_vm.Workspace.RulersVisible;
                break;
            case "canvas.showGuides":
                _vm.Workspace.GuidesVisible = !_vm.Workspace.GuidesVisible;
                break;
            case "canvas.lockGuides":
                _vm.Workspace.GuidesLocked = !_vm.Workspace.GuidesLocked;
                break;
            case "tool.move":
                _vm.SelectToolCommand.Execute(ToolId.Move);
                break;
            // Sizing by eye used to be Shift+drag on the canvas. Shift is the
            // constraint key now, everywhere, so the brush keeps the two keys
            // every other application binds this to.
            case "brush.smaller":
                _vm.BrushSize = Math.Max(1, _vm.BrushSize - BrushSizeStep(_vm.BrushSize));
                break;
            case "brush.larger":
                _vm.BrushSize = Math.Min(500, _vm.BrushSize + BrushSizeStep(_vm.BrushSize));
                break;
            default:
                return; // unbound or context-gated: not ours
        }
        e.Handled = true;
    }

    private async void OnConfigureClicked(object? sender, RoutedEventArgs e) =>
        await new ConfigureWindow(_shortcuts, _vm).ShowDialog(this);

    /// <summary>
    /// The tip workshop. A window rather than a docker because making a tip is
    /// not something you do mid-stroke, and a panel for it would cost layout
    /// space in every session that never opens one.
    /// </summary>
    private async void OnBrushTipsClicked(object? sender, RoutedEventArgs e) =>
        await new BrushTipsWindow(_vm).ShowDialog(this);

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

    private async void OnSaveClicked(object? sender, RoutedEventArgs e) => await SaveDocumentAsAsync();

    private async Task SaveDocumentAsAsync()
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

    // ---- projects --------------------------------------------------------------

    /// <summary>Ctrl+S: save without a picker when the tab already knows where it lives.</summary>
    private async void OnSaveInPlaceClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.CanSaveInPlace)
        {
            _vm.Save();
            return;
        }
        // Nowhere to put it yet, so Save falls through to Save as… rather than
        // silently doing nothing.
        await SaveDocumentAsAsync();
    }

    private async void OnNewProjectClicked(object? sender, RoutedEventArgs e)
    {
        var suggested = _vm.ActiveTab?.Title is { Length: > 0 } t && !t.StartsWith("Untitled") ? t : "Project";
        if (await new NewProjectDialog(suggested).ShowDialog<NewProjectSettings?>(this) is not { } settings) return;
        await CreateProjectAsync(settings);
    }

    /// <summary>
    /// Ask where the folder goes, then make the project.
    /// </summary>
    /// <remarks>
    /// After the settings rather than before them, because the picker is the
    /// part someone might back out of and there is no sense collecting a name
    /// and a type first only to throw them away. Shared with the start screen,
    /// which collects the same settings without a dialog.
    /// </remarks>
    private async Task CreateProjectAsync(NewProjectSettings settings)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose where the project folder goes",
            AllowMultiple = false,
        });
        if (folder.Count == 0 || folder[0].TryGetLocalPath() is not { } parent) return;

        var root = Path.Combine(parent, settings.Name + Lightbox.Core.Projects.ProjectIo.Extension);
        _vm.NewProject(root, settings.Name, settings.Type, settings.Workspace);
    }

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a .lbproj folder",
            AllowMultiple = false,
        });
        if (folder.Count == 0 || folder[0].TryGetLocalPath() is not { } root) return;
        _vm.OpenProject(root);
    }

    /// <summary>Double-click a project row to open that animation as a tab.</summary>
    private void OnProjectRowActivated(object? sender, Avalonia.Input.TappedEventArgs e) =>
        _vm.ProjectDocker.OpenSelected();

    // ---- re-filing a document by dragging it -----------------------------------

    private static readonly DataFormat<string> ProjectRowFormat =
        DataFormat.CreateInProcessFormat<string>("lightbox-project-row");

    private ProjectRow? _draggedRow;

    /// <summary>
    /// Start a drag from a document row. Character rows are drop targets only:
    /// dragging a character would have to mean reordering, and reordering
    /// characters is not a thing the project model has an opinion about yet.
    /// </summary>
    private async void OnProjectRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ProjectRow pressed) return;

        // Right-click selects first. Every item in the row's menu acts on the
        // selection, and a menu that acted on whatever was selected before you
        // right-clicked would delete the wrong thing sooner or later.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _vm.ProjectDocker.Selected = pressed;
            return;
        }

        if (pressed is not { Animation: not null } row) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _vm.ProjectDocker.Selected = row;

        _draggedRow = row;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ProjectRowFormat, row.Animation.Id));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("project-row-drag", ex);
        }
        finally
        {
            _draggedRow = null;
        }
    }

    private void OnProjectNewAnimation(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewAnimation);

    private void OnProjectNewCharacter(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewCharacterItem);

    private void OnProjectNewScene(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewSceneItem);

    private void OnProjectNewShot(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewShotItem);

    private void OnProjectNewDocument(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.AddItemCommand.Execute(ProjectViewModel.NewLooseDocument);

    private void OnProjectOpen(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.OpenSelectedRowCommand.Execute(null);

    private void OnProjectOpenExternally(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.OpenSelectedExternallyCommand.Execute(null);

    private void OnProjectReveal(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.RevealSelectedCommand.Execute(null);

    private void OnProjectDuplicate(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.DuplicateSelectedCommand.Execute(null);

    private void OnProjectRemove(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.RemoveSelectedCommand.Execute(null);

    private void OnProjectRowRename(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Selected is { } row) row.IsRenaming = true;
    }

    /// <summary>
    /// Set the selected document's production status — and, with auto-export on, hand it
    /// to the engine.
    /// </summary>
    /// <remarks>
    /// The status comes off the menu item's <c>Tag</c> rather than from six near-identical
    /// handlers. A tag that does not parse does nothing rather than guessing a status, so
    /// a typo in the XAML is inert instead of quietly marking things Design.
    /// </remarks>
    private void OnProjectStatusSet(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Selected is not { } row) return;
        if ((sender as Control)?.Tag as string is not { } tag) return;
        if (!Enum.TryParse<Lightbox.Core.Projects.AssetStatus>(tag, out var status)) return;

        _vm.SetProjectStatus(row, status);
    }

    private void OnProjectStatusClear(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Selected is { } row) _vm.SetProjectStatus(row, null);
    }

    private void OnProjectNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ProjectRow row) return;
        switch (e.Key)
        {
            case Key.Enter:
                _vm.ProjectDocker.Rename(row, box.Text ?? "");
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

    private void OnProjectNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not ProjectRow row) return;
        if (row.IsRenaming) _vm.ProjectDocker.Rename(row, box.Text ?? "");
        row.IsRenaming = false;
    }

    /// <summary>
    /// Copy the selected row's path. The view model records it too, so the
    /// behaviour is testable without a clipboard, but the clipboard is the
    /// point and only the window has one.
    /// </summary>
    private async void OnProjectCopyPath(object? sender, RoutedEventArgs e)
    {
        _vm.ProjectDocker.CopySelectedPathCommand.Execute(null);
        if (Clipboard is { } clipboard && _vm.ProjectDocker.CopiedPath is { Length: > 0 } path)
        {
            try
            {
                using var transfer = new DataTransfer();
                transfer.Add(DataTransferItem.Create(DataFormat.Text, path));
                await clipboard.SetDataAsync(transfer);
            }
            catch (Exception ex)
            {
                Rendering.CanvasControl.LogDiag("project-copy-path", ex);
            }
        }
    }

    private void OnProjectRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DropTargetFor(e) is not null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnProjectRowDrop(object? sender, DragEventArgs e)
    {
        if (_draggedRow is not { } row) return;
        if (DropTargetFor(e) is not { } target) return;
        e.Handled = true;
        _vm.ProjectDocker.Move(row, target.Character);
    }

    /// <summary>
    /// Where a drop would land: the character under the pointer, or the
    /// project itself when the pointer is over a loose document or past the
    /// end of the list. Null when the drop would change nothing.
    /// </summary>
    private (Character? Character, bool Valid)? DropTargetFor(DragEventArgs e)
    {
        if (_draggedRow is not { } dragged) return null;
        var over = (e.Source as Control)?.DataContext as ProjectRow;

        // Over nothing in particular means the project: dropping into the
        // empty space below the tree is the natural way to say "not under any
        // character", and it is the only way to say it when every row is one.
        var destination = over?.Character;
        if (ReferenceEquals(destination, dragged.Character)) return null;
        return (destination, true);
    }

    private async void OnExportDocumentClicked(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export a standalone document",
            SuggestedFileName = $"{_vm.ActiveTab?.Title ?? "untitled"}.lightbox.json",
            FileTypeChoices = [LightboxFileType],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        // Flattened: every shared palette and gradient it uses travels with it,
        // so the file re-renders with the project gone.
        await writer.WriteAsync(_vm.ExportStandaloneDocument());
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

    /// <summary>
    /// <c>File ▸ Export for a game engine…</c> — the entry point Pillar 5 did not have.
    /// </summary>
    /// <remarks>
    /// Two steps on purpose: the settings, then the path. Asking for a filename first
    /// and the format afterwards is how somebody ends up with a <c>.png</c> holding a
    /// PNG sequence's folder name.
    /// </remarks>
    private async void OnExportSheetClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new ExportWindow();
        await dialog.ShowDialog(this);
        if (dialog.Chosen is not { } preset) return;

        string path;
        if (preset.Target == Services.ExportTarget.PngSequence)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export PNG sequence to folder",
                AllowMultiple = false,
            });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir) return;
            path = dir;
        }
        else
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export sprite sheet",
                SuggestedFileName = (_vm.ActiveTab?.Title ?? "sheet") + ".png",
                DefaultExtension = "png",
            });
            if (file?.TryGetLocalPath() is not { } sheet) return;
            path = sheet;
        }

        try
        {
            var run = await Task.Run(() => Services.ExportRunner.Run(_vm.Doc, preset, path));
            _vm.AiStatus = _vm.DescribeExport(run);
        }
        catch (Exception ex)
        {
            // A failed export must say so in the status line rather than taking the
            // window down. An unwritable path is the ordinary case, not a bug.
            _vm.AiStatus = $"Export failed: {ex.Message}";
        }
    }

    // ---- guides -------------------------------------------------------------------

    /// <summary>
    /// Hand the canvas the guides to draw, or null when there are none.
    /// </summary>
    /// <remarks>
    /// A vanishing point constrains a continuum of directions, so it is
    /// flattened here into an evenly spread fan. Drawing every direction would
    /// be a filled disc; twenty-four is enough to read as perspective and few
    /// enough to see the drawing through. The renderer stays dumb about what a
    /// guide means, which is what keeps it off the document objects.
    /// </remarks>
    private void RefreshGuides()
    {
        // The rulers carry a mark per guide, so they move with them.
        RefreshRulerMarks();

        if (!_vm.HasGuides || !_vm.Workspace.GuidesVisible)
        {
            Canvas.Guides = null;
            return;
        }
        var lines = new List<Rendering.CanvasControl.GuideLine>();
        foreach (var guide in _vm.Guides)
        {
            if (!guide.Visible) continue;
            var angles = guide.Kind == GuideKind.VanishingPoint
                ? Fan()
                : guide.Angles;
            lines.Add(new Rendering.CanvasControl.GuideLine(
                guide.Id, (int)guide.Kind, (float)guide.X, (float)guide.Y,
                (float)guide.Spacing, angles));
        }
        Canvas.Guides = lines.Count > 0 ? lines : null;
    }

    private static IReadOnlyList<double> Fan()
    {
        const int rays = 24;
        var angles = new double[rays];
        for (var i = 0; i < rays; i++) angles[i] = i * (180.0 / rays);
        return angles;
    }

    // ---- rulers ------------------------------------------------------------------

    /// <summary>
    /// Hook the two ruler strips up to the canvas.
    /// </summary>
    /// <remarks>
    /// Everything the rulers do runs through here: the mapping they draw
    /// against, the pointer they track, the guides they mark, and the drag
    /// that pulls a new guide out of one.
    /// </remarks>
    private void InitialiseRulers()
    {
        Canvas.ViewChanged += RefreshRulerMapping;
        CanvasHost.SizeChanged += (_, _) => RefreshRulerMapping();

        // Tracking the pointer is most of what a ruler is for — reading a
        // tick to work out where you are is slower than glancing at a line
        // that is already there. Handled events too: every tool marks its
        // moves handled, and the ruler has to follow all of them.
        Canvas.AddHandler(
            PointerMovedEvent,
            (_, e) => TrackPointer(e.GetPosition(Canvas)),
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        Canvas.PointerExited += (_, _) => TrackPointer(null);

        foreach (var strip in (RulerStrip[])[TopRuler, LeftRuler])
        {
            strip.PullStarted += (s, p) => UpdateDraftGuide(s, p);
            strip.PullMoved += (s, p) => UpdateDraftGuide(s, p);
            strip.PullEnded += EndPull;
        }

        Canvas.ContentMoveStarted += (x, y, wholeLayer) => _vm.BeginMove(x, y, wholeLayer);
        Canvas.ContentMoveUpdated += (x, y, axisLock) => _vm.UpdateMove(x, y, axisLock);
        Canvas.ContentMoveEnded += _vm.EndMove;
        Canvas.ContentMoveCancelled += _vm.CancelMove;

        Canvas.GuideMoved += (id, dx, dy) =>
        {
            if (GuideById(id) is not { } guide) return;
            // Remembered so the release can close the drag off: the canvas
            // only knows an id, and by then the guide it names may have been
            // replaced by an undo.
            _draggingGuide = guide;
            _vm.DragGuide(guide, dx, dy);
        };
        Canvas.GuideDragEnded += () =>
        {
            if (_draggingGuide is { } guide) _vm.EndGuideDrag(guide);
            _draggingGuide = null;
        };

        _vm.Workspace.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(WorkspaceViewModel.RulersVisible)
                or nameof(WorkspaceViewModel.GuidesVisible)
                or nameof(WorkspaceViewModel.GuidesLocked))
            {
                ApplyRulers();
            }
        };
        ApplyRulers();
    }

    private Guide? _draggingGuide;

    private Guide? GuideById(string id) => _vm.Guides.FirstOrDefault(g => g.Id == id);

    /// <summary>
    /// Put the rulers on or take them away, and say whether guides can be
    /// picked up.
    /// </summary>
    /// <remarks>
    /// The canvas is inset by the strips rather than sharing a grid with them,
    /// so its own coordinates line up with each strip's along that strip's
    /// axis and the mapping needs no offset. Absent, not disabled: with the
    /// rulers off the canvas gets its whole cell back.
    /// </remarks>
    private void ApplyRulers()
    {
        var on = _vm.Workspace.RulersVisible;
        Canvas.Margin = on
            ? new Thickness(RulerStrip.Thickness, RulerStrip.Thickness, 0, 0)
            : default;
        RefreshGuideGrab();
        RefreshGuides();
        RefreshRulerMapping();
    }

    /// <summary>
    /// Whether a guide under the pointer can be picked up.
    /// </summary>
    /// <remarks>
    /// The Move tool's job, and only its job. The rulers used to carry it,
    /// which worked but put the switch a long way from the gesture: you had to
    /// know that showing a strip of numbers was also what made the rig
    /// draggable. A tool says it plainly, is visible in the palette, and is
    /// still the answer with the rulers down — which is most of the time.
    /// Locking or hiding the guides still overrides it, because both of those
    /// mean "leave the rig alone" whatever tool is in hand.
    /// </remarks>
    /// <summary>
    /// How much one press of <c>[</c> or <c>]</c> moves the brush.
    /// </summary>
    /// <remarks>
    /// Proportional, not a fixed step. One pixel at a time is right at size 3
    /// and useless at 300, and a flat ten is the other way round; a tenth of
    /// the current size takes about the same number of presses to double or
    /// halve wherever you start from.
    /// </remarks>
    private static double BrushSizeStep(double size) => Math.Max(1, Math.Round(size / 10));

    private void RefreshGuideGrab() =>
        Canvas.GuideDragEnabled =
            _vm.IsMoveTool && _vm.Workspace.GuidesVisible && !_vm.Workspace.GuidesLocked;

    private void RefreshRulerMapping()
    {
        if (!_vm.Workspace.RulersVisible) return;
        TopRuler.Mapping = Canvas.AxisMapping(horizontal: true);
        LeftRuler.Mapping = Canvas.AxisMapping(horizontal: false);
    }

    /// <summary>
    /// Mark each guide on the ruler it crosses.
    /// </summary>
    /// <remarks>
    /// Only the straight ones: a grid crosses the top ruler everywhere and a
    /// vanishing point is a place on the canvas rather than a position along
    /// an edge, so marking either would be noise where the marks have to stay
    /// readable.
    /// </remarks>
    private void RefreshRulerMarks()
    {
        if (!_vm.Workspace.RulersVisible) return;
        var horizontal = new List<double>();
        var vertical = new List<double>();
        if (_vm.Workspace.GuidesVisible)
        {
            foreach (var guide in _vm.Guides)
            {
                if (!guide.Visible || guide.Kind != GuideKind.Line) continue;
                var angle = ((guide.Angle % 180) + 180) % 180;
                if (angle < 1 || angle > 179) horizontal.Add(guide.Y);
                else if (Math.Abs(angle - 90) < 1) vertical.Add(guide.X);
            }
        }
        // A horizontal guide crosses the *left* ruler, at its height.
        TopRuler.Marks = vertical;
        LeftRuler.Marks = horizontal;
    }

    private void TrackPointer(Point? view)
    {
        if (!_vm.Workspace.RulersVisible) return;
        if (view is not { } p)
        {
            TopRuler.Tracking = null;
            LeftRuler.Tracking = null;
            return;
        }
        var (x, y) = Canvas.ViewToDoc(p);
        TopRuler.Tracking = x;
        LeftRuler.Tracking = y;
    }

    /// <summary>
    /// Follow a guide being pulled out of a ruler.
    /// </summary>
    /// <remarks>
    /// The axis that matters is the other one: dragging out of the top ruler
    /// places a horizontal guide, and where it lands is the pointer's height,
    /// not its position along the ruler. Which is why the strip hands over a
    /// raw point and this works in the canvas's coordinates.
    /// </remarks>
    private void UpdateDraftGuide(RulerStrip strip, Point inStrip)
    {
        var (x, y) = DocOf(strip, inStrip);
        var horizontal = strip.Orientation == Avalonia.Layout.Orientation.Horizontal;
        Canvas.DraftGuide = new Rendering.CanvasControl.GuideLine(
            "draft", (int)GuideKind.Line, (float)x, (float)y, 0,
            horizontal ? [0d] : [90d]);
    }

    private void EndPull(RulerStrip strip, Point inStrip, bool onStrip)
    {
        Canvas.DraftGuide = null;
        // Let go back over the ruler and it never existed. That is how
        // Photoshop throws a guide away, and it doubles as the way out of a
        // drag you did not mean to start.
        if (onStrip) return;
        var (x, y) = DocOf(strip, inStrip);
        _vm.AddGuide(GuideKind.Line, x, y,
            angle: strip.Orientation == Avalonia.Layout.Orientation.Horizontal ? 0 : 90);
    }

    private (double X, double Y) DocOf(RulerStrip strip, Point inStrip) =>
        Canvas.ViewToDoc(strip.TranslatePoint(inStrip, Canvas) ?? inStrip);


    /// <summary>
    /// New guides land in the middle of the canvas.
    /// </summary>
    /// <remarks>
    /// Somewhere visible, so the artist can see what appeared and drag it where
    /// they meant. A guide placed at the origin on a large canvas is a guide
    /// that looks like nothing happened.
    /// </remarks>
    private (double X, double Y) CanvasMiddle() => (_vm.Doc.Scene.Width / 2.0, _vm.Doc.Scene.Height / 2.0);

    private void OnAddHorizontalGuide(object? sender, RoutedEventArgs e)
    {
        var (x, y) = CanvasMiddle();
        _vm.AddGuide(GuideKind.Line, x, y);
    }

    private void OnAddVerticalGuide(object? sender, RoutedEventArgs e)
    {
        var (x, y) = CanvasMiddle();
        _vm.AddGuide(GuideKind.Line, x, y, angle: 90);
    }

    private void OnAddGridGuide(object? sender, RoutedEventArgs e) =>
        // From the origin, because a grid is a lattice over the whole canvas
        // and starting it in the middle would put its intersections in
        // half-cell offsets from every edge. The pitch comes from the
        // configuration — Edit ▸ Configure ▸ Guides and grid.
        _vm.AddGuide(GuideKind.Grid, 0, 0, spacing: _vm.GridSpacing);

    private void OnAddIsometricGuide(object? sender, RoutedEventArgs e)
    {
        var (x, y) = CanvasMiddle();
        _vm.AddGuide(GuideKind.Isometric, x, y);
    }

    private void OnAddVanishingPoint(object? sender, RoutedEventArgs e)
    {
        // On the horizon — a third of the way down is where one usually is —
        // and off to one side, since a VP directly ahead gives you nothing to
        // draw along.
        var scene = _vm.Doc.Scene;
        _vm.AddGuide(GuideKind.VanishingPoint, scene.Width * 0.15, scene.Height / 3.0);
    }

    // ---- converting a project ---------------------------------------------------

    private static readonly (string Label, ProjectType? Type)[] ProjectTypeChoices =
    [
        ("Unset", null),
        ("Illustration", ProjectType.Illustration),
        ("Animation", ProjectType.Animation),
        ("Game art", ProjectType.GameArt),
        ("Storyboard", ProjectType.Storyboard),
        ("Comic", ProjectType.Comic),
        ("Asset library", ProjectType.AssetLibrary),
    ];

    /// <summary>
    /// Fill the convert submenu, in code for the usual reason: a menu declared
    /// in the template lives in a popup where its bindings resolve to nothing.
    /// Rebuilt each time so the tick follows the current type.
    /// </summary>
    private void RefreshConvertMenu()
    {
        ConvertProjectMenu.Items.Clear();
        var current = _vm.ProjectDocker.Project?.Manifest.Type;
        foreach (var (label, type) in ProjectTypeChoices)
        {
            var item = new MenuItem
            {
                Header = label,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = type == current,
            };
            var target = type;
            item.Click += async (_, _) => await ConvertAsync(target);
            ConvertProjectMenu.Items.Add(item);
        }
    }

    /// <summary>
    /// Convert, then report — and offer the new type's panels as a separate
    /// question.
    /// </summary>
    /// <remarks>
    /// Separate because rearranging somebody's screen as a side effect of a
    /// menu item is how a tool loses trust. The conversion has already
    /// happened and cannot fail; declining only means keeping the panels.
    /// </remarks>
    private async Task ConvertAsync(ProjectType? to)
    {
        if (_vm.ProjectDocker.Project?.Manifest.Type == to) return;
        if (_vm.ConvertProject(to) is not { } report) return;
        if (to is not null && await ConfirmWorkspaceAsync(report))
        {
            _vm.TakeProjectTypeWorkspace();
        }
    }

    private async Task<bool> ConfirmWorkspaceAsync(ProjectIo.ConversionReport report)
    {
        var take = false;
        var dialog = new Window
        {
            Title = "Project converted",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var notes = new StackPanel { Spacing = 6 };
        foreach (var note in report.Notes)
        {
            notes.Children.Add(new TextBlock
            {
                Text = "• " + note,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontSize = 12,
            });
        }
        var useDefaults = new Button { Content = "Use this type's panels", MinWidth = 150 };
        var keep = new Button { Content = "Keep my panels", MinWidth = 120, IsCancel = true };
        useDefaults.Click += (_, _) => { take = true; dialog.Close(); };
        keep.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"Now a {report.To} project. Nothing was rewritten.",
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                notes,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { useDefaults, keep },
                },
            },
        };
        await dialog.ShowDialog(this);
        return take;
    }

    // ---- the start screen ------------------------------------------------------

    /// <summary>
    /// Ask what to open, once, on the way in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Over the untitled document the window already opened with, not instead
    /// of it. Escape is therefore a complete answer rather than a cancelled
    /// one: the screen closes and the blank page is already there. That is
    /// what keeps "open it and draw" a keystroke instead of a setting somebody
    /// has to go and find.
    /// </para>
    /// <para>
    /// Public and returning a task so a test can drive it without waiting for
    /// a real Loaded.
    /// </para>
    /// </remarks>
    public async Task OfferStartScreenAsync()
    {
        if (!_vm.Settings.ShowStartScreen) return;
        var screen = new StartScreen(_vm.Settings.Recent);
        await screen.ShowDialog(this);
        await ApplyStartChoiceAsync(screen.Answer);
    }

    /// <summary>Act on what the start screen was answered with.</summary>
    public async Task ApplyStartChoiceAsync(StartChoice choice)
    {
        if (choice.DontShowAgain && _vm.Settings.ShowStartScreen)
        {
            _vm.Settings.ShowStartScreen = false;
            _vm.Settings.Save();
        }

        if (choice.Document is { } document)
        {
            _vm.NewDocument(document, choice.ReuseBlank);
            return;
        }
        if (choice.Project is { } project)
        {
            await CreateProjectAsync(project);
            return;
        }
        if (choice.Open is { } path)
        {
            _vm.OpenRecent(new Services.RecentItem
            {
                Path = path,
                Name = Services.RecentItems.DisplayNameOf(path),
                Kind = choice.OpenKind,
            });
            return;
        }
        if (!choice.Browse) return;
        if (choice.OpenKind == Services.RecentKind.Project) OnOpenProjectClicked(this, new RoutedEventArgs());
        else OnOpenClicked(this, new RoutedEventArgs());
    }

    // ---- recents ---------------------------------------------------------------

    /// <summary>
    /// Fill the Open recent submenu. Built each time it opens, in code.
    /// </summary>
    /// <remarks>
    /// In code because a menu declared in the template lives in a popup, where
    /// the bindings that would reach the view model resolve to nothing — items
    /// that look right and do nothing. Each time because the list changes
    /// whenever anything is opened or saved.
    /// </remarks>
    private void RefreshRecentMenu()
    {
        RecentMenu.Items.Clear();
        var entries = _vm.RecentEntries;
        if (entries.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "Nothing yet", IsEnabled = false });
            return;
        }
        foreach (var entry in entries)
        {
            var item = new MenuItem
            {
                Header = $"{entry.Glyph}  {entry.Name}",
                // The folder, because two characters can both have a "walk".
                [ToolTip.TipProperty] = entry.Path,
            };
            var target = entry;
            item.Click += (_, _) => _vm.OpenRecent(target);
            RecentMenu.Items.Add(item);
        }
        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear the list" };
        clear.Click += (_, _) => _vm.ForgetRecentsCommand.Execute(null);
        RecentMenu.Items.Add(clear);
    }

    // ---- templates (Q12) --------------------------------------------------------

    /// <summary>
    /// Build <c>New from template…</c> when it opens.
    /// </summary>
    /// <remarks>
    /// Built on open rather than bound, for the same reason as the recents list:
    /// finding the project's templates reads documents, and doing that on every
    /// property change would read the project every time anything at all
    /// happened.
    /// </remarks>
    private void RefreshTemplatesMenu()
    {
        TemplatesMenu.Items.Clear();
        var templates = _vm.TemplateChoices;
        if (templates.Count == 0)
        {
            TemplatesMenu.Items.Add(new MenuItem
            {
                Header = "No templates yet",
                IsEnabled = false,
                [ToolTip.TipProperty] =
                    "Mark any document as a template with File ▸ Use as template. "
                    + "Real templates come from work you have already done.",
            });
            return;
        }
        foreach (var reference in templates)
        {
            var item = new MenuItem { Header = reference.Name, [ToolTip.TipProperty] = reference.Path };
            var target = reference;
            item.Click += (_, _) => _vm.NewFromTemplate(target);
            TemplatesMenu.Items.Add(item);
        }
    }

    private async void OnUpdateFromTemplateClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.PreviewTemplatePull() is not { } preview)
        {
            _vm.AiStatus = "This document did not come from a template, or its template is gone.";
            return;
        }

        var dialog = new UpdateFromTemplateWindow();
        dialog.Show(preview);
        await dialog.ShowDialog(this);
        if (dialog.Result is { } options) _vm.UpdateFromTemplate(options);
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
