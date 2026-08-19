using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Lightbox.App.Views;

/// <summary>Part of the MainWindow code-behind — see MainWindow.axaml.cs.</summary>
/// <remarks>
/// <para>
/// <b>The gear opens the tool options at the gear (B261).</b> It used to open
/// the Tool options panel, which works and is easy to miss: the panel is across
/// the window from the button, and an artist watching the canvas sees nothing
/// happen. A flyout appears where the hand already is.
/// </para>
/// <para>
/// <b>The flyout borrows the panel's content rather than copying it.</b>
/// <see cref="Docker"/> is a <c>ContentControl</c>, so the whole tool-options
/// page can be lent to a flyout and handed back on close — one definition, two
/// hosts, and every <c>x:Name</c> the brush picker and toolbar code-behind
/// reach for still resolves, because the controls are the same instances. A
/// second copy in markup would be 700 lines that drift, and would exceed the
/// file's ratchet besides.
/// </para>
/// <para>
/// The reparenting is the trick <see cref="Controls.OverflowBar"/> already uses
/// for its ▾, and for the same reason: opening a flyout is a discrete moment
/// where nothing is mid-layout.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>Kept so the content can be given back to exactly the docker it came from.</summary>
    private Flyout? _toolOptionsFlyout;

    /// <summary>
    /// Whether the flyout currently holds the panel's content, so the handler
    /// cannot lend it twice — a second lend would leave the first host empty
    /// and the page unreachable until a restart.
    /// </summary>
    private bool _toolOptionsLent;

    /// <summary>
    /// Tall enough to show a brush page without scrolling on an ordinary
    /// screen, short enough that the flyout cannot run off a laptop's.
    /// </summary>
    private const double FlyoutMaxHeight = 620;

    private const double FlyoutMaxWidth = 560;

    private void OnToolOptionsGear(object? sender, RoutedEventArgs e)
    {
        // Already on screen: bring the panel forward rather than opening a
        // second way to look at it. The complaint the flyout answers — a change
        // you miss because it happened elsewhere — does not apply when the
        // options are visible, and lending the content away from a docked panel
        // would empty the panel the artist is looking at.
        if (_vm.Workspace.ToolOptionsDockerVisible)
        {
            _vm.OpenToolOptionsCommand.Execute(null);
            return;
        }

        if (sender is not Button gear || _toolOptionsLent) return;
        if (ToolOptionsDocker.Content is not Control page) return;

        var host = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = FlyoutMaxHeight,
            MaxWidth = FlyoutMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // Detach first, and make the detach *complete* before the popup adopts
        // it. Clearing Content only unparents the page logically; the presenter
        // lets go on its next layout pass, and this docker is parked in the
        // hidden panel pool where no pass ever runs. Handing a still-attached
        // control to a Popup throws "AttachedToLogicalTreeCore called for
        // 'Panel' but control has no logical parent" — a half-detached state,
        // not a missing one, which is why the message reads backwards.
        ToolOptionsDocker.Content = null;
        ToolOptionsDocker.UpdateLayout();
        host.Content = page;
        _toolOptionsLent = true;

        _toolOptionsFlyout = new Flyout
        {
            Content = host,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
        };
        _toolOptionsFlyout.Closed += ReturnToolOptionsToItsPanel;
        // Attached rather than ShowAt: an attached flyout can be found from the
        // gear and closed through the ordinary path, so light dismiss, Escape
        // and anything else all raise Closed — which is the only thing that
        // gives the page back.
        FlyoutBase.SetAttachedFlyout(gear, _toolOptionsFlyout);
        FlyoutBase.ShowAttachedFlyout(gear);
    }

    /// <summary>
    /// Give the page back, whatever closed the flyout.
    /// </summary>
    /// <remarks>
    /// <b>This must run on every close path</b> — clicking away, Escape, or the
    /// gear again — because the docker is left with no content until it does,
    /// and a workspace that then shows the panel would show an empty one.
    /// </remarks>
    private void ReturnToolOptionsToItsPanel(object? sender, System.EventArgs e)
    {
        if (_toolOptionsFlyout is { } flyout)
        {
            flyout.Closed -= ReturnToolOptionsToItsPanel;
            if (flyout.Content is ScrollViewer { Content: Control page } host)
            {
                host.Content = null;
                ToolOptionsDocker.Content = page;
            }
            flyout.Content = null;
            _toolOptionsFlyout = null;
        }
        _toolOptionsLent = false;
    }
}
