using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// <c>File ▸ Version history…</c> — one resource's versions, with revert. The
/// window is a shell over <see cref="VersionHistoryViewModel"/>, which owns
/// every decision; the same window serves documents and character sheets
/// because the view model does not care which it was handed.
/// </summary>
public partial class VersionHistoryWindow : Window
{
    public VersionHistoryWindow()
    {
        InitializeComponent();
    }

    public VersionHistoryWindow(VersionHistoryViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
