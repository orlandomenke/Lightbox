using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lightbox.App.Views;

/// <summary>
/// The crop frame's options bar. Its two buttons forward to the window, which
/// is where the apply lives — the crop refits the view afterwards, and the view
/// is the window's.
/// </summary>
/// <remarks>
/// Walking up to the window rather than reaching for the view model directly,
/// so the button and the Enter key take the same route and cannot drift about
/// what "apply" includes.
/// </remarks>
public partial class CropOptionsBar : UserControl
{
    public CropOptionsBar()
    {
        InitializeComponent();
    }

    private void OnCropApplyClicked(object? sender, RoutedEventArgs e) =>
        this.FindAncestorOfType<MainWindow>()?.ApplyCropFrameFromBar();

    private void OnCropResetClicked(object? sender, RoutedEventArgs e) =>
        this.FindAncestorOfType<MainWindow>()?.CancelCropFrameFromBar();
}
