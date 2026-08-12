using Avalonia.Controls;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>
/// The fields of File → New, without the buttons that dismiss anything.
/// </summary>
/// <remarks>
/// A control rather than a dialog because the same fields now appear in two
/// places — the dialog and the start screen. Two copies would be two places to
/// add a preset to, and the one that was missed would be the one somebody used.
/// </remarks>
public partial class NewDocumentPanel : UserControl
{
    private sealed record Preset(string Label, int Width, int Height)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// The seven-way picker. <c>None</c> is a real answer, not an absence of
    /// one — it is what makes File → New still mean "one drawing".
    /// </summary>
    private sealed record TypeChoice(string Label, ProjectType? Type)
    {
        public override string ToString() => Label;
    }

    private static readonly TypeChoice[] Types =
    [
        new("None — a single file", null),
        new("Illustration", ProjectType.Illustration),
        new("Animation", ProjectType.Animation),
        new("Game art", ProjectType.GameArt),
        new("Storyboard", ProjectType.Storyboard),
        new("Comic", ProjectType.Comic),
        new("Asset library", ProjectType.AssetLibrary),
    ];

    private sealed record PanelChoice(string Label, WorkspaceChoice Choice)
    {
        public override string ToString() => Label;
    }

    private static readonly PanelChoice[] PanelChoices =
    [
        new("Keep the current arrangement", WorkspaceChoice.Keep),
        new("Use this type's defaults", WorkspaceChoice.ProjectDefaults),
    ];

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

    public NewDocumentPanel()
    {
        InitializeComponent();
        PresetBox.ItemsSource = Presets;
        PresetBox.SelectedIndex = 3; // Full HD default
        TypeBox.ItemsSource = Types;
        TypeBox.SelectedIndex = 0;   // None
        WorkspaceBox.ItemsSource = PanelChoices;
        WorkspaceBox.SelectedIndex = 0;
    }

    private void OnSwapSizeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SwapSize();

    /// <summary>
    /// Trade width for height — portrait to landscape in one gesture. The
    /// preset stays where it was, like any other hand edit of the fields.
    /// </summary>
    public void SwapSize() => (WidthBox.Value, HeightBox.Value) = (HeightBox.Value, WidthBox.Value);

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is not Preset { Width: > 0 } preset) return;
        WidthBox.Value = preset.Width;
        HeightBox.Value = preset.Height;
    }

    private void OnTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Nothing to offer for None: there is no type whose defaults to take,
        // and an option that cannot change anything is worse than no option.
        WorkspaceRow.IsVisible = TypeBox.SelectedItem is TypeChoice { Type: not null };
    }

    /// <summary>What the fields currently say.</summary>
    public NewDocumentSettings Collect()
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled" : NameBox.Text.Trim();
        var background = Services.ColorSpace.HexToRgb(BackgroundField.Hex ?? "") is null
            ? "#ffffff"
            : BackgroundField.Hex!.Trim().ToLowerInvariant();
        return new NewDocumentSettings(
            name,
            (int)(WidthBox.Value ?? 1920),
            (int)(HeightBox.Value ?? 1080),
            (int)(FpsBox.Value ?? 12),
            (int)(PpiBox.Value ?? 72),
            background,
            TransparentBox.IsChecked == true,
            (TypeBox.SelectedItem as TypeChoice)?.Type,
            WorkspaceRow.IsVisible && WorkspaceBox.SelectedItem is PanelChoice choice
                ? choice.Choice
                : WorkspaceChoice.Keep);
    }
}
