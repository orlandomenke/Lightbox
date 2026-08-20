using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The Scene panel: layer depths and the camera's path (Q84).</summary>
public partial class ScenePanel : UserControl
{
    public ScenePanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
