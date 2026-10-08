# Q217 · Bone auto-weights: rigid with a joint zone, new binds only, per-bone size — **answered**

**Answered 2026-10-08.**

1. **Falloff: rigid, blending only at joints** (as recommended). Each point
   follows its nearest bone fully and blends with the neighbouring bone only
   inside a zone around the joint, so a straight line along a bone stays
   straight and bends at the elbow — Spine's result without building meshes.
2. **Only new binds** (against the recommendation). Nothing that exists poses
   differently because of the falloff. Strokes already auto-bound keep their
   stored weights, and a layer that follows the whole skeleton keeps the old
   inverse-square falloff unless it is bound after this change — which needs
   a stored marker on the layer saying which weighting it uses, absent for
   the old behaviour. **Cost, as stated when asked:** a new key, and two
   weighting behaviours to keep forever, because an old document must keep
   posing exactly as it did.
3. **Joint zone: per bone, with defaults** (against the recommendation of one
   value per rig). A value on each bone, absent until changed, with a default
   derived from the bone so an untouched rig needs no setting. **Cost:** a
   field in the bone panel for every bone, where a per-rig value would have
   been one.
4. **The two plain bugs are fixed everywhere** (as recommended, asked
   separately because answer 2 reached further than the falloff): a two-point
   stroke posed as a chord (B404) and pruned weights not renormalised (B405).
   Only the broken cases change; nothing in the file does.

Raised by: the owner, 2026-10-08 — "if I draw a straight line and want to
rotate the bone, the line should still be straight for most part of it …
But I am seeing artifacting."

What it blocks: the auto-weight rewrite. Measured on a two-bone arm (100 px
bones, a line 10 px off them, forearm at 90°) before asking:

- a **line-tool line** (two control points) skipped densification and was
  posed as its chord — 72 px of change between neighbouring points, the
  upper-arm half swinging off its bone (B404);
- a **freehand line** under `Skinning.AutoBind`'s inverse-square falloff,
  normalised over every bone, moved up to 7.3 px and bowed 1.3 px over the
  upper arm, whose bone did not move — a point 50 px from the elbow still
  took 4% from the forearm;
- on a **four-bone chain** the pruned weights summed to 0.984, so moving the
  whole rig 300 px left points up to 4.7 px behind (B405).

What the alternatives cost:

- *Steeper falloff* — a one-line change, and still no truly rigid stretch.
- *Bounded biharmonic weights* — the research-grade answer, a separate
  roadmap item with its own solver.
- *Layers and new binds* (the recommendation for 2) — whole-skeleton layers
  would have posed better at once with no new key; declined so that no
  existing document changes how it poses.
- *Per rig* (the recommendation for 3) — one setting instead of one per bone.

The part that was never a preference: whichever falloff, weights must sum to
1 after pruning, or the drawing trails the rig — that is B405, not a choice.
