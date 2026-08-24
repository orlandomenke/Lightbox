using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The Fill tool's options along the quick bar. Markup only — every decision it
/// shows belongs to <c>MainViewModel.Fill</c>.
/// </summary>
public partial class FillOptionsBar : UserControl
{
    public FillOptionsBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
