# Q258 · New layers start with onion skin off, the first layer included — **answered 2026-10-13**

**Answered: every new layer starts with its onion off, the first layer of a new document included** — against the recommendation, which was to leave the first layer on. Taken knowing the cost: in a fresh document, turning onion skin on shows nothing until a layer's own ◉ is turned on too.

Raised by the owner's decision of 2026-10-08 that a new layer's onion starts off, so a character's colour, shading and effects layers stop all ghosting at once. That decision lay uncommitted in a worktree until 2026-10-13; finishing it raised the one question it had not answered — what the very first layer does — because ghosts need both the global switch and the layer's own.

What it blocks: the default of `Layer.OnionEnabled`.

## The options as put to the owner

- **(a) First layer on, later layers off** — recommended. Onion works out of the box; the layers added for colour, shading and effects start off. Cost: two defaults where there was one.
- **(b) The global switch turns on the active layer when no layer is on.** Covers old files with every layer off too. Cost: a switch that changes something other than itself.
- **(c) All off, as written** — taken. Simplest, one rule. Cost: a fresh document's onion skin looks broken until the layer's ◉ is found; the manual says so.

## What does not change

A layer read from a file keeps what it was: files have written the key since it existed, and a layer saved before it existed loads on, as it always did (`Layer.OnDeserializing`). Pinned by `RoundTripTests.OnionEnabled_RoundTrips_AndDefaultsTrueForOlderDocs` and `ANewLayerStartsWithItsOnionOff_AndOnRoundTrips`, and `CanvasOverlayTests.AnAddedLayerStartsWithItsOnionOff`.
