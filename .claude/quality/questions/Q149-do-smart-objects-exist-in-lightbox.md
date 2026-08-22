# Q149 · Do smart objects (and text layers) exist in Lightbox? — **answered 2026-08-22: both roadmapped, neither built yet; smart objects reframed as linked instances**

The other half of Q146: two of the five requested features are not on the
roadmap at all, and one of them may not even be a feature here.

**Smart objects.** In Photoshop they exist to make raster edits non-destructive
and re-editable — which the stroke record already gives every layer by
construction (invariant 1: pixels are derived, so nothing is ever baked). What
Photoshop's smart objects provide that Lightbox genuinely lacks is **reusable
instances**: one drawing placed many times, edited once, updated everywhere —
which for an animation tool is the *symbol/library* concept, and Q144 already
touches symbol placements from the rigging side.

| | What it costs |
| --- | --- |
| **Roadmap both, build later** (recommended, **chosen**) | Text layers become a designed roadmap item on the vector side (fonts, shaping, editing — real scope, needs its own design pass). Smart objects become a roadmap item under the honest name *linked instances*, with the Photoshop motivation recorded as already-covered so nobody re-imports the wrong half of the concept. |
| Drop smart objects entirely | Saves a roadmap line, but loses the half that is real: instancing is a legitimate want (props, repeated set pieces, a sprite reused across scenes). |
| Design both now | Two design documents in a branch whose objective is masks — the "and" that makes it two branches. |

Text layers note for whoever picks it up: `LayerKind.Vector` exists, the SVG
save item already establishes that the vector side must not be faked, and text
wants to live there — an editable text object that rasterizes through the
ordinary deterministic path, not a raster stamp of a font.

**What recording it turned up (2026-08-22):** the linked-instances half needed
no new roadmap item, because it already has a pillar. One drawing placed many
times, edited once, updated everywhere is exactly Pillar 3's *symbols* —
shipped for the flat case, with nesting deliberately open — so a second item
would have been the same feature under a Photoshop name, which is the wish-list
shape the checkbox rules exist to prevent. Text got the new item (`[?] Text`,
under *Guides and shapes*); smart objects resolved to "already built, plus
invariant 1".
