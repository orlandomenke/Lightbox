namespace Lightbox.Ai;

/// <summary>How Lightbox reaches the model.</summary>
public enum AiTransport
{
    /// <summary>An HTTP endpoint Lightbox calls directly.</summary>
    Api,

    /// <summary>
    /// A local MCP server Lightbox launches and speaks JSON-RPC to. The server
    /// owns the model; Lightbox only hands it a prompt and a schema.
    /// </summary>
    Mcp,
}

/// <summary>What kind of editor a field wants, and whether it must be hidden.</summary>
public enum AiFieldKind
{
    Text,

    /// <summary>Masked in the UI and never written to a document or a log.</summary>
    Secret,

    Url,

    Model,
}

/// <summary>
/// One configurable value on a provider. The catalogue is the single source of
/// truth for what a provider needs: the Configure window builds its editor from
/// this list rather than hard-coding a page per provider, so adding a provider
/// is a catalogue entry and a factory case, never a new UI.
/// </summary>
/// <param name="EnvVar">
/// An environment variable that supplies this field when it is not stored.
/// Keeps the "export the key, never type it" workflow the Anthropic path had.
/// </param>
public sealed record AiField(
    string Id,
    string Label,
    AiFieldKind Kind,
    bool Required,
    string? Default = null,
    string? Hint = null,
    string? EnvVar = null);

/// <summary>A provider Lightbox can talk to, and the fields it needs to do it.</summary>
public sealed record AiProvider(
    string Id,
    string Name,
    AiTransport Transport,
    string Summary,
    IReadOnlyList<AiField> Fields);

/// <summary>
/// The providers Lightbox ships with. Three shapes cover the field:
/// Anthropic's own API, anything that speaks the OpenAI chat-completions
/// dialect (GPT, OpenRouter, LM Studio, vLLM, a gateway in front of a private
/// model), and an MCP server that owns the model itself.
/// </summary>
public static class AiProviders
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public const string OpenRouter = "openrouter";
    public const string Ollama = "ollama";
    public const string Compatible = "openai-compatible";
    public const string Mcp = "mcp";

    /// <summary>What a fresh install selects: the path that already worked.</summary>
    public const string DefaultId = Anthropic;

    /// <summary>The field every API provider needs, spelled the same way each time.</summary>
    private static AiField Model(string @default) =>
        new("model", "Model", AiFieldKind.Model, Required: true, @default,
            "The model name exactly as the service spells it.");

    public static IReadOnlyList<AiProvider> All { get; } =
    [
        new(Anthropic, "Claude (Anthropic)", AiTransport.Api,
            "Anthropic's Messages API with structured outputs and streaming. The strongest "
            + "inbetweener of the built-in options, and the one Lightbox is tuned against.",
            [
                new("apiKey", "API key", AiFieldKind.Secret, Required: true, null,
                    "From console.anthropic.com. Stored in your settings file, never in a document.",
                    "ANTHROPIC_API_KEY"),
                Model(AnthropicArtist.Model),
            ]),

        new(OpenAi, "GPT (OpenAI)", AiTransport.Api,
            "OpenAI's chat completions with a strict JSON schema, so responses parse by "
            + "construction. Reference views ride along as images.",
            [
                new("apiKey", "API key", AiFieldKind.Secret, Required: true, null,
                    "From platform.openai.com.", "OPENAI_API_KEY"),
                Model("gpt-5"),
                new("baseUrl", "Endpoint", AiFieldKind.Url, Required: false,
                    "https://api.openai.com/v1",
                    "Change this only to reach a proxy or an Azure deployment."),
            ]),

        new(OpenRouter, "OpenRouter", AiTransport.Api,
            "One key for many vendors' models. Speaks the OpenAI dialect, so schema-constrained "
            + "output depends on the model you route to.",
            [
                new("apiKey", "API key", AiFieldKind.Secret, Required: true, null,
                    "From openrouter.ai/keys.", "OPENROUTER_API_KEY"),
                Model("anthropic/claude-opus-4.1"),
                new("baseUrl", "Endpoint", AiFieldKind.Url, Required: false,
                    "https://openrouter.ai/api/v1"),
            ]),

        new(Ollama, "Ollama (on this machine)", AiTransport.Api,
            "A local model, no key and no network. Small models inbetween noticeably worse than "
            + "a frontier one — this path is for working offline and for testing the pipeline.",
            [
                Model(OllamaArtist.DefaultModel),
                new("baseUrl", "Server", AiFieldKind.Url, Required: false, OllamaArtist.DefaultUrl,
                    "Where Ollama is listening.", "LIGHTBOX_OLLAMA_URL"),
            ]),

        new(Compatible, "Custom (OpenAI-compatible)", AiTransport.Api,
            "Anything that answers /chat/completions: LM Studio, vLLM, llama.cpp's server, a "
            + "gateway in front of a model of your own. The key is optional because a local one "
            + "usually wants none.",
            [
                new("baseUrl", "Endpoint", AiFieldKind.Url, Required: true, null,
                    "The base URL, up to and including any version segment — the /chat/completions "
                    + "part is added for you."),
                Model(""),
                new("apiKey", "API key", AiFieldKind.Secret, Required: false, null,
                    "Leave empty if the endpoint does not authenticate."),
            ]),

        new(Mcp, "Custom agent (MCP)", AiTransport.Mcp,
            "An MCP server that owns the model. Lightbox launches it and calls one tool with the "
            + "system prompt, the task and the JSON schema; whatever text comes back is parsed as "
            + "strokes. Use this to put an agent of your own between Lightbox and a model.",
            [
                new("command", "Command", AiFieldKind.Text, Required: true, null,
                    "The executable to launch, e.g. npx or a path to your server."),
                new("arguments", "Arguments", AiFieldKind.Text, Required: false, null,
                    "Space-separated; quote anything containing a space."),
                new("tool", "Tool", AiFieldKind.Text, Required: true, "generate",
                    "The tool Lightbox calls. It is passed system, prompt and schema, and must "
                    + "return text that matches the schema."),
                new("workingDirectory", "Working directory", AiFieldKind.Text, Required: false),
            ]),
    ];

    public static AiProvider? Find(string? id) =>
        id is null ? null : All.FirstOrDefault(p => p.Id == id);

    /// <summary>The provider for an id, falling back to the default rather than throwing.</summary>
    public static AiProvider Resolve(string? id) => Find(id) ?? Find(DefaultId)!;
}
