# Q215 · Krita-style inherit alpha: folder shape, union, folder boundary — **answered**

**Answered 2026-10-08, every part as recommended:**

1. **Turned on per folder.** Right-click a layer inside a folder → *Keep the
   layers above inside this*. Every layer above it in that folder is carved to
   the shape, including layers added later. Per-layer clipping stays for the
   exceptions.
2. **The shape is everything beneath, within the folder** — the union, Krita's
   inherit-alpha rule — not one base layer. It is a new mode: a Photoshop-style
   clip in an existing document renders exactly as before.
3. **The existing clip stops at the folder boundary.** A clipped layer at the
   bottom of a folder renders unclipped instead of clipping to a layer outside
   the folder.
4. **Clarity aids taken:** a bracket in the docker from the carved layers down
   to the shape, a row that says *No shape below* instead of failing silently,
   and a `ShortcutMap` binding plus the MCP surface. **Not taken:** dimming the
   canvas outside the shape while painting.

Raised by: the owner, 2026-10-08 — "Krita has alpha inheritance … I could
paint sections and not worry about drawing outside of the lines. I want that
in this app. But unlike in Krita I want this to be more clear and easier to
use."

What it blocks: the feature itself. The code had one answer — Photoshop's
positional clip (`LayerShapes.BaseOf`) — and three ways it falls short of the
workflow described:

- **One base, not a union.** A shading layer over separate skin, hair and
  cloth flats clips to the cloth flat only. Krita's inherit alpha clips to
  everything below it in the group, and that is what makes one shading layer
  per character possible.
- **It ignores folders.** `BaseOf` walks the flat `Scene.Layers` list, so a
  clipped layer at the bottom of a folder clips to whatever sits outside it.
- **It is per layer.** "Everything above this stays inside it" is N toggles,
  and every new layer starts unclipped — the friction the owner named.

Krita's confusion is the other half of the request: inherit alpha outside a
group appears to do nothing (it inherits the opaque background), and nothing
shows what a layer is carved by.

**Recommendation, taken:** the folder is the unit, the union is the shape,
the old clip learns the folder boundary, and the docker says what carves what.

What the alternatives cost:

- *Per layer, Krita-style* — most flexible, and exactly the N-clicks friction
  the request is about.
- *Both* — two records that can disagree about one layer.
- *Only the base layer* — already built, and cannot put one shading layer
  over several flats.
- *Leave the folder boundary alone* — no change to existing renders, and the
  new mode would obey a different rule from the old clip in the same docker.
- *Dim outside while painting* — not taken. The carved result is already what
  the canvas shows, so the dim would mostly duplicate it.

The part that was never a preference: whichever gesture won, the shape has to
be described through `LayerShapes`, because every compositor (canvas publish,
exports, thumbnails, MCP render, fill sampling, smudge backdrop) already asks
it. A second route to the same carve would let two of them disagree.
