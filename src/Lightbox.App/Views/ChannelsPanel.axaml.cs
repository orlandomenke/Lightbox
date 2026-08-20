using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The Channels docker's tiles — extracted for the ratchet's reason; see the axaml.</summary>
public partial class ChannelsPanel : UserControl
{
    public ChannelsPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
