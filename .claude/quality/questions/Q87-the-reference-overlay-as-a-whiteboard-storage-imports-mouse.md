# Q87 · The reference overlay as a whiteboard: storage, imports, mouse and the old window — **answered 2026-08-14, all four as recommended**

Prompted before building the roadmap's reference overlay as a PureRef-style
board: one window holding every reference sheet in scope, flattened to one
picture each, auto-fitted, rearrangeable by hand, and able to hold imported and
dragged-in pictures beside them.

1. **The arrangement is a project sidecar, filed per scope.** A board hangs on
   the folder a sheet made from that document would be filed on — the top of the
   subtree — so every animation of the knight opens the knight's wall rather than
   rebuilding its own. Path derived from the folder id, so there is no manifest
   entry and no migration; a scope with no board has no file. A document with no
   project falls back to a nullable `Doc.ReferenceBoard` with its pictures
   embedded, which is the only case where a board carries pixels. The two
   alternatives and what they cost: **per document** is one record instead of two
   and makes the wall per-animation, which is the friction this feature exists to
   remove; **workspace state** touches no document at all and loses the
   arrangement on a workspace reset, which is that same friction with a longer
   fuse.
2. **Imported pictures are copied into the project**, into `references/`, and the
   tile points at the copy. Embedding them like `ReferenceStrip` does would be
   consistent and would put tens of megabytes of base64 into a file that is
   otherwise strokes; linking absolute paths would be free and would break
   silently the first time a downloads folder was emptied — and a picture dragged
   off a web page has no durable path to link in the first place. The original
   path is kept as provenance only.
3. **Picking a picture up raises it; the menu sends it back.** Left-press brings
   the tile to the front and starts the move, a corner drag resizes, the wheel
   zooms and a middle-drag pans; *Bring to front* / *Send to back* / *Take off
   the board* are on the right-click menu and in `ShortcutMap` under a new
   **Reference** category. Select-without-raising was the safer option and made
   the commonest action on a board cost two gestures. The literal reading of the
   request — LMB raises, RMB lowers — was declined because it spends the right
   button, and the board would then have no context menu for the operations that
   have nowhere else to go.
4. **The board supersedes Q69's single-view window**, which is deleted rather
   than kept beside it. Q69 was right about *live* and wrong about *one*: an
   artist works from several references at once, so one window per view meant
   several windows and no way to arrange them against each other. Its four
   promises — shows the picture, follows edits, does not churn on unrelated ones,
   stops listening when closed — are carried over as the first four tests of
   `ReferenceBoardWindowTests`, which is what makes this a replacement rather than
   a rewrite. Keeping both was the low-risk option and would have left two
   surfaces claiming to show a reference view, which is exactly how B133 started.

One thing that was not a preference, whichever way the four went: **a view tile
holds no pixels.** It names the view and renders through
`RenderReferenceViewPng`, the same definition of "the view as a picture" the AI
payloads and the taped-on-canvas strip use. A board that stored flattened copies
would be a second, stale copy of every sheet in the project, and nothing would
say when it had gone out of date.
