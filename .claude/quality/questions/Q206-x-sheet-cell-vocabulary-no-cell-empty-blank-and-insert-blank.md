# Q206 · X-sheet cell vocabulary: no cell, empty, blank, and Insert blank keyframe — **answered 2026-10-07**

**Answered:** the sheet speaks the owner's vocabulary, which needed no change to
the record:

- a frame on a row **has a cel or does not**; no cel is the **hatch**, and that
  only exists past the end of the scene — every row runs to the scene's end,
  padded with empties;
- a cel is **empty** — a hold, showing the drawing before it, as Krita does —
  or carries a drawing, and a drawing with nothing on it is a **blank**
  keyframe, the white cel;
- a drawing has a **type** (keyframe, breakdown, inbetween); an empty cel has
  none of its own.

From that: **Insert blank keyframe** is a new verb on **I** over the X-sheet,
Q196's *Insert blank frame* is relabelled **Insert empty cell** (its shortcut id
is kept so a rebind survives), and *Delete* already meant "the cel becomes
empty". **Delete and pull is unchanged**: a pulled row is padded with empties,
and hatching appears when the scene itself gets shorter — a column selection, or
the last column.

Raised by the owner, 2026-10-07, after Q196/Q197: *"hatched (green) cells are in
my terms a deleted cell … Empty (red) cells are in my terms cleared cells (it
becomes a hold) … Blank keyframe is a new keyframe with no content whatsoever …
If I paint over an empty/cleared/hold cell it will become a blank keyframe"*,
and *"pressing I in the x-sheet does not insert a new blank keyframe"*.

## How it was asked, including the turn that went wrong

1. Prompted: what should Delete and pull leave at the end of a partially pulled
   row? The recommendation was **hatched cells**, and the owner took it.
2. The recommendation had been made without checking the record: hatching means
   past the end of the *scene*, for every row at once, and a row that stops
   short of the scene holds its last drawing (`ExposedFrame` clamps). Per-row
   hatching would need a third kind of cel. It was re-asked with that cost —
   first stated as roadmap-sized, then corrected after measuring: every reader
   goes through `ExposedFrame` (72 calls, 33 files), so it is a moderate feature,
   not a large one.
3. The owner then gave the vocabulary above directly: *"all rows end at the
   hatched frame. It is filled with empties"* and *"Delete and pull is correct.
   Or by deleting the last column."* So there is no per-row absence to build,
   and the first answer is superseded by the owner's own model rather than
   reversed.

Recorded because step 1 is the failure CLAUDE.md warns about — a recommendation
that sounded settled and was a guess about the record. It was caught before
anything was built.

## Implementation choices inside the answer

Not put to the owner separately; each can be reopened.

- **A drawn cel is skipped, never emptied.** Replacing its drawing with nothing
  is Delete followed by this verb; doing both silently would make I the one key
  on the sheet that throws work away. The status line says to Delete first.
- **In place.** Nothing on the row moves; Insert empty cell is the verb that
  opens a gap.
- **Past the end of the scene it grows the scene**, as drawing there does (Q103)
  and as the Timeline docker's I already did (`SetKeyAt`).
- **I, scoped to the X-sheet**, beside the Timeline docker's I (insert a key);
  the eyedropper keeps I everywhere else.

## The report that started it was a stale build

*"Q197 promised me that drawing on a blank frame would create an empty keyframe.
That still does not work"*: every reproduction passed, headless and through the
canvas. The app the owner was running
(`AIAnimationFramework\Builds\Lightbox\Lightbox.App.exe`) was built at 07:53 and
Q197 merged at 11:41 the same day; the exe's strings carry the old setting names
and not `StartABlankDrawing`. No code change was needed for it.
