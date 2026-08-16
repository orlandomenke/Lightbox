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
        // B223: the drag is a transform session, the same one Ctrl+T and the
        // Move tool open. The pixels follow the pointer through the session's
        // composite preview rather than arriving on release, and the whole drag
        // is one undo step — which is what MainViewModel.StrokeActions said it
        // wanted and could not have while the filter had no line source.
        // Q104: Ctrl on the artwork inside a marquee moves what is in it. It
        // rides the line drag's move/release channel below rather than growing
        // one of its own — the same events, the same commit, the same discard
        // of a press that went nowhere.
        Canvas.SetSelectionMoveEntry(_vm.BeginSelectionMove);
        Canvas.SelectedLinesMoveStarted += _vm.BeginLineMove;
        Canvas.SelectedLinesMoved += (x, y, axisLock) => _vm.UpdateMove(x, y, axisLock);
        Canvas.SelectedLinesMoveEnded += _vm.EndMove;
        Canvas.SelectedLinesMoveCancelled += _vm.CancelMove;
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
        // B217: the whole-line preview, for the two tools that take a whole
        // line. Same split as everything else here — the canvas reports where
        // the pointer is, the view model decides what is under it.
        Canvas.SetLineHover(_vm.HoverStrokeAt);
        _vm.HoveredLineChanged += Canvas.SetHoveredLine;
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
        // The marquee shapes are built in the canvas, so the snap has to reach
        // them as a function (B216). Same split as every other decision here:
        // the control has the geometry, the view model has the record.
        Canvas.SetPointSnapper(_vm.SnapPointForCanvas);
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
        // B189: the publisher paces its coalesced publishes to what the canvas
        // has actually drawn, and this is the signal it paces against.
        Canvas.SnapshotPresented += seq => _vm.NoteFramePresented(seq);
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

        WireTransformSession(); // window side lives in MainWindow.Transform.cs
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
        //
        // The panel is resolved HERE, at move time, and the id is what is kept
        // — never the element (B199; the reasoning lives on CurrentShortcutScope).
        // The next move overwrites it and PointerExited keeps it, so lifting a
        // pen keeps the panel and moving to the canvas releases it.
        AddHandler(
            PointerMovedEvent,
            (_, e) =>
            {
                var source = e.Source as Visual;
                _hoveredPanel = PanelUnder(source);
                _pointerInWindow = true;
                // Everything that is NOT the canvas. The canvas counts itself in
                // its own handler, because the question the two counters answer
                // is whether a frame reaching the screen depends on the canvas's
                // own invalidate — and the reported symptom is that moving over
                // a docker does not help. See InputPulse.
                if (!IsInsideCanvas(source)) Rendering.InputPulse.Elsewhere();
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // The pointer leaving the window — which includes a pen leaving
        // proximity — invalidates the LIVE flags, not the remembered panel.
        // IsPointerOver has been observed stale in both directions after this
        // event (an X11 run kept a docker's flag true with the pointer gone),
        // so the flag is only consulted while the pointer is known present.
        PointerExited += (_, _) => _pointerInWindow = false;

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

        WireOverlayGestures();
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

        Canvas.PointerHovered += (x, y, mods) => _vm.UpdatePointerContext(x, y, mods, Canvas.ViewScale);
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
        // The release edge, so a borrowed tool comes back — see OnKeyUpEdge for
        // why it tunnels and why it is shared with the floating panel windows.
        AddHandler(KeyUpEvent, OnKeyUpEdge, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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

    // ---- state shared across sections ---------------------------------------
    //
    // Every field below is used by more than one section, so it stays here rather than
    // travelling into one of the partials beside this file. _vm being everywhere is not
    // coupling to fix: a view holding one reference to its view model is a view.

    /// <summary>
    /// Every panel, once. They are created by the XAML into
    /// <c>PanelPool</c> and moved between strips from there; nothing here ever
    /// builds a panel, so a panel keeps its scroll position, its bindings and
    /// any half-typed value across a drag.
    /// </summary>
    private readonly Dictionary<DockPanelId, Docker> _panels = [];

    private readonly Dictionary<DockPanelId, FloatingPanelWindow> _floating = [];

    private readonly Input.CelDragGesture _celDrag = new();
    private PointerPressedEventArgs? _celDragPress;

    private readonly Services.ShortcutMap _shortcuts = new();
}
