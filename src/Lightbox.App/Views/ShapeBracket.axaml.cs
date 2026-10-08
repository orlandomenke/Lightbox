using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The layer docker's folder-shape bracket and carve warning. Its DataContext is a LayerRow.</summary>
public partial class ShapeBracket : UserControl
{
    public ShapeBracket() => AvaloniaXamlLoader.Load(this);
}
