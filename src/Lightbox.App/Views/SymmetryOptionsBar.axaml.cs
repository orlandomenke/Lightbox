using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The symmetry toggle and its fields along the quick bar, for the brush and
/// the eraser. Markup only — every decision it shows belongs to
/// <c>MainViewModel.Symmetry</c>.
/// </summary>
public partial class SymmetryOptionsBar : UserControl
{
    public SymmetryOptionsBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
