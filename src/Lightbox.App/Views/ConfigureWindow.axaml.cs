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

    public ConfigureWindow() : this(new ShortcutMap())
    {
    }

    public ConfigureWindow(ShortcutMap map)
    {
        _map = map;
        InitializeComponent();
        _allRows = map.Definitions.Select(d => new ShortcutRow(d)).ToList();
        RebuildGroups();
        AddHandler(KeyDownEvent, OnCaptureKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
