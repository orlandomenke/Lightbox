# Q55 · Do the derived codemap files stay committed? — **answered: no, gitignored**

**Answered 2026-08-08, asked when the owner reported the treadmill directly:**
*"We keep running into the same problem due to Claude documents: index,
features and bugs. We tried guarding it but with each push main gets ahead and
the next branch always blocks due to merge conflicts on those docs."*

**Decision: stop committing `INDEX.md` and `FEATURES.md`.** They are derived
from the whole solution, so every branch that touches code rewrote them end to
end and any two parallel branches conflicted by construction — and GitHub runs
no merge driver, so every open pull request went red the moment any other one
merged, requiring a hand-merge of `main` into every survivor after every
merge. The files are gitignored beside `HOTSPOTS.md`; the session-start hook
builds them when stale or absent; CI runs `build` instead of `verify`; the
merge driver is retired. `LedgerGateTests.TheDerivedIndexIsNotTracked` pins it.

**What the alternative cost and why it lost:** the committed copy bought a
fresh clone an index without a ten-second build, defended by a local merge
driver plus a CI byte-verify. Both worked as designed and neither ended the
conflicts, because the web UI merge is the one place neither could run.

**Decided in the same exchange: the ledgers stay committed and hand-resolved.**
`BUGS.md` is authored prose no script can reproduce; its collisions are rarer
(two branches must both file bugs) and the pre-push `bugs.py ids` gate already
refuses the silent losses. Sharding it per domain was offered and declined.
