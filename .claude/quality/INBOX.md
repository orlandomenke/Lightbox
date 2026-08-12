# Inbox

Raw, unstructured bug reports from outside this repo's tooling — a person, or
an agent (ChatGPT, another assistant) that does not know `BUGS.md`'s
conventions. Land them here, not in `BUGS.md` directly.

**Why the separation.** `BUGS.md`'s checkboxes are derived, not typed:
`scripts/bugs.py sync` expects `evidence:` to name a real, existing test (or
the literal `manual`), ids to be unique across the whole file, a domain from
a fixed list, and a priority read off the severity × reach matrix. A report
written by something that has not read those rules will not follow them —
not out of carelessness, but because it cannot see `bugs.py` or the codemap
that would let it name a real test. An entry with a guessed evidence line is
worse than no entry: it either fails `bugs.py check` loudly, or — worse —
happens to resolve against an unrelated test and reports a bug fixed that
never was.

**Format: whatever the reporter can produce.** A sentence, a screenshot
description, a repro. No structure is enforced here on purpose — the cost of
writing a report should not be "learn this file's conventions first."

**Processing.** A Claude Code session periodically works through this file:
for each entry, it either
- turns it into a proper `BUGS.md` entry — a real id, a domain, a priority,
  and either a named regression test or `evidence: manual` if none can reach
  it headlessly — following the format documented at the top of `BUGS.md`;
- or, if the report does not describe a real defect (already fixed, not
  reproducible, out of scope), removes it and says why in the commit;
- or, if it is ambiguous enough to need a person, leaves it here with a note
  under **Needs a decision** below, rather than guessing.

Processed entries are deleted from this file — `INBOX.md` is a queue, not an
archive. The archive is `BUGS.md` itself.

---

## Unprocessed

<!-- Append new reports below this line, oldest first. -->

*(empty)*

## Needs a decision

<!-- Reports that could not be turned into a BUGS.md entry without a human call. -->

Nothing outstanding. The batch of 2026-08-04 became B61-B71, with three product
decisions split out to `QUESTIONS.md` as Q22 (is a Document a Workfile), Q23
(how a tab shows project membership) and Q24 (what a saved brush setting is
scoped to, and whether saving needs a button).

One report was **dropped rather than filed**, on the reporter's own evidence:
*"this might have been a fluke, restarting Lightbox does not reproduce the
issue: I switched documents a couple of times between Untitled and a character
sheet and was unable to paint anything anymore."* Not reproducible, and an
entry whose evidence cannot be named is the thing `BUGS.md` refuses at check
time. It is written down here rather than silently discarded: **if painting
ever stops after switching documents, this is a second sighting, not a first** —
and the two neighbouring entries are the ones to suspect, since B66 says a
character sheet has no file behind it and B67 says tool state is shared between
documents.

**Batch of 2026-08-04, second pass — all eight filed, none needed a decision.**
B72 and B74 (the brush gizmo: stale size, and a circle where the tip's outline
belongs), B73 (fast strokes trailing the pen), B75 (no Save on the
unsaved-changes dialog), B76 (a new document written to disk on creation, with
the docker's pending state specified), B77 (the colour switcher only appearing
for the brush), B78 and B79 (the character sheet name asked twice, and the
unsaved badge surviving a save).

**B78 is a regression from B66, shipped hours earlier and reported immediately.**
Worth keeping visible rather than folding into the entry: the B66 tests pinned
the decision each dialog makes and still could not see the pair, because neither
dialog is reachable headlessly. Two correct prompts in sequence are one bad
prompt, and only a person looking at the screen was ever going to catch it.

**Batch of 2026-08-12 — twelve lines became ten bugs and one roadmap item.**
B168 (deselect inert under the Select tool, and `SelectAll` broken the same
way), B169 and B170 (the live eraser cutting through the layers beneath, and
the crash beside it, filed apart), B171 (a selection surviving into the next
document), B172 (the white arrow unable to enter a path), B173 (Delete and
Backspace ignoring a marquee), B174 (the palette hierarchy pinned at 128 px
under an inert splitter), B175 (fill and the eyedropper wearing the shared
crosshair), B176 (no momentary tools), B177 (Pencil and the flat brushes
lagging while the badge calls them Fast).

**Two decisions were prompted rather than assumed, and one went against the
recommendation.** Where feature-shaped reports land: the small missing
affordances became P3 `ui`/`canvas` entries so `bugs.py mine` surfaces them to
whoever next edits the area, and the two vector reports became one Pillar 0
roadmap item because they specify an interaction model rather than a broken
behaviour. On Delete's precedence — the marquee wins and lines are the
fallback, which is Photoshop's rule and the one an artist arrives with. It is
recorded on B173 with what it costs: `NudgeSelection` asks the line selection
*first* and says why in a comment, so the two keys will disagree about which
selection they mean, and Delete quietly changes meaning while a stale marquee
is up. B171 makes stale marquees more likely than they should be, which is why
B173 says to land B171 first.

**One report's premise was wrong, and it made the request bigger.** "Press and
hold E should have the same behaviour as the Eyedropper tool with I" assumes
`I` is momentary. It is not — it is a latching switch, exactly like `E`.
Nothing in the application holds a tool for the duration of a key, so B176 is
about building that rather than about copying it. Filed on the reported
symptom, corrected in the entry, because a report that is wrong about the cause
is still right about the gap.

**The one claim filed as unconfirmed** is the palette docker's overflow hiding
the swatches. With the tree's height hard-coded it cannot push them off, so
B174 records the report as made and says to measure before changing anything —
and notes that fixing the resize is what makes real overflow possible, so the
scroll wants to land in the same commit rather than after it.

