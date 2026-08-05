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

## Layer-name rename exits immediately on double-click

- **Area:** Layers
- **Type:** Regression
- **Summary:** Double-clicking a layer name to rename it immediately exits rename mode, preventing layer renaming.
- **Evidence:** Reported after recent changes.
- **Steps:** Double-click a layer name.
- **Expected:** The layer name enters rename mode and can be edited.
- **Actual:** Rename mode exits immediately.

---

## Character sheet name is requested again when saving

- **Area:** Character sheet saving
- **Type:** UX
- **Summary:** After a character sheet has been named through the name prompt, the save dialog asks for the name again. The sheet name should be used as the document name.
- **Evidence:** Reported behavior: a name was set through the character-sheet name prompt before the save dialog appeared.
- **Expected:** The save dialog uses the existing character-sheet name as the document name.
- **Actual:** The name must be entered again in the save dialog.

---

## Unsaved-changes badge remains after document is saved

- **Area:** Document tab
- **Type:** Bug
- **Summary:** The unsaved-changes badge remains visible after saving a document that has no further changes.
- **Evidence:** Reported behavior: the badge remains after saving. Changing an attached character sheet correctly results in the badge being shown.
- **Expected:** The badge is hidden after a save when neither the document nor its related character sheet has changed.
- **Actual:** The badge remains visible after saving.
- **Notes:** Brush-setting changes are saved separately and are not part of this badge mechanism.

---


## Needs a decision

<!-- Reports that could not be turned into a BUGS.md entry without a human call. -->

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
