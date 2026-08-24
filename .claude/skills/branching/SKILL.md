---
name: branching
description: How branches, merges, pull requests and ledger ids work in Lightbox, and the incidents that produced each rule. Read before creating a branch, resolving a conflict in BUGS.md / ROADMAP.md / a ratchet, renumbering a bug or question id, merging to main, or when a push is refused by .githooks/pre-push.
---

# Branches, merges and pull requests

`CLAUDE.md` carries the rules. This carries the reasons — six days of measured
id collisions, two retired generations of merge machinery, and the five
commits that went straight to `main` with the rule already written above them.
Read it when a rule looks arbitrary or when you are about to resolve a ledger
conflict by hand, which is the one place where doing the obvious thing
silently destroys work.

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
nothing ever **issued** an id: an author read the ledger, took the highest
number
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
measured instead of assumed (Q91): it would have stopped roughly 60% of the bug
collisions, 0% of the question ones, and not the worst case in the list — the
bone-icon bug collided with another `ui` bug, inside the band it would have been
given.

**The shape that stops the *textual* conflict is one file per entry** (Q92), and
it now applies to both places where every branch wrote to the same spot:

| | |
| --- | --- |
| `.claude/quality/questions/` | one file per question. Raising one used to mean appending a section to a 3,689-line file, so two branches raising two questions conflicted by construction. `QUESTIONS.md` is a generated index and is **not committed**, for Q55's reason. |
| `.claude/quality/ratchets/` | one file per line budget, holding the number and every reason it has moved. They were a table in `MonolithRatchetTests.cs`, so two branches growing two *different* oversized files still met there. |

The ratchets are the case where the derived-file trick does **not** apply, and
the reason is worth keeping: a budget looks derived — three of the four equal
their file's exact line count — but a ceiling re-measured from the tree can
never
be exceeded, so a script that synced it would delete the mechanism and leave the
paperwork. `ratchets.py remeasure` exists for one moment only, resolving a
merge,
where *measure on the merged tree, never take a side's number* is mechanical and
was being done by eye. It is wired to no hook on purpose.

**`python3 scripts/branchstate.py` answers "would this merge?" before a reviewer
does**, and separates the two kinds of conflict — authored files, which need a
decision, from the generated index, which needs a rebuild. A `PostToolUse` hook
runs it after any `dotnet build` or `dotnet test` that passed, alongside
re-deriving
the ledgers, so both facts arrive when the code has just changed rather than
when
somebody remembers to look. It stays silent unless something moved, refuses to
touch anything while a build is red, and refuses again mid-merge — rewriting
`BUGS.md` while somebody resolves a conflict in `BUGS.md` would destroy the
resolution.

**The derived ledgers resolve against `map.json`, which is gitignored, so a branch
switch leaves it describing a tree nobody is looking at.** That produced two
opposite lies in one `bugs.py check` — a bug reported fixed that was not, and
one
reported open that was. `evidence.py` now rebuilds when the index is stale
rather
than answering from it, because a wrong answer that leaves no trace in the
diff is
the kind nobody catches.

**A branch is one objective, and its name says which** — `<type>/<domain>/<id>-<slug>`
for a bug, as in `fix/brush/B39-effect-brush-scratch`, and `<type>/<slug>` for
work
that has no ledger id.

**The domain is in the name for the same reason `BUGS.md` groups by it**: work is
picked up by area, not by number. A branch list reading `fix/B67-…`,
`fix/B62-…`,
`fix/B58-…` says nothing about which parts of the application are in flight,
so two
branches heading for the same file are invisible until they collide. With the
domain
in front, four open branches are legible at a glance. Use the domains `bugs.py`
already knows — brush, timeline, layers, canvas, transform, colour, export,
project,
ui, ai — so the branch, the ledger entry and `bugs.py mine <domain>` all
agree. The agent has the full convention and
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
