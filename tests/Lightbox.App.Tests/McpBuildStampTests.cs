using System.Text.Json;
using Avalonia.Headless.XUnit;
using Lightbox.App.Services;
using Lightbox.App.ViewModels;
using Lightbox.Mcp;

namespace Lightbox.App.Tests;

/// <summary>
/// Which build am I talking to — the question B195 could not answer.
/// </summary>
/// <remarks>
/// <para>
/// B195 was a defect in <c>Lightbox.Mcp</c>. It was fixed and merged the same
/// morning, and the report came back unchanged, because Claude Desktop launches
/// a <em>published</em> <c>Lightbox.Mcp.exe</c> and nothing in the protocol said
/// which one. "Already fixed" and "still broken" were indistinguishable from
/// either end of the pipe.
/// </para>
/// <para>
/// So <c>get_scene</c> now carries two build strings, and the pair is the
/// diagnosis: equal is healthy, different means one half was republished and
/// the other was not, and <b>a missing <c>mcpBuild</c> means the server predates
/// the stamp</b> and is certainly stale.
/// </para>
/// </remarks>
public class McpBuildStampTests(Xunit.ITestOutputHelper output)
{
    private static IpcProtocol.Request Req(string op) => new() { Op = op };

    private static JsonElement Scene(MainViewModel vm)
    {
        var response = new IpcDocumentApi(vm).Handle(Req("get_scene"));
        Assert.True(response.Ok, response.Error);
        return response.Payload!.Value;
    }

    /// <summary>
    /// The app answers with its own build, in the call an agent already makes
    /// first — no extra round trip and nothing to remember to ask for.
    /// </summary>
    [AvaloniaFact]
    public void TheSceneSaysWhichBuildOfTheApplicationAnsweredIt()
    {
        var build = Scene(new MainViewModel(null)).GetProperty("appBuild").GetString();
        output.WriteLine($"appBuild = {build}");

        Assert.False(string.IsNullOrWhiteSpace(build));
        // DiagnosticLog.Build is the same stamp the crash reports carry; the two
        // must not drift into naming the same binary differently.
        Assert.Equal(DiagnosticLog.Build, build);
    }

    /// <summary>
    /// <b>The property the whole design turns on: the app's build reaches the
    /// agent through a server that knows nothing about it.</b>
    /// </summary>
    /// <remarks>
    /// Every other tool forwards the payload verbatim, which is what a pre-stamp
    /// server does with <c>get_scene</c> too. Simulated here by taking the app's
    /// real payload and passing it through the plain forwarding path rather than
    /// the annotating one: <c>appBuild</c> survives, <c>mcpBuild</c> is absent,
    /// and that absence is the signal. A stamp only the new server could report
    /// would be useless for the one case it exists for.
    /// </remarks>
    [AvaloniaFact]
    public void AServerTooOldToStampItselfStillRelaysTheApplicationsBuild()
    {
        var payload = Scene(new MainViewModel(null));

        // What a pre-stamp Lightbox.Mcp did with a get_scene payload.
        var forwardedVerbatim = payload.GetRawText();
        output.WriteLine(forwardedVerbatim[..Math.Min(160, forwardedVerbatim.Length)]);

        using var seen = JsonDocument.Parse(forwardedVerbatim);
        Assert.True(seen.RootElement.TryGetProperty("appBuild", out var app));
        Assert.Equal(DiagnosticLog.Build, app.GetString());
        Assert.False(seen.RootElement.TryGetProperty("mcpBuild", out _));
    }

    /// <summary>
    /// A current server adds its own build beside the app's, and loses nothing
    /// of the scene doing it.
    /// </summary>
    [AvaloniaFact]
    public void ACurrentServerAddsItsOwnBuildWithoutDisturbingTheScene()
    {
        var vm = new MainViewModel(null);
        var payload = Scene(vm);

        var annotated = LightboxTools.WithBuildForTests(payload);
        output.WriteLine(annotated[..Math.Min(200, annotated.Length)]);

        using var seen = JsonDocument.Parse(annotated);
        Assert.Equal(BuildInfo.Build, seen.RootElement.GetProperty("mcpBuild").GetString());
        Assert.Equal(DiagnosticLog.Build, seen.RootElement.GetProperty("appBuild").GetString());

        // The scene itself is untouched — a diagnostic that costs the caller the
        // answer it asked for is a worse bug than the one it diagnoses.
        Assert.Equal(vm.Doc.Scene.Width, seen.RootElement.GetProperty("width").GetInt32());
        Assert.Equal(
            payload.GetProperty("layers").GetArrayLength(),
            seen.RootElement.GetProperty("layers").GetArrayLength());
    }

    /// <summary>
    /// A payload that is not an object comes back untouched rather than
    /// throwing. The stamp must never be the reason a tool call fails.
    /// </summary>
    [Fact]
    public void AnOddPayloadIsPassedThroughRatherThanBreakingTheCall()
    {
        using var array = JsonDocument.Parse("[1,2,3]");
        Assert.Equal("[1,2,3]", LightboxTools.WithBuildForTests(array.RootElement));

        Assert.Equal("{}", LightboxTools.WithBuildForTests(default));
    }

    /// <summary>
    /// The stamp names the commit, not just the version — that is what makes it
    /// answer "is the fix in this one". A number forty builds share cannot.
    /// </summary>
    [Fact]
    public void TheStampIsSpecificEnoughToNameABuild()
    {
        output.WriteLine($"mcp: {BuildInfo.Build}");
        output.WriteLine($"app: {DiagnosticLog.Build}");

        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Build));
        Assert.NotEqual("unknown", BuildInfo.Build);
        // The SDK appends +<sha>; a stamp without one is a version number that
        // cannot distinguish two builds of the same release.
        Assert.Contains('+', BuildInfo.Build);
    }
}
