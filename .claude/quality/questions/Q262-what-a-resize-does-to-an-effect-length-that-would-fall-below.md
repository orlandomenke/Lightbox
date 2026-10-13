# Q262 · What a resize does to an effect length that would fall below the least its effect works at — **answered 2026-10-13**

Raised 2026-10-13 by the sensitivity review of B466 (an image resize scaling
the effect parameters that are lengths), and asked with the prompt.

Shrinking an image scales a blur's radius down with everything else. Two
effects — find edges and sharpen — declare a minimum radius of two, because
the blur beneath them does nothing below it. Halve a document with find edges
at a radius of two and it becomes one: the layer renders as a flat white
shape, every line gone, at 100% zoom, and comes back when zoomed in past
about 160%. One undo restores it.

What it blocks: B466.

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **A length a resize would take below its effect's minimum** | **Stops at the minimum** (recommended) | **Keep the exact value**: proportional on paper, and find edges goes blank and sharpen stops sharpening until the artist notices and raises the radius by hand. **Fix it in the renderer**: the stored value stays exact and the renderer never blurs below its working threshold, which also fixes small values from any other source and the dependence on zoom — at the cost of a change on the performance-critical render path, with its own measurement and review, and the blank picture remaining until it lands. |

**Recommendation:** as marked, and the owner took it.

## What follows from it, not up for preference

- **Only the minimum, never the maximum.** A slider's end limits the slider;
  the renderer takes any size, and a blur of 80 on a doubled image is 160. The
  effects window's row widens its range to show and keep such a value rather
  than showing 100 and writing it back.
- **Only when the resize is what would cross it.** A value a file already had
  below the minimum is scaled like any other, not quietly raised.
- **After a shrink a stopped effect is slightly stronger against the smaller
  drawing** than exact proportion would make it. That is the cost the choice
  accepts, and it is the cost of the effect still working.
