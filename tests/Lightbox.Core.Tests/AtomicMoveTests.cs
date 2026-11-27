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
    /// What <see cref="DocJson.MoveIntoPlace"/> sleeps in total before it gives
    /// up: 50 + 100 + 200 + 400 + 800 ms, i.e. its "about 1.5 s". Copied rather
    /// than read, because the retry schedule is a local loop, not a constant.
    /// </summary>
    private static readonly TimeSpan RetrySleep = TimeSpan.FromMilliseconds(1550);

    /// <summary>
    /// How long a refused save may take before it counts as a hang: the retry
    /// sleep plus 4.5 s of headroom, so 6 s.
    /// </summary>
    /// <remarks>
    /// The bound used to be 3 s, under 2x the sleep, and it failed once at
    /// 3.14 s in a full <c>testplan.py</c> run with four test legs on one
    /// machine. That was a slow pass, not a hang: Thread.Sleep wakes late on a
    /// busy CPU, and the first exception and file move pay their cold-start
    /// cost there too. So the slowest pass seen took about 1.6 s on top of the
    /// sleep, and 4.5 s is roughly three times that. What this test exists to
    /// catch still fails: a retry with no end (the wait below gives up at the
    /// bound instead of hanging the suite) or a schedule several times longer.
    /// Only a small change, such as one more doubling (to 3.15 s), passes, and
    /// that is a design change for review, not a hang. This is the same
    /// "catch order-of-magnitude regressions, not drift" rule the performance
    /// budgets follow (CLAUDE.md).
    /// </remarks>
    private static readonly TimeSpan HangBound = RetrySleep + TimeSpan.FromSeconds(4.5);

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
                // On its own thread, so a save that never gives up fails here at
                // the bound instead of hanging the test run.
                var save = Task.Factory.StartNew(() => DocJson.WriteAtomic(path, "new"),
                    TaskCreationOptions.LongRunning);
                Assert.True(Task.WaitAny([save], HangBound) == 0,
                    $"still retrying after {clock.Elapsed}, past the {HangBound} bound");
                // Windows reports the same hold either way: a sharing violation
                // or access denied.
                var refused = save.Exception?.InnerException;
                Assert.True(refused is IOException or UnauthorizedAccessException,
                    $"{refused?.ToString() ?? "the save succeeded"} after {clock.Elapsed}");
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
