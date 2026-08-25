# Q152 · Can the pose track animate on 2s — stepped bone timing? — **answered 2026-08-23: yes, and it must be optional**

Raised by the owner: bones give fluid movement through auto-tweening, but if
the rig could be animated on 2s (or any interval), posed frames become
reusable drawings for frame-by-frame work. Asked whether that should exist.

**Answered yes, with one binding constraint: it is optional** — in the house
sense of the word. The default stays what it is today (fluid interpolation
every frame), a document that never steps its rig writes no new key, and no
stepping UI appears until asked for. The camera's rule, applied to timing.

## Why this is the right feature and not a compromise

Interpolating a rig every frame is exactly what makes puppet animation read
as CG-floaty rather than drawn; sampling the same fluid motion on 2s is the
standard cure (the Spider-Verse trick). For Lightbox it does more than fix a
look: a held pose lands on the exposure sheet's grid, and **bake-to-strokes**
(already first-class, `docs/DESIGN-bones.md`) turns each held pose into an
ordinary drawing — so a cycle posed once becomes drawings an artist can
re-expose, re-time with the timing presets, and hand to the inbetweener. It
is the bridge between the bones machinery and the frame-by-frame pillar,
using the timing vocabulary the roadmap already speaks ("a drawing on 2s
under a peg moving on 1s is a moving hold").

## The mechanism — two halves, recommended together

The recommendation put to the owner was **both**, and the answer ("it should
be optional") constrains rather than contradicts it, so it stands as the
shape to build when this is scheduled:

| | What it costs |
| --- | --- |
| **Both: `Hold` ease + track step interval** (recommended) | S–M together. Two knobs, but they answer different acts and neither substitutes for the other. |
| Track step interval only | A single dead-still hold needs a duplicated key, which is clumsy for the most common act in pose-to-pose work. |
| `Hold` easing only | Retiming a move means moving every key by hand — loses the auto-tween-then-step workflow that motivated the question. |
| Follow the exposure sheet | One timing grid, but couples the rig to exposures: a live rigged layer with no drawn cels has no exposures to follow. Most machinery. |

1. **`Easing.Hold` on a `PoseKey`** — the pose freezes until the next key.
   Pose-to-pose animation: the keys *are* the drawings, reused exactly. One
   enum member and one switch arm (`Easing.cs`, `PoseKey.Ease`).
2. **A step interval on the `PoseTrack`** — the track still auto-tweens
   fluidly, but evaluation snaps to every Nth frame and holds between. The
   move can be retimed without re-posing, and the *result* steps like drawn
   animation. Deterministic by construction (a `floor`, no state).

## What "optional" pins down for the implementation

- The interval lives **on the record** (`PoseTrack`), nullable, absent until
  authored — never a preference, so a rigged scene reopens exactly as it was
  left and changing a setting never re-times existing animation (invariant
  4's spirit).
- A document that never steps serializes byte-for-byte as today —
  `Assert.DoesNotContain("\"step\"", json)` (or whatever the key is named)
  belongs in the landing commit, per the "optional has two halves" rule.
- The default `Ease` stays `EaseInOut`; `Hold` is a choice on a key, not a
  new default.

## Landed 2026-08-23, as recommended

Both halves, on this decision's own branch: `Easing.Hold`, `PoseTrack.Step`
(nullable, absent until authored), and the sampling in
`ArmatureOps.EffectivePoseAt` — the render pose, never `PoseAt`, so authoring
stays fluid and the jiggle walk holds inside a step. `PoseSteppingTests`
guards the record and the split; the UI is the pose key's ease menu and a
"Pose on…" item on the armature row, mirroring the camera's menus.

## What this did not answer

Whether the step interval eventually accepts the timing presets' patterns
(1-1-2-3-4 slow-ins on pose sampling, not just a constant N) — plausible
unification, real machinery, left for whoever misses it. And where the
stepping control surfaces in the UI (the bone options bar, the timeline, or
both) is a design-system question for the landing branch, not decided here.
