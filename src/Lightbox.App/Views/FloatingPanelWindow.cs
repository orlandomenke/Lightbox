using Avalonia.Controls;
using Lightbox.App.Controls;
using Lightbox.App.Docking;
using Lightbox.App.Services;

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
    /// The scope a key pressed in this window belongs to: the panel it is
    /// showing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tearing a panel off used to silence its keyboard entirely.</b> Not
    /// just its own bindings — every shortcut. A floating timeline answered
    /// neither <c>I</c> (insert a key, which is its own) nor <c>B</c> (the
    /// brush, which is general), because this window wired no key handling at
    /// all and the main window never saw the press. Docking the panel again
    /// brought them all back, which is what made it read as a mystery rather
    /// than as a missing handler.
    /// </para>
    /// <para>
    /// <b>The docker's own id, not its <c>ActiveTab</c>.</b> The two agree
    /// whenever the bookkeeping is right, because a tab group's visible control
    /// <i>is</i> the active panel's — so this is which of them is trusted rather
    /// than a different answer. The id belongs to the control the pointer is
    /// physically inside; <c>ActiveTab</c> is derived state a strip rebuild
    /// writes to. The docked side reads it the same way, for the same reason.
    /// </para>
    /// <para>
    /// <b>No hover test here, and that is not a shortcut taken.</b> Docked, the
    /// pointer picks between panels because they share a window. This window
    /// <i>is</i> one panel, so anywhere in it is that panel — and a key only
    /// arrives here when this window has the focus, which is the artist saying
    /// where they are at least as plainly as the pointer would.
    /// </para>
    /// </remarks>
    public ShortcutScope Scope =>
        ShortcutScope.In((Content as Docker)?.PanelId ?? PanelId);

    /// <summary>
    /// Let go of the panel without closing it — used when the panel is being
    /// docked again, so the window can be closed without the panel going with
    /// it.
    /// </summary>
    /// <returns>
    /// The panel, or <c>null</c> when the window is no longer holding one.
    /// </returns>
    /// <remarks>
    /// <b>Nullable because "somebody already took it" is a normal state, not a bug</b>
    /// (B48). Docking a floating panel runs the layout pass first, and that pass detaches
    /// every panel it is about to put in a strip — so by the time this is called the
    /// content is gone and the panel is somewhere better.
    /// <para>
    /// This was <c>(Docker)Content!</c>, and the interesting part is that the cast did not
    /// throw: casting a null reference succeeds and yields null, so the <c>!</c> silenced
    /// the warning and handed the null on. The crash surfaced two frames later in
    /// <c>MainWindow.Detach</c>, which is a long way from the line that was wrong. Returning
    /// a nullable is the honest signature — there really is nothing to hand back.
    /// </para>
    /// </remarks>
    public Docker? Release()
    {
        var panel = Content as Docker;
        Content = null;
        Dismissed = null;
        Moved = null;
        return panel;
    }
}
