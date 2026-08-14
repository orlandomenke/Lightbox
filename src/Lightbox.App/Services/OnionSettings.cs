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
    /// <remarks>
    /// <b>0.75 rather than the 0.5 this defaulted to, because 0.5 made the
    /// depth control look broken.</b> Compounding halves the nearest ghost's
    /// opacity, so at the default 0.35 the ghosts came out 0.350, 0.175,
    /// 0.0875 — the second faint and the third invisible against paper. An
    /// artist who raised the depth to 3 saw nothing appear and reasonably
    /// concluded that showing more than one drawing each way did not work; it
    /// did, and every ghost was being rendered. At 0.75 the same stack is
    /// 0.350, 0.263, 0.197, 0.148: four readable ghosts, still ranked by
    /// distance, which is what the falloff is for.
    /// <para>
    /// The near ghost is unchanged either way — this only scales what is
    /// behind it — so the inbetweening case the 0.5 default was tuned for is
    /// a notch away rather than gone, and <see cref="FalloffChosen"/> keeps it
    /// for anyone who went and set it.
    /// </para>
    /// </remarks>
    public double Falloff { get; set; } = 0.75;

    /// <summary>
    /// The artist has set the falloff themselves, so nothing may revise it.
    /// </summary>
    /// <remarks>
    /// The <c>CanvasQualityChosen</c> pattern, and it exists for the one case
    /// a changed default cannot reach on its own: these settings persist, so
    /// an install that has ever saved them carries an explicit
    /// <c>"falloff": 0.5</c> and would never see the new default. Migrating
    /// that value on load is what makes the fix reach the people who reported
    /// the problem rather than only new installs.
    /// <para>
    /// It costs one thing, and it is worth naming: a file written before this
    /// flag existed has no way to say "I chose 0.5 deliberately", so such a
    /// choice is moved to 0.75 once. That is a single visible change to a
    /// control the artist already knows how to set, against a default that
    /// otherwise silently keeps its worst behaviour for exactly the people who
    /// have used the app longest.
    /// </para>
    /// </remarks>
    public bool FalloffChosen { get; set; }

    /// <summary>The falloff this shipped with before 0.75 — see <see cref="FalloffChosen"/>.</summary>
    internal const double LegacyFalloff = 0.5;

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
