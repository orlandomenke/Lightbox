using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The playback transport, shared by the timeline family's bars.</summary>
public partial class TransportBar : UserControl
{
    public TransportBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
