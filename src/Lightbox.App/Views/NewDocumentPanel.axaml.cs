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
    /// <summary>
    /// What a new document's resolution says, in pixels per inch.
    /// </summary>
    /// <remarks>
    /// <b>Q183.</b> 72 was the screen convention of a generation ago and is a
    /// number nothing prints at — it made every HD canvas claim to be 26 inches
    /// wide. 180 is a real print resolution and puts 1920 × 1080 at roughly
    /// 10.7 × 6 inches, which is a sketchbook page rather than a broadsheet.
    /// PPI is metadata today, so this decides only what the document *claims*
    /// its physical size is; the pixels are unchanged either way.
    /// </remarks>
    public const int DefaultPpi = 180;

    /// <summary>
    /// The paper a new document opens on: mid grey, half way between black and
    /// white in 8-bit sRGB value.
    /// </summary>
    /// <remarks>
    /// <b>Q183.</b> White paper is the animator's default and grey is the
    /// painter's — both are first-class purposes here, and grey was chosen for
    /// both rather than being made to follow the <em>For</em> box, so the
    /// Background field never changes under you while you are filling the form
    /// in. An animator wanting white sets it once and every preset keeps it.
    ///
    /// <c>#808080</c> is 128/255 — mid grey by *value*, which is what an artist
    /// means by "50% grey" and what a fill of 50% grey gives them elsewhere.
    /// Mid grey by luminance would be nearer <c>#bcbcbc</c> and is not what
    /// anybody asks for.
    /// </remarks>
    public const string DefaultBackground = "#808080";

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
        // Set here rather than in the XAML so the constants above are the only
        // statement of what a new document opens as — the literal in the markup
        // and the fallback in Collect() were two places to change and one to
        // forget.
        PpiBox.Value = DefaultPpi;
        BackgroundField.Hex = DefaultBackground;
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
        // A field holding something that is not a colour falls back to the
        // default paper, not to white: white would be a third answer, neither
        // what the field says nor what the panel offers.
        var background = Services.ColorSpace.HexToRgb(BackgroundField.Hex ?? "") is null
            ? DefaultBackground
            : BackgroundField.Hex!.Trim().ToLowerInvariant();
        return new NewDocumentSettings(
            name,
            (int)(WidthBox.Value ?? 1920),
            (int)(HeightBox.Value ?? 1080),
            (int)(FpsBox.Value ?? 12),
            (int)(PpiBox.Value ?? DefaultPpi),
            background,
            TransparentBox.IsChecked == true,
            (TypeBox.SelectedItem as TypeChoice)?.Type,
            WorkspaceRow.IsVisible && WorkspaceBox.SelectedItem is PanelChoice choice
                ? choice.Choice
                : WorkspaceChoice.Keep);
    }
}
