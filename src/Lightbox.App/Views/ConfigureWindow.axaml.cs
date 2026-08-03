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
/// One field of the chosen AI provider, editable.
/// </summary>
/// <remarks>
/// Built from the catalogue rather than declared in XAML, so the page does
/// not have to know that Claude wants a key and an MCP server wants a command
/// line. The watermark carries the part a person cannot see: whether a blank
/// box is actually blank, or is already satisfied by an environment variable
/// or a default.
/// </remarks>
public sealed partial class AiFieldRow : ObservableObject
{
    private readonly Lightbox.Ai.AiConnection _connection;
    private readonly Action _changed;

    public AiFieldRow(Lightbox.Ai.AiField field, Lightbox.Ai.AiConnection connection, Action changed)
    {
        Field = field;
        _connection = connection;
        _changed = changed;
        _value = connection.Stored(field.Id) ?? "";
    }

    public Lightbox.Ai.AiField Field { get; }

    public string Label => Field.Required ? Field.Label : Field.Label + " (optional)";

    public string? Hint => Field.Hint;

    public bool HasHint => !string.IsNullOrEmpty(Field.Hint);

    /// <summary>'\0' means "show the text" — a secret shows dots instead.</summary>
    public char Mask => Field.Kind == Lightbox.Ai.AiFieldKind.Secret ? '•' : '\0';

    [ObservableProperty]
    private string _value;

    public string Placeholder => _connection.OriginOf(Field.Id) switch
    {
        Lightbox.Ai.AiValueOrigin.Environment => $"using {Field.EnvVar}",
        Lightbox.Ai.AiValueOrigin.Default => Field.Default ?? "",
        Lightbox.Ai.AiValueOrigin.Missing when Field.Required => "required",
        _ => "",
    };

    partial void OnValueChanged(string value)
    {
        _connection.Set(Field.Id, value);
        // Clearing a box can uncover an environment variable or a default;
        // the watermark has to say so or the field reads as unset.
        OnPropertyChanged(nameof(Placeholder));
        _changed();
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
        LoadDrawingPage();
        LoadAiPage();
    }

    // ---- AI page ---------------------------------------------------------------

    private Lightbox.Ai.AiConnection _ai = new();
    private bool _loadingAi;
    private CancellationTokenSource? _aiTestCts;

    /// <summary>Test seam: the rows the page is currently showing.</summary>
    internal IReadOnlyList<AiFieldRow> AiFieldRows { get; private set; } = [];

    /// <summary>Test seam: the last thing Test connection said.</summary>
    internal string AiTestMessage => AiTestStatus?.Text ?? "";

    /// <summary>The two depths, in the order the picker shows them.</summary>
    private static readonly (Lightbox.Ai.AiTestDepth Depth, string Label, string Explain)[] AiDepths =
    [
        (Lightbox.Ai.AiTestDepth.Quick, "Quick test",
            "Asks for one short line on a small canvas. Seconds, a few hundred tokens, and it "
            + "proves the whole path: the key, the model name, schema-constrained output, the "
            + "parse, and that the strokes land on the canvas."),
        (Lightbox.Ai.AiTestDepth.Thorough, "Test with a drawing",
            "The quick test, then a real inbetween between two keyframes — checked for landing "
            + "between them rather than merely being well-formed. This is what catches a model "
            + "that answers in perfect JSON and cannot inbetween. Minutes on a local model."),
    ];

    private void LoadAiPage()
    {
        if (AiProviderBox is null) return;
        _loadingAi = true;
        _ai = Lightbox.Ai.AiSettings.Load();
        AiEnabledBox.IsChecked = _ai.Enabled;
        AiProviderBox.ItemsSource = Lightbox.Ai.AiProviders.All.Select(p => p.Name).ToList();
        AiProviderBox.SelectedIndex = Lightbox.Ai.AiProviders.All
            .Select((p, i) => (p, i)).First(x => x.p.Id == _ai.Provider.Id).i;
        AiTestDepthBox.ItemsSource = AiDepths.Select(d => d.Label).ToList();
        AiTestDepthBox.SelectedIndex = 0;
        _loadingAi = false;
        RebuildAiFields();
        RefreshAiTestExplain();
    }

    private void OnAiEnabledChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingAi || AiEnabledBox.IsChecked is not { } on) return;
        _ai.Enabled = on;
        SaveAi();
    }

    private void OnAiTestDepthChanged(object? sender, SelectionChangedEventArgs e) => RefreshAiTestExplain();

    private void RefreshAiTestExplain()
    {
        if (AiTestExplain is null) return;
        AiTestExplain.Text = AiDepths[Math.Max(0, AiTestDepthBox.SelectedIndex)].Explain;
    }

    /// <summary>
    /// Rebuild the editor for the selected provider. Whole-list rather than
    /// diffed: switching provider changes which fields exist, and six rows is
    /// not worth the reconciliation.
    /// </summary>
    private void RebuildAiFields()
    {
        if (AiFieldsHost is null) return;
        AiSummary.Text = _ai.Provider.Summary;
        AiFieldRows = _ai.Provider.Fields
            .Select(f => new AiFieldRow(f, _ai, SaveAi))
            .ToList();
        AiFieldsHost.ItemsSource = AiFieldRows;
        // A stale verdict beside changed fields is worse than none: it reads
        // as though the new values were the ones that passed.
        SetAiStatus("", ok: null);
    }

    /// <summary>
    /// Persist on every keystroke, and hand the view model the new artist.
    /// </summary>
    /// <remarks>
    /// No Save button because this window has never had one, and because
    /// "typed the key, closed the window, AI still off" is the failure the
    /// button would cause. Writing a partial key is harmless — the connection
    /// is simply incomplete until it is not.
    /// </remarks>
    private void SaveAi()
    {
        if (_loadingAi) return;
        Lightbox.Ai.AiSettings.Save(_ai);
        _vm?.ReloadAiProvider();
    }

    private void OnAiProviderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingAi || AiProviderBox.SelectedIndex < 0) return;
        _ai.ProviderId = Lightbox.Ai.AiProviders.All[AiProviderBox.SelectedIndex].Id;
        RebuildAiFields();
        SaveAi();
    }

    /// <summary>
    /// Beyond this, say so. A thorough test against a local model genuinely
    /// takes minutes, and silence for that long is indistinguishable from a
    /// hang — which is the whole reason for the clock and the bar.
    /// </summary>
    private static readonly TimeSpan LongTest = TimeSpan.FromMinutes(2);

    private async void OnAiTestClicked(object? sender, RoutedEventArgs e)
    {
        // A second click cancels the first: a hung endpoint would otherwise
        // leave the button dead until its ten-minute timeout.
        if (_aiTestCts is not null)
        {
            await _aiTestCts.CancelAsync();
            return;
        }

        var depth = AiDepths[Math.Max(0, AiTestDepthBox.SelectedIndex)].Depth;
        _aiTestCts = new CancellationTokenSource();
        AiTestButton.Content = "Cancel";
        AiTestProgress.IsVisible = true;
        AiTestElapsed.IsVisible = true;
        SetAiStatus("Connecting…", ok: null);

        var started = DateTime.UtcNow;
        var stage = "Connecting…";
        var clock = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - started;
            AiTestElapsed.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
            SetAiStatus(
                elapsed > LongTest
                    ? stage + " Still going — a local model can take several minutes. Click Cancel to stop."
                    : stage,
                ok: null);
        };
        clock.Start();

        try
        {
            var progress = new Progress<string>(s => stage = s);
            var check = await Lightbox.Ai.AiConnectionTester.TestAsync(
                _ai, depth, progress, _aiTestCts.Token);
            clock.Stop();
            // Three states, not two: "unreachable" and "reachable but drawing
            // nonsense" need different fixes, and amber says which.
            SetAiStatus(check.Message, check.Ok ? true : check.Connected ? null : false);
        }
        catch (Exception ex)
        {
            // The tester is meant to return failures rather than throw; if one
            // escapes, the window must still be usable.
            clock.Stop();
            SetAiStatus(ex.Message, ok: false);
        }
        finally
        {
            clock.Stop();
            _aiTestCts.Dispose();
            _aiTestCts = null;
            AiTestButton.Content = "Test connection";
            AiTestProgress.IsVisible = false;
            AiTestElapsed.IsVisible = false;
        }
    }

    /// <summary>Green passed, amber connected-but-unusable, red not connected.</summary>
    private void SetAiStatus(string message, bool? ok)
    {
        if (AiTestStatus is null) return;
        AiTestStatus.Text = message;
        AiTestStatus.Foreground = ok switch
        {
            true => Avalonia.Media.Brushes.MediumSeaGreen,
            false => Avalonia.Media.Brushes.IndianRed,
            _ => Avalonia.Media.Brushes.Goldenrod,
        };
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

    private bool _loadingDrawing;

    private void LoadDrawingPage()
    {
        if (_vm is null || SampleBox is null) return;
        _loadingDrawing = true;
        SampleBox.ItemsSource = _vm.SampleSourceChoices;
        SampleBox.SelectedItem = _vm.SmudgeSampleSource;
        BrushScopeBox.ItemsSource = _vm.BrushMemoryChoices;
        BrushScopeBox.SelectedItem = _vm.BrushMemoryChoice;
        _loadingDrawing = false;
        RefreshSampleHint();
        RefreshBrushScopeHint();
    }

    private void OnBrushScopeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingDrawing || _vm is null) return;
        if (BrushScopeBox.SelectedItem is string choice) _vm.BrushMemoryChoice = choice;
        RefreshBrushScopeHint();
    }

    /// <summary>
    /// Say what the setting resolves to, not just what was picked.
    /// </summary>
    /// <remarks>
    /// "Follow the project" is the default and is the one answer a person
    /// cannot check by reading it back, so the hint spells out what it means
    /// for the project they actually have open.
    /// </remarks>
    private void RefreshBrushScopeHint()
    {
        if (BrushScopeHint is null || _vm is null) return;
        var effective = _vm.BrushScope == Lightbox.Core.Documents.BrushScope.PerProject
            ? "the project keeps the brush and gives it to every document in it"
            : "one brush for the application";
        BrushScopeHint.Text = _vm.BrushMemoryChoice switch
        {
            "Global" =>
                "One brush, carried between documents and sessions — what Photoshop and Krita do.",
            "Per project" =>
                "The project remembers the brush you paint with and hands it to every document in it, "
                + "including the ones you have not made yet. Saved with the project, so it survives the "
                + "break that made you forget it.",
            _ =>
                $"Illustration, comic, game art and asset libraries keep the brush with the project; "
                + $"animation and storyboards keep one brush for the tool. Right now: {effective}.",
        };
    }

    private void OnSampleSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingDrawing || _vm is null) return;
        if (SampleBox.SelectedItem is Lightbox.Core.Documents.SampleSource source) _vm.SmudgeSampleSource = source;
        RefreshSampleHint();
    }

    private void RefreshSampleHint()
    {
        if (SampleHint is null || _vm is null) return;
        SampleHint.Text = _vm.SmudgeSampleSource switch
        {
            Lightbox.Core.Documents.SampleSource.AllLayersLive =>
                "Blends what you can see, and keeps following it — repaint a layer underneath and the "
                + "smudge above changes with it. Frames holding one cannot be cached, so a scene full of "
                + "them redraws more often.",
            Lightbox.Core.Documents.SampleSource.AllLayersBaked =>
                "Blends what you can see at the moment you make the mark, and then keeps it. Costs "
                + "nothing to redraw; the stroke carries a copy of what it blended, so the file grows.",
            _ =>
                "Only the layer you are painting on. The default, and the cheapest — a layer stays a "
                + "picture of itself.",
        };
    }

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ShortcutsPage is null || PerformancePage is null || GuidesPage is null
            || TimelinePage is null || DrawingPage is null || AiPage is null)
        {
            return;
        }
        var page = CategoryList.SelectedIndex;
        ShortcutsPage.IsVisible = page == 0;
        PerformancePage.IsVisible = page == 1;
        GuidesPage.IsVisible = page == 2;
        TimelinePage.IsVisible = page == 3;
        DrawingPage.IsVisible = page == 4;
        AiPage.IsVisible = page == 5;
        if (page == 1) RefreshMeasured();
        // Rebuilt on the way in: a grid may have been placed since the window
        // opened, and the window outlives the drawing that made it.
        if (page == 2) RefreshGrids();
        if (page == 3) LoadTimelinePage();
        if (page == 4) LoadDrawingPage();
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
