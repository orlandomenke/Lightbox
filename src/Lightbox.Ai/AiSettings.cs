using System.Text.Json;

namespace Lightbox.Ai;

/// <summary>
/// Where the chosen AI provider is stored, and how a pre-provider install is
/// carried forward.
/// </summary>
/// <remarks>
/// Its own file, <c>ai.json</c>, beside <c>brushes.json</c> and
/// <c>shortcuts.json</c> — deliberately not inside <c>settings.json</c>.
/// <c>AppSettings.Save</c> serializes its whole object over that file, so
/// anything else living there is erased the next time a preference changes.
/// The old <c>anthropicApiKey</c> key sat in exactly that line of fire; it is
/// still read here, once, to migrate, and never written back.
/// </remarks>
public static class AiSettings
{
    /// <summary>Test seam: point the store at a scratch directory.</summary>
    public static string? PathOverride { get; set; }

    public static string Path =>
        PathOverride ?? System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(ApiKeyProvider.SettingsPath)!, "ai.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>
    /// The stored connection, or one migrated from the legacy keys, or the
    /// default provider with nothing filled in. Never throws and never null:
    /// a broken settings file means "not configured", not a dead app.
    /// </summary>
    public static AiConnection Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var stored = JsonSerializer.Deserialize<AiConnection>(File.ReadAllText(Path), Json);
                if (stored is not null)
                {
                    // A provider that no longer exists must not strand someone
                    // on a page with no fields.
                    if (AiProviders.Find(stored.ProviderId) is null) stored.ProviderId = AiProviders.DefaultId;
                    return stored;
                }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to migration, then to the default.
        }

        return Migrate() ?? new AiConnection();
    }

    /// <summary>
    /// Read the pre-provider configuration. A key beats an Ollama model, which
    /// is the order <c>ResolveArtist</c> used before providers existed, so an
    /// upgrade lands on the same artist it had yesterday.
    /// </summary>
    private static AiConnection? Migrate()
    {
        if (ApiKeyProvider.GetApiKey() is { } key)
        {
            var c = new AiConnection { ProviderId = AiProviders.Anthropic };
            // Only store it if it was in the settings file; an environment
            // variable resolves on its own and copying it in would freeze a
            // rotated key into a second place.
            if (Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is null or "") c.Set("apiKey", key);
            return c;
        }

        if (ApiKeyProvider.GetOllamaConfig() is { } ollama)
        {
            var c = new AiConnection { ProviderId = AiProviders.Ollama };
            c.Set("model", ollama.Model);
            if (ollama.Url != OllamaArtist.DefaultUrl) c.Set("baseUrl", ollama.Url);
            return c;
        }

        return null;
    }

    /// <summary>
    /// Persist the connection. Failures are swallowed for the same reason
    /// <see cref="AiSettings"/> exists as its own file: a preference that will
    /// not save is an annoyance, never a reason for something else to fail.
    /// </summary>
    public static void Save(AiConnection connection)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            // Write-then-move: a crash mid-save must not leave a half file that
            // reads as "not configured" and silently turns the AI off.
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(connection, Json));
            File.Move(tmp, Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
