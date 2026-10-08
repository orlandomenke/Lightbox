using System.Diagnostics;
using Lightbox.Core.Serialization;
using Xunit;

namespace Lightbox.Core.Tests;

/// <summary>
/// B416. Every save writes a temporary file and moves it over the document. On
/// Windows that move fails while anything holds the document open without
/// delete sharing, which is exactly what a virus scanner or the search indexer
/// does, for a moment, to a file that was just written. The save failed with
/// "access denied" and the artist was told it had not saved.
/// </summary>
/// <remarks>
/// Found as a flaky test: <c>ProjectVersionsTests</c> lost that move twice in
/// one day under a full test run. The holder here is deterministic, which the
/// scanner never is. On a system where the move does not care who holds the
/// file, these pass trivially, which is the right answer there.
/// </remarks>
public class AtomicMoveTests
{
    private static string Temp() =>
        Path.Combine(Path.GetTempPath(), $"lightbox-atomic-{Guid.NewGuid():N}.json");

    /// <summary>Open the file the way a scanner does: reading, sharing everything but delete.</summary>
    private static FileStream Hold(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    [Fact]
    public void ASaveOverAFileSomethingBrieflyHoldsLandsOnceItLetsGo()
    {
        var path = Temp();
        try
        {
            File.WriteAllText(path, "old");
            var held = Hold(path);
            var release = Task.Delay(150).ContinueWith(_ => held.Dispose());

            DocJson.WriteAtomic(path, "new");

            release.Wait();
            Assert.Equal("new", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    /// <summary>
    /// A file held for good still fails, and promptly: the retry is a short
    /// wait for a scanner, not a hang, and the document on disk is untouched.
    /// </summary>
    [Fact]
    public void ASaveOverAFileHeldForGoodFailsPromptlyAndLeavesItWhole()
    {
        if (!OperatingSystem.IsWindows()) return; // elsewhere the move does not care
        var path = Temp();
        try
        {
            File.WriteAllText(path, "old");
            using (Hold(path))
            {
                var clock = Stopwatch.StartNew();
                // Windows reports the same hold either way: a sharing violation
                // or access denied.
                var refused = Record.Exception(() => DocJson.WriteAtomic(path, "new"));
                Assert.True(refused is IOException or UnauthorizedAccessException, $"{refused}");
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
            }
            Assert.Equal("old", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }
}
