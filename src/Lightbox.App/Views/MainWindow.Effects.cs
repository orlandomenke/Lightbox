using Avalonia.Interactivity;

namespace Lightbox.App.Views;

/// <summary>
/// The effects window's one attachment point on the main window.
/// </summary>
/// <remarks>
/// Reached from <c>Effects ▸ Fluid effects…</c> and from the registered
/// <c>effects.window</c> shortcut.
/// <para>
/// <b>Deliberately this small.</b> <c>MainWindow.axaml</c> and
/// <c>MainViewModel</c> are the two hottest files in <c>HOTSPOTS.md</c>, so a
/// feature this size arriving as a menu item, a shortcut case and one field is
/// the point rather than an accident — everything else lives in
/// <see cref="FluidEffectsWindow"/> and its view model.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>
    /// The effects window, if it is open. One per main window rather than one
    /// per element, for the reference board's reason: it is a workbench, and a
    /// second workbench showing the same document is what it exists to replace.
    /// </summary>
    private FluidEffectsWindow? _effects;

    private void OnOpenEffects(object? sender, RoutedEventArgs e)
    {
        if (_effects is { } open)
        {
            open.Activate();
            return;
        }

        var window = new FluidEffectsWindow(_vm);
        _effects = window;
        window.Closed += (_, _) => _effects = null;
        // A child of the main window, so it floats beside the art and closing
        // the application does not leave it orphaned on the desktop.
        window.Show(this);
    }
}
