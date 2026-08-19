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

**Effects ▸ Fluid effects…**, or `Ctrl+Shift+E`. It is a window rather than a panel
because tuning an effect is a look-and-adjust loop, and the preview has to be
big enough to judge a silhouette in.

The window has three columns:

- **Left** — the document's elements, and the emitters inside the selected one.
- **Middle** — everything about the selected element.
- **Right** — the preview, a frame scrubber, and the two buttons that cost
  something.

## Effects: several elements that are one thing

A real explosion is not one simulation. It is a flash, a fireball, a roll of
smoke and some sparks, each with its own timing, its own grid and its own line
treatment — and each baking to its own layer, so they composite, blend and
z-order like any drawing.

An **effect** is the name for that set. Select an element and press **Group** to
start one; select another and press **Add** to put it in. **Out** takes an
element back out and leaves it exactly where it is.

Once elements are in an effect you get three numbers that act on all of them:

- **Place X / Y** — where the effect's left and top edges sit. Moving it moves
  every member by the same amount.
- **Starts on** — the frame the effect begins. Shifting it shifts every member
  equally, so **the smoke stays four frames behind the flash**. That spacing is
  the effect's timing, and lining members up on a common frame would destroy it.

There is no group transform hiding anywhere — those numbers read the members and
write the difference back. An element's origin is always honestly its origin,
and **Ungroup** is lossless because there is nothing to un-apply.

- **Copy** duplicates the whole effect, elements and all, so the copy can be
  retuned without touching the original.
- **Delete** removes the effect and everything in it, drawings included.
- **Bake the whole effect** bakes every member and puts their layers in one
  folder named after the effect.

Because each element keeps its own grid, layering is cheaper here than in a
system with one shared simulation: a small hot fireball can run at four document
pixels per cell beside a slow smoke at ten, and each pays only for its own
resolution.

> **Elements do not interact.** Each runs its own solver on its own grid, so a
> fireball does not push its own smoke. If you want the blast to shove the
> smoke, give the smoke element matching wind or burst — that is a deliberate
> trade, and it is what buys the per-element grid sizes above.

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
start from. It arrives torch-sized, about 192 × 176 document pixels, and already
alight rather than starting from cold. Anything wanting a bonfire raises the
grid, which is one slider.

The difference between them is what the bands read from, what colours they start
with, and two things worth knowing about because they are the difference between
smoke that works and smoke that does not:

- **A smoke emitter is warm.** Smoke rises because it is hot, and the solver
  means that literally — buoyancy reads heat while weight reads density, so an
  emitter at zero heat gets pushed *down* by its own mass and spreads on the
  floor as a pancake. Smoke's emitter starts at a third of fire's.
- **Smoke arrives lit and fire does not.** A flame is the light source, so
  nothing lights it: its bands stay concentric and the ramp from dull red to
  white *is* the drawing. Smoke is lit from outside, and unshaded it reads as an
  onion — three rings round one centre, which is a cross-section rather than a
  volume. See **Shading** below.

Everything else is the same machinery, and you can turn one into the other with
the **Bands read** control.

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
- **Burst** — push outward from the emitter's own centre rather than in one
  direction. **This is the difference between an explosion and a plume**: the
  velocities above move the whole stamp one way, as a lump, while a burst pushes
  every part of it a different way, so the front expands as a ring. The two
  combine — a burst that also travels is a muzzle flash.
- **Emit from / Emit for** — when the emitter feeds, and for how long. Normally
  it runs the whole element and these write nothing. **A blast is one or two
  frames**: an emitter that keeps feeding refuels its own fireball every frame,
  so it never cools into smoke and never disperses.
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

### Scatter — flames on a surface, rather than a surface on fire

An emitter feeds **every** cell it covers, every frame. Over a wide area that
means nothing can ever detach: whatever rises is replaced from below the same
frame, so a painted hem reads as one continuous burning edge.

**Scatter** breaks it into separate flames standing over the same area.

- **Coverage** — what fraction of the surface burns. A fraction rather than a
  count, so painting a longer hem gives proportionally more flames with nothing
  else to change.
- **Spacing** — how far apart they stand, in cells, *and how big they are*: each
  flame is half a spacing across, so they just touch at full coverage. One
  number, because that is how you would describe a row of flames.
- **Size varies** — how much they differ in width at the base.
- **Heat varies** — how much they differ in **fierceness**, which for fire means
  which colour bands each one reaches: some running up into the pale core,
  others staying dull red.
- **Lean** — a sideways push that differs per flame, so they stop swaying in
  step with each other.

> **The flames come out at different heights on their own.** You do not need to
> ask for it, and turning *heat varies* up will not give you more of it. What
> makes it is the fluid: a flame with neighbours either side is fed by their
> rising column and runs tall, one on the end of a run is not and stays short.
> Measured with every variation at zero, a scattered hem burns at heights of 10,
> 16, 24, 30, 38, 42 and 44 cells.

Scatter belongs to the emitter, not to fire and not to masks — a plain disc
scatters into a handful of small flames inside its own outline, and smoke or
steam off a surface scatters the same way.

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
- **Light angle** — where the light is, in degrees clockwise from straight up.
  One light serves both halves of lighting: the shading below, and the line
  weight if a driver reads it.
- **Shading** — slide the inner bands toward the light, in stroke widths, so a
  volume reads as lit instead of as an onion of concentric rings. The
  silhouette never moves — it is the silhouette — and the bands inside it move
  further the deeper they are, which puts the highlight on the lit side and
  crowds the rest into a crescent opposite. **A flame wants none of this**;
  smoke, steam and dust want most of it.

  It is clamped so a highlight can never leave the silhouette, so past a point
  the slider stops doing anything. That is the shape telling you it has run out
  of room, not the control breaking.

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

## Making an explosion

Everything it needs is above; what is not obvious is which handful to reach for.

1. **Emit for 1 or 2 frames.** Everything else follows from the blast being over
   before the smoke starts.
2. **Burst around 0.5–1.** Enough that the front rolls outward rather than
   drifting.
3. **Density high, heat modest.** Heat is what makes it rise, and a blast that
   rises too fast reads as a plume with a bang at the bottom. Add heat back once
   the shape is right.
4. **Vorticity up.** This is what turns an expanding disc into something with
   rolls and tongues in it.
5. **Band low down.** The band range is measured against the highest value the
   element reaches *anywhere*, and for a blast that is frame one — so everything
   after it is being compared to a peak it will never see again, and the effect
   thins out of the bands sooner than you expect. Lowering **Band low** brings
   the dispersing smoke back.

Point 5 is the one that surprises people, and it is the cost of a deliberate
choice: a band range that followed each frame's own peak would rescale the bands
every frame, and a band that moves because its scale moved is flicker. Steady
plumes get the better end of that trade and blasts get the worse one.

## Keeping an effect — the library

**Keep** puts the selected effect in your library under its name; the box beside
**Effects** lists what is on the shelf, and **Use** makes the chosen one again
here. It arrives at the current effect's place and frame, so using one beside
something you are already working on does not drop it at the corner of the
canvas.

**A preset stores the parameters, not the drawings.** Using one re-simulates —
which costs a moment, and in exchange the new one is a real effect you can then
retune: forty frames instead of twenty-four, twice the size, blowing left. It
draws exactly what the original drew, moved to wherever you put it.

Two consequences worth knowing:

- **The library is somewhere to choose from, not something your document depends
  on.** Using a preset copies it in, so the document keeps working with the
  library gone, and editing the preset afterwards does not reach back into
  effects already made from it.
- **Layer references travel by name.** An effect that emits from a painted layer
  cannot carry that layer's identity into another document — nothing there would
  match it, and an emitter pointed at a layer that is not present emits nothing
  at all. So the preset remembers the layer's *name* and reconnects to a layer
  called the same thing. If there is none, the reference is dropped and you are
  told which name was missing, rather than finding out when you bake.

*(Planned: a project-scoped shelf, so a show can carry its own fire alongside
each artist's own library.)*

## Baking, editing, and re-baking

**Bake** writes the drawings into a layer of the element's own, as one undoable
step. They are ordinary strokes from that point on.

> **One thing to know about undo here.** Moving a slider in this window is not
> its own undo step — a hundred of them while tuning would bury the history you
> actually want. The document is still marked as changed, so nothing is lost
> silently. But an undo taken *after* tuning and *before* baking puts the
> parameters back along with everything else. Bake when you like what you see.

Re-baking replaces what the element drew last time and leaves everything else
alone. **A stroke you have edited by hand stops belonging to the element**, so a
re-bake will not take your work back out — but it also will not update it.

## What is planned

- **Emission flicker**, for shimmer. It used to be the planned answer to "a
  painted area reads as a burning edge" — **Scatter** is that answer now, and a
  better one, because its gaps are in the same place every frame so what rises
  off a flame actually leaves.
- **Obstacles** — pointing an element at a layer whose ink the fluid cannot pass
  through, so smoke goes around a figure rather than through her. There is no
  control for it yet, on purpose: the solver has walls only at the edge of its
  grid, and a picker that stored a layer and changed nothing would just look
  broken.
- **Attaching an element to a drawing's anchor**, so it follows a rigged figure
  without keying Travel by hand.
- **Saving an effect as a symbol**, so a baked explosion can be dropped in
  three times at different frames, one of them mirrored — symbols already carry
  animation frames and per-placement time offset, so most of this exists.
- **A project-scoped effects shelf**, so a show carries its own fire beside each
  artist's own library.
- **Water and goo.** The solver is the same; a free surface and a metaball
  source are not built.
- **Style inference** — a reference drawing in, a line treatment out.
