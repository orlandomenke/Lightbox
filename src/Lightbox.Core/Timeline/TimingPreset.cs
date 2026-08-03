namespace Lightbox.Core.Timeline;

/// <summary>
/// A saved exposure pattern: how long each drawing in a run is held.
/// </summary>
/// <remarks>
/// <para>
/// The answer to Q11, and the half of frame-by-frame work a symbol cannot
/// carry. A symbol carries *drawings*; this carries their **spacing**. Two
/// placements of one cycle run the same drawings out of step, which is reuse of
/// the art — a timing preset is reuse of the timing, applied to whatever art is
/// already there.
/// </para>
/// <para>
/// <c>[2]</c> is "on 2s". <c>[1]</c> is "on 1s". <c>[1,1,2,3,4]</c> is a
/// slow-in: two snappy frames, then progressively longer holds. The pattern is
/// a list of hold *lengths*, in cels, one per drawing — not a list of positions,
/// because positions only make sense against a particular number of drawings
/// and lengths compose with any.
/// </para>
/// <para>
/// It never creates or deletes a drawing. Applying one re-exposes the drawings
/// in the range, which is what makes it compose with everything else rather than
/// competing: the artist's drawings are the artist's, and only their spacing
/// moves.
/// </para>
/// </remarks>
/// <param name="Name">What an animator calls it — "on 2s", "slow in".</param>
/// <param name="Holds">
/// Hold length per drawing, in cels. Values below 1 are meaningless and are
/// clamped on the way in rather than rejected, because a preset with a zero in
/// it should behave rather than throw at the artist.
/// </param>
public sealed record TimingPreset(string Name, IReadOnlyList<int> Holds)
{
    /// <summary>The patterns worth having on day one; an artist adds their own.</summary>
    public static IReadOnlyList<TimingPreset> BuiltIns { get; } =
    [
        new("On 1s", [1]),
        new("On 2s", [2]),
        new("On 3s", [3]),
        new("On 4s", [4]),
        // Two snappy frames then progressively longer: the standard ease out of
        // a fast move into a settle.
        new("Slow in", [1, 1, 2, 3, 4]),
        // The mirror — long holds easing into speed.
        new("Slow out", [4, 3, 2, 1, 1]),
    ];

    /// <summary>Hold length for the n-th drawing, repeating the pattern.</summary>
    /// <remarks>
    /// Repeating rather than stopping is what makes <c>[2]</c> mean "on 2s for
    /// as far as you dragged" instead of "on 2s for the first drawing and
    /// whatever was there before for the rest".
    /// </remarks>
    public int HoldFor(int drawingIndex)
    {
        if (Holds.Count == 0) return 1;
        var h = Holds[((drawingIndex % Holds.Count) + Holds.Count) % Holds.Count];
        return Math.Max(1, h);
    }

    /// <summary>Cels one full cycle of the pattern occupies.</summary>
    public int CycleLength => Holds.Count == 0 ? 1 : Holds.Sum(h => Math.Max(1, h));

    /// <summary>The pattern as an artist would type it — <c>"1, 1, 2, 3, 4"</c>.</summary>
    /// <remarks>
    /// Round-trips through <see cref="TryParse"/>, which is what lets a saved
    /// preset be stored, shown and edited as one short string rather than as a
    /// structure nobody can read in a settings file.
    /// </remarks>
    public string Pattern => string.Join(", ", Holds.Select(h => Math.Max(1, h)));

    /// <summary>
    /// Read a pattern an artist typed: <c>"2"</c>, <c>"1 1 2 3 4"</c>,
    /// <c>"1,1,2,3,4"</c>.
    /// </summary>
    /// <remarks>
    /// Commas, spaces and both together all work, because an animator writing
    /// down a timing chart uses whichever is to hand. Anything that is not a
    /// positive number fails the whole parse rather than being skipped: silently
    /// dropping the typo in "1,1,x,3" would make a preset that quietly is not
    /// the one on screen.
    /// </remarks>
    public static bool TryParse(string name, string? text, out TimingPreset preset)
    {
        preset = new TimingPreset(name, [1]);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var holds = new List<int>();
        foreach (var part in text.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var h) || h < 1) return false;
            holds.Add(h);
        }
        if (holds.Count == 0) return false;

        preset = new TimingPreset(string.IsNullOrWhiteSpace(name) ? holds.Count == 1 ? $"On {holds[0]}s" : "Custom" : name, holds);
        return true;
    }
}
