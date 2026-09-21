# Sensitive topics, and the two-track flow

Written 2026-09-21. Decisions: Q190 (what is guarded) and Q191 (how much of the
factory). Rules: `.claude/quality/SENSITIVITY.md`. Routing: `.claude/quality/FLOW.md`.
Reasons and cases: the `sensitivity` skill. This document is the argument for the
whole — what was borrowed, what was refitted, what was left, and what is not built.

## What was asked

A sibling project, a user-space shell for Windows, is developed by a looping pipeline
of specialised agents: an architect, a test author, an implementer, code, security
and performance guardians, QA, and a **coach** that watches a ledger of how each cycle
went and trains the others. The request was to bring that to Lightbox — *translated
carefully, not copied* — for an application positioned against Photoshop, Krita, GIMP,
Moho and Toon Boom, and to make it take **sensitive topics** into account. The answers
to two questions settled the rest: guard all four areas of sensitivity, and adopt the
full pipeline **in two tracks**, so small things are still fixed inline, with a scout
choosing the track.

## What Lightbox already had

Most of the factory's machinery was already here under other names, which is why this
is a small change rather than a large one:

| The sibling has | Lightbox has |
| --- | --- |
| codemap, heatmap | `codemap.py`, `HOTSPOTS.md` |
| numbered gates | `CHARTER.md` G1–G12 |
| guardian agents | `adversary`, `leak-hunter`, `ui-critic`, `perf-warden`, `feature-guard` |
| two reviewers who disagree on purpose | `ai-engineer` + `art-director`, with named vetoes |
| ledgers | `BUGS.md`, `ROADMAP.md`, `QUESTIONS.md`, ratchets |
| an ADR for a decision | a question file with what each option costs |
| a per-cycle work folder | the pull request |
| a token-cost budget | the prelude ratchet (fixed cost only — see *Not built*) |

## The translation, piece by piece

| Piece | What Lightbox did | Why it is not a copy |
| --- | --- | --- |
| `security.md` — trust boundaries and numbered rules | **`SENSITIVITY.md`**, rules in four areas: A (artist data and AI consent), L (IP and licensing), W (work is never lost), S (security and untrusted input) | The sibling's rules are about a process that watches a desktop and acts on it. Lightbox's are about a drawing that is irreplaceable, an audience that distrusts AI, other tools' formats, and a public GPL repository |
| `security-guardian` | **`sensitivity-guardian`**, on the strongest model | Reviews for loss, leak, lift and hostile input rather than for privilege and injection; reads the project's own known gaps |
| `guard.py` hooks | **`.claude/hooks/guard.py`** and a `grep` Read hook: refuse a credential, ask before an owner-only file, refuse commands that read the artist's key store | The sibling guards against elevation and `iwr \| iex`. The live risk here is a **real key from the developer's own `%APPDATA%\Lightbox\ai.json` landing in an agent transcript**, which goes to a provider |
| the strict 8-stage flow | **two tracks** behind a router, `FLOW.md` | One pipeline for every change would make fixing dearer and recreate the ledger's 79%-left-open measurement (`CLAUDE.md`: *Fixing is the default*) |
| return rules and limits | kept in shape; the numbers are starting values | Six returns / three of the same came from the sibling's own history and have none here yet |
| `mission.md` — owned by the user | **owner-only files**: `SENSITIVITY.md`, `FLOW.md`, the charter, `CLAUDE.md`, `settings.json`, `guard.py` | Same idea; enforced by an *ask* hook, not a block, so the owner can still ask a session to edit them |
| `architect`, `test-author`, `implementer` agents | **not new**: `story-analyst`, `test-smith`, and the main thread | The sibling's own rule is *extend before adding an agent*. `story-analyst` and `test-smith` already exist; the implementer is deliberately the main thread, which holds the context and is the one the owner is talking to |
| the coach, ledger, recall, token budgets | **specified below, not built** | A coach needs a ledger to read and a ledger needs cycles to have run |

**Deliberately not ported:** admin-rights and machine-wide-install rules (Lightbox is an
ordinary desktop app), the portable-core ADR, the native-code-behind-an-ADR gate, and the
sibling's numbers (its token thresholds, latency budgets, stage limits). Each answers a
question Lightbox does not have, or has with different answers.

**Two agents were added, and each justifies itself** against the sibling's rule that the
default answer to "add an agent" is "extend a skill": an agent is warranted when a
responsibility needs *independent judgement*. `sensitivity-guardian` must not share the
author's context — the person who wrote the change is the worst reviewer of whether it
leaks — and `triage` must not be the implementer deciding its own work is small.

## The router

**Sensitivity-first, not size-first.** The obvious router is a line count and its obvious
failure is a one-line change to where an API key is read from. So the router sends a
change to FULL if it touches a sensitive path, an owner-only file, a hotspot, a new source
file, or exceeds a size — and size only chooses between tracks once nothing else has.

**The sensitive paths are data in the owner's file**, in a JSON block at the foot of
`SENSITIVITY.md`, so a session that wants a change through the fast track cannot narrow the
list. `sensitivity.py selftest` fails when a glob matches no file, because a stale trigger
looks identical to a working one.

**The fast track is not a hole.** It skips ceremony and never a check: the hooks and
`sensitivity.py scan` run on both tracks, and both cost no tokens.

**A track is raised and never lowered**, except by the owner. `triage` can move a change up
when it sees something the router cannot (two objectives, a decision hiding in a request).

**Splitting a big change into small ones to stay fast** is the obvious way to game it.
Two things blunt it: mid-work `triage` reads the whole branch's diff against `main`, not
the last commit, so splitting across commits changes nothing; and splitting across
*branches* is what `CLAUDE.md` already asks for — one objective each — at which point each
is triaged on its own, and a sensitive one is FULL however small. What remains open is a
sequence of individually-small, individually-ordinary branches that add up to something
large; that is a review problem, and the owner reviews every pull request.

### Calibration

The first version counted every changed line, at 120. **Replayed over the last 40 merged
pull requests it sent 31 to FULL**, because tests come with every change; the fast track
would have been nearly empty, and an unused fast track teaches people to describe their
change as smaller than it is. Tests, the ledgers and the manual are therefore not counted.
With source only, at 150 lines and 5 files: **23 FAST, 17 FULL**; of the 29 that touched
`src/`, 11 (38%) hit a sensitive path, mostly the document model. The hotspot cut, 0.15, is
chosen so the six riskiest files on `HOTSPOTS.md` that day are FULL.

The replay shows the *distribution*, not whether the FULL ones needed it. That needs the
ledger below. **Recalibrate by replay when it exists.**

## What the first run found

`sensitivity.py scan --all` over the existing tree: 0 blocking. Its first attempt found
three, all in the *inventory*, not the code — the process-launch allowlist had missed
`VideoReferenceImporter`, and a doc comment mentioning `Process.Start` was read as a call.
Both were the scan being wrong, fixed by the allowlist and by skipping comment lines.

The guardian's own first run then verified the four items the baseline had marked
unverified, against the code rather than against the baseline's wording, and found
the wording wrong in one direction and right in another:

- **A6 was flatly wrong.** The baseline guessed the stroke record carried no AI
  provenance. `Frame.Ai` (`AiProvenance`) already exists, is stamped on every AI-
  and MCP-authored frame, and round-trips — found by reading `Lightbox.Core.Documents`
  rather than assuming from outside it. Corrected in place.
- **A4 and A3/A5 were under-stated in one direction and confirmed true in
  another.** The key's masking in the UI and its absence from every log and MCP
  path were already right; only the value at rest is a real gap. The lack of any
  first-use disclosure of what a provider receives was confirmed, not merely
  suspected.
- **One new, live defect: S2.** `StrokePayload`'s `Math.Clamp` calls pass a `NaN`
  straight through, and inbound point count is uncapped where outbound already is.
  Filed as **B377** rather than fixed here, per *finish the branch in hand first* —
  this branch's objective is the sensitivity system, not the MCP payload path.
- **A1 resolved outright.** The font catalogue fetch on opening the browser carries
  no artist data; only choosing a face sends a name. Not a gap.

This is the case for running the guardian **before** merging the baseline that
created it, not only after: three of five corrections went the other way from what
a cautious first draft assumed, and a baseline that had shipped its guesses
unchecked would have sent every later reviewer chasing a document format change
(A6) that had already been built, while under-selling how real the A4/A3 gaps are.

**The adversary's first run found the hooks weaker than claimed, and fixed before
merge:**

- **The credential shapes were incomplete for this app's own providers.** The
  regex matched Anthropic keys but missed OpenAI's current `sk-proj-`/`sk-svcacct-`
  and OpenRouter's `sk-or-v1-` — both hyphenated in a way a plain
  `sk-[A-Za-z0-9]{32,}` alphanumeric class rejects, and both are providers
  `AiProviders` supports. Broadened, and the deliberate fixture still passes.
- **A heuristic that blanks a text-verb's quoted argument outright missed that a
  quoted argument can still execute.** `echo "$ANTHROPIC_API_KEY"` was allowed
  because `echo` is treated as printing inert text, but a `$`-expansion inside the
  quotes is not inert — it is the whole risk. Fixed by leaving a quoted span
  unblanked when it contains `$` or a backtick.
- **The same blanket blanking produced a real false positive** the other
  direction: a heredoc's real newlines defeated the (then newline-splitting)
  blanking, so an ordinary commit message — `"B376: write more carefully to
  Lightbox/ai.json"` — tripped the reader rule on the English word "more". Fixed
  properly rather than papered over: a heredoc's body is stdin text, not an
  argument, so it is stripped before matching *unless* it is fed to something that
  would actually execute it (`bash`, `python`, …), which is tested explicitly so
  the fix cannot silently swallow a real risk along with the false one.
- **Two hook-bypasses were pure gaps**, not disguise: a bare `env`/`printenv`/`set`
  dumps every configured key with no key name in the command for the existing rule
  to match, and `git -c core.hooksPath=/dev/null commit` disables hooks without
  ever writing `--no-verify`. Both added.
- **The router under-matched its own subject.** `ConfiguredArtist.cs` and
  `MainViewModel.Ai.cs` — the App-layer files that actually assemble and dispatch
  an AI request — matched no sensitive-path glob and got no G12 pair, because the
  glob list named only `src/Lightbox.Ai/*` and the pair-selection heuristic
  (`"/Ai" in path`) does not match a dot before "Ai" or a file named
  `ConfiguredArtist.cs` that never spells "Ai" at all. Both are now named
  explicitly, and the review pair now has its own list (`ai_pair_paths`) instead of
  overloading area A, which also covers files — the font source, the diagnostic
  log — that are not AI-dispatch code and do not need `ai-engineer`/`art-director`.
- **Confirmed and left as-is:** the `Read` hook's grep-based key-store block
  behaves the same across path styles and casing, and the scan's own selftest is
  not vacuous — deleting its process-launch check would fail an assertion that
  exercises `scan_file` directly, not through git.

## Costs, stated

- **Hooks add latency to every tool call.** Measured: a Python hook costs about 85 ms of
  interpreter start on a real interpreter and about 280 ms on Windows' Store `python3` alias,
  which is what a bare `python3` resolves to on this machine; the command picks the real one.
  Measured end to end through `bash -c`: ~310 ms for Write/Bash, ~220 ms for Read even
  as a `grep`. `Read` is the most frequent tool, which is why its check is a one-line `grep`
  rather than an interpreter. If it bothers, the lever is `matcher`, not the rules.
- **The guardian runs on the strongest model.** It runs on a minority of diffs (38% touched a
  sensitive path in the replay) and a wrong answer costs a drawing or a licence. That is a
  choice; `model:` in its frontmatter is the knob.
- **The hooks fail open.** A guard that is broken must not take the session with it, so an
  internal error allows the call and says so. The selftest is what keeps that honest, and CI
  runs it.
- **An *ask* does not proceed unattended.** A run with nobody to answer stops at an owner-only
  file. That is the intended default for a file that says what agents may not do.
- **The scan is a heuristic floor.** It finds credential shapes, network and process use
  outside the inventory, a new dependency, a new asset, an instruction aimed at an agent. It
  does not find a payload that is too large, a table that was lifted, or a migration that
  changes the picture. Regex misses are expected; the guardian is what reads.
- **`CLAUDE.md` grew by 626 characters** (`PRELUDE.md` records why), paid by every agent in
  every session. The agent definitions and the skill are on-demand and are not in that number.
- **Hooks take effect in the next session.** A session already running does not pick up a
  changed `settings.json`.

## Not built: the second branch

Specified here so that it is refitted, not copied, when it is built.

| Piece | Refit for Lightbox |
| --- | --- |
| **Cycle ledger** | The unit is the **pull request**, not the sibling's "cycle": one line per merged PR — track, triage's verdict, reviewers run, returns and their edges, and how long it stayed open. Stored under `.claude/quality/ledger/`. Not derivable from git alone (returns and verdicts are not in a diff), so it is not a derived file and *is* committed — with the ledgers' rule that both sides of a merge keep their lines |
| **Coach** | Runs after a batch of merged PRs, not after every change. Same three tiers, mapped onto Lightbox's files: *self-apply* — skill examples, a checklist line; *propose* — an agent's role, tools or model, a gate's wording, the router's numbers; *owner only* — `SENSITIVITY.md`, `FLOW.md`, the charter, `CLAUDE.md`, hooks. Every change cites at least two merged PRs or one owner-flagged fault. It may not edit its own definition |
| **Recall** | One search over `BUGS.md`, `questions/`, `DECISIONS.md`, `LOOP.md`, `docs/DESIGN-*.md` and the skills, with a source id per hit. Lightbox has a `find` per ledger and nothing across them, so "has this been decided" means five greps. No model needed |
| **Token budgets** | The prelude ratchet prices the *fixed* cost of a session. What is unmeasured is what an agent *run* costs — how many turns, how many searches the codemap should have answered. Measure first from the session transcripts; **set no thresholds until there is a baseline** — the sibling calibrated its own from 41 stage runs, and Lightbox's numbers will be different |
| **Turn nudge** | A `PostToolUse` hook that tells a subagent it has spent its tool-call budget. Only worth having once the budgets above exist |
| **Drift audit** | Every N merged PRs, compare what merged against `ROADMAP.md`'s order and the charter's standing objectives. Lightbox's `LOOP.md` journal is the closest thing today, and is written by hand |

**Why this order.** Nothing in the second branch can be tuned without the ledger, and the
ledger has nothing in it until the pipeline has run. Building the coach first would have
produced an agent reviewing an empty file.
