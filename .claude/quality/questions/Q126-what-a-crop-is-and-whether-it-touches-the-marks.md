# Q126 · What a crop is, and whether it touches the marks outside it — **answered 2026-08-19: paper only; menu commands first, interactive tool second**

Asked before building the crop the `_Image` menu's own comment has anticipated
since it was written ("*a menu of its own … because the pair will grow (crop to
selection, trim)*"). Two forks, both load-bearing, both put to the owner with a
recommendation before a line was written.

## How far the crop goes

| | What it costs |
| --- | --- |
| **Menu commands only** (recommended, **chosen first**) | Crop to selection and Trim to drawing, on the Image menu. Reuses the marquee contours and `BrushEngine.ReachBounds`; one `Perform` each. No canvas overlay, no new `ToolId`. |
| Menu commands **and** an interactive tool (**chosen second**) | A rubber-band with eight handles, Enter to apply, Escape to cancel — roughly triple the work: a `ToolId`, a canvas overlay, hit-testing, a tool-options bar and its own suite. |
| Interactive tool only | Cheapest of the tool options and the one that answers the request least: the ask was for crop *in the main menu*. |

**What the first answer cost, and why it was revisited the same day.** With menu
commands alone there is no drag-a-rectangle crop, so a crop that is not already a
selection means drawing a marquee first — one extra gesture, and it is the
gesture the marquee exists for, but it makes the common "just take a bit off the
top" two steps rather than one.

**Revised the same day, after the difference between the two commands was put in
words**: *crop is you saying where the edge goes; trim is asking the drawing
where the edge already is.* Said that way, the menu answers trim completely and
answers crop only halfway — deliberate framing is a thing you do **by eye**, and
a dialog or a marquee is the wrong instrument for judging a frame. So the
interactive tool is in after all, on top of the commands rather than instead of
them.

**Nothing built for the first answer is wasted, which is the part worth
recording.** Both commands funnel through `CanvasResize.CropTo`, which takes a
rectangle and does not care who chose it — a marquee's bounds, the ink's extent,
or a rectangle dragged by hand. The tool is a third caller, not a rewrite. Had
the first answer put the crop arithmetic inside a tool, the menu commands would
have had to be dug back out of it.

**What the tool costs**, which the first answer priced correctly: a `ToolId`, a
`CanvasToolMode`, a canvas overlay with eight handles and their hit-testing, a
tool-options bar, and a suite for the drag math. It is roughly three times the
menu commands, and it is spent on the half of the feature that is about
judgement rather than arithmetic.

## Whether a crop deletes the marks it cuts off

| | What it costs |
| --- | --- |
| **Leave them in the record** (recommended, **chosen**) | Crop writes `Scene.Width`, `Height` and the origin, and nothing else — exactly what `Resize canvas` already does. |
| Delete the marks outside | Smaller files, and no ink reappearing when the canvas grows back. |

**Invariant 1 and `Hash01` settle this rather than taste.** Every dab dynamic is
seeded from the IEEE-754 bits of the dab's position, which is why
`CanvasResize` moves the origin instead of translating the drawing. A
destructive crop is not the mirror of that: deleting a stroke and drawing it
again later is a *different mark*, so crop-then-grow-back would return altered
art. Keeping the record whole makes cropping exactly as lossless as the resize
it is a special case of, and it keeps crop from becoming the only paper
operation that reaches into the stroke list.

**What that choice costs, and it is real.** Ink that has been cropped away is
still in the file, still costs bytes, and still comes back if the canvas is
later grown. An artist who crops in order to *discard* has not discarded
anything. The honest answer to that want is a separate destructive command
("Delete everything outside the canvas"), which nobody has asked for and which
would be filed rather than smuggled in under this word.
