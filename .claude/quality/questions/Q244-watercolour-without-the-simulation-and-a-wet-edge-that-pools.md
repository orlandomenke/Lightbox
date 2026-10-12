# Q244 · Watercolour without the simulation, and a wet edge that pools — **answered 2026-10-11**

**Answered: the wash is "row 2", and the wet edge that makes it is the wet edge for every brush.** Both as recommended.

Raised by phase 3 of the paint path (`docs/DESIGN-paint-path.md`), the first medium to be rebuilt as an under-the-pen recipe after Q240 removed the fluid simulation from painting.

What it blocks: every other medium's recipe leans on the same parts, and the first sheet — each simulated brush beside a recipe made only from settings that already existed — showed the watercolour had no character without the simulation: pale, soft, and no visible edge. The wet edge as it stood only darkened the rim's colour, on the argument that the paint moved rather than multiplied, and on a pale wash that cannot be seen.

## What was asked

A second sheet: today's simulated Watercolor above five washes with no simulation, each on a wet edge that **gathers** paint — thinner in the middle, denser and slightly darker at the rim — on the scanned papers.

**1. Which wash?** **Answered: row 2** — a round tip, hardness 0.8, flow 0.35, wet edge 0.7, Fibre paper at depth 0.35. The alternatives were a harder edge and stronger rim on Plain paper (the rim reads as drawn on), and the same wash through the irregular wet-edge tip (more organic, dearer per pointer event, and a ragged outline).

**2. Does the gathering wet edge replace the old one, or sit beside it?** **Answered: replace it.** One wet edge, and one that can be seen. The alternative, a second setting, would have left drawings already made untouched at the price of another option to explain and maintain.

The owner's read of the first sheet, the same day: *"Oil does need some additional work. It looks like a stamp or that it only skims the paper surface instead of flowing into the crevices with pressure. All others look pretty good."* Oil is its own round.

## What it costs

- **Strokes already drawn with a wet edge re-render with the gathered rim**, including Watercolor (flat) and Gouache (flat). Two fingerprints moved and are re-recorded.
- **Strokes already drawn with the simulated Watercolor are untouched for now** — their settings are in each stroke and still name the simulation. They change when the simulation is deleted (phase 4), as Q240 already accepted.
- **The shipped Watercolor is round and unjittered.** A tip that varies in size puts faint bumps on the edge the rim is measured from, and the rim breaks into dashes on them. An irregular wash edge will want the rim measured from somewhere steadier, and is not built.
- **A stroke that mixes colour was still rendered again at pen-up** when this was answered: the recipe keeps colour mixing, and a mixing brush reads the canvas as it goes. Closed the same day - a mixing stroke is replayed in the document's own coordinates, as it is drawn, and is kept (`PenUpChangesNothingTests.AMixingStrokeAcrossPaintIsKept`).
