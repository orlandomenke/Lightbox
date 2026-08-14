using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Lightbox.Ai;
using Lightbox.Ai.Golden;
using Lightbox.App.Services;
using Lightbox.App.Views;

namespace Lightbox.App.Tests;

/// <summary>
/// Configure ▸ AI ▸ Grade this model: the surface for phase 2's golden set.
/// </summary>
/// <remarks>
/// <para>
/// A section on the AI page rather than a page of its own, because grading a
/// model belongs beside choosing and testing one — a second page would put
/// three halves of the same job in two places.
/// </para>
/// <para>
/// What is not tested here is a real run: that needs a provider, a key and
/// tokens. The harness is covered offline in <c>GoldenSetTests</c>; what this
/// file holds is everything between the artist and it — the cost shown before
/// spending, what survives a restart, and the warning that stops a reading
/// being read as being about the wrong model.
/// </para>
/// </remarks>
[Collection("BrushState")]
public class AiCapabilityPageTests(Xunit.ITestOutputHelper output) : BrushStateIsolated
{
    private static ConfigureWindow Open() => new(new ShortcutMap());

    /// <summary>
    /// The subject string the page builds, taken from the catalogue rather than
    /// spelled out — the provider's display name is "Ollama (on this machine)",
    /// and a test that guessed "Ollama" would fail for a reason that has
    /// nothing to do with what it is checking.
    /// </summary>
    private static string Subject(string providerId, string model) =>
        $"{AiProviders.Resolve(providerId).Name} · {model}";

    private static ComboBox RunSize(ConfigureWindow w) => w.FindControl<ComboBox>("AiRunSizeBox")!;
    private static TextBlock Cost(ConfigureWindow w) => w.FindControl<TextBlock>("AiRunCost")!;
    private static Border Result(ConfigureWindow w) => w.FindControl<Border>("AiProfileResult")!;

    /// <summary>
    /// The brush rule, applied to tokens: the expensive option is available,
    /// deliberate, and badged so the trade is made knowingly. A picker whose
    /// two settings look the same is not a choice.
    /// </summary>
    [AvaloniaFact]
    public void TheCostOfARunIsShownBeforeItIsSpent()
    {
        var window = Open();
        var costs = new List<string>();

        RunSize(window).SelectedIndex = 0;
        costs.Add(Cost(window).Text!);
        RunSize(window).SelectedIndex = 1;
        costs.Add(Cost(window).Text!);
        foreach (var c in costs) output.WriteLine(c);

        Assert.All(costs, c => Assert.False(string.IsNullOrWhiteSpace(c)));
        Assert.NotEqual(costs[0], costs[1]);
        // Both say what will be sent, and the expensive one says how much
        // worse it is rather than leaving the reader to compare two numbers.
        Assert.All(costs, c => Assert.Contains("characters of payload", c));
        Assert.Contains("× the short run", costs[1]);
    }

    /// <summary>
    /// Nothing is shown until something has been measured. An empty panel
    /// promising a reading is the control-that-makes-a-promise failure.
    /// </summary>
    [AvaloniaFact]
    public void AConnectionNobodyGradedShowsNoReading()
    {
        var window = Open();

        Assert.False(Result(window).IsVisible);
        Assert.Empty(window.AiProfileShown);
    }

    /// <summary>
    /// A run costs money, so its result outlives the window.
    /// </summary>
    [AvaloniaFact]
    public void AProfileSurvivesClosingTheWindow()
    {
        var stored = new StoredCapabilityProfile(
            Subject(AiProviders.Ollama, "llama3"), DateTimeOffset.UtcNow, false,
            ["short run, 7 pair(s)", "Degrades past 8 strokes — send it fewer than that."]);
        var connection = AiSettings.Load();
        connection.ProviderId = "ollama";
        connection.Set("model", "llama3");
        connection.LastProfile = stored;
        AiSettings.Save(connection);

        var window = Open();
        output.WriteLine(string.Join("\n", window.AiProfileShown));

        Assert.True(Result(window).IsVisible);
        Assert.Equal(stored.Lines, window.AiProfileShown);
        Assert.False(window.AiProfileIsStale);
    }

    /// <summary>
    /// <b>A reading is about the model it was taken on.</b> Point the
    /// connection somewhere else and the numbers on screen stop describing
    /// what is configured — which is worse than showing nothing, because it
    /// looks like an answer. The old reading is still true about the old model
    /// and re-running costs money, so it is kept and labelled rather than
    /// thrown away.
    /// </summary>
    [AvaloniaFact]
    public void AReadingTakenOnAnotherModelSaysSoRatherThanPassingAsThisOne()
    {
        var connection = AiSettings.Load();
        connection.ProviderId = "ollama";
        connection.Set("model", "llama3");
        connection.LastProfile = new StoredCapabilityProfile(
            Subject(AiProviders.Ollama, "llama3"), DateTimeOffset.UtcNow, false, ["Degrades past 8 strokes."]);
        AiSettings.Save(connection);

        var window = Open();
        Assert.False(window.AiProfileIsStale);

        // The artist types a different model into the same connection.
        var model = window.AiFieldRows.Single(r => r.Field.Id == "model");
        model.Value = "qwen2.5";

        var warning = window.FindControl<TextBlock>("AiProfileStale")!;
        output.WriteLine(warning.Text ?? "(no warning)");
        Assert.True(window.AiProfileIsStale);
        Assert.Contains("llama3", warning.Text!);
        Assert.Contains("qwen2.5", warning.Text!);
        // Kept, not discarded — it is still a true reading about llama3.
        Assert.NotEmpty(window.AiProfileShown);
    }

    /// <summary>
    /// The optional half that is easy to miss: a connection nobody graded must
    /// write no key at all, not <c>"lastProfile": null</c>. Checked by looking
    /// at the file rather than at the model, which is the only way this kind of
    /// thing is ever caught.
    /// </summary>
    [Fact]
    public void AnUnprofiledConnectionWritesNoProfileKey()
    {
        var connection = new AiConnection { ProviderId = "ollama" };
        connection.Set("model", "llama3");
        AiSettings.Save(connection);

        var json = File.ReadAllText(AiSettings.Path);
        output.WriteLine(json);
        Assert.DoesNotContain("lastProfile", json, StringComparison.OrdinalIgnoreCase);

        connection.LastProfile = new StoredCapabilityProfile(Subject(AiProviders.Ollama, "llama3"), DateTimeOffset.UtcNow, false, ["x"]);
        AiSettings.Save(connection);
        Assert.Contains("lastProfile", File.ReadAllText(AiSettings.Path), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A profile round-trips through the settings file — it is worthless if
    /// reloading it loses the lines it exists to keep.
    /// </summary>
    [Fact]
    public void AStoredProfileSurvivesTheSettingsFile()
    {
        var connection = new AiConnection { ProviderId = "ollama" };
        connection.LastProfile = new StoredCapabilityProfile(
            Subject(AiProviders.Ollama, "llama3"), new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero), true,
            ["line one", "line two"]);
        AiSettings.Save(connection);

        var reloaded = AiSettings.Load().LastProfile;
        Assert.NotNull(reloaded);
        Assert.Equal(Subject(AiProviders.Ollama, "llama3"), reloaded!.Subject);
        Assert.True(reloaded.FullRun);
        Assert.Equal(["line one", "line two"], reloaded.Lines);
    }

    /// <summary>
    /// The run sizes the page offers are the ones the set actually defines, and
    /// the short one is the default. A picker that opened on the expensive
    /// option would be the brush rule broken by an off-by-one.
    /// </summary>
    [AvaloniaFact]
    public void TheShortRunIsWhatThePageOpensOn()
    {
        var window = Open();
        var box = RunSize(window);

        Assert.Equal(2, ((IEnumerable<string>)box.ItemsSource!).Count());
        Assert.Equal(0, box.SelectedIndex);
        Assert.Contains(
            $"{GoldenSet.Short().Count} pairs",
            Cost(window).Text!);
    }
}
