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

    public AiConnection Clone() => new()
    {
        Enabled = Enabled,
        ProviderId = ProviderId,
        Values = new Dictionary<string, string>(Values, StringComparer.Ordinal),
    };
}

public enum AiValueOrigin
{
    Missing,
    Default,
    Environment,
    Stored,
}
