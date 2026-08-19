# Q129 · Does an effect baked to a symbol keep the group that made it? — **answered 2026-08-19: it keeps the group record**

Raised by: the owner, asking whether effects could be combined and layered the
way Unity's particle systems are, and observing that presets "could also tie in
with the symbol system — symbols carry and insert animation frames as well".

What it blocks: the shape of `SimGroup`, and whether a baked effect is a
one-way flatten or a live thing.

## Why the symbol route is the answer at all

The observation is right, and the record was readier for it than it looked:

- `Symbol.Frames` is already `List<Frame>`, so a symbol **is** an animation.
- `SymbolKind.Fx` is already in the enum, with nothing producing it.
- `SymbolPlacement` already carries `FrameOffset`, `ScaleX`/`ScaleY` (so flip),
  `Angle`, `Opacity` and `SwatchOverrideId`.

So a baked group placed as an `Fx` symbol gets instancing, per-placement time
offset, flip, rotate, fade and recolour — plus the browser and the project
libraries — with no new machinery. Three explosions across a shot, offset four
frames each and one mirrored, becomes placement work rather than simulation
work. That is the cheapest available answer to "combine and layer", and it
falls out of what already exists.

**A symbol and a preset are still two different reusable things**, and
conflating them is the trap this question exists to avoid:

| | stores | costs | every use is |
| --- | --- | --- | --- |
| symbol | the baked frames | free to place | *identical* |
| preset | the parameters | a re-solve | *different* — longer, bigger, blowing left |

Both are wanted. Symbols answer "that explosion again, three times"; presets
answer "an explosion like that one, but for this shot".

## The decision

**Recommendation, and what the owner chose: the symbol carries the `SimGroup`
alongside its frames**, so *edit this effect* reopens the effects window,
retunes and re-bakes — and every placement updates, which is Pillar 3's promise
applied to an effect rather than to a sword.

What it costs, stated rather than discovered:

- The group record is serialized **twice** — in the document that authored it
  and in the symbol. They can drift, and the symbol's copy is the one a
  placement renders from.
- Re-baking can change the **frame count** under existing placements.
  `SymbolPlacement.FrameIndexAt` wraps, so nothing crashes; what changes is
  *when* each placement is showing what, which an artist will read as their
  timing moving on its own. It needs the same treatment `Symbol.Version` and
  `SeenVersion` already give a changed drawing: reported, not silently applied.

## What was turned down

- **One-way bake, like Break link.** Simplest record and honest — a symbol is
  drawings, full stop. Refused because retuning would mean going back to the
  original group in whatever document made it, and if that document is gone the
  effect is not editable at all, only replaceable.
- **The symbol references a project preset by id.** No duplication, and one
  tuning updates every symbol built from it. Refused for two reasons: it needs
  the preset system built first, which delays the symbol route behind work that
  had deliberately been put last; and it makes a symbol non-self-contained,
  which is the thing `ProjectIo.Flatten` exists to undo at the export boundary.

## What this does not settle

Whether a **group** is the only thing that can become a symbol, or a single
element can too. The group is the interesting case and the one asked for; a
one-element group is not a burden, so nothing is lost by requiring it at first.
