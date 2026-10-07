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
}
