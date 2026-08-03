using Lightbox.Ai.Mcp;

namespace Lightbox.Ai;

/// <summary>
/// Turns a stored <see cref="AiConnection"/> into an artist. The one place
/// that knows which provider id means which class, so the rest of the app —
/// and every AI feature after this one — sees only <see cref="IAiArtist"/>.
/// </summary>
public static class AiArtistFactory
{
    /// <summary>
    /// Build the artist, or null when the connection is incomplete. Null is an
    /// ordinary state: it is what "no AI configured" has always meant, and the
    /// UI already greys out the AI commands for it.
    /// </summary>
    /// <param name="ignoreSwitch">
    /// Build the artist even with assistance turned off. Only Test connection
    /// passes this: configuring and proving a provider before switching AI on
    /// is the sensible order, and refusing to test until it is enabled would
    /// invert it.
    /// </param>
    public static IAiArtist? Create(AiConnection connection, bool ignoreSwitch = false)
    {
        if (!connection.Enabled && !ignoreSwitch) return null;
        if (!connection.IsComplete) return null;

        string? Get(string id) => connection.Value(id);

        try
        {
            return connection.ProviderId switch
            {
                AiProviders.Anthropic => new AnthropicArtist(Get("apiKey")!, Get("model")),

                AiProviders.OpenAi => new OpenAiArtist(
                    Get("baseUrl") ?? OpenAiArtist.OpenAiUrl, Get("apiKey"), Get("model"), "OpenAI"),

                AiProviders.OpenRouter => new OpenAiArtist(
                    Get("baseUrl") ?? OpenAiArtist.OpenRouterUrl, Get("apiKey"), Get("model"), "OpenRouter"),

                AiProviders.Compatible => new OpenAiArtist(
                    Get("baseUrl"), Get("apiKey"), Get("model"), "The endpoint"),

                AiProviders.Ollama => new OllamaArtist(Get("baseUrl"), Get("model")),

                AiProviders.Mcp => new McpArtist(
                    new StdioMcpChannel(Get("command")!, Get("arguments"), Get("workingDirectory")),
                    Get("tool") ?? McpArtist.DefaultTool),

                _ => null,
            };
        }
        catch (Exception e) when (e is McpException or UriFormatException or ArgumentException)
        {
            // A malformed endpoint or a command that will not start is a
            // configuration mistake, not a crash. Test connection reports it
            // properly; startup just carries on without an artist.
            return null;
        }
    }

    /// <summary>The artist for whatever is configured on this machine right now.</summary>
    public static IAiArtist? CreateFromSettings() => Create(AiSettings.Load());
}
