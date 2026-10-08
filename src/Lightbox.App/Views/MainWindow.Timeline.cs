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
    // ---- the chosen layer, shown -------------------------------------------------

    /// <summary>
    /// Choosing a layer brings its row into view in the timeline and the
    /// X-sheet, wherever it was chosen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layer docker, the timeline and the sheet are three lists of the
    /// same layers, each scrolling on its own. On a document with more layers
    /// than fit, picking one in the docker used to leave the other two showing
    /// whatever they showed before.
    /// </para>
    /// <para>
    /// <b>Any change of the active layer, not only a click in the docker</b>:
    /// the keyboard's layer up and down and the canvas's own picking want the
    /// same thing, and a choice made in the timeline itself is a row already
    /// showing, for which the smallest scroll is none.
    /// </para>
    /// <para>
    /// <b>Posted rather than done on the spot</b>, because the change arrives
    /// before the rows it names have been laid out — a layer just added has no
    /// container yet — and coalesced, so a burst of changes is one scroll.
    /// </para>
    /// </remarks>
    private void WireLayerReveal()
    {
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ActiveLayerIndex)) QueueLayerReveal();
        };
        // The timeline and the sheet are tabs of one docker: whichever was
        // behind when the layer was chosen catches up when it comes forward.
        // "Comes forward" is asked two ways because a tab is hidden two ways —
        // its docker made invisible, or its content never laid out — and
        // either ends with one of these changing.
        foreach (var docker in new Control[] { XsheetDocker, TimelineDocker })
        {
            docker.PropertyChanged += (_, args) =>
            {
                if (args.Property == IsVisibleProperty && args.NewValue is true) QueueLayerReveal();
            };
        }
        foreach (var list in new Control[] { XsheetLayerList, TimelineTrackView })
        {
            list.PropertyChanged += (_, args) =>
            {
                if (args.Property == BoundsProperty
                    && args.OldValue is Rect { Height: <= 0 } && args.NewValue is Rect { Height: > 0 })
                {
                    QueueLayerReveal();
                }
            };
        }
    }

    private bool _layerRevealQueued;

    private void QueueLayerReveal()
    {
        if (_layerRevealQueued) return;
        _layerRevealQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                _layerRevealQueued = false;
                RevealActiveLayer();
            },
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void RevealActiveLayer()
    {
        var index = ActiveLayerRowIndex(_vm);
        if (index < 0) return;

        if (XsheetLayerList.ContainerFromIndex(index) is { } sheetRow)
        {
            RevealVertically(sheetRow, 0, sheetRow.Bounds.Height);
        }
        if (TimelineTrackView.Tracks is { } tracks)
        {
            var row = TimelineRowOfActiveLayer(_vm, tracks.Count);
            RevealVertically(TimelineTrackView, TrackView.RulerHeight + row * TrackView.RowPitch, TrackView.RowPitch);
        }
    }

    /// <summary>The active layer's place in <see cref="MainViewModel.LayerRows"/>, or -1.</summary>
    private static int ActiveLayerRowIndex(MainViewModel vm)
    {
        var layers = vm.Doc.Scene.Layers;
        if (vm.ActiveLayerIndex < 0 || vm.ActiveLayerIndex >= layers.Count) return -1;
        var active = layers[vm.ActiveLayerIndex];
        for (var i = 0; i < vm.LayerRows.Count; i++)
        {
            if (ReferenceEquals(vm.LayerRows[i].Layer, active)) return i;
        }
        return -1;
    }

    /// <summary>
    /// The timeline row the active layer is drawn on.
    /// </summary>
    /// <remarks>
    /// The timeline's first rows are not layers — the camera, then the
    /// armature and its bones — and the layers follow in the order of
    /// <see cref="MainViewModel.LayerRows"/>. So the rows that are not layers
    /// are whatever the track list holds beyond the layer rows, and they are
    /// counted rather than re-derived: asking the view model for its track
    /// list builds a fresh one every time.
    /// </remarks>
    internal static int TimelineRowOfActiveLayer(MainViewModel vm, int trackCount) =>
        ActiveLayerRowIndex(vm) is var index and >= 0
            ? Math.Max(0, trackCount - vm.LayerRows.Count) + index
            : -1;

    /// <summary>
    /// Scroll every list above <paramref name="control"/> just far enough to
    /// show the span from <paramref name="y"/> down <paramref name="height"/> of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every scroller, not the nearest.</b> The sheet has a scroller of its
    /// own inside the docker's, and the docker's gives it all the height it
    /// asks for — so the inner one never scrolls vertically and the outer one
    /// is the one that has to move. Which of them scrolls is a fact about the
    /// layout, so each is asked.
    /// </para>
    /// <para>
    /// Vertical only. The sheet scrolls along the frames as well, and finding
    /// a layer must not cost the artist their place in time.
    /// </para>
    /// </remarks>
    private static void RevealVertically(Control control, double y, double height)
    {
        if (!control.IsEffectivelyVisible || height <= 0) return;
        foreach (var scroller in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            if (scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled) continue;
            if (control.TranslatePoint(new Point(0, y), scroller) is not { } inView) continue;

            var offset = scroller.Offset.Y;
            var top = inView.Y + offset;
            var furthest = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
            var target = Math.Clamp(
                Input.ScrollReveal.Offset(offset, scroller.Viewport.Height, top, top + height), 0, furthest);
            if (Math.Abs(target - offset) > 0.5) scroller.Offset = scroller.Offset.WithY(target);
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

    /// <summary>
    /// The X-sheet's way into the bone system's one timeline command. Aimed at
    /// the cel that was right-clicked, not at the playhead — see
    /// <c>MainViewModel.InsertDrawingFromPoseAt</c>.
    /// </summary>
    private void OnInsertDrawingFromPose(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.InsertDrawingFromPoseAt(cell);
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

    private void OnInsertBlankFrame(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.InsertBlankFrameAt(cell);
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

    // ---- Animation menu ---------------------------------------------------------
    // The cel context menu's verbs, re-aimed: a menu item has no cel under a
    // pointer, so each acts on the active layer's cel at the playhead — the
    // same answer the timeline shortcuts give.

    private void OnMenuInsertKeyframe(object? sender, RoutedEventArgs e) =>
        _vm.InsertFrameAtPlayhead(FrameRole.Key);

    private void OnMenuInsertBreakdown(object? sender, RoutedEventArgs e) =>
        _vm.InsertFrameAtPlayhead(FrameRole.Breakdown);

    private void OnMenuInsertInbetween(object? sender, RoutedEventArgs e) =>
        _vm.InsertFrameAtPlayhead(FrameRole.Inbetween);

    private void OnMenuExtendExposure(object? sender, RoutedEventArgs e) => _vm.ExtendExposureAtPlayhead();

    private void OnMenuReduceExposure(object? sender, RoutedEventArgs e) => _vm.ReduceExposureAtPlayhead();

    private void OnMenuCopyCel(object? sender, RoutedEventArgs e) => _vm.CopyCurrentCel();

    private void OnMenuCutCel(object? sender, RoutedEventArgs e) => _vm.CutCurrentCel();

    private void OnMenuPasteCel(object? sender, RoutedEventArgs e) => _vm.PasteCurrentCel();

    private void OnMenuClearCel(object? sender, RoutedEventArgs e) => _vm.ClearCelAtPlayhead();

    private void OnMenuDeleteCel(object? sender, RoutedEventArgs e) => _vm.DeleteCelAtPlayhead();

    private void OnMenuInsertBlankFrame(object? sender, RoutedEventArgs e) => _vm.InsertBlankFrameAtPlayhead();

    private void OnMenuInsertBlankKeyframe(object? sender, RoutedEventArgs e) => _vm.InsertBlankKeyframeAtPlayhead();

    private void OnInsertBlankKeyframe(object? sender, RoutedEventArgs e)
    {
        if (CellOf(sender) is { } cell) _vm.InsertBlankKeyframeAt(cell);
    }

    private void OnMenuSetStartFrame(object? sender, RoutedEventArgs e) => _vm.SetPlaybackStartAtPlayhead();

    private void OnMenuSetEndFrame(object? sender, RoutedEventArgs e) => _vm.SetPlaybackEndAtPlayhead();

    // ---- multi-cel selection (Ctrl+click, Shift+click, drag) --------------------

    /// <summary>A plain drag across the sheet selects a block (Q207); Alt+drag moves a cel.</summary>
    private readonly Input.CelBlockSelectGesture _celSelect = new();

    private static FrameCell? CellUnder(object? source) =>
        (source as Control)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext as FrameCell;

    private void OnTimelinePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CellUnder(e.Source) is not { } cell) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Both are handled here and marked handled, so the cell's own click —
        // which selects the frame and clears the selection — never also runs.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _vm.RangeSelectTo(cell);
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _vm.ToggleCelSelection(cell);
            e.Handled = true;
            return;
        }
        // Remember the press so a later move can turn it into a drag: Alt
        // carries the drawing along its row, a plain drag selects a block (Q207).
        // Exactly one of the two is armed, so they cannot both claim the press.
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _celDrag.Press(cell, e.GetPosition(this), leftButton: true, keyed: alt && cell.IsKeyed && !cell.IsVirtual);
        _celDragPress = _celDrag.Candidate is null ? null : e;
        if (alt) _celSelect.Cancel();
        else _celSelect.Press(cell, e.GetPosition(this), leftButton: true);
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
        _celSelect.Cancel();
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
        _celSelect.Cancel();
    }

    // ---- drag a cel along its row ------------------------------------------------

    private static readonly DataFormat<FrameCell> CelDragFormat =
        DataFormat.CreateInProcessFormat<FrameCell>("lightbox-cel");


    private async void OnCellPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not FrameCell cell) return;
        if (_celSelect.From is { } from)
        {
            var at = e.GetCurrentPoint(this);
            if (_celSelect.Moved(at.Position, at.Properties.IsLeftButtonPressed, out var started))
            {
                // The pressed cel holds the pointer, so every move would report
                // it. Letting go of the capture sends moves to whichever cel is
                // under the pointer — and means the release is no longer a click
                // on the first cel, which would move the playhead and clear the
                // block just made.
                if (started) e.Pointer.Capture(null);
                if (_celSelect.CornerMovedTo(cell)) _vm.DragSelectTo(from, cell);
            }
            return;
        }
        if (_celDragPress is not { } press) return;
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
}
