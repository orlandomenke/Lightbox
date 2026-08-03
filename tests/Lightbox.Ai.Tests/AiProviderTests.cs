using System.Text.Json;
using Lightbox.Ai;

namespace Lightbox.Ai.Tests;

/// <summary>
/// The catalogue, the connection's resolution order, and the settings file.
/// These are what the Configure window and the factory both read, so a
/// mistake here shows up as "the provider I picked is not the one that draws".
/// </summary>
public class AiProviderTests
{
    /// <summary>Point the store at a throwaway directory for the life of a test.</summary>
    private static IDisposable Scratch()
    {
        var path = Path.Combine(Path.GetTempPath(), "lightbox-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        AiSettings.PathOverride = Path.Combine(path, "ai.json");
        return new Restore(path);
    }

    private sealed class Restore(string dir) : IDisposable
    {
        public void Dispose()
        {
            AiSettings.PathOverride = null;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void EveryProviderHasAUniqueIdAndAtLeastOneField()
    {
        Assert.Equal(
            AiProviders.All.Count,
            AiProviders.All.Select(p => p.Id).Distinct().Count());
        Assert.All(AiProviders.All, p => Assert.NotEmpty(p.Fields));
        Assert.All(AiProviders.All, p =>
            Assert.Equal(p.Fields.Count, p.Fields.Select(f => f.Id).Distinct().Count()));
    }

    [Fact]
    public void AnUnknownProviderIdFallsBackRatherThanThrowing()
    {
        var connection = new AiConnection { ProviderId = "a-service-that-was-removed" };
        Assert.Equal(AiProviders.DefaultId, connection.Provider.Id);
    }

    [Fact]
    public void AStoredValueBeatsTheEnvironmentWhichBeatsTheDefault()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Ollama };

        // Nothing set: the field's default.
        Assert.Equal(OllamaArtist.DefaultUrl, connection.Value("baseUrl"));
        Assert.Equal(AiValueOrigin.Default, connection.OriginOf("baseUrl"));

        Environment.SetEnvironmentVariable("LIGHTBOX_OLLAMA_URL", "http://box:1234");
        try
        {
            Assert.Equal("http://box:1234", connection.Value("baseUrl"));
            Assert.Equal(AiValueOrigin.Environment, connection.OriginOf("baseUrl"));

            connection.Set("baseUrl", "http://typed:9999");
            Assert.Equal("http://typed:9999", connection.Value("baseUrl"));
            Assert.Equal(AiValueOrigin.Stored, connection.OriginOf("baseUrl"));

            // Clearing it uncovers the environment again — the watermark case.
            connection.Set("baseUrl", "");
            Assert.Equal(AiValueOrigin.Environment, connection.OriginOf("baseUrl"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LIGHTBOX_OLLAMA_URL", null);
        }
    }

    [Fact]
    public void MissingNamesTheRequiredFieldsThatResolveToNothing()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Compatible };
        var missing = connection.Missing().Select(f => f.Id).ToList();

        // baseUrl and model are required and have no default; the key is not.
        Assert.Contains("baseUrl", missing);
        Assert.Contains("model", missing);
        Assert.DoesNotContain("apiKey", missing);
        Assert.False(connection.IsComplete);

        connection.Set("baseUrl", "http://localhost:1234/v1");
        connection.Set("model", "local-model");
        Assert.True(connection.IsComplete);
    }

    [Fact]
    public void AnthropicIsCompleteFromTheEnvironmentAlone()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Anthropic };
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-test");
        try
        {
            // The model has a default, so the key is the only thing needed —
            // which is what makes an upgrade from the env-var era seamless.
            Assert.True(connection.IsComplete);
            Assert.Equal(AnthropicArtist.Model, connection.Value("model"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        using var scratch = Scratch();
        var saved = new AiConnection { ProviderId = AiProviders.OpenAi };
        saved.Set("apiKey", "sk-abc");
        saved.Set("model", "gpt-5-mini");
        AiSettings.Save(saved);

        var loaded = AiSettings.Load();
        Assert.Equal(AiProviders.OpenAi, loaded.ProviderId);
        Assert.Equal("sk-abc", loaded.Value("apiKey"));
        Assert.Equal("gpt-5-mini", loaded.Value("model"));
    }

    [Fact]
    public void ABrokenSettingsFileMeansNotConfiguredRatherThanACrash()
    {
        using var scratch = Scratch();
        File.WriteAllText(AiSettings.Path, "{ this is not json");

        var loaded = AiSettings.Load();
        Assert.Equal(AiProviders.DefaultId, loaded.ProviderId);
    }

    [Fact]
    public void AProviderThatNoLongerExistsLoadsAsTheDefault()
    {
        using var scratch = Scratch();
        File.WriteAllText(AiSettings.Path, """{"ProviderId":"gone","Values":{}}""");

        Assert.Equal(AiProviders.DefaultId, AiSettings.Load().ProviderId);
    }

    [Fact]
    public void OnlyWhatWasTypedIsWrittenBack()
    {
        // Defaults and environment values must not be frozen into the file:
        // a rotated key or a changed default would otherwise be shadowed by a
        // stale copy nobody remembers making.
        using var scratch = Scratch();
        var connection = new AiConnection { ProviderId = AiProviders.Ollama };
        connection.Set("model", "llama3");
        AiSettings.Save(connection);

        var json = File.ReadAllText(AiSettings.Path);
        Assert.Contains("llama3", json);
        Assert.DoesNotContain("baseUrl", json);
    }

    [Fact]
    public void ThePasswordFieldsAreTheOnesMarkedSecret()
    {
        // A key rendered as plain text is the kind of thing nobody notices
        // until it is on a screen share.
        foreach (var provider in AiProviders.All)
        {
            foreach (var field in provider.Fields)
            {
                var looksSecret = field.Id.Contains("key", StringComparison.OrdinalIgnoreCase)
                                  || field.Id.Contains("token", StringComparison.OrdinalIgnoreCase);
                Assert.Equal(looksSecret, field.Kind == AiFieldKind.Secret);
            }
        }
    }

    [Fact]
    public void EveryApiProviderNamesAModelAndEveryMcpProviderNamesATool()
    {
        foreach (var provider in AiProviders.All)
        {
            var ids = provider.Fields.Select(f => f.Id).ToList();
            if (provider.Transport == AiTransport.Api) Assert.Contains("model", ids);
            else Assert.Contains("tool", ids);
        }
    }
}

/// <summary>The factory: the one place that maps a provider id to a class.</summary>
public class AiArtistFactoryTests
{
    [Fact]
    public void AnIncompleteConnectionProducesNoArtist()
    {
        Assert.Null(AiArtistFactory.Create(new AiConnection { ProviderId = AiProviders.OpenAi }));
    }

    [Theory]
    [InlineData(AiProviders.OpenAi)]
    [InlineData(AiProviders.OpenRouter)]
    [InlineData(AiProviders.Compatible)]
    public void TheOpenAiDialectProvidersAllBuildTheSameArtist(string id)
    {
        var connection = new AiConnection { ProviderId = id };
        connection.Set("apiKey", "sk-test");
        connection.Set("model", "some-model");
        connection.Set("baseUrl", "https://example.test/v1");

        Assert.IsType<OpenAiArtist>(AiArtistFactory.Create(connection));
    }

    [Fact]
    public void OllamaNeedsNoKey()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Ollama };
        Assert.IsType<OllamaArtist>(AiArtistFactory.Create(connection));
    }

    [Fact]
    public void AnthropicBuildsFromAKeyAlone()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Anthropic };
        connection.Set("apiKey", "sk-ant-test");
        Assert.IsType<AnthropicArtist>(AiArtistFactory.Create(connection));
    }

    [Fact]
    public void AnMcpCommandThatCannotStartIsNotACrash()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Mcp };
        connection.Set("command", "definitely-not-an-executable-" + Guid.NewGuid().ToString("N"));

        // Startup must survive a stale command line; Test connection is where
        // the person finds out.
        Assert.Null(AiArtistFactory.Create(connection));
    }

    [Fact]
    public async Task TestingAnIncompleteConnectionSaysWhatIsMissingWithoutACall()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Compatible };
        var check = await AiConnectionTester.TestAsync(connection);

        Assert.False(check.Ok);
        Assert.False(check.Connected);
        Assert.Contains("endpoint", check.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model", check.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TurningAssistanceOffProducesNoArtistHoweverCompleteTheConnectionIs()
    {
        var connection = new AiConnection { ProviderId = AiProviders.Ollama, Enabled = false };
        connection.Set("model", "llama3");

        Assert.True(connection.IsComplete);
        Assert.Null(AiArtistFactory.Create(connection));

        // But it can still be built for a test: configuring and proving a
        // provider before switching AI on is the sensible order.
        Assert.NotNull(AiArtistFactory.Create(connection, ignoreSwitch: true));
    }

    [Fact]
    public void TheSwitchIsOnByDefaultAndSurvivesARoundTrip()
    {
        Assert.True(new AiConnection().Enabled);

        AiSettings.PathOverride = Path.Combine(
            Path.GetTempPath(), "lightbox-ai-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            AiSettings.Save(new AiConnection { Enabled = false });
            Assert.False(AiSettings.Load().Enabled);
        }
        finally
        {
            if (File.Exists(AiSettings.Path)) File.Delete(AiSettings.Path);
            AiSettings.PathOverride = null;
        }
    }
}
