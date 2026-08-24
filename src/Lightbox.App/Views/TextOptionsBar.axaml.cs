using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The text tool's options along the quick bar: the face, the size, the
/// spacing and the alignment. Markup only — every decision it shows belongs to
/// <c>MainViewModel.Text</c>.
/// </summary>
public partial class TextOptionsBar : UserControl
{
    public TextOptionsBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
