# Q101 · Which bone-system phase next, after the panel fixes? — **answered 2026-08-16: live-pose weight painting**

Asked after B240/B229 landed (PR #304): phases 1–3 of `docs/DESIGN-bones.md`
are built, the doc says nothing further starts until the owner schedules it,
and the owner asked to continue. Prompted with four options.

| | What it costs |
| --- | --- |
| **Phase 4 — angle-driven correctives** (recommended, *not chosen — but see below*) | Size M. The phase that makes deformation look drawn rather than puppeted, and the foundation of the drivers design. |
| **Phase 6 — rig export** | Size S. Own JSON + Godot + DragonBones; proves the schema end to end, adds converter surface to maintain. |
| **Phase 5 — secondary motion** | Size S–M. Bake-time springs; polish for feel, builds on splines. |
| **Live-pose weight painting** (**chosen**) | Size S. Least new capability, fastest to land — but it closes the loop on what just shipped: you correct weights while *seeing* the deformation you are correcting, instead of scrubbing away to check. |

The recommendation went to correctives for capability; the owner chose the
workflow gap instead — and then the choice cost less than the table says,
because **correctives landed anyway from a parallel thread** (PR #307,
`feat/bones/correctives`, merged the same day). The two were not actually
competing for the same hands.

What the choice buys: weight painting stops being a rest-pose-only activity.
The dab hits the points **where the artist sees them** — their posed
positions at the playhead — and the heat view draws there too, so the
armpit being fixed is the armpit on screen. The record does not change
shape: weights are the same per-point values against the same bind-pose
strokes (Q81 stands — the *edit* is still authored at rest, only the
brush's hit-test and the feedback move with the pose).
