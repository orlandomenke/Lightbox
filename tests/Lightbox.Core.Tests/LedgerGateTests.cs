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
    /// An entry that was renumbered is not an entry that went.
    /// </summary>
    /// <remarks>
    /// <b>B227, and the tool was tripping over its own advice.</b> The loss
    /// check compared ids alone, so a renumber — the entry still present under
    /// a different number — was indistinguishable from a deletion. That is
    /// exactly what <c>ids --fix</c> does, what the message printed beside the
    /// loss recommends ("keep both and renumber"), and what the pre-push hook
    /// runs for you.
    ///
    /// <para>
    /// On a merge it was permanent rather than momentary: the pre-renumber
    /// commit stays a merge parent for ever, so the old id is missing from
    /// every future comparison against it and no amount of further work clears
    /// the refusal.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARenumberedEntryIsNotReportedAsLost()
    {
        var before = Ledger(("B1", "A thing"), ("B2", "Something unrelated"));
        // B2 became B9 — the entry is here, under a new number.
        var after = Ledger(("B1", "A thing"), ("B9", "Something unrelated"));

        var (code, said) = Bugs($"ids --ledger={after} --questions={Questions()} {before}");

        Assert.DoesNotContain("LOST", said);
        Assert.Equal(0, code);
    }

    /// <summary>
    /// A renumber alongside a real deletion still catches the deletion.
    /// </summary>
    /// <remarks>
    /// The test that stops the fix above from being "stop reporting losses".
    /// One entry moves and another genuinely goes, in the same comparison; only
    /// the one that went is named.
    /// </remarks>
    [Fact]
    public void ARenumberDoesNotHideARealLossBesideIt()
    {
        var before = Ledger(("B1", "Kept"), ("B2", "Renumbered"), ("B3", "Genuinely deleted"));
        var after = Ledger(("B1", "Kept"), ("B9", "Renumbered"));

        var (code, said) = Bugs($"ids --ledger={after} --questions={Questions()} {before}");

        Assert.Equal(1, code);
        Assert.Contains("LOST      ID B3", said);
        Assert.DoesNotContain("LOST      ID B2", said);
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
    /// <c>check</c>'s own lost-id loop ran only when HEAD was a merge commit,
    /// and the first merge HEAD it ever saw raised a NameError instead of a
    /// report (B214) — the loop was written against a table that never
    /// existed, and nothing exercised it because nobody's HEAD happened to be
    /// a merge. An explicit ref forces the loop whatever HEAD is, which is
    /// what makes this deterministic: without the fix this run dies in a
    /// traceback, whatever the ledgers contain.
    /// </summary>
    [Fact]
    public void CheckSurvivesComparingAgainstAMergeParent()
    {
        var (_, said) = Bugs("check HEAD");

        Assert.DoesNotContain("Traceback", said);
        Assert.DoesNotContain("NameError", said);
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
        Assert.Contains("ids unique, unclashed, none lost", said);
    }

    // -----------------------------------------------------------------------
    // Detecting a collision is not preventing one.
    //
    // Every test above this line proves the gate *sees* a reused id. None of them
    // stopped one being written, because nothing here ever issued an id: an author
    // read the ledger, took the highest number in it and added one — which is the
    // same number on two branches that started from the same `main`. Six bugs and
    // three questions were renumbered by hand in the six days to 2026-08-14, one
    // of them twice because the second guess collided as well.
    //
    // The fixtures below hand in the other branches explicitly, so the allocator
    // is tested without a repository to test it in. In a real run the same list
    // comes from every ref the clone can see.
    // -----------------------------------------------------------------------

    [Fact]
    public void AnIdIsAllocatedAboveEveryBranchAndNotJustThisOne()
    {
        var mine = Ledger(("B1", "What my branch can see"));
        var theirs = Ledger(("B1", "What my branch can see"), ("B7", "What another branch filed"));

        var (code, said) = Bugs($"freeid bug --ledger={mine} --elsewhere={theirs}");

        Assert.Equal(0, code);
        // B2 is what counting by eye gives, and it is the number the other branch
        // is one merge away from proving wrong.
        Assert.Equal("B8", said.Trim());
    }

    [Fact]
    public void TheAllocatorIssuesQuestionIdsToo()
    {
        // Q46 and Q73-75 collided exactly as the bug ids did, and a question has no
        // domain to partition by even if the ledger were partitioned.
        var mine = Questions(("Q1", "Mine"));
        var theirs = Questions(("Q1", "Mine"), ("Q12", "Somebody else's"));

        var (code, said) = Bugs($"freeid question --questions={mine} --elsewhere={theirs}");

        Assert.Equal(0, code);
        Assert.Equal("Q13", said.Trim());
    }

    /// <summary>
    /// The collision caught before the merge that creates it — which is the only
    /// place it can be caught while it is still one branch's problem.
    /// </summary>
    [Fact]
    public void AnIdTakenOnTwoBranchesIsCaughtBeforeTheyMerge()
    {
        var pastBase = Ledger(("B1", "Already on main"));
        var mine = Ledger(("B1", "Already on main"), ("B2", "What I filed"));
        var theirs = Ledger(("B1", "Already on main"), ("B2", "What they filed"));

        var (code, said) = Bugs(
            $"ids --ledger={mine} --questions={Questions()} --base={pastBase} --elsewhere={theirs}");

        Assert.Equal(1, code);
        Assert.Contains("CLASHES   ID B2", said);
        // Not a duplicate: neither file has B2 twice. That is the whole point —
        // `duplicates_in` cannot see this until somebody merges.
        Assert.DoesNotContain("DUPLICATE", said);
    }

    /// <summary>
    /// The false positive that would make the clash check unusable: an id both
    /// branches carry because it was on the default branch before either started.
    /// </summary>
    [Fact]
    public void AnIdInheritedFromTheDefaultBranchIsNotAClash()
    {
        var pastBase = Ledger(("B1", "Already on main"));
        var mine = Ledger(("B1", "Already on main, retitled here"));
        var theirs = Ledger(("B1", "Already on main"));

        var (code, said) = Bugs(
            $"ids --ledger={mine} --questions={Questions()} --base={pastBase} --elsewhere={theirs}");

        Assert.Equal(0, code);
        Assert.DoesNotContain("CLASHES", said);
    }

    [Fact]
    public void FixMovesTheEntryThisBranchFiledAndLeavesTheOlderOneAlone()
    {
        var ledger = Ledger(
            ("B1", "The one that was here first"),
            ("B1", "The one this branch filed"),
            ("B4", "The highest id anybody has"));

        var (code, said) = Bugs($"ids --ledger={ledger} --questions={Questions()} --fix");
        var after = File.ReadAllLines(ledger);

        Assert.Equal(0, code);
        Assert.Contains("RENUMBERED B1 -> B5", said);
        Assert.Contains("**B1**", after[0]);   // first in, keeps the number
        Assert.Contains("**B5**", after[1]);   // and above B4, not merely unused here
        Assert.Contains("**B4**", after[2]);
    }

    // -----------------------------------------------------------------------
    // The questions are one file each (Q92), and the gate reads their ids from
    // the filenames.
    //
    // Fixing the id collision left the textual one: as a single file, every branch
    // that raised a question appended a section at the same place, so two branches
    // raising two questions conflicted by construction. A new file conflicts with
    // nothing — and the id being in the name is what keeps the gate cheap, because
    // listing a ref's questions becomes one `git ls-tree` with no file reads.
    // -----------------------------------------------------------------------

    private static string RepoQuestions() =>
        Path.Combine(RepoRoot(), ".claude", "quality", "questions");

    [Fact]
    public void EveryQuestionFileIsNamedForTheQuestionInIt()
    {
        var (code, said) = Questions("check");
        output.WriteLine(said.TrimEnd());
        Assert.Equal(0, code);
    }

    /// <summary>
    /// The id lives in two places once a question is a file, and only one of them
    /// is read by the gate. They have to agree or the heading is decorative.
    /// </summary>
    [Fact]
    public void AQuestionWhoseNameAndHeadingDisagreeFailsItsCheck()
    {
        var stray = Path.Combine(RepoQuestions(), "Q9999-a-deliberately-misnamed-fixture.md");
        File.WriteAllText(stray, "# Q9998 · A deliberately misnamed fixture\n");
        try
        {
            var (code, said) = Questions("check");
            Assert.Equal(1, code);
            Assert.Contains("MISNAMED", said);
        }
        finally
        {
            File.Delete(stray);
        }
    }

    /// <summary>
    /// The index is derived from the directory, so it is not committed — the same
    /// move `INDEX.md` and `FEATURES.md` made in Q55, and for the stronger form of
    /// the same reason: a stored derived file is one every branch rewrites, which
    /// is the collision this shape exists to end.
    /// </summary>
    [Fact]
    public void TheGeneratedQuestionIndexIsNotTracked()
    {
        var info = new ProcessStartInfo("git", "ls-files .claude/quality/QUESTIONS.md")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info);
        Assert.NotNull(process);
        var tracked = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(30_000);

        Assert.True(tracked.Length == 0,
            "QUESTIONS.md is tracked again. It is generated from .claude/quality/questions/ by "
            + "scripts/questions.py, so committing it puts a rewritten copy in every branch and "
            + "moves the conflict one artefact along instead of ending it (Q92).");
    }

    /// <summary>
    /// B211. `-p P3` is the spelling anybody types first, and it was refused with
    /// "unknown priority ''" because only `-p=P3` and `-pP3` were read.
    /// </summary>
    [Theory]
    [InlineData("-p PX")]
    [InlineData("-p=PX")]
    [InlineData("-pPX")]
    public void SpaceSeparatedFlagsAreAccepted(string spelling)
    {
        // A deliberately invalid priority, so the value the parser read comes back
        // in the error and nothing is written to the real ledger. Before the fix
        // the space-separated form reported `unknown priority ''` — the flag's
        // value was never read at all, so `-p P1` was refused just as loudly.
        var (code, said) = Bugs($"new project \"A title\" {spelling} --no-fetch");

        output.WriteLine(said.TrimEnd());
        Assert.Equal(2, code);
        Assert.Contains("unknown priority 'PX'", said);
    }

    private (int Code, string Out) Questions(string args)
    {
        var info = new ProcessStartInfo("python3", $"scripts/questions.py {args}")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(info);
        Assert.NotNull(process);
        var text = process!.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);
        return (process.ExitCode, text);
    }

    /// <summary>
    /// Refusing leaves the author to work out which entry gives up the number, what
    /// number is safe, and which citations in their diff mean the one that moved.
    /// That is what the nine hand-written renumber commits were.
    /// </summary>
    [Fact]
    public void ThePrePushHookRepairsRatherThanOnlyRefusing()
    {
        var hook = File.ReadAllText(Path.Combine(RepoRoot(), ".githooks", "pre-push"));

        Assert.Contains("ids --fix", hook);
        // Never while a merge is being resolved: rewriting BUGS.md under somebody
        // resolving a conflict in BUGS.md destroys the resolution.
        Assert.Contains("MERGE_HEAD", hook);
        // And never for a lost id — putting an entry back is a judgement about
        // what it said, which a number cannot supply.
        Assert.Contains("grep -q 'LOST'", hook);
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

    /// <summary>
    /// Every <c>subprocess.run</c> in <c>scripts/</c> that reads output as text names
    /// its encoding, because the default is the locale's and the ledgers are full of
    /// characters cp1252 has no mapping for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>text=True</c> without <c>encoding=</c> decodes git's output with
    /// <c>locale.getpreferredencoding()</c>. On Linux that is UTF-8 and the bug is
    /// invisible; under a Windows console codepage it is cp1252, and the house style's
    /// em dashes and arrows are exactly what it cannot decode.
    /// </para>
    /// <para>
    /// The failure is worse than a crash. <c>subprocess</c> drains the pipe on a helper
    /// thread, so a <c>UnicodeDecodeError</c> kills that thread rather than raising to
    /// the caller: the pipe is never drained and the parent blocks on it indefinitely.
    /// <c>bugs.py ids</c> did that for fourteen minutes and took a whole
    /// <c>dotnet test</c> run down with it — and the same call is in
    /// <c>.githooks/pre-push</c>, where a hang blocks every push.
    /// </para>
    /// <para>
    /// A ratchet rather than a one-off repair, on purpose. The original fix decorated
    /// every call that existed at the time; by the time it was merged forward,
    /// <c>main</c> had grown another one that was not. A fix naming only today's
    /// callers is reopened by the next person to add one.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryTextModeSubprocessCallInTheScriptsNamesItsEncoding()
    {
        var scripts = Directory.GetFiles(Path.Combine(RepoRoot(), "scripts"), "*.py");
        Assert.NotEmpty(scripts);

        var undecorated = new List<string>();
        var scanned = 0;
        foreach (var path in scripts)
        {
            var src = File.ReadAllText(path);
            for (var at = src.IndexOf("subprocess.run(", StringComparison.Ordinal); at >= 0;
                 at = src.IndexOf("subprocess.run(", at + 1, StringComparison.Ordinal))
            {
                var open = src.IndexOf('(', at);
                int depth = 0, close = open;
                for (; close < src.Length; close++)
                {
                    if (src[close] == '(') { depth++; }
                    else if (src[close] == ')' && --depth == 0) { break; }
                }

                var call = src[open..Math.Min(close + 1, src.Length)];
                scanned++;

                // Byte mode decodes nothing, so it cannot hit this.
                var textMode = call.Contains("text=True", StringComparison.Ordinal)
                               || call.Contains("universal_newlines=True", StringComparison.Ordinal);
                if (!textMode || call.Contains("encoding=", StringComparison.Ordinal))
                {
                    continue;
                }

                var line = 1;
                const char newline = (char)10;
                for (var i = 0; i < at; i++)
                {
                    if (src[i] == newline) { line++; }
                }

                undecorated.Add($"{Path.GetFileName(path)}:{line}");
            }
        }

        output.WriteLine($"{scripts.Length} scripts, {scanned} subprocess.run calls, "
                         + $"{undecorated.Count} undecorated");

        Assert.True(undecorated.Count == 0,
            "these read subprocess output as text without naming an encoding, so they decode with "
            + "the locale codec and block forever on a byte it cannot map: "
            + string.Join(", ", undecorated));
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
    /// The derived index is not committed — the decision that ended the merge
    /// conflicts, pinned.
    /// </summary>
    /// <remarks>
    /// <b>Q55, 2026-08-08.</b> INDEX.md and FEATURES.md are rewritten end to
    /// end by every branch that touches code, so any two parallel branches
    /// conflicted on them by construction — and GitHub runs no merge driver,
    /// so every open pull request went red the moment any other one merged,
    /// round after round. The old answer was a committed copy defended by a
    /// local merge driver and a CI byte-verify; the stronger form of that
    /// design's own argument ("a committed derived file is not believed, it
    /// is recomputed") is to commit nothing: the session hook builds the
    /// index when it is stale or absent, and CI builds it to prove the tree
    /// parses. This test is what turns re-committing them from an accident
    /// into a decision.
    /// </remarks>
    [Fact]
    public void TheDerivedIndexIsNotTracked()
    {
        var info = new ProcessStartInfo("git", "ls-files .claude/codemap")
        {
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(info);
        Assert.NotNull(process);
        var tracked = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(30_000);

        output.WriteLine(tracked.Length == 0 ? "(nothing tracked under .claude/codemap)" : tracked);
        Assert.True(tracked.Length == 0,
            $"derived codemap files are committed again — every parallel branch will conflict on them:\n{tracked}");

        var ignored = File.ReadAllText(Path.Combine(RepoRoot(), ".gitignore"));
        Assert.Contains(".claude/codemap/INDEX.md", ignored);
        Assert.Contains(".claude/codemap/FEATURES.md", ignored);
    }

    /// <summary>
    /// No entry names <c>manual</c> beside a real evidence anchor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>bugs.py</c> matches <c>manual</c> as <b>exactly</b> <c>["manual"]</c>, so
    /// mixing it with an anchor is not a slightly-worse evidence line — it drops
    /// the entry out of <em>every</em> category the report prints. Not manual, so
    /// it leaves the verify-by-hand list a person actually reads; and <c>manual</c>
    /// is then resolved as though it were a code symbol, which it never is, so the
    /// checkbox stays open for an accidental reason instead of a stated one.
    /// </para>
    /// <para>
    /// <b>Found by making the mistake.</b> B30's measurement moved into
    /// <c>tools/Lightbox.Bench</c> and the entry gained
    /// <c>evidence: PaintingRebuild, manual</c>. Nothing looked wrong in the file,
    /// <c>check</c> stayed green, the mark did not move — and the bug silently
    /// stopped being listed anywhere. That is the exact shape of failure the ledger
    /// tooling exists to refuse, so it gets a gate rather than a note.
    /// </para>
    /// <para>
    /// Asserted against the real ledger rather than a synthetic one on purpose:
    /// <c>check</c> takes no <c>--ledger</c> override (only <c>ids</c> does), and
    /// the property worth guarding is that <em>this</em> file stays clean. A
    /// supporting measurement belongs in an entry's prose, which is where B30 now
    /// names its bench scenario.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoEntryNamesManualBesideARealAnchor()
    {
        var ledger = File.ReadAllLines(Path.Combine(RepoRoot(), ".claude", "quality", "BUGS.md"));
        var offenders = new List<string>();

        foreach (var line in ledger)
        {
            var at = line.IndexOf("`evidence:", StringComparison.Ordinal);
            if (at < 0) continue;
            var close = line.IndexOf('`', at + 1);
            if (close < 0) continue;

            var anchors = line[(at + "`evidence:".Length)..close]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (anchors.Contains("manual") && anchors.Length > 1)
            {
                offenders.Add(line.Trim()[..Math.Min(140, line.Trim().Length)]);
            }
        }

        foreach (var offender in offenders) output.WriteLine(offender);
        Assert.True(offenders.Count == 0,
            "'manual' must stand alone in an evidence line — mixed with an anchor, the entry "
            + $"falls out of every report while looking fine:\n{string.Join('\n', offenders)}");
    }

    [Fact]
    public void CiBuildsTheIndexRatherThanVerifyingACommittedOne()
    {
        var yaml = File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "build.yml"));
        Assert.Contains("scripts/codemap.py build", yaml);
        Assert.DoesNotContain("scripts/codemap.py verify", yaml);

        // Not in the hook, and that is a decision rather than an omission: a full
        // analysis is about ten seconds against a few milliseconds for the ledger
        // ids, and a hook nobody can afford gets turned off.
        var hook = File.ReadAllText(Path.Combine(RepoRoot(), ".githooks", "pre-push"));
        Assert.DoesNotContain("codemap.py", hook);
        output.WriteLine("the index builds in CI, never in the hook — 10s versus milliseconds");
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
            // Untracked since Q55, so a fresh checkout may not have built them
            // yet — absence is fine; a commit stamp in a built one is not.
            var path = Path.Combine(dir, name);
            if (!File.Exists(path))
            {
                output.WriteLine($"{name}: not built in this checkout — nothing to inspect");
                continue;
            }
            var head = File.ReadLines(path).Take(6).ToList();
            output.WriteLine($"{name}: {string.Join(" ⏎ ", head)}");
            Assert.DoesNotContain(head, line => line.Contains("commit", StringComparison.OrdinalIgnoreCase));
        }

        // HOTSPOTS.md is the counter-example and is deliberately not committed —
        // it is built from git churn, so it would be wrong the moment it landed.
        var ignored = File.ReadAllText(Path.Combine(RepoRoot(), ".gitignore"));
        Assert.Contains("HOTSPOTS.md", ignored);
    }
}
