using System.Text.Json;

namespace Lightbox.Ai;

/// <summary>
/// Resolves the Anthropic API key: the ANTHROPIC_API_KEY environment variable
/// wins; otherwise the per-user settings file. The key is never written into
/// documents or logs.
/// </summary>
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

    public static void SaveApiKey(string key)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["anthropicApiKey"] = key },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
