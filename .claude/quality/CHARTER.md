# Quality charter

What "good" means here, concretely enough that an agent can check it and a
reviewer can disagree with it. The `/improve` loop treats section 1 as gates
(a round is rejected if any fails) and section 2 as the backlog it works from.

## 1. Gates — every round must leave these true

| # | Gate | How it is checked |
| --- | --- | --- |
| G1 | Solution builds with no errors | `dotnet build Lightbox.sln` |
| G2 | Every test passes | `dotnet test` |
| G3 | No promise disappeared | every entry in `.claude/codemap/FEATURES.md` from the previous round still has a passing test, or the removal is justified in `LOOP.md` |
| G4 | Performance budgets hold | the `Category=Performance` tests pass unweakened; a budget may only be raised with a measurement and a reason |
| G5 | Determinism intact | no new randomness in a render path; settings that affect pixels are stored per stroke |
| G6 | Claims are verified, not asserted | anything reported as fixed has a test that fails without the fix, or an explicit note that it could not be tested and why |

A round that cannot satisfy a gate is reverted or narrowed, never merged with
the gate marked "expected to fail".

## 2. Standing objectives — the backlog the loop mines

**O1 · Cover the hotspots.** `HOTSPOTS.md` ranks files by change risk. A file
in the top ten with no test reference is the highest-value test to write.

**O2 · Guard behaviour, not implementation.** A regression test should fail
for the reason the user reported, and keep passing through a refactor. Tests
that assert internal call order are a liability.

**O3 · Keep painting interactive.** Pointer-to-pixels must stay far inside a
60 Hz frame on the largest supported canvas (4K). Costs that scale with
canvas area per event are defects; costs that scale with the region a stroke
touched are fine.

**O4 · Keep memory bounded.** Caches are sized in bytes, never in item count —
96 frames is 100 MB at 960×540 and 3 GB at 4K.

**O5 · Prefer deleting to adding.** Two ways to do the same thing is a bug
report waiting to happen.

**O6 · Ask rather than assume.** A request that admits more than one sensible
reading goes to `QUESTIONS.md` with the options and a recommendation. The
loop keeps working on the unambiguous parts meanwhile.

## 3. Performance budgets

Measured on the dev container, which is slow; a mid-range desktop is several
times quicker. Budgets are set roughly 4× above observed medians so a noisy
shared runner does not produce false alarms.

| Path | Budget | Observed |
| --- | --- | --- |
| Pointer event during a stroke, 4K, 180 px brush | 20 ms | ~2 ms |
| Whole stroke + commit, 4K | 400 ms | ~78 ms |
| Stroke + undo, 4K | 1500 ms | ~520 ms |
| Live effect-brush segment, 960×540 | 12 ms | — |
| Flood fill, 960×540 | 250 ms | — |
| Frame cache ceiling | 512 MB | 601 MB peak incl. buffers |

Raising a budget requires a measurement in the commit message explaining what
got slower and why that is acceptable.

## 4. Definition of done for a user-visible change

1. It works when driven the way a user drives it (headless pixel test where
   the UI is involved, not only a view-model assertion).
2. A test fails without the change.
3. The behaviour inventory gains a line describing the new promise.
4. Performance gates still pass on the largest canvas.
5. Anything ambiguous that was decided by guesswork is written down in
   `QUESTIONS.md` so the guess can be corrected cheaply.
