using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The selected guide's numbers, along the quick options bar: where it is, and
/// the one or two figures that decide what it is. Markup only — every decision
/// it shows belongs to <c>MainViewModel.GuidesAndReferences</c>.
/// </summary>
/// <remarks>
/// The horizontal glance; <see cref="GuideOptionsPanel"/> is the same bindings
/// with room for labels, in the tool-options docker.
/// </remarks>
public partial class GuideOptionsBar : UserControl
{
    public GuideOptionsBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
