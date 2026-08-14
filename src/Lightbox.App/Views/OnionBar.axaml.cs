using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>Onion skin's controls, shared by the timeline family's bars.</summary>
public partial class OnionBar : UserControl
{
    public OnionBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
