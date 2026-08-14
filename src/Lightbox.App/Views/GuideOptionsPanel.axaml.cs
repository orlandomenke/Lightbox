using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The selected guide's numbers, in the tool-options docker: where it is, what
/// it is made of, whether it shows, snaps or is pinned. Markup only — every
/// decision it shows belongs to <c>MainViewModel.GuidesAndReferences</c>.
/// </summary>
/// <remarks>
/// The vertical home; <see cref="GuideOptionsBar"/> is the same bindings as a
/// horizontal glance along the quick bar.
/// </remarks>
public partial class GuideOptionsPanel : UserControl
{
    public GuideOptionsPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
