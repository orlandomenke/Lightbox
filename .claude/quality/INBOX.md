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

- Erasing somethings shows the transparency checkerboard while erasing. Sometimes crashing the application.
- Press and hold E should have the same behaviour as the Eyedropper tool with I, a quick switch to eraser on hold, return to previous tool.
- On canvas icons show a cross instead of their respective tool icon; fill, eyedropper.
- Selection tool:
- Deselecting does not work anymore not by shortcut, not by on screen buttons.
- closing and opening a new document in the same session should not keep selection up.
- Backspace fill selection with background color, delete, delete whatever is inside the selection
- Vector tooling:
- Direct select is not able to select, or enter, strokes. Selecting should work like the arrow tool. On hover shouw points and handles; clicking the line shows all but selects all like arrow. Clicking a point or handle selects that handle, but keeps the rest of the points visible. Modifier key: widen stroke
- Pen tool: show on canvas icon for closing a shape. Clicking it should stroke the entire shape. Add modifier key (press and hold) to enter direct select tool. Like how illustrator handles it.
- Palette docker; create visual separation between the hierarchy and swatches. Hierarchy can be rescaled and scollable, do not overflow hide the swatches.
- Brush: ink is really responsive; flat medium and pencil lag.  

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

