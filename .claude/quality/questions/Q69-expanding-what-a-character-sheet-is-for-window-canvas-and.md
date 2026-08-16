# Q69 · Expanding what a character sheet is for: window, canvas, and how live — **answered 2026-08-12**

The owner asked for two things while another thread moves sheet storage: a
sheet view you can look at *beside* the art, and a sheet view you can see
*under* the art. Four decisions, prompted and answered together.

### The answers

1. **Build on `main`, small surface.** The storage-moving branch could not be
   found on the remote, so both features read views through the existing model
   and touch storage not at all — whatever lands under them merges cleanly.
2. **The floating window is a read-only live viewer first**, shaped so an
   editable canvas can replace its content pane later; the editable step is
   roadmap material, not this build. Full editing in a second window means
   input routing and split brush state — a design doc, not a feature branch.
3. **On the canvas, a sheet view is a `ReferenceStrip`, not a layer.** The
   strip already renders over paper and under drawings, carries opacity, scale
   and offset, and holds the promise that a reference never reaches an exported
   pixel. A "temporary layer" would re-answer export, AI payload, undo and the
   layer docker — four hard questions for the same picture. One addition was
   needed: `Pinned`, because a strip is otherwise only visible on frames with
   assigned slots, and a taped-up sheet must show on all of them.
4. **The taped copy is live, not a snapshot** — *against the recommendation*,
   and recorded with its cost. Editing the sheet re-flattens the strip on the
   edit funnel: one PNG encode at the view's authored size per edit, per taped
   view, paid while a sheet is on canvas. The recommendation was a snapshot
   plus a refresh button — cheaper, more predictable, and rejected because a
   reference that shows yesterday's drawing is worse than one that costs a few
   milliseconds on commit. The string compare in
   `RefreshLinkedReferenceStrips` keeps no-op edits from re-registering
   identical bytes, and the refresh is deliberately not an undo step: the
   drawing's own undo re-runs the funnel and the copy follows.

### What was deliberately not decided

Whether the strip's `Pinned` flag should grow into per-strip slot policies
(hold ranges, per-scene pins) — nothing asked for it, and the flag is one
boolean that a richer policy could replace without a migration.
