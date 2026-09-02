using Lightbox.App.Services;
using Lightbox.Core.Documents;

namespace Lightbox.App.ViewModels;

/// <summary>
/// The two brush configurations being edited, the saved presets, and the guard that
/// keeps applying a preset from writing back into the preset.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>MainViewModel</c> under Q78's follow-up. One owner and a handful
/// of readers: <c>Brushes.cs</c> edits the working settings through the bound
/// properties, <c>BrushPresets.cs</c> swaps whole presets in and out.
/// </para>
/// <para>
/// <b><see cref="Applying"/> is why this is a class and not four fields moved
/// sideways.</b> Applying a preset assigns the bound properties, and every one of
/// their setters writes back into the working settings — so the assignment has to be
/// fenced or choosing a preset immediately edits it. The fence was nine hand-written
/// <c>flag = true; …; flag = false;</c> pairs, <b>none of them in a try/finally</b>.
/// </para>
/// <para>
/// That is a real failure and not a theoretical one: the fenced region reaches
/// <c>Settings.Save()</c>, which is file I/O and can throw. One throw and the guard
/// stays raised for the rest of the session, at which point every bound brush
/// property silently does nothing — the size slider moves and the brush does not
/// change, with no error anywhere. It is B39's shape exactly: a flag left stale that
/// suppresses behaviour quietly.
/// </para>
/// <para>
/// So the raise and the lower are one method with a <c>finally</c>, and there is no
/// public setter for the flag. A caller cannot leave it raised without writing a loop
/// on purpose.
/// </para>
/// </remarks>
sealed class BrushWorkingSet
{
    /// <summary>The brush's working configuration — what the bound properties edit.</summary>
    internal BrushSettings Brush { get; set; } = new();

    /// <summary>
    /// The eraser's, kept apart so switching tools does not lose either one's tuning.
    /// </summary>
    internal BrushSettings Eraser { get; set; } = new() { Size = 14, Hardness = 0.9 };

    /// <summary>Whichever of the two the current tool edits.</summary>
    internal BrushSettings For(bool isEraser) => isEraser ? Eraser : Brush;

    /// <summary>The presets the artist has saved.</summary>
    internal List<BrushPreset> UserPresets { get; } = [];

    /// <summary>
    /// What each preset has been nudged to, by preset id — the settings in hand
    /// the last time that preset was the one being edited, kept only while they
    /// differ from the preset itself (B71).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A preset is a saved thing and a tweak is a working thing, and the two
    /// used to have different lifetimes: the preset lived in the store, the
    /// tweak lived in <see cref="Brush"/> and died the moment another preset was
    /// chosen. So an artist who tuned three brushes over an afternoon kept one
    /// of them. This is the other two — Krita's "dirty preset", where a nudged
    /// brush stays nudged until it is reloaded or written back.
    /// </para>
    /// <para>
    /// Keyed by preset id rather than kept on the preset, so a tweak never
    /// leaks into the preset record: <c>Update</c> is still the only thing that
    /// writes a preset, and picking the brush again in the picker is still the
    /// thing that gives the saved one back. Absent for a brush left at what its
    /// preset says, which is what lets the store write no key for it.
    /// </para>
    /// </remarks>
    internal Dictionary<string, BrushSettings> Tweaks { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// True while a preset is being applied, so a bound setter knows the write came
    /// from the preset rather than from the artist.
    /// </summary>
    internal bool IsApplying { get; private set; }

    /// <summary>
    /// Run <paramref name="apply"/> with the guard raised, and lower it whatever
    /// happens.
    /// </summary>
    /// <remarks>
    /// Re-entrant calls run the body without re-raising: nesting is not an error, it
    /// just must not lower the guard early. The <c>finally</c> is the whole point —
    /// see the class remarks for what a leaked guard costs.
    /// </remarks>
    internal void Applying(Action apply)
    {
        if (IsApplying)
        {
            apply();
            return;
        }
        IsApplying = true;
        try
        {
            apply();
        }
        finally
        {
            IsApplying = false;
        }
    }
}
