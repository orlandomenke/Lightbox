# Q207 · X-sheet block selection: Shift+click across rows, drag selects, Alt+drag moves — **answered 2026-10-07**

**Answered:** **Shift+click ranges across rows as well as frames** — the block
from the anchor cel to the clicked one, as a spreadsheet does — and **a plain
drag selects the block it sweeps**, with **Alt+drag** now the gesture that moves
a cel along its row (Ctrl still makes the move a copy). A plain click is
unchanged: it moves the playhead and clears the selection. The owner chose the
recommended Shift+click *and* the drag swap ("1 + 3"), over the middle option
of keeping the plain drag as a move and adding a modifier drag to select.

Raised by the owner, 2026-10-07: *"I want to be able to multi select cells and
perform delete operations on it. This will later be also used for animation
aware tools."* Checked against the sheet as it was: Ctrl+click already picked
single cels on any row and every delete already covered the selection, but
Shift+click ranged along one row and dropped its anchor on any other, and a
plain drag picked a drawing up — so a block over several layers could only be
built a click at a time.

## What it costs

- **Muscle memory.** A plain drag on a cel used to carry it; now it selects,
  and carrying needs Alt. The manual says so where it used to describe the drag.
- **Alt on Windows.** Releasing Alt alone can hand focus to the menu bar. A
  pointer press between the Alt down and up is expected to cancel that, but it
  could not be verified headless and is the first thing to check in the app.

## Implementation choices inside the answer

- **Rows in screen order**, not scene stacking order: the block is what the eye
  sweeps (`LayerRows`).
- **The block stops at the end of the scene.** The hatch holds no cels (Q103),
  so a drag into it selects up to its edge rather than nothing.
- **The press cel is the anchor**, so a Shift+click after a drag resizes the
  same block.
- **The drag releases the pressed cel's pointer capture** when it starts, so
  moves report the cel under the pointer and the release is not a click on the
  first cel (which would clear the block just made).
- The gesture is its own state machine (`CelBlockSelectGesture`), armed only
  without Alt while `CelDragGesture` is armed only with it, so the two cannot
  both claim a press.
