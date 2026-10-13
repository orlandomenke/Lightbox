# Q257 · Image resize: camera zoom, an effect's default outline pen, and paper grain — **answered 2026-10-13**

Raised 2026-10-13 from the sensitivity review of B462, which read every field
the image resize scales or leaves against the code that consumes it. Three of
the fields it found left behind are not omissions with one right answer: each
is a choice about what "resize the image" should mean for something that is
not a stroke. Asked with the prompt, with a recommendation each.

What it blocks: the branch after B463 that finishes the resize's leftovers.

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **Camera zoom** — a key's position scales, its zoom and the output size do not, so after doubling the image every shot frames half the width of the drawing it did | **Leave it as it is** — *against the recommendation* (adjust the zoom by the resize factor) | The recommendation kept every shot framing the same part of the drawing at the same output size, at the cost of a zoom of 1 no longer meaning one image pixel per output pixel after a resize. Growing the output size instead keeps zoom's meaning and changes the deliverable's resolution. **What the choice costs:** the shots of a resized document frame a different part of the drawing and are re-keyed by hand; the manual says so. |
| **An effect with no outline pen of its own** — it draws with the default brush, six pixels at any size of document, so after a doubling its outlines on the paper are twelve and a re-bake draws six | **Give it a pen at resize** (recommended) | Leaving it, and saying so in the manual, writes no new key and keeps the surprise. The choice writes an outline-brush key the element did not have, because after a resize the default is no longer what it draws with. |
| **A simulated medium's paper grain** (`Medium.PaperScale`) — document pixels, read every time the stroke renders, not scaled | **Scale it with the mark** (recommended) | Leaving it keeps the rule that a rescale may change the mark: grain stays a fixed number of pixels, finer on an enlarged painting and coarser on a reduced one. |

## What follows from them, not up for preference

- **The grain is scaled only on a medium that is in use.** Scaling the grain of
  an untouched medium would make it "used", and an untouched medium is what
  keeps twenty-one keys off every stroke (`AStrokeWithNoMediumIsNotGivenOneByAResize`).
- **The pen an effect is given is the default brush, exactly, at the new
  size** — so the bake draws with what it drew with before, and a document
  that is never resized never gains the key (`AnEffectThatIsNeverResizedKeepsNoPen`).
- **The camera decision is written where it will be read**: in the resize's
  own code, in the omission guard's list (zoom is "not scaled, by decision",
  not "holds no size"), in the test that pins it, and in the manual.
