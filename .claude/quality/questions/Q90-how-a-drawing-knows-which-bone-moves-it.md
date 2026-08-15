# Q90 · How a drawing knows which bone moves it — **answered 2026-08-14**

Prompted by the owner's question, which exposed more than it asked: "is it
bound to the layer? To the layer group? Is it assignable?" The honest answer
was **none of those** — `Stroke.Weights` is per stroke, `Assign` acts on the
current stroke selection on the current frame, and layers know nothing about
the rig. Fine for one illustration; for a two-layer character over 200 frames
it is 400 manual binds, and nothing binds a stroke drawn after rigging. For an
application whose stated unit of work is a sequence, that is a hole rather
than a missing convenience.

The owner's own proposal was a layer *lock* to the layer above or below,
carrying alpha lock, "bone lock" and more. Answered:

1. **Layers link, and the link holds across frames** — the owner's shape,
   sharpened by their own words: *"link layer across frames … so we can paint
   lines, colors, details, effects and only the linked layers move along."*
   Recommended layer-level binding was declined, and rightly: it makes each
   layer state its own relationship to the rig, where what an artist has is a
   **set of layers that are one drawing**. Linking says that once. Because the
   link is a property of the layer structure rather than of a drawing, it
   applies to every frame at no extra cost — which is the half that closes the
   400-binds hole.
2. **A general link, several properties travel it** — recommended
   bone-specific was declined. Cost accepted knowingly: every property that
   travels has to answer what inheriting it *means*, and some have no sensible
   answer. The mitigation is that travelling is **opt-in per property on the
   link**, so a link made for bones does not silently start sharing alpha lock.
3. **The bone is named on the layer**, not inferred from adjacency.
   Layer-above/below addressing was declined for the reason it keeps being
   declined here: reordering layers would silently retarget the link, and a
   silent retarget is the invisible-failure shape this tool has already been
   reported for three times. The owner's ctrl/alt+RMB gesture survives as a
   fast way to *join a link*, which is stable under reorder in a way that
   "follow whatever is above me" is not.
4. **A stroke drawn on a linked-and-bound layer is bound.**

One interpretation made rather than asked, recorded because it departs from
the letter of (4) while serving its intent: the layer's binding is resolved at
solve time for any stroke that has no weights of its own, instead of writing
`Weights` into each stroke as it is created. It delivers the same thing an
artist sees — draw on a rigged layer and the line is rigged — and it is
better on three counts. It binds strokes drawn *before* the layer was linked,
so linking is retroactive rather than only forward. It writes no per-stroke
key on 200 frames of drawing, which is the camera's rule. And there is one
source of truth rather than a copy in every stroke that can drift from the
layer that made it. Per-stroke weights stay exactly as they are: the override
an artist paints when a shoulder needs two bones, and they win over the
layer's binding wherever they exist.

**The gestures, added 2026-08-14 by the owner.** Adjacency returns as the
*gesture* rather than as the addressing, which is exactly the distinction (3)
preserved — the gesture reads the neighbour once, at the moment it is made,
and what it writes is a link membership by id. Reordering afterwards is
therefore safe, where "follow whatever is above me" would not be.

- **Ctrl+Shift+RMB** — link to the layer above. **Ctrl+Alt+RMB** — the layer
  below. **Shift+RMB** — remove the link.
- **RMB** — the docker's own context menu, unchanged, with **Linking** and
  **Follows the rig** added as flyouts: the same options, named, plus the
  per-property switches, so the gestures are discoverable rather than
  folklore. A *bare* right-click deliberately does nothing new — that menu
  already carries rename, reorder, merge and folders, and opening a link menu
  over it would shadow the lot.

**Every link gesture is on the right button, and that is the correction that
produced the mapping.** The first spec put the menu on Ctrl+click, which is
the docker's multi-layer toggle. Raised, and the owner moved the gestures
rather than the selection — *"You are right. Lets move it to ctrl + shift and
ctrl + alt to link layers. Shift click to remove the link."* Taken as
right-button throughout, because moving off Ctrl only helps if Shift is
cleared too: Shift+click is the docker's range select, so a left-button
unlink would have traded one collision for another. Keeping the whole left
button for selection is what makes "Ctrl+click still toggles" and "Shift+click
still ranges" both true at once.

**The docker draws it, added 2026-08-14.** A link nobody can see is a link an
artist has to remember, so a linked row is indented and carries an elbow down
its left edge in the link's colour. The owner described two cases — an elbow
one way up for a layer linked above, the other way for below — and it is
generalised to a **bracket**: corner at the top of a run, line with a tick
through the middle, corner at the bottom. The two cases are what that reduces
to for a pair, and it keeps reading with three or more, which their two-case
form does not cover.

The mark is asked of a row's **neighbours** rather than of the link's
membership order, and that is the part worth keeping: a link's members need
not be adjacent, so a bracket drawn from membership order would run a
continuous line down the side of a row that is not in the link.
`LayerLinkMark.Detached` is the honest answer for a member with neither
neighbour in the link — a tick, joining nothing.
