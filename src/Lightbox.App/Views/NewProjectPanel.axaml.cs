using Avalonia.Controls;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>What File → New project collected.</summary>
public sealed record NewProjectSettings(string Name, ProjectType? Type, WorkspaceChoice Workspace);

/// <summary>
/// The project-type picker. Separate from the document panel because the two
/// answer different questions: File → New asks what one drawing is, and only
/// mentions a type because it decides which panels you get. This asks what a
/// container is, and the answer is written into the manifest.
/// </summary>
public partial class NewProjectPanel : UserControl
{
    private sealed record TypeChoice(string Label, ProjectType? Type)
    {
        public override string ToString() => Label;
    }

    private static readonly TypeChoice[] Types =
    [
        new("Unset", null),
        new("Illustration", ProjectType.Illustration),
        new("Animation", ProjectType.Animation),
        new("Game art", ProjectType.GameArt),
        new("Storyboard", ProjectType.Storyboard),
        new("Comic", ProjectType.Comic),
        new("Asset library — characters other projects import", ProjectType.AssetLibrary),
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

    public NewProjectPanel()
    {
        InitializeComponent();
        TypeBox.ItemsSource = Types;
        TypeBox.SelectedIndex = 2;   // Animation: what this app is mostly for
        WorkspaceBox.ItemsSource = PanelChoices;
        WorkspaceBox.SelectedIndex = 0;
    }

    /// <summary>Offer a name the artist is likely to want.</summary>
    public void Suggest(string name) => NameBox.Text = name;

    public NewProjectSettings Collect() => new(
        string.IsNullOrWhiteSpace(NameBox.Text) ? "Project" : NameBox.Text.Trim(),
        (TypeBox.SelectedItem as TypeChoice)?.Type,
        (WorkspaceBox.SelectedItem as PanelChoice)?.Choice ?? WorkspaceChoice.Keep);
}
