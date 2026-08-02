using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lightbox.App.Views;

/// <summary>
/// File → New settings, Photoshop/Krita style but digital-first (no ICC
/// profiles, RGB 8-bit fixed). Returns a <see cref="ViewModels.NewDocumentSettings"/>
/// via ShowDialog, or null on cancel.
/// </summary>
/// <remarks>
/// A frame around <see cref="NewDocumentPanel"/>, which is where the fields
/// live — the start screen offers the same ones without a dialog around them.
/// </remarks>
public partial class NewDocumentDialog : Window
{
    public NewDocumentDialog() => InitializeComponent();

    private void OnCreateClicked(object? sender, RoutedEventArgs e) => Close(Fields.Collect());

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}
