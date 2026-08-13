# Bones and spline deformers for 2D — design

*Status: designed, not scheduled. Decisions taken with the owner 2026-08-13;
nothing here is built. The roadmap item under Pillar 3 carries the evidence
anchors that will flip when it is.*

## The tension this design resolves

Bones are the *other* animation paradigm. Every big package picked a side:
Spine, Live2D and DragonBones are puppet tools that barely draw; Toon Boom and
Moho bolted rigs onto drawing tools and the two halves famously fight — the
rigged parts and the drawn parts of a Harmony scene do not feel like one
application. Lightbox's record allows a cleaner marriage than any of them,
because of what invariant 1 already promises: the document is stroke geometry
and the pixels are derived.

## The feature bar, measured against the field

Across Spine, Harmony, Moho and Live2D, "fully featured" reduces to eight
things. Everything else those packages ship is UI over these:

1. A bone **hierarchy** with a bind pose.
2. **FK posing** with pose keys and interpolation curves.
3. **IK** — two-bone limbs plus longer chains, targets, bend direction.
4. **Constraints** — aim, transform-copy, and **spline chains** (the
   bendy-bone / curve deformers Moho and Harmony use for tails, hair, capes).
5. **Skinning** — geometry follows bones by per-point weights.
6. **Corrective shapes driven by joint angle** — Moho's "smart bones", the
   feature professionals actually choose Moho for.
7. **Secondary motion** — jiggle and springs.
8. **Export** — both baked frames and a live rig a game engine can animate.

## The core idea: skin the stroke points, not the pixels

Every raster puppet tool deforms a *texture* over a mesh, and the result
always has the rubber-sheet smell — lines stretch, marks smear. Lightbox
instead deforms the **control points of strokes** and lets `BrushEngine`
re-stamp the mark along the deformed path. A bent arm is *re-drawn*, not
warped: line weight, grain and edge quality survive any pose at any
resolution. That is the design's spine, and it is the thing the big packages
structurally cannot do to painted linework.

### The one trap, and the rule that closes it

It is invariant 7's exact shape. `Hash01` seeds every dab dynamic from the
IEEE-754 bits of its position, so naively deforming coordinates re-rolls
scatter, jitter, size and the colour jitters per pose — a rigged character
would **boil** as it moves. The rule is the same one the output-scale work
wrote down:

> **Dynamics seed from the bind-pose coordinates; the transform moves only
> placement.** Each dab carries its rest position for seeding and its posed
> position for stamping.

Same pose, same pixels, forever — invariant 2 holds, and a rigged character
animates without boiling, which is a claim not even Moho can make about
textured brushes.

## The record — optional-absent, like the camera

The camera is the precedent: authored, keyframed, saved, and it never mutates
a stroke — it decides what a render shows. Bones are many small cameras with
weights.

| Key | Shape | Absent when |
| --- | --- | --- |
| `Doc.Armature?` | bones (id, name, parent, length, rest transform), IK chains, constraints | never rigged — no key, no cost |
| `Stroke.Weights?` | sparse per-point bone weights | stroke unbound |
| `Scene.PoseTrack?` | keys: frame → per-bone transforms, with curves | never posed |

Weights live **on the stroke** (invariant 4: settings that reach pixels are
per stroke). Auto-weighted at bind by distance/falloff — bounded biharmonic
weights or similar, implemented from the papers — then paintable per stroke.

### Determinism rules

- Linear-blend skinning in doubles, fixed evaluation order.
- IK solved by FABRIK or CCD (published, patent-free) with a **fixed
  iteration count** rather than convergence-to-tolerance, so a reload and an
  inbetween solve bit-identically.
- Secondary motion is solved at bake time, seeded from geometry, never from a
  clock or an index — visual life without logical randomness.

## Live pose and bake are both first-class *(owner's decision)*

The fork was: is a "rigged layer" whose pixels derive live from strokes +
pose allowed in the document, or is baking to ordinary strokes the only
citizen? **Decided: live posing during authoring, and bake at will.** The
cost is a new render path and frames whose pixels depend on the armature; the
pose track is recorded, so replay stays exact and invariant 1's spirit —
reload renders the same image — holds. What made the alternative untenable is
iteration speed: re-baking on every pose tweak is exactly where puppet tools
beat drawing tools today, and conceding that would concede the feature.

**Bake writes ordinary strokes.** A baked frame is indistinguishable from a
drawn one, so every downstream system — export, inbetweening, editing —
already works on it. Bake is also the inbetweener's substrate and the export
path's input.

## The marriage with frame-by-frame — and with the inbetweener

The armature is a *construction armature under drawing*, not a replacement
for it. Phase 1 alone earns its keep with no deformation at all: bones give
the AI inbetweener the thing it fundamentally lacks — **intent**.
Interpolating the armature solves the correspondence problem; the inbetweener
draws over the interpolated pose. Anchors ride bones, so the limb-length
checker (roadmap, construction guides) gets its per-frame annotations free.
G12 applies to that conditioning work: it touches the AI surface and gets the
engineer/director pair.

## Phases, each shippable alone

| Phase | What | Cost |
| --- | --- | --- |
| 1 | Armature record, bone tool, FK posing, pose keys, armature onion-skin, anchors ride bones, inbetweener conditioning | M |
| 2 | Bind + LBS on stroke points, rest-pose seeding, bake-to-strokes, live rigged layers | L |
| 3 | IK, aim/copy constraints, spline chains | M |
| 4 | Angle-driven correctives — the artist *draws* the fix at a joint extreme; it interpolates by angle | M |
| 5 | Deterministic secondary motion (bake-time springs) | S–M |
| 6 | Export (below) | S |

Total: XL. Nothing starts until the owner schedules it.

## Export *(owner's decision: own format + Godot + DragonBones)*

Baked frames flow into the sprite-sheet / engine path that already exists,
multi-document sheets included — phase 6 costs S because of that. For live
rigs:

- **Own JSON schema is the source of truth** — the document format is plain
  JSON everywhere else, and the rig is no different.
- **Godot Skeleton2D converter** proves the schema end to end in an open
  engine.
- **DragonBones format converter** for reach into Unity and other engines
  through a BSD-licensed format. This was chosen over the cheaper own+Godot
  option knowing it is more surface to maintain: two converters, and the
  DragonBones ecosystem is semi-dormant, so its importers vary in quality.

## Licensing constraints (the "no proprietary code" requirement)

- **Never Spine's format or runtimes.** The runtimes are proprietary and
  license-gated; emitting Spine-compatible JSON invites exactly the
  entanglement being ruled out. Same for Live2D's SDK.
- **Algorithms from papers are fine** — LBS, dual-quaternion skinning,
  FABRIK, CCD, ARAP, bounded biharmonic weights are published and
  patent-free. Implement from the papers; vendor nothing GPL-incompatible
  (MIT/BSD/Apache-2.0 are compatible with GPL-3.0; Spine's and Live2D's
  licenses are not, and their code does not enter this repository in any
  form).
