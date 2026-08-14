using System.Text.Json.Serialization;

namespace Lightbox.Ai;

/// <summary>
/// A chosen provider and the values for its fields — everything needed to
/// build an <see cref="IAiArtist"/>, and the only AI state that is persisted.
/// </summary>
/// <remarks>
/// Values are keyed by <see cref="AiField.Id"/> rather than typed properties
/// so that a new provider needs no new record. Reading a field goes through
/// <see cref="Value"/>, which applies the resolution order: what you typed,
/// then the environment variable the field declares, then the field's default.
/// That order is what keeps <c>ANTHROPIC_API_KEY</c> working for someone who
/// has never opened the Configure window.
/// </remarks>
public sealed class AiConnection
{
    /// <summary>
    /// Whether AI assistance is offered at all. On by default.
    /// </summary>
    /// <remarks>
    /// Written explicitly even at its default, unlike a document setting: this
    /// is a preference file a person is expected to be able to read, and "the
    /// switch is on" is worth stating where "the medium block is untouched" is
    /// not. Off is a first-class answer — a studio that does not want AI
    /// anywhere near a shot turns it off and the bar goes with it, rather than
    /// staying visible and greyed.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    public string ProviderId { get; set; } = AiProviders.DefaultId;

    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public AiProvider Provider => AiProviders.Resolve(ProviderId);

    /// <summary>The stored value for a field, ignoring environment and defaults.</summary>
    public string? Stored(string fieldId) =>
        Values.TryGetValue(fieldId, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    /// <summary>What the field actually resolves to: stored, then environment, then default.</summary>
    public string? Value(string fieldId)
    {
        if (Stored(fieldId) is { } stored) return stored;

        var field = Provider.Fields.FirstOrDefault(f => f.Id == fieldId);
        if (field?.EnvVar is { } env)
        {
            var fromEnv = Environment.GetEnvironmentVariable(env);
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
        }
        return string.IsNullOrWhiteSpace(field?.Default) ? null : field!.Default;
    }

    /// <summary>Where a resolved value came from — the Configure window says so.</summary>
    public AiValueOrigin OriginOf(string fieldId)
    {
        if (Stored(fieldId) is not null) return AiValueOrigin.Stored;
        var field = Provider.Fields.FirstOrDefault(f => f.Id == fieldId);
        if (field?.EnvVar is { } env && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(env)))
            return AiValueOrigin.Environment;
        return string.IsNullOrWhiteSpace(field?.Default) ? AiValueOrigin.Missing : AiValueOrigin.Default;
    }

    public void Set(string fieldId, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Values.Remove(fieldId);
        else Values[fieldId] = value.Trim();
    }

    /// <summary>Required fields with nothing behind them. Empty means "worth trying".</summary>
    public IReadOnlyList<AiField> Missing() =>
        Provider.Fields.Where(f => f.Required && Value(f.Id) is null).ToList();

    /// <summary>True when every required field resolves to something.</summary>
    [JsonIgnore]
    public bool IsComplete => Missing().Count == 0;

    /// <summary>
    /// The last capability profile measured for this connection, or null.
    /// </summary>
    /// <remarks>
    /// <b>Absent unless somebody measured it</b> — <c>ai.json</c> for a
    /// connection nobody profiled is byte-identical to one written before the
    /// golden set existed. That needed <c>AiSettings</c>'s serializer options
    /// to ignore nulls, which they did not: a nullable property alone would
    /// have written <c>"lastProfile": null</c> into every file, which is the
    /// half of "optional" that is easy to miss.
    /// </remarks>
    public StoredCapabilityProfile? LastProfile { get; set; }

    public AiConnection Clone() => new()
    {
        Enabled = Enabled,
        ProviderId = ProviderId,
        Values = new Dictionary<string, string>(Values, StringComparer.Ordinal),
        LastProfile = LastProfile,
    };
}

/// <summary>
/// A capability profile as it is kept between sessions: what was measured,
/// when, and the lines it produced.
/// </summary>
/// <remarks>
/// <para>
/// The lines rather than the whole <c>CapabilityProfile</c>, deliberately. What
/// an artist comes back to is the reading; keeping every per-pair outcome would
/// put a few kilobytes of scoring detail in a settings file to render the same
/// eight lines. Re-running is how you get the detail back.
/// </para>
/// <para>
/// <paramref name="Subject"/> is what the profile is *about* — provider and
/// model. It is stored rather than derived because the connection can be
/// pointed at a different model afterwards, and a profile shown against the
/// wrong model is worse than no profile. The page compares the two and says so.
/// </para>
/// </remarks>
public sealed record StoredCapabilityProfile(
    string Subject,
    DateTimeOffset Measured,
    bool FullRun,
    IReadOnlyList<string> Lines);

public enum AiValueOrigin
{
    Missing,
    Default,
    Environment,
    Stored,
}
