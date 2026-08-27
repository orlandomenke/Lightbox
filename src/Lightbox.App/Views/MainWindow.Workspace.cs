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

/// <summary>Part of the MainWindow code-behind — see MainWindow.axaml.cs.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c> under Q76, which was 5,706 lines across 37
/// sections with 79% of its fields touched by exactly one of them. Every field this
/// file uses is either declared here or in the shared block at the top of
/// <c>MainWindow.axaml.cs</c>. See <c>docs/DESIGN-mainviewmodel-decomposition.md</c>.
/// </remarks>
public partial class MainWindow
{
    // ---- the workspace -------------------------------------------------------



    /// <summary>
    /// Tell the publisher how to pace itself to this canvas.
    /// </summary>
    /// <remarks>
    /// <b>B189</b> gave it the signal: the publisher holds its coalesced
    /// publishes until the canvas has actually drawn the last one, and
    /// <c>SnapshotPresented</c> is how it learns that. <b>B321</b> gave it the
    /// same truth without the dispatcher hop, for when the dam is deciding
    /// mid-stroke and the message has not had a turn yet.
    /// </remarks>
    private void WireCanvasPacing()
    {
        Canvas.SnapshotPresented += seq => _vm.NoteFramePresented(seq);
        _vm.SetRenderedSeqProbe(() => Canvas.LastRenderedSeq);
        _vm.SetRenderedAtProbe(() => Canvas.LastRenderedAtTicks);
    }

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
            // The report mostly describes playback, so when a playback quality
            // is set the string has to say which quality the run actually used.
            _vm.PlaybackQuality is { } playbackQuality
                ? $"{_vm.CanvasQuality} while drawing, {playbackQuality} in playback"
                : _vm.CanvasQuality.ToString(),
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
            (_vm.LivePostPasses, _vm.LivePostTotalMs, _vm.LivePostWorstMs,
             _vm.LivePostWaits, _vm.LivePostWaitTotalMs, _vm.LivePostWaitWorstMs,
             _vm.LivePostPixels, _vm.LivePostMarkPixels,
             _vm.LivePostWorstMsWidth, _vm.LivePostWorstMsHeight,
             _vm.LivePostWorstMsMarkPixels, _vm.LiveWorstProvisionalTail),
            (_vm.LiveTipDrawn, _vm.LiveTipTooFarBehind, _vm.LiveTipNoPass,
             _vm.LiveTipOutstanding.MedianMs, _vm.LiveTipOutstanding.WorstMs,
             _vm.LiveTipOutstanding.PercentileMs(0.9), _vm.LiveTipOutstanding.PercentileMs(0.99),
             _vm.LiveTipStampMs.MedianMs, _vm.LiveTipStampMs.WorstMs,
             _vm.LiveTipNewDabs.MedianMs, _vm.LiveTipNewDabs.PercentileMs(0.9),
             _vm.LiveTipNewDabs.WorstMs),
            (_vm.DamDeferrals, _vm.DamReleasedByPresent, _vm.DamReleasedByTimer,
             _vm.DamReleasedByPresent + _vm.DamReleasedByTimer,
             _vm.DamHeldTotalMs, _vm.DamHeldWorstMs,
             _vm.DamLateTotalMs, _vm.DamLateWorstMs, _vm.DamReleasedByEvent),
            (_vm.CycleTally.MedianMs, _vm.CycleTally.MeanMs, _vm.CycleTally.Count,
             _vm.ReleaseToPublishTally.MedianMs, _vm.ReleaseToPublishTally.MeanMs,
             _vm.EventIntervalTally.MedianMs, _vm.EventIntervalTally.Count),
            (_vm.ComposeCount, _vm.ComposeTotalMs, _vm.ComposeWorstMs,
             _vm.ComposeMedianMs, _vm.ComposeMeanIsDistorted),
            (_vm.BuildDescribeMs, _vm.BuildComposeMs, _vm.BuildHandoffMs),
            _vm.ReportPublishTally);
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
    /// <summary>
    /// Start or stop keeping the last published frames and the buffers behind
    /// them.
    /// </summary>
    private void OnToggleFrameCapture(object? sender, RoutedEventArgs e)
    {
        var on = sender is MenuItem { IsChecked: true };
        _vm.Capture.Arm(on);
        _vm.AiStatus = on
            ? "Recording frames. Draw until the problem shows, then press F10."
            : "Stopped recording frames. F10 still writes out what was recorded.";
    }

    /// <summary>
    /// Write the recorded frames out. Bound to F10 as well as to the menu, for
    /// the reason <c>ShortcutMap</c> gives: the artifact is mid-stroke and a
    /// trip to a menu is a trip away from it.
    /// </summary>
    private void OnWriteFrameCapture(object? sender, RoutedEventArgs e) => WriteFrameCapture();

    /// <inheritdoc cref="OnWriteFrameCapture"/>
    internal void WriteFrameCapture()
    {
        if (!_vm.Capture.Armed && _vm.Capture.Recorded == 0)
        {
            _vm.AiStatus = "Nothing recorded — turn on Help ▸ Record frames while drawing first";
            return;
        }
        var path = _vm.Capture.Write(Services.DiagnosticLog.Directory);
        _vm.AiStatus = path is null
            ? "Could not write the frame capture"
            : $"{_vm.Capture.Recorded} frames recorded, the last few written to {path}";
    }

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

    /// <summary>
    /// Start recording what the pointer delivers, or stop and write the report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One command for both halves</b>, because the ritual is one thing an
    /// artist does: arm, hover for a minute doing the thing that misbehaves,
    /// disarm. A pair of menu items would put a decision ("which one do I want
    /// now?") in front of a person who is trying to reproduce a flicker.
    /// </para>
    /// <para>
    /// The status line carries the outcome the way the render report's does. The
    /// live half is <see cref="Services.InputTrace.Armed"/>, which the canvas's
    /// own pen readout folds in — that readout sits where somebody chasing a pen
    /// problem is already looking, and it is the one surface a modal Configure
    /// window could not have been.
    /// </para>
    /// </remarks>
    private void ToggleInputTrace()
    {
        if (!Services.InputTrace.Armed)
        {
            Services.InputTrace.Arm();
            Title = RecordingTitle;
            Announce("Recording pen input — hover, draw and open a flyout, then press the key again to write the report.");
            return;
        }

        var path = Services.InputTrace.WriteReport();
        Title = IdleTitle;
        Announce(path is null
            ? "Could not write the input trace"
            : $"Input trace written to {path}");
    }

    private const string IdleTitle = "Lightbox";

    private const string RecordingTitle = "● Lightbox — recording an input trace (press the key again to stop)";

    /// <summary>
    /// Say something about the trace everywhere it could possibly be read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written because the first version said it into a hidden bar.</b> The
    /// status line this used alone lives in the AI strip, which is
    /// <c>IsVisible="{Binding AiEnabled}"</c> — so on a machine with AI
    /// assistance switched off, pressing the key produced no visible change
    /// whatsoever and the feature looked dead. Reported as exactly that, by the
    /// one person who has the tablet this exists for.
    /// </para>
    /// <para>
    /// The <b>window title</b> is the fix that cannot be hidden: nothing else
    /// writes it, every window manager shows it, and it stays legible for the
    /// whole minute the trace is running rather than only at the moment of the
    /// press. The other two are kept because they are where somebody already
    /// looking at a pen problem is looking — the pen readout especially, which
    /// is otherwise only refreshed by a pointer event and so would say nothing
    /// at all until the artist moved the pen.
    /// </para>
    /// </remarks>
    private void Announce(string message)
    {
        _vm.AiStatus = message;
        _vm.PenDiagnostic = message;
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
            // Onto a header: tab into that slot, at the position aimed at —
            // which for a tab dropped back in its own header is the whole of
            // the operation, so the group named can be the panel itself.
            if (drop.IntoGroupOf is { } host) _vm.Workspace.JoinGroup(panel.PanelId, host, drop.TabIndex);
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
                panel.HeaderHeight,
                // The same measure-don't-assume, one level down: where each tab
                // is, so a drop can name a position in the strip rather than
                // only the group. Lifted from the docker's coordinates into the
                // window's, which is the space every other rectangle here is in.
                [.. panel.TabRects().Select(t => t with { Bounds = t.Bounds.Offset(origin.X, origin.Y) })]));
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
        // A layout restored at construction — the session, or a workspace
        // saved with a panel torn off — reaches here before this window is
        // visible, and a floating window cannot be shown with a non-visible
        // owner (B289: the app died on the restart after tearing a panel
        // off). Defer the whole re-apply to Opened, when Show(this) is legal;
        // the panel waits in the pool until then.
        if (!IsVisible)
        {
            if (!_floatingDeferred)
            {
                _floatingDeferred = true;
                Opened += (_, _) => ApplyDockLayout();
            }
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
        // A torn-off panel keeps the keyboard it had while docked — see
        // FloatingPanelWindow.Scope for what tearing one off used to cost.
        window.KeyDown += OnKeyDown;
        window.AddHandler(
            KeyUpEvent, OnKeyUpEdge, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        _floating[id] = window;
        window.Show(this);
    }

    /// <summary>The panels currently in windows of their own.</summary>
    internal IReadOnlyCollection<FloatingPanelWindow> FloatingWindowsForTests => _floating.Values;

    /// <summary>An Opened re-apply is already queued for a deferred float.</summary>
    private bool _floatingDeferred;

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
    /// <b>The panel is a decision remembered from the last move, confirmed by
    /// the live flags only while the pointer is known present.</b> This took
    /// three implementations to get right, and the two dead ones each name a
    /// state the next change must keep surviving. Caching the <i>element</i>
    /// under the pointer died the moment a docker rebuilt its rows — a detached
    /// visual's parent chain reaches no docker. Asking
    /// <see cref="InputElement.IsPointerOver"/> instead died the moment a pen
    /// left proximity, which it does a centimetre off the tablet, between
    /// pointing at a panel and pressing the key with the other hand — the leave
    /// clears every live flag, and the same event has also been observed
    /// leaving a docker's flag stale-<i>true</i> on X11, so the flag is wrong in
    /// both directions once the pointer is gone. What survives both is an
    /// <i>id</i> resolved at move time, while the element was attached: the next
    /// move overwrites it, and <c>PointerExited</c> deliberately does not — so
    /// lifting the pen over the timeline keeps the timeline, and moving to the
    /// canvas releases it. <see cref="_pointerInWindow"/> gates the live check
    /// for the same reason: <c>IsPointerOver</c> is only evidence while the
    /// pointer is known to be here.
    /// </para>
    /// <para>
    /// <b>A displayed docker's scope is its own id, not its <c>ActiveTab</c>.</b>
    /// Both are the same value whenever the bookkeeping is right — a tab group's
    /// visible control <i>is</i> <c>_panels[active]</c>, because panels live in a
    /// pool and the layout only names them — so this is not a behaviour change
    /// for a healthy layout. It is a change in which of the two is trusted:
    /// <see cref="Controls.Docker.PanelId"/> is the identity of the control the
    /// pointer is physically inside, while <c>ActiveTab</c> is derived state a
    /// strip rebuild writes to. Reading the fact rather than the bookkeeping
    /// costs nothing and removes a way for the scope to be quietly wrong.
    /// </para>
    /// <para>
    /// <b>A key from a torn-off panel is answered by that panel, and the
    /// pointer does not get a say.</b> The hover state below only ever
    /// describes <i>this</i> window — so a floating panel asking it would be
    /// handed wherever the main window's pointer happened to be resting, which
    /// is a different room. The window the key arrived in is the one piece of
    /// evidence that is certainly about the key.
    /// </para>
    /// </remarks>
    /// <param name="from">
    /// The window the key press arrived in — the <c>sender</c> of the key
    /// handler, which is this window for a docked panel and the floating window
    /// for a torn-off one.
    /// </param>
    private Services.ShortcutScope CurrentShortcutScope(object? from = null)
    {
        if (from is FloatingPanelWindow floating) return floating.Scope;
        if (_pointerInWindow && DockerUnderPointer() is { } over)
        {
            return Services.ShortcutScope.In(over.PanelId);
        }
        if (_hoveredPanel is { } remembered) return Services.ShortcutScope.In(remembered);
        if (PanelUnder(FocusManager?.GetFocusedElement() as Visual) is { } focused)
        {
            return Services.ShortcutScope.In(focused);
        }
        return Services.ShortcutScope.Canvas;
    }

    /// <summary>
    /// The docked panel the pointer is inside, asked of the panels themselves.
    /// </summary>
    /// <remarks>
    /// Only ever one: dockers do not overlap, so the first match is the answer.
    /// Parked panels are skipped by the visibility test rather than by knowing
    /// which ones the layout is showing — a pooled docker is in the tree at zero
    /// size and could otherwise claim a pointer that is nowhere near it.
    /// </remarks>
    private Controls.Docker? DockerUnderPointer()
    {
        foreach (var docker in _panels.Values)
        {
            if (docker.IsPointerOver && docker.IsEffectivelyVisible && docker.Bounds.Width > 0)
            {
                return docker;
            }
        }
        return null;
    }

    /// <summary>
    /// The panel of the docker containing this element, if any. Called at
    /// pointer-move time, while the element is guaranteed attached — the id it
    /// returns is what gets remembered, never the element itself.
    /// </summary>
    private static Docking.DockPanelId? PanelUnder(Visual? from)
    {
        for (var v = from; v is not null; v = v.GetVisualParent())
        {
            if (v is Controls.Docker docker) return docker.PanelId;
        }
        return null;
    }

    /// <summary>
    /// The panel the pointer last moved over, or null when that was the canvas
    /// or the chrome. Deliberately survives <c>PointerExited</c>: a pen leaving
    /// proximity is the pointer going away, not the artist pointing somewhere
    /// else, and the key they press next belongs to the place they pointed.
    /// </summary>
    private Docking.DockPanelId? _hoveredPanel;

    /// <summary>
    /// Whether the pointer is known to be in this window — the gate on trusting
    /// the live <c>IsPointerOver</c> flags, which go stale in both directions
    /// once it is not.
    /// </summary>
    private bool _pointerInWindow;

    /// <summary>
    /// The scope a key press would resolve in, for tests.
    /// </summary>
    /// <remarks>
    /// Asserting on the scope directly rather than on what a key happened to do:
    /// inferring it from a side effect is how a probe reports "the shortcut is
    /// dead" when the truth is that inserting a key on a frame that already has
    /// one changes nothing.
    /// </remarks>
    internal Services.ShortcutScope ShortcutScopeForTests => CurrentShortcutScope();

    /// <summary>
    /// Put the resolver in the state a pen leaving proximity produces: the live
    /// pointer flags no longer trusted, the remembered panel intact.
    /// </summary>
    /// <remarks>
    /// The state cannot be reached by moving a headless mouse — a move to
    /// "outside" is still a move, and it would overwrite the memory this exists
    /// to test. Only the platform can take the pointer away without moving it,
    /// and the headless platform never does.
    /// </remarks>
    internal void SimulateProximityLossForTests() => _pointerInWindow = false;

    /// <summary>
    /// Clicking anywhere on a layer-docker row makes that layer active. Ctrl
    /// adds it to the selection (or drops it), Shift takes the run from the
    /// last row picked.
    /// </summary>
    /// <remarks>
    /// Ctrl+click on the row's <em>thumbnail</em> means something else entirely
    /// — select the layer's opaque pixels — and that handler marks the event
    /// handled, so the two never both run.
    /// </remarks>
    private void OnLayerRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LayerRow row) return;
        if (Services.LayerLinkGestures.From(
                e.KeyModifiers, e.GetCurrentPoint(this).Properties.IsRightButtonPressed) is { } gesture)
        {
            // The layer has to be the active one first: every link operation
            // is written in terms of it, and a right-click that linked the
            // PREVIOUSLY active layer would be the worst kind of wrong — it
            // does something, just not to the row under the pointer.
            _vm.SelectLayer(row, toggle: false, range: false);
            ApplyLayerLinkGesture(gesture);
            // Handled, so the row's own context menu does not open on top of
            // a gesture that has already done what it was asked.
            e.Handled = true;
            return;
        }
        _vm.SelectLayer(
            row,
            toggle: e.KeyModifiers.HasFlag(KeyModifiers.Control),
            range: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        // Pull keyboard focus off menus/sliders so the arrow-key layer walk
        // (and Delete/Backspace) reaches the window's shortcut handler.
        (sender as Control)?.Focus();

        // A plain left press may become a drag; a modified press is a selection
        // gesture and never one. The threshold decides which in OnLayerRowMoved.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.KeyModifiers is KeyModifiers.None)
        {
            _layerDragCandidate = row;
            _layerDragFrom = e.GetCurrentPoint(this).Position;
            _layerDragPress = e;
        }
    }

    // ---- drag a layer row up or down the stack -------------------------------

    private static readonly DataFormat<LayerRow> LayerRowDragFormat =
        DataFormat.CreateInProcessFormat<LayerRow>("lightbox-layer-row");

    private LayerRow? _layerDragCandidate;
    private Point _layerDragFrom;
    private PointerPressedEventArgs? _layerDragPress;

    private async void OnLayerRowMoved(object? sender, PointerEventArgs e)
    {
        if (_layerDragPress is not { } press || _layerDragCandidate is not { } row) return;
        if ((sender as Control)?.DataContext is not LayerRow over || !ReferenceEquals(over, row)) return;
        var point = e.GetCurrentPoint(this);
        // A move with the button up means the release happened somewhere this
        // handler never saw; disarm rather than wait (the cel drag's lesson).
        if (!point.Properties.IsLeftButtonPressed)
        {
            _layerDragCandidate = null;
            _layerDragPress = null;
            return;
        }
        var delta = point.Position - _layerDragFrom;
        if (Math.Abs(delta.X) < Input.CelDragGesture.ThresholdPx
            && Math.Abs(delta.Y) < Input.CelDragGesture.ThresholdPx) return;

        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(LayerRowDragFormat, row));
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Rendering.CanvasControl.LogDiag("layer-row-drag", ex);
        }
        finally
        {
            _layerDragCandidate = null;
            _layerDragPress = null;
        }
    }

    private void OnLayerRowReleased(object? sender, PointerReleasedEventArgs e)
    {
        _layerDragCandidate = null;
        _layerDragPress = null;
    }

    private static LayerRow? DraggedLayerOf(DragEventArgs e) =>
        e.DataTransfer is { } transfer ? transfer.TryGetValue(LayerRowDragFormat) : null;

    /// <summary>
    /// The row or folder header under the drop pointer, with its visual so the
    /// drop can tell the upper half from the lower.
    /// </summary>
    private static (object? Item, Control? Container) LayerDropTargetOf(DragEventArgs e)
    {
        var control = e.Source as Control;
        while (control is not null && control.DataContext is not (LayerRow or GroupRow))
        {
            control = control.Parent as Control;
        }
        return (control?.DataContext, control);
    }

    private void OnLayerDragOver(object? sender, DragEventArgs e)
    {
        var (item, _) = LayerDropTargetOf(e);
        e.DragEffects = DraggedLayerOf(e) is not null && item is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnLayerDrop(object? sender, DragEventArgs e)
    {
        if (DraggedLayerOf(e) is not { } dragged) return;
        e.Handled = true;
        switch (LayerDropTargetOf(e))
        {
            case (LayerRow target, { } container) when !ReferenceEquals(target, dragged):
                var above = e.GetPosition(container).Y < container.Bounds.Height / 2;
                _vm.DropLayerOnRow(dragged, target, above);
                break;
            case (GroupRow header, _):
                _vm.MoveLayerIntoGroup(dragged.Layer, header.Group);
                break;
        }
    }

    /// <summary>
    /// Carry out a link gesture on the active layer, or show the menu.
    /// </summary>
    /// <remarks>
    /// The menu is built here rather than in XAML because its contents depend
    /// on state the row does not carry — whether there is a link to leave,
    /// what it already carries, and which bone the Bone tool has selected.
    /// </remarks>
    private void ApplyLayerLinkGesture(Services.LayerLinkGesture gesture)
    {
        switch (gesture)
        {
            case Services.LayerLinkGesture.LinkAbove:
                _vm.LinkLayerAboveCommand.Execute(null);
                break;
            case Services.LayerLinkGesture.LinkBelow:
                _vm.LinkLayerBelowCommand.Execute(null);
                break;
            case Services.LayerLinkGesture.Unlink:
                _vm.UnlinkLayerCommand.Execute(null);
                break;
        }
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
