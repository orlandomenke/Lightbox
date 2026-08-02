using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;

namespace Lightbox.App.Views;

/// <summary>One editable shortcut row in the Configure window.</summary>
public sealed partial class ShortcutRow(ShortcutDefinition definition) : ObservableObject
{
    public ShortcutDefinition Definition { get; } = definition;

    public string Name => Definition.Name;

    [ObservableProperty]
    private string _gestureText = definition.GestureText;

    public void Refresh() => GestureText = Definition.GestureText;
}

public sealed class ShortcutGroup(string name, IEnumerable<ShortcutRow> rows)
{
    public string Name { get; } = name;

    public ObservableCollection<ShortcutRow> Rows { get; } = new(rows);
}

/// <summary>
/// One grid already placed on the document, editable.
/// </summary>
/// <remarks>
/// Every setter goes back through the view model rather than at the guide,
/// so each change is one undo step and the canvas redraws. A row that wrote
/// straight to the object would move a grid nobody could put back.
/// </remarks>
public sealed partial class GridRow(
    Lightbox.Core.Documents.Guide guide, ViewModels.MainViewModel vm) : ObservableObject
{
    public string Title { get; } = string.IsNullOrWhiteSpace(guide.Name) ? "Grid" : guide.Name;

    public double Spacing
    {
        get => guide.Spacing;
        set
        {
            if (Math.Abs(guide.Spacing - value) < 1e-9) return;
            vm.SetGridSpacing(guide, value);
            OnPropertyChanged();
        }
    }

    public double Angle
    {
        get => guide.Angle;
        set
        {
            if (Math.Abs(guide.Angle - value) < 1e-9) return;
            vm.SetGridAngle(guide, value);
            OnPropertyChanged();
        }
    }

    public bool Visible
    {
        get => guide.Visible;
        set
        {
            if (guide.Visible == value) return;
            vm.SetGuideFlags(guide, value, guide.Snaps);
            OnPropertyChanged();
        }
    }

    public bool Snaps
    {
        get => guide.Snaps;
        set
        {
            if (guide.Snaps == value) return;
            vm.SetGuideFlags(guide, guide.Visible, value);
            OnPropertyChanged();
        }
    }
}

/// <summary>
/// Edit → Configure: categories on the left, content in the center. The
/// Shortcuts page lists every rebindable command grouped by area, searchable
/// by name or by keys, with a conflict warning before a clashing binding can
/// be committed.
/// </summary>
public partial class ConfigureWindow : Window
{
    private readonly ShortcutMap _map;
    private readonly List<ShortcutRow> _allRows;
    private ShortcutRow? _capturing;
    private (ShortcutRow Row, KeyGesture Gesture, ShortcutDefinition Conflict)? _pending;

    private readonly ViewModels.MainViewModel? _vm;

    public ConfigureWindow() : this(new ShortcutMap())
    {
    }

    public ConfigureWindow(ShortcutMap map, ViewModels.MainViewModel? vm = null)
    {
        _map = map;
        _vm = vm;
        InitializeComponent();
        _allRows = map.Definitions.Select(d => new ShortcutRow(d)).ToList();
        RebuildGroups();
        AddHandler(KeyDownEvent, OnCaptureKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        LoadPerformancePage();
        LoadGuidesPage();
        LoadTimelinePage();
    }

    // ---- timeline page -----------------------------------------------------------

    private bool _loadingTimeline;

    private void LoadTimelinePage()
    {
        if (_vm is null) return;
        _loadingTimeline = true;
        HoldBox.ItemsSource = _vm.HoldDrawingChoices;
        HoldBox.SelectedItem = _vm.DrawingOnAHold;
        LoopBox.IsChecked = _vm.LoopPlayback;
        FrameWidthBox.Value = (decimal)_vm.TimelineFrameWidth;
        _loadingTimeline = false;
        RefreshHoldHint();
    }

    private void RefreshHoldHint()
    {
        if (_vm is null) return;
        HoldHint.Text = _vm.DrawingOnAHold switch
        {
            ViewModels.HoldDrawing.EditTheHeldDrawing =>
                "The mark joins the drawing being held, so it appears on every frame holding it. "
                + "Right for touching up a held pose without breaking the hold.",
            _ =>
                "The cel becomes a drawing of its own and the mark lands on it. What every animation "
                + "tool does, and what makes the timeline show a drawing where you made one.",
        };
    }

    private void OnHoldDrawingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingTimeline || _vm is null) return;
        if (HoldBox.SelectedItem is ViewModels.HoldDrawing choice)
        {
            _vm.DrawingOnAHold = choice;
            RefreshHoldHint();
        }
    }

    private void OnLoopChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingTimeline || _vm is null || LoopBox.IsChecked is not { } on) return;
        _vm.LoopPlayback = on;
    }

    private void OnFrameWidthChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingTimeline || _vm is null || e.NewValue is not { } value) return;
        _vm.TimelineFrameWidth = (double)value;
    }

    // ---- guides and grid page --------------------------------------------------

    private bool _loadingGuides;

    private void LoadGuidesPage()
    {
        if (_vm is null) return;
        _loadingGuides = true;
        GridSpacingBox.Value = (decimal)_vm.GridSpacing;
        SnapToleranceBox.Value = (decimal)_vm.SnapTolerance;
        _loadingGuides = false;
        RefreshGrids();
    }

    private void RefreshGrids()
    {
        if (_vm is null) return;
        var rows = _vm.GridGuides.Select(g => new GridRow(g, _vm)).ToList();
        GridsHost.ItemsSource = rows;
        NoGridsText.IsVisible = rows.Count == 0;
    }

    private void OnGridSpacingChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingGuides || _vm is null || e.NewValue is not { } value) return;
        _vm.GridSpacing = (double)value;
    }

    private void OnSnapToleranceChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingGuides || _vm is null || e.NewValue is not { } value) return;
        _vm.SnapTolerance = (double)value;
    }

    // ---- performance page -----------------------------------------------------

    private bool _loadingPerformance;

    private void LoadPerformancePage()
    {
        if (_vm is null) return;
        _loadingPerformance = true;
        QualityBox.ItemsSource = _vm.CanvasQualityChoices;
        QualityBox.SelectedItem = _vm.CanvasQuality;
        UndoDepthBox.Value = _vm.UndoDepth;
        CacheBudgetBox.Value = _vm.FrameCacheBudgetMb;
        _loadingPerformance = false;
        RefreshMeasured();
    }

    private void RefreshMeasured()
    {
        if (_vm is null) return;
        var perf = _vm.Performance;
        MeasuredText.Text =
            $"{_vm.DocumentSizeLabel} · {_vm.MemoryLabel}\n" +
            $"Compositing an edit: {perf.PublishMs:0.0} ms · " +
            $"Presenting a frame: {perf.FrameMs:0.0} ms · " +
            $"Headroom {perf.HeadroomPercent}% ({perf.HealthLabel})";
        BackendText.Text = Rendering.CanvasControl.SoftwareRendering switch
        {
            true =>
                "This machine is presenting the canvas in software — no GPU context was available. "
                + "Rescaling the whole document every frame is then the dominant cost, which is why "
                + "the quality above starts at Half here. Updating the graphics driver, or running "
                + "without remote desktop or a virtual machine, is what gets a GPU context back. "
                + "The document, exports and thumbnails are full resolution either way.",
            false =>
                "The canvas is being presented by the GPU. Editing the drawing is the cost that "
                + "matters on this machine, not showing it.",
            null => "Nothing has been drawn yet, so the graphics backend is not known.",
        };
        QualityHint.Text = _vm.CanvasQuality switch
        {
            ViewModels.CanvasQuality.Full =>
                "Sharpest at every zoom, and the most expensive — the whole document is rescaled for each frame.",
            ViewModels.CanvasQuality.Half =>
                "Softer while you work; the drawing itself is unaffected. Best on a large canvas or a slower machine.",
            _ => "Matches the screen: full detail when zoomed in, less when zoomed out. The right default.",
        };
    }

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ShortcutsPage is null || PerformancePage is null
            || GuidesPage is null || TimelinePage is null)
        {
            return;
        }
        var page = CategoryList.SelectedIndex;
        ShortcutsPage.IsVisible = page == 0;
        PerformancePage.IsVisible = page == 1;
        GuidesPage.IsVisible = page == 2;
        TimelinePage.IsVisible = page == 3;
        if (page == 1) RefreshMeasured();
        // Rebuilt on the way in: a grid may have been placed since the window
        // opened, and the window outlives the drawing that made it.
        if (page == 2) RefreshGrids();
        if (page == 3) LoadTimelinePage();
    }

    private void OnQualityChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingPerformance || _vm is null) return;
        if (QualityBox.SelectedItem is ViewModels.CanvasQuality quality)
        {
            // Through the choosing path: from here it is a decision, and the
            // software-rendering fallback must never revise it again.
            _vm.ChooseCanvasQuality(quality);
            RefreshMeasured();
        }
    }

    private void OnUndoDepthChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingPerformance || _vm is null || e.NewValue is not { } value) return;
        _vm.UndoDepth = (int)value;
    }

    private void OnCacheBudgetChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingPerformance || _vm is null || e.NewValue is not { } value) return;
        _vm.FrameCacheBudgetMb = (int)value;
        RefreshMeasured();
    }

    private void RebuildGroups()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        var rows = _allRows.Where(r =>
            query.Length == 0
            || r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || r.Definition.GestureText.Contains(query, StringComparison.OrdinalIgnoreCase));
        GroupsHost.ItemsSource = rows
            .GroupBy(r => r.Definition.Category)
            .Select(g => new ShortcutGroup(g.Key, g))
            .ToList();
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) => RebuildGroups();

    private void OnCaptureClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ShortcutRow row) return;
        CancelPending();
        if (_capturing is { } previous) previous.Refresh();
        _capturing = row;
        row.GestureText = "press keys…";
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturing is not { } row) return;
        e.Handled = true;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            return; // wait for the real key
        }
        _capturing = null;
        if (e.Key == Key.Escape)
        {
            row.Refresh();
            return;
        }

        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        if (_map.ConflictWith(row.Definition.Id, gesture) is { } conflict)
        {
            // Warn before committing a clash: the user must choose explicitly.
            _pending = (row, gesture, conflict);
            ConflictText.Text =
                $"“{gesture}” is already assigned to “{conflict.Name}” ({conflict.Category}). " +
                $"Assign it to “{row.Name}” instead? “{conflict.Name}” will lose its shortcut.";
            ConflictBar.IsVisible = true;
            row.Refresh();
            return;
        }

        _map.Assign(row.Definition.Id, gesture);
        RefreshAllRows();
    }

    private void OnAssignAnyway(object? sender, RoutedEventArgs e)
    {
        if (_pending is not { } pending) return;
        _map.Assign(pending.Row.Definition.Id, pending.Gesture, unbindConflicts: true);
        CancelPending();
        RefreshAllRows();
    }

    private void OnCancelAssign(object? sender, RoutedEventArgs e) => CancelPending();

    private void CancelPending()
    {
        _pending = null;
        ConflictBar.IsVisible = false;
    }

    private void OnResetAll(object? sender, RoutedEventArgs e)
    {
        _map.ResetToDefaults();
        CancelPending();
        RefreshAllRows();
    }

    private void RefreshAllRows()
    {
        foreach (var row in _allRows) row.Refresh();
    }
}
