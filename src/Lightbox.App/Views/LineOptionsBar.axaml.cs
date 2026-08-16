using Avalonia.Controls;

namespace Lightbox.App.Views;

/// <summary>
/// What you can do to the line you are inside — the isolation session's own
/// options.
/// </summary>
/// <remarks>
/// See the comment in the XAML for why this exists as a control and why it is
/// gated on the session rather than on a tool (B221).
/// </remarks>
public partial class LineOptionsBar : UserControl
{
    public LineOptionsBar() => InitializeComponent();
}
