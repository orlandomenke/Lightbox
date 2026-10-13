namespace Lightbox.Ai;

/// <summary>
/// How hard a Claude model thinks before it draws (Q262).
/// </summary>
/// <remarks>
/// <para>
/// <b>Low by default, and that is a measurement rather than a saving.</b> On
/// real drawings (2026-10-13) thinking was 60–90% of a request's output tokens
/// and most of its time; one layer of 760 points spent 50,000 thinking tokens
/// and nine and a half minutes at the model's default. At low effort the same
/// four layers ran 1.4–7× faster and up to 5× cheaper, every frame was still
/// accepted by the verifier, and side by side the drawings were
/// indistinguishable from the default's. What limited quality was what the
/// model was shown, not how long it thought about it.
/// </para>
/// <para>
/// Four layers is a small sample, which is why this is a field an artist can
/// raise and not a constant.
/// </para>
/// </remarks>
public static class AiEffort
{
    public const string Default = "low";

    /// <summary>
    /// Send no effort at all and let the model use its own. The way out for a
    /// model — or an older Claude Code — that does not take the setting: an
    /// empty field means <see cref="Default"/>, so "leave it out" needs a word.
    /// </summary>
    public const string ModelsOwn = "model";

    private static readonly string[] Levels = ["low", "medium", "high", "xhigh", "max", ModelsOwn];

    /// <summary>What goes on the wire for a normalized level: the level, or null for the model's own.</summary>
    public static string? Sent(string level) => level == ModelsOwn ? null : level;

    /// <summary>
    /// When a refusal is about effort, the sentence that says what to change;
    /// null when it is about something else.
    /// </summary>
    /// <remarks>
    /// Low is sent by default, so an artist who names an older model gets a
    /// rejection for a setting they never touched. Left as the service's own
    /// words that is a generic error; said this way it is one field to edit.
    /// </remarks>
    public static string? Rejection(string said, string? level = null) =>
        said.Contains("effort", StringComparison.OrdinalIgnoreCase)
            ? "This model or version does not accept the Effort setting"
              + (level is null ? "" : $" “{level}”")
              + $". In Configure ▸ AI, set Effort to “{ModelsOwn}” to leave it out, or choose another level."
            : null;

    /// <summary>
    /// The level typed, or <see cref="Default"/> when it is empty or not a
    /// level — a typo in a settings field must not become an error halfway
    /// through a request.
    /// </summary>
    public static string Normalize(string? typed)
    {
        var level = typed?.Trim().ToLowerInvariant();
        return level is not null && Levels.Contains(level) ? level : Default;
    }

    /// <summary>The field both Claude routes show in Configure.</summary>
    internal static AiField Field { get; } = new(
        "effort", "Effort", AiFieldKind.Text, Required: false, Default,
        "How hard the model thinks before drawing: low, medium, high, xhigh or max — or “model” to "
        + "send nothing and use the model's own. Low is the default: several times faster and cheaper, "
        + "with no loss we could see on the drawings we tried. Raise it if overlaps, turning forms or "
        + "crowded layers come out wrong. A word that is not a level is read as low.");
}
