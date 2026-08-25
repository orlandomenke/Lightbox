using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The size and alignment half of the text tool's quick options — see
/// <see cref="TextOptionsBar"/> for why it is a second control.
/// </summary>
public partial class TextFormatBar : UserControl
{
    public TextFormatBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
