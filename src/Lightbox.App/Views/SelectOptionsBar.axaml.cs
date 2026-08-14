using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The Select tool's options along the quick bar: the marquee shape, the wand's
/// settings, feather, grow and shrink, and the selection actions. Markup only —
/// every decision it shows belongs to <c>MainViewModel.Selection</c>.
/// </summary>
public partial class SelectOptionsBar : UserControl
{
    public SelectOptionsBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
