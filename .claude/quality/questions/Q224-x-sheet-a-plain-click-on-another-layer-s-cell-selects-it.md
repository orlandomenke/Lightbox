# Q224 · X-sheet: a plain click on another layer's cell selects it without changing the layer you draw on — **answered 2026-10-08**

Raised by the owner, 2026-10-08: *"In the xsheet if i select a cell of another
layer I do not want to change to that layer. Maybe CTRL + click but default
click just moves on the selection to that cell. On the same layer as selected."*

What a click did when it was asked: went to the cell's frame, cleared the
selection, **and made the cell's layer the active layer**. Ctrl+click toggled a
cell in the selection, Shift+click ranged, a plain drag selected a block (Q207),
Alt+drag carried a drawing along its row.

Three things were the owner's to decide, asked with `AskUserQuestion`.

### 1. What a plain click in another layer's row does — **select that cell, keep drawing where you were**

**Against the recommendation.** Recommended was the other reading of "on the
same layer as selected": only the frame changes and every row is a place to
scrub, so the picked cell and the drawing layer can never differ. The owner
chose: *the clicked cell becomes the selected cell for timeline verbs, the
playhead goes to its frame, and the layer you paint on stays.*

The cost was stated with the option and is real: the cell that is picked and
the layer being drawn on can now be different layers, which was never possible
before. It is paid in the build rather than left for the artist to find — see
below.

### 2. Which gesture switches to the clicked cell's layer — **a double click**

Recommended and taken. The owner's own suggestion was Ctrl+click, which already
adds a cell to the selection; moving that would have changed a habit shared with
every spreadsheet. A double click on a cell did nothing before, so nothing is
displaced. Clicking a layer's name and picking it in the Layers docker switch
layer as they always did.

### 3. Whether a plain drag can still start on another layer's row — **yes, unchanged**

Recommended and taken.

### Decided in the build, because the answer to 1 requires them

- **Every timeline verb acts on the picked cell.** Delete, Delete and pull,
  Insert empty cell and Insert blank keyframe already took the selection (Q196).
  Copy, cut and paste, extend and reduce exposure, the timing presets and
  marking a keyframe, breakdown or inbetween aimed at the active layer's cel at
  the playhead; left alone, Ctrl+C after clicking another layer's cell would
  have copied a different cell from the one highlighted. They now ask the same
  question the first four do.
- **No verb changes the drawing layer.** Paste, insert keyframe and insert empty
  cell used to take the cel's layer as a side effect. From the keyboard and the
  Animation menu they no longer do. **From a cel's own right-click menu they
  still do**, deliberately left: that menu is aimed at one particular cel and
  has always meant "here".
- **A click in your own layer's row is exactly what it was** — playhead moves,
  selection cleared — because there the picked cell and the playhead's cel are
  the same cel and there is nothing to keep apart.
- **Drawing and AI inbetweening stay with the layer**, not with the pick.

### Left open

- A cel's right-click menu still switches layer for Insert and Paste. Whether
  that should follow the same rule is a small question of its own.
- The Timeline tab's dots are unchanged; this is the X-sheet's click.
