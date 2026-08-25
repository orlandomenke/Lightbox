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

These two rules govern different things and both hold at once:

| Rule | What it governs |
| --- | --- |
| *Optional means absent, not disabled* | The **record** and the **UI**. Unused writes no keys and shows no controls. |
| *Every feature is reachable* | The **capability**. Nothing is locked behind a value in a manifest. |

The camera proves both: absent from the file until authored, absent from the UI
until asked for, and askable for anywhere. A feature framed as "this is for
feature film" describes **which project type turns it on by default** — never who
is allowed to have it.

**Scoping a feature, or arguing about how far to take one? Use the `scope-call`
skill.** It carries the worked examples — how much of a brush panel to take, how
far to simulate a medium, per-stroke versus per-preference — and the reasoning is
meant to be reusable rather than re-derived.

### When it does not resolve, ask — with the prompt, not with prose

A request that does not resolve against the rules above needs a decision, and a
guess recorded as a decision is the failure to avoid.

- **Use `AskUserQuestion`.** Batch up to four, each with the recommendation
  marked and the cost of the alternatives stated. Prose alongside it is fine;
  prose *instead* of it is not — a paragraph inside a wall of findings is
  skippable and gets skipped.
- **Lead with a recommendation and the reason.** "Here are three options" hands
  the work back.
- **Separate what needs deciding from what does not.** Some of a question is
  usually not a preference at all.
- **Write the file after the answer arrives**, with
  `python3 scripts/questions.py new "<title>"` — one file per question, and
  record the decision faithfully when it goes against the recommendation.
- **A question in the file that was never prompted is a defect**, the same way a
  bug with no evidence line is. It looks like deliberation and is a guess.
- **A run that cannot reach the owner stops and asks in a pull request** — push
  what is finished, put the question first in the body above the diagnosis, and
  title it `[needs a decision] …` so it cannot be mistaken for ready.

The session-start hook prints every unanswered question. If that list is
non-empty, ask them before doing work they block.

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
| Why is a rule below the way it is? | the skill that carries its reasons — `branching`, `scope-call`, `ai-work`, `brush-measurement`, `optional-settings` |

Rebuild by hand with `python3 scripts/codemap.py build` after large changes.

**This file is the rules; the skills are the reasons.** Everything here is loaded
into every session and again into every subagent, so a paragraph added here is
paid once per agent per session forever — which is why `CLAUDE.md` has a ratchet
(`.claude/quality/ratchets/prelude.md`, checked by `scripts/prelude.py`) and why
the incident that produced a rule lives in a skill rather than beside it. When
you add something, ask which half it is: a rule has to be **resident** to be
obeyed, its history only has to be **reachable**. `python3 scripts/prelude.py
measure` shows what a session currently pays.

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
merge. `.githooks/pre-push` refuses a push to the default branch, and
`LIGHTBOX_PUSH_TO_MAIN=1` is the escape hatch when the owner does say merge.

**A branch is one objective, and its name says which** —
`<type>/<domain>/<id>-<slug>` for a bug, as in `fix/brush/B39-effect-brush-scratch`,
and `<type>/<slug>` for work with no ledger id. The domain is in the name because
work is picked up by area: use the ones `bugs.py` knows — brush, timeline, layers,
canvas, transform, colour, export, project, ui, ai. If the sentence describing the
branch needs an "and", it is two branches. Above **four** unmerged branches the
agent warns.

Four mechanical rules, each of which has been broken expensively:

- **Never allocate an id by reading the ledger.** `bugs.py new <domain> "<title>"`
  files a bug and `bugs.py freeid question` issues a question id; both allocate
  above every ref the clone can see. `bugs.py ids --fix` repairs a clash.
- **Resolving a ledger conflict, every id in every parent must still be present.**
  Taking one side deletes the other side's entry and leaves a file with no
  duplicate in it — every check passes and the loss is permanent. Both entries
  survive; the later one is renumbered. `LIGHTBOX_ALLOW_LEDGER_DELETION=1` is for
  a deletion that is genuinely meant.
- **Nothing under `.claude/codemap/` is committed**, nor `QUESTIONS.md`. They are
  derived, and a stored derived file collides on every parallel branch.
- **`python3 scripts/branchstate.py` answers "would this merge?"** before a
  reviewer does. A `PostToolUse` hook already runs it after a green build.

**Read the `branching` skill** before resolving a ledger conflict, renumbering an
id, or when one of these looks arbitrary — it carries the incidents that produced
them, including the two retired generations of merge machinery and the six days of
measured collisions that moved id allocation into a script.

### Touching anything AI: two agents, on purpose

AI work is reviewed by a **pair**, `.claude/agents/ai-engineer.md` and
`.claude/agents/art-director.md`, and they are meant to disagree.
**art-director has a veto on expression, ai-engineer has a veto on
determinism**; where they disagree and cannot measure, it becomes a question
rather than going to whoever ran last. Gate G12 in the charter makes this
non-optional for a diff touching `src/Lightbox.Ai`, the MCP surface, a prompt,
or an AI path in the view model.

**Use the `ai-work` skill** on any such diff — it carries the pair's failure
modes and the payload economics, of which the load-bearing fact is that
**images are ~87% of a request's bytes and ~5% of its tokens, and strokes are
the reverse**. "Make the payload smaller" is two goals that recommend opposite
changes; a proposal that has not said which one it means is not ready.


### Measuring a brush: the saturation trap

**Dabs overlap, so alpha along a stroke saturates.** Twenty dabs of flow `a`
come out at `1 - (1-a)^20`, which is 0.92 at a flow of 0.12 — so a test that
sets flow low, renders, and reads alpha down the middle of the stroke passes on
a build where flow is wired to nothing. Measure below saturation or measure
something that does not accumulate, and always print both numbers.

**Writing or debugging a test that renders a stroke and reads pixels back? Use
the `brush-measurement` skill first.** This has cost real time three times.

### "Optional" has two halves, and the second one is easy to miss

A setting is optional when it is **absent unless used** — not merely inert at
its default. After adding one, **serialize a document that does not use it and
look**: `Assert.DoesNotContain("\"yourKey\"", json)` is the cheap version and
belongs in the same commit.

**Adding a setting, a brush option or a new block to the model? Use the
`optional-settings` skill** — it names the two ways this goes wrong, both found
by dumping the JSON rather than by reading the model.

### Verifying UI behaviour

Prefer a headless pixel test over a screenshot: tests in
`tests/Lightbox.App.Tests/LivePreviewPixelTests.cs` drive the real
begin/move/end pipeline and inspect the published frame. Synthetic input
through Xvfb is unreliable in this environment — a dropped click looks
exactly like a bug.

## Self-improvement loop

`/improve` runs an audit → fix → verify loop; see `.claude/skills/improve/SKILL.md`.
