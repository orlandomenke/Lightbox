using Avalonia.Input;

namespace Lightbox.App.Services;

/// <summary>What a press on a layer-docker row asks of the link machinery.</summary>
public enum LayerLinkGesture
{
    /// <summary>Join a link with the row above.</summary>
    LinkAbove,

    /// <summary>Join a link with the row below.</summary>
    LinkBelow,

    /// <summary>Leave the link.</summary>
    Unlink,
}

/// <summary>
/// The modifier-and-button mapping for layer linking, as a pure function.
/// </summary>
/// <remarks>
/// <para>
/// Separated from the handler for <c>ArmatureOverlay</c>'s reason: synthetic
/// input through Xvfb is unreliable here, so a mapping that lived inside a
/// <c>PointerPressed</c> handler would ship unguarded and a dropped click
/// would look exactly like a wrong binding. This takes modifiers and a button
/// and answers; the handler does what it says.
/// </para>
/// <para>
/// <b>Every link gesture is on the right button, and that is load-bearing.</b>
/// The first spec put the menu on Ctrl+click, which is the docker's
/// multi-layer toggle — and moving the link actions off it only works if they
/// clear Shift+click too, which is the docker's range select. Keeping the
/// whole left button for selection is what makes "Ctrl+click still toggles"
/// and "Shift+click still ranges" both true at once, rather than trading one
/// collision for another.
/// </para>
/// <para>
/// <b>Order is the rest of the content.</b> Ctrl+Shift must be tested before
/// Shift alone, or "link above" would be read as "unlink" — the failure a
/// reader of an if-chain does not see and a test does.
/// </para>
/// </remarks>
public static class LayerLinkGestures
{
    /// <summary>
    /// What this press means for linking, or null when it means nothing and
    /// the ordinary selection handling should run.
    /// </summary>
    public static LayerLinkGesture? From(KeyModifiers modifiers, bool rightButton)
    {
        // The left button is the docker's, entirely: plain click activates,
        // Ctrl toggles the selection, Shift takes a range, and Ctrl on the
        // thumbnail selects the layer's pixels before this is ever reached.
        if (!rightButton) return null;

        var ctrl = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);

        // Most specific first.
        if (ctrl && shift) return LayerLinkGesture.LinkAbove;
        if (ctrl && alt) return LayerLinkGesture.LinkBelow;
        if (shift) return LayerLinkGesture.Unlink;
        // A BARE right-click is not ours. The docker's context menu is already
        // there and carries rename, reorder, merge and folders; opening a
        // second menu over it would shadow all of that. The link options live
        // inside it as a flyout instead, which is also the discoverable route
        // for an artist who has not learned the modifiers.
        return null;
    }
}
