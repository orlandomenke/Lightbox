using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.App.Services;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Configure ▸ AI: the provider picker, and the fields it shows.
/// </summary>
/// <remarks>
/// The thing worth guarding is that the page is generated from the catalogue
/// rather than hand-written per provider. A page that hard-coded Claude's
/// fields would pass a test that only checked Claude, and then show an API
/// key box for a local MCP server.
/// </remarks>
[Collection("BrushState")]
public class AiProviderPageTests : BrushStateIsolated
{
    private static ConfigureWindow Open() => new(new ShortcutMap());

    private static IReadOnlyList<string> FieldIds(ConfigureWindow window) =>
        window.AiFieldRows.Select(r => r.Field.Id).ToList();

    private static void Choose(ConfigureWindow window, string providerId)
    {
        var box = window.FindControl<ComboBox>("AiProviderBox")!;
        box.SelectedIndex = AiProviders.All.Select((p, i) => (p, i)).First(x => x.p.Id == providerId).i;
    }

    [AvaloniaFact]
    public void ThePickerOffersEveryProviderAndStartsOnTheStoredOne()
    {
        var window = Open();
        var box = window.FindControl<ComboBox>("AiProviderBox")!;

        Assert.Equal(AiProviders.All.Count, ((IEnumerable<string>)box.ItemsSource!).Count());
        Assert.Equal(AiProviders.DefaultId, AiProviders.All[box.SelectedIndex].Id);
    }

    [AvaloniaFact]
    public void EachProviderShowsItsOwnFieldsAndNobodyElses()
    {
        var window = Open();

        Choose(window, AiProviders.Anthropic);
        Assert.Equal(["apiKey", "model"], FieldIds(window));

        // A local server wants a command line, not a key.
        Choose(window, AiProviders.Mcp);
        Assert.Contains("command", FieldIds(window));
        Assert.Contains("tool", FieldIds(window));
        Assert.DoesNotContain("apiKey", FieldIds(window));

        // Ollama needs neither a key nor a command.
        Choose(window, AiProviders.Ollama);
        Assert.DoesNotContain("apiKey", FieldIds(window));
        Assert.DoesNotContain("command", FieldIds(window));
    }

    [AvaloniaFact]
    public void OnlyTheSecretFieldsAreMasked()
    {
        var window = Open();
        Choose(window, AiProviders.OpenAi);

        var rows = window.AiFieldRows.ToDictionary(r => r.Field.Id);
        Assert.Equal('•', rows["apiKey"].Mask);
        Assert.Equal('\0', rows["model"].Mask);
        Assert.Equal('\0', rows["baseUrl"].Mask);
    }

    [AvaloniaFact]
    public void ChoosingAProviderPersistsItImmediately()
    {
        // No Save button, so the choice has to survive the window closing —
        // "typed the key, closed the window, AI still off" is the failure
        // this guards.
        var window = Open();
        Choose(window, AiProviders.OpenRouter);

        Assert.Equal(AiProviders.OpenRouter, AiSettings.Load().ProviderId);
    }

    [AvaloniaFact]
    public void TypingAValuePersistsItUnderTheRightProvider()
    {
        var window = Open();
        Choose(window, AiProviders.Compatible);
        var rows = window.AiFieldRows.ToDictionary(r => r.Field.Id);
        rows["baseUrl"].Value = "http://localhost:1234/v1";
        rows["model"].Value = "my-model";

        var stored = AiSettings.Load();
        Assert.Equal(AiProviders.Compatible, stored.ProviderId);
        Assert.Equal("http://localhost:1234/v1", stored.Value("baseUrl"));
        Assert.Equal("my-model", stored.Value("model"));
        Assert.True(stored.IsComplete);
    }

    [AvaloniaFact]
    public void ADefaultShowsAsAPlaceholderRatherThanAsTypedText()
    {
        // The difference matters: a default that is prefilled as text gets
        // saved, and then stops tracking the default when it changes.
        var window = Open();
        Choose(window, AiProviders.Ollama);
        var model = window.AiFieldRows.First(r => r.Field.Id == "model");

        Assert.Equal("", model.Value);
        Assert.Equal(Lightbox.Ai.OllamaArtist.DefaultModel, model.Placeholder);
        Assert.DoesNotContain("model", AiSettings.Load().Values.Keys);
    }

    [AvaloniaFact]
    public void ARequiredFieldWithNothingBehindItSaysSo()
    {
        var window = Open();
        Choose(window, AiProviders.Compatible);
        var url = window.AiFieldRows.First(r => r.Field.Id == "baseUrl");

        Assert.Equal("required", url.Placeholder);

        url.Value = "http://x/v1";
        Assert.Equal("", url.Placeholder);

        // Clearing it puts the prompt back.
        url.Value = "";
        Assert.Equal("required", url.Placeholder);
    }

    [AvaloniaFact]
    public void SwitchingProviderClearsAStaleTestVerdict()
    {
        // A green "Connected" left beside a different provider's fields reads
        // as though those values are the ones that passed.
        var window = Open();
        Choose(window, AiProviders.OpenAi);
        Assert.Equal("", window.AiTestMessage);
    }

    [AvaloniaFact]
    public void TheAiPageIsTheLastCategoryAndHiddenUntilChosen()
    {
        var window = Open();
        var list = window.FindControl<ListBox>("CategoryList")!;
        var page = window.FindControl<ScrollViewer>("AiPage")!;

        Assert.False(page.IsVisible);
        list.SelectedIndex = list.ItemCount - 1;
        Assert.True(page.IsVisible);
    }

    [AvaloniaFact]
    public void TheViewModelPicksUpAProviderChosenWhileItIsRunning()
    {
        // Without this, "choose a provider, test it, use it" needs a restart.
        var vm = new ViewModels.MainViewModel(artist: null);
        Assert.False(vm.IsAiAvailable);

        var connection = new AiConnection { ProviderId = AiProviders.Ollama };
        connection.Set("model", "llama3");
        AiSettings.Save(connection);
        vm.ReloadAiProvider();

        Assert.True(vm.IsAiAvailable);
        Assert.Contains("Ollama", vm.AiProviderLabel);
    }

    [AvaloniaFact]
    public void TheUnavailableHintPointsAtTheConfigureWindowRatherThanAnEnvironmentVariable()
    {
        var vm = new ViewModels.MainViewModel(artist: null);

        Assert.Contains("Configure", vm.AiUnavailableHint);
        Assert.Contains("MCP", vm.AiUnavailableHint);
    }

    // ---- the switch -------------------------------------------------------------

    [AvaloniaFact]
    public void AiAssistanceIsOnByDefault()
    {
        var window = Open();
        var box = window.FindControl<CheckBox>("AiEnabledBox")!;

        Assert.True(box.IsChecked);
        Assert.True(new ViewModels.MainViewModel(artist: null).AiEnabled);
    }

    [AvaloniaFact]
    public void TurningItOffPersistsAndTakesTheArtistWithIt()
    {
        // A working provider plus the switch off must still mean no AI: the
        // switch has to beat a complete connection, not merely hide it.
        var connection = new AiConnection { ProviderId = AiProviders.Ollama };
        connection.Set("model", "llama3");
        AiSettings.Save(connection);

        var window = Open();
        window.FindControl<CheckBox>("AiEnabledBox")!.IsChecked = false;

        Assert.False(AiSettings.Load().Enabled);

        var vm = new ViewModels.MainViewModel(artist: null);
        vm.ReloadAiProvider();
        Assert.False(vm.AiEnabled);
        Assert.False(vm.IsAiAvailable);
    }

    [AvaloniaFact]
    public void TheProviderFieldsStayUsableWhileAssistanceIsOff()
    {
        // Configure and test, then switch on — refusing to let someone set up
        // a provider until they have enabled AI inverts the sensible order.
        var window = Open();
        window.FindControl<CheckBox>("AiEnabledBox")!.IsChecked = false;
        Choose(window, AiProviders.Compatible);

        window.AiFieldRows.First(r => r.Field.Id == "baseUrl").Value = "http://x/v1";
        window.AiFieldRows.First(r => r.Field.Id == "model").Value = "m";

        var stored = AiSettings.Load();
        Assert.False(stored.Enabled);
        Assert.True(stored.IsComplete);
        // Buildable for a test, not for the app.
        Assert.NotNull(AiArtistFactory.Create(stored, ignoreSwitch: true));
        Assert.Null(AiArtistFactory.Create(stored));
    }

    // ---- the test button ---------------------------------------------------------

    [AvaloniaFact]
    public void BothTestDepthsAreOfferedAndQuickIsTheDefault()
    {
        var window = Open();
        var box = window.FindControl<ComboBox>("AiTestDepthBox")!;

        Assert.Equal(2, ((IEnumerable<string>)box.ItemsSource!).Count());
        Assert.Equal(0, box.SelectedIndex);
        Assert.Contains("Quick", ((IEnumerable<string>)box.ItemsSource!).First());
    }

    [AvaloniaFact]
    public void TheDepthPickerExplainsWhatEachOneCosts()
    {
        // The choice is only meaningful if the price is visible: one is
        // seconds, the other is minutes on a local model.
        var window = Open();
        var box = window.FindControl<ComboBox>("AiTestDepthBox")!;
        var explain = window.FindControl<TextBlock>("AiTestExplain")!;

        Assert.Contains("Seconds", explain.Text!);
        box.SelectedIndex = 1;
        Assert.Contains("inbetween", explain.Text!);
        Assert.Contains("minutes", explain.Text!, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void TheProgressBarAndClockAreHiddenUntilATestIsRunning()
    {
        var window = Open();

        Assert.False(window.FindControl<ProgressBar>("AiTestProgress")!.IsVisible);
        Assert.False(window.FindControl<TextBlock>("AiTestElapsed")!.IsVisible);
    }
}
