# Q245 · Reworking paint: what is wet, and what re-wets it — **answered 2026-10-11**

**Answered, against all three options offered: there is no wet or dry paint. Reworking is a brush, or a mode of one.** The owner: *"Perhaps a separate brush specifically for rewetting. Or a specific special mode for the brush."*

Raised by phase 3 of the paint path, from the owner's brief for the media after the first recipe sheets (2026-10-11), with reference images of thick paint and of watercolour on paper: *"All medium paints should have pressure sensitive behavior of kinds. We do not have to simulate it but like Clip Studio Paint it should have dynamic effects […] where it dynamically shows paper texture. How you can rewet and activate gouache and watercolor, or smear oil and acrylic until dry to create more traditional effects that digital lack. Again. No full simulation is needed."*

What it blocks: how the media recipes treat paint that is already on the layer. "Until dry" needs something to say what is dry, and it cannot be elapsed time — a drawing has to render the same when it is reopened or replayed, and a clock would change it (invariant 2).

## What was offered

- **(a) Always workable; the brush decides** — recommended. Paint can always be picked up or smeared, how much is a property of the brush in hand. No dry paint to glaze over on the same layer.
- **(b) Workable until you dry the layer** — a Dry layer command and a per-layer state saved in the document. Closest to real paint; costs a command, a state and a way to show it.
- **(c) Only recent strokes are wet** — no clock, but hard to predict and changed by undo.

## What was answered, and what it means

A fourth, which is (a) made narrower: an ordinary paint brush lays paint and mixes with what is under it only as much as its own recipe says; **reworking what is already there is done by picking up a brush made for it** — water for watercolour and gouache, a dry blender or knife for oil — **or by switching the brush in hand into a rework mode** in which it lays no paint and only moves, softens or lifts what it finds.

So:

- **No wet/dry state anywhere.** Nothing is saved per layer or per stroke about dryness, and nothing depends on stroke order or on time.
- **A rework stroke is a stroke in the record** like any other, replayed in order (invariants 1 and 2). Most of it is what colour mixing (Q232), smudge and blur already do; what is new is a brush that carries no paint of its own, and lifting — taking paint away with water.
- **It costs** the glaze: paint on the same layer is never protected from a rework brush. A new layer is the way to paint over something without disturbing it.

Whether the mode is a toggle on every paint brush or only a set of shipped brushes is not decided here; the shipped brushes come first, and the toggle is the same settings behind a switch.
