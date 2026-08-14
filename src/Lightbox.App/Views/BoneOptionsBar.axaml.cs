using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Lightbox.App.Views;

/// <summary>
/// The Bone tool's options: the mode, the rig's bones, the weight brush and
/// the binding actions. Markup only — every decision it shows belongs to
/// <c>MainViewModel.Armature</c>.
/// </summary>
public partial class BoneOptionsBar : UserControl
{
    public BoneOptionsBar() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
