# Q76 · Decomposing the two big view files: which tool, which file first, and whether to cap growth — **answered 2026-08-13**

**Asked with the question prompt after a review of `MainViewModel.cs`,
`MainWindow.axaml.cs` and `docs/DESIGN-mainviewmodel-decomposition.md`; all three
answers took the recommendation.** The review's own finding is why it was asked
at all: the design document was written against a 10,098-line file, merged when
the file was 12,001, and its every line anchor was off by 1,200–2,000 lines.

What survived the review, and is worth recording because it was measured twice:
re-deriving the document's coupling analysis against the now 13,110-line file
reproduces it almost exactly — **53% of fields touched by exactly one section**
(it measured 54%), **nine fields crossing five or more** (it measured nine), the
shape tool still widest at **33** (it measured 32). The file is not getting more
tangled as it grows; it is getting longer at constant shallowness. The
section → hub diagnosis stands.

Three questions, three answers:

1. **Which tool? — (a) split by mechanism, as recommended.** The document
   rejected more partial files outright, on the grounds that they buy
   navigability with zero decoupling because every section keeps its licence to
   touch every field. True in the language, false here: the nine partials that
   exist use 0–13 distinct fields each and declare most of them locally —
   `StrokeSelection` touches none, `Momentary` declares 4 of 4, `Audio` 10 of 13.
   3,527 lines left the file that way and stayed loose, because **giving a
   section its own file creates the pressure to declare its state there.** So
   partials for a section that owns its state and touches ≤5 hub fields;
   extracted collaborators, in the manner of `SelectionManager`, for the hub and
   the genuinely shared clusters. **What that choice costs:** two routes to
   explain and a judgement per section about which applies — mitigated by
   `scripts/monolith.py`, which answers it from the field counts. The document's
   real point is kept: a partial for a *hub* would look solved and decouple
   nothing.
2. **Which file first? — (a) split the view first, as recommended.** The same
   analysis run on `MainWindow.axaml.cs` comes back inverted: **79%** of fields
   single-section, and exactly **one** field crossing five or more — `_vm`, used
   in 35 of 37 sections. There is no hub to name and no shared mutable state; it
   is 37 near-independent handler groups over one view-model reference. So the
   view needs *splitting*, not decomposing, and it is the cheap safe proof of the
   pattern before the expensive file. Two things the review turned up alongside:
   the render and publish core is not where the markers say it is — it sits from
   roughly `:11857` under a marker reading *video clip bars (Q57)* — and
   `MainWindow.axaml`, 4,188 lines of XAML with **no test file**, is above both
   C# files on `HOTSPOTS.md`'s risk table.
3. **Cap the growth? — (a) yes, a size ratchet, as recommended.** Since the
   document was merged: **94 commits to `MainViewModel.cs`, +2,793/−965 lines,
   zero leaves extracted.** The file gained more lines while the plan sat
   unstarted than the plan proposed to remove, and an extraction costing a branch
   plus a full suite run per leaf cannot outrun that. `MonolithRatchetTests` now
   holds a line budget for the four oversized files, seeded at current length:
   they may shrink and may not grow, a budget comes down with the extraction that
   earns it, and a second test caps the slack so a stale budget cannot become room
   to regrow. **What that choice costs:** an occasional forced decision mid-feature
   about where new code goes. There is deliberately no environment-variable escape
   hatch — raising the number in a diff is the visible form of the same decision,
   which is the reasoning behind `LIGHTBOX_PUSH_TO_MAIN` applied to a line count.
