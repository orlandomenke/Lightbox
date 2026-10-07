using Avalonia.Input;

namespace Lightbox.App.Services;

/// <summary>
/// Recognises the second press of a pen tap that arrives twice — once as the
/// pen, once as Windows Ink's emulated mouse — so a selection click that
/// toggles is not toggled back out by its own echo (B391).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a toggle needs this and nothing else did.</b> B126 measured the
/// owner's tablet: every pen event is shadowed by a <c>Mouse</c> event on a
/// whole pixel, at a median of 0.5 ms. A plain click that arrives twice
/// activates the same layer twice, which is harmless; a Ctrl+click adds the
/// layer and then removes it, so the artist sees one row lit and concludes
/// multi-select does not work. Every headless test clicks with one mouse,
/// which is why they were green.
/// </para>
/// <para>
/// <b>Narrow on purpose.</b> An echo is a press from the <em>other</em> device
/// kind — one of the two a pen — on the same target, with the same modifiers,
/// within <see cref="WindowMilliseconds"/>. Two presses from the same device are
/// always two clicks, so a deliberate quick double Ctrl+click with a mouse or a
/// pen is untouched; and a machine with no pen never sees a pen press, so it can
/// never call anything an echo. Either order is caught, because nothing
/// measured says which of the pair arrives first.
/// </para>
/// <para>
/// This is not the raw-input filter B255 retired: nothing is dropped below the
/// routed event. The handler that owns the click asks, and swallows its own
/// duplicate.
/// </para>
/// </remarks>
internal sealed class PenClickEcho(Func<long>? clock = null)
{
    /// <summary>Two orders of magnitude above the measured 0.5 ms handoff, well below any human second click.</summary>
    public const long WindowMilliseconds = 150;

    private readonly Func<long> _clock = clock ?? (() => Environment.TickCount64);
    private (PointerType Type, long At, string Target, KeyModifiers Mods)? _last;

    /// <summary>
    /// Whether this press is the echo of the one before it. A press that is not
    /// becomes the one the next is compared against; an echo is consumed, so a
    /// third press is never matched against a pair.
    /// </summary>
    public bool IsEcho(PointerType type, string target, KeyModifiers mods)
    {
        var now = _clock();
        var echo = _last is { } last
                   && last.Type != type
                   && (last.Type == PointerType.Pen || type == PointerType.Pen)
                   && last.Target == target
                   && last.Mods == mods
                   && now - last.At <= WindowMilliseconds;
        _last = echo ? null : (type, now, target, mods);
        return echo;
    }
}
