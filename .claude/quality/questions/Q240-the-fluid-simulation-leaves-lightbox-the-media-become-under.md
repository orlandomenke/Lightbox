# Q240 · The fluid simulation leaves Lightbox: the media become under-the-pen recipes — **answered 2026-10-10**

**Answered: remove it**, as recommended. Watercolour, gouache, oil and ink wash are rebuilt as recipes that are applied under the pen — colour mixing with the ground (Q232), a textured tip, paper tooth and grain as ceilings, hairs as a ceiling along the stroke, a wet-edge rim and the light on thick paint as local stencils, paint load that runs out — and `FluidLattice` and `MediumSimulator`'s solve leave the codebase, not only the paint path. Taken knowing two costs: strokes already saved with a simulated medium re-render with the new look, and what only a solver does is given up — pigment that travels after it is laid, pooling that depends on the whole wet region, a bloom where two wet strokes meet.

Two riders answered in the same prompt. **B437** (the pencil's tooth as a ceiling) **rides the `feat/brush/paint-path` branch** rather than shipping alone — against a recommendation to ship it first, so `main` keeps the pencil's large-brush stall until the branch lands. And the branch is judged as a whole against a **frozen baseline** (`main` at `70d8a644`, 2026-10-10), the owner's own proposal: *"test the complete feature (change) vs the baseline, instead of only testing iterative changes as that might prove some performance gains, but not the overall."*

Raised by the paint-path work, 2026-10-10 (`docs/DESIGN-paint-path.md`), after the owner set the rule it answers to: *"Painting is the primary function of this app. […] No texture pop-ins, no stalls or delays, no slow downs. […] What you draw is what you see. I do not know of any drawing program that changes the stroke after pen lift. […] We do not have to keep the simulation engine if that proves too slow. But like CSP I do want convincing effect. But every decision is possible."*

What it blocks: every decision about how a medium brush draws live. The lattice solves the whole stroke on every live pass and again at pen-up, and its output is not a function of the dabs laid so far that later dabs leave alone — flow moves pigment already on screen — so it cannot satisfy "the mark under the pen is the mark" at any speed. Measured on `main` (perf lab, 300 px watercolour): a live pass every 78–93 ms, the finish landing 24–37 points behind the pen.

## The options as put to the owner

- **(a) Remove it; the media become under-the-pen recipes** — recommended and taken. One engine, one route, nothing deferred.
- **(b) Remove it from painting, keep it only to redraw old strokes.** Old documents keep their look. Cost: two engines to maintain and test, and old strokes still stall at open and undo.
- **(c) Remove it from painting, bring it back later as a command** — an explicit "let it flow" an artist runs on a finished stroke or layer, as an edit with its own undo step. Cost: the lattice stays in the tree unused until that command is designed. Not taken, and not ruled out for ever: if it returns it returns as an edit, never as a brush.

## What this retires

The roadmap's *Physical media simulation* item, B24, B25, B35, B431 and B429 (the lattice's capillary term, twice repaired and once filed as global), and Q168's two live routes. `docs/DESIGN-fluid-media.md` becomes history; `docs/DESIGN-paint-path.md` is the direction.
