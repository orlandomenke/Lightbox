# Q279 · Insert frame on a drawing: where the new drawing goes, and where re-marking went — **answered 2026-10-15**

Raised 2026-10-15 by the owner: "we cannot insert new keyframes between frames.
I tried the Insert Frame option from the right-click menu hovering a frame but
that does nothing. I would expect it to add a frame to the right of it by
default." Asked with the prompt.

Why it did nothing: on a cel that already had a drawing, *Insert frame* only
re-marked the drawing's role. A key marked as a key changes nothing anyone can
see.

| Decision | Chosen | What the alternatives cost |
| --- | --- | --- |
| **Where the new drawing goes** (A held for two, then B: `A A B B`) | **After the hold** (recommended): `A A new B B`, whichever A cel was clicked | Directly right of the clicked cel: `A new · B B`, so the hold that followed A now shows the new drawing and A is quietly put on ones. That is literal, but the insert re-times the drawing it was aimed at. |
| **What goes in it** | **A blank drawing** with the chosen role (recommended), the same thing Insert frame makes on an empty cel | A copy of the drawing: a different verb (duplicate), and it costs stroke copies. |
| **Where re-marking a role goes** | **A new *Mark as* submenu** (recommended): Key / Breakdown / Inbetween, in the cel menu and the Animation menu. It re-marks drawn cels in place and moves nothing. | Insert re-marks when several cels are selected: no new menu item, but one item would do two different things depending on the selection. |

**Recommendation:** as marked, and the owner took all three.

## What follows from it, not up for preference

- **On an empty cel Insert still keys in place**, including past the end of the
  scene (Q103). That is how a drawing gets started on an empty cel, and it did
  not change.
- **On a layer, picked drawings win over picked holds.** A Shift+click run over
  `A A B B` picks two drawings and two holds. It gets one new drawing after each
  picked drawing, and the holds stay holds. Keying the holds as well would put
  four new drawings through the run, interleaved. Found by the adversary review.
- **The whole selection is one undo step** (`DocumentEditor.InsertKeys`), and
  **I** and the Animation menu go through the same lock check as the cel menu.
  Before this, both took a shortcut to the editor that was harmless while the
  verb only re-marked, and is not harmless once it shifts a row.
- **The playhead moves to the new drawing** when the verb is aimed at the
  playhead's cel or comes from the cel menu. A cel picked elsewhere keeps the
  playhead where it is (Q224).
