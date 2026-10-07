# Q212 · Delete and pull on trailing empty cells: what it removes — **answered 2026-10-07**

**Answered: Delete and pull on empty cells at the end of a row trims the scene
to end at the last drawing on any layer.** The owner chose this over the
recommendation, which was narrower: shorten the scene only where *every* row
is empty at the picked frames, and otherwise say which layer still has drawings
there. The third option — keep the behaviour and explain it in the status line —
was not taken.

Raised by the owner, 2026-10-07: *"Delete and pull does not delete an empty
cell. Something I requested multiple times already, was told it was fixed, but
the issue still persists."* They had tried it from the cel's menu, with
Shift+Delete over the X-sheet and on a selected block, on cells at the end of
the scene and on holds after a drawing, and *"nothing moves"*.

## What was found before asking

A headless probe of `DeleteCelAt` (the view model's Delete and pull), on a row
`A · · B · · _ _`:

- **One drawing layer:** every selection is a whole column, the scene gets
  shorter, and empty cells go — this is the case earlier fixes were tested on.
- **More than one layer, a hold between drawings:** works; B moves back a frame.
- **More than one layer, empty cells after the row's last drawing:** nothing
  changes. The row is pulled back and padded with empties at its end (Q206's
  model: every row runs to the end of the scene), so removing an empty and
  padding one back is the same sheet. The command did nothing and said nothing.

So "it was fixed" and "it still does nothing" were both true: the fix and its
tests covered the single-layer and between-drawings cases, and the owner's
document is the multi-layer, trailing case.

The between-drawings case that the owner also saw fail works headless; the
performance lab (Q209) is being extended to reproduce the owner's own gestures
against the installed build, to find where the real UI loses it. That is a
separate entry.

## What it costs

- **It can cut frames kept on purpose.** A hold at the end of a shot, after
  every layer's last drawing, goes the moment Delete and pull lands on any
  trailing empty cell — the owner accepted that in choosing this option.
- **Other layers' trailing empties go with it**, for the same reason: the scene
  ends at the last drawing, for every row at once (Q206: every row runs to the
  end of the scene).

## Implementation choices inside the answer

- **"Trailing"** means an empty cel with no drawing after it on its own row.
- **Where the scene ends:** one frame after the last keyed drawing on any layer,
  the paper excepted (it is held for the whole scene by design), and never
  shorter than one frame.
- **Only when a trailing empty was picked.** A Delete and pull that picked no
  trailing empty behaves exactly as before; one that picked both pulls the rows
  as before and then trims.
- **One undo step** for the whole edit, as every X-sheet verb is (Q196).
- **If trimming removes nothing** — another layer's last drawing is at the very
  end — the status line says so and names the layer, rather than doing nothing
  silently again.
