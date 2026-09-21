---
name: sensitivity
description: The reasons behind Lightbox's sensitivity baseline and three-track flow, with worked cases — a one-line change to where a key is read, code that looks lifted from another tool, a new importer, an MCP argument that names a path, a rule that seems to be in the way. Read when a change touches a trust boundary, when the router forces a review on a change that seems too small for one, when the guardian and the author disagree, or before proposing a change to SENSITIVITY.md.
---

# Sensitivity: the reasons

`CLAUDE.md` says the tracks exist and `.claude/quality/SENSITIVITY.md` holds the
numbered rules. This holds what makes them arguable — the cases, and why each
rule is shaped as it is. Read it when a rule looks arbitrary; do not read it to
find a way round one.

## Why three tracks over one stage list, and not one pipeline

The rule *fixing is the default* has a measurement under it: of the last nineteen
bugs recorded when it was written, 79% were still open, because filing was cheaper
than fixing. A single heavy pipeline for every change would make fixing dearer and
recreate that. A single light one would send a change to the AI layer through
without the review it needs.

The first cut of this was two tracks, fast and full. **The owner's objection to it
was right, and it is worth keeping**: a small *fix* and a small *tweak* are not the
same shape of small. A defect needs proof it is fixed, which only a regression test
written first gives; a tuning pass has nothing to prove broken and only needs to
not break anything. So there are three named tracks over the same eight stages —
MAINTENANCE, BUGHUNT, FEATURE — each a different subset, run by the same agents and
skills, chosen by the `triage` agent from **why** the change is being made, which no
file path can say.

Separately, and this is the part a script *can* decide, a router forces stages and
reviewers on regardless of track. It is **sensitivity-first, not size-first**,
because the failure it exists to prevent is a small change to a sensitive place —
and it is now also **performance-first for the brush path**, which was added when
the owner pointed at the ledger's recurring brush-latency regressions. The lesson
there is worth stating precisely, because it is easy to get wrong: those
regressions passed a green suite every time, so *more stages and more tests do not
catch them* — a capture does, and that is why Real app is a stage for BUGHUNT and
FEATURE and the owner's capture outranks the suite. What the router *can* fix is
that the two reviewers built for the leak shapes a suite cannot see, `leak-hunter`
and `perf-warden`, were optional; they are now forced on any diff to those paths.

The first version of the router counted every changed line. Replayed over the last
forty merged pull requests it escalated 31, because tests come with every change and
are its guard, not its risk. **A threshold set by reasoning is a guess; run it over
history before shipping it.** That replay is why tests are not counted, and it is
the method for recalibrating.

## Worked cases

**A one-line change to where the key is read from.** Say `AiSettings` starts
reading the key from a new place. It is one line and the router forces a guardian review on it, correctly:
*where a secret comes from and where it is written* is the whole of rule A4, and a
review that has the diff and the rule in front of it asks the questions the author
did not — does it reach a log, an exception message, a diagnostics bundle? The
router does not know any of that; it knows the file is on the boundary, which is
enough to make someone look.

**Code that looks lifted.** A brush curve table appears with the same constants
Krita uses. Nobody says it was copied. The guardian's question is *where did this
come from*, and the design note must answer with a public specification or the
owner's own files (L1). The stakes are not only courtesy: `CONTRIBUTING.md` keeps
sole copyright and Q131 chose open core with paid add-ons, so relicensing has to
stay possible, and a copied GPL file ends that option **one file at a time**, quietly.
This is why L2 is stricter than GPL compatibility requires. *Ideas and observable
behaviour are free; expression is not* — matching how another tool's brush looks is
fine, matching its numbers because they were sitting there is not.

**A new importer.** The PSD reader rejects an implausible canvas and one larger than
it will decode; that is S1 already met, and the reason a *new* parser without a
hostile-input test is BLOCKING rather than a note: the first hostile file will
arrive as an artist opening a brush pack a stranger sent them, and the failure mode
is a crash or an allocation of the whole machine's memory, in a tool that may hold
their unsaved work. Ask of any parser: four bytes; a length of 2³¹; a
decompression ratio of a million; an archive entry named `../../x`.

**An MCP argument that names a path.** `import_character` takes a `library` path
from the agent. It copies rather than links and needs a library-shaped project, so
the risk is small — which is exactly why it is recorded as a gap and not a defect.
The rule (S2) is that no tool's argument names the filesystem outside the folders
the artist configured. The wrong response to "it is low risk" is to leave it out of
the baseline: the *next* tool added will copy the shape of this one.

**A rule that seems to be in the way.** A feature genuinely needs a new outbound
request — say, fetching a font catalogue. A1 is not a wall; it is that **the
inventory is the owner's file**, so adding a network path is a visible decision with
a name on it. The session's job is to raise it as a question with what the artist
sees and what it costs, not to add the file to the allowlist and continue. An
allowlist a session can extend is a list of what sessions have done.

**A gap that is listed.** *Known gaps* in the baseline are not permission. The
plaintext key (A4) is listed because it is true and because a baseline that
flattered the project would be worthless in a public repository; the guardian does
not re-report a listed gap the diff did not touch, and does report one the diff makes
worse. And per `CLAUDE.md`, a confirmed gap is fixed or filed *with its reason* —
this one is filed as large, because the honest fix is a cross-platform credential
store and that needs a decision.

## What the scan is and is not

`sensitivity.py scan` runs on every track because it costs nothing. It finds
credential shapes, network and process use outside the inventory, a new dependency,
a new bundled asset, and an instruction aimed at an agent. **It finds none of the
judgement problems** — whether a payload is minimal, whether a table was lifted,
whether a migration changes the picture. A clean scan is where the review starts.
Its own selftest fails when a trigger glob matches no file, because a stale trigger
looks identical to a working one.

## What does not belong here

Content moderation of what an artist draws; accessibility; data-protection-law
compliance. Each needs an owner decision before it can be a rule, and each is a
question when raised, not an assumption in the baseline.
