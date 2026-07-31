using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// File → New settings, Photoshop/Krita style but digital-first (no ICC
/// profiles, RGB 8-bit fixed). Returns a <see cref="NewDocumentSettings"/>
/// via ShowDialog, or null on cancel.
/// </summary>
public partial class NewDocumentDialog : Window
{
    private sealed record Preset(string Label, int Width, int Height)
    {
        public override string ToString() => Label;
    }

    private static readonly Preset[] Presets =
    [
        new("Custom", 0, 0),
        new("Pencil test (960 × 540)", 960, 540),
        new("HD 720p (1280 × 720)", 1280, 720),
        new("Full HD 1080p (1920 × 1080)", 1920, 1080),
        new("4K UHD (3840 × 2160)", 3840, 2160),
        new("Square (1080 × 1080)", 1080, 1080),
        new("Portrait (1080 × 1920)", 1080, 1920),
    ];

    public NewDocumentDialog()
    {
        InitializeComponent();
        PresetBox.ItemsSource = Presets;
        PresetBox.SelectedIndex = 3; // Full HD default
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not Preset { Width: > 0 } preset) return;
        WidthBox.Value = preset.Width;
        HeightBox.Value = preset.Height;
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled" : NameBox.Text.Trim();
        var background = Services.ColorSpace.HexToRgb(BackgroundBox.Text ?? "") is null
            ? "#ffffff"
            : BackgroundBox.Text!.Trim().ToLowerInvariant();
        Close(new NewDocumentSettings(
            name,
            (int)(WidthBox.Value ?? 1920),
            (int)(HeightBox.Value ?? 1080),
            (int)(FpsBox.Value ?? 12),
            (int)(PpiBox.Value ?? 72),
            background,
            TransparentBox.IsChecked == true));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
