# Q254 · Replacing the Plain paper scan to remove its streaked strip — **answered 2026-10-12**

**Answered: replace Plain under its own name, as recommended.**

Raised by: B459. A 300 px Ink wash drawn across the canvas showed a pale band of vertical streaks every 1024 px. The Plain scan (ambientCG Paper 001) has a strip some 140 px wide at its right edge where the grain is smeared - where its maker blended the tile shut. Pencil, Gouache and Ink wash all sit on Plain.

What it blocks: the fix. `ScannedPaperTests.TheSurfaceIsTheOneThatShipped` pins each scan and says why: a stroke on one of these papers has to replay to the same pixels in every later build, so "a replaced scan needs a new kind, not a new file under an old name". Cleaning the tile changes its size (1024x600 to 816x552) and so where its grain falls.

## What was asked

How the cleaned tile should go in.

- **Replace Plain (recommended, chosen).** Every brush on Plain loses the band at once. Cost: anything drawn on Plain since it merged on 2026-10-11 re-renders with the same paper and its grain in different places.
- **Add a new paper kind.** Existing strokes stay pixel-identical. Cost: a paper in the list that exists only to preserve a defect, and brushes saved on Plain keep the band.
- **Leave it.** The band stays.

## Why the rule bends here and not in general

The paper had been on main for one day when this was asked, and the fault is in the sheet itself and not a matter of taste. The pin stays, re-recorded, and the rule it states stands for every later change.

## Not decided here

Kraft (Paper 002) has the same fault at its left and top edges, measured the same way (0.74 against Plain's 0.75 before cleaning). No shipped brush uses it. It was not part of this question and is not changed; `NoStripOfAScanIsSmearedIntoStreaks` leaves it out and says so.
