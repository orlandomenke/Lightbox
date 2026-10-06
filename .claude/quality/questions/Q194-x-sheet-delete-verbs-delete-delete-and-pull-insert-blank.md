# Q194 · X-sheet delete verbs: delete, delete and pull, insert blank frame — **answered 2026-10-06**

**Answered:** the X-sheet gets **two deletes over any selection** — *Delete*
(the drawings become holds, the slots stay) and *Delete and pull* (the cels go
and the rest of the row moves back) — **Delete column is removed as a verb** and
becomes what Delete and pull does when the selection covers every layer for its
frames, and a new **Insert blank frame** puts a hold *at* the cel. Delete is on
the Delete key and Delete and pull on Shift+Delete, over the X-sheet; Insert
blank frame is registered and unbound. Every recommendation was taken.

Raised by the owner: *"X-sheet: Delete; remove delete column and extend delete
so that it applies to a selection of frames and cells. We can just delete or
delete and pull. Delete and Pull means we remove the column and pull all next
frames one back. Add an insert blank frame; which is always a hold on a
previous frame."*

## The three decisions, and what each costs

1. **Two verbs, renamed for what they leave behind.** *Delete* is the old
   *Clear cel* (`ClearCelsAcross`), *Delete and pull* the old *Delete cel*
   (`DeleteCelsAcross`), both now over the whole multi-layer selection and each
   one undo step — the old per-layer loop made a three-layer delete three steps
   to undo. Cost: the word *Delete* now means "keep the slot", which is the
   opposite of what it meant on this menu last week; an artist who learned
   *Delete cel* finds that edit under *Delete and pull*. The alternative —
   keeping *Clear* / *Delete* — was not taken because the owner named the pair.

2. **A column is a shape, not a verb.** When the selection is the same frames on
   every drawing layer (`DocumentEditor.ColumnsOf`), Delete and pull removes
   those frames from the scene (`DeleteColumns`): every layer loses the cel,
   references ripple back, the scene shortens, one undo step, never below one
   frame, refused while any drawing layer is locked. This retires Q88's *Delete
   column* item and delivers the selection-driven multi-column delete Q88 left
   unbuilt. Cost: **a document with one drawing layer makes every selection a
   column**, so there Delete and pull always shortens the scene where it used to
   pad the row with a hold — `TimelineBugTests.DeleteCel_RipplesTheRest` now
   adds a second layer to keep testing the row pull. And a column has to be
   selected cel by cel on each row; there is no click-the-ruler gesture for it
   yet.

3. **Insert blank frame is a hold at the cel.** The new cel shows the drawing
   before it and the cel and the rest of its row move right; a row that runs
   past the end grows the scene, as Extend exposure does. A column selection
   makes it a column insert (`InsertHoldColumns`: every layer, references
   rippled forward, as `AddFrameAfter` does). Cost: one more verb on an already
   long cel menu; at frame 0 the inserted hold shows nothing, because there is
   no drawing before it.

## Implementation choices made inside the answer

These were not put to the owner separately — each follows from the answer and
is written down so it can be reopened if it reads wrong in use.

- **The paper is carried, not counted.** "Every layer" means every drawing
  layer; the background is locked from birth and nobody selects it, so
  requiring it would make a column selection impossible in practice. Its cels in
  a selection add nothing. A column delete or insert keeps the paper showing on
  every frame that is left — the paper's drawing moves onto the following hold,
  and an insert goes in after the paper's drawing rather than in front of it.
  The old `DeleteFrame` did not do this: the 🗑 at frame 0 removed the paper
  from the whole scene. `DeleteFrame` now routes through `DeleteColumns`.
- **A run of n selected cels inserts n holds at its start** ("insert n blank
  frames"), not one in front of each cel, which would interleave holds through
  the drawings — re-timing, a different command.
- **`timeline.deleteColumn` is retired, not re-pointed.** shortcuts.json is keyed
  by id, so this drops anyone's rebind of it; re-pointing it at Delete and pull
  would have put their key on a different edit (a row pull unless the selection
  is a column) without telling them. It shipped with no default.
- **The keys are scoped to the X-sheet docker, not the Timeline docker.** The
  cel grid lives in `DockPanelId.Xsheet`; a binding on `.Timeline` would never
  have answered over the cels. Over the Timeline docker's track view Delete
  still falls through to the canvas, as before (Q82).
- **The keys and the Animation menu take the selection wherever the playhead
  is**, and the playhead's cel only when nothing is selected — Ctrl+click picks
  cels without moving the playhead, so anything else ignores a visible
  selection.
- **The timeline bar's 🗑 stays a column verb** beside ➕ and ⧉, and now refuses
  on a locked layer and keeps the paper, through the same path.

**Blocks:** nothing.
