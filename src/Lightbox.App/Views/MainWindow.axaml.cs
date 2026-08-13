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
using static Lightbox.App.Views.PlacementChoiceDialog;

namespace Lightbox.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    private Services.IpcServer? _ipc;

    /// <summary>The momentary tool key currently down, and what it borrowed (B176).</summary>
    private (Avalonia.Input.Key Key, ToolId Tool)? _momentaryToolKey;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Canvas.SetSelectionManager(_vm.Selection);
        Canvas.SetLinePicker(_vm.PickStrokeAt);
        _vm.SelectedLinesChanged += Canvas.SetSelectedLines;
        Canvas.LinesMarqueed += (rect, add) => _vm.PickStrokesIn(rect, add);
        // The drag commits on release. The outline follows the pointer while the
        // button is down (chrome only, see DrawSelectedLines) and the pixels move
        // once, here — a per-move re-render would repaint the whole frame from its
        // strokes, which is exactly what invariant 6 forbids.
        Canvas.SelectedLinesDragged += (dx, dy) => _vm.MoveSelectedStrokes(dx, dy);
        // Reshaping one line (vector phase 2). The canvas owns the gesture and
        // nothing else: every decision — what was grabbed, where it may go, when
        // it becomes an undo step — is the view model's, so all of it is
        // reachable by a test with no window attached.
        Canvas.SetPathEditEntry(_vm.BeginPathEditAt);
        Canvas.SetPathEditHandlers(
            _vm.GrabPathPart,
            _vm.DragPathPart,
            () => _vm.CommitPathEdit(),
            _vm.HoverPathAt);
        // One subscription for both halves, because they are one fact: the nodes
        // the overlay draws must be the nodes the hit test will find. B147 is the
        // cost of a canvas keeping a copy that something forgets to refresh.
        _vm.PathEditChanged += PublishPathNodes;
        // The pen (vector phase 3). Same division as above: the canvas turns
        // pointer events into document coordinates and modifier flags, and every
        // decision about what those mean lives in the view model.
        Canvas.SetPenHandlers(
            _vm.PenPress,
            _vm.PenDrag,
            _vm.PenRelease,
            _vm.PenHover);
        _vm.PenChanged += PublishPenPath;
        // The width tool (vector phase 4b). Same split again: the canvas turns
        // pointer events into document coordinates, and what they mean is the
        // view model's.
        Canvas.SetWidthHandlers(
            _vm.GrabWidthAt,
            _vm.DragWidth,
            () => _vm.EndWidthDrag());
        TimelineTrackView.KeyDragged += OnTrackKeyDragged;
        // The clip bars (Q57): body slides, edges trim, right-click splits;
        // the view model owns what that does to the record. An audio bar's
        // StripIndex is its section index; a video bar is named by its strip
        // and the frame its section starts on.
        TimelineTrackView.AudioClipEdited += (bar, kind, delta) =>
        {
            switch (kind)
            {
                case Controls.ClipEditKind.Slide: _vm.SlideAudioClip(bar.StripIndex, delta); break;
                case Controls.ClipEditKind.TrimIn: _vm.TrimAudioClipIn(bar.StripIndex, delta); break;
                case Controls.ClipEditKind.TrimOut: _vm.TrimAudioClipOut(bar.StripIndex, delta); break;
            }
        };
        TimelineTrackView.VideoClipEdited += (bar, kind, delta) =>
        {
            switch (kind)
            {
                case Controls.ClipEditKind.Slide: _vm.SlideVideoClip(bar.StripIndex, bar.Start, delta); break;
                case Controls.ClipEditKind.TrimIn: _vm.TrimVideoClipIn(bar.StripIndex, bar.Start, delta); break;
                case Controls.ClipEditKind.TrimOut: _vm.TrimVideoClipOut(bar.StripIndex, bar.Start, delta); break;
            }
        };
        TimelineTrackView.ClipMenuRequested += OnClipMenu;
        GraphEditorView.KeyEdited += (series, from, to, value) => _vm.EditCameraKey(series, from, to, value);
        GraphEditorView.KeyAddRequested += frame => _vm.AddCameraKeyAt(frame);
        GraphEditorView.KeyMenuRequested += OnGraphKeyMenu;
        Canvas.SetPlacementProvider(_vm.GetCurrentFramePlacements);

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
        _vm.Selection.SelectionChanged += Canvas.InvalidateVisual; // Redraw when object selection changes
        _vm.LazyBrushMoved += (x, y) => Canvas.SetLazyAnchor(x, y);
        _vm.LazyBrushCleared += () => Canvas.SetLazyAnchor(null, null);
        Canvas.InputDiagnostic += text => _vm.PenDiagnostic = text;
        // The canvas is the only place that knows how much of the document is
        // actually visible, and how long presenting a frame took.
        Canvas.DisplayScaleChanged += scale => _vm.SetDisplayScale(scale);
        Canvas.ViewportChanged += viewport => _vm.SetViewport(viewport);
        Canvas.FrameRendered += ms => _vm.RecordFrameTime(ms);
        // The backend is only knowable once a frame has been drawn, so the
        // startup report waits for that rather than for construction. One frame
        // later than "startup", and the only point at which it has an answer.
        //
        // BackendDetected is static and this handler is an instance method, so
        // the subscription would outlive the window and keep it alive. Detached
        // in OnClosed — a diagnostic has no business being the reason a window
        // cannot be collected.
        Rendering.CanvasControl.BackendDetected += WriteStartupRenderReport;
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
        Canvas.CameraPanned += (dx, dy) => _vm.NudgeCamera(dx, dy);
        Canvas.CameraZoomedBy += factor => _vm.ZoomCameraBy(factor);
        Canvas.CameraRotatedBy += deg => _vm.RotateCameraBy(deg);

        // One handler on the window instead of a pair per docker. Tunnelling, so
        // it sees the move even when a child marks it handled — a docker whose
        // content swallows pointer events would otherwise be invisible to the
        // shortcut scope and silently lose its bindings.
        AddHandler(
            PointerMovedEvent,
            (_, e) =>
            {
                _hoveredElement = e.Source as Visual;
                // Everything that is NOT the canvas. The canvas counts itself in
                // its own handler, because the question the two counters answer
                // is whether a frame reaching the screen depends on the canvas's
                // own invalidate — and the reported symptom is that moving over
                // a docker does not help. See InputPulse.
                if (!IsInsideCanvas(_hoveredElement)) Rendering.InputPulse.Elsewhere();
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        PointerExited += (_, _) => _hoveredElement = null;

        bool IsInsideCanvas(Visual? from)
        {
            for (var v = from; v is not null; v = v.GetVisualParent())
                if (ReferenceEquals(v, Canvas)) return true;
            return false;
        }
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
        // B164: while the transport runs, the canvas keeps a compositor frame on
        // request so the loop ticks at display rate instead of at the scene's
        // frame rate. Driven from here because the canvas does not know about
        // playback and should not — it only knows whether to keep asking.
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsPlaying))
            {
                Canvas.KeepPresenting = _vm.IsPlaying;
            }
        };

        _vm.ReferenceChanged += RefreshReferenceBoxes;
        _vm.GuidesChanged += RefreshGuides;

        // B58. The whole reason the rig was invisible: `RigMarks` existed and
        // nothing ever asked for it. Pushed rather than bound, following `Guides`
        // — the list is a flattened snapshot for the render thread, not a value
        // an artist edits.
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.RigMarks)
                or nameof(MainViewModel.RigEditMode)
                or nameof(MainViewModel.SelectedRigMarkId)
                or nameof(MainViewModel.CurrentFrameIndex))
            {
                RefreshRigOverlay();
            }
        };
        Canvas.RigPressed += (x, y, scale) =>
        {
            var hit = _vm.PressRig(x, y, scale);
            if (hit is { Id: { } id }) Canvas.BeginRigDrag(id, hit.Corner);
            RefreshRigOverlay();
        };
        Canvas.RigDragged += (id, corner, dx, dy) =>
        {
            _vm.DragRig(id, corner, dx, dy);
            RefreshRigOverlay();
        };
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
        // B80. Closing a tab asked about unsaved work and closing the window did
        // not, so quitting mid-edit lost it silently.
        Closing += OnWindowClosing;
        Canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasColorDragOver);
        Canvas.AddHandler(DragDrop.DropEvent, OnCanvasColorDrop);
        Canvas.AddHandler(DragDrop.DragOverEvent, OnCanvasSymbolDragOver);
        Canvas.AddHandler(DragDrop.DropEvent, OnCanvasSymbolDrop);
        // Files from outside land anywhere on the window and become
        // references. Registered on the window so no panel has to opt in, and
        // checked by format so the in-process drags above are untouched.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnFileDragOver);
        AddHandler(DragDrop.DropEvent, OnFileDrop);

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
        // B67. The canvas framing belongs to the document, and the window is the
        // only thing that holds both a tab and a CanvasControl.
        _vm.TabSwitched += CarryTheViewBetweenTabs;
        _vm.LastDocumentClosed += OnLastDocumentClosed;
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

        Canvas.PointerHovered += (x, y, mods) => _vm.UpdatePointerContext(x, y, mods);
        Canvas.PointerExited += (_, _) => _vm.ClearPointerContext();
        Canvas.ViewChanged += () =>
        {
            // The text, not the Content: the readout's content is a TextBlock that carries
            // its own width and rotation, and assigning a string over it would put the
            // squeezed, unrotated version back.
            ZoomLabelText.Text = $"{Canvas.ZoomPercent:0}%";
            MirrorButton.Background = Canvas.IsMirrored
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4a6ea9"))
                : Avalonia.Media.Brushes.Transparent;
        };

        _shortcuts.Load();
        ShowSaveGestures();
        KeyDown += OnKeyDown;
        // The release edge, so a borrowed tool comes back. Tunnelling, because a
        // focused control that swallows the key-up would otherwise leave the
        // modifier stuck down and the artist holding an eyedropper they let go
        // of — the failure mode that makes a momentary tool worse than none.
        AddHandler(
            KeyUpEvent,
            (_, e) =>
            {
                _vm.ApplyHeldModifiers(e.KeyModifiers);
                // The other half of a momentary tool key (B176): matched on the
                // physical key rather than the gesture, so a modifier pressed
                // mid-hold cannot orphan the release.
                if (_momentaryToolKey is { } held && e.Key == held.Key)
                {
                    _momentaryToolKey = null;
                    _vm.EndMomentaryTool(held.Tool);
                }
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // And a window that loses focus mid-hold never sees the key-up at all.
        Deactivated += (_, _) =>
        {
            _vm.ApplyHeldModifiers(Avalonia.Input.KeyModifiers.None);
            _momentaryToolKey = null;
            _vm.CancelMomentaryTool();
            // Same reasoning as the modifiers: a release delivered to whichever
            // window took the focus is one we will never see, and a gesture
            // still marked in-flight would resume from its stale position the
            // moment the pen touches down again (B185).
            Canvas.CancelPointerGestures();
        };
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
            // Picking a tab shows it and hides its siblings. Through the view
            // model rather than the layout, so it marks the workspace dirty like
            // any other rearrangement.
            panel.TabPicked += (_, id) => _vm.Workspace.Activate(id);
            panel.PanelDragStarted += BeginPanelDrag;
            // The float button follows movability: the timeline cannot leave
            // the bottom, so it offers no way to try.
            panel.CanFloat = DockPanels.Of(panel.PanelId).Movable;
            panel.FloatToggleRequested += OnFloatToggle;
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
    /// <summary>
    /// Put the canvas framing down on the tab being left, and pick up the one
    /// belonging to the tab being entered.
    /// </summary>
    /// <remarks>
    /// <b>B67.</b> A document nobody has framed yet opens fitted rather than at
    /// whatever the last one was left at — which is the reported defect, and is
    /// why the stored value is nullable rather than defaulted.
    /// </remarks>
    private void CarryTheViewBetweenTabs(ViewModels.DocumentTab? leaving, ViewModels.DocumentTab arriving)
    {
        if (leaving is not null) leaving.State.View = Canvas.Framing;
        if (arriving.State.View is { } framed) Canvas.Framing = framed;
        else Canvas.ResetView();
    }

    private void ApplyDockLayout()
    {
        var layout = _vm.Workspace.Layout;

        foreach (var (side, strip) in Strips())
        {
            // One control per slot — the one showing. The others in the slot are
            // its tabs and stay parked, so a hidden tab costs nothing but a word
            // in a header, which is the whole point of tabbing.
            var panels = new List<Docker>();
            foreach (var slot in layout.SlotsIn(side))
            {
                var usable = slot.Where(IsPanelUsable).ToList();
                if (usable.Count == 0) continue;

                // The active member may be one the document cannot use — a
                // project panel with no project. Fall back rather than leave
                // the slot blank.
                var active = usable.Contains(layout.ActiveOf(slot)) ? layout.ActiveOf(slot) : usable[0];
                var panel = _panels[active];
                // One call, not two assignments: between them the strip holds a
                // new tab list against an old active id, and the docker used to
                // read that as a click. See Docker.ShowTabs and B132.
                //
                // A slot of ONE also gets the list — the owner wants the tabbed
                // header even when a docker stands alone, so every panel wears
                // one treatment and dropping another onto it reads as joining
                // tabs that are already there.
                panel.ShowTabs(usable.Select(DockPanels.Of).ToList(), active);
                panels.Add(panel);
            }
            foreach (var panel in panels)
            {
                Detach(panel);
                panel.IsFloating = false;
            }
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
                // The bottom strip lives inside the centre column now, so the
                // sidebars keep their full height beside it.
                SizeRow(CentreColumn.RowDefinitions[2], occupied, extent, cap);
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
    /// <remarks>
    /// The dialog itself is <see cref="TextPrompt"/>, shared with the project
    /// window since it started creating documents and folders too.
    /// </remarks>
    private Task<string?> PromptForText(string title, string label, string initial) =>
        TextPrompt.ShowAsync(this, title, label, initial);

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

    /// <summary>Show the artist where the crash reports and the log are.</summary>
    /// <remarks>
    /// The folder rather than a message naming it: a path an artist has to
    /// retype is a path they will not use, and this is the moment they are
    /// already looking for the file. <c>FileReveal</c> knows how each desktop
    /// spells this and promises not to throw.
    /// </remarks>
    private void OnOpenDiagnosticsFolder(object? sender, RoutedEventArgs e)
    {
        var folder = _vm.DiagnosticsFolder;
        // The folder is created when something is first written to it, which
        // on a healthy installation may be never. Opening nothing would look
        // broken, so make it rather than explain it.
        try { System.IO.Directory.CreateDirectory(folder); }
        catch (Exception ex) { Rendering.CanvasControl.LogDiag("diagnostics-folder", ex); }

        if (!Services.FileReveal.Open(folder))
        {
            _vm.AiStatus = $"Could not open the folder — it is at {folder}";
        }
    }

    /// <summary>Let go of the static subscription this window took out.</summary>
    protected override void OnClosed(EventArgs e)
    {
        Rendering.CanvasControl.BackendDetected -= WriteStartupRenderReport;
        base.OnClosed(e);
    }

    /// <summary>
    /// Gather what the render report needs from the places that own each fact.
    /// </summary>
    /// <remarks>
    /// Assembled here rather than inside <c>RenderReport</c> so that class reaches
    /// into nothing: the backend is a static on the canvas, the frame's state is
    /// the canvas's, and the document and quality belong to the view model. A
    /// diagnostic that reaches across the app to collect itself is a diagnostic
    /// that breaks when any of them move.
    /// </remarks>
    /// <summary>
    /// Hand the canvas the nodes to draw, or nothing when isolation ends.
    /// </summary>
    /// <remarks>
    /// Derived from the session on every change rather than kept in step by
    /// hand. The session is the single source — the overlay is a view of it, and
    /// the hit test the artist's pointer meets is the session's own, so the two
    /// cannot disagree about where a node is.
    /// </remarks>
    private void PublishPathNodes()
    {
        if (_vm.PathEdit is not { } session)
        {
            // Nothing isolated: show what the white arrow is hovering, if
            // anything. Same channel as isolation and the pen, because it is
            // the same overlay and only one of the three can be live — see
            // PublishPenPath for why two node lists would be a question with no
            // answer. Nothing is drawn selected: a hover is a preview of what
            // is there, not a claim about what is picked.
            Canvas.SetPathTrace(null);
            Canvas.SetPathNodes(HoverGlyphs());
            return;
        }

        // The line's current shape, retraced on every session change, so a
        // node drag moves the path on screen and not only the glyphs — the
        // raster stroke cannot follow until the commit (invariant 6), but the
        // trace can and does. The same flatten the commit will run, so the
        // preview cannot promise a shape the release then changes.
        Canvas.SetPathTrace(Core.Geometry.PathFlattener.Flatten(session.Path));

        var glyphs = new List<Rendering.CanvasControl.PathNodeGlyph>(session.NodeCount);
        for (var i = 0; i < session.Path.Nodes.Count; i++)
        {
            var n = session.Path.Nodes[i];
            glyphs.Add(new Rendering.CanvasControl.PathNodeGlyph(
                n.X, n.Y, n.InX, n.InY, n.OutX, n.OutY, n.Corner, session.IsNodeSelected(i)));
        }
        Canvas.SetPathNodes(glyphs);
    }

    /// <summary>The hovered line's points, or null when nothing is hovered.</summary>
    private IReadOnlyList<Rendering.CanvasControl.PathNodeGlyph>? HoverGlyphs()
    {
        if (_vm.PathHover is not { } hover) return null;

        var glyphs = new List<Rendering.CanvasControl.PathNodeGlyph>(hover.Path.Nodes.Count);
        foreach (var n in hover.Path.Nodes)
        {
            glyphs.Add(new Rendering.CanvasControl.PathNodeGlyph(
                n.X, n.Y, n.InX, n.InY, n.OutX, n.OutY, n.Corner, Selected: false));
        }
        return glyphs;
    }

    /// <summary>
    /// Hand the pen's path to the canvas: the traced shape and its nodes.
    /// </summary>
    /// <remarks>
    /// <b>Through the same node channel as isolation</b>, because they are the
    /// same overlay and only one can be live at a time — the pen finishes its
    /// path when the tool changes, and isolation ends the same way. Two
    /// independent node lists would mean deciding which wins, which is a
    /// question with no answer that would show up on screen as both.
    /// <para>
    /// The node the pointer is shaping carries the handles, so it is the one
    /// marked selected — that is exactly the rule <c>DrawPathNodes</c> uses, and
    /// making it the <em>last</em> node rather than the shaped one would draw
    /// handles for a node nobody is holding.
    /// </para>
    /// </remarks>
    private void PublishPenPath()
    {
        if (_vm.Pen is not { NodeCount: > 0 } session)
        {
            Canvas.SetPenPreview(null);
            if (!_vm.PathEditActive) Canvas.SetPathNodes(null);
            return;
        }

        Canvas.SetPenPreview(session.Preview());

        var live = session.Shaping >= 0 ? session.Shaping : session.NodeCount - 1;
        var glyphs = new List<Rendering.CanvasControl.PathNodeGlyph>(session.NodeCount);
        for (var i = 0; i < session.Path.Nodes.Count; i++)
        {
            var n = session.Path.Nodes[i];
            glyphs.Add(new Rendering.CanvasControl.PathNodeGlyph(
                n.X, n.Y, n.InX, n.InY, n.OutX, n.OutY, n.Corner, i == live,
                // The closing indicator: ring the first node while a click
                // would join the path back to it.
                CloseHint: i == 0 && _vm.PenWouldClose));
        }
        Canvas.SetPathNodes(glyphs);
    }

    private Services.RenderReport.Facts RenderFacts()
    {
        var totals = Canvas.PresentedFrameTotals;
        return new Services.RenderReport.Facts(
            Rendering.CanvasControl.GraphicsBackend,
            Rendering.CanvasControl.SoftwareRendering,
            totals.OnGpu,
            totals.GpuFailed,
            Rendering.CanvasControl.MaxTextureSize,
            _vm.ReportDocWidth,
            _vm.ReportDocHeight,
            _vm.ReportDisplayScale,
            _vm.CanvasQuality.ToString(),
            _vm.ReportComposeScale,
            Rendering.CanvasControl.DurableFrameEnabled,
            totals.Presents > 0,
            _vm.ReportTileFallbacks,
            _vm.Prewarm,
            _vm.PlaybackPacing,
            Canvas.PresentWait,
            _vm.TickProfile.Snapshot(),
            _vm.TickProfile.Ticks,
            _vm.FrameCacheTraffic,
            _vm.SceneShape,
            Canvas.AnimationFrames,
            // The work the tick breakdown cannot see: drawing the composited
            // frame to the screen happens outside the tick entirely (B161).
            _vm.Performance.FrameMs,
            Canvas.TextureResidency,
            Rendering.GpuComposite.OptedIn,
            _vm.FramesReused,
            _vm.FlattenCacheTraffic,
            _vm.AwaitingUnpinBytes,
            _vm.PinnedBitmaps,
            _vm.TileStoreBytes,
            Rendering.StrokeToScreen.Shared.Snapshot,
            (_vm.LivePostPasses, _vm.LivePostTotalMs, _vm.LivePostWorstMs));
    }

    /// <summary>
    /// Write the startup report once the backend is knowable — which is the first
    /// frame, not construction: before anything is drawn there is no lease and so
    /// no answer to the only question this report exists to settle.
    /// </summary>
    private void WriteStartupRenderReport()
    {
        try
        {
            Services.RenderReport.WriteStartup(RenderFacts());
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("render-report-startup", ex);
        }
    }

    /// <summary>
    /// Write a full report, running the upload probe inside a lease first.
    /// </summary>
    /// <remarks>
    /// The probe needs the compositor's context, which only exists inside a draw
    /// operation, so this queues it and writes when it comes back. If no frame
    /// arrives — a hidden window — the report is still written, without a probe,
    /// because the facts and the session totals are most of its value.
    /// </remarks>
    private void OnWriteRenderReport(object? sender, RoutedEventArgs e)
    {
        var t = Canvas.PresentedFrameTotals;
        var totals = new Services.RenderReport.Totals(
            t.Presents, t.Full, t.Free, t.Patched, t.IfAlwaysFull,
            _vm.Performance.PublishMs, _vm.Performance.FrameMs);

        // The probe runs at the size the app is actually compositing at, so its
        // answer is about this document rather than a fixed benchmark canvas.
        var width = (int)Math.Ceiling(_vm.ReportDocWidth * _vm.ReportComposeScale);
        var height = (int)Math.Ceiling(_vm.ReportDocHeight * _vm.ReportComposeScale);

        Canvas.RunWithGpuContext(gpu =>
        {
            var probe = Services.RenderReport.RunUploadProbe(gpu, width, height);
            // Back to the UI thread to write and to report where it went.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var path = Services.RenderReport.WriteOnDemand(RenderFacts(), totals, probe);
                _vm.AiStatus = path is null
                    ? "Could not write the render report"
                    : $"Render report written to {path}";
            });
        });
    }

    /// <summary>Run a deliberate failure, after asking if there is anything to lose.</summary>
    /// <remarks>
    /// The warning is the scenario's own, not this method's: a survivable failure
    /// interrupts nobody, a crash asks when the drawing has edits worth keeping,
    /// and the hard kill always asks because it takes the process without any of
    /// the usual courtesies. One handler, three policies, decided by the list.
    /// </remarks>
    private async void OnRunCrashScenario(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string key }) return;
        if (Services.CrashScenarios.ByKey(key) is not { } scenario) return;

        var ask = scenario.Warning switch
        {
            Services.CrashWarning.Always => true,
            Services.CrashWarning.WhenUnsaved => _vm.SaveTargetTab?.IsDirty ?? false,
            _ => false,
        };

        if (ask && !await ConfirmAsync(
                "Trigger a test failure",
                $"{scenario.Label} — this ends Lightbox, and unsaved edits in the open drawing "
                    + "will be lost. The autosave recovery copy is not affected.",
                "Crash on purpose"))
        {
            return;
        }

        // Deliberately outside any try/catch. The whole point is that the
        // application's own handlers see this exactly as they would see a real
        // failure; catching it here would test this method instead.
        scenario.Run();
    }

    /// <summary>Put the build on the clipboard, because a bug report needs it.</summary>
    /// <remarks>
    /// "The newest build" is several different programs in a week. The label is
    /// the informational version, which carries the commit.
    /// </remarks>
    private async void OnCopyBuildLabel(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not { } clipboard) return;
        try
        {
            using var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(DataFormat.Text, _vm.BuildLabel));
            await clipboard.SetDataAsync(transfer);
            _vm.AiStatus = $"Copied: {_vm.BuildLabel}";
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("copy-build-label", ex);
        }
    }

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
        if (sender is not ListBox picker || picker.SelectedItem is not WorkspaceRow row) return;
        // The tabs SHOW the current workspace now, so selection is state rather
        // than a verb — and the guard is what stops the loop: applying raises
        // SelectedName, which re-selects the row, which fires this again.
        if (row.Name == _vm.Workspace.SelectedName) return;
        _vm.Workspace.Apply(row.Name);
    }

    private void OnDeleteWorkspace(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not string name) return;
        e.Handled = true;
        _vm.Workspace.Delete(name);
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
        // The button is up and no release reached us — a lost release (the
        // capture target was rebuilt under the pointer) must cancel the drag,
        // not leave a ghost chasing the mouse with no way to put it down.
        if (_dragHost is { } host && !e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
        {
            DragGhost.Hide();
            EndPanelDrag();
            return;
        }
        UpdateDropTarget(e);
        e.Handled = true;
    }

    private void OnPanelDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        DragGhost.Hide();
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
            // Onto a header: tab into that slot. Onto a body: a slot of its own.
            if (drop.IntoGroupOf is { } host) _vm.Workspace.JoinGroup(panel.PanelId, host);
            else _vm.Workspace.Dock(panel.PanelId, drop.Side, drop.Index);
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
        DragGhost.Hide();
    }

    /// <summary>
    /// Both halves of the feedback: what is moving, and where it would land.
    /// </summary>
    private void UpdateDropTarget(PointerEventArgs e)
    {
        DropIndicator.Show(ResolveDrop(e));

        // Null when the pointer cannot be mapped into this window — a drag that
        // has wandered off a floating panel onto the desktop. The ghost lives in
        // this window's overlay, so it stops at the edge rather than following.
        if (_dragging is { } panel && PointerOverRoot(e) is { } at)
        {
            DragGhost.Show(DockPanels.TitleOf(panel.PanelId), at);
        }
    }

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
                new DockRect(origin.X, origin.Y, panel.Bounds.Width, panel.Bounds.Height),
                // Measured rather than assumed a constant: the header carries a
                // tab strip now, and a band that does not match what is on
                // screen is a drop target you cannot see to aim at.
                panel.HeaderHeight));
        }
        return slots;
    }

    private static DockRect RectOf(Control c) => new(0, 0, c.Bounds.Width, c.Bounds.Height);

    // ---- floating panels -------------------------------------------------------

    /// <summary>
    /// The header's ⧉/⇱ button: float a docked panel from where it stands,
    /// or dock a floating one back where it came from.
    /// </summary>
    private void OnFloatToggle(Docker panel)
    {
        var id = panel.PanelId;
        if (_vm.Workspace.Layout.SideOf(id) == DockSide.Floating)
        {
            _vm.Workspace.Redock(id);
            return;
        }
        // Float it where it already is, so the panel appears to pop out of
        // the strip rather than teleporting somewhere new.
        var at = panel.PointToScreen(default);
        var info = DockPanels.Of(id);
        _vm.Workspace.Float(
            id, at.X + 24, at.Y + 24,
            Math.Max(panel.Bounds.Width, 260), Math.Max(panel.Bounds.Height, Math.Max(240, info.DefaultExtent)));
    }

    private void ShowFloating(DockPanelId id, Docker panel, DockLayout layout)
    {
        if (_floating.TryGetValue(id, out var open))
        {
            open.Activate();
            return;
        }
        Detach(panel);
        panel.IsFloating = true;
        var window = new FloatingPanelWindow(panel, layout.Place(id));
        window.Dismissed += floated =>
        {
            // The window's own close button closes the panel. Park it first so
            // the panel outlives the window that was showing it.
            if (!_floating.Remove(floated, out var w)) return;
            if (w.Release() is { } released) Park(released);
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

    /// <summary>The panels currently in windows of their own.</summary>
    internal IReadOnlyCollection<FloatingPanelWindow> FloatingWindowsForTests => _floating.Values;

    /// <summary>
    /// Take a panel out of its own window, because the layout no longer says it floats.
    /// </summary>
    /// <remarks>
    /// <b>Only parks what the window still holds, and that is the whole fix for B48.</b>
    /// Two different journeys end here and they need opposite things. Closing the window is
    /// closing the panel, so the panel has to be parked or it goes with the window. But
    /// <em>docking</em> a floating panel reaches here too — and by then
    /// <see cref="ApplyDockLayout"/> has already detached the panel from this window and
    /// put it in a strip, so the window has no content left. Parking unconditionally
    /// therefore did two wrong things at once: it dereferenced a null content, and had it
    /// not thrown it would have pulled the freshly docked panel straight back out of the
    /// strip and into the pool, leaving it in the layout and invisible.
    /// </remarks>
    private void CloseFloating(DockPanelId id)
    {
        if (!_floating.Remove(id, out var window)) return;
        if (window.Release() is { } panel) Park(panel);
        window.Close();
    }

    // Shortcut contexts follow the pointer: the same key can mean different
    // things over the canvas, the timeline, or the Layers docker.

    /// <summary>
    /// Which docker the key press belongs to, or the canvas scope when none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived by walking the tree, not by a flag per docker.</b> The old
    /// form kept a bool for the layers docker and another for the timeline, set
    /// from hand-wired <c>PointerEntered</c> handlers — so a docker could only
    /// own a binding if somebody had added a third bool, and eleven of the twelve
    /// never got one. Asking the element which <see cref="Controls.Docker"/>
    /// contains it works for every docker, including ones that do not exist yet.
    /// </para>
    /// <para>
    /// <b>Hover beats focus, and that is a choice.</b> Focus is sticky and
    /// invisible: leave it in the timeline, move the pointer to the colour
    /// docker, and a focus-first rule would have the same key do two different
    /// things with nothing on screen explaining why. The pointer is where the
    /// artist is looking. Focus is the fallback for the keyboard-only case,
    /// where there is no pointer to consult.
    /// </para>
    /// <para>
    /// <b>A docker's scope is its visible tab</b>, not the docker's own id: a
    /// tabbed docker showing the palette is the palette as far as a key press is
    /// concerned, whatever is behind it.
    /// </para>
    /// </remarks>
    private Services.ShortcutScope CurrentShortcutScope()
    {
        if (PanelUnder(_hoveredElement) is { } hovered) return Services.ShortcutScope.In(hovered);
        if (PanelUnder(FocusManager?.GetFocusedElement() as Visual) is { } focused)
        {
            return Services.ShortcutScope.In(focused);
        }
        return Services.ShortcutScope.Canvas;
    }

    /// <summary>The visible panel of the docker containing this element, if any.</summary>
    private static Docking.DockPanelId? PanelUnder(Visual? from)
    {
        for (var v = from; v is not null; v = v.GetVisualParent())
        {
            if (v is Controls.Docker docker) return docker.ActiveTab;
        }
        return null;
    }

    /// <summary>What the pointer is over, for <see cref="CurrentShortcutScope"/>.</summary>
    private Visual? _hoveredElement;

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

    private void OnLayerMenuMergeDown(object? sender, RoutedEventArgs e)
    {
        if (LayerRowOf(sender) is { } row) RequestMergeDown(row.Layer);
    }

    /// <summary>
    /// Merge a layer into the one below, asking first when the merge would
    /// turn drawings into pixels — the Q52 warning, shown only when AI is
    /// enabled because "the inbetweener cannot read pixels" is noise without
    /// one. A merge that keeps every stroke record just happens.
    /// </summary>
    private async void RequestMergeDown(Lightbox.Core.Documents.Layer? layer)
    {
        if (_vm.AiEnabled && _vm.MergeWouldBake(layer))
        {
            var below = _vm.MergeTargetOf(layer);
            var go = await AskImportChoice(
                "Merge layer down?",
                $"Blend modes, opacity or erasers here mean the merged drawings become pixels. "
                + $"The AI inbetweener reads strokes and cannot read pixels, so it will skip "
                + $"what lands on “{below?.Name}”.",
                ("Merge", "The drawings that need it are flattened to pixels; the rest keep their strokes.", true));
            if (go != true) return;
        }
        _vm.MergeLayerDown(layer);
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

    /// <summary>
    /// The timing chart editor (Q58), as a small window over the cel: the
    /// ladder from this extreme to the next key, preset shapes to start
    /// from, and a clear that returns the extreme to the bar's default.
    /// Edits write through the view model, so each is one undo step.
    /// </summary>
    private void OnEditTimingChart(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is not { } cell) return;
        var anchor = _vm.ChartAnchorFrame(cell);
        if (anchor < 0)
        {
            _vm.AiStatus = "A timing chart needs a key drawing to sit on.";
            return;
        }

        var dialog = new Window
        {
            Title = $"Timing chart on frame {anchor + 1}",
            Width = 300,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var ladder = new Controls.TimingChartView { Rungs = _vm.ChartAt(cell) };

        // Whether the chart is live, derived from the record on every edit —
        // a stale chart is ignored by the spacing curve, and that has to be
        // readable here rather than discovered by counting drawings.
        var state = new TextBlock { FontSize = 11, Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        void RefreshState()
        {
            var rungs = _vm.ChartAt(cell)?.Count ?? 0;
            var run = _vm.ChartRunInbetweens(cell);
            state.Text = rungs == 0
                ? "No chart — the bar's count and easing decide."
                : run is not { } drawings || drawings == 0
                    ? $"{rungs} rung{(rungs == 1 ? "" : "s")}: ＋ Inbetween draws one drawing per rung."
                    : rungs == drawings
                        ? $"{rungs} rung{(rungs == 1 ? "" : "s")}, matching the run — the spacing curve reads this chart."
                        : $"{rungs} rung{(rungs == 1 ? "" : "s")} but the run holds {drawings} drawing{(drawings == 1 ? "" : "s")} — the spacing curve keeps the easing until they agree.";
        }
        RefreshState();

        ladder.ChartEdited += chart =>
        {
            _vm.SetChartAt(cell, chart);
            ladder.Rungs = _vm.ChartAt(cell);
            RefreshState();
        };

        var hint = new TextBlock
        {
            Text = "Each rung is one inbetween. Drag to re-space, click to add, right-click to remove.",
            FontSize = 11,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.7,
        };

        // Preset shapes seed the ladder with today's rung count (3 when
        // empty), so "Ease in" answers with the chart it names rather than
        // asking for a count first.
        var presets = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 4 };
        foreach (var (label, easing) in (ValueTuple<string, Lightbox.Core.Inbetween.Easing>[])
                 [("Even", Lightbox.Core.Inbetween.Easing.Linear),
                  ("Ease in", Lightbox.Core.Inbetween.Easing.EaseIn),
                  ("Ease out", Lightbox.Core.Inbetween.Easing.EaseOut),
                  ("Ease in-out", Lightbox.Core.Inbetween.Easing.EaseInOut)])
        {
            var choice = easing;
            var button = new Button { Content = label, FontSize = 11, Padding = new Thickness(6, 2) };
            button.Click += (_, _) =>
            {
                // Seeded with the run's own drawing count when there is one,
                // so the preset lands live rather than pre-stale.
                var count = _vm.ChartAt(cell)?.Count
                    ?? (_vm.ChartRunInbetweens(cell) is { } run and > 0 ? run : 3);
                _vm.SetChartAt(cell, Lightbox.Core.Inbetween.TimingChart.FromEasing(count, choice));
                ladder.Rungs = _vm.ChartAt(cell);
                RefreshState();
            };
            presets.Children.Add(button);
        }

        var clear = new Button { Content = "Clear chart", FontSize = 11, Padding = new Thickness(6, 2) };
        clear.Click += (_, _) =>
        {
            _vm.SetChartAt(cell, null);
            ladder.Rungs = null;
            RefreshState();
        };

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(12),
            Spacing = 8,
            Children = { ladder, state, presets, clear, hint },
        };
        dialog.Show(this);
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

    /// <summary>
    /// Create a character sheet: name it first, then make sure the document it
    /// lives in has somewhere on disk to live.
    /// </summary>
    /// <remarks>
    /// <b>B66.</b> A sheet is part of its document (Q25 answered (a)), so an
    /// untitled document meant the work existed nowhere. The order is the fix:
    /// the name is asked for before anything is written — B65's rule on this
    /// surface — and the save is offered only once the sheet exists, so
    /// cancelling the save keeps the work rather than discarding it.
    /// </remarks>
    private async void OnAddReferenceSheet(object? sender, RoutedEventArgs e)
    {
        var suggested = $"Character {_vm.ReferenceSheetsView.Count + 1}";
        var name = await PromptForText("New character sheet", "Name", suggested);
        if (name is null) return;   // cancelled: nothing is created

        var needsAFile = _vm.AReferenceSheetWouldBeUnsaved;
        // Null only with nothing open, where the docker holding this button is
        // not on screen to be pressed.
        if (_vm.AddReferenceSheet(name) is not { } sheet) return;
        // B78: the picker opens already named, rather than asking a second time.
        if (needsAFile) await SaveDocumentAsAsync(sheet.Name);
    }

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

    /// <summary>One window per view: a second click brings it forward.</summary>
    private readonly Dictionary<string, ReferenceViewWindow> _referenceViewWindows = [];

    private void OnOpenReferenceViewWindow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not Lightbox.Core.Documents.ReferenceView view) return;
        if (_referenceViewWindows.TryGetValue(view.Id, out var open))
        {
            open.Activate();
            return;
        }

        var sheet = _vm.ReferenceSheetsView.FirstOrDefault(s => s.Views.Contains(view));
        var window = new ReferenceViewWindow(_vm, view, sheet?.Name ?? "Reference");
        _referenceViewWindows[view.Id] = window;
        window.Closed += (_, _) => _referenceViewWindows.Remove(view.Id);
        // A child of the main window: it floats beside the art, and closing
        // the application does not leave a reference orphaned on the desktop.
        window.Show(this);
    }

    private void OnToggleViewOnCanvas(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Lightbox.Core.Documents.ReferenceView view)
            _vm.ToggleViewOnCanvas(view);
    }

    /// <summary>
    /// The text in a sheet or view name box, captured when it took focus, so
    /// losing focus can tell a rename from a click-through.
    /// </summary>
    private string? _referenceNameOnFocus;

    private void OnReferenceNameFocused(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox box) _referenceNameOnFocus = box.Text ?? "";
    }

    /// <remarks>
    /// <b>B95.</b> Bound to <c>LostFocus</c>, which fires whether or not the
    /// artist typed anything — so this used to mark the document unsaved for
    /// clicking into a name box and out again. Compare against what was there
    /// on the way in, exactly as the project docker's rename does.
    /// </remarks>
    private void OnReferenceRenamed(object? sender, RoutedEventArgs e)
    {
        var before = _referenceNameOnFocus;
        _referenceNameOnFocus = null;
        if (sender is not TextBox box) return;
        // The two-way binding has already written the new name by now, so the
        // only record of the old one is what was captured on the way in.
        if (before is null || string.Equals(before, box.Text ?? "", StringComparison.Ordinal))
        {
            _vm.RefreshReferenceList();
            return;
        }
        // The DataContext says what was renamed — a sheet or a view — which the
        // view model needs now that a sheet can belong to the project rather
        // than to the document.
        _vm.MarkReferenceRenamed(box.DataContext);
    }

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
        // Mapped through BrushChoice so each row carries a picture of its mark. The
        // previews are cached on the preset's id and settings, so filtering — which is
        // what somebody does constantly in here — re-uses them rather than re-rendering.
        var tiles = matches.Select(BrushChoice.For).ToList();

        _pickingBrush = true;
        BrushPresetList.ItemsSource = tiles;
        BrushPresetList.SelectedItem = tiles.FirstOrDefault(t => t.Preset.Id == _vm.SelectedBrushPreset?.Id);
        _pickingBrush = false;

        BrushFilterEmpty.IsVisible = tiles.Count == 0;
    }

    private void OnBrushPresetPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_pickingBrush || BrushPresetList?.SelectedItem is not BrushChoice { Preset: { } preset }) return;
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

    private async void OnImportAudio(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add audio",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WAV audio") { Patterns = ["*.wav"], MimeTypes = ["audio/wav"] },
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;

        // Reference or embed (Q57): the artist chooses where the sound lives.
        var mb = new FileInfo(path).Length / (1024.0 * 1024.0);
        var embed = await AskImportChoice(
            "Where should the sound live?",
            $"“{Path.GetFileName(path)}” — {mb:0.#} MB.",
            ("Reference the file",
             "The document stays light and the file stays editable in your audio tool. Keep them together when sharing.",
             false),
            ("Embed a copy in the document",
             mb > 10
                 ? $"Self-contained — survives being shared alone — but carries {mb:0.#} MB through every save."
                 : "Self-contained: the document survives being shared without the file beside it.",
             true));
        if (embed is not { } chosen) return;

        _vm.AiStatus = _vm.ImportAudio(path, chosen) is { } error
            ? $"Audio import failed: {error}"
            : $"Timing against “{Path.GetFileName(path)}”.";
    }

    /// <summary>
    /// A small modal: a sentence of context, one button per choice with its
    /// cost written under it, and Cancel. Returns null when dismissed.
    /// </summary>
    private async Task<T?> AskImportChoice<T>(
        string title, string subtitle, params (string Label, string Detail, T Value)[] options)
        where T : struct
    {
        T? picked = null;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var stack = new StackPanel { Margin = new Thickness(16), Spacing = 8, MaxWidth = 440 };
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.85,
        });
        foreach (var (label, detail, value) in options)
        {
            var button = new Button
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = detail,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            FontSize = 11,
                            Opacity = 0.75,
                        },
                    },
                },
            };
            button.Click += (_, _) =>
            {
                picked = value;
                dialog.Close();
            };
            stack.Children.Add(button);
        }
        var cancel = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        cancel.Click += (_, _) => dialog.Close();
        stack.Children.Add(cancel);
        dialog.Content = stack;
        await dialog.ShowDialog(this);
        return picked;
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

    // Brush import used to live here, straight off a button in the brush options: pick files,
    // parse them all on this thread, add them. That is what made the window go transparent on
    // a fifty-six brush collection, and it also left the artist with fifty-six brushes and
    // nowhere to remove them. Both halves now live in BrushLibraryWindow.

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
                new FilePickerFileType("Images and video")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp",
                                "*.mp4", "*.mov", "*.avi", "*.mkv", "*.webm"],
                },
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"] },
                new FilePickerFileType("Video") { Patterns = ["*.mp4", "*.mov", "*.avi", "*.mkv", "*.webm"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        await ImportReferenceFile(path);
    }

    /// <summary>Extensions <see cref="ImportReferenceFile"/> accepts as still images.</summary>
    private static readonly string[] ReferenceImageExtensions =
        [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    /// <summary>Extensions <see cref="ImportReferenceFile"/> accepts as footage.</summary>
    private static readonly string[] ReferenceVideoExtensions =
        [".mp4", ".mov", ".avi", ".mkv", ".webm"];

    /// <summary>
    /// Import a file as reference — one path for the picker and for a file
    /// dropped onto the window, so the two can never drift apart.
    /// </summary>
    private async Task ImportReferenceFile(string path)
    {
        // Footage goes its own way (Q56/Q57): frames extracted at the
        // scene's fps, and the artist chooses what the document keeps.
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ReferenceVideoExtensions.Contains(ext))
        {
            var clipMb = new FileInfo(path).Length / (1024.0 * 1024.0);
            var storage = await AskImportChoice(
                "What is this clip for?",
                $"“{Path.GetFileName(path)}” — {clipMb:0.#} MB.",
                ("Reference — keep by path",
                 "To draw against. The document stays light; keep the clip beside it when sharing. Never exports.",
                 Services.ClipStorage.ReferenceByPath),
                ("Reference — embed the frames",
                 "To draw against, self-contained: the extracted frames travel in the document at reference quality. Never exports.",
                 Services.ClipStorage.ReferenceEmbedded),
                ("Production — embed the clip",
                 $"Part of the shot: the footage travels in the document at full fidelity ({clipMb:0.#} MB) and composites into video and PNG exports.",
                 Services.ClipStorage.Production));
            if (storage is not { } mode) return;

            _vm.AiStatus = "Reading the clip…";
            var error = await _vm.ImportVideoReference(path, mode);
            _vm.AiStatus = error ?? $"Drawing against “{Path.GetFileName(path)}”.";
            if (error is null) _vm.ReferenceDockerVisible = true;
            return;
        }

        if (_vm.ImportReferenceImageFile(path)) _vm.ReferenceDockerVisible = true;
    }

    /// <summary>
    /// Does a drag carry files this window would import as reference? The
    /// in-process drags (cels, colours, symbols) carry no file format, so
    /// they never reach the answer.
    /// </summary>
    private static List<string> DroppedReferenceFiles(DragEventArgs e)
    {
        var paths = new List<string>();
        if (e.DataTransfer?.TryGetFiles() is not { } items) return paths;
        foreach (var item in items)
        {
            if (item.TryGetLocalPath() is not { } path) continue;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ReferenceImageExtensions.Contains(ext) || ReferenceVideoExtensions.Contains(ext))
            {
                paths.Add(path);
            }
        }
        return paths;
    }

    private void OnFileDragOver(object? sender, DragEventArgs e)
    {
        if (DroppedReferenceFiles(e).Count == 0) return;
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>
    /// Any image dropped anywhere on the window becomes a reference — the
    /// shortest path from "found the perfect pose on disk" to drawing against
    /// it, with no menu in between. Footage goes through the same import as
    /// the picker, question dialog included.
    /// </summary>
    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        var files = DroppedReferenceFiles(e);
        if (files.Count == 0) return;
        e.Handled = true;
        foreach (var path in files)
        {
            await ImportReferenceFile(path);
        }
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
            // Reviving the object-selection mode rather than adding a parallel
            // one. It has been in the enum with its whole hit-test chain since
            // the selection manager landed, and nothing ever assigned it — so
            // picking a placement, guide or anchor has been unreachable. The
            // black arrow is what that code was always for.
            ToolId.Arrow => Rendering.CanvasControl.CanvasToolMode.Select,
            ToolId.DirectSelect => Rendering.CanvasControl.CanvasToolMode.PathEdit,
            ToolId.Pen => Rendering.CanvasControl.CanvasToolMode.Pen,
            ToolId.Width => Rendering.CanvasControl.CanvasToolMode.Width,
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
    private void OnToolbarSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ReflowToolRail(e.NewSize.Width, e.NewSize.Height);

    /// <summary>
    /// The rail's column count follows the window (Q56): one column when the
    /// window is tall enough to hold every tool in it, two as the ordinary
    /// case, three when the window is short and the rail wide enough — and
    /// the columns sit centred. Dragged past 150 px the rail becomes the
    /// labelled single-column list it always was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two modes, told apart by whether the column is still <c>Auto</c>.</b>
    /// Until the rail is dragged it sizes itself: the column count comes from
    /// the height alone and the column then measures to exactly that many
    /// tiles. Deriving the count from the width as well would be circular now
    /// that the width follows the count — and the fixed 88px that used to break
    /// the circle is what left a one-column rail sitting in the middle of 27px
    /// of nothing down each side.
    /// </para>
    /// <para>
    /// A drag writes pixels into the column, and from then on the artist's
    /// width is the input and the count follows it, exactly as before. That is
    /// also the only way to reach the three-column and labelled layouts, which
    /// is what the manual has always said: three columns need the rail
    /// <i>dragged</i> wide enough.
    /// </para>
    /// </remarks>
    private void ReflowToolRail(double width, double height)
    {
        if (height <= 0) return;

        var chosen = !WorkArea.ColumnDefinitions[0].Width.IsAuto;
        if (chosen && width <= 0) return;

        if (chosen && width >= 150)
        {
            Toolbar.Classes.Set("labels", true);
            ToolButtons.ItemWidth = Math.Max(40, width - 14);
            ToolButtons.Width = double.NaN;
            return;
        }
        Toolbar.Classes.Set("labels", false);

        const double tile = 34;
        var visible = ToolButtons.Children.Count(c => c.IsVisible);
        if (visible == 0) return;
        var first = ToolButtons.Children.First(c => c.IsVisible);
        var tileH = first.Bounds.Height > 1 ? first.Bounds.Height + 4 : 32;

        // Self-sizing stops at two. A third column is worth the canvas it costs
        // only when somebody has asked for it, and asking is the drag.
        var most = chosen ? Math.Clamp((int)((width - 8) / tile), 1, 3) : 2;
        var cols = 1;
        while (cols < most && Math.Ceiling(visible / (double)cols) * tileH > height - 8) cols++;

        ToolButtons.ItemWidth = tile;
        ToolButtons.Width = cols * tile;
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

        // Isolation owns Escape and Enter for the same reason, and above the
        // shortcut switch for the same reason: a mode you cannot leave with the
        // key everybody tries first is a mode you are stuck in. Both keys mean
        // "done" here rather than "keep" and "discard" — every node drag was
        // already its own undo step, so there is nothing left to discard.
        if (_vm.PathEditActive && e.Key is Key.Escape or Key.Enter)
        {
            _vm.EndPathEdit();
            e.Handled = true;
            return;
        }

        // A pen path in progress owns the same two keys, and Backspace as well.
        // Above the shortcut switch for isolation's reason and one more: Delete
        // is `lines.delete` down there, and a pen halfway through a path is the
        // one moment where that key plainly means "take the last point off".
        // Only while the pen is in hand: a parked path (the session survives a
        // tool switch now) must not swallow Delete from the arrow's selection
        // or Escape from whatever mode the current tool is in.
        if (_vm.PenActive && _vm.IsPenTool)
        {
            if (e.Key is Key.Escape or Key.Enter)
            {
                // Both mean "done", and neither discards — see FinishPen. What
                // was drawn is one undo step, so Ctrl+Z is the way to lose it.
                _vm.FinishPen();
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Back or Key.Delete)
            {
                _vm.RemoveLastPenNode();
                e.Handled = true;
                return;
            }
        }

        // Both edges go through one call: a press and a release are the same
        // question — what should the tool be now — and answering it in two
        // handlers is how a modifier gets stuck down when a key-up goes missing.
        _vm.ApplyHeldModifiers(e.KeyModifiers);

        var shortcutId = _shortcuts.IdFor(e, CurrentShortcutScope());

        // A momentary tool key (B176): the press borrows, and the release
        // decides — a tap latches, a hold restores. The physical key is
        // remembered here because the release may arrive with different
        // modifiers than the press and would no longer resolve to the same
        // gesture; the key itself is the identity that survives that.
        if (shortcutId is not null
            && _shortcuts.Find(shortcutId)?.MomentaryTool is { } momentary)
        {
            _momentaryToolKey = (e.Key, momentary);
            _vm.BeginMomentaryTool(momentary);
            e.Handled = true;
            return;
        }

        switch (shortcutId)
        {
            case "file.save":
                // Deliberately the same path as the menu item rather than _vm.Save(): a
                // document with nowhere to go has to reach the picker, and duplicating
                // that decision here is how the two drift apart.
                OnSaveInPlaceClicked(this, e);
                e.Handled = true;
                break;
            case "file.saveAs":
                _ = SaveDocumentAsAsync();
                e.Handled = true;
                break;
            case "file.saveVersion":
                // Same path as the menu item, for the reason file.save gives:
                // the "is there anything to version" answer lives in one place.
                OnSaveVersionClicked(this, e);
                e.Handled = true;
                break;
            case "file.versionHistory":
                OnVersionHistoryClicked(this, e);
                e.Handled = true;
                break;
            case "canvas.transform":
                if (!_vm.TransformActive) _vm.BeginTransform();
                break;
            case "project.refresh":
                // Harmless with no project — the command guards on it — so this
                // does not need a HasProject check that could drift from the one
                // in the view model.
                _vm.ProjectDocker.RefreshFromDiskCommand.Execute(null);
                e.Handled = true;
                break;
            case "project.window":
                // Harmless with no project, for the same reason as above: the
                // method guards on it rather than the key handler holding a
                // second copy of the condition.
                _ = OpenProjectWindowAsync();
                e.Handled = true;
                break;
            case "image.resizeCanvas":
                _ = ResizeAsync(ViewModels.ResizeMode.Canvas);
                e.Handled = true;
                break;
            case "image.resizeImage":
                _ = ResizeAsync(ViewModels.ResizeMode.Image);
                e.Handled = true;
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
            // tool.eraser and canvas.pickColor never reach this switch: they
            // carry MomentaryTool, so the branch above owns both their tap
            // (latch) and their hold (borrow and restore).
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
            case "docker.mergeDown":
                RequestMergeDown(null); // null = the active layer
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
            case "canvas.rigEditMode":
                _vm.RigEditMode = !_vm.RigEditMode;
                e.Handled = true;
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
            case "tool.arrow":
                _vm.SelectToolCommand.Execute(ToolId.Arrow);
                break;
            case "tool.directselect":
                _vm.SelectToolCommand.Execute(ToolId.DirectSelect);
                break;
            case "tool.pen":
                _vm.SelectToolCommand.Execute(ToolId.Pen);
                break;
            case "tool.shape":
                _vm.SelectToolCommand.Execute(ToolId.Shape);
                break;
            case "tool.width":
                _vm.SelectToolCommand.Execute(ToolId.Width);
                break;
            case "lines.simplify":
                _vm.SimplifyLineCommand.Execute(null);
                break;
            case "lines.delete":
                _vm.DeleteSelectedLinesCommand.Execute(null);
                break;
            // B173. Delete asks the marquee first and falls back to the lines,
            // so the decision lives in the command rather than being split
            // between here and there — this is the case the shortcut registry
            // exists to keep honest, and a branch in the key handler is exactly
            // what the Configure window cannot see.
            case "select.clear":
                _vm.DeleteSelectionContentsCommand.Execute(null);
                break;
            case "select.fillBackground":
                _vm.FillSelectionWithBackgroundCommand.Execute(null);
                break;
            case "lines.recolour":
                _vm.RecolourSelectedLinesCommand.Execute(null);
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

    private async void OnConfigureClicked(object? sender, RoutedEventArgs e)
    {
        await new ConfigureWindow(_shortcuts, _vm).ShowDialog(this);
        // A rebind has to reach the menu labels, or they advertise the old key.
        ShowSaveGestures();
    }

    /// <summary>
    /// Open the project window — Q29's second surface.
    /// </summary>
    /// <remarks>
    /// Modal on the main window, like Configure and Export. It edits the same
    /// manifest the docker is showing, and two surfaces writing one project with
    /// neither knowing about the other is the class of bug B61 was; the docker
    /// re-reads on close through the same <c>changed</c> callback every other
    /// edit uses.
    /// </remarks>
    private async Task OpenProjectWindowAsync()
    {
        if (_vm.ProjectDocker.Project is not { } project) return;
        var window = new Views.ProjectWindow(project, () => _vm.ProjectDocker.MarkManifestChanged());
        // What creation needs, supplied rather than reached for: the same
        // blank document every other creator makes, the docker's dirty set so
        // the save writes what the window made, and the save itself so
        // "created" means "on disk" — the window is used between drawings,
        // where a pending badge would only defer the question.
        window.ViewModel.NewDocument = _vm.NewProjectDocument;
        window.ViewModel.DocumentCreated = _vm.ProjectDocker.MarkDirty;
        window.ViewModel.RequestSave = () => _vm.SaveProject();
        await window.ShowDialog(this);
        _vm.ProjectDocker.Refresh();
    }

    private async void OnProjectWindowClicked(object? sender, RoutedEventArgs e) =>
        await OpenProjectWindowAsync();

    private async void OnResizeCanvasClicked(object? sender, RoutedEventArgs e) =>
        await ResizeAsync(ViewModels.ResizeMode.Canvas);

    private async void OnResizeImageClicked(object? sender, RoutedEventArgs e) =>
        await ResizeAsync(ViewModels.ResizeMode.Image);

    /// <summary>
    /// Ask for a size, then hand it to the view model and refit the view.
    /// </summary>
    /// <remarks>
    /// The view is refitted because the paper is a different size than the one
    /// the current zoom and pan were chosen for — leaving a grown canvas half
    /// off-screen reads as the resize having gone wrong.
    /// </remarks>
    private async Task ResizeAsync(ViewModels.ResizeMode mode)
    {
        var dialog = new Views.ResizeDialog(_vm.Doc.Scene, mode);
        await dialog.ShowDialog(this);
        if (!dialog.Confirmed) return;
        if (_vm.ApplyResize(dialog.Choice)) Canvas.ResetView();
    }

    /// <summary>
    /// The tip workshop. A window rather than a docker because making a tip is
    /// not something you do mid-stroke, and a panel for it would cost layout
    /// space in every session that never opens one.
    /// </summary>
    private async void OnBrushTipsClicked(object? sender, RoutedEventArgs e) =>
        await new BrushTipsWindow(_vm).ShowDialog(this);

    /// <summary>
    /// The brush library — import, rename, remove.
    /// </summary>
    /// <remarks>
    /// Reached from three places on purpose: the Edit menu, the brush options page, and the
    /// bottom of the picker flyout. The picker is where an artist is standing when they notice
    /// they have forty brushes they did not choose, and making them close it and go to a menu
    /// is the friction that left the collection sitting there.
    /// <para>
    /// The picker's flyout is hidden first. Opening a modal from inside a flyout leaves the
    /// flyout floating over it, which looks like two windows arguing.
    /// </para>
    /// </remarks>
    private async void OnBrushLibraryClicked(object? sender, RoutedEventArgs e)
    {
        if (BrushPickerButton?.Flyout is { } picker) picker.Hide();
        await new BrushLibraryWindow(_vm).ShowDialog(this);
        RefreshBrushPickerButton();
        RefreshPresetPage();
    }

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

    /// <summary>
    /// A dot on the track timeline was dragged to a new frame. Track 0 is the
    /// camera when one exists (the projection puts it on top); everything
    /// after maps onto LayerRows in the same order.
    /// </summary>
    private void OnTrackKeyDragged(int trackIndex, int fromFrame, int toFrame)
    {
        var hasCamera = _vm.HasCamera;
        if (hasCamera && trackIndex == 0)
        {
            _vm.MoveCameraKey(fromFrame, toFrame);
            return;
        }
        var rowIndex = trackIndex - (hasCamera ? 1 : 0);
        if (rowIndex < 0 || rowIndex >= _vm.LayerRows.Count) return;
        var row = _vm.LayerRows[rowIndex];
        var from = row.Cells.FirstOrDefault(c => c.Index == fromFrame);
        var to = row.Cells.FirstOrDefault(c => c.Index == toFrame);
        if (from is null || to is null) return;
        _vm.MoveCel(from, to, copy: false);
    }

    /// <summary>
    /// The graph's key menu: how this key eases into the next, and removal.
    /// Built in code because the flyout needs the frame it was asked about.
    /// </summary>
    private void OnGraphKeyMenu(string series, int frame, Avalonia.Point at)
    {
        var current = _vm.CameraKeyEaseAt(frame);
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };
        foreach (var ease in (Lightbox.Core.Inbetween.Easing[])
                 [Lightbox.Core.Inbetween.Easing.Linear, Lightbox.Core.Inbetween.Easing.EaseIn,
                  Lightbox.Core.Inbetween.Easing.EaseOut, Lightbox.Core.Inbetween.Easing.EaseInOut])
        {
            var item = new MenuItem
            {
                Header = ease == current ? $"✓ {ease}" : ease.ToString(),
            };
            var chosen = ease;
            item.Click += (_, _) => _vm.SetCameraKeyEase(frame, chosen);
            flyout.Items.Add(item);
        }
        flyout.Items.Add(new Separator());
        var remove = new MenuItem { Header = $"Remove key at {frame + 1}" };
        remove.Click += (_, _) => _vm.RemoveCameraKeyAt(frame);
        flyout.Items.Add(remove);
        flyout.ShowAt(GraphEditorView, showAtPointer: true);
    }

    /// <summary>
    /// Right-clicking a clip bar (Q57): what can be done to a section, at the
    /// playhead. A cut is offered only where one is possible — inside a
    /// section rather than at its edge — and says so when it is not, because a
    /// menu item that silently does nothing teaches an artist to distrust the
    /// menu.
    /// </summary>
    private void OnClipMenu(Controls.ClipBar bar, bool isAudio, Avalonia.Point at)
    {
        var frame = _vm.CurrentFrameIndex;
        var insideSection = frame > bar.Start && frame <= bar.End;
        var flyout = new MenuFlyout { Placement = PlacementMode.Pointer };

        var split = new MenuItem
        {
            Header = $"Split at frame {frame + 1}",
            IsEnabled = insideSection,
        };
        split.Click += (_, _) =>
        {
            if (isAudio) _vm.SplitAudioAtPlayhead();
            else _vm.SplitVideoAtPlayhead(bar.StripIndex);
        };
        flyout.Items.Add(split);

        if (!insideSection)
        {
            flyout.Items.Add(new MenuItem
            {
                Header = "Move the playhead inside the clip to cut it",
                IsEnabled = false,
            });
        }
        flyout.ShowAt(TimelineTrackView, showAtPointer: true);
    }

    // ---- the chrome is ours -------------------------------------------------

    /// <summary>
    /// A press on the title bar's bare chrome moves the window. Presses that
    /// land on the menu, a caption button or anything else interactive stay
    /// with their control — the same walk the docker headers do.
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        for (var node = e.Source as Visual; node is not null && !ReferenceEquals(node, TitleBar); node = node.GetVisualParent())
        {
            if (node is Button or Menu or MenuItem) return;
        }
        BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        for (var node = e.Source as Visual; node is not null && !ReferenceEquals(node, TitleBar); node = node.GetVisualParent())
        {
            if (node is Button or Menu or MenuItem) return;
        }
        ToggleMaximised();
    }

    private void OnMinimiseClicked(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximiseClicked(object? sender, RoutedEventArgs e) => ToggleMaximised();

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximised() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;


    private async void OnNewClicked(object? sender, RoutedEventArgs e)
    {
        var settings = await new NewDocumentDialog().ShowDialog<NewDocumentSettings?>(this);
        if (settings is null) return;
        _vm.NewDocument(settings);
    }

    /// <summary>What the artist chose when told the document has unsaved changes.</summary>
    private enum UnsavedChoice
    {
        /// <summary>Closed the dialog, or pressed Escape. The document stays open.</summary>
        Cancel,

        /// <summary>Close it and lose the edits.</summary>
        Discard,

        /// <summary>Write it first, then close.</summary>
        Save,
    }

    private async void OnCloseTabClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not DocumentTab tab) return;
        // HasWorkToLose, not IsDirty — B99 split them exactly for this moment.
        // A brand-new document badges dirty because it differs from disk (it has
        // no disk), but there is nothing in it to lose, and "close the tab you
        // just made" should not argue. Gating on IsDirty was masked while
        // closing the last tab conjured a replacement; with the application able
        // to empty, it put the unsaved-changes dialog on an untouched blank.
        if (!tab.HasWorkToLose)
        {
            _vm.CloseTab(tab);
            return;
        }

        switch (await ConfirmDiscardAsync(tab.Title))
        {
            case UnsavedChoice.Cancel:
                return;

            case UnsavedChoice.Save:
                // Save acts on the active document, so the tab being closed has
                // to be the active one — otherwise pressing Save on tab B's
                // dialog would write tab A and close B unsaved, which is the
                // failure this whole entry is about wearing a different hat.
                _vm.ActiveTab = tab;
                await SaveOrSaveAsAsync(tab.Title);

                // Still dirty means the file picker was cancelled. Closing now
                // would discard the work the artist just asked to keep, so the
                // close is abandoned instead — Cancel on the picker cancels the
                // close, which is the only reading that does not lose anything.
                if (tab.IsDirty) return;
                break;

            case UnsavedChoice.Discard:
                break;
        }
        _vm.CloseTab(tab);
    }

    /// <remarks>
    /// <b>B75.</b> This offered Discard and Cancel only, so the artist who wanted
    /// to keep the work had to cancel, save by hand and close again — and the one
    /// who did not read carefully lost it. Save is the default button because it
    /// is the outcome that cannot destroy anything; Discard is the one that
    /// needs deliberate aim.
    /// </remarks>
    private Task<UnsavedChoice> ConfirmDiscardAsync(string title) => ConfirmDiscardAsync([title]);

    /// <inheritdoc cref="ConfirmDiscardAsync(string)"/>
    /// <remarks>
    /// <b>B80.</b> Takes a list because closing the window can have several
    /// documents in flight, and a chain of modal boxes — one per tab, each
    /// answerable differently — is a worse answer than one that says how much is
    /// at stake. The single-tab call is the same dialog with one name in it, so
    /// the two paths cannot drift into disagreeing about what Save means.
    /// </remarks>
    private async Task<UnsavedChoice> ConfirmDiscardAsync(IReadOnlyList<string> titles)
    {
        var result = UnsavedChoice.Cancel;
        var dialog = new Window
        {
            Title = "Unsaved changes",
            Width = 380,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        // "Save all" when there is more than one, because "Save" beside a list of
        // four names does not say whether it means all of them or the first.
        var save = new Button
        {
            Content = titles.Count > 1 ? "Save all" : "Save",
            MinWidth = 80,
            IsDefault = true,
        };
        var discard = new Button { Content = "Discard changes", MinWidth = 120 };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        save.Click += (_, _) => { result = UnsavedChoice.Save; dialog.Close(); };
        discard.Click += (_, _) => { result = UnsavedChoice.Discard; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    // Named rather than counted. "3 documents have unsaved
                    // changes" is a number to weigh and the names are the thing
                    // an artist actually recognises — and the list is what makes
                    // Discard a decision rather than a gamble.
                    Text = titles.Count == 1
                        ? $"“{titles[0]}” has unsaved changes."
                        : $"{titles.Count} documents have unsaved changes:\n"
                          + string.Join("\n", titles.Select(t => $"    • {t}")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    // Discard sits furthest from Save on purpose: the destructive
                    // button should not be the one a fast hand lands on next to
                    // the safe one.
                    Children = { discard, cancel, save },
                },
            },
        };
        await dialog.ShowDialog(this);
        return result;
    }

    /// <summary>
    /// Set once the artist has answered the unsaved-changes dialog and the close
    /// may go ahead, so the re-close does not ask again.
    /// </summary>
    private bool _closeConfirmed;

    /// <summary>
    /// Closing the application asks about unsaved work, the way closing a tab does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>B80.</b> There was no handler at all — closing a *tab* asked and
    /// closing the *window* did not, so quitting with edits in flight lost them
    /// silently. Worse than B75, which at least told the artist something.
    /// </para>
    /// <para>
    /// <b>Cancel, then re-close.</b> <c>Closing</c> cannot be awaited, so the
    /// only way to ask a question during it is to refuse the close, ask, and
    /// close again — which is why <see cref="_closeConfirmed"/> exists rather
    /// than being a smell. Without it the second <c>Close()</c> re-enters here
    /// and asks the same question for ever.
    /// </para>
    /// <para>
    /// <b>A cancelled picker abandons the whole close</b>, exactly as it does for
    /// a single tab: the artist asked to keep the work, and no reading of
    /// "cancel the save" ends with the application exiting and the work gone.
    /// </para>
    /// </remarks>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed) return;

        // B99. HasWorkToLose, not IsDirty: a never-saved document badges from
        // the moment it exists, and File ▸ New followed by a close must not
        // argue about a drawing nobody made. What this must still catch — and
        // what B80 shipped unable to catch — is a new document that *has* been
        // drawn in, which has no file at all and is the easiest work to lose.
        var dirty = _vm.Tabs.Where(t => t.HasWorkToLose).ToList();
        if (dirty.Count == 0) return;

        e.Cancel = true;
        switch (await ConfirmDiscardAsync(dirty.Select(t => t.Title).ToList()))
        {
            case UnsavedChoice.Cancel:
                return;

            case UnsavedChoice.Save:
                foreach (var tab in dirty)
                {
                    // A project save writes every dirty document at once, so by
                    // the time the loop reaches the second tab it is usually
                    // already clean. Re-saving would be harmless and asking for
                    // a filename again would not.
                    if (!tab.IsDirty) continue;
                    // Save acts on the active document, so the tab being saved
                    // has to be the active one — the same trap B75 records, and
                    // it is worse here because the loop would write one document
                    // several times and never touch the others.
                    _vm.ActiveTab = tab;
                    await SaveOrSaveAsAsync(tab.Title);
                    if (tab.IsDirty) return;
                }
                break;

            case UnsavedChoice.Discard:
                break;
        }

        _closeConfirmed = true;
        Close();
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e) => await SaveDocumentAsAsync();

    /// <param name="suggestedName">
    /// What to put in the picker's name box, when the caller already asked the
    /// artist for a name.
    /// </param>
    /// <remarks>
    /// <b>B78.</b> B66 added a name prompt before creating a character sheet and
    /// then offered this dialog for a document with no file — so the artist typed
    /// a name and was immediately asked for one again, with the first answer
    /// thrown away. The two prompts ask different questions, *what is this
    /// called* and *where does it go*, and the second should arrive already
    /// answering the first. Two correct prompts in sequence are one bad prompt.
    /// </remarks>
    private async Task SaveDocumentAsAsync(string? suggestedName = null)
    {
        var stem = string.IsNullOrWhiteSpace(suggestedName)
            ? _vm.ActiveTab?.Title ?? "untitled"
            : suggestedName.Trim();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save animation",
            SuggestedFileName = $"{stem}.lightbox.json",
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
    private async void OnSaveInPlaceClicked(object? sender, RoutedEventArgs e) =>
        await SaveOrSaveAsAsync();

    /// <summary>
    /// Save where the document already lives, or ask where to put it.
    /// </summary>
    /// <remarks>
    /// The rule the Save button has always followed, extracted so the
    /// unsaved-changes dialog gives the same answer (B75). Two places deciding
    /// separately what Save means is how they come to disagree — and the one
    /// that would have been written second is the one an artist meets while
    /// losing work.
    /// </remarks>
    private async Task SaveOrSaveAsAsync(string? suggestedName = null)
    {
        if (_vm.CanSaveInPlace)
        {
            _vm.Save();
            return;
        }
        // Nowhere to put it yet, so Save falls through to Save as… rather than
        // silently doing nothing.
        await SaveDocumentAsAsync(suggestedName);
    }

    /// <summary>
    /// Make sure the document is on disk before something outside the app is told about it.
    /// </summary>
    /// <param name="action">
    /// What was attempted, phrased to follow "before" — "exporting", "marking this Ready".
    /// </param>
    /// <param name="refuseLabel">What refusing does, named after the thing it undoes.</param>
    /// <returns>
    /// True when the document is saved and the caller may go ahead; false when the artist
    /// declined, in which case the caller must undo whatever prompted this.
    /// </returns>
    /// <remarks>
    /// Shared by the export and the status change on purpose. Both are claims about a file
    /// made to somebody else — an engine, a designer waiting on the asset — and two
    /// implementations of "is it saved?" would eventually disagree about it.
    /// </remarks>
    private async Task<bool> EnsureSavedAsync(
        string action, string refuseLabel, (string? FilePath, bool HasUnsavedEdits)? about = null)
    {
        // The row being marked when there is one, otherwise the document in front. Asking
        // about the active tab while marking a different row is the near-miss this avoids.
        var facts = about ?? (_vm.SaveTargetTab?.FilePath, _vm.SaveTargetTab?.IsDirty ?? false);
        var gate = Services.SaveRequirement.For(facts.FilePath, facts.HasUnsavedEdits);

        if (gate == Services.SaveGate.SaveInPlaceFirst)
        {
            // It already has a home, so no dialog: asking permission to write where the
            // artist already said it goes is a click in the way.
            _vm.Save();
            return true;
        }
        if (!Services.SaveRequirement.NeedsTheArtist(gate)) return true;

        var choice = await new SaveFirstDialog(
            Services.SaveRequirement.Explain(gate, action), refuseLabel)
            .ShowDialog<SaveFirstChoice?>(this);
        if (choice != SaveFirstChoice.SaveAs) return false;

        await SaveDocumentAsAsync();
        // Saved, or the picker was cancelled too — either way the answer is whether there
        // is a file now, not whether a button was pressed.
        return _vm.SaveTargetTab?.FilePath is { Length: > 0 };
    }

    /// <summary>
    /// Put the current Save / Save as gestures on their menu items.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="Services.ShortcutMap"/> rather than written in the XAML, which
    /// is the whole point of registering them: an artist who rebinds Save sees the new key
    /// on the menu instead of a label that lies. Refreshed after the Configure window
    /// closes for the same reason.
    /// </remarks>
    private void ShowSaveGestures()
    {
        SaveMenu.InputGesture = _shortcuts.Definitions.FirstOrDefault(d => d.Id == "file.save")?.Current;
        SaveAsMenu.InputGesture = _shortcuts.Definitions.FirstOrDefault(d => d.Id == "file.saveAs")?.Current;
        SaveVersionMenu.InputGesture = _shortcuts.Definitions.FirstOrDefault(d => d.Id == "file.saveVersion")?.Current;
        VersionHistoryMenu.InputGesture = _shortcuts.Definitions.FirstOrDefault(d => d.Id == "file.versionHistory")?.Current;
    }

    /// <summary>
    /// <c>File ▸ Save version…</c> — ask for a label and notes, then keep a
    /// copy of the active document or sheet in the project's history.
    /// </summary>
    private async void OnSaveVersionClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.VersionableResource is not { } resource || _vm.ProjectDocker.Project is not { } project)
        {
            _vm.AiStatus = "Versions live in a project — save this document into one first.";
            return;
        }
        // The offered label continues the numbering the history already has,
        // so accepting the default is always a sensible answer (B107's rule
        // applied to content rather than caret position).
        var next = Lightbox.Core.Projects.ProjectVersions.StoreFor(project)
            .GetVersions(resource.Id).Length + 1;
        if (await SaveVersionPrompt.ShowAsync(this, resource.Name, $"v{next}") is not { } answer) return;
        _vm.SaveVersionOfActiveTab(answer.Label, answer.Notes);
    }

    /// <summary><c>File ▸ Version history…</c> for the active tab's resource.</summary>
    private async void OnVersionHistoryClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.HistoryForActiveTab() is not { } history) return;
        await new VersionHistoryWindow(history).ShowDialog(this);
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

        // B108. One rule, before any other question: a press on a row selects
        // that row — either button, every kind of row. It used to select on
        // right-click for anything and on left-click only for a document, so a
        // left press on a folder left the selection wherever it was. Both
        // toolbar surfaces read that selection as "where I am", which is how
        // 🗁 came to reveal the project folder and ＋ New to file at the root
        // while the artist was plainly looking at a folder.
        //
        // The decision lives in the docker (SelectFromPointer) rather than here,
        // because synthetic pointer input is unreliable in this environment and
        // a rule that only exists inside a pointer handler is one no test can
        // reach.
        _vm.ProjectDocker.SelectFromPointer(pressed);

        // Right-click stops here: every item in the row's menu acts on the
        // selection, which has just been set to the row under the pointer.
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

        // Documents and sheets drag; folders stay drop targets only.
        if (pressed is not ({ Animation: not null } or { Sheet: not null }) || pressed is not { } row) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _draggedRow = row;
        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ProjectRowFormat, row.Key ?? ""));
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

    // B114. Five handlers became one: Animation, Character, Scene, Shot and
    // Document were four names for two things, and the New menu says so now.
    private async void OnProjectNewDocument(object? sender, RoutedEventArgs e) =>
        await CreateProjectItemAsync(ProjectViewModel.NewDocumentItem);

    private async void OnProjectNewFolder(object? sender, RoutedEventArgs e) =>
        await CreateProjectItemAsync(ProjectViewModel.NewFolderItem);

    /// <summary>
    /// Ask what it is called, then create it.
    /// </summary>
    /// <remarks>
    /// <b>B65.</b> Every one of these wrote to disk immediately under
    /// <c>Character 3</c> or <c>Scene 2</c>, so a project filled with numbered
    /// items and the only correction was a file manager — B64 says the docker
    /// cannot rename. The prompt comes first and cancelling creates nothing,
    /// which is the ordering B66 and B78 also landed on: a name is a question,
    /// not something to fix afterwards.
    /// </remarks>
    private Task CreateProjectItemAsync(ProjectViewModel.NewItemKind kind)
    {
        // B65. The sequence moved into the docker so the cancel path is
        // testable; what stays here is the dialog, which is all a window should
        // own. Attached once rather than per call — it is the same dialog every
        // time and re-assigning it on each click would be a subscription leak
        // waiting to be written.
        _vm.ProjectDocker.AskName ??= (k, suggested) =>
            PromptForText($"New {k.Label.ToLowerInvariant()}", "Name", suggested);
        return _vm.ProjectDocker.CreateAsync(kind);
    }

    private void OnProjectOpen(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.OpenSelectedRowCommand.Execute(null);

    private void OnProjectOpenExternally(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.OpenSelectedExternallyCommand.Execute(null);

    private void OnProjectReveal(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.RevealSelectedCommand.Execute(null);

    private void OnProjectDuplicate(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.DuplicateSelectedCommand.Execute(null);

    /// <summary>
    /// The docker row's road to the same history window the File menu opens —
    /// a version is worth looking at without opening the document first.
    /// Folders no-op: a folder is not a file and has no history of its own.
    /// </summary>
    private async void OnProjectRowHistory(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Project is null || _vm.ProjectDocker.Selected is not { } row) return;
        var history = row switch
        {
            { Animation: { } d } => _vm.HistoryFor(d.Id, d.Path, d.Name),
            { Sheet: { } s } => _vm.HistoryFor(s.Id, s.Path, s.Name),
            _ => null,
        };
        if (history is null) return;
        await new VersionHistoryWindow(history).ShowDialog(this);
    }

    /// <summary>
    /// Export the selected folder: count it, confirm it, then write it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Q30's last mile.</b> <c>ExportPlan</c> could describe an artifact
    /// holding forty documents and <c>ProjectViewModel</c> could count and
    /// describe one, and no view called either — the whole of export scoping was
    /// reachable from tests and from nowhere an artist could press.
    /// </para>
    /// <para>
    /// <b>The count comes before the folder picker, and before anything is
    /// written.</b> "2 files from 47 documents, 3 held back by status" tells you
    /// whether you picked the right folder in a way <em>are you sure?</em>
    /// cannot — and the plan is computed without reading a drawing, so asking is
    /// cheap even when the answer is no.
    /// </para>
    /// <para>
    /// <b>One artifact failing does not stop the rest.</b> A plan of nine where
    /// the fourth cannot be written should produce eight files and a sentence
    /// naming the fourth, not four files and an exception.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Ask the model what the selected character is. The command holds every
    /// precondition and says which one is missing, so this is only the gesture.
    /// </summary>
    private void OnProjectReadSubject(object? sender, RoutedEventArgs e) =>
        _vm.AiReadSubjectCommand.Execute(null);

    /// <summary>Q38's free-entry half: type any character.</summary>
    /// <remarks>
    /// The prompt is supplied here and the decision lives on the view model, the
    /// same split B65 uses for the name box — a cancel path inside a window
    /// handler is a path no test can reach.
    /// </remarks>
    private async void OnProjectChooseGlyph(object? sender, RoutedEventArgs e)
    {
        _vm.ProjectDocker.AskGlyph ??= current =>
            PromptForText("Folder glyph", "Glyph", current);
        await _vm.ProjectDocker.ChooseIconAsync();
    }

    private async void OnProjectExportFolder(object? sender, RoutedEventArgs e)
    {
        var docker = _vm.ProjectDocker;
        if (docker.Project is null) return;

        var summary = docker.DescribeExportPlan();
        if (!await ConfirmAsync("Export", summary, "Export")) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export to folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } destination) return;

        var missing = new List<string>();
        var planned = docker.ResolveExport(destination, missing);
        if (planned.Count == 0)
        {
            _vm.AiStatus = missing.Count > 0
                ? $"Nothing exported — {string.Join(", ", missing)} could not be read."
                : "Nothing to export: every document was held back.";
            return;
        }

        var written = 0;
        var failed = new List<string>();
        foreach (var item in planned)
        {
            try
            {
                var run = await Task.Run(
                    () => Services.ExportRunner.Run(item.Documents, item.Preset, item.Path, item.Names));
                // No files means the runner refused — a target that cannot hold
                // several documents says so rather than throwing.
                if (run.Files.Count == 0)
                {
                    failed.Add($"{item.Name}: {run.Summary}");
                    continue;
                }
                written++;
                // Recorded only on success, so a failed artifact stays stale and
                // shows up in StaleExports rather than looking freshly built.
                docker.RecordExport(item.Artifact, item.Path);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Name}: {ex.Message}");
            }
        }

        var said = $"{written} file(s) written to {Path.GetFileName(destination)}";
        if (missing.Count > 0) said += $" — could not read {string.Join(", ", missing)}";
        if (failed.Count > 0) said += $" — {string.Join("; ", failed)}";
        _vm.AiStatus = said;
    }

    /// <summary>
    /// The selected drawing, exported somewhere it cannot break the build.
    /// </summary>
    /// <remarks>
    /// A test is a different destination rather than a smaller export: it writes
    /// to <c>test-exports/</c> beside the project, forces per-document grouping
    /// and drops the status filter — grouping is about the deliverable and a test
    /// is not one, and the filter exists to keep work in progress out of a
    /// shipped sheet, which is precisely what a test wants in. No confirmation,
    /// because nothing it can overwrite is a deliverable.
    /// </remarks>
    private async void OnProjectTestExport(object? sender, RoutedEventArgs e)
    {
        var docker = _vm.ProjectDocker;
        if (docker.PlanTestExport() is not var (reference, preset, path))
        {
            // Says so rather than doing nothing: a menu item that is sometimes
            // inert and never explains itself reads as the app being broken.
            _vm.AiStatus = "Select a drawing to test-export.";
            return;
        }
        if (docker.Project is not { } project) return;
        if (ProjectIo.LoadDocument(project, reference) is not { } doc)
        {
            _vm.AiStatus = $"“{reference.Name}” could not be read.";
            return;
        }

        try
        {
            var run = await Task.Run(() => Services.ExportRunner.Run(doc, preset, path));
            _vm.AiStatus = $"Test export: {run.Summary}";
        }
        catch (Exception ex)
        {
            _vm.AiStatus = $"Test export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Hand the row to the context menu's items, because nothing else will.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A <c>MenuFlyout</c>'s items have no DataContext.</b> Not the window's,
    /// not the target's — measured, not assumed: the host DockPanel's is the
    /// <see cref="ProjectRow"/> and the MenuItem's is null. So every binding in
    /// that menu resolves against nothing, which is fine for a
    /// <c>Click</c> handler and fatal for an <c>ItemsSource</c>.
    /// </para>
    /// <para>
    /// That is not a hypothetical. <em>Share a palette here</em> and <em>Export
    /// this as</em> shipped with <c>$parent[Window]</c> bindings three lines
    /// below a comment warning that they do not resolve here, and both submenus
    /// have been <b>empty since the day they landed</b> — the view models were
    /// correct, the tests exercised the view models, and no artist could reach
    /// either one. Found by asserting the binding resolved rather than that the
    /// command worked.
    /// </para>
    /// <para>
    /// Set on the top-level items only. Children generated from an
    /// <c>ItemsSource</c> take the list entry as their own DataContext, which is
    /// what their <c>$parent[MenuItem]</c> bindings hop over to get back here.
    /// </para>
    /// </remarks>
    private void OnProjectRowMenuOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout) return;
        var row = (flyout.Target as Control)?.DataContext;
        foreach (var item in flyout.Items.OfType<MenuItem>()) item.DataContext = row;
    }

    private void OnProjectRemove(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.RemoveSelectedCommand.Execute(null);

    // Q30. Two entries rather than one with a held modifier: subtree and
    // project-wide are different reaches with different blast radii, and
    // telling them apart by a key is how somebody publishes by accident.
    private void OnShareAsReferenceHere(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.ShareSelectedAsReference(projectWide: false);

    private void OnShareAsReferenceEverywhere(object? sender, RoutedEventArgs e) =>
        _vm.ProjectDocker.ShareSelectedAsReference(projectWide: true);

    /// <summary>
    /// Delete for real, asking first when there is something inside.
    /// </summary>
    /// <remarks>
    /// <b>B87.</b> The docker decides <em>whether</em> to ask and what the
    /// question says; this only puts it on screen. A view model that opened its
    /// own dialogs would be one no test could drive, which is the same split
    /// B65 uses for the name prompt.
    /// </remarks>
    private async void OnProjectDeletePermanently(object? sender, RoutedEventArgs e)
    {
        var docker = _vm.ProjectDocker;
        if (docker.DeleteNeedsConfirmation
            && !await ConfirmAsync("Delete permanently", docker.DeleteWarning, "Delete"))
        {
            return;
        }
        docker.DeleteSelectedPermanentlyCommand.Execute(null);
    }

    /// <summary>A yes/no the artist has to mean, with the destructive verb spelled out.</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        var yes = false;
        var confirm = new Button { Content = confirmLabel, IsDefault = false };
        var cancel = new Button { Content = "Cancel", IsDefault = true, IsCancel = true };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = message, MaxWidth = 360, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancel, confirm },
                    },
                },
            },
        };
        // Cancel is the default and Delete is not, which is the opposite of the
        // save dialog B75 landed: there, Enter should reach the outcome that
        // cannot destroy anything, and here that is Cancel.
        confirm.Click += (_, _) => { yes = true; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return yes;
    }

    private void OnProjectRowRename(object? sender, RoutedEventArgs e)
    {
        // The root row commits now too — it renames the project, folder
        // included (owner's call, 2026-08-13, superseding the B62 refusal).
        if (_vm.ProjectDocker.Selected is not { } row || row.IsSheet) return;
        row.IsRenaming = true;
    }

    /// <summary>
    /// Double-click on the name starts the rename — the layer panel's idiom,
    /// applied to the row the pointer is on.
    /// </summary>
    /// <remarks>
    /// On the name text only, and handled so the click stops there: the row's
    /// own double-click opens the document, and one gesture must not mean both
    /// depending on nothing the artist can see.
    /// </remarks>
    private void OnProjectNameDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ViewModels.ProjectRow row }) return;
        if (row.IsSheet) return;   // sheets rename in their own panel
        // The root row renames the project — folder on disk included (owner's
        // call, 2026-08-13, superseding the B62-era refusal).
        _vm.ProjectDocker.SelectFromPointer(row);
        row.IsRenaming = true;
        e.Handled = true;
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
    private async void OnProjectStatusSet(object? sender, RoutedEventArgs e)
    {
        if (_vm.ProjectDocker.Selected is not { } row) return;
        if ((sender as Control)?.Tag as string is not { } tag) return;
        if (!Enum.TryParse<Lightbox.Core.Projects.AssetStatus>(tag, out var status)) return;

        // A status is a message to a designer: "this drawing is finished, go and use it".
        // It is worth nothing if the drawing was never written down, and Ready in
        // particular fires an auto-export of a file that is not there. So the status does
        // not change at all until there is one — no half state to explain afterwards.
        if (!await EnsureSavedAsync(
                $"marking this {Lightbox.Core.Projects.AssetStatuses.Label(status)}",
                "Revert status change",
                _vm.SaveFactsFor(row)))
        {
            _vm.AiStatus = "Status unchanged — the drawing has not been saved.";
            return;
        }

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
                // B64. The box stays open when the rename was refused, so the
                // artist can fix the name rather than retype it from scratch —
                // and the docker's status line says which of the several
                // reasons it was.
                if (_vm.ProjectDocker.Rename(row, box.Text ?? ""))
                {
                    row.IsRenaming = false;
                    RememberRenamedProject(row);
                }
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
        // Losing focus commits what it can and always closes the box: leaving
        // an edit open on a row nobody is looking at is how a rename gets
        // applied to whatever is clicked next.
        if (row.IsRenaming)
        {
            if (_vm.ProjectDocker.Rename(row, box.Text ?? "")) RememberRenamedProject(row);
            else box.Text = row.Name;
        }
        row.IsRenaming = false;
    }

    /// <summary>
    /// A renamed project's recents entry points at a folder that no longer
    /// exists; re-remembering the new root keeps File ▸ Recent honest.
    /// </summary>
    private void RememberRenamedProject(ViewModels.ProjectRow row)
    {
        if (row.IsRoot && _vm.ProjectDocker.Project?.Root is { Length: > 0 } root)
        {
            _vm.Remember(root, RecentKind.Project);
        }
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
        _vm.ProjectDocker.Move(row, target.Folder);
    }

    /// <summary>
    /// Where a drop would land: the folder under the pointer, or the project
    /// itself when the pointer is over a loose document or past the end of the
    /// list. Null when the drop would change nothing.
    /// </summary>
    /// <remarks>
    /// <b>B114.</b> Two axes collapsed into one. This used to read
    /// <c>over?.Character</c> and compare characters and folders separately,
    /// with a comment (B94) about a third axis slipping past — there is one axis
    /// now, so there is nothing to keep in step.
    /// <para>
    /// Dropping into the empty space below the tree means the project, and so
    /// does dropping onto the project row: neither has a folder.
    /// </para>
    /// </remarks>
    private (ProjectFolder? Folder, bool Valid)? DropTargetFor(DragEventArgs e)
    {
        if (_draggedRow is null) return null;
        var over = (e.Source as Control)?.DataContext as ProjectRow;
        // A document row means the folder it is in; a folder row means itself.
        return (over?.Folder, true);
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
        var clip = _vm.ResolvedAudioPathForExport() is not null ? _vm.AudioClipNow : null;
        var written = await Task.Run(() =>
        {
            var files = Services.SequenceExporter.ExportPngSequence(_vm.Doc, dir);
            // The scratch track rides along as plain PCM, the one encoding
            // every comp package reads (Q56).
            if (clip is not null)
            {
                Services.VideoExporter.WriteWavPcm16(clip, Path.Combine(dir, "audio.wav"));
            }
            return files;
        });
        _vm.AiStatus = clip is null
            ? $"Exported {written.Count} PNG frame(s)."
            : $"Exported {written.Count} PNG frame(s) and audio.wav.";
    }

    /// <summary>
    /// <c>File ▸ Export video…</c> — the settings window owns the whole
    /// render (B146). It used to be a save picker whose every answer, the
    /// missing encoder included, went to the AI bar; that row is hidden
    /// whenever assistance is off, so the export looked like it did nothing.
    /// </summary>
    private async void OnExportVideoClicked(object? sender, RoutedEventArgs e)
    {
        var dialog = new VideoExportWindow(
            _vm.Doc,
            () => _vm.ResolvedAudioPathForExport(),
            _vm.ActiveTab?.Title ?? "animation");
        await dialog.ShowDialog(this);
        // Echoed into the status strip as well, for the artist who has closed
        // the window and wants to know what happened.
        if (dialog.Reported is { Length: > 0 } said) _vm.AiStatus = said;
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
        // Asked before the settings dialog rather than after: finding out that the drawing
        // was never saved is worse after picking a preset and a filename than before.
        if (!await EnsureSavedAsync("exporting", "Don't export")) return;

        var dialog = new ExportWindow();
        await dialog.ShowDialog(this);
        if (dialog.Chosen is not { } preset) return;

        string path;
        if (preset.Target == Lightbox.Core.Projects.ExportTarget.PngSequence)
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

    /// <summary>
    /// Add an anchor in the middle of the canvas.
    /// </summary>
    /// <remarks>
    /// <b>B58.</b> The centre rather than the pointer, because a menu item has no
    /// pointer position to speak of — it is the place you can always find, and the
    /// mark is draggable the instant it exists. Placing by click is the canvas's
    /// job and arrives with <c>RigEmptyPressed</c>.
    /// </remarks>
    private void OnAddRigAnchor(object? sender, RoutedEventArgs e)
    {
        var (x, y) = (_vm.Doc.Scene.Width / 2.0, _vm.Doc.Scene.Height / 2.0);
        _vm.SelectedRigMarkId = _vm.AddAnchorAt($"anchor {(_vm.Doc.Scene.Anchors?.Count ?? 0) + 1}", x, y);
        RefreshRigOverlay();
    }

    private void OnAddRigShape(object? sender, RoutedEventArgs e)
    {
        // A quarter of the canvas, centred: big enough to see and grab, small
        // enough not to be mistaken for the frame.
        var w = _vm.Doc.Scene.Width / 4.0;
        var h = _vm.Doc.Scene.Height / 4.0;
        _vm.SelectedRigMarkId = _vm.AddShapeAt(
            $"shape {(_vm.Doc.Scene.Shapes?.Count ?? 0) + 1}",
            (_vm.Doc.Scene.Width - w) / 2, (_vm.Doc.Scene.Height - h) / 2, w, h);
        RefreshRigOverlay();
    }

    private void OnDeleteRigMark(object? sender, RoutedEventArgs e)
    {
        _vm.DeleteSelectedRigMark();
        RefreshRigOverlay();
    }

    /// <summary>
    /// Hand the canvas the marks to draw, or nothing at all.
    /// </summary>
    /// <remarks>
    /// <b>B58.</b> One line of plumbing, and its absence is what made a whole
    /// feature — thirty tests, a hit-tester, a drag solver and an editor path —
    /// unreachable. `RigMarks` already returns an empty list when the mode is off,
    /// so null and absent are the same thing here and neither needs a mode check.
    /// </remarks>
    private void RefreshRigOverlay()
    {
        Canvas.RigEditMode = _vm.RigEditMode;
        var marks = _vm.RigMarks;
        Canvas.RigMarks = marks.Count > 0 ? marks : null;
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

        Canvas.GuidesMovedStarted += _vm.BeginGuidesMove;
        Canvas.GuidesMoved += (dx, dy) => _vm.UpdateGuidesMove(dx, dy);
        Canvas.GuidesMovedEnded += _vm.EndGuidesMove;

        Canvas.RefBoxesMoveStarted += _vm.BeginRefBoxesMove;
        Canvas.RefBoxesMoved += (dx, dy) => _vm.UpdateRefBoxesMove(dx, dy);
        Canvas.RefBoxesMovedEnded += _vm.EndRefBoxesMove;

        Canvas.AnchorsMoveStarted += _vm.BeginAnchorsMove;
        Canvas.AnchorsMoved += (dx, dy) => _vm.UpdateAnchorsMove(dx, dy);
        Canvas.AnchorsMovedEnded += _vm.EndAnchorsMove;

        Canvas.ShapesMoveStarted += _vm.BeginShapesMove;
        Canvas.ShapesMoved += (dx, dy) => _vm.UpdateShapesMove(dx, dy);
        Canvas.ShapesMovedEnded += _vm.EndShapesMove;

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
    /// <summary>Say that the previous run ended unexpectedly, and where to look.</summary>
    /// <remarks>
    /// The status strip rather than a dialog: by now the artist has started
    /// working again, and a modal about something that happened before they
    /// opened the app interrupts the wrong moment. The crash-time dialog is
    /// where the interrupting is meant to happen; this is the record for when
    /// that could not be shown.
    /// </remarks>
    public void NotePreviousCrash(string logPath) =>
        _vm.AiStatus = $"Lightbox closed unexpectedly last time — details in {logPath}";

    public async Task OfferStartScreenAsync()
    {
        if (!_vm.Settings.ShowStartScreen) return;
        await AskWhatToOpenAsync();
    }

    /// <summary>Show the start screen and act on the answer, gate or no gate.</summary>
    private async Task AskWhatToOpenAsync()
    {
        var screen = new StartScreen(_vm.Settings.Recent);
        await screen.ShowDialog(this);
        await ApplyStartChoiceAsync(screen.Answer);
    }

    /// <summary>
    /// The last tab closed, so ask what to open next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Closing the last document used to conjure a replacement, which is the
    /// same complaint as escaping the start screen: a canvas nobody chose,
    /// arrived at by not answering. Asking is the honest version — and it is
    /// asked <em>after</em> the tab is gone, so the answer can be "nothing" and
    /// leave the application empty.
    /// </para>
    /// <para>
    /// Posted rather than shown inline. <c>CloseTab</c> is still unwinding when
    /// this fires — the tab strip and the canvas have not caught up — and a
    /// modal opened from inside that paints over a window still describing a
    /// document that no longer exists.
    /// </para>
    /// <para>
    /// It ignores <c>ShowStartScreen</c>, which gates the startup offer only.
    /// That preference means "do not interrupt me on the way in"; this is a
    /// question about something the artist just did.
    /// </para>
    /// </remarks>
    private void OnLastDocumentClosed() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(
            async () =>
            {
                // Something may have been opened in the gap — a recent, a
                // project row — and asking then would be asking about a
                // question that has already been answered.
                if (_vm.HasDocument) return;
                await AskWhatToOpenAsync();
            },
            Avalonia.Threading.DispatcherPriority.Background);

    /// <summary>Act on what the start screen was answered with.</summary>
    /// <remarks>
    /// The screen used to carry a "Don't show this again" checkbox, from the
    /// days when it sat over an already-open blank document and skipping it
    /// cost nothing. The application now starts empty, so the screen *is* the
    /// question — opting out of it here would opt into staring at an empty
    /// workspace on every launch. The preference survives as
    /// <c>ShowStartScreen</c> under Edit, where turning it off is a deliberate
    /// choice rather than a checkbox ticked on the way past.
    /// </remarks>
    public async Task ApplyStartChoiceAsync(StartChoice choice)
    {
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

    /// <summary>Show a dialog asking how to place a multi-frame symbol.</summary>
    public async Task<PlacementChoice?> ShowPlacementChoiceDialogAsync(Symbol symbol)
    {
        var dialog = new PlacementChoiceDialog();
        return await dialog.ShowDialog<PlacementChoice?>(this);
    }
}
