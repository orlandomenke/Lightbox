# Ratchets

Line ceilings for the files that are already too big. They may shrink and may
not grow: new work goes into a partial or a collaborator rather than onto the
end of them. `MonolithRatchetTests` reads this directory.

**Why a ratchet and not a plan.** `docs/DESIGN-mainviewmodel-decomposition.md`
measured `MainViewModel.cs` at 10,098 lines and proposed extracting eight leaves
totalling about 2,000. By the commit that merged that document the file was
12,001 lines; four days later it was 13,110, across 94 commits. The plan was
sound and it was losing a race — the file gained more lines while the document
sat unstarted than the document proposed to remove. An extraction that costs a
branch and a full suite run per leaf cannot outrun feature work landing in the
same file, so the arithmetic had to be fixed from the other side first.

So this is deliberately the cheapest possible mechanism: no judgement about
whether a section belongs somewhere, no architecture, just a line count that is
not allowed to go up.

**Why line counts, given the files are shallow.** Re-deriving the design
document's own coupling analysis against the then-current 13,110-line file
reproduced it almost exactly: 54% of fields touched by exactly one section (the
document measured 54%), nine fields crossing five or more sections (it measured
nine), the shape tool still widest at 33 (it measured 32). The file was not
getting more tangled as it grew — it was getting longer at constant shallowness.
Length is the honest thing to measure, and it is what makes a file unnavigable.

## One file per budget

Each `<name>.md` holds the path it guards, the number, and every reason the
number has moved:

```markdown
# src/Lightbox.App/Views/MainWindow.axaml

budget: 4276

## Why it has moved
- **→ 4,276** (2026-08-14): the past-the-end cel's hatch (Q89) — …
```

They used to be a table in `MonolithRatchetTests.cs`, so every branch touching
*any* budgeted file edited the same C# file and two branches growing two
different files conflicted anyway (Q92). One file each ends that.

## The rules

**Lowering a budget is the point.** When an extraction lands, the number comes
down with it in the same commit, and the file can never climb back. That is what
makes this a ratchet rather than a cap — a cap with slack in it is a licence to
grow up to the slack, which is why a budget more than 250 lines above its file
fails the check too.

**Raising one is a decision, and there is no escape hatch on purpose.** If a
feature genuinely cannot land without growing one of these files, edit the number
and say why in the commit message — a visible line in a diff rather than an
environment variable nobody reads. Same reasoning as `LIGHTBOX_PUSH_TO_MAIN`:
make the bypass cost a sentence.

The only legitimate reason to raise one is that the file got *more legible* and
slightly longer. "A feature needed the room" is not on the list — that is what a
partial or a collaborator is for.

**A merged budget is measured on the merged tree, never taken from a side.** Two
branches that both grew a file each measured a tree the other had also changed,
so neither number is right afterwards, and taking either banks the other's
extraction as headroom nobody earned. That step is mechanical and was being done
by eye:

```bash
python3 scripts/ratchets.py remeasure     # after resolving a merge
python3 scripts/ratchets.py check         # what the test checks
```

**Nothing syncs these numbers, and that is deliberate.** Three of the four
ceilings equal their file's exact line count, which makes an automatic re-measure
look obvious — and it would delete the mechanism. A ceiling re-measured from the
tree can never be exceeded, so the test could never fail. `remeasure` is for
resolving a merge and is wired to no hook.
