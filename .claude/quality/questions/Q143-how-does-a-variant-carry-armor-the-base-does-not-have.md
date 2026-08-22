# Q143 · How does a variant carry armor the base character does not have? — **answered 2026-08-21: a symbol riding an anchor, with a document override as the escape hatch**

Asked while completing the variant UI (the picker and the override gesture —
the model existed and nothing in the app could reach it). A palette swap covers
recolours; `SubjectVariant.Overrides` covers a wholesale different drawing. The
owner's actual want sits between the two: *the variant gains armor, and every
animation of the character should gain it* — drawn once, following the motion,
without redrawing two hundred frames.

| | What it costs |
| --- | --- |
| **A symbol riding an anchor** (recommended, **chosen as the default**) | New machinery: an attachment record on the variant `{anchorId, symbolId, offset, scale, follow-direction}`, compositor support for drawing a symbol at an anchor's animated position, and per-document/per-frame override storage for nudges and hides. |
| **Override documents per animation** | No new machinery — but the armor is redrawn into every frame of every animation, which is exactly the duplication the request exists to avoid. |
| **Both, layered** (**chosen**) | The most machinery, and the honest amount: the attachment covers the ninety-percent case and the full document override stays for the drawing the attachment cannot carry — extreme foreshortening, armor that deforms with the pose. |

The pieces already exist separately, which is what makes this an assembly
rather than an invention:

- **Anchors** (`Scene.Anchors` + `Frame.Anchors`) supply the animated position.
  The animator places them in the *base* animation documents — the rig overlay
  (Ctrl+K) is already the tool — and every variant reads the same rig. Q144
  gives them the direction the attachment needs.
- **Symbols** (`Symbol`, `SymbolPlacement`, `SymbolRasterizer`) supply the
  drawn-once add-on with its own pivot: "edit the sword once, every animation
  holding it updates" is already Pillar 3's promise, and the armor piece is the
  same promise attached to a moving point.
- **The variant** is the owner: attachments live on `SubjectVariant`, next to
  `Overrides`, because "who wears the armor" is exactly the question a variant
  answers. Nullable/absent-until-used, per the optional-means-absent rule.

## Always overridable, at every level

The owner's phrase was *"always overwriteable"*, and the layering is the
answer, most specific wins:

1. The attachment's default transform (offset/scale/rotation relative to the
   anchor) applies everywhere.
2. A per-document adjustment overrides it for one animation.
3. A per-frame adjustment overrides that for one drawing — the same shape as
   `Frame.Anchors` overriding nothing-in-particular, stored beside it.
4. A full document override (`ProjectIo.OverrideDocument`) replaces the whole
   drawing and the attachment machinery bows out for that animation.

## What the build settled (2026-08-22, `feat/variant-attachments`)

- **The anchor is named by name, not id.** The sketch above said `anchorId`
  and the model refused it: anchor ids are per document — the knight's Walk
  and Run each declare their own "shoulder" — so an id could only ever reach
  one animation. Names are already the cross-document contract (the sidecar
  keys anchors by name because "leftHand" is what an engine script looks
  for), and the attachment binds by the same word for the same reason.
- **Levels 2 and 3 of the layering collapsed onto data that already
  exists.** The per-document and per-frame overrides needed no new store:
  the anchor *is* the override channel. Nudge it on one drawing and the
  armor moves there; aim it (Q144) and the armor turns; *Clear here* and the
  armor is absent on that drawing. What remains as record is only the
  attachment's own default transform — offset in the anchor's frame, scale,
  extra angle, follow-the-aim — and level 4 stays `OverrideDocument`.
- **Symbol cycles came free.** A placement already advances with the
  timeline index, so an attached flame flickers through the walk without a
  field being added.
- **Ephemeral placements, never stored.** Resolution produces fresh
  `SymbolPlacement`s at render time; nothing touches a frame, so invariant 1
  holds without a flatten step — and the export composes the same overlay
  the canvas shows, which is the palette's existing contract extended to
  pixels the variant wears.

## What this still does not answer

- **Draw order.** The overlay renders above the whole layer stack. Armor
  *behind* the near arm needs an attachment to name the layer it sits under,
  and which layer that is on two hundred frames of differing stacks is a
  real design question — left open rather than guessed.
- **Whose armor dresses a multi-folder export.** The overlay resolver is
  ambient per active document, like the palettes; a folder export that spans
  other subjects' folders composes with the active document's attachment
  list. Same-folder exports — the ordinary case — are correct.
