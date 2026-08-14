# Q89 · Two kinds of empty cell look alike in the X-sheet — how are they told apart? — **answered 2026-08-14: hatch the past-the-end cells**

Raised by the owner from a screenshot: *"we now have 2 types of empty cells.
The red circled ones are after deleting cells. The green circled ones are
default. The deleted ones are scrubbable and auto create keyframes when
painting. The other are not scrubbable and unselectable. This is creating some
confusion, though also helpful as the playerhead never reaches the empty ones.
But still it is a bit confusing as both seem deleted."*

The two are real and the model already separates them: a cel inside the scene
with no drawing is a **hold or a blanked cel** — the playhead goes there and a
mark keys it — while a cell past `Scene.FrameCount` is **virtual**
(`FrameCell.IsVirtual`), refused by `SelectFrame`, by the cel drag and by the
range highlight. The only thing saying so was `Opacity 0.35`
(`MainWindow.axaml`, `Button.cel.virtualCell`), which is far too quiet to carry
a distinction that decides whether a click does anything.

**Answered: hatch the past-the-end cells** — keep the cell boxes and fill them
with a faint diagonal hatch or darker tone.

The recommendation was *the grid stops* — dropping the cell chrome entirely
past the end, so the sheet visibly ends where the scene does. The owner's
choice keeps the column grid legible far to the right, which is what you want
when judging how far to extend a scene, and that is a real gain the
recommendation gave up.

What it costs, recorded because the choice was made knowingly: a hatch is **new
visual vocabulary** — the sheet has no other hatched state, so it has to be
learned rather than read, where an absent grid needs no explanation. It also
has to stay legible against the current-frame highlight, the out-of-range dim
(`Button.cel.dim`) and the selection tint without becoming noise, which is a
tuning job the absent-grid option would not have had.
