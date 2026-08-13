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

/// <summary>Dockers, the panel layout, and the gestures that move them.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c>, which was 5,544 lines. See
/// <c>docs/DESIGN-mainviewmodel-decomposition.md</c> — the view is 79% single-section
/// state over one <c>_vm</c> field, so it needed splitting rather than decomposing.
/// </remarks>
public partial class MainWindow
{
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
            _vm.TileStoreBytes);
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
}
