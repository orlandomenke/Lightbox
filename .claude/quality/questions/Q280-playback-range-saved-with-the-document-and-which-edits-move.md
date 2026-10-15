# Q280 · Playback range: saved with the document, and which edits move it — **answered 2026-10-15**

Raised 2026-10-15 by the owner: "On save the set start and end times are saved
as well. Reopening the application loads this as well," and "When a start and
end time is set and we extend exposure on a frame, I want the end time to move
with it." Asked with the prompt.

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **Whether a range change is an undo step** (raised by the sensitivity review: a new step empties the redo stack) | **Outside the history** (recommended): setting, dragging and clearing never push a step and never clear redo, and undo and redo keep the range you have. An edit that moved the range takes it back when undone. | An undo step that clears redo: setting a range after undoing strokes to compare throws those strokes away. An undo step that keeps redo: needs a special step kind in the undo machinery, which is the most work and the most risk. |
| **Which edits move the range** | **Every edit that shifts a row or a column** (recommended): Extend and Reduce exposure, Insert empty cell, Delete and pull, adding, duplicating and deleting frames, re-timing, and inserted inbetweens. An insert at or before the end moves the end, and one at or before the start moves both. | Extend exposure only: exactly what was asked, but Insert empty cell inside the range would push the last drawing out of it. The range would then frame different drawings depending on which verb moved them. |

**Recommendation:** as marked, and the owner took both.

## What follows from it, not up for preference

- **The range is in the document** (`Scene.PlaybackStart` / `PlaybackEnd`,
  null unless set, so a document without one writes no key), and it is not a
  preference. It names frames of this sequence and means nothing in any other.
- **How undo keeps it.** Undo puts back a whole snapshot of the document,
  range included, so the snapshot's copy is not what comes back. Each
  snapshot step records the range just before and just after its own edit. On
  undo the live range is replaced by the "before" value only when it still
  equals the "after" value, which means the step moved it. Redo works the same
  way in the other direction. A range set directly is written to the scene
  and marks the file dirty through the camera's route
  (`MarkEditedOutsideTheRecord`), with no recompose and no snapshot.
- **An insert lengthens the frame before it.** An insert at `at` grows
  whatever is exposed at `at − 1`, so holding the last drawing in the range a
  frame longer moves the end. That is the owner's case. An insert directly
  after the range counts as lengthening its last drawing too.
- **A row edit moves the scene's one range.** A change on one layer shifts it
  for everyone, because leaving it in place would push out of the range the
  drawing it was set to end on. The same insert on three rows shifts it once,
  not three times, and runs of different lengths at one place shift it by the
  longest, which is how much the scene grew there.
- **Opening another document shows that document's range**, or none. The old
  behaviour cleared the range on every switch, because it lived only in the
  view model.
- **Insert frame (Q279) ripples it too** once both branches are on main.
  Whichever of the two merges second adds the `ShiftPlaybackRange` call to
  `DocumentEditor.InsertKeys`.
