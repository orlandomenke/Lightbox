namespace Lightbox.App.Services;

/// <summary>What onion skin ghosts, and how.</summary>
public enum OnionMode
{
    /// <summary>Other drawings on this layer — ordinary onion skin.</summary>
    Frames,

    /// <summary>
    /// The other layers at this frame, as a physical light table shows the
    /// sheets under the one you are drawing on.
    /// </summary>
    /// <remarks>
    /// A genuinely different question from "what came before": you are
    /// checking this drawing against the background and the other elements at
    /// the same instant, not against its own neighbours in time.
    /// </remarks>
    LightTable,
}

/// <summary>
/// Onion skin as the artist has set it up.
/// </summary>
/// <remarks>
/// Persisted with the application rather than the document. See
/// <see cref="AppSettings.Onion"/> for why.
/// </remarks>
public sealed class OnionSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many drawings back to show. Separate from <see cref="After"/>
    /// because they are not the same question — an animator working forwards
    /// wants two behind and none ahead most of the time, and one control for
    /// both makes that arrangement impossible to ask for.
    /// </summary>
    public int Before { get; set; } = 1;

    public int After { get; set; } = 1;

    /// <summary>Visibility of the nearest ghost, 0–1.</summary>
    public double Opacity { get; set; } = 0.35;

    /// <summary>
    /// What each further ghost is worth, 0–1. At 1 every ghost is equally
    /// visible — right for checking registration across a sequence; at 0.5
    /// each is half the one before — right when drawing an inbetween.
    /// </summary>
    public double Falloff { get; set; } = 0.5;

    /// <summary>
    /// Step by keyed drawings rather than timeline frames. What an artist on
    /// 2s or 3s means by "the drawing before this one".
    /// </summary>
    public bool KeysOnly { get; set; }

    /// <summary>
    /// Draw the ghosts on top of the current drawing rather than under it.
    /// </summary>
    /// <remarks>
    /// Off by default, because under is how a real lightbox works and is what
    /// you want while drawing. On is for checking: a line you have just made
    /// is easier to compare against a previous one when the previous one is
    /// not hidden behind it.
    /// </remarks>
    public bool DrawOver { get; set; }

    public OnionMode Mode { get; set; } = OnionMode.Frames;

    /// <summary>Earlier drawings. Red by convention, and by every other tool.</summary>
    public string PreviousTint { get; set; } = "#d04040";

    public string NextTint { get; set; } = "#3060c0";

    public OnionSettings Clone() => (OnionSettings)MemberwiseClone();
}
