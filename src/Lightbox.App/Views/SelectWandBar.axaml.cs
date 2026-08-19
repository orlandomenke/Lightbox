using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// Part of the Select tool's quick-bar group, split into four so the
/// OverflowBar can drop the least important parts instead of all of them
/// (B260). Markup only — every decision belongs to <c>MainViewModel</c>.
/// </summary>
public partial class SelectWandBar : UserControl
{
    public SelectWandBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
