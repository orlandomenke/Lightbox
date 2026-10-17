using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The Reference panel's line for a reference that follows another document
/// (Q284): what it is linked to, Refresh now, Open source. Its DataContext is
/// the MainViewModel.
/// </summary>
public partial class ReferenceLinkBar : UserControl
{
    public ReferenceLinkBar() => AvaloniaXamlLoader.Load(this);
}
