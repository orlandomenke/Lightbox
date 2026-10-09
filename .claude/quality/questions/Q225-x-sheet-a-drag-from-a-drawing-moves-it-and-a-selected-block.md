# Q225 · X-sheet: a drag from a drawing moves it, and a selected block moves as one — **answered 2026-10-09**

**Answered:** **a drag that starts on a drawn cel moves it, with no modifier**;
a drag that starts on a cel that is part of a selection moves **the whole
selection** by the same number of frames, each drawing along its own row; and a
drag that starts on an **empty** cel still sweeps a block. Shift+click and
Ctrl+click are unchanged. Both were the recommended options. This revises Q207,
two days old, which had given every plain drag to block selection and put the
move under Alt.

Raised by the owner, 2026-10-09: *"Click and hold on x-sheet should enable drag
and drop for cell. So changing timing is easier. If this conflicts with
selection, remove selection. Should also work on columns."*

Two things were the owner's to decide, asked with `AskUserQuestion`.

## Which press becomes the move

- **Drawn cel moves** (chosen, recommended). The cel under the press decides.
- *Drag always moves* — drag-select removed, blocks built by Shift+click and
  Ctrl+click only. The simplest rule, and it throws away the half of Q207 that
  does not conflict with anything.
- *Hold still, then drag* — a timer. Keeps both gestures on every cel, but a
  held press is how a pen right-clicks on Windows (B8), so the timer would have
  fought the context menu, and waiting is the opposite of "easier".

## What "columns" meant

The sheet runs frames left to right with one row per layer, so the request had
four readings. The owner picked one:

- **A selected block moves as one** (chosen, recommended) — including a whole
  frame column selected down several layers.
- *A whole frame on every layer from the ruler*, *dropping a cel on another
  layer's row*, and *reordering layer rows in the sheet* were offered and not
  taken. None is ruled out; none was asked for.

## What it costs

- **A block cannot be started by dragging from a drawing.** It is made with
  Shift+click, or swept from an empty cel. The manual says so.
- **Alt+drag still moves**, so nothing learned since Q207 stops working.

## Implementation choices inside the answer

- The decision is `CelPressRouting`, a pure function of the cel and Alt, so it
  can be tested without synthetic pointer input.
- A block move is `RetimeSelection` — the Timeline tab's drag — so it is one
  undo step, ordered by direction, and refused whole if any cel would leave the
  front of the sheet. A camera or bone key in the same selection moves with it,
  as it does there.
- **Ctrl copies the block** as it copies a single cel; the copy takes cels only.
- A drawing outside the selection moves alone and leaves the selection alone.
- A drop still has to land on the grabbed cel's own row.
