using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using Lightbox.App.Services;
using Lightbox.Core.Projects;

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

/// <summary>
/// One heading in the shortcut editor: every command that applies in one place.
/// </summary>
/// <remarks>
/// <b>Grouped by where a binding applies rather than by what it does (Q82).</b>
/// The list used to be grouped by category, which meant the two commands bound
/// to <c>I</c> sat under "Tools" and "Timeline" with nothing saying that one of
/// them only answers over the timeline — an artist reading the list saw the same
/// key twice and no rule. The cost of the swap is written down in
/// <c>QUESTIONS.md</c> → Q82: "Everywhere" is now a long group, and commands an
/// artist thinks of together are split across headings when they happen to
/// differ in scope. <see cref="Hint"/> is what buys the trade back — the heading
/// states the rule instead of leaving it to be inferred.
/// </remarks>
public sealed class ShortcutGroup(string name, string hint, IEnumerable<ShortcutRow> rows)
{
    public string Name { get; } = name;

    /// <summary>What this heading means, in one line under it.</summary>
    public string Hint { get; } = hint;

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
/// One character height scale already placed on the document, editable.
/// </summary>
/// <remarks>
/// The same shape as <see cref="GridRow"/> and for the same reason: every
/// setter goes back through the view model so each change is one undo step
/// and the canvas redraws.
/// </remarks>
public sealed partial class HeightScaleRow(
    Lightbox.Core.Documents.Guide guide, ViewModels.MainViewModel vm) : ObservableObject
{
    public string Title { get; } =
        string.IsNullOrWhiteSpace(guide.Name) ? "Height scale" : guide.Name;

    public double Unit
    {
        get => guide.Spacing;
        set
        {
            if (Math.Abs(guide.Spacing - value) < 1e-9) return;
            vm.SetHeightScale(guide, value, guide.Divisions ?? 1);
            OnPropertyChanged();
        }
    }

    public int Divisions
    {
        get => guide.Divisions ?? 1;
        set
        {
            if ((guide.Divisions ?? 1) == value) return;
            vm.SetHeightScale(guide, guide.Spacing, value);
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
/// One feature toggle in the Features page, editable.
/// </summary>
/// <remarks>
/// Each feature has a key, label, and description. The toggle binds to the
/// document's feature overrides; changes update the document and mark it dirty.
/// </remarks>
public sealed partial class FeatureToggleRow : ObservableObject
{
    private readonly FeatureKey _feature;
    private readonly ViewModels.MainViewModel _vm;
    private readonly bool _projectDefault;

    public FeatureToggleRow(FeatureKey feature, ViewModels.MainViewModel vm, bool projectDefault)
    {
        _feature = feature;
        _vm = vm;
        _projectDefault = projectDefault;
        _isEnabled = ResolveEnabled();
    }

    public string Label => _feature switch
    {
        FeatureKey.FixedFrameBoundsExport => "Fixed frame bounds export",
        FeatureKey.Camera => "Camera",
        FeatureKey.Layers => "Layers",
        FeatureKey.ExposureSheet => "Exposure sheet",
        _ => _feature.ToString(),
    };

    public string Description => _feature switch
    {
        FeatureKey.FixedFrameBoundsExport =>
            "Constrain export to fixed frame bounds for sprite sheet generation.",
        FeatureKey.Camera =>
            "Add camera and multiplane support for shots and film sequences.",
        FeatureKey.Layers =>
            "Organize the document with layers. Enabled by default in all project types.",
        FeatureKey.ExposureSheet =>
            "Display timing and hold information for frame-by-frame animation.",
        _ => "",
    };

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_vm.ActiveTab?.Doc is not { } doc) return;
        _vm.SetDocumentFeature(_feature, value, _projectDefault);
    }

    private bool ResolveEnabled()
    {
        if (_vm.ActiveTab?.Doc is not { } doc) return _projectDefault;
        return doc.GetFeature(_feature, _projectDefault);
    }

    public void Refresh() => IsEnabled = ResolveEnabled();
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
        LoadFeaturesPage();
        LoadGuidesPage();
        LoadTimelinePage();
        LoadDrawingPage();
        LoadAiPage();
        LoadLibraryPage();
    }

    // ---- Library page ------------------------------------------------------

    /// <summary>
    /// The library roots, edited through the same <see cref="ViewModels.LibraryViewModel"/>
    /// the library window uses — one owner, so the two editors cannot desync,
    /// and every change lands in the settings file the moment it is made.
    /// </summary>
    private void LoadLibraryPage()
    {
        if (_vm is null) return;
        LibraryRootsList.ItemsSource = _vm.Characters.Roots;
    }

    private async void OnLibraryAddRoot(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        var picked = await StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Add a library folder",
                AllowMultiple = false,
            });
        if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
        {
            _vm.Characters.AddRoot(path);
        }
    }

    private void OnLibraryRemoveRoot(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (LibraryRootsList.SelectedItem is string root) _vm.Characters.RemoveRoot(root);
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
        AiRunSizeBox.ItemsSource = AiRunSizes.Select(r => r.Label).ToList();
        AiRunSizeBox.SelectedIndex = 0;
        _loadingAi = false;
        RebuildAiFields();
        RefreshAiTestExplain();
        RefreshAiRunCost();
        ShowStoredProfile();
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
        // Typing a different model name makes a stored reading about something
        // else, and the warning has to appear as the field changes rather than
        // at the next window open.
        ShowStoredProfile();
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

    // ---- grading the model (phase 2) ---------------------------------------------

    private CancellationTokenSource? _aiProfileCts;

    /// <summary>Test seam: the profile lines the page is currently showing.</summary>
    internal IReadOnlyList<string> AiProfileShown { get; private set; } = [];

    /// <summary>Test seam: whether the page is warning that the profile is about another model.</summary>
    internal bool AiProfileIsStale => AiProfileStale?.IsVisible ?? false;

    /// <summary>The two run sizes, in the order the picker shows them.</summary>
    private static readonly (bool Full, string Label)[] AiRunSizes =
    [
        (false, "Short run"),
        (true, "Full run"),
    ];

    private static IReadOnlyList<Lightbox.Ai.Golden.GoldenPair> AiPairs(bool full) =>
        full ? Lightbox.Ai.Golden.GoldenSet.Full() : Lightbox.Ai.Golden.GoldenSet.Short();

    private bool AiFullRun => AiRunSizes[Math.Max(0, AiRunSizeBox?.SelectedIndex ?? 0)].Full;

    private void OnAiRunSizeChanged(object? sender, SelectionChangedEventArgs e) => RefreshAiRunCost();

    /// <summary>
    /// What the chosen run will send, before any of it is sent.
    /// </summary>
    /// <remarks>
    /// Payload characters rather than tokens or money, because the conversion
    /// is model-specific and a currency figure would be a guess wearing a
    /// uniform. What the number is for is the *comparison* — the full run is
    /// about five times the short one, and seeing that is what makes choosing
    /// it a decision rather than a click.
    /// </remarks>
    private void RefreshAiRunCost()
    {
        if (AiRunCost is null) return;
        var pairs = AiPairs(AiFullRun);
        var chars = Lightbox.Ai.Golden.CapabilityProfiler.EstimatedPayloadChars(pairs);
        var other = Lightbox.Ai.Golden.CapabilityProfiler.EstimatedPayloadChars(AiPairs(!AiFullRun));
        AiRunCost.Text = AiFullRun
            ? $"{pairs.Count} pairs, about {chars:N0} characters of payload — roughly {(double)chars / Math.Max(1, other):F0}× the short run. "
              + "The long stroke ladder is what costs; it is also what finds where the model gives up."
            : $"{pairs.Count} pairs, about {chars:N0} characters of payload. "
              + "Enough to grade every category and place the stroke ladder roughly; the full run places it precisely.";
    }

    /// <summary>
    /// Show the profile kept from last time, if this connection has one.
    /// </summary>
    /// <remarks>
    /// It is stored against the model it was measured on, so pointing the
    /// connection at a different model leaves a reading that is about
    /// something else. Rather than discard it — the old reading is still true
    /// about the old model, and re-running costs money — the page keeps it and
    /// says whose it is.
    /// </remarks>
    private void ShowStoredProfile()
    {
        if (AiProfileResult is null) return;
        if (_ai.LastProfile is not { } stored)
        {
            AiProfileShown = [];
            AiProfileResult.IsVisible = false;
            return;
        }
        ShowProfileLines(stored.Lines, stored.Subject, stored.Measured);
    }

    private void ShowProfileLines(IReadOnlyList<string> lines, string subject, DateTimeOffset measured)
    {
        AiProfileShown = lines;
        AiProfileLines.ItemsSource = lines;
        AiProfileWhen.Text = $"Measured {measured.ToLocalTime():d MMM yyyy HH:mm}";
        var current = AiSubject();
        var stale = !string.Equals(subject, current, StringComparison.Ordinal);
        AiProfileStale.IsVisible = stale;
        AiProfileStale.Text = stale
            ? $"This reading is about {subject}, and the connection now points at {current}. Grade it again to measure what is actually configured."
            : "";
        AiProfileResult.IsVisible = true;
    }

    /// <summary>Who the profile is about: the provider, and the model when there is one.</summary>
    private string AiSubject() =>
        _ai.Value("model") is { Length: > 0 } model ? $"{_ai.Provider.Name} · {model}" : _ai.Provider.Name;

    private async void OnAiProfileClicked(object? sender, RoutedEventArgs e)
    {
        // Same contract as Test connection: a second click cancels, so a model
        // thinking for ten minutes never leaves a dead button.
        if (_aiProfileCts is not null)
        {
            await _aiProfileCts.CancelAsync();
            return;
        }

        Lightbox.Ai.IAiArtist? artist;
        try
        {
            artist = Lightbox.Ai.AiArtistFactory.Create(_ai, ignoreSwitch: true);
        }
        catch (Exception ex)
        {
            SetAiProfileStatus(ex.Message, ok: false);
            return;
        }
        if (artist is null)
        {
            SetAiProfileStatus("Fill the fields above first — there is no connection to grade.", ok: false);
            return;
        }

        var full = AiFullRun;
        var pairs = AiPairs(full);
        var subject = AiSubject();
        _aiProfileCts = new CancellationTokenSource();
        AiProfileButton.Content = "Cancel";
        AiProfileProgress.IsVisible = true;
        AiProfileElapsed.IsVisible = true;

        var started = DateTime.UtcNow;
        var stage = $"Grading {subject} on {pairs.Count} pairs…";
        SetAiProfileStatus(stage, ok: null);
        var clock = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) =>
        {
            var elapsed = DateTime.UtcNow - started;
            AiProfileElapsed.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
            SetAiProfileStatus(stage, ok: null);
        };
        clock.Start();

        try
        {
            var done = 0;
            var progress = new Progress<string>(s => stage = $"{++done}/{pairs.Count} — {s}");
            var profile = await Lightbox.Ai.Golden.CapabilityProfiler.ProfileAsync(
                artist, subject, pairs, full, progress, _aiProfileCts.Token);
            clock.Stop();

            // Kept before it is shown, so closing the window does not lose a
            // run somebody paid for.
            _ai.LastProfile = new Lightbox.Ai.StoredCapabilityProfile(
                subject, DateTimeOffset.UtcNow, full, profile.Lines());
            SaveAi();
            ShowProfileLines(profile.Lines(), subject, DateTimeOffset.UtcNow);
            SetAiProfileStatus("", ok: null);
        }
        catch (OperationCanceledException)
        {
            clock.Stop();
            // Nothing is stored for a part-finished run: half a ladder would
            // report a degradation rung that is really where somebody clicked.
            SetAiProfileStatus("Stopped. Nothing was recorded — a part-finished run would report the wrong limit.", ok: null);
        }
        catch (Exception ex)
        {
            clock.Stop();
            SetAiProfileStatus(ex.Message, ok: false);
        }
        finally
        {
            clock.Stop();
            _aiProfileCts.Dispose();
            _aiProfileCts = null;
            AiProfileButton.Content = "Grade this model";
            AiProfileProgress.IsVisible = false;
            AiProfileElapsed.IsVisible = false;
        }
    }

    private void SetAiProfileStatus(string message, bool? ok)
    {
        if (AiProfileStatus is null) return;
        AiProfileStatus.Text = message;
        AiProfileStatus.Foreground = ok switch
        {
            true => Avalonia.Media.Brushes.MediumSeaGreen,
            false => Avalonia.Media.Brushes.IndianRed,
            _ => Avalonia.Media.Brushes.Goldenrod,
        };
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
        OffSheetKeyBox.IsChecked = _vm.MarkOffSheetKeys;
        AnimateRigBox.IsChecked = _vm.AnimateRigDuringPlayback;
        FrameWidthBox.Value = (decimal)_vm.TimelineFrameWidth;
        VolumeToleranceBox.Value = (decimal)Math.Round(_vm.Settings.VolumeTolerance * 100);
        _loadingTimeline = false;
        RefreshHoldHint();
    }

    private void OnVolumeToleranceChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingTimeline || _vm is null || e.NewValue is not { } value) return;
        _vm.Settings.VolumeTolerance = (double)value / 100.0;
        _vm.Settings.Save();
        // The readings judge against the tolerance, so a new tolerance is a
        // new set of flags.
        if (_vm.VolumeCheck) _vm.RecomputeVolumeCheck();
    }

    // ---- Export: auto-export on a status change -----------------------------------

    private bool _loadingExport;

    /// <summary>The presets in the picker, in the order they are shown.</summary>
    private List<Lightbox.Core.Projects.ExportPreset> _exportPresets = [];

    private void LoadExportPage()
    {
        if (_vm is null) return;
        _loadingExport = true;

        var settings = _vm.Settings.AutoExport;
        AutoExportBox.IsChecked = settings.Enabled;

        AutoExportStatusBox.ItemsSource = Lightbox.Core.Projects.AssetStatuses.InOrder
            .Select(Lightbox.Core.Projects.AssetStatuses.Label).ToList();
        AutoExportStatusBox.SelectedIndex =
            Lightbox.Core.Projects.AssetStatuses.InOrder.ToList().IndexOf(settings.Trigger);

        // Built-ins plus the artist's own, read fresh: a preset may have been saved from
        // the export window since this one opened.
        _exportPresets = Lightbox.Core.Projects.ExportPreset.BuiltIns
            .Concat(Services.ExportPresetStore.Load()).ToList();
        AutoExportPresetBox.ItemsSource = _exportPresets.Select(p => p.Name).ToList();
        var index = _exportPresets.FindIndex(p => p.Name == settings.PresetName);
        AutoExportPresetBox.SelectedIndex = index < 0 ? 0 : index;

        AutoExportFolderBox.Text = settings.OutputFolder ?? "";

        _loadingExport = false;
    }

    private void OnAutoExportChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingExport || _vm is null) return;
        _vm.Settings.AutoExport.Enabled = AutoExportBox.IsChecked == true;
        _vm.Settings.Save();
    }

    private void OnAutoExportStatusChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingExport || _vm is null) return;
        var order = Lightbox.Core.Projects.AssetStatuses.InOrder;
        if (AutoExportStatusBox.SelectedIndex < 0 || AutoExportStatusBox.SelectedIndex >= order.Count) return;

        _vm.Settings.AutoExport.Trigger = order[AutoExportStatusBox.SelectedIndex];
        _vm.Settings.Save();
    }

    private void OnAutoExportPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingExport || _vm is null) return;
        if (AutoExportPresetBox.SelectedIndex < 0
            || AutoExportPresetBox.SelectedIndex >= _exportPresets.Count)
        {
            return;
        }
        _vm.Settings.AutoExport.PresetName = _exportPresets[AutoExportPresetBox.SelectedIndex].Name;
        _vm.Settings.Save();
    }

    private void OnAutoExportFolderChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingExport || _vm is null) return;
        var text = AutoExportFolderBox.Text?.Trim();
        // Empty means unset rather than "the current directory", which is the one value
        // that would write files somewhere nobody chose.
        _vm.Settings.AutoExport.OutputFolder = string.IsNullOrWhiteSpace(text) ? null : text;
        _vm.Settings.Save();
    }

    private async void OnBrowseAutoExportFolder(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Where auto-exported assets go",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } dir) return;

        // An absolute path from the picker. Somebody who wants the portable relative form
        // types it; a picker cannot express "relative to whatever project is open".
        AutoExportFolderBox.Text = dir;
        _vm.Settings.AutoExport.OutputFolder = dir;
        _vm.Settings.Save();
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

    private void OnMarkOffSheetKeysChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingTimeline || _vm is null || OffSheetKeyBox.IsChecked is not { } on) return;
        _vm.MarkOffSheetKeys = on;
    }

    private void OnAnimateRigChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingTimeline || _vm is null || AnimateRigBox.IsChecked is not { } on) return;
        _vm.AnimateRigDuringPlayback = on;
    }

    private void OnFrameWidthChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingTimeline || _vm is null || e.NewValue is not { } value) return;
        _vm.TimelineFrameWidth = (double)value;
    }

    // ---- features page -------------------------------------------------------

    /// <summary>Test seam: the feature rows the page is currently showing.</summary>
    internal IReadOnlyList<FeatureToggleRow> FeatureRows { get; private set; } = [];

    private void LoadFeaturesPage()
    {
        if (_vm?.ActiveTab?.Doc is null) return;
        RefreshFeatures();
    }

    private void RefreshFeatures()
    {
        if (_vm?.ActiveTab?.Doc is not { } || FeaturesHost is null) return;

        var defaults = new FeatureDefaults();
        var projectType = _vm.ProjectDocker?.Project?.Manifest.Type ?? ProjectType.Animation;
        var features = Enum.GetValues<FeatureKey>();

        var rows = features
            .Select(f => new FeatureToggleRow(f, _vm, defaults.GetDefault(projectType, f)))
            .ToList();

        FeatureRows = rows;
        FeaturesHost.ItemsSource = rows;
    }

    // ---- guides and grid page --------------------------------------------------

    private bool _loadingGuides;

    private void LoadGuidesPage()
    {
        if (_vm is null) return;
        _loadingGuides = true;
        GridSpacingBox.Value = (decimal)_vm.GridSpacing;
        SnapToleranceBox.Value = (decimal)_vm.SnapTolerance;
        VanishingPointRaysBox.Value = _vm.VanishingPointRays;
        HeightScaleHeadsBox.Value = _vm.HeightScaleHeads;
        HeightScaleFillBox.Value = (decimal)Math.Round(_vm.HeightScaleFill * 100);
        _loadingGuides = false;
        RefreshGrids();
    }

    private void RefreshGrids()
    {
        if (_vm is null) return;
        var rows = _vm.GridGuides.Select(g => new GridRow(g, _vm)).ToList();
        GridsHost.ItemsSource = rows;
        NoGridsText.IsVisible = rows.Count == 0;

        var scales = _vm.HeightScaleGuides.Select(g => new HeightScaleRow(g, _vm)).ToList();
        HeightScalesHost.ItemsSource = scales;
        NoHeightScalesText.IsVisible = scales.Count == 0;
    }

    private void OnGridSpacingChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingGuides || _vm is null || e.NewValue is not { } value) return;
        _vm.GridSpacing = (double)value;
    }

    private void OnVanishingPointRaysChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingGuides || _vm is null || e.NewValue is not { } value) return;
        _vm.VanishingPointRays = (int)value;
    }

    private void OnHeightScaleHeadsChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_loadingGuides || _vm is null || e.NewValue is not { } value) return;
        _vm.HeightScaleHeads = (int)value;
    }

    private void OnHeightScaleFillChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        // Percent on the way in, a fraction on the way through: the setting is
        // a share of the canvas and a percentage is how anyone says it.
        if (_loadingGuides || _vm is null || e.NewValue is not { } value) return;
        _vm.HeightScaleFill = (double)value / 100;
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
        PlaybackQualityBox.ItemsSource = _vm.PlaybackQualityChoices;
        PlaybackQualityBox.SelectedItem = _vm.PlaybackQualityChoice;
        UndoDepthBox.Value = _vm.UndoDepth;
        CacheBudgetBox.Value = _vm.FrameCacheBudgetMb;
        GpuCompositeBox.ItemsSource = _vm.GpuCompositingChoices;
        GpuCompositeBox.SelectedItem = _vm.GpuCompositingMode;
        DesktopCompositorBox.IsChecked = _vm.PresentThroughDesktopCompositor;
        _loadingPerformance = false;
        RefreshGpuCompositeHint();
        RefreshMeasured();
    }

    /// <summary>
    /// Which way frames reach the screen. Takes effect at the next start, and
    /// says so rather than appearing to have done nothing.
    /// </summary>
    private void OnDesktopCompositorChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingPerformance || _vm is null) return;
        if (DesktopCompositorBox.IsChecked is not { } on) return;
        if (on == _vm.PresentThroughDesktopCompositor) return;
        _vm.PresentThroughDesktopCompositor = on;
        _vm.AiStatus = on
            ? "Lightbox will present through the desktop compositor at the next start"
            : "Lightbox will present straight to the screen at the next start";
    }

    private void OnGpuCompositeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingPerformance || _vm is null) return;
        if (GpuCompositeBox.SelectedItem is Rendering.GpuComposeMode mode)
        {
            _vm.GpuCompositingMode = mode;
        }
        RefreshGpuCompositeHint();
        RefreshMeasured();
    }

    /// <summary>
    /// Say what will actually happen, rather than only what was asked for.
    /// </summary>
    /// <remarks>
    /// <b>The status bar reading "GPU" and meaning only that Avalonia can blit
    /// misled the owner once already</b>, and B125's entry records it as part of
    /// the bug rather than incidental to it. So this distinguishes three states
    /// the checkbox alone cannot: asked for and running, asked for and refused
    /// because the machine has no graphics context, and asked for but not
    /// reached because nothing takes the GPU route in the current view.
    /// </remarks>
    private void RefreshGpuCompositeHint()
    {
        if (_vm is null) return;

        if (_vm.GpuCompositingMode == Rendering.GpuComposeMode.Off)
        {
            GpuCompositeHint.Text = "Layers are blended on the processor.";
            return;
        }

        if (Rendering.CanvasControl.SoftwareRendering == true)
        {
            GpuCompositeHint.Text =
                "This machine is presenting in software, so there is no graphics card to "
                + "blend on and the processor will keep doing it. Nothing will change.";
            return;
        }

        if (_vm.GpuCompositingMode == Rendering.GpuComposeMode.On)
        {
            GpuCompositeHint.Text =
                "On, whatever this machine measures. Play a scene back, then "
                + "Help ▸ Write a render report to see what it did.";
            return;
        }

        // Automatic. Say what was measured rather than what was asked for — the
        // status bar reading "GPU" and meaning only that Avalonia can blit
        // misled the owner once already, and B125's entry records that as part
        // of the bug rather than incidental to it.
        GpuCompositeHint.Text = Rendering.GpuComposite.AutoProbe switch
        {
            null => "Automatic. Nothing has been drawn yet, so this machine has not been measured.",
            { } probe when Rendering.GpuComposite.AutoDecision == true =>
                $"Automatic: the card blended {probe.Speedup:F1}x faster than the processor here, "
                + "so it is being used. Help ▸ Write a render report says what it did.",
            { HadContext: false } =>
                "Automatic: there is no graphics context on this machine, so the processor "
                + "is blending. Choosing On would change nothing.",
            { SurfaceRefused: true } =>
                "Automatic: the driver would not give Lightbox a surface to blend into, so "
                + "the processor is doing it.",
            { } probe =>
                $"Automatic: the card was only {probe.Speedup:F1}x the processor's speed here — "
                + "not enough to be worth the graphics memory — so the processor is blending. "
                + "Some machines report a graphics card and draw in software anyway; this is "
                + "how that gets caught. Choose On to override it.",
        };
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
        PlaybackQualityHint.Text = _vm.PlaybackQuality switch
        {
            ViewModels.CanvasQuality.Full =>
                "Playback pays for the sharpest frames. Only worth it on a machine with headroom to spare.",
            ViewModels.CanvasQuality.Half =>
                "Frames composite at half size while a scene runs — the single biggest lever when playback stutters. Drawing stays at the quality above.",
            ViewModels.CanvasQuality.Display =>
                "Playback matches the screen, whatever you draw at.",
            _ => "Playback composites at the same quality you draw at. The right default until playback stutters.",
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
        RecordPenAxesBox.IsChecked = _vm.AlwaysRecordPenAxes;
        GoogleFontsBox.IsChecked = _vm.Settings.Fonts.UseGoogleFonts;
        EmbedFontsBox.IsChecked = _vm.Settings.Fonts.EmbedOpenFonts;
        _loadingDrawing = false;
        RefreshSampleHint();
        RefreshBrushScopeHint();
    }

    private void OnRecordPenAxesChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingDrawing || _vm is null) return;
        _vm.AlwaysRecordPenAxes = RecordPenAxesBox.IsChecked == true;
    }

    /// <summary>
    /// Turning Google Fonts off has to reach the library, not only the file.
    /// </summary>
    /// <remarks>
    /// The library decides once, when it is first asked for, whether it has a
    /// Google source at all — which is what makes "off" mean no network rather
    /// than a result that is thrown away. So the switch drops the built library
    /// and the next font list builds the other kind.
    /// </remarks>
    private void OnGoogleFontsChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingDrawing || _vm is null) return;
        _vm.Settings.Fonts.UseGoogleFonts = GoogleFontsBox.IsChecked == true;
        _vm.Settings.Save();
        _vm.ForgetFontLibrary();
    }

    private void OnEmbedFontsChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingDrawing || _vm is null) return;
        _vm.Settings.Fonts.EmbedOpenFonts = EmbedFontsBox.IsChecked == true;
        _vm.Settings.Save();
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
        if (ShortcutsPage is null || PerformancePage is null || FeaturesPage is null
            || GuidesPage is null || TimelinePage is null || DrawingPage is null
            || ExportPage is null || AiPage is null || LibraryPage is null)
        {
            return;
        }
        var page = CategoryList.SelectedIndex;
        ShortcutsPage.IsVisible = page == 0;
        PerformancePage.IsVisible = page == 1;
        FeaturesPage.IsVisible = page == 2;
        GuidesPage.IsVisible = page == 3;
        TimelinePage.IsVisible = page == 4;
        DrawingPage.IsVisible = page == 5;
        ExportPage.IsVisible = page == 6;
        // Library sits before AI: the AI page stays the last category,
        // which TheAiPageIsTheLastCategoryAndHiddenUntilChosen asserts on
        // purpose — appending here is how that test earns its keep.
        LibraryPage.IsVisible = page == 7;
        AiPage.IsVisible = page == 8;
        if (page == 1) RefreshMeasured();
        if (page == 2) RefreshFeatures();
        // Rebuilt on the way in: a grid may have been placed since the window
        // opened, and the window outlives the drawing that made it.
        if (page == 3) RefreshGrids();
        if (page == 4) LoadTimelinePage();
        if (page == 5) LoadDrawingPage();
        // Rebuilt on the way in for the same reason: a preset may have been saved from
        // the export window since this one opened.
        if (page == 6) LoadExportPage();
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

    private void OnPlaybackQualityChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingPerformance || _vm is null) return;
        if (PlaybackQualityBox.SelectedItem is string choice)
        {
            _vm.PlaybackQualityChoice = choice;
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
            || r.Definition.GestureText.Contains(query, StringComparison.OrdinalIgnoreCase)
            // The scope is a heading now, so it has to be searchable too, or
            // typing "timeline" hides the group it is the name of.
            || r.Definition.ScopeName.Contains(query, StringComparison.OrdinalIgnoreCase));
        GroupsHost.ItemsSource = rows
            .GroupBy(r => r.Definition.ScopeName)
            .OrderBy(g => RankOf(g.First().Definition.Context))
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new ShortcutGroup(
                g.Key,
                HintFor(g.First().Definition),
                // Category no longer groups, so it orders instead: it is still
                // how an artist thinks of these, and the widest group would be
                // an undifferentiated wall without it.
                g.OrderBy(r => r.Definition.Category, StringComparer.CurrentCultureIgnoreCase)
                 .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)))
            .ToList();
    }

    /// <summary>Widest first, so the list reads as a fallback chain top to bottom.</summary>
    private static int RankOf(ShortcutContext context) => context switch
    {
        ShortcutContext.Global => 0,
        ShortcutContext.Canvas => 1,
        _ => 2,
    };

    /// <summary>
    /// The rule under the heading, so it is stated rather than inferred.
    /// </summary>
    private static string HintFor(ShortcutDefinition definition) => definition.Context switch
    {
        ShortcutContext.Global =>
            "Everywhere — unless the panel under the pointer claims the key for itself.",
        ShortcutContext.Canvas =>
            "The canvas, the bars, the rail and the menu — and any panel that does not claim the key.",
        _ =>
            $"Only while the pointer is over {definition.ScopeName}, or it has the keyboard focus. "
            + "Elsewhere the key keeps its usual meaning.",
    };

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
