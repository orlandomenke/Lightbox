using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The text tool's options with room for labels, in the Tool options docker.
/// </summary>
/// <remarks>
/// The roomy half of the pair; <see cref="TextOptionsBar"/> is the horizontal
/// glance. Markup only — every decision it shows belongs to
/// <c>MainViewModel.Text</c>.
/// </remarks>
public partial class TextOptionsPanel : UserControl
{
    public TextOptionsPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
