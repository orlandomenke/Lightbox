using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The effects docker: stacks and adjustment layers (Q151).</summary>
public partial class EffectsPanel : UserControl
{
    public EffectsPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
