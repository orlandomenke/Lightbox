using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lightbox.App.Controls;
using Lightbox.App.ViewModels;
using Lightbox.Core.Documents;
using Lightbox.Raster;
using SkiaSharp;

namespace Lightbox.App.Views;

/// <summary>
/// The brush editor (Q211): the preset whole, in a popup from the bar's gear.
/// See the remarks at the top of BrushEditor.axaml for its four parts.
/// </summary>
/// <remarks>
/// <para>
/// <b>An option's check reads the record rather than a flag of its own.</b> The
/// brush has no per-option "enabled" field, and adding one would write a key
/// into every saved brush. So a check is on when the option changes the mark at
/// all; turning it off sets the option's values to the ones that leave the mark
/// alone and keeps the old ones for the session, so turning it back on restores
/// them. Reload brings back what was saved.
/// </para>
/// <para>
/// Moved out of MainWindow.axaml, where the same settings were five pages of
/// the Tool options docker (General, Effects, Medium, Pen pressure, Presets).
/// </para>
/// </remarks>
public partial class BrushEditor : UserControl
{
    /// <summary>One entry in the option list.</summary>
    /// <param name="Name">What the list calls it.</param>
    /// <param name="Panel">The x:Name of its panel.</param>
    /// <param name="Locked">Every brush has one: shown checked, never unchecked.</param>
    /// <param name="Off">The view-model properties it owns and the value of each that leaves the mark alone.</param>
    /// <param name="Curves">The pen-pressure curves shown beside it, and the panel they go in.</param>
    /// <param name="Shown">Whether it applies to this brush at all (Smudge is for smudge brushes).</param>
    internal sealed record Option(
        string Name,
        string Panel,
        bool Locked,
        IReadOnlyDictionary<string, object?> Off,
        (BrushDynamic Target, string Label)[] Curves,
        string? CurvePanel = null,
        Func<MainViewModel, bool>? Shown = null);

    private static readonly Dictionary<string, object?> None = [];

    internal static readonly Option[] Options =
    [
        new("Tip", "OptionTip", true, None, [(BrushDynamic.Hardness, "Hardness")], "CurvesTip"),
        new("Size", "OptionSize", true, None, [(BrushDynamic.Size, "Size")], "CurvesSize"),
        new("Opacity", "OptionOpacity", true, None, []),
        new("Flow", "OptionFlow", true, None, [(BrushDynamic.Flow, "Flow")], "CurvesFlow"),
        new("Spacing", "OptionSpacing", true, None, []),
        new("Shape dynamics", "OptionShape", false,
            new Dictionary<string, object?> { [nameof(MainViewModel.BrushRoundnessJitter)] = 0.0, [nameof(MainViewModel.BrushRotationJitter)] = 0.0 },
            [(BrushDynamic.Roundness, "Roundness")], "CurvesShape"),
        new("Scatter", "OptionScatter", false,
            new Dictionary<string, object?> { [nameof(MainViewModel.BrushScatter)] = 0.0 },
            [(BrushDynamic.Scatter, "Scatter")], "CurvesScatter"),
        new("Texture", "OptionTexture", false,
            new Dictionary<string, object?>
            {
                [nameof(MainViewModel.BrushTextureSurface)] = "None",
                [nameof(MainViewModel.BrushGranulation)] = 0.0,
                [nameof(MainViewModel.BrushWetEdge)] = 0.0,
            },
            []),
        new("Colour dynamics", "OptionColour", false,
            new Dictionary<string, object?>
            {
                [nameof(MainViewModel.BrushSecondaryColor)] = "",
                [nameof(MainViewModel.BrushColorJitter)] = 0.0,
                [nameof(MainViewModel.BrushHueJitter)] = 0.0,
                [nameof(MainViewModel.BrushSaturationJitter)] = 0.0,
                [nameof(MainViewModel.BrushBrightnessJitter)] = 0.0,
            },
            []),
        new("Smudge", "OptionSmudge", true, None,
            [(BrushDynamic.ColorRate, "Colour rate"), (BrushDynamic.SmudgeLength, "Length")], "CurvesSmudge",
            Shown: vm => vm.IsSmudgeBrush),
        new("Medium", "OptionMedium", false,
            new Dictionary<string, object?> { [nameof(MainViewModel.BrushMedium)] = MediumKind.None },
            []),
        new("Blend", "OptionBlend", true, None, []),
    ];

    /// <summary>
    /// What an option held before its check was turned off, for this session,
    /// filed under the brush it belongs to — the tool (brush and eraser keep
    /// separate settings) and the preset. A nudged brush stays nudged across a
    /// switch to another and back, so what its unticked options held has to
    /// survive the same switch (the adversary review found the first version
    /// dropping it on any change of brush).
    /// </summary>
    private readonly Dictionary<string, (Dictionary<string, object?> Values, Dictionary<BrushDynamic, ResponseCurve?> Curves)> _kept = [];

    private string BrushKey => $"{(_vm?.IsEraserTool == true ? "eraser" : "brush")}|{_vm?.SelectedBrushPreset?.Id}";

    private string KeptKey(Option option) => $"{BrushKey}|{option.Name}";

    private MainViewModel? _vm;
    private bool _syncing;
    private bool _refreshQueued;

    /// <summary>Raised when presets were saved, overwritten, deleted or managed, so the bar's picker follows.</summary>
    public event Action? PresetsChanged;

    public BrushEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        Scratchpad.Settings = () => _vm?.CurrentBrushCopy() ?? new BrushSettings();
        Scratchpad.Colour = () => _vm?.ColorHex ?? "#1a1a1a";
    }

    private void Attach(MainViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = vm;
        if (_vm is null) return;
        _vm.PropertyChanged += OnViewModelChanged;
        BuildOptionList();
        OptionList.SelectedIndex = 0;
        RefreshAll();
    }

    /// <summary>The option list, rebuilt when the set of options that apply changes.</summary>
    private void BuildOptionList()
    {
        if (_vm is null) return;
        var keep = (OptionList.SelectedItem as ListBoxItem)?.Tag as Option;
        _syncing = true;
        var items = new List<ListBoxItem>();
        foreach (var option in Options.Where(o => o.Shown?.Invoke(_vm) ?? true))
        {
            var check = new CheckBox
            {
                IsChecked = option.Locked || InUse(option),
                IsEnabled = !option.Locked && CanTurnOff(option),
                Padding = default,
                // The name's breath from the box: GapAfter's 4, plus the box's own.
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = option,
            };
            ToolTip.SetTip(check, option.Locked ? "Every brush has one" : $"Whether {option.Name.ToLowerInvariant()} changes the mark at all");
            check.IsCheckedChanged += (_, _) =>
            {
                if (_syncing) return;
                SetInUse(option, check.IsChecked == true);
            };
            var label = new TextBlock { Text = option.Name, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel();
            DockPanel.SetDock(check, Dock.Left);
            row.Children.Add(check);
            row.Children.Add(label);
            items.Add(new ListBoxItem { Content = row, Tag = option });
        }
        OptionList.ItemsSource = items;
        OptionList.SelectedItem = items.FirstOrDefault(i => ReferenceEquals(i.Tag, keep)) ?? items.FirstOrDefault();
        _syncing = false;
        ShowOption((OptionList.SelectedItem as ListBoxItem)?.Tag as Option);
    }

    // ---- an option's check -----------------------------------------------------------

    /// <summary>Whether an option changes the mark at all, read off the record.</summary>
    internal bool InUse(Option option)
    {
        if (_vm is null) return false;
        if (option.Name == "Texture" && _vm.HasImportedTexture) return true;
        foreach (var (name, off) in option.Off)
        {
            if (!Equals(Read(name), off)) return true;
        }
        return option.Curves.Any(c => _vm.BrushDrives(c.Target));
    }

    /// <summary>
    /// An imported paper is a picture in the drawing rather than a value, so the
    /// check cannot turn it off and give it back; the ✕ beside it removes it.
    /// </summary>
    private bool CanTurnOff(Option option) => !(option.Name == "Texture" && _vm?.HasImportedTexture == true);

    internal void SetInUse(Option option, bool on)
    {
        if (_vm is null || option.Locked) return;
        if (!on)
        {
            if (!InUse(option)) return;
            _kept[KeptKey(option)] = (
                option.Off.Keys.ToDictionary(k => k, Read),
                option.Curves.ToDictionary(c => c.Target, c => _vm.BrushDrives(c.Target) ? _vm.BrushCurve(c.Target) : null));
            foreach (var (name, off) in option.Off) Write(name, off);
            foreach (var (target, _) in option.Curves) _vm.SetBrushDrives(target, false);
        }
        else if (_kept.Remove(KeptKey(option), out var kept))
        {
            foreach (var (name, value) in kept.Values) Write(name, value);
            foreach (var (target, curve) in kept.Curves)
            {
                if (curve is not null) _vm.SetBrushCurve(target, curve);
            }
        }
        // Turned on with nothing kept: the option's panel is where its values
        // are set, so it is opened rather than guessed at.
        SelectOption(option);
        QueueRefresh();
    }

    private object? Read(string property) =>
        typeof(MainViewModel).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetValue(_vm);

    private void Write(string property, object? value) =>
        typeof(MainViewModel).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(_vm, value);

    // ---- choosing an option ----------------------------------------------------------

    private void OnOptionChosen(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        ShowOption((OptionList.SelectedItem as ListBoxItem)?.Tag as Option);
    }

    internal void SelectOption(Option option)
    {
        var item = (OptionList.ItemsSource as IEnumerable<ListBoxItem>)?.FirstOrDefault(i => ReferenceEquals(i.Tag, option));
        if (item is not null) OptionList.SelectedItem = item;
    }

    /// <summary>The name of the panel on show, for tests.</summary>
    internal string? ShownPanel =>
        OptionPanels.Children.OfType<Control>().FirstOrDefault(c => c.IsVisible)?.Name;

    private void ShowOption(Option? option)
    {
        foreach (var panel in OptionPanels.Children.OfType<Control>())
        {
            panel.IsVisible = option is not null && panel.Name == option.Panel;
        }
        if (option is null) return;
        if (option.Name == "Tip") RefreshTipButton();
        BuildCurves(option);
    }

    // ---- pen-pressure curves, beside the value they drive ---------------------------

    private bool _buildingCurves;

    /// <summary>
    /// The chosen option's curves. Rebuilt when shown rather than bound: a curve
    /// is edited by dragging, and there is nothing for a two-way binding to carry.
    /// </summary>
    private void BuildCurves(Option option)
    {
        if (_vm is null || option.CurvePanel is null || this.FindControl<StackPanel>(option.CurvePanel) is not { } host) return;
        _buildingCurves = true;
        host.Children.Clear();
        host.IsEnabled = _vm.BrushPressureEnabled;
        host.Children.Add(new TextBlock { Classes = { "subhead" }, Text = "Driven by pen pressure" });
        foreach (var (target, label) in option.Curves)
        {
            var driven = _vm.BrushDrives(target);
            var check = new CheckBox { Content = option.Curves.Length > 1 ? label : "Pressure", IsChecked = driven };
            check.IsCheckedChanged += (_, _) =>
            {
                if (_buildingCurves) return;
                _vm.SetBrushDrives(target, check.IsChecked == true);
                BuildCurves(option);
            };
            var reset = new Button { Classes = { "text", "ghost" }, Content = "Straight", IsEnabled = driven };
            ToolTip.SetTip(reset, "Back to a straight line: pressure in, the same amount out");
            reset.Click += (_, _) =>
            {
                _vm.ResetBrushCurve(target);
                BuildCurves(option);
            };
            var head = new DockPanel();
            DockPanel.SetDock(reset, Dock.Right);
            head.Children.Add(reset);
            head.Children.Add(check);

            var editor = new CurveEditor
            {
                Curve = _vm.BrushCurve(target),
                IsActive = driven,
                Height = 110,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            editor.CurveChanged += curve =>
            {
                if (_buildingCurves) return;
                _vm.SetBrushCurve(target, curve);
            };
            host.Children.Add(head);
            host.Children.Add(editor);
        }
        host.Children.Add(new TextBlock
        {
            Classes = { "note" },
            Text = _vm.BrushPressureEnabled
                ? "Light pressure at the left, full at the right. Drag a point; click for a new one, middle-click to remove."
                : "Pen pressure is off for this brush (the switch under the list).",
        });
        _buildingCurves = false;
    }

    // ---- keeping up with the brush --------------------------------------------------

    private static readonly HashSet<string> Structural =
    [
        nameof(MainViewModel.ActiveTool), nameof(MainViewModel.SelectedBrushPreset), nameof(MainViewModel.IsSmudgeBrush),
    ];

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A different brush rebuilds the list; what was kept stays filed under
        // the brush it came from, for when that brush comes back.
        if (e.PropertyName is { } name && Structural.Contains(name)) BuildOptionList();
        QueueRefresh();
    }

    /// <summary>Many properties change at once when a preset is applied; draw once for all of them.</summary>
    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            RefreshAll();
        }, DispatcherPriority.Background);
    }

    private void RefreshAll()
    {
        if (_vm is null) return;
        RefreshHeader();
        RefreshChecks();
        RefreshTipButton();
        RefreshTextureNote();
        Scratchpad.Redraw();
    }

    private void RefreshChecks()
    {
        if (OptionList.ItemsSource is not IEnumerable<ListBoxItem> items) return;
        _syncing = true;
        foreach (var item in items)
        {
            if (item.Content is DockPanel { Children: [CheckBox check, ..] } && item.Tag is Option option)
            {
                check.IsChecked = option.Locked || InUse(option);
                check.IsEnabled = !option.Locked && CanTurnOff(option);
            }
        }
        _syncing = false;
    }

    private void RefreshHeader()
    {
        if (_vm is null) return;
        var preset = _vm.SelectedBrushPreset;
        PresetNameText.Text = preset?.Name ?? "Unsaved brush";
        ToolTip.SetTip(PresetStatusText, _vm.BrushIsModified ? _vm.BrushModifiedTip : null);
        PresetStatusText.Text = _vm.BrushIsModified
            ? "Changed since it was saved"
            : preset is null ? "Not saved as a brush yet" : string.Join(" · ", preset.Tags is { Count: > 0 } t ? t : ["Saved"]);
        ReloadButton.IsEnabled = preset is not null && _vm.BrushIsModified;
        OverwriteButton.IsEnabled = _vm.CanUpdateBrushPreset;
        DeletePresetMenuItem.IsEnabled = preset is not null;
        DeletePresetMenuItem.Header = preset?.IsBuiltIn == true ? "Back to the shipped brush" : "Delete this brush";
        TagsMenuItem.IsEnabled = preset is not null;
        PreviewImage.Source = Picture(BrushPreviewRenderer.Render(_vm.CurrentBrushCopy(), _vm.ColorHex, 112, 40));
    }

    private static Bitmap Picture(SKBitmap mark)
    {
        using (mark)
        using (var image = SKImage.FromBitmap(mark))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;
            return new Bitmap(stream);
        }
    }

    // ---- the save verbs -------------------------------------------------------------

    private void OnReloadClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedBrushPreset is not { } preset) return;
        // Picking the brush you are on puts it back — the same call the picker makes.
        var reloaded = BrushKey + "|";
        _vm.ApplyPreset(preset);
        // The saved brush is back, so what its unticked options held goes with the tweaks.
        foreach (var key in _kept.Keys.Where(k => k.StartsWith(reloaded, StringComparison.Ordinal)).ToList()) _kept.Remove(key);
        _vm.AiStatus = $"“{preset.Name}” is back as it was saved.";
        Changed();
    }

    private void OnOverwriteClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedBrushPreset is not { } preset || !_vm.UpdateSelectedPreset()) return;
        _vm.AiStatus = $"Updated “{preset.Name}”.";
        Changed();
    }

    private void OnSavePresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var preset = _vm.SaveCurrentAsPreset(PresetNameBox.Text ?? "", SplitTags(PresetTagBox.Text));
        PresetNameBox.Text = "";
        PresetTagBox.Text = "";
        SaveNewButton.Flyout?.Hide();
        _vm.AiStatus = $"Saved brush preset “{preset.Name}”.";
        Changed();
    }

    private void OnTagsClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedBrushPreset is not { } preset) return;
        var box = new TextBox { Text = string.Join(", ", preset.Tags ?? []), Width = 240, PlaceholderText = "Tags, comma separated" };
        var apply = new Button { Classes = { "text", "primary" }, Content = "Tag", HorizontalAlignment = HorizontalAlignment.Right };
        var flyout = new Flyout { Content = new StackPanel { Spacing = 6, Children = { box, apply } } };
        apply.Click += (_, _) =>
        {
            _vm.SetPresetTags(preset, SplitTags(box.Text));
            flyout.Hide();
            Changed();
        };
        flyout.ShowAt(MoreButton);
    }

    private void OnDeletePresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm?.SelectedBrushPreset is not { } preset) return;
        var wasBuiltIn = preset.IsBuiltIn;
        if (!_vm.DeletePreset(preset)) return;
        _vm.AiStatus = wasBuiltIn ? $"“{preset.Name}” is back to the one that ships with Lightbox." : $"Deleted “{preset.Name}”.";
        Changed();
    }

    private async void OnBrushLibraryClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        await new BrushLibraryWindow(_vm).ShowDialog(owner);
        Changed();
    }

    private async void OnBrushTipsClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (TipPickerButton.Flyout is { } picker) picker.Hide();
        await new BrushTipsWindow(_vm).ShowDialog(owner);
        RefreshTipButton();
    }

    private void Changed()
    {
        QueueRefresh();
        PresetsChanged?.Invoke();
    }

    private static List<string> SplitTags(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void OnClearScratchpadClicked(object? sender, RoutedEventArgs e) => Scratchpad.Clear();

    // ---- texture ----------------------------------------------------------------------

    private async void OnImportTextureClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Paper texture",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        if (SKBitmap.Decode(path) is not { } bitmap) return;
        using (bitmap)
        {
            _vm.ImportBrushTexture(PngCodec.Encode(bitmap));
        }
        _vm.AiStatus = $"Painting on “{Path.GetFileName(path)}”.";
        _textureName = Path.GetFileName(path);
        QueueRefresh();
    }

    private void OnClearTextureClicked(object? sender, RoutedEventArgs e)
    {
        _vm?.ClearBrushTexture();
        _textureName = null;
        QueueRefresh();
    }

    /// <summary>
    /// The file the paper came from. The document keeps the pixels, so this is
    /// the artist's memory rather than a reference.
    /// </summary>
    private string? _textureName;

    private void RefreshTextureNote()
    {
        var imported = _vm?.HasImportedTexture == true;
        ClearTextureButton.IsVisible = imported;
        TextureSourceNote.IsVisible = imported;
        TextureSourceNote.Text = imported ? $"Paper: {_textureName ?? "an imported image"}" : "";
    }

    // ---- the tip picker -------------------------------------------------------------

    private bool _pickingTip;

    /// <summary>Fill the flyout the moment before it opens, so it is never stale.</summary>
    private void OnTipPickerOpen(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var choices = new List<TipChoice> { TipChoice.Round() };
        choices.AddRange(_vm.AvailableTips().Select(TipChoice.For));
        _pickingTip = true;
        TipPickerList.ItemsSource = choices;
        TipPickerList.SelectedItem = choices.FirstOrDefault(c => c.Tip?.Id == _vm.BrushTipId) ?? choices[0];
        _pickingTip = false;
    }

    private void OnTipPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || _pickingTip || TipPickerList.SelectedItem is not TipChoice choice) return;
        _vm.SetBrushTip(choice.Tip);
        RefreshTipButton();
        TipPickerButton.Flyout?.Hide();
    }

    /// <summary>The button shows the tip itself, because that is what it is for.</summary>
    private void RefreshTipButton()
    {
        if (_vm is null) return;
        var chosen = _vm.BrushTipId is { } id ? _vm.AvailableTips().FirstOrDefault(t => t.Id == id) : null;
        // Named but missing: the tip travelled into the drawing and then left
        // the library. The mark still renders, so say so rather than "Round".
        TipPickerName.Text = chosen?.Name ?? (_vm.BrushTipId is null ? "Round" : "Custom");
        TipPickerThumb.Source = chosen is null ? null : TipChoice.For(chosen).Thumbnail;
    }
}
