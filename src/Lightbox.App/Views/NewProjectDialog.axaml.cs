using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.ViewModels;
using Lightbox.Core.Projects;

namespace Lightbox.App.Views;

/// <summary>What File → New project collected.</summary>
public sealed record NewProjectSettings(string Name, ProjectType? Type, WorkspaceChoice Workspace);

/// <summary>
/// The project-type picker. Separate from the document dialog because the two
/// answer different questions: File → New asks what one drawing is, and only
/// mentions a type because it decides which panels you get. This asks what a
/// container is, and the answer is written into the manifest.
/// </summary>
public partial class NewProjectDialog : Window
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

    public NewProjectDialog() : this("Project")
    {
    }

    public NewProjectDialog(string suggestedName)
    {
        InitializeComponent();
        NameBox.Text = suggestedName;
        TypeBox.ItemsSource = Types;
        TypeBox.SelectedIndex = 2;   // Animation: what this app is mostly for
        WorkspaceBox.ItemsSource = PanelChoices;
        WorkspaceBox.SelectedIndex = 0;
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Project" : NameBox.Text.Trim();
        Close(new NewProjectSettings(
            name,
            (TypeBox.SelectedItem as TypeChoice)?.Type,
            (WorkspaceBox.SelectedItem as PanelChoice)?.Choice ?? WorkspaceChoice.Keep));
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
