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
