using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The layer docker's link bracket. Its DataContext is a LayerRow.</summary>
public partial class LinkBracket : UserControl
{
    public LinkBracket() => AvaloniaXamlLoader.Load(this);
}
