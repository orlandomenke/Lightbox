using Avalonia.Controls;
using Avalonia.Interactivity;
using Lightbox.App.ViewModels;

namespace Lightbox.App.Views;

/// <summary>
/// Dialog for choosing whether to import multi-frame symbol as expanded frames
/// or as a single animated reference.
/// </summary>
public partial class PlacementChoiceDialog : Window
{
    public PlacementChoiceDialog()
    {
        InitializeComponent();
    }

    private void OnPlaceClicked(object? sender, RoutedEventArgs e)
    {
        var choice = ImportOption.IsChecked == true
            ? FrameImportChoice.ImportFrames
            : FrameImportChoice.Reference;
        Close(new PlacementChoice { Choice = choice, DontAskAgain = DontAskAgain.IsChecked == true });
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);
}

/// <summary>User's response to the placement choice dialog.</summary>
public sealed class PlacementChoice
{
    public FrameImportChoice Choice { get; set; }
    public bool DontAskAgain { get; set; }
}
