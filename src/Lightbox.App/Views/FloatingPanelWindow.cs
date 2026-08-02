using Avalonia.Controls;
using Lightbox.App.Controls;
using Lightbox.App.Docking;

namespace Lightbox.App.Views;

/// <summary>
/// A panel torn out of the window, in a window of its own.
/// </summary>
/// <remarks>
/// The <see cref="Docker"/> inside is the same instance that was docked — it
/// is reparented, not rebuilt — so its bindings, its scroll position and any
/// half-typed value in it survive the trip. That is the whole reason the
/// panels live in a pool and the layout only names them.
/// </remarks>
public sealed class FloatingPanelWindow : Window
{
    public DockPanelId PanelId { get; }

    /// <summary>The window was closed by its own chrome: park the panel.</summary>
    public event Action<DockPanelId>? Dismissed;

    /// <summary>The window was moved or resized: the layout has a new rectangle.</summary>
    public event Action<DockPanelId>? Moved;

    public FloatingPanelWindow(Docker panel, DockPlacement placement)
    {
        PanelId = panel.PanelId;
        Title = DockPanels.TitleOf(panel.PanelId);
        Content = panel;
        Width = placement.FloatWidth;
        Height = placement.FloatHeight;
        Position = new Avalonia.PixelPoint((int)placement.FloatX, (int)placement.FloatY);
        ShowInTaskbar = false;
        Background = Avalonia.Media.Brushes.Transparent;

        PositionChanged += (_, _) => Moved?.Invoke(PanelId);
        // Closing the window is closing the panel. Anything else would leave
        // the layout claiming a floating panel that is nowhere on screen.
        Closing += (_, _) => Dismissed?.Invoke(PanelId);
    }

    /// <summary>
    /// Let go of the panel without closing it — used when the panel is being
    /// docked again, so the window can be closed without the panel going with
    /// it.
    /// </summary>
    public Docker Release()
    {
        var panel = (Docker)Content!;
        Content = null;
        Dismissed = null;
        Moved = null;
        return panel;
    }
}
