# Q91 · Ledger ids collide on every parallel branch — partition the number space, or issue the numbers? — **answered 2026-08-14: issue them**

Raised by the owner: *"We have a lot of ledger ID conflicts when merging due to
working on multiple items at once. Would it be an idea to make the ledger
thematic? Like AI is 1 - 100 or 300, canvas is 101, 301 - 500 etc. So that we
cannot accidentally write duplicates anymore?"*

The problem is real and was measured rather than agreed to: in the six days to
2026-08-14 there were **six bug renumbers and three question renumbers**, one
bug renumbered twice because the second guess collided as well. `main` was
carrying a live duplicate — `Q87` used by both PR #279 and PR #280 — when the
question was asked, with `LedgerGateTests.TheLedgersInThisTreePassTheirOwnGate`
red on the default branch as a result.

**Answered: issue the ids.** Thematic bands were measured and would not have
done it:

- **Same-domain collisions are the common case, not the rare one.** New bugs are
  not spread evenly — of B150–B207, `canvas` is 57%, `ui` 20%, `timeline` 10%.
  Two concurrent branches pick the same domain with probability Σp² ≈ **39%**, so
  bands remove about 60% of bug collisions rather than all of them. The worst
  case in the six-day list is one of the 39%: the bone-icon bug (`ui`) collided
  with the docker-shortcut bug (`ui`), inside the band it would have had.
- **Questions have no domain at all**, and they were three of the nine.
- **Existing ids cannot move.** B1–B207 are cited from `ProjectViewModel.cs`,
  `BrushEngine.cs`, test names, design docs and commit messages, so bands would
  mean a legacy range plus new ranges above it — two vocabularies in one file.

The actual cause is upstream of the number space: **nothing ever issued an id.**
An author read the ledger, took the highest number in it and added one — which
is `max(what my branch fetched) + 1`, the same answer on two branches that
started from the same `main`. Partitioning by domain only makes that stale
snapshot smaller. So `bugs.py new` and `bugs.py freeid` allocate above every ref
the clone can see, `ids` reports a clash before the merge that would create the
duplicate, and `ids --fix` moves the entry with its citations.

What it costs, recorded because it is not free: **the allocator is only as fresh
as its last fetch.** Two branches allocating between the same pair of fetches
still land on the same number — the window shrinks from "as long as your branch
is open" to "as long as your fetch is stale" rather than closing. That is why
`--fix` is part of the answer and not a footnote to it: what is left is made to
cost a command instead of a hand-edited commit. A truly collision-free scheme
means content-derived ids (`B-7f3a`), which was on the table and refused — it
costs the ordering that makes `sort by id descending = newest first` mean
anything, and it costs being able to say a bug number out loud.
