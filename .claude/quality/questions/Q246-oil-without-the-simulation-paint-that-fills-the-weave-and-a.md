# Q246 · Oil without the simulation: paint that fills the weave, and a tip with hairs — **answered 2026-10-11**

**Answered: oil is "row 3" for how it meets the canvas and "row 4" for its bristles; and bristles are part of the brush tip, not of a medium.**

Raised by phase 3 of the paint path, after the owner's verdict on the first oil recipe (Q244's sheet): *"Oil does need some additional work. It looks like a stamp or that it only skims the paper surface instead of flowing into the crevices with pressure."* That recipe put the paint on linen through the pencil's tooth, which never quite fills — right for graphite, and on a weave it held the paint to the threads at every pressure, a halftone.

What it blocks: every medium with body. Gouache and acrylic-like paint meet a surface the same way.

## What was asked

**1. How paint meets the canvas.** A sheet of six oils in which pressure pushes paint into the surface until the mark is solid, and the surface shows only at a light touch or as the brush runs dry — a new brush setting, *Fills at*. **Answered: row 3** — Linen at grain 6, fills at a press of 0.35, paint load 0.55 — so the weave appears through the last third of a long stroke. (The prompt was first answered "row 4", the same with more paint on the brush, and corrected at once: *"NOOO! I meant 3."* Nothing had been built on the slip.)

**2. Bristles.** A second sheet on those settings with the brush's hairs at three strengths. **Answered: row 4, hairs 0.8.** A loaded brush pressed home is solid; as it lifts or empties, the thin hairs stop reaching the canvas and the mark opens into streaks.

**3. Where bristles belong** — not prompted; the owner said it while the sheet was being made: *"Bristle in part would affect all paints, right? […] I would like to have the pen's bristles and hairs to be part of the pen tip system, where we can change and customize brushes with tips as well. We do have tip shape, but I do not believe they have hair. Later on I also want to experiment with hair movement — pressing and dragging does push and pull hairs in certain directions. But I first want the base to be working."* So the setting is `BrushSettings.Bristles`, on the Tip option of the brush editor, for any brush; the unread `MediumSettings.BristleDrag` is left as it was.

## How it is built

- **Fills at** (`TextureFill`, absent unless used): with it, the tooth's threshold runs all the way down past the deepest valley, so a full press keeps the whole sheet; and how hard the paint is pressed is the pen's pressure times the paint left on the brush, over the fill pressure — a brush running dry skims however hard it is pressed.
- **A hair is a contact, not a shade.** The first version made each hair a level, and got a ribbon with faint stripes. Each hair now has its own share of the dab's pressure, and the sheet is gated hair by hair, so a hair that is barely down shows the canvas along its whole length. The hairs are the brush's — a weight per hair, the same on every stroke — laid across the tip at its full size, in the dab's own frame.
- Both are ceilings laid under the pen, like the pencil's tooth. Nothing is simulated and nothing changes at pen-up.

## What it costs, and what is not built

- **Strokes already drawn with the simulated Oil are untouched for now**; their settings name the simulation. They change when it is deleted (Q240).
- **The hairs need a sheet.** They work by pressing paint into a paper unevenly, so a brush with no paper texture has none. A tip that streaks on a perfectly smooth ground is not built.
- **One number.** How many hairs a tip has, how wide, which are thin, and how they move when the brush is pressed and dragged are what the owner means the tip system to grow into; none of it exists yet.
- **Was still rendered again at pen-up** when this was answered, like Watercolor, because it mixes colour with what is under it. Closed the same day (`PenUpChangesNothingTests.AMixingStrokeAcrossPaintIsKept`).
