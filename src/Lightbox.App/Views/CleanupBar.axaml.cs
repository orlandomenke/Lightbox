using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>The line cleaner's button and its Apply/Cancel strip, on the timeline bar.</summary>
public partial class CleanupBar : UserControl
{
    public CleanupBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
