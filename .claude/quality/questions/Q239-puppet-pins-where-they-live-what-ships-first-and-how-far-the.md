# Q239 · Puppet pins: where they live, what ships first, and how far the motion options reach — **answered 2026-10-10**

Raised by the owner, 2026-10-10: *"I just learned of After Effects puppet
tool. A mesh (that can be simplified visually to simple lines) that can deform
the object. Pins that act as joints. Options to change the motion; arc,
fluidity, squash and stretch etc. I want that."* — with an investigation, a
plan and tests asked for before any implementation, and the performance lab
named as part of the tests.

What it blocks: `docs/DESIGN-puppet-pins.md` and every branch that builds it.

The investigation found most of a puppet system already here — a bone rig
with posing, inverse kinematics, constraints, spline chains, weight painting,
jiggle, stepped timing and engine export — and three real gaps: **immediacy**
(drop three pins and drag, against build a skeleton, bind, fix weights), a
**mesh that knows the drawing's shape** (bone auto-bind and the cage warp both
measure straight-line distance, so an arm handle pulls a nearby leg), and
**squash and stretch** (a bone pose is rotation and translation; nothing in
the rig can scale). Arc and fluidity exist in part — bones rotate, so they
arc; jiggle is a spring — but not as controls an artist reaches for by name.
After Effects' own Puppet tool has none of the three motion options; they
were asked for as principles, and are Lightbox's to define.

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **Where pins live** | **Inside the rig** (recommended): a second kind of handle in the same `Armature` and pose track | A separate puppet deformer is closest to After Effects and builds bake, jiggle, timing on 2s, export and the timeline rows a second time, with two systems that can disagree on one character. Keyframing the cage is cheapest and has no pins and no shape-aware mesh. |
| **First cut** | **Pins and mesh first, motion options second** (recommended) | Motion options first fills the one true gap in the rig soonest and leaves immediacy untouched. Everything in one plan is the most coherent design and the largest branch, with the mesh risk blocking all of it. |
| **Motion reach** | **Bones and pins alike, one mechanism** (recommended) | Pins only leaves bones rigid as designed and needs no exporter work, and puts the same slider on one handle and not the other. |

**Recommendation:** all three as marked, and the owner took all three. What
"both" costs is on the record: bones gain scale for the first time, which
changes the pose record, the solver, and both engine exporters.

## What follows from them, not up for preference

- **The record stays strokes.** A pin moves stroke points and the brush
  re-stamps them; the picture is never warped (invariant 1, and the spine of
  `docs/DESIGN-bones.md`). Dabs seed from the rest path, so a pinned drawing
  does not boil — the mechanism (`Stroke.RestPoints`) is the one bones use.
- **Imported pixels ride the same mesh**, as they do under the cage (Q199).
- **Deterministic by construction.** A direct factorisation with a fixed
  elimination order and a fixed number of rounds — never iterate-to-tolerance
  (invariant 2; the bones design's determinism rules).
- **Optional means absent.** A rig with no pins writes no `pins` key; a
  document with no rig is untouched.
- **A drag is bounded work** (invariant 6): the mesh has a vertex cap, the
  solve is prepared once when pins are placed, and the budgets are written
  down as tests before the solve exists.

## Left open, to be asked when they are reached

- Whether a pinned layer's mesh is derived per drawing (the plan's
  assumption) or frozen when the first pin drops. The spike in the plan's
  step 1 measures how much a mesh changes between neighbouring drawings
  before this is put to the owner.
- What "fluidity" names, exactly — follow-through lag, overshoot, or
  smoothing of the path between keys. Phase 2's question, with pictures.
