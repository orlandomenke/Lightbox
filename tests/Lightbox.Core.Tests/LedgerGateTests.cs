using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace Lightbox.Core.Tests;

/// <summary>
/// The ledger ids are unique, nothing silently deletes an entry, and both facts
/// are enforced before a push rather than after one.
/// </summary>
/// <remarks>
/// <para>
/// <b>B81.</b> Four ids collided on 2026-08-07 — <c>Q46</c>, <c>B123</c>,
/// <c>B124</c> and <c>B125</c>, each filed independently on <c>main</c> and on a
/// branch, for entirely unrelated things. The entry blamed the wrong thing twice
/// before this, and both wrong answers are worth writing down because they are the
/// obvious ones:
/// </para>
/// <para>
/// <b>Not</b> "the duplicate check only prints". It has exited non-zero since two
/// bugs shared <c>B39</c>, and CI runs it. <b>Not</b> "removing an answered entry
/// frees its number" either — that is what the ledger's own convention once
/// caused, and it is not what happened here. The cause is <b>timing</b>: a
/// collision does not exist in either branch, only in the merged file, so the
/// earliest CI can see one is after it has been pushed and other branches have
/// rebased onto it. The gate was real, correct, and downstream of the damage.
/// </para>
/// <para>
/// So the fix is <i>when</i> rather than <i>whether</i>: <c>bugs.py ids</c> is the
/// cheap half of <c>check</c> — no evidence anchors, no code index, no rebuild —
/// and <c>.githooks/pre-push</c> runs it on every push, which is the last moment a
/// bad resolution is still private.
/// </para>
/// <para>
/// It also closes the hole a duplicate check <i>cannot</i> see. Resolving a ledger
/// conflict by taking one side is the mechanical thing to do and it deletes the
/// other branch's entry — leaving a file with no duplicate in it, so every check
/// passes and the loss is permanent. That failure is strictly worse than a
/// duplicate: a duplicate is loud and costs a renumber.
/// </para>
/// <para>
/// These run the real script rather than asserting on its source, because "the
/// gate exists" and "the gate fails the build" are different claims and only the
/// second one is worth anything. The two wiring tests at the end are text
/// assertions in the manner of <see cref="CiDraftGateTests"/>, since a hook and a
/// workflow cannot be executed here.
/// </para>
/// </remarks>
public class LedgerGateTests(ITestOutputHelper output)
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>
    /// Run <c>bugs.py</c> and hand back what a caller actually decides on: the exit
    /// code and everything it said.
    /// </summary>
    private (int Code, string Out) Bugs(string args, string? allowDeletion = null)
    {
        var root = RepoRoot();
        foreach (var exe in new[] { "python3", "python" })
        {
            var info = new ProcessStartInfo(exe, $"scripts/bugs.py {args}")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (allowDeletion is not null)
            {
                info.Environment["LIGHTBOX_ALLOW_LEDGER_DELETION"] = allowDeletion;
            }

            Process? process;
            try
            {
                process = Process.Start(info);
            }
            catch (Exception)
            {
                continue; // not this interpreter's name on this machine
            }

            Assert.NotNull(process);
            var text = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(60_000);
            output.WriteLine($"$ {exe} scripts/bugs.py {args}  -> exit {process.ExitCode}");
            output.WriteLine(text.TrimEnd());
            return (process.ExitCode, text);
        }

        Assert.Fail("neither python3 nor python could be started — the ledger gate needs one, "
                    + "and CI as well as .githooks/pre-push both invoke python3 directly");
        return (0, "");
    }

    /// <summary>A ledger holding exactly the entries given, and nothing else.</summary>
    private static string Ledger(params (string Id, string Title)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bugs-{Guid.NewGuid():N}.md");
        File.WriteAllLines(path, entries.Select(e =>
            $"- [ ] **{e.Id}** `P2` `canvas` {e.Title} `evidence: manual`"));
        return path;
    }

    private static string Questions(params (string Id, string Title)[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"questions-{Guid.NewGuid():N}.md");
        File.WriteAllLines(path, entries.Select(e => $"## {e.Id} · {e.Title}"));
        return path;
    }

    [Fact]
    public void TwoBugsWithTheSameIdFailTheGate()
    {
        var ledger = Ledger(("B1", "A thing"), ("B1", "Something unrelated"));
        var (code, said) = Bugs($"ids --ledger={ledger} --questions={Questions()}");

        Assert.Equal(1, code);
        Assert.Contains("DUPLICATE ID B1", said);
        Assert.Contains("renumber all but one", said);
    }

    [Fact]
    public void TwoQuestionsWithTheSameIdFailTheGate()
    {
        // The half B81 is actually about: its collisions were in QUESTIONS.md.
        var questions = Questions(("Q46", "What colour is the accent"), ("Q46", "How does editing start"));
        var (code, said) = Bugs($"ids --ledger={Ledger()} --questions={questions}");

        Assert.Equal(1, code);
        Assert.Contains("DUPLICATE Q  Q46", said);
    }

    /// <summary>
    /// The failure a duplicate check can never see, because the file it leaves
    /// behind is perfectly consistent.
    /// </summary>
    [Fact]
    public void AMergeThatKeepsOnlyOneSideOfALedgerConflictFailsTheGate()
    {
        var before = Ledger(("B1", "A thing"), ("B2", "Something unrelated"));
        var after = Ledger(("B1", "A thing"));

        var (code, said) = Bugs($"ids --ledger={after} --questions={Questions()} {before}");

        Assert.Equal(1, code);
        Assert.Contains("LOST      ID B2", said);
        Assert.DoesNotContain("DUPLICATE", said);
    }

    /// <summary>
    /// Refusing every deletion would be a rule nobody could work around, so there
    /// is one way to say it is meant — and it has to be typed.
    /// </summary>
    [Fact]
    public void ADeletionCanBeAllowedDeliberately()
    {
        var before = Ledger(("B1", "A thing"), ("B2", "Something unrelated"));
        var after = Ledger(("B1", "A thing"));

        var (code, said) = Bugs(
            $"ids --ledger={after} --questions={Questions()} {before}", allowDeletion: "1");

        Assert.Equal(0, code);
        Assert.Contains("DELETED   ID B2", said);
    }

    /// <summary>
    /// The gate passing on a synthetic fixture proves the detector; this proves the
    /// tree. It is also what fails if a merge is resolved badly and committed.
    /// </summary>
    [Fact]
    public void TheLedgersInThisTreePassTheirOwnGate()
    {
        var (code, said) = Bugs("ids");
        Assert.Equal(0, code);
        Assert.Contains("ids unique, none lost", said);
    }

    [Fact]
    public void ThePrePushHookRunsTheLedgerGate()
    {
        var hook = File.ReadAllText(Path.Combine(RepoRoot(), ".githooks", "pre-push"));

        // Before the early exit, or it would only guard pushes to the default
        // branch — and the collision arrives on a feature branch.
        var gate = hook.IndexOf("bugs.py\" ids", StringComparison.Ordinal);
        var earlyExit = hook.IndexOf("[ -n \"$blocked\" ] || exit 0", StringComparison.Ordinal);
        output.WriteLine($"gate at {gate}, early exit at {earlyExit}");

        Assert.True(gate >= 0, "the pre-push hook no longer runs the ledger id gate");
        Assert.True(earlyExit >= 0, "the pre-push hook's early exit has moved — check this test");
        Assert.True(gate < earlyExit,
            "the ledger gate sits after the hook's early exit, so it only runs when pushing to "
            + "the default branch — a duplicate id arrives on a feature branch, so it must run first");
    }

    [Fact]
    public void CiChecksTheLedgerToo()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "build.yml"));
        Assert.Contains("scripts/bugs.py check", yaml);
        output.WriteLine("build.yml runs bugs.py check — the belt to the hook's braces, and the "
                         + "one that sees a push that bypassed the hook");
    }

    // -----------------------------------------------------------------------
    // The same idea one artefact along: the generated index.
    //
    // `INDEX.md` and `FEATURES.md` are committed and derived, which is the pair of
    // properties that lets them be wrong without anything failing. The merge
    // driver rebuilds them on a local merge; GitHub does not run merge drivers,
    // the driver gives up while the build is red, and an ordinary commit that adds
    // a type can simply forget. So the check is the same one the ledger uses —
    // derive the truth and compare it, rather than trusting the file.
    // -----------------------------------------------------------------------

    private (int Code, string Out) Codemap(string args)
    {
        var root = RepoRoot();
        var info = new ProcessStartInfo("python3", $"scripts/codemap.py {args}")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info);
        Assert.NotNull(process);
        var text = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(300_000);
        output.WriteLine($"$ python3 scripts/codemap.py {args}  -> exit {process.ExitCode}");
        output.WriteLine(text.TrimEnd());
        return (process.ExitCode, text);
    }

    /// <summary>
    /// The committed index describes this tree. Fails on the ordinary mistake —
    /// adding a type and not rebuilding — as well as on a badly merged index.
    /// </summary>
    [Fact]
    [Trait("Category", "Performance")]   // ~10s: it analyses the whole solution
    public void TheCommittedIndexDescribesThisTree()
    {
        var (code, said) = Codemap("verify");
        Assert.Equal(0, code);
        Assert.Contains("the index describes this tree", said);
    }

    [Fact]
    public void CiVerifiesTheCommittedIndex()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "build.yml"));
        Assert.Contains("scripts/codemap.py verify", yaml);

        // Not in the hook, and that is a decision rather than an omission: a full
        // analysis is about ten seconds against a few milliseconds for the ledger
        // ids, and a hook nobody can afford gets turned off.
        var hook = File.ReadAllText(Path.Combine(RepoRoot(), ".githooks", "pre-push"));
        Assert.DoesNotContain("codemap.py verify", hook);
        output.WriteLine("verify runs in CI, not in the hook — 10s versus milliseconds");
    }

    /// <summary>
    /// The property the whole check rests on: these two artefacts are derived from
    /// file contents and nothing else, so two runs agree and a shallow clone gets
    /// the same answer as a full one.
    /// </summary>
    [Fact]
    public void TheIndexCarriesNoCommitStampSoItIsReproducible()
    {
        var dir = Path.Combine(RepoRoot(), ".claude", "codemap");
        foreach (var name in new[] { "INDEX.md", "FEATURES.md" })
        {
            var head = File.ReadLines(Path.Combine(dir, name)).Take(6).ToList();
            output.WriteLine($"{name}: {string.Join(" ⏎ ", head)}");
            Assert.DoesNotContain(head, line => line.Contains("commit", StringComparison.OrdinalIgnoreCase));
        }

        // HOTSPOTS.md is the counter-example and is deliberately not committed —
        // it is built from git churn, so it would be wrong the moment it landed.
        var ignored = File.ReadAllText(Path.Combine(RepoRoot(), ".gitignore"));
        Assert.Contains("HOTSPOTS.md", ignored);
    }
}
