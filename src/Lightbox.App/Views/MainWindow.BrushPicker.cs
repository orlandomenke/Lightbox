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

/// <summary>Choosing a brush: presets, the picker, the pressure-curve editor and the tip picker.</summary>
/// <remarks>
/// Split out of <c>MainWindow.axaml.cs</c>, which was 5,544 lines. See
/// <c>docs/DESIGN-mainviewmodel-decomposition.md</c> — the view is 79% single-section
/// state over one <c>_vm</c> field, so it needed splitting rather than decomposing.
/// </remarks>
public partial class MainWindow
{
    // ---- brush presets --------------------------------------------------------

    private void OnSavePresetClicked(object? sender, RoutedEventArgs e)
    {
        var preset = _vm.SaveCurrentAsPreset(PresetNameBox.Text ?? "", SplitTags(PresetTagBox.Text));
        PresetNameBox.Text = "";
        PresetTagBox.Text = "";
        _vm.AiStatus = $"Saved brush preset “{preset.Name}”.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private void OnUpdatePresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset || !_vm.UpdateSelectedPreset()) return;
        _vm.AiStatus = $"Updated “{preset.Name}”.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private void OnRevertPresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset || !_vm.RevertBrushPreset()) return;
        _vm.AiStatus = $"“{preset.Name}” is back to the one that ships with Lightbox.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private void OnApplyTagsClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset) return;
        _vm.SetPresetTags(preset, SplitTags(PresetTagBox.Text));
        RefreshPresetPage();
    }

    private void OnDeletePresetClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedBrushPreset is not { } preset) return;
        var wasBuiltIn = preset.IsBuiltIn;
        if (!_vm.DeletePreset(preset)) return;
        _vm.AiStatus = wasBuiltIn ? $"“{preset.Name}” reverted." : $"Deleted “{preset.Name}”.";
        RefreshPresetPage();
        RefreshBrushPickerButton();
    }

    private static List<string> SplitTags(string? text) =>
        (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// The preset page reflects one preset, and which one changes under it —
    /// so it is refreshed rather than bound. Two of the four buttons only make
    /// sense some of the time, and a button that does nothing is worse than an
    /// absent one.
    /// </summary>
    private void RefreshPresetPage()
    {
        if (PresetCurrentName is null) return;

        var preset = _vm.SelectedBrushPreset;
        PresetCurrentName.Text = preset?.Name ?? "No brush chosen";
        UpdatePresetButton.IsEnabled = _vm.CanUpdateBrushPreset;
        RevertPresetButton.IsVisible = preset?.IsBuiltIn == true;
        RevertPresetButton.IsEnabled = _vm.CanRevertBrushPreset;
        DeletePresetButton.IsEnabled = preset is not null;
        DeletePresetButton.Content = preset?.IsBuiltIn == true ? "Revert this brush" : "Delete this brush";
        ApplyTagsButton.IsEnabled = preset is not null;

        PresetModifiedNote.IsVisible = _vm.BrushIsModified;
        PresetModifiedText.Text = _vm.BrushModifiedTip;

        // Only when the artist has not started typing, or refreshing would eat
        // what they were halfway through writing.
        if (!PresetTagBox.IsFocused) PresetTagBox.Text = string.Join(", ", preset?.Tags ?? []);
    }

    // ---- the brush picker -------------------------------------------------------

    private readonly HashSet<string> _brushTagFilter = new(StringComparer.OrdinalIgnoreCase);
    private bool _pickingBrush;

    private void OnBrushPickerOpen(object? sender, RoutedEventArgs e)
    {
        BuildBrushTagChips();
        RefreshBrushPresetList();
    }

    private void BuildBrushTagChips()
    {
        if (BrushTagChips is null) return;

        // Absent until there are tags. An empty strip of chips is a row of
        // nothing that says the feature is broken.
        BrushTagChips.IsVisible = _vm.BrushTagChoices.Count > 0;
        if (!BrushTagChips.IsVisible)
        {
            BrushTagChips.ItemsSource = null;
            return;
        }

        var chips = new List<Control>();
        foreach (var tag in _vm.BrushTagChoices)
        {
            var chip = new ToggleButton
            {
                Content = tag,
                FontSize = 10,
                Padding = new Thickness(6, 1),
                Margin = new Thickness(0, 0, 4, 4),
                IsChecked = _brushTagFilter.Contains(tag),
            };
            chip.IsCheckedChanged += (_, _) =>
            {
                if (chip.IsChecked == true) _brushTagFilter.Add(tag);
                else _brushTagFilter.Remove(tag);
                RefreshBrushPresetList();
            };
            chips.Add(chip);
        }
        BrushTagChips.ItemsSource = chips;
    }

    private void OnBrushFilterChanged(object? sender, TextChangedEventArgs e) => RefreshBrushPresetList();

    /// <summary>Re-run the filter. The rules themselves live in <see cref="BrushFilter"/>.</summary>
    private void RefreshBrushPresetList()
    {
        if (BrushPresetList is null) return;

        var matches = BrushFilter.Apply(_vm.BrushPresetChoices, BrushSearchBox.Text, _brushTagFilter);
        // Mapped through BrushChoice so each row carries a picture of its mark. The
        // previews are cached on the preset's id and settings, so filtering — which is
        // what somebody does constantly in here — re-uses them rather than re-rendering.
        var tiles = matches.Select(BrushChoice.For).ToList();

        _pickingBrush = true;
        BrushPresetList.ItemsSource = tiles;
        BrushPresetList.SelectedItem = tiles.FirstOrDefault(t => t.Preset.Id == _vm.SelectedBrushPreset?.Id);
        _pickingBrush = false;

        BrushFilterEmpty.IsVisible = tiles.Count == 0;
    }

    private void OnBrushPresetPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_pickingBrush || BrushPresetList?.SelectedItem is not BrushChoice { Preset: { } preset }) return;
        // ApplyPreset rather than the property, so picking the brush you are
        // already on puts it back — which is the obvious way to undo a nudge
        // once there is a dot telling you the brush has been nudged.
        _vm.ApplyPreset(preset);
        RefreshBrushPickerButton();
        RefreshPresetPage();
        if (BrushPickerButton?.Flyout is { } flyout) flyout.Hide();
    }

    private void RefreshBrushPickerButton()
    {
        if (BrushPickerName is null) return;
        BrushPickerName.Text = _vm.SelectedBrushPreset?.Name ?? "Brush";
    }

    private async void OnImportAudio(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add audio",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("WAV audio") { Patterns = ["*.wav"], MimeTypes = ["audio/wav"] },
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;

        // Reference or embed (Q57): the artist chooses where the sound lives.
        var mb = new FileInfo(path).Length / (1024.0 * 1024.0);
        var embed = await AskImportChoice(
            "Where should the sound live?",
            $"“{Path.GetFileName(path)}” — {mb:0.#} MB.",
            ("Reference the file",
             "The document stays light and the file stays editable in your audio tool. Keep them together when sharing.",
             false),
            ("Embed a copy in the document",
             mb > 10
                 ? $"Self-contained — survives being shared alone — but carries {mb:0.#} MB through every save."
                 : "Self-contained: the document survives being shared without the file beside it.",
             true));
        if (embed is not { } chosen) return;

        _vm.AiStatus = _vm.ImportAudio(path, chosen) is { } error
            ? $"Audio import failed: {error}"
            : $"Timing against “{Path.GetFileName(path)}”.";
    }

    /// <summary>
    /// A small modal: a sentence of context, one button per choice with its
    /// cost written under it, and Cancel. Returns null when dismissed.
    /// </summary>
    private async Task<T?> AskImportChoice<T>(
        string title, string subtitle, params (string Label, string Detail, T Value)[] options)
        where T : struct
    {
        T? picked = null;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        var stack = new StackPanel { Margin = new Thickness(16), Spacing = 8, MaxWidth = 440 };
        stack.Children.Add(new TextBlock
        {
            Text = subtitle,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.85,
        });
        foreach (var (label, detail, value) in options)
        {
            var button = new Button
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                        new TextBlock
                        {
                            Text = detail,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            FontSize = 11,
                            Opacity = 0.75,
                        },
                    },
                },
            };
            button.Click += (_, _) =>
            {
                picked = value;
                dialog.Close();
            };
            stack.Children.Add(button);
        }
        var cancel = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        };
        cancel.Click += (_, _) => dialog.Close();
        stack.Children.Add(cancel);
        dialog.Content = stack;
        await dialog.ShowDialog(this);
        return picked;
    }

    private async void OnImportTextureClicked(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Paper texture",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        if (SkiaSharp.SKBitmap.Decode(path) is not { } bitmap) return;

        using (bitmap)
        {
            _vm.ImportBrushTexture(Lightbox.Raster.PngCodec.Encode(bitmap));
        }
        _vm.AiStatus = $"Painting on “{Path.GetFileName(path)}”.";
        RefreshTextureNote(Path.GetFileName(path));
    }

    private void OnClearTextureClicked(object? sender, RoutedEventArgs e)
    {
        _vm.ClearBrushTexture();
        RefreshTextureNote(null);
    }

    /// <summary>
    /// Names the file the paper came from. The document keeps the pixels, so
    /// this is the artist's memory rather than a reference — the same job
    /// <c>BrushTip.Source</c> does.
    /// </summary>
    private void RefreshTextureNote(string? name)
    {
        if (TextureSourceNote is null) return;
        TextureSourceNote.IsVisible = name is not null;
        TextureSourceNote.Text = name is null ? "" : $"Paper: {name}";
    }

    // Brush import used to live here, straight off a button in the brush options: pick files,
    // parse them all on this thread, add them. That is what made the window go transparent on
    // a fifty-six brush collection, and it also left the artist with fifty-six brushes and
    // nowhere to remove them. Both halves now live in BrushLibraryWindow.

    // ---- pressure curves --------------------------------------------------------

    /// <summary>
    /// What each curve drives, and what to call it. Data rather than seven
    /// blocks of near-identical XAML, so adding a dynamic is a row here.
    /// </summary>
    private static readonly (BrushDynamic Target, string Label, string Tip, bool SmudgeOnly)[] PressureRows =
    [
        (BrushDynamic.Size, "Size", "Line thickness", false),
        (BrushDynamic.Flow, "Transparency", "Paint amount per dab — light pressure paints lighter", false),
        (BrushDynamic.Hardness, "Hardness", "Light pressure gives a softer dab edge", false),
        (BrushDynamic.Scatter, "Scatter", "How far dabs are thrown off the path", false),
        (BrushDynamic.Roundness, "Roundness", "Press harder and a flat dab spreads toward circular", false),
        (BrushDynamic.ColorRate, "Colour rate", "How much of the brush's own colour a smudge adds", true),
        (BrushDynamic.SmudgeLength, "Smudge length", "How far a smudge drags what it picked up", true),
    ];

    private bool _buildingCurves;

    /// <summary>
    /// Build the pressure page's rows. Rebuilt when the page is shown rather
    /// than bound, because a curve is edited by dragging rather than by typing
    /// and there is nothing for a two-way binding to carry.
    /// </summary>
    private void BuildPressureCurves()
    {
        if (PressureCurveRows is null) return;

        _buildingCurves = true;
        PressureCurveRows.Children.Clear();

        foreach (var (target, label, tip, smudgeOnly) in PressureRows)
        {
            if (smudgeOnly && !_vm.IsSmudgeBrush) continue;

            var driven = _vm.BrushDrives(target);

            var editor = new CurveEditor
            {
                Curve = _vm.BrushCurve(target),
                IsActive = driven,
                Width = 128,
                Height = 84,
            };
            editor.CurveChanged += curve =>
            {
                if (_buildingCurves) return;
                _vm.SetBrushCurve(target, curve);
            };

            var check = new CheckBox { Content = label, FontSize = 12, IsChecked = driven };
            check.IsCheckedChanged += (_, _) =>
            {
                if (_buildingCurves) return;
                _vm.SetBrushDrives(target, check.IsChecked == true);
                BuildPressureCurves();
            };

            var reset = new Button
            {
                Content = "Reset",
                FontSize = 11,
                IsEnabled = driven,
                [ToolTip.TipProperty] = "Back to a straight line",
            };
            reset.Click += (_, _) =>
            {
                _vm.ResetBrushCurve(target);
                BuildPressureCurves();
            };

            var side = new StackPanel { Spacing = 4, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            side.Children.Add(check);
            side.Children.Add(new TextBlock
            {
                Text = tip, FontSize = 10, Opacity = 0.6, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            side.Children.Add(reset);

            var row = new DockPanel { [ToolTip.TipProperty] = tip };
            DockPanel.SetDock(editor, Dock.Right);
            row.Children.Add(editor);
            row.Children.Add(side);
            PressureCurveRows.Children.Add(row);
        }

        _buildingCurves = false;
    }

    // ---- the tip picker ---------------------------------------------------------

    /// <summary>Fill the flyout the moment before it opens, so it is never stale.</summary>
    private void OnTipPickerOpen(object? sender, RoutedEventArgs e)
    {
        if (TipPickerList is null) return;

        var choices = new List<TipChoice> { TipChoice.Round() };
        choices.AddRange(_vm.AvailableTips().Select(TipChoice.For));

        _pickingTip = true;
        TipPickerList.ItemsSource = choices;
        TipPickerList.SelectedItem = choices.FirstOrDefault(c => c.Tip?.Id == _vm.BrushTipId) ?? choices[0];
        _pickingTip = false;
    }

    private bool _pickingTip;

    private void OnTipPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_pickingTip || TipPickerList?.SelectedItem is not TipChoice choice) return;
        _vm.SetBrushTip(choice.Tip);
        RefreshTipButton();
        if (TipPickerButton?.Flyout is { } flyout) flyout.Hide();
    }

    /// <summary>The button shows the tip itself, because that is what it is for.</summary>
    private void RefreshTipButton()
    {
        if (TipPickerName is null) return;

        var chosen = _vm.BrushTipId is { } id
            ? _vm.AvailableTips().FirstOrDefault(t => t.Id == id)
            : null;

        // Named but missing: the tip travelled into the drawing and then left
        // the library. The mark still renders — the raster is in the document —
        // so say so rather than showing "Round", which would be a lie about
        // what the brush is doing.
        TipPickerName.Text = chosen?.Name ?? (_vm.BrushTipId is null ? "Round" : "Custom");
        TipPickerThumb.Source = chosen is null ? null : TipChoice.For(chosen).Thumbnail;
    }
}
