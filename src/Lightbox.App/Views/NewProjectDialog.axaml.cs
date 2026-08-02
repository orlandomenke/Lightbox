using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lightbox.App.Views;

/// <summary>
/// A frame around <see cref="NewProjectPanel"/>. The fields live there because
/// the start screen offers the same ones without a dialog around them.
/// </summary>
public partial class NewProjectDialog : Window
{
    public NewProjectDialog() : this("Project")
    {
    }

    public NewProjectDialog(string suggestedName)
    {
        InitializeComponent();
        Fields.Suggest(suggestedName);
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e) => Close(Fields.Collect());

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
