# Bugs

What is wrong, and what would prove it is not.

**The checkboxes are derived, not typed.** Every entry names the regression
test that must exist for the bug to be closed, and
`python3 scripts/bugs.py sync` resolves that against
`.claude/codemap/map.json` and rewrites the mark.

| | meaning |
| --- | --- |
| `[ ]` | open — the test does not exist, or does not resolve |
| `[x]` | fixed — every named test resolves |

Deleting the test reopens the bug on the next run. That is the property worth
having: a bug marked fixed with nothing guarding it is worse than an open bug,
because it stops anyone looking.

An entry with no `evidence:` is **refused at check time**. If you cannot name
what would prove the fix, you have not finished describing the bug.

```bash
python3 scripts/bugs.py check          # status, exits 1 on drift
python3 scripts/bugs.py sync           # rewrite the marks
python3 scripts/bugs.py next           # highest-priority open bugs
python3 scripts/bugs.py mine timeline  # open bugs in one domain
python3 scripts/bugs.py stats          # counts per priority and domain
```

## Priority — severity × reach

|  | every session | common | occasional | rare |
| --- | --- | --- | --- | --- |
| **blocks work** | P1 | P1 | P2 | P2 |
| **corrupts art** | P1 | P1 | P2 | P3 |
| **wrong output** | P2 | P2 | P3 | P3 |
| **annoyance** | P3 | P3 | P4 | P4 |

*Corrupts art* outranks *wrong output* because a wrong render is visible and
recoverable; a damaged record is neither.

**Cost is not in the matrix, on purpose.** It is recorded on each entry so a
session with ten spare minutes can pick a cheap P2 over an expensive one, but
it never changes the order — folding effort into the rank is how the hard,
important bugs never get picked.

## Domains

One tag per bug, from this list, so an agent about to edit an area can find
what is already known to be wrong in it:

`brush` · `timeline` · `layers` · `canvas` · `transform` · `colour` ·
`export` · `project` · `ui` · `ai`

**The rule for an agent working in a domain:** fix its open **P1 and P2** bugs
alongside whatever you came for, each with its own regression test in the same
commit. Mention P3 and P4 without touching them — a request to change one
thing must not come back as a diff touching six. Anything needing a product
decision goes to `QUESTIONS.md` and is left alone.

---

## Open

- [ ] **B1** `P1` `timeline` Onion skin invisible since the document gained a paper layer `evidence: OnionGhostsShowOverThePaper`
  - Repro: open the app (paper layer present), draw on frame 1, add frame 2, turn onion skin on. No ghost.
  - Cause: `MainViewModel.PublishSnapshot` queues **every** onion pass first and then composites every layer over them. The paper is opaque and at the bottom of the stack, so it paints over all the ghosts. Before the paper existed the ghosts showed through a transparent stack.
  - Fix: interleave — for each layer, its own ghosts, then the layer. That is also what makes multi-layer onion read correctly.
  - Regression I introduced with the paper layer. Cost: S

- [ ] **B2** `P1` `timeline` Cannot draw on a layer whose cels are all cleared `evidence: PaintingWithNoKeyAtThePlayhead_CreatesOne`
  - Repro: clear every cel on a layer, pick the brush, drag on the canvas. Nothing happens and nothing is said.
  - Cause: `PaintTarget()` returns null when no cel at or before the playhead is keyed, so `BeginStroke` returns silently. Fill and gradient no-op the same way.
  - Fix: painting where there is no key should **create** one, which is what every animation tool does. Silence is the worst part — even refusing would be better than nothing.
  - Cost: S

- [ ] **B3** `P1` `timeline` Thumbnail never returns after a cel is cleared and redrawn `evidence: RedrawingAClearedCel_BringsTheThumbnailBack`
  - Repro: clear a cel, draw on it again. The timeline cell stays blank.
  - Cause: likely the same root as B2 — nothing is drawn, so there is nothing to show. If it survives B2, it is a missing `_dirtyThumbIds` entry in `ClearCelAt`.
  - Cost: S

- [ ] **B4** `P2` `brush` Blur, smudge and blender brushes only update on pen lift `evidence: SmudgeShowsMidDrag`
  - Repro: pick smudge or blur, drag across existing paint. The smear appears only when the pen lifts.
  - Cause: `FlushLivePreview` appends the draft into `_liveComposite`, but `PublishSnapshot`'s overlay branch only ever reads `_liveScratch`. The composite is computed every event and never shown.
  - Fix: for the active layer, a blur/smudge drag **replaces** the layer bitmap rather than overlaying a scratch — these tools modify pixels that are already there, so an overlay is the wrong shape.
  - Same class as the wet-media bug already fixed: brushwork must look while drawing the way it will look afterwards.
  - Cost: M

- [ ] **B5** `P2` `transform` Transform shows no live pixels, only the gizmo `evidence: TransformPreviewMovesThePixels`
  - Repro: Ctrl+T, drag a handle. The quad moves; the drawing does not until commit.
  - Cause: the gizmo is view-only chrome and nothing maps the strokes until `CommitTransformAffine`/`Perspective`.
  - Fix: preview through the live-scratch overlay, the same seam the gradient drag uses.
  - Cost: M

- [ ] **B6** `P2` `timeline` No way to delete a cel `evidence: DeleteCel_RipplesTheRest`
  - Repro: right-click a cel. There is "Clear cel" (blank it, keep the timing) and "Cut cel", but nothing that removes it and pulls the following cels back.
  - Cause: never built. `DeleteFrame` is a different operation — it deletes across every layer.
  - Fix: a ripple delete on one layer's row, one undo step.
  - Cost: S

- [ ] **B7** `P3` `transform` Transform does not affect gradients `evidence: TransformingAGradient_MovesItsAxis`
  - Repro: lay a gradient, Ctrl+T, move it. The ramp does not follow.
  - Cause: not yet established. `TransformOps.TransformStroke` maps `stroke.Points` for every tool kind, so a gradient's two axis points *should* move. Suspect the region filter (`MajorityInside` on a two-point stroke) or `Bounds` padding by `Brush.Size / 2` where a gradient's brush size means nothing.
  - Reproduce before fixing. Cost: M

## Fixed

Entries move here when `sync` closes them; the evidence stays so a deleted
test reopens the bug.
