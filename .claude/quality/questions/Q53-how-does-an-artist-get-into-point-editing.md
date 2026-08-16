# Q53 · How does an artist get into point editing? — **answered: Illustrator's model in full**

**Answered 2026-08-07: two pointers *and* isolation mode.** A black-arrow
**Select** tool for whole strokes, a white-arrow **Direct select** for nodes, a
**Pen** with modifiers — and double-clicking a stroke isolates it, Esc leaves.

**The property being bought is that geometry editing is a decision, not an
accident**, and the research is one-sided about how you get it. Illustrator's
isolation mode *"automatically locks all other objects so that only the objects
in isolation mode are affected"*; Figma enters vector edit on Enter and leaves on
Esc; Grease Pencil separates Draw, Edit and Sculpt. The tools that feel mushy use
a modifier you have to remember instead — Krita's own vector-tool wiki says
*"Alt+drag allows you to start a rubber band without accidentally selecting and
moving a shape"*, and Inkscape's node tool requires that *"the drag must not
begin on a path unless Shift is used"*. **Modes are safe by default; modifiers
are unsafe by default and ask you to remember the antidote.**

The recommendation was isolation alone; the owner's answer added the two
pointers, on the grounds that Illustrator has both and the black/white
distinction is what makes the split legible at a glance. Illustrator's actual
convention is used — **black selects objects, white edits anchors** — rather than
the reversed pairing in the original note.

**What it costs.** Three tools rather than one mode, so three walks of the tool
registration checklist, and a `Select` that overlaps conceptually with the
existing pixel selection tools. Answered by Q48: they look different and do
visibly different things.

**Blocks:** nothing. `PathEditSession` is a second instance of the transform
tool's modal-session pattern.
