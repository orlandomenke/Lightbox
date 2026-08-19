# Effects — fire, smoke and water

An **effect** in Lightbox is not a filter over your pixels. It is a small fluid
simulation whose result is **drawn**: bands of colour with outlines around them,
in your line style, written into the document as ordinary strokes. You can
select them, recolour them, transform them, erase into them and export them, and
nothing downstream knows they came from a simulation.

That is the whole design. A drawn effect can sit inside a hand-drawn scene
without looking pasted on, and it survives everything a drawing survives.

A document that never adds an effect writes nothing about them and shows no
effects controls.

## Opening the window

**Window ▸ Effects…**, or `Ctrl+Shift+E`. It is a window rather than a panel
because tuning an effect is a look-and-adjust loop, and the preview has to be
big enough to judge a silhouette in.

The window has three columns:

- **Left** — the document's elements, and the emitters inside the selected one.
- **Middle** — everything about the selected element.
- **Right** — the preview, a frame scrubber, and the two buttons that cost
  something.

## The one thing to understand: Simulate and Bake

They are separate buttons because they cost different things, by about a factor
of forty.

| | |
| --- | --- |
| **Simulate** | Runs the fluid again. Seconds. Needed after anything under *The fluid*, the emitters, or placement and timing. |
| **Bake** | Draws the strokes into the document, replacing whatever this element drew before. Fast, and one undo step. |

Everything under **Line treatment** and **Bands** re-draws from the simulation
already in hand, so it previews *as you move the slider* and costs nothing. That
is where the art direction happens, and it is meant to be played with freely.

When a change does need a new simulation, the preview says **"the picture is out
of date — press Simulate"** rather than quietly running for two seconds. Nothing
is lost by leaving it stale while you change five more things.

**Clear** takes an element's drawings back out and leaves the element itself, so
you can keep tuning without a stack of undos.

## Making an element

**Fire** and **Smoke** on the left both make a new element with one emitter
already in it, burning or smoking on the floor of its own grid — an element with
no emitter simulates still air and draws nothing, which is not a useful place to
start from.

The difference between the two buttons is only what the bands read from and what
colours they start with. Everything else is the same machinery, and you can turn
one into the other with the **Bands read** control.

## Placement and timing

- **First frame / Frames** — where the element sits on the timeline.
- **Expose on** — bake one drawing every N frames and hold it. **Two is
  animating on 2s**, and it does more than halve the work: tracing every frame
  makes the outline crawl, and holding kills it.
- **Pre-roll** — frames simulated before the first drawn one, so the element
  opens on an established plume rather than on still air. It does *not* make a
  cycle seamless; that is a different problem and not solved yet.
- **Grid width / height / Cell size** — the simulation's own grid, in cells, and
  how many document pixels a cell is. **The grid is deliberately coarser than
  your document**: cell size is the main lever on cost, and a flame does not get
  better by being simulated at drawing resolution.
- **Origin X / Y** — where cell (0,0) sits in the document.
- **Substeps** — solver steps per frame. The other main lever on cost. More is
  more accurate, not more detailed.

## The fluid

These are what the simulation does, and every one of them means a re-simulate.

- **Buoyancy** — how hard heat lifts. A flame with none of it stalls.
- **Weight** — how hard the fluid's own mass pulls down. Smoke has some; fire
  has almost none.
- **Vorticity** — puts back the curl a coarse grid loses. **This is what makes
  the tongues and the curls**, and it is usually the first thing to reach for
  when an effect looks limp.
- **Drag** — air resistance. Without it a closed element circulates forever and
  reads as a lava lamp rather than as fire.
- **Turbulence**, **scale** and **drift** — a standing eddy field. Scale is the
  size of the eddies in cells; set it *larger than the element* and it stops
  being turbulence and becomes a steady wind, which is a mistake that looks like
  the fluid being broken.
- **Dissipation** — how fast the stuff thins out. Smoke wants a little, steam a
  lot.
- **Cooling** — how fast heat leaves. **This is what gives a flame its length.**
- **Wind X / Y** — ambient flow across the element, in cells per step.

### Wind, and the number that surprises everybody

Wind is in the same units as the flow it acts on, and a plume rises at roughly
**0.15 cells per step**. So:

- **0.15** bends a flame by about half a right angle.
- **0.5** lays it flat and horizontal.

The useful range for a figure in motion is well under a tenth of what the
field's own top speed would suggest. If wind seems to do nothing, you are
probably two decimal places out.

**A character running right is wind blowing left.** A run cycle runs on the
spot, so nothing about her motion can be derived automatically — you are
choosing the reference frame, and this is the one control artists get backwards.

## Emitters

An emitter is where the stuff comes from. An element can have several.

- **Shape** — a disc, a line between (X,Y) and (X2,Y2), or a whole layer's ink.
- **Density** — how much stuff it puts in per step.
- **Heat** — how hot. Buoyancy reads this, so **heat is what makes a flame rise
  rather than sit**. A smoke emitter usually has none.
- **Velocity X / Y** — a push given to the fluid at the emitter, for a jet or a
  vent.
- **Travel X / Y** — where the emitter's origin goes. This is how an effect is
  carried along with something: **the emitter moves, and the fluid it has
  already laid down does not**, which is what makes a trail behave like a trail
  instead of like a rigidly translated picture.

### Emitting from a drawing

**Emit from** points an emitter at a layer, and then it emits from wherever that
layer has ink. This is how flames follow a drawn costume: paint the hem of a
cloak on its own layer, point an emitter at it, and the fire is on the hem.

The mask **is** the emission — it is not intersected with anything at bake time,
and only the emitter's own origin (and its Travel keys) moves it.

> **Known limit.** A mask that emits over a wide *area* refuels itself every
> frame, so heat never leaves it and no tongue detaches. That reads as a burning
> *edge* rather than as flames. The fix is emission that flickers along the mask
> and is not built yet; in the meantime, **paint a broken mask** — sparse
> emission in space already works exactly as you would want.

## Bands and colour

The drawing is a set of nested contours through the field — the **bands** — each
filled, with outlines around them.

- **Bands read** — *Temperature* for fire, where the heat ramp is the drawing,
  or *Density* for smoke.
- **Band low / high** — where the bands sit in the field's range, as a fraction
  of the highest value the element reaches anywhere. **They belong low.** A
  plume's field is steeply peaked: only about 4% of cells hold more than a
  hundredth of the peak, so bands spread across the whole range all land inside
  the brightest core and draw scraps. The defaults put the outermost band around
  the visible edge, which is where a silhouette belongs.
- **Colours** — one hex per band, outermost first, separated by spaces.
- **Outline** — the colour the outlines are drawn in.

The range is measured over the **whole element**, never per frame. A range that
followed each frame's own peak would rescale the bands every frame, and a band
that moves because its scale moved is flicker.

## Line treatment — the art style

This is the part that makes an effect look like *your* drawing rather than like
a simulation. All of it is cheap, and all of it previews live.

- **Bands** — how many.
- **Outlined** — *None* (fills only), *Silhouette* (outline the outer edge only)
  or *Every* band.
- **Band spacing** — *Even*, or *CoreBiased* to crowd them towards the hot
  middle.
- **Line weight** — the base thickness, in stroke widths.
- **Offset** — push the outline in or out of the fill's edge.
- **Simplify** — how hard the contour is reduced. Higher is fewer, longer,
  more deliberate lines.
- **Smoothing** — how much the corners are rounded off.
- **Break length / gap** — a broken, sketched line instead of a continuous one.
- **Light angle** — where the light is, for treatments that thicken the line on
  the shadow side.

A treatment can be shared between elements. When an element states a field of
its own, that row shows a **↺** button: the value is this element's override,
and one press puts it back to what the shared treatment says. **Revert all**
does the lot.

### The pen

**Take brush** gives the element the brush the toolbar is currently holding, and
its size becomes the width every treatment distance is measured in. It is
copied on the press, not read when you bake — so a re-bake always draws the same
line, and changing your brush afterwards does not silently restyle every effect
in the document. **Default pen** takes it back off.

## Baking, editing, and re-baking

**Bake** writes the drawings into a layer of the element's own, as one undoable
step. They are ordinary strokes from that point on.

Re-baking replaces what the element drew last time and leaves everything else
alone. **A stroke you have edited by hand stops belonging to the element**, so a
re-bake will not take your work back out — but it also will not update it.

## What is planned

- **Emission flicker**, so an area mask reads as flames rather than as a burning
  edge.
- **Obstacles** — pointing an element at a layer whose ink the fluid cannot pass
  through, so smoke goes around a figure rather than through her. There is no
  control for it yet, on purpose: the solver has walls only at the edge of its
  grid, and a picker that stored a layer and changed nothing would just look
  broken.
- **Attaching an element to a drawing's anchor**, so it follows a rigged figure
  without keying Travel by hand.
- **Presets**, so a flame is tuned once and reused.
- **Water and goo.** The solver is the same; a free surface and a metaball
  source are not built.
- **Style inference** — a reference drawing in, a line treatment out.
