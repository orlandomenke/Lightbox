# Q261 · How does an artist direct and revise the AI inbetweener? — **answered 2026-10-13**

**Answered: the button stays one click; a note is a second way in; Revise redraws the AI frames between the keys — or only the selected ones — with the note; the note is kept on the frames; the verifier still decides.** Three as recommended; the second widened by the owner to include a selection.

Raised by the owner, 2026-10-13, once the Claude Code provider (Q259) meant the AI buttons could be used without leaving Lightbox: *"we should be able to send directions or changes to the last run to claude from Lightbox. This should be a text field … the inbetween button should already resolve with a pop up with an optional text field."*

What it blocks: steering a model whose first answer is plausible but wrong, without redrawing the keys or leaving the application.

## What was found first

The repair loop already does the hard half. A refused frame is handed back to the model with a sentence saying what is wrong, and redrawn as an edit rather than a reroll. A note from the artist is the same mechanism with the artist writing the sentence — so revision reuses the request shape, the verifier and the loop, and adds two fields (`Direction`, and `Earlier`/`Fixed` for a revision).

It also stays inside rule 0 of the AI roadmap: a note always rides on keys the artist drew, so nothing here makes a drawing from words.

## What was asked, and what was answered

**1. How is the note reached?** *One click stays one click* (recommended): an arrow beside the button, Shift+click, and an unbound shortcut open the popup. Against: every press opens the popup — discoverable, but a keystroke on each of hundreds of presses; or a note only on revision.

**2. What does "change the last run" act on?** Recommended: *the AI frames between the keys around the playhead*, redrawn in place as one undo step, surviving a reopen. **Answered: that, or optionally the frames selected on the timeline** — the selection narrows it, and the unselected AI frames go along as fixed context. Against: only the most recent run this session (gone after a restart).

**3. Is the note saved?** *Yes, in the AI marking* (recommended) — `AiProvenance.Note`, written only when there was one. Against: send and forget.

**4. If a note asks for something outside the keys, does the verifier still refuse it?** *Still refuse, say why* (recommended). An overshoot is a breakdown the artist draws. Against: a note relaxes betweenness to a warning, which would let frames in that nothing in the record justifies.

## What it costs

- **A revision re-sends the frames being revised** and, when some are kept, those too — roughly one more frame's strokes per frame in the request, which is the expensive kind of payload (`DESIGN-ai-payload.md`).
- **The bounds are the nearest drawings the artist made**, not the nearest keys by role — an AI frame can never be an end, and a breakdown the artist drew is something a revision has to sit between.
- **A revision's t is the frame's place between its bounds**, or the key's timing chart when it has one rung per slot. Frames drawn under an eased curve with no chart are revised against the linear position, so a revise can drift their spacing slightly; the verifier still judges the result.
- **The AI bar moved to `AiBar.axaml`** to carry the menu — `MainWindow.axaml` is the top of the risk table and may not grow.
