
namespace Lightbox.App.Tests;

/// <summary>
/// Which way frames reach the screen, and that the fast path is the one taken
/// unless somebody asks otherwise.
/// </summary>
/// <remarks>
/// Measured on the owner's machine, 2026-08-26, same build and same brush over
/// two sessions of about six thousand pointer events each: presenting to a
/// low-latency swap chain took <c>publish -&gt; drawn</c> from 52.07 ms to
/// 30.67 ms and delivered 544 frames where the desktop compositor delivered
/// 381. That is why the default is what it is, and why these pin it — a
/// default that silently reverts would give back the largest measured win of
/// that day without anything going red.
/// </remarks>
public class CompositionModeTests(ITestOutputHelper output)
{
    /// <summary>
    /// The chosen modes by name. Compared as strings so this suite needs no
    /// reference to Avalonia.Win32 — the enum is a platform package's, and
    /// pulling it in for three assertions would put a Windows-only dependency
    /// on a suite that has none.
    /// </summary>
    private static string[] Names() =>
        [.. Program.CompositionModes().Select(m => m.ToString()!)];

    private static IDisposable Env(string? value)
    {
        var before = Environment.GetEnvironmentVariable("LIGHTBOX_COMPOSITION");
        Environment.SetEnvironmentVariable("LIGHTBOX_COMPOSITION", value);
        return new Restore(before);
    }

    private sealed class Restore(string? before) : IDisposable
    {
        public void Dispose() =>
            Environment.SetEnvironmentVariable("LIGHTBOX_COMPOSITION", before);
    }

    [Fact]
    public void TheSwapChainIsAskedForFirst()
    {
        using var _ = Env(null);
        var modes = Names();
        output.WriteLine(string.Join(" -> ", modes));
        Assert.Equal("LowLatencyDxgiSwapChain", modes[0]);
    }

    /// <summary>
    /// A driver that refuses the swap chain must still get a window. The list
    /// is a preference order, not a demand.
    /// </summary>
    [Fact]
    public void TheOrdinaryPathsRemainAsFallbacks()
    {
        using var _ = Env(null);
        var modes = Names();
        Assert.Contains("WinUIComposition", modes);
        Assert.Contains("RedirectionSurface", modes);
    }

    /// <summary>
    /// The escape hatch actually escapes: asked for the compositor, the swap
    /// chain is not in the list at all rather than merely demoted.
    /// </summary>
    [Fact]
    public void AskingForTheCompositorLeavesTheSwapChainOut()
    {
        using var _ = Env("compositor");
        var modes = Names();
        output.WriteLine(string.Join(" -> ", modes));
        Assert.Equal("WinUIComposition", modes[0]);
        Assert.DoesNotContain("LowLatencyDxgiSwapChain", modes);
    }

    /// <summary>
    /// The report names the path a session actually ran under. Two captures
    /// taken to compare them are worthless if neither says which it was, and
    /// that is not hypothetical — it is how the A/B this default came from was
    /// kept honest.
    /// </summary>
    [Theory]
    [InlineData(null, "swap chain")]
    [InlineData("compositor", "WinUI")]
    [InlineData("lowlatency", "swap chain")]
    public void TheReportNamesTheModeItRanUnder(string? asked, string expected)
    {
        using var _ = Env(asked);
        var said = Program.CompositionChoice;
        output.WriteLine($"{asked ?? "(unset)"} -> {said}");
        Assert.Contains(expected, said);
    }
}
