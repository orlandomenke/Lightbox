using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The tab strip's documents still being read from disk (Q229). Its DataContext is the MainViewModel.</summary>
public partial class OpeningTabs : UserControl
{
    public OpeningTabs() => AvaloniaXamlLoader.Load(this);
}
