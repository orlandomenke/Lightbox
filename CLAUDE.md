# Lightbox

A raster + vector desktop application for **frame-by-frame animation** and
**digital painting**, with AI assistance throughout — most visibly filling in
the inbetweens. C#/.NET 10, Avalonia 12, SkiaSharp.

**This repository is public, and licensed GPL-3.0.** Everything written into it
is published the moment it is pushed — including `BUGS.md`, `QUESTIONS.md` and
`ROADMAP.md`, which are candid by design about what is broken, what was decided
badly, and what is not built yet. That candour is the point and it should not
change: a ledger that flatters the project is worth nothing. What *does* change
is that it is now a choice rather than a private note. Two consequences worth
holding on to:

- **Nothing private goes in.** No keys, no customer names, no anything that
  would be a problem in a search result. The one deliberate test fixture is
  `sk-ant-test`, which is not a key.
- **Write for a reader who is not here.** The ledgers already explain *why*
  rather than *what*, which is what makes them survive being read by a stranger
  — keep it that way, and the file stays useful to the next contributor as well
  as to the next session.

Contributions are not being accepted while Lightbox is alpha
(`CONTRIBUTING.md`), because sole copyright is what keeps relicensing possible.

## What it is for, and how that settles arguments

Two first-class purposes, not one with a hobby attached:

1. **Frame-by-frame animation.** Drawing on paper, one frame at a time:
   exposure sheets, holds, onion skin, flipping, animating on 2s. The unit of
   work is a sequence, and anything that makes a single drawing nicer at the
   expense of handling two hundred of them is a bad trade.
2. **Digital painting.** A single image worth finishing: real media, pigment
   that layers like pigment, brushes an artist can tune and import from the
   tools they already own.
3. **AI assistance** serving both — inbetweening, reference generation, and
   an MCP surface so an agent can work the document directly.

### Two output targets, and neither is the default

Animation here lands in one of two places, and a feature that serves one must
not tax the other:

- **Assets** — sprite sheets and character cycles for a game. The canvas *is*
  the output. There is no camera, frame bounds must stay consistent, and every
  frame is a deliverable.
- **Shots** — sequences for a film or show. The canvas is a world, a camera
  frames part of it, and the deliverable is what the camera saw.

The consequence is that shot-level machinery — camera, multiplane, parallax —
is **opt-in and absent until asked for**. A document that never adds a camera
must serialize, render and export exactly as it does today, and must never
show camera UI or pay for one. "Optional" here means absent, not disabled.

### Absent by default is not the same as out of reach

There is a third thing that is neither, and it is the one to refuse:

> **Every feature is reachable in every project type. A project type sets
> defaults, never availability.**

An artist doing a comic who wants an exposure sheet gets one; somebody drawing a
single illustration who wants a camera can have it. A project type decides what
is *on*, what is *in front of you*, and what a new document starts with — never
what the application can do.

These two rules govern different things and both hold at once:

| Rule | What it governs |
| --- | --- |
| *Optional means absent, not disabled* | The **record** and the **UI**. Unused writes no keys and shows no controls. |
| *Every feature is reachable* | The **capability**. Nothing is locked behind a value in a manifest. |

The camera is already the proof of all three: absent from the file until
authored, absent from the UI until asked for, and askable for anywhere. So when
a feature arrives framed as "this is for feature film" or "this is for games",
that describes **which project type turns it on by default** — not who is allowed
to have it. `ROADMAP.md` → *Reach and configuration* carries the plan and the one
place the codebase currently breaks this.

Most scope questions answer themselves once asked against these. Some worked
examples, so the reasoning is reusable:

- *"How much of Photoshop's brush panel should we take?"* — the parts that
  change how a **mark reads** and that `.abr`/`.kpp` files actually carry.
  Not the parts that only pay off on a single illustration you will spend a
  day on, because every one of them also has to survive being replayed across
  two hundred frames.
- *"Should a simulation be allowed?"* — yes, if it is deterministic. A stroke
  is replayed on load, on undo, and by the inbetweener; a mark that cannot be
  reproduced exactly is not a mark, it is a one-off. This is why invariant 2
  is absolute rather than a preference.
- *"Should this be per-document or per-preference?"* — if it reaches pixels,
  per stroke. An artist who returns to a scene after a month must find it
  exactly as they left it.
- *"Is flicker acceptable?"* — no. An effect that varies subtly between
  similar strokes looks fine on one image and boils at 12 fps. Anything
  stochastic must be seeded from geometry, not from an index or a clock.
- *"How far do we go simulating a medium?"* — **as far as the expression goes,
  and not one step further.** This is a drawing application, not a physics
  paper: we are not recreating watercolour, we are giving an artist the part
  of watercolour that makes a mark say something. Where a cheap approximation
  and an accurate simulation look the same to a person, the cheap one is
  correct and the accurate one is a defect. Krita's engine pushes further than
  most and still leaves this on the table; the edge is in the *expression*
  rather than in the fidelity, and chasing fidelity at the cost of a frame
  budget spends the advantage rather than earning it.
- *"Should this expensive brush option exist at all?"* — yes, if an artist
  would reach for it deliberately, and **no if it becomes the default**. The
  costly options are opt-in, they live on presets, and the picker badges them
  (`BrushCostOf`, derived from the settings so it cannot lie) so the trade is
  made knowingly. Every simulated medium also ships a fast counterpart — a
  medium nobody can afford is a trap, not a feature.

Two things that read as the same word and are not: **visual variation** is
wanted, **logical randomness** is forbidden. Marks should differ the way real
media differ — because of where they are, what they are on and how fast the
hand moved. That is invariant 2 restated from the artist's side rather than a
constraint fighting it.

When a request genuinely does not resolve against these, it belongs in
`.claude/quality/questions/` rather than in a guess — **one file per question**,
`python3 scripts/questions.py new "<title>"` to raise one, and
`.claude/quality/QUESTIONS.md` is a generated index over the directory that is
not committed (Q55's argument, Q91's application of it). Raising a question used
to mean appending a section to a single file, so two branches raising two
questions conflicted by construction; a new file conflicts with nothing.

**Ask it in the conversation first, with a recommendation, and write the file
afterwards — never the other way round.** A question written straight to the
directory and mentioned in passing is a decision the owner has to go
looking for, and the file then records deliberation nobody took part in.
Asking first makes the file record an *answer*; asking after makes it record a
guess waiting to be corrected.

**Ask with the question prompt, not with prose.** This paragraph was here and
still failed, on 2026-08-07: four questions were put in the body of a long
message, went unanswered twice while the conversation moved on, and were written
to the file anyway. The owner's correction is the rule now — *"prompt me the
questions then record them to the file, instead of letting me navigate to the
questions file."* A paragraph inside a wall of findings is skippable and gets
skipped; a prompt is answered in one click. So:

- **Use `AskUserQuestion`.** Batch up to four, each with a recommendation marked
  and the cost of the alternatives stated. Prose alongside it is fine; prose
  *instead* of it is the failure above.
- **Write the file after the answer arrives**, recording the decision — and
  record it faithfully when it goes against the recommendation, with what that
  choice costs. Q32 is the worked example.
- **A question in the file that was never prompted is a defect**, the same way a
  bug with no evidence line is. It looks like deliberation and is a guess.

**A run that cannot reach the owner stops and asks in a pull request.** The rule
above says to use `AskUserQuestion`, and a scheduled or background run has no
interface to put it in — which is how questions ended up accumulating in a file
instead of being answered. So when such a run hits a decision it cannot make:

- **Stop.** Do not guess, and do not pick the reversible option and carry on.
- **Push what is finished** to its branch and open the pull request.
- **Put the question first in the PR body**, above the diagnosis — a short block
  that states the choice and what each option costs, so it can be answered in a
  sentence by somebody who has not read the rest.
- Title it so it cannot be mistaken for ready: `[needs a decision] …`.

The point is to move unanswered questions to where the owner already looks. An
open pull request with a question at the top is visible; a file under
`questions/` is only visible to whoever opens it, and the evidence is
that nobody did. The directory still records the *answer* once it arrives —
that has not changed, and it is what makes the decision survive the thread.

**The session-start hook prints every unanswered question**, because a rule that
depends on remembering is the rule that just failed. Its first run listed five
that had been sitting unasked for weeks. If that list is non-empty at the start
of a session, ask them before doing work they block.

Two things that make the asking worth the interruption:

- **Lead with a recommendation and the reason for it.** "Here are three
  options" hands the work back. "(b), because it grows into tagging rather than
  being replaced by it" is a position that can be agreed with in one word or
  argued down in two.
- **Separate what needs deciding from what does not.** Q28 had three live
  options and one part that was not a preference at all — whichever won,
  `Flatten` still has to inline resolved references or invariant 1 stops
  holding. Saying so keeps the question about the actual choice.

Batch them: several questions in one exchange costs one interruption, and the
answers are usually related enough that seeing them together improves all of
them.

## Start here, not with a search

`.claude/codemap/` is a generated index of the whole solution. Reading it
costs a fraction of exploring the source, and it is rebuilt automatically at
session start when it is stale.

| Question | Do this |
| --- | --- |
| Where does X live? | `python3 scripts/codemap.py find X` |
| What is in this file, who uses it, what tests it? | `python3 scripts/codemap.py file <path>` |
| What is the shape of the codebase? | read `.claude/codemap/INDEX.md` |
| What is risky to change? | read `.claude/codemap/HOTSPOTS.md` (generated, not committed — build if absent) |
| What behaviour is promised today? | read `.claude/codemap/FEATURES.md` |
| What are we building, and how far along? | read `.claude/quality/ROADMAP.md` |
| What is known broken? | `python3 scripts/bugs.py next` |
| What is broken in the area I am editing? | `python3 scripts/bugs.py mine <domain>` |
| What does the app do, from the artist's side? | `python3 scripts/manual.py find X`, then read that one section |
| What is this supposed to *look* like? | read `docs/design/ui-reference.png`, then `.claude/quality/DESIGN.md` — the image is the source, the file is the rules read off it |
| What does an AI request cost? | read `docs/DESIGN-ai-payload.md` — do not re-derive it |
| Why is compositing on the CPU, and what would move it? | read `docs/DESIGN-gpu-compositing.md` — B125's design note, decisions included |
| What should I pick up next? | `python3 scripts/roadmap.py next` |

Rebuild by hand with `python3 scripts/codemap.py build` after large changes.

**The index is only worth its cost if it is read, and the honest failure is
reaching for `grep` out of habit.** Two rules, both learned by breaking them:

- **Search the index before the source.** `codemap.py find X` answers "where does
  X live" for a fraction of a repo-wide grep.
- **Read `HOTSPOTS.md` before editing, not after.** It is the only thing that says
  *this file is dangerous* — and its top two entries are XAML files with hundreds
  of commits and, for a long time, no test files at all. A session that added a
  whole `ConfigureWindow` page and left it unguarded is what that list is for; it
  was caught late because nobody looked.

`BUGS.md` is the same idea pointed at defects: every entry names the
regression test that closes it, `bugs.py sync` derives the checkbox from
whether that test exists, and deleting the test reopens the bug. An agent
about to edit an area runs `bugs.py mine <domain>` and fixes the open P1/P2
bugs it finds there alongside its own work.

### Fixing is the default; filing is the exception that needs a reason

**A bug you found and only wrote down is a bug you moved from the code into a
list.** That is worth doing when the alternative is losing it — and it is not
what should happen to most of them. The measurement that produced this rule,
taken 2026-08-12: of B1–B60, 9% are still open; B61–B120, 14%; B121–B160, 18%;
**of B161–B179, 79%.** The ledger stopped being a record of what is broken and
started being a queue nobody drains.

So, on encountering a defect that is not the objective in hand:

| | |
| --- | --- |
| **P1 or P2** | Fix it. Always. |
| **P3, and small** | Fix it. |
| **Needs a decision** | Ask (see below) — do not guess and do not silently file. |
| **Genuinely large** | File it, *and* a roadmap item, *and* a cost. All three. |

**Finish the branch in hand first, then give the bug its own branch.** That is
what keeps this from eating the one-objective rule: the answer to "I found
something else" is still a new branch, never an "and" bolted onto this one. It
costs more pull requests, and that is the price of not accumulating — the four
open P1s that prompted this rule had been open for, respectively, one, three,
three and thirty-one merges.

**Filing without fixing has to say why**, in the entry, in a sentence that names
which of the two exceptions applies. "Recorded for later" is not one of them. An
entry that cannot say why it was not fixed is an entry that should have been a
fix.

**The one thing this does not license: fixing badly to keep a number down.** A
fix still needs its regression test, its evidence anchor and its manual update.
If those cannot be had, the honest outcome is the *large* row above — file it,
roadmap it, cost it — not a green checkbox over a guess. B60 is the worked
example: an afternoon establishing that the fix is a research project, written
up, and left open on purpose.

The user manual is `docs/MANUAL.md` (the index) and `docs/manual/*.md` (one
file per section), and it is **part of the definition of done**: a change that
alters what an artist sees or does updates the relevant **section file** in the
same commit. Find the right one with `python3 scripts/manual.py find <term>`
rather than opening the lot — the manual is 100 KB and no change needs all of
it. It describes what exists today and marks what does not as *Planned* — a
manual that documents a feature nobody can use is worse than no manual, because
it cannot be trusted anywhere.

The contents list in the index is **derived** (`manual.py sync`, checked in CI),
so adding a section means adding a file rather than editing two places. The
manual is also published to the repository's wiki on merge — a generated view,
never a source; see `.github/scripts/publish-wiki.sh`.

### Land the feature, then land the places it shows up

A feature is not finished when it works. It is finished when **every surface
that is supposed to know about it does**. The failure is always the same shape:
the thing works, nothing is red, and the artist cannot find it or cannot change
it — which reads as the app being inconsistent rather than as one missing
registration.

So after any feature, any bug fix that changes behaviour, and any new setting,
walk this list and update what applies **in the same commit**:

| If the change… | then it belongs in |
| --- | --- |
| binds a key, or should be bindable | `ShortcutMap` — the single registry the Configure window's editor reads. A shortcut that is not in it cannot be seen, searched or rebound, and an artist's remap will not apply to it |
| adds a preference an artist would want to change | the Configure window, on the page it belongs to |
| adds a per-document or per-project option | the window that owns that scope, not the global one |
| adds a brush, export or timing setting | its preset record, so it survives being saved and reused |
| changes a menu, a panel or a default layout | the workspace defaults, or reopening a workspace silently loses it |
| adds a document-level capability | the MCP surface, if an agent should be able to reach it |

**Shortcuts are the one with a history**, which is why they are first: a command
wired straight to a gesture in the view or the view model works perfectly and is
invisible to the whole configuration system.

The general rule behind the table: **anything with a registry has that registry
for a reason, and the reason is always that something else enumerates it.** When
adding a capability, find the registry before writing the feature — retrofitting
one after the fact means finding every place that bypassed it.

`ROADMAP.md` holds the six pillars that give the app its identity, the drawing
floor beneath them, and **one cross-cutting `## AI assistance` section** — not a
seventh pillar, but the one area whose cost, failure modes and review process are
shared and only legible together. A feature belongs there if it needs a model to
be possible at all; anything that merely measures geometry or timing stays with
the pillar it serves, whatever the word "assistant" in its name suggests. **Its checkboxes are derived from the code**, not
asserted: each item names the types and tests that would exist if it were
built, and `python3 scripts/roadmap.py sync` resolves them. Landing a feature
means adding its evidence anchors in the same commit — a green box with no
anchor is the one thing the file cannot represent.

## Invariants — breaking these is a defect even if tests pass

1. **The stroke record is the document.** A frame is a list of strokes; the
   pixels are derived. Anything that paints must go through
   `BrushEngine.StampStroke` so a reload renders the same image.
2. **No randomness in rendering.** Effects (scatter, granulation, jitter) are
   seeded from dab position via `Hash01`. An RNG would make re-renders and
   AI inbetweens diverge.
3. **Fills and selections are part of the record.** A fill is a `ToolKind.Fill`
   stroke with contours; a selection is a content-hashed entry in
   `Doc.ClipRegions` referenced by `Stroke.ClipId`.
4. **Settings that affect pixels are stored per stroke**, not read from
   global state at render time (anti-aliasing, pressure curves), so changing
   a preference never alters existing art.
5. **The view transform is view-only.** Zoom, rotation, mirror and pan never
   touch the document. The **camera** is the one transform that is not: it is
   authored, keyframed, saved and exported. It still never mutates a stroke —
   invariant 1 holds — it only decides what part of the record a render shows.
   "View through camera" is on the view-only side of that line.
7. **Render bigger by scaling the surface, never the geometry.** Stroke
   coordinates are never multiplied. `Hash01` seeds every dab dynamic from the
   IEEE-754 bits of a position, so doubling a coordinate re-rolls scatter,
   size, flow, roundness, rotation and all three colour jitters — a 2× render
   would be a *different mark*, not a sharper one. Output scale is therefore a
   canvas transform (`FrameRasterizer`/`BrushEngine`), and
   `OutputScaleTests` renders it the wrong way round on purpose to keep the
   reason written down.
6. **Painting is bounded work.** Live preview and commits repaint only the
   region a stroke can reach; anything that goes full-canvas per pointer
   event is a performance regression (see `.claude/quality/CHARTER.md`).

## Layout

- `src/Lightbox.Core` — document model, serialization, geometry, inbetweening.
  No rendering, no UI.
- `src/Lightbox.Raster` — `BrushEngine` (the only pixel path), flood fill,
  frame rasterization.
- `src/Lightbox.App` — Avalonia UI: view models, canvas control, compositing.
- `src/Lightbox.Ai` — Claude/Ollama artists behind an interface.
- `src/Lightbox.Mcp`, `src/Lightbox.Import` — MCP server, brush importers.

## Working here

- Build: `dotnet build Lightbox.sln`
- Test: `dotnet test` (all four suites must stay green)
- **One .NET, and it is 10.** Every project targets `net10.0`, so the SDK that
  builds this carries the runtime that runs it. That was not always true — the
  solution targeted `net8.0` while needing the 10.0 SDK for Avalonia 12's
  source generators, which meant a machine with only one of them compiled or
  ran but not both. `docs/DESIGN-net10-upgrade.md` records the migration and,
  more usefully, the evidence: the render is **bit-identical** across the two
  runtimes, pinned by `RuntimeDeterminismTests` against a fingerprint recorded
  on .NET 8 before the move.
- Performance budgets run inside the normal suite, tagged
  `[Trait("Category", "Performance")]`. They are deliberately loose — they
  catch order-of-magnitude regressions, not drift.
- The app runs headless for visual checks under Xvfb; see `MANUAL_TESTING.md`.
- Work happens on a branch off `main`, merged back when its **objective** is
  complete — green is necessary and is not the bar. `feature/ui-dockers` was
  the long-lived UI branch and has landed.

### Branches, merges and pull requests

Delegate them to the **git-handler** agent (`.claude/agents/git-handler.md`)
rather than doing them by hand — its own definition says what it covers.

**Finished work becomes a pull request, and that is the standing route** — it
does not need asking for. **Merging to `main` needs an explicit instruction to
merge**; "it's finished" and a green suite are a request for a PR, not for a
merge.

**`.githooks/pre-push` enforces that sentence, because the sentence alone did
not.** On 2026-08-05 five commits went straight to `main` — B27/B46/B54, B50,
the visual tests, B73, B70 — with this paragraph already written above them. The
cost was not abstract: two open PRs had their base move underneath them and both
went to conflicts. The hook refuses a push whose destination is the default
branch; the session-start hook points `core.hooksPath` at `.githooks` unless
something already set it. When the owner *does* say merge, the escape hatch is
`LIGHTBOX_PUSH_TO_MAIN=1`, and typing it is meant to be a decision rather than a
way past a refusal.

**The conflicts used to land in `.claude/codemap/INDEX.md` and `FEATURES.md`**,
which neither PR author had touched — every branch regenerates the index, so
parallel branches collided there by construction. Two generations of machinery
tried to make that livable: a `codemap` merge driver that rebuilt the files
from the merged tree, and a CI `verify` that derived them and compared the
bytes, on the principle that a committed derived file is not believed, it is
recomputed. Both worked as designed and neither ended the pain, because
**GitHub runs no merge driver**: every pull request merged in the web UI put
`main`'s index ahead of every open branch, each of which then showed conflicts
and had to have `main` hand-merged in — round after round, once per merge, for
as long as more than one branch was open.

**So the files are not committed at all any more (Q55, 2026-08-08).** The
stronger form of verify's own argument won: with nothing committed there is
nothing to drift, nothing to merge and nothing to verify. `INDEX.md` and
`FEATURES.md` are gitignored beside `HOTSPOTS.md`; the session-start hook
builds them when they are stale or absent, so a fresh clone self-heals before
the first question is asked; CI runs `codemap.py build` to prove the tree
parses. The merge driver and `verify` are retired, and
`LedgerGateTests.TheDerivedIndexIsNotTracked` is what turns re-committing them
from an accident back into a decision.

**The ledgers do not get the same treatment, and that is a rule rather than a
backlog.** They collide on parallel branches just as reliably, so the obvious
next step is to gitignore or auto-merge them too — and it would destroy work.
The test is *what can be reconstructed*: `codemap.py build` writes the index
from nothing, so nothing is lost by not storing it. `bugs.py sync`,
`roadmap.py sync` and `manual.py sync` instead parse a file and rewrite a
checkbox, an ordering or a marked block; every entry around those is authored
prose no script can reproduce. Two branches that each filed a bug have two
entries that both have to survive, and which id each keeps is a judgement. The
ledgers stay committed, conflict occasionally, are resolved by hand, and are
guarded by a check instead of a driver.

**That guard is `.githooks/pre-push` running `python3 scripts/bugs.py ids`, and
where it runs is the whole point.** `bugs.py check` has failed on duplicate ids
since two bugs shared B39, and CI has run it all along — and on 2026-08-07 four
ids collided across two merges with it green throughout. Not because the check
was weak: **a collision does not exist in either branch, only in the merged
file**, so the earliest CI can see one is after it is pushed and other branches
have rebased onto the bad resolution. `ids` is the cheap half of `check` — no
evidence anchors, no code index, milliseconds — so it can run on every push,
which is the last moment the mistake is private.

It also refuses the failure a duplicate check *cannot* see, and this is the one
to remember when resolving a ledger conflict by hand:

> **Taking one side deletes the other side's entry, and leaves a file with no
> duplicate in it.** Every check passes and the loss is permanent. A duplicate is
> loud and costs a renumber; this is silent and costs a bug.

So when HEAD is a merge, every id in every parent must still be present. Both
entries survive and the later one is renumbered above the highest id on either
side. `LIGHTBOX_ALLOW_LEDGER_DELETION=1` exists for a deletion that is genuinely
meant, and typing it is a decision in the same way `LIGHTBOX_PUSH_TO_MAIN=1` is.

**Every word above detects a collision, and none of it stopped one.** The
measurement that settled this, over the six days to 2026-08-14: six bug
renumbers and three question renumbers, one bug renumbered *twice* because the
second guess collided as well — every one a hand-edited commit on a branch whose
objective was something else. The cause was never the checking. It was that
nothing ever **issued** an id: an author read the ledger, took the highest number
in it and added one, which is the same number on two branches that both started
from `main`. So:

- **`bugs.py new <domain> "<title>"` files a bug**, and `bugs.py freeid question`
  issues an id for a question you then write by hand. Both allocate above every
  ref the clone can see, not above the working tree, and both fetch first.
- **`ids` reports a *clash*** — an id this branch created that another branch
  created too — which is the same collision one merge earlier, while it is still
  one branch's problem. It is checked against the merge base, so an id both sides
  carry because it was already on `main` is shared rather than clashed.
- **`ids --fix` moves the entry this branch filed**, above the highest id
  anywhere, and rewrites the citations *this branch wrote* for it. Not the
  others: the id it collided with is older, and every mention of it in the tree
  already means the entry keeping the number.
- **The pre-push hook runs the fix for you** and still refuses the push, because
  a repair made during a push is not in the commits being pushed. It stands down
  mid-merge, and never touches a *lost* id — putting an entry back is a judgement
  about what it said, which no number supplies.

Partitioning the number space by domain was the obvious alternative and was
measured instead of assumed (Q90): it would have stopped roughly 60% of the bug
collisions, 0% of the question ones, and not the worst case in the list — the
bone-icon bug collided with another `ui` bug, inside the band it would have been
given.

**The shape that stops the *textual* conflict is one file per entry** (Q91), and
it now applies to both places where every branch wrote to the same spot:

| | |
| --- | --- |
| `.claude/quality/questions/` | one file per question. Raising one used to mean appending a section to a 3,689-line file, so two branches raising two questions conflicted by construction. `QUESTIONS.md` is a generated index and is **not committed**, for Q55's reason. |
| `.claude/quality/ratchets/` | one file per line budget, holding the number and every reason it has moved. They were a table in `MonolithRatchetTests.cs`, so two branches growing two *different* oversized files still met there. |

The ratchets are the case where the derived-file trick does **not** apply, and
the reason is worth keeping: a budget looks derived — three of the four equal
their file's exact line count — but a ceiling re-measured from the tree can never
be exceeded, so a script that synced it would delete the mechanism and leave the
paperwork. `ratchets.py remeasure` exists for one moment only, resolving a merge,
where *measure on the merged tree, never take a side's number* is mechanical and
was being done by eye. It is wired to no hook on purpose.

**`python3 scripts/branchstate.py` answers "would this merge?" before a reviewer
does**, and separates the two kinds of conflict — authored files, which need a
decision, from the generated index, which needs a rebuild. A `PostToolUse` hook
runs it after any `dotnet build` or `dotnet test` that passed, alongside re-deriving
the ledgers, so both facts arrive when the code has just changed rather than when
somebody remembers to look. It stays silent unless something moved, refuses to
touch anything while a build is red, and refuses again mid-merge — rewriting
`BUGS.md` while somebody resolves a conflict in `BUGS.md` would destroy the
resolution.

**The derived ledgers resolve against `map.json`, which is gitignored, so a branch
switch leaves it describing a tree nobody is looking at.** That produced two
opposite lies in one `bugs.py check` — a bug reported fixed that was not, and one
reported open that was. `evidence.py` now rebuilds when the index is stale rather
than answering from it, because a wrong answer that leaves no trace in the diff is
the kind nobody catches.

**A branch is one objective, and its name says which** — `<type>/<domain>/<id>-<slug>`
for a bug, as in `fix/brush/B39-effect-brush-scratch`, and `<type>/<slug>` for work
that has no ledger id.

**The domain is in the name for the same reason `BUGS.md` groups by it**: work is
picked up by area, not by number. A branch list reading `fix/B67-…`, `fix/B62-…`,
`fix/B58-…` says nothing about which parts of the application are in flight, so two
branches heading for the same file are invisible until they collide. With the domain
in front, four open branches are legible at a glance. Use the domains `bugs.py`
already knows — brush, timeline, layers, canvas, transform, colour, export, project,
ui, ai — so the branch, the ledger entry and `bugs.py mine <domain>` all agree. The agent has the full convention and
the mechanical checks; the part worth knowing before you start is the reason.
Branches were once named after the chat that made them
(`claude/codespaces-agentic-setup-fjq295`), which records **provenance rather
than scope** — and a name that states no objective cannot be departed from, so
every one of them drifted. One carried a brush-compositor fix and a packaging
change whose file sets shared *no directory at all*. The one branch named for
its objective, `net10-upgrade`, is the one that did exactly what it said.

So: if the sentence describing the branch needs an "and", it is two branches.
Finding a second thing to fix mid-branch is normal — it is a new branch, not a
new commit. **That is the same answer the fix-rather-than-file rule gives**, and
the two are meant to be read together: fixing what you find produces *more*
branches, in sequence, each doing one thing — not fatter ones. Above **four**
unmerged branches the agent warns, because four is
where a person stops holding the set in their head.

### Touching anything AI: two agents, on purpose

AI work is reviewed by a **pair**, `.claude/agents/ai-engineer.md` and
`.claude/agents/art-director.md` — machinery and result respectively, per their
own descriptions — and they are meant to disagree.

Either alone fails in a direction you can predict. Alone, the engineer
optimises until the output is cheap and lifeless; alone, the director asks for
richness nobody can afford or reproduce. **art-director has a veto on
expression, ai-engineer has a veto on determinism**, and where they disagree
and cannot measure, it goes to `QUESTIONS.md` rather than to whoever ran last.
Q18 — flat point arrays are 57% cheaper and might cost stroke labels — is the
live example, and it is exactly the shape of argument the pair exists for.

Gate G12 in the charter makes this non-optional for a diff touching
`src/Lightbox.Ai`, the MCP surface, a prompt, or an AI path in the view model.

### What an AI request costs, before optimising it

`docs/DESIGN-ai-payload.md` has the measured numbers, and one of them settles
most of these arguments before they start: **images are ~87% of a request's
bytes and ~5% of its tokens; strokes are the reverse.** So "make the payload
smaller" is not a goal — it is two goals that recommend opposite changes, and a
proposal that has not said which one it means is not ready. The corollaries are
measured in that doc — compression, GraphQL and the six-times lever of sending
fewer strokes are all settled there, so read it rather than re-deriving them.
`AiPayloadBudgetTests` keeps the numbers honest.

### Measuring a brush: the saturation trap

**Dabs overlap, so alpha along a stroke saturates.** A brush at `Spacing = 0.05`
lays about twenty dabs on every pixel, and twenty dabs of flow `a` come out at
`1 - (1-a)^20` — which is **0.92 at a flow of 0.12**. So a test that sets flow
to 0.1, renders, and reads the alpha down the middle of the stroke gets 0.93
and concludes the control works, when a brush at flow 1.0 also reads 1.00 and
the two are a hair apart. The same test passes on a build where flow is wired
to nothing.

This has now cost real time three times — B26's depletion tuning (`Reach` 24 →
50 → 12 before the overlap was noticed), and twice since in tests that had to
be rewritten. The rules:

- **Measure below saturation, or not along the stroke.** Either use values low
  enough that `1-(1-a)^n` is still climbing (flow ~0.01–0.02 at ordinary
  spacing), or widen the spacing so the dabs barely overlap, or measure
  something that does not accumulate — stroke *width*, a profile across the
  stroke, the ratio between two places on the same stroke.
- **Always print both numbers.** `output.WriteLine($"faint {a:F3}, full {b:F3}")`
  is what turns "the assertion passed" into "0.929 vs 1.000, which is nothing".
- **Sanity-check the other way.** Assert the faint mark is *present* as well as
  fainter; a test that only checks `a < b` also passes when `a` is zero because
  the brush is broken.

The general form is the lesson `docs/DESIGN-performance.md` records for
measurement: **the number was real and the attribution was not.** Ask *what
else is in this measurement* before *what is wrong with the code*.

### "Optional" has two halves, and the second one is easy to miss

A setting is optional when it is **absent unless used** — not merely inert at
its default. Two ways that goes wrong, both found by dumping the JSON for a
document with one default stroke rather than by reading the model:

- **A non-nullable block serializes even when it is untouched.** The medium was
  behaviourally absent and written anyway: twenty-one keys on every stroke of
  every document, a third of the brush record, for a pass nobody switched on.
  A block whose default is "off" wants to be nullable, or to have a shadow
  property that returns null when it is untouched.
- **A convenience getter beside a nullable field is a property.**
  `BlendOrNormal => Blend ?? Normal` had a public getter, so every stroke wrote
  `"blendOrNormal": "normal"` — reintroducing under a second name the exact key
  that making `Blend` nullable existed to remove. These need `[JsonIgnore]`.

So: after adding a setting, **serialize a document that does not use it and
look**. `Assert.DoesNotContain("\"yourKey\"", json)` is the cheap version and
belongs in the same commit.

### Verifying UI behaviour

Prefer a headless pixel test over a screenshot: tests in
`tests/Lightbox.App.Tests/LivePreviewPixelTests.cs` drive the real
begin/move/end pipeline and inspect the published frame. Synthetic input
through Xvfb is unreliable in this environment — a dropped click looks
exactly like a bug.

## Self-improvement loop

`/improve` runs an audit → fix → verify loop; see `.claude/skills/improve/SKILL.md`.
