using System.Text.Json;

namespace Lightbox.Ai;

/// <summary>
/// The pre-provider configuration, kept for two reasons only: it names the
/// settings directory every other store hangs off, and <see cref="AiSettings"/>
/// reads it once to migrate an existing install.
/// </summary>
/// <remarks>
/// Nothing here writes. It used to: <c>SaveApiKey</c> serialized a single-key
/// object over <c>settings.json</c>, which is the same file
/// <c>AppSettings.Save</c> serializes its whole object over — each silently
/// erased the other. New configuration lives in <c>ai.json</c> instead, and
/// this type is read-only so that hazard cannot come back.
/// </remarks>
public static class ApiKeyProvider
{
    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Lightbox",
            "settings.json");

    public static string? GetApiKey()
    {
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;

        try
        {
            if (!File.Exists(SettingsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("anthropicApiKey", out var prop)
                && prop.ValueKind == JsonValueKind.String)
            {
                var key = prop.GetString();
                return string.IsNullOrWhiteSpace(key) ? null : key;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken settings file just means "no key".
        }
        return null;
    }

    /// <summary>
    /// Ollama is opted into by naming a model — env vars LIGHTBOX_OLLAMA_MODEL
    /// (+ optional LIGHTBOX_OLLAMA_URL) win over settings keys "ollamaModel" /
    /// "ollamaUrl". Returns null when not configured.
    /// </summary>
    public static (string Url, string Model)? GetOllamaConfig()
    {
        var envModel = Environment.GetEnvironmentVariable("LIGHTBOX_OLLAMA_MODEL");
        var envUrl = Environment.GetEnvironmentVariable("LIGHTBOX_OLLAMA_URL");
        if (!string.IsNullOrWhiteSpace(envModel))
            return (string.IsNullOrWhiteSpace(envUrl) ? OllamaArtist.DefaultUrl : envUrl, envModel);

        try
        {
            if (!File.Exists(SettingsPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            string? Get(string name) =>
                doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;
            var model = Get("ollamaModel");
            if (string.IsNullOrWhiteSpace(model)) return null;
            var url = Get("ollamaUrl");
            return (string.IsNullOrWhiteSpace(url) ? OllamaArtist.DefaultUrl : url, model);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
