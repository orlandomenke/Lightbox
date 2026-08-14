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

    // ---- multi-cel selection (Ctrl+click, Shift+click) --------------------------

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
}
