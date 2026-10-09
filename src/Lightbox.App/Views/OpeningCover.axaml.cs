using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The canvas's cover while a document is read from disk (Q229). Its DataContext is the MainViewModel.</summary>
public partial class OpeningCover : UserControl
{
    public OpeningCover() => AvaloniaXamlLoader.Load(this);
}
