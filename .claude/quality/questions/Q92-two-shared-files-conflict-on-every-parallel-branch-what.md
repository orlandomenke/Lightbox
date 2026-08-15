# Q92 · Two shared files conflict on every parallel branch — what shape stops it? — **answered 2026-08-14: one file per entry, for both**

Raised by the owner immediately after Q91, which fixed the *id* collisions and
left the *textual* ones: *"So for both these files it also results in a decent
amount of conflicts. Can we find some way to mitigate that as well?"* —
`.claude/quality/QUESTIONS.md` and `tests/Lightbox.App.Tests/MonolithRatchetTests.cs`.

They are the same failure with two different remedies, because only one of the
two numbers involved is derived.

## The questions: one file each, and the index is not committed

Every branch that raised a question appended a section at the same place in a
3,689-line file, so two branches raising two questions conflicted **by
construction** — on top of the id collision Q91 had just dealt with.

**Answered: one file per question**, `Q<id>-<slug>.md` under
`.claude/quality/questions/`, with `QUESTIONS.md` becoming a generated index
that is **not committed** — the same move `INDEX.md` and `FEATURES.md` made in
Q55, for the same reason. A stored derived file is one every branch rewrites, so
committing the index would move the collision one artefact along rather than
ending it. `questions.py build` writes it from nothing and the session-start hook
runs that, so a fresh clone self-heals.

Two things fall out that are worth having on their own:

- **The gate got cheaper.** A question's id is in its filename, so listing every
  question at a ref is one `git ls-tree` and no file reads — where the single
  file had to be fetched and parsed in full for every ref compared against.
- **`questions.py check` is a new thing that can rot**, and is checked in CI: a
  file whose name and heading disagree carries an id nothing verifies.

What it costs: **81 files where there was one**, so "read all the open
questions" is now a directory rather than a scroll, and any tool that grepped
`QUESTIONS.md` for content rather than for headings has to walk a directory
instead. `questions.py find` exists for that, in the manner of `manual.py find`.

## The ratchet: one file per budget, and the number stays authored

`MonolithRatchetTests.cs` carries four line ceilings and, above each, a paragraph
explaining every move it has made. Every branch that touches a budgeted file
edits the number *and* writes prose — in one 267-line file shared by all four
budgets, so branches growing **different** files still met there.

**Answered: one file per budgeted file**, `.claude/quality/ratchets/<name>.md`,
with the test reading them. Branches touching different budgeted files stop
conflicting at all. Two branches growing the same one still conflict, and that
part is irreducible — see below.

**The number is not derived, and this is the part worth not getting wrong.** It
looks derived: three of the four ceilings equalled their file's exact line count
when this was written. But a ceiling re-measured from the tree can never be
exceeded, so a script that "synced" it would delete the mechanism and leave the
paperwork. The ratchet works precisely because the number is a *snapshot an
author must consciously raise*, which is what makes the raise a visible line in a
diff — the same reasoning as `LIGHTBOX_PUSH_TO_MAIN`.

So `ratchets.py remeasure` is deliberately **not** run by any hook. It exists for
one moment: resolving a merge, where the doctrine those long comments spell out
by hand — *measure on the merged tree, never take a side's number* — is
mechanical and was being done by eye. Taking either side banks the other side's
extraction as headroom nobody earned, and that is the one thing a ratchet must
not do.

The reasons stay authored and append-only, so a merge keeps both sides' entries
by the same rule the ledgers already use: **taking one side deletes the other's,
and leaves a file with nothing wrong in it.**
